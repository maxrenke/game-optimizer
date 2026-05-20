# I Built a Windows Game Optimizer With AI - Here's What I Actually Learned

Most "game optimizer" software is one of two things: snake oil (registry tweaks that do nothing measurable) or bloatware (background services that steal more resources than they free up). A few of the real optimizations - CPU affinity, process priority, timer resolution - genuinely move the needle, but they require manual setup every single game launch.

I wanted a tool that did the real stuff automatically. I built one from scratch in a few weeks using Claude as my primary coding partner. Here's the technical story.

---

## The Legitimate Optimizations

Before getting into the build, it's worth being specific about what actually works on modern Windows.

**CPU affinity pinning** is real, at least on some hardware. On a Ryzen 7 5800X3D - the game-focused CPU I have - a few cores have notably higher L3D V-Cache (3D stacked cache). Pinning the game process to those cores and keeping background processes off them can meaningfully reduce 1% lows. On Intel hybrid CPUs (P-cores + E-cores), the effect is even more clear-cut: you want the game on P-cores, period.

**Timer resolution** is real. Windows defaults to a 15ms scheduling tick interval. Calling `timeBeginPeriod(1)` drops that to 1ms. The difference shows up in online games as reduced jitter - `Sleep(1)` becomes accurate instead of sleeping anywhere from 1-15ms. This one is unambiguous.

**Win32PrioritySeparation** is real but subtle. Setting it to `26` (short fixed quanta, no foreground boost) keeps the OS from aggressively boosting whatever window has focus. For a fullscreen game that's already the foreground, this mainly prevents Windows from yanking scheduler quanta from the game thread to service the foreground boost.

**SysMain (Superfetch)** is a relic on NVMe. Its job is prefetching frequently used pages from slow spinning disks into RAM. On an NVMe drive it adds overhead without benefit, and under memory pressure it actively competes with the game for RAM. Stopping it is a safe, low-overhead win.

**Process priority and I/O class** for background apps: setting OneDrive, your virus scanner, cloud sync clients to BelowNormal CPU priority and background I/O class means they back off the moment anything higher-priority (like your game) needs resources.

What doesn't work: registry "optimization packs", disabling services at random, most things popular YouTube channels recommend.

---

## Why Native WinUI 3, Not Electron

The obvious joke here is "it would be ironic to build a performance optimizer on a framework that uses 300MB of RAM by default." But beyond the joke, native is actually the right call.

The app uses P/Invoke to call `pdh.dll`, `winmm.dll`, `ntdll.dll`, and `advapi32.dll` directly. It uses WMI for process event subscriptions. It reads CPU frequency data out of the Windows registry per logical core. Electron or any browser-based framework would make all of this substantially harder or require a native helper process anyway - at which point you've got the worst of both worlds.

WinUI 3 with the Windows App SDK turned out to be a strong choice. The XAML toolchain is mature, Mica backdrop looks great, `CommunityToolkit.Mvvm` makes the MVVM boilerplate disappear, and the whole thing runs as a standard win32 process that can call any Windows API.

The one gotcha: the default `MicaBackdrop Kind="MicaAlt"` attribute silently crashes the XAML compiler with exit code 1 and no error message. I lost an hour to this. The fix was to remove the `Kind` attribute. I suspect this is a bug in Windows App SDK 2.0.1's XAML compiler that only triggers on certain build configurations.

---

## The WMI vs PDH Discovery

The most surprising technical finding in this project was the performance difference between WMI and PDH for CPU sampling.

My first implementation used WMI `Win32_PerfFormattedData_PerfOS_Processor` to get per-core CPU utilization:

```csharp
// First attempt - WMI approach
using var searcher = new ManagementObjectSearcher(
    "SELECT PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor");
foreach (var obj in searcher.Get()) { ... }
```

This takes **300-500ms** per call. That's a COM overhead + kernel round-trip cost that you pay every single time you open a new `ManagementObjectSearcher`. The app's main loop runs every 1 second - I couldn't spend half of it in a CPU query.

The PDH (Performance Data Helper) API solves this by keeping an open query handle. You pay the initialization cost once, then each subsequent sample takes ~5ms:

```csharp
PdhOpenQuery(null, 0, out var queryHandle);
PdhAddEnglishCounterW(queryHandle, @"\Processor(0)\% Processor Time", 0, out var counter);
// ...one handle per core...

// In the loop - ~5ms per call
PdhCollectQueryData(queryHandle);
PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out _, out var value);
```

That's a **60-100x improvement** that unlocked the whole 1-second cadence architecture. The heavy path (CPU + GPU sampling) runs every 3 seconds; the fast path (process scan, network, snapshot emission) runs every 1 second and reads the cached PDH result without re-querying.

---

## P-Core Detection Without CPUID

Intel hybrid CPUs (P-cores + E-cores) present a problem: you want to pin the game to P-cores, but how do you identify which logical processors are P-cores without writing CPUID assembly or pulling in a hardware info library?

It turns out Windows already knows, and stores it in the registry.

`HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\{N}\~MHz` contains the current clock speed of logical processor N. On a hybrid CPU, P-cores run at meaningfully higher frequencies than E-cores - typically 2x higher. Reading this key for all logical processors and clustering by frequency gives you a reliable P/E split without any low-level CPU instructions:

```csharp
var coreMhz = new List<int>();
for (int i = 0; i < total; i++)
{
    using var key = Registry.LocalMachine.OpenSubKey(
        $@"HARDWARE\DESCRIPTION\System\CentralProcessor\{i}");
    if (key?.GetValue("~MHz") is int mhz) coreMhz.Add(mhz);
}

// If max/min differ by >15%, it's a hybrid CPU
if ((double)(maxMhz - minMhz) / maxMhz < 0.15) return null; // uniform, not hybrid

// Cores with MHz > 15% above min are P-cores
var pCores = coreMhz.Select((mhz, idx) => (mhz, idx))
    .Where(x => x.mhz > minMhz + (maxMhz - minMhz) * 0.15)
    .Select(x => x.idx).ToList();
```

