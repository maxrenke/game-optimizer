# I Built a Windows Game Optimizer With AI - Here's What I Actually Learned

Most "game optimizer" software is one of two things: snake oil (registry tweaks that do nothing measurable) or bloatware (background services that steal more resources than they free up). A few real optimizations - CPU affinity, process priority, timer resolution - genuinely move the needle. They just require manual setup every single game launch.

I wanted a tool that did the real stuff automatically. I built one from scratch in a few weeks using Claude as my primary coding partner. This is the technical story, including some things that surprised me.

---

## What Actually Works on Modern Windows

Before the build story, it's worth being specific about which optimizations are real.

**CPU affinity pinning** depends heavily on your hardware. On a Ryzen 7 5800X3D, a few cores have 3D-stacked V-Cache that makes a measurable difference to 1% lows when the game is pinned there. On Intel hybrid CPUs (P-cores + E-cores), the effect is even cleaner: game goes on P-cores, background processes stay off them. On a standard homogeneous CPU the effect is smaller, but reducing OS scheduling noise on the game process is still a valid goal.

**Timer resolution** is unambiguous. Windows defaults to a 15ms scheduling tick. Calling `timeBeginPeriod(1)` drops it to 1ms. `Sleep(1)` in a game loop becomes accurate instead of sleeping anywhere from 1-15ms. This shows up directly in online game smoothness.

**Win32PrioritySeparation** is subtle. Setting it to `26` (short fixed quanta, no foreground boost) prevents the OS from aggressively stealing scheduler quanta to service the foreground boost mechanism. For a fullscreen game that already has foreground, this is mainly defensive.

**SysMain (Superfetch)** is a relic on NVMe. It was designed to prefetch pages from slow spinning disks. On NVMe it adds overhead without benefit, and under memory pressure it competes with your game for RAM.

**Background process I/O priority** is a quiet win. Setting OneDrive, cloud sync, and antivirus to BelowNormal CPU priority and Background I/O class means they yield to anything higher-priority the moment it needs resources. The jitter monitor made this very visible - more on that later.

**Game DVR / background capture** is one people often forget. Xbox Game Bar keeps a rolling video buffer of your gameplay even when you're not actively recording. That's constant GPU encoder load and memory bandwidth. The app can disable `GameDVR_Enabled` and `AppCaptureEnabled` while pinning is active and restore them on exit.

What doesn't work: registry "optimization packs", randomly disabling services, most things popular YouTube channels recommend.

---

## Why Native WinUI 3, Not Electron

The obvious joke is "it would be ironic to build a performance optimizer on a framework that uses 300MB of RAM by default." But beyond the joke, native is the right call for this specific problem.

The app uses P/Invoke to call `pdh.dll`, `winmm.dll`, `ntdll.dll`, and `advapi32.dll` directly. It subscribes to WMI process events. It reads CPU frequency data from the Windows registry per logical core. Electron or any browser-based framework would require a native helper process for all of this anyway - at which point you've got the worst of both worlds.

WinUI 3 with the Windows App SDK turned out to be a strong choice. The XAML toolchain is mature, Mica backdrop looks great, `CommunityToolkit.Mvvm` eliminates the MVVM boilerplate, and the app runs as a standard Win32 process that can call any Windows API.

One sharp edge: the `MicaBackdrop Kind="MicaAlt"` attribute silently crashes the XAML compiler with exit code 1 and no error message. I lost an hour to it. The fix is to remove the `Kind` attribute. This appears to be a bug in Windows App SDK 2.0.1's XAML compiler on specific build configurations.

---

## WMI vs PDH: A 60-100x Performance Difference

The most surprising technical finding: the gap between WMI and PDH for CPU sampling.

My first implementation used WMI `Win32_PerfFormattedData_PerfOS_Processor`:

```csharp
using var searcher = new ManagementObjectSearcher(
    "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor");
foreach (var obj in searcher.Get()) { ... }
```

Each call takes **300-500ms** due to COM overhead and kernel round-trips. The app's main loop runs every 1 second - I couldn't spend half of it waiting for a CPU query.

The PDH (Performance Data Helper) API keeps an open query handle. You pay initialization once; each subsequent sample is ~5ms:

```csharp
PdhOpenQuery(null, 0, out var queryHandle);
PdhAddEnglishCounterW(queryHandle, @"\Processor(0)\% Processor Time", 0, out var counter);

// In the update loop - ~5ms
PdhCollectQueryData(queryHandle);
PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var value);
```

That's a **60-100x improvement** that shaped the whole architecture. The heavy path (CPU + GPU sampling via PDH) runs every 3 seconds. The fast path (process scan, network ping, snapshot emission) runs every 1 second and reads the cached PDH result without re-querying.

---

## P-Core Detection Without CPUID

On Intel hybrid CPUs, you want the game pinned to P-cores. The challenge: how do you identify which logical processors are P-cores without writing CPUID assembly or pulling in a hardware info library?

Windows already knows, and stores it in the registry.

`HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\{N}\~MHz` holds the current clock speed of logical processor N. On a hybrid CPU, P-cores run at meaningfully higher frequencies than E-cores - typically around 2x. Clustering by frequency gives a reliable P/E split in pure managed C#:

