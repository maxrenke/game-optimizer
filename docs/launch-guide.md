# Gaming Optimizer - Launch Guide

Everything in order. Do the steps top to bottom.

---

## Launch Order (quick reference)

| Step | Action | Where |
|------|--------|--------|
| 1 | Record GIF and push it | `docs/gif-guide.md` |
| 2 | Tag v1.0.0 to trigger release | Step 2 below |
| 3 | Post Show HN | Step 3 below |
| 4 | Post first HN comment immediately | Step 4 below |
| 5 | Reddit crosspost | Step 6 below |
| 6 | LinkedIn post | Step 7 below |
| 7 | Publish blog post | `docs/blog-post.md` |

---

## Step 1 - Record and Add the GIF

Full instructions: `docs/gif-guide.md`

After recording, save to:
  docs/screenshots/dashboard.gif

Then commit and push:

  git add docs/screenshots/dashboard.gif
  git commit -m "Add dashboard GIF to README"
  git push origin main

The README img tag is already wired - it renders immediately after push.

---

## Step 2 - Tag and Release

Run in PowerShell from the repo root (C:\Users\m_ren\repos\GameOptimizer):

  git tag v1.0.0
  git push origin v1.0.0

This triggers the release.yml GitHub Actions workflow which:
  1. Runs the full test suite (106 tests)
  2. Publishes win-x64 self-contained single-file exe
  3. Creates a zip of the full build folder
  4. Creates a GitHub Release with both files attached

Check progress: https://github.com/maxrenke/game-optimizer/actions
Expected runtime: 5-8 minutes.

Release URL after completion:
  https://github.com/maxrenke/game-optimizer/releases/tag/v1.0.0

---

## Step 3 - Post on Hacker News

URL: https://news.ycombinator.com/submit

Timing: weekday, 9am-12pm Eastern. Monday-Wednesday gets the most traction.
Do NOT post and walk away - stay in the comments for the first 2-3 hours.

### TITLE (copy exactly)

Show HN: Gaming Optimizer - WinUI 3 app that auto-pins games to your fastest CPU cores

### BODY (paste into the "text" field)

Native WinUI 3 desktop app that manages CPU affinity, process priorities, and timer
resolution automatically the moment a game launches - then restores everything when
the game closes. Monitoring-only by default; pinning is opt-in via a toggle.

MIT, no telemetry: https://github.com/maxrenke/game-optimizer

---

## Step 4 - Post First Comment (do this immediately after submitting)

This is the most important part. Post it yourself before anyone else comments.
Copy the block below exactly:

---

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
topology, and WinUI 3. Cross-platform isn't really on the table for this kind of app.

Happy to answer questions about any of the implementation details.

---

## Step 5 - HN Comment Prep (likely questions and ready answers)

"Does it actually help FPS?"
  Depends on hardware. On hybrid CPUs (Intel P/E cores, AMD X3D V-Cache)
  measurably yes for 1% lows. On uniform CPUs the effect is smaller. The dashboard
  lets you observe it directly - I'm not asking you to take my word for it.

"Why Windows only?"
  The app uses PDH, WMI, Win32 process APIs, the Windows registry for CPU topology,
  and WinUI 3. It's not a choice against other platforms - it's inherently
  Windows-native in every layer.

"Why not just use Process Lasso / Razer Cortex?"
  Those are both fine. This is open source, free, and focused specifically on the
  optimizations that are measurable rather than a full feature suite. No background
  service, no startup entry.

"Does it work on Windows 10?"
  Targets build 17763 (1809). Tested primarily on Windows 11 - Windows 10
  testing pass is on the roadmap.

"What's it doing to my system / is it safe?"
  Everything is in the source. The pinning safety invariant (monitoring-only by
  default, gated writes, 106 tests) is documented in the README and blog post.
  No telemetry, one configurable outbound ICMP ping for the latency monitor.

"I built something similar"
  Engage genuinely. Ask what approach they took, share specifics. This is good.

---

## Step 6 - Reddit Crosspost

Post to r/pcgaming or r/hardware after HN gets some traction (same day or next day).

Title:
  I built an open-source Windows game optimizer that automatically manages CPU affinity and process priorities - here's what actually works vs. placebo

Body:
  Most game optimizers are snake oil. But a few things genuinely work:

  - CPU affinity pinning (especially P-cores on Intel hybrid CPUs, or V-Cache cores on AMD)
  - timer resolution (timeBeginPeriod(1) drops OS scheduling jitter from ~15ms to ~1ms)
  - Win32PrioritySeparation (prevents foreground boost from robbing your game)
  - Stopping SysMain on NVMe (it's actively harmful under memory pressure)
  - Suspending cloud-sync apps during gameplay (OneDrive mid-game disk access is a real 1% low killer)
  - Disabling Game DVR background capture (constant GPU encoder load you're not using)

  I built an app that does all of this automatically the moment a game launches, then
  restores everything when you close it. Dashboard shows per-zone CPU %, GPU metrics,
  network sparkline, and latency/jitter (the metric that actually predicts online game
  smoothness).

  MIT, no telemetry: https://github.com/maxrenke/game-optimizer

  Happy to answer questions about what the optimizations actually do.

---

## Step 7 - LinkedIn Post

Post after HN. Link to the blog post or GitHub.

---

I spent the last few weeks building a native Windows game optimizer from scratch - and writing about what I actually learned.

The short version: most "game optimizer" software is either placebo or bloatware. But a handful of optimizations genuinely work (CPU affinity, timer resolution, process I/O priority), they just require manual setup every single game launch. I built a tool that handles it automatically.

Some things that surprised me during the build:

- WMI and PDH both read CPU utilization, but WMI costs 300-500ms per call vs ~5ms for PDH with a persistent query handle. That's a 60-100x difference that completely changed the app's architecture.

- On Intel hybrid CPUs, you can detect which logical processors are P-cores vs E-cores by reading clock frequency from the Windows registry per core - no CPUID assembly required.

- "Monitoring-only by default" sounds simple until you audit every call site and find four places where process modifications were happening regardless of the setting. The fix needed tests, not just code changes.

I built the majority of this through conversation with Claude rather than writing code directly. It's fast at the plumbing (P/Invoke signatures, WMI queries, XAML data binding). Architecture needs more steering - it defaults toward abstraction layers the problem doesn't require.

Full technical writeup and the app (MIT, no telemetry):
https://github.com/maxrenke/game-optimizer

---

## Step 8 - Publish Blog Post

File: docs/blog-post.md

Suggested platforms: dev.to, Hacker News (self-post), your own site.
Post the same day as or day after the Show HN submission.
The HN first comment is a condensed version of the blog - they reinforce each other.