The 15% threshold is conservative enough to avoid false positives from frequency variation at the same core type, but wide enough to catch the real P/E split. On an i9-13900K with 8 P-cores at ~5GHz and 16 E-cores at ~3.8GHz, this nails it every time.

---

## The Pinning Safety Invariant

The app's default state is monitoring-only - no process modifications. CPU pinning must be explicitly enabled. This sounds simple but turns out to have a surprisingly large attack surface.

Early in development I noticed the app was modifying Firefox's CPU affinity even with "CPU Pinning: OFF" displayed in the UI. The root cause: a WMI event callback for `Win32_ProcessStartTrace` was unconditionally calling affinity-setting code when Firefox started, regardless of the `PinningEnabled` flag.

After auditing every call site, I found the problem in four places:
- `OnProcessStarted` WMI callback - fired for Firefox/media and background processes
- `Scan()` fallback loop - the 1s polling fallback for Firefox
- `ThrottleBg()` - background process throttling on a 60s cadence
- `ApplyGame()` - game priority/affinity application

The fix was mechanical: every code path in `ProcessManager` that writes to `ProcessorAffinity` or `PriorityClass` is now wrapped in `if (PinningEnabled)`. `ThrottleBg()` has an early return at the top. But the lesson is that the fix needs tests, not just code changes.

The test approach: since every actual process modification produces a log entry (via the `LogEntry` event), you can subscribe to that event and assert nothing is logged:

```csharp
[Fact]
public void ThrottleBg_EmitsNoLogEntries_WhenPinningDisabled()
{
    var pm = new ProcessManager(DefaultConfig());
    var logs = new List<string>();
    pm.LogEntry += logs.Add;
    
    pm.ThrottleBg(); // PinningEnabled is false by default
    
    Assert.Empty(logs); // any modification would have logged
    pm.Dispose();
}
```

This pattern - "no log = no modification" - let me verify the invariant across all the affected call sites in a way that survives future refactoring.

---

## Building With Claude

I built this project almost entirely through conversation with Claude. Not "Copilot autocomplete" style - more like pair programming where Claude writes most of the code and I review, redirect, and push back on decisions I disagree with.

A few observations on what that workflow actually looks like:

**It's very good at the plumbing.** P/Invoke signatures, WMI query strings, `ConcurrentDictionary` threading patterns, XAML data binding setup - the kind of code that's tedious to get right from documentation, Claude handles quickly and correctly. This probably represented 60-70% of the actual code volume.

**It needs steering on architecture.** Left to its own devices, Claude tends toward over-engineering: abstract base classes, unnecessary interfaces, defensive null checks everywhere. I had to repeatedly ask for "the simplest thing that works" and reject first drafts that were technically fine but more complex than the problem required. The CLAUDE.md instructions in the repo ("return the simplest working solution, no over-engineering") helped calibrate this.

**It catches its own bugs when you ask the right questions.** The pinning-when-off bug wasn't caught by Claude during initial implementation - but when I described the symptom ("it's pinning Firefox even though pinning is off"), it identified the problem correctly and produced the fix. The key was describing observable behavior, not asking abstract questions.

**Context management matters.** Long sessions degrade. After ~50+ messages, I noticed responses starting to forget constraints established early in the conversation. The project's CLAUDE.md file (which Claude Code loads at session start) serves as a persistent memory of code conventions and architectural decisions, and noticeably improved consistency across sessions.

The most honest framing: building with Claude is faster than building alone, but it's not easier. You spend less time typing and more time thinking about what you actually want - which is probably the right trade.

---

## The Live Latency Monitoring

One feature that surprised me by being more useful than I expected: the latency and jitter display.

Most game "performance" discussions focus on FPS. But for online games, what matters is the consistency of the network path to the game server - specifically, the variance in round-trip time (jitter), not the average latency. A 20ms ping with 15ms jitter plays worse than a 40ms ping with 2ms jitter.

The app pings a configurable host (default `1.1.1.1`) every 2 seconds and computes jitter using the RFC 3550 method (smoothed mean deviation between consecutive samples): `jitter += (|new - prev| - jitter) / 16`. This is the same algorithm RTP uses for QoS measurement.

In practice, this became my go-to diagnostic when a game session felt choppy but FPS was fine. Nine times out of ten the jitter chart was spiking whenever a cloud sync client was doing a bulk upload. The "suspend apps during gameplay" feature came directly from watching this happen repeatedly.

---

## What's Next

The feature gap that's most conspicuous right now: no overlay. There's no way to see the CPU/GPU/latency metrics without alt-tabbing to the app. A lightweight DirectX overlay (similar to what MSI Afterburner does) is on the list.

A few other things I want to add:
- `winget` manifest so `winget install gaming-optimizer` works
- AMD Ryzen X3D detection (the 3D V-Cache cores aren't identified by frequency, so they need a different approach - probably checking CPUID topology via a small native helper)
- Windows 10 testing pass (the app targets build 17763 but I've only actively tested on Windows 11)

The code is on GitHub at [github.com/maxrenke/game-optimizer](https://github.com/maxrenke/game-optimizer) under MIT. 106 tests, GitHub Actions CI, no telemetry.

If you run into SmartScreen on the release binary, that's expected - the app isn't signed with a paid certificate. Click "More info" -> "Run anyway". The release workflow builds directly from this source via GitHub Actions, so you can verify the build chain or build from source yourself.