```csharp
var coreMhz = new List<int>();
for (int i = 0; i < total; i++)
{
    using var key = Registry.LocalMachine.OpenSubKey(
        $@"HARDWARE\DESCRIPTION\System\CentralProcessor\{i}");
    if (key?.GetValue("~MHz") is int mhz) coreMhz.Add(mhz);
}

// If max/min differ by <15%, cores are uniform - not a hybrid CPU
if ((double)(maxMhz - minMhz) / maxMhz < 0.15) return null;

// Cores more than 15% above min are P-cores
var pCores = coreMhz.Select((mhz, idx) => (mhz, idx))
    .Where(x => x.mhz > minMhz + (maxMhz - minMhz) * 0.15)
    .Select(x => x.idx).ToList();
```

The 15% threshold avoids false positives from frequency variation within the same core type while catching the real P/E split. On an i9-13900K with 8 P-cores at ~5GHz and 16 E-cores at ~3.8GHz, it works correctly every time.

(AMD Ryzen X3D is a different problem - the V-Cache cores aren't identified by frequency. That's on the roadmap, likely requiring a small native CPUID helper.)

---

## The Pinning Safety Invariant

The app's default state is monitoring-only: zero process modifications until you explicitly flip the toggle. This sounds simple but has a surprisingly large attack surface.

Early in development I noticed the app was modifying Firefox's CPU affinity even with "CPU Pinning: OFF" shown in the UI. The root cause: a WMI event callback for `Win32_ProcessStartTrace` was unconditionally calling affinity-setting code when Firefox launched, ignoring the `PinningEnabled` flag.

After auditing every call site, I found four places where the guard was missing:
- `OnProcessStarted` WMI callback
- `Scan()` polling fallback
- `ThrottleBg()` - background throttling on a 60s cadence
- `ApplyGame()` - game affinity/priority application

The fix was mechanical: every write to `ProcessorAffinity` or `PriorityClass` in `ProcessManager` is now wrapped in `if (PinningEnabled)`. But the lesson is that a code fix without a test just re-grows over time.

The test pattern: every actual process modification produces a log entry via the `LogEntry` event. Subscribe and assert nothing is logged:

```csharp
[Fact]
public void ThrottleBg_EmitsNoLogEntries_WhenPinningDisabled()
{
    var pm = new ProcessManager(DefaultConfig());
    var logs = new List<string>();
    pm.LogEntry += logs.Add;

    pm.ThrottleBg(); // PinningEnabled defaults to false

    Assert.Empty(logs); // any modification would have logged
    pm.Dispose();
}
```

"No log = no modification" is an invariant that survives future refactoring. 106 tests pass on every push via GitHub Actions CI.

---

## The Live Latency Monitor

One feature that turned out more useful than I expected: the latency and jitter display.

Most gaming performance discussion focuses on FPS. But for online games, what matters is the consistency of your network path - specifically, jitter (variance in round-trip time), not average latency. A 20ms ping with 15ms jitter plays worse than a 40ms ping with 2ms jitter.

The app pings a configurable host (default `1.1.1.1`) every 2 seconds and computes jitter using the RFC 3550 smoothed mean deviation: `jitter += (|new - prev| - jitter) / 16`. This is the same algorithm RTP uses for QoS measurement.

In practice this became my go-to diagnostic when sessions felt choppy but FPS was fine. The jitter chart consistently spiked during OneDrive bulk uploads. The "suspend sync apps during gameplay" feature came directly from watching this happen.

---

## Building With Claude

I built this project almost entirely through conversation with Claude. Not autocomplete - more like pair programming where Claude writes most of the code and I review, redirect, and reject things I disagree with.

**It handles the plumbing well.** P/Invoke signatures, WMI query strings, `ConcurrentDictionary` threading patterns, XAML data binding, registry access - the code that's tedious to get right from documentation. This was probably 60-70% of the code volume.

**Architecture needs steering.** Left alone, Claude defaults to abstraction layers that aren't needed: interfaces for single implementations, base classes for two subclasses, defensive null checks on things that can't be null. I had to repeatedly ask for "the simplest thing that works." The `CLAUDE.md` file in the repo (loaded at session start) helped - explicit written constraints outlasted the context window better than repeated verbal corrections.

**It catches its own bugs when asked the right way.** The pinning-when-off bug wasn't caught during initial implementation. But when I described the symptom - "it's pinning Firefox even though pinning is off" - Claude identified the call sites and produced the fix correctly. Observable behavior beats abstract questions.

**Context degrades over long sessions.** After 50+ messages, early constraints start being forgotten. `CLAUDE.md` as a persistent anchor helped noticeably.

The honest summary: building with Claude is faster, not easier. Less time typing, more time thinking about what you actually want. That's probably the right trade.

---

## What's Next

The most obvious gap: no overlay. There's currently no way to see CPU/GPU/latency metrics without alt-tabbing out of a game. A lightweight DirectX overlay is on the list.

Other planned work:
- `winget` manifest so `winget install gaming-optimizer` works
- AMD Ryzen X3D V-Cache core detection (frequency clustering doesn't work here)
- Windows 10 testing pass (targets build 17763 but only actively tested on Windows 11)

The code is MIT-licensed on GitHub at [github.com/maxrenke/game-optimizer](https://github.com/maxrenke/game-optimizer). 106 tests, CI on every push, no telemetry, single-file `.exe` download from the releases page.

If you hit SmartScreen on the release binary, that's expected for unsigned open-source software. Click "More info" -> "Run anyway". The release builds directly from source via GitHub Actions - you can verify the build chain or build from source yourself.
