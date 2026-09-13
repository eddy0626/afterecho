# AFTERECHO 0.2.2 — music restart and Feel movement

## Changes
- Tutorial remains a safe, separate preview. The ready button now creates a fresh run and schedules both the original song and chart from 0 seconds after four count-in beats. Intro notes in the real run count as movement/score; original note and encounter timestamps are unchanged.
- Wait for decoded audio before scheduling. Seek using seconds instead of import sample indices. If a source stops unexpectedly, pause the chart without advancing omission/enemy deadlines; resume with four beats.
- Feel 6.1 MMF_Player uses SquashAndStretch, Position and Rotation on the visual child: 140 ms body elasticity, 0.18-unit lift, 6-degree directional lean. Six pooled afterimages (170 ms) and foot dust marks (210 ms) accompany successful movement.
- Reduced motion, pause, retry and attacks restore the player pose and cancel conflicting feedback. No timeScale/hitstop, song pitch or judgment-window changes.

## Diagnosis boundary
The original WAV is 136.3735 seconds; stage end is 136.36 seconds. Original audio has signal through the ending. The previous tutorial handed off at approximately 12.370 seconds. In the Editor that handoff kept the audio and DSP aligned, so it was not alone proof of the reported WebGL cutoff. This release removes that mid-song handoff, gates audio readiness and protects against an interrupted source. Actual device audio output/latency still needs a phone retest.

## Verified before build
- Core deterministic checks: 111 passed for easy, normal and hard.
- Real-time Editor run: 19 checks passed. Tutorial → ready wait → song beginning → full run; normal pause/resume; forced audio stop/recovery; reduced motion; final door with music still playing; retry reset.
- 69 runtime samples, maximum observed AudioSource-vs-DSP difference 0.02134 seconds (this is software timing, not measured physical speaker latency).
- All chart notes resolved once, zero misses and six enemies defeated.
- Feel lift/stretch and enabled fading afterimages observed during the actual run.
- Full report: music-movement-live.json. Core report: core-tests.txt.

## Unity CLI (no Unity Assistant credits)
Run from /Users/youngbum/musicgame:

```sh
unity status --format json
unity command recompile --format json
unity command recompile_status --format json
unity command eval --timeout 30000 --code 'return Afterecho.Editor.AfterechoTests.RunAll();' --format json
unity command editor_play --format json
unity command eval --timeout 30000 --code 'return Afterecho.Editor.AfterechoMusicMovementQA.Start();' --format json
unity command eval --timeout 30000 --code 'return Afterecho.Editor.AfterechoMusicMovementQA.Status();' --format json
unity command editor_stop --format json
```

The live QA takes approximately 160 seconds. Do not recompile while it runs. The runner enables background execution only for this Editor test. Release has autoplay/invincibility/loop disabled and retains focus-loss pause.

## Build
Version: 0.2.2-movement.20260913
Active profile: Afterecho_Web_Team_0913 - Desktop - Release
Output: Builds/Afterecho_WebGL_Movement_20260913
Existing release: https://play.unity.com/en/games/cc1a30d0-6ce8-46ae-90f4-9dab209b3d83/afterecho-0913

Build and web verification results will be appended after completion.

Build succeeded: 0 errors, 545 warnings. ZIP validated, 30337980 bytes.

Build warning categories: {"Pipeline disabled in release": 1, "legacy importer metadata": 179, "deprecated API": 1, "other package diagnostics": 1, "unsupported optional video assets": 3, "installed AI inference shaders": 353, "optional Feel integration resources": 3, "TMP diagnostics": 4}

## Published web verification
Published to the existing unlisted URL. Fresh CDN build 367a5b68-39b0-475a-97f1-f8bf5168e5e8 loaded. New tutorial/main handoff and direct keyboard movement verified; reached STAGE 01 CLEAR / 100%, then returned to the menu. Browser test used periodic input and idle inspection gaps (score 9000, 2 kills, 4 damage); it verifies end-to-end flow, not difficulty. The existing unsupported FSR upscaling warning remains. No new-build runtime errors observed. See music-movement-web.json.
