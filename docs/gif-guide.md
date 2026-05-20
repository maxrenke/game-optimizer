# How to Record a GIF for Gaming Optimizer

---

## Goal

Capture the dashboard updating live while a game is running with CPU pinning
enabled. Viewers need to see numbers moving and processes appearing in the zone
list - a static screenshot does the same job as a frozen GIF.

---

## Setup (do this before opening ScreenToGif)

1. Set monitor to 1920x1080 if possible - GIFs scale poorly from 4K
2. Set the app window to roughly 1000x860 (its default) and center it on screen
3. Have a game ALREADY RUNNING before you start recording - game detection is
   instant but launching a game during a 15s GIF wastes most of the clip on a
   loading screen
4. Turn on CPU pinning BEFORE you start recording so the zone list is already
   populated with your game and background processes
5. Close anything behind the app window that would be distracting if it shows

---

## What to Capture (30-45 seconds real time, trims to ~10-15s)

  Step 1 - App open, pinning ON, idle metrics visible          ~3 seconds
  Step 2 - Hold on the dashboard while metrics update live     ~8 seconds
           (CPU game-zone bar, GPU util, latency sparkline all moving)
  Step 3 - Optional: hover over the tray icon to show tooltip  ~3 seconds

Do NOT capture: settings page, app launching, anything sitting still.

---

## Recording with ScreenToGif

Install (run in PowerShell):

  winget install NickeManarin.ScreenToGif

Steps:
  1. Open ScreenToGif -> Recorder
  2. Drag the orange capture region to cover ONLY the Gaming Optimizer window
     Crop tight to the app border - do not capture your whole screen
  3. Set framerate to 15 fps
  4. Hit record, do your sequence, hit stop
  5. In the editor: trim the first and last second
     (the click to start/stop is always awkward)
  6. Editor -> Optimize: set lossy compression to 15-20
     Cuts file size in half with no visible quality loss
  7. Save as GIF

Target file size: under 3MB
GitHub renders GIFs inline up to ~10MB but slow connections stall above 3-4MB.
At 900px wide, 15fps, 12 seconds you should land around 2-3MB with compression.

---

## After Recording

1. Save the GIF to:
     docs/screenshots/dashboard.gif

2. The README img tag is already wired. Commit and push:

     git add docs/screenshots/dashboard.gif
     git commit -m "Add dashboard GIF to README"
     git push origin main

   GitHub renders it inline immediately.

---

## What Good Looks Like

GIFs that get upvoted on HN and Reddit for tools like this have one thing in
common: something is visibly happening the whole time. For this app that means
the sparklines are moving, CPU % numbers are changing, and there is at least
one game process visible in the zone list.

If the GIF could be a screenshot, reshoot it.
