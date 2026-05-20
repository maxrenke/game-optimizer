# Show HN Post

---

## TITLE (copy this exactly into the HN title field)

```
Show HN: Gaming Optimizer - WinUI 3 app that auto-pins games to your fastest CPU cores
```

---

## BODY (optional - paste into the "text" field if you want to add context to the submission)

```
Native WinUI 3 desktop app that manages CPU affinity, process priorities, and timer
resolution automatically the moment a game launches - then restores everything when
the game closes. Monitoring-only by default; pinning is opt-in via a toggle.

MIT, no telemetry: https://github.com/maxrenke/game-optimizer
```

---

## FIRST COMMENT (post this yourself immediately after submitting - this is the most important part)

```
Hi HN, I'm Max, the author.

Why I built this: most "game optimizer" software is either placebo (random registry
tweaks) or spyware. But a few optimizations genuinely work - CPU affinity, timer
resolution, Win32PrioritySeparation - they just require manual setup every single
game launch. I wanted a tool that did the real stuff automatically.

Some technical things I found interesting while building it:

---

**WMI vs PDH for CPU sampling - 60-100x difference**

My first implementation used WMI Win32_PerfFormattedData_PerfOS_Processor to get
per-core CPU %. Each call takes 300-500ms due to COM overhead. The PDH API
(pdh.dll) with a persistent open query takes ~5ms for the same data. That discovery
unlocked the whole architecture: a fast 1s path for process scanning/snapshots and a
separate 3s path for CPU/GPU that reads cached PDH results without blocking.

---

**P-core detection without CPUID**

On Intel hybrid CPUs you want the game pinned to P-cores. I didn't want to write
CPUID assembly or pull in a hardware library. Turns out Windows already knows:
HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\{N}\~MHz holds the current
clock frequency per logical processor. P-cores run ~2x faster than E-cores, so
clustering by frequency gives a reliable P/E split in pure managed C# code.

---

**The pinning safety invariant**

The app's default state is monitoring-only - zero process modifications. This turned
out to have more attack surface than I expected. I found that Firefox was getting its
affinity set even with "CPU Pinning: OFF" displayed, because a WMI event callback
(Win32_ProcessStartTrace) was calling affinity code unconditionally.

After auditing all call sites, every write to ProcessorAffinity or PriorityClass in
ProcessManager is now wrapped in `if (PinningEnabled)`. The invariant is tested by
subscribing to LogEntry events and asserting nothing is logged when pinning is off -
since every actual modification produces a log entry.

---

**Building with Claude**

I built most of this through conversation with Claude rather than writing code
directly. The P/Invoke signatures, WMI queries, threading patterns, XAML data
binding - the tedious-but-precise work - Claude handles quickly. Architecture and
simplicity needed more steering; left alone it tends toward abstraction layers that
aren't needed. Overall: faster than building alone, but not easier - you spend less
time typing and more time thinking about what you actually want.

---

Windows-only: it's using PDH, WMI, Win32 process APIs, the Windows registry for CPU
topology, and WinUI 3. Cross-platform isn't really on the table for this kind of
app.

Happy to answer questions about any of the implementation details.
```

---

## NOTES FOR POSTING

- Post between 9am-12pm Eastern on a weekday (Mon-Wed tend to get the most traction)
- Do NOT submit and go to sleep - you need to be in the comments for the first 2-3 hours
- Respond to every technical question quickly; HN rewards engagement
- If someone asks "does it work on Windows 10?" - yes, it targets build 17763 (1809)
- If someone asks about security / what it's doing to their system - point to the source and the pinning invariant section; everything is in the code
- If someone asks why not use Process Lasso / Razer Cortex - those are good, this is open source, free, and focused on the specific optimizations that are measurable rather than a full feature suite
- Expect the top comments to be: "why Windows only?", "does it actually help FPS?", and "I built something similar" - all fine, engage honestly

## REDDIT CROSSPOST (r/pcgaming or r/hardware)

Title:
```
I built an open-source Windows game optimizer that automatically manages CPU affinity and process priorities - here's what actually works vs. placebo
```

Body:
```
Most game optimizers are snake oil. But a few things genuinely work:

- CPU affinity pinning (especially P-cores on Intel hybrid CPUs, or V-Cache cores on AMD)
- timer resolution (timeBeginPeriod(1) drops OS scheduling jitter from ~15ms to ~1ms)
- Win32PrioritySeparation (prevents foreground boost from robbing your game)
- Stopping SysMain on NVMe (it's actively harmful under memory pressure)
- Suspending cloud-sync apps during gameplay (OneDrive mid-game disk access is a real 1% low killer)

I built an app that does all of this automatically the moment a game launches, then restores everything when you close it. Dashboard shows per-zone CPU %, GPU metrics, network sparkline, and latency/jitter (the metric that actually predicts online game smoothness).

MIT, no telemetry: https://github.com/maxrenke/game-optimizer

Happy to answer questions about what the optimizations actually do.
```
