# Windows 1.0.0 — tray caption validation

Date: 2026-10-11, Asia/Dubai. Branch: `windows/input-validation`. Source: `2227741b2177c01318d1805ba3fe80f7d4703275`.

Physical Windows 11 Pro 25H2, build 26200.9457, interactive desktop, 2560×1440 at 100% / 96 DPI. .NET SDK 10.0.401. Release self-contained win-x64 publish with `TreatWarningsAsErrors=true` passed without warnings after source publication.

Installed `ProductVersion`: `1.0.0+2227741b2177c01318d1805ba3fe80f7d4703275`. Size: 234453039 bytes. SHA-256: `48e4a1420dfae087f35a60bd5cf3414d20f789dd4ada98abdb898cb66613313a`. Desktop EXE and build hashes match; previous installed EXE backed up locally outside Git.

## Native visual reproduction and validation

Old installed 0.3.4 (`e614282`) retained a red Close caption button after closing into the tray and restoring with the cursor already in the window body. Verified using Computer Use: close ordinary window with X; open isolated `--ui-preview`; click a neutral body point; close preview with Alt+F4 without returning the pointer to X; start ordinary EXE again; inspect restored ordinary window. Preview has no pairing/settings/input hooks/network/audio.

Replacing the WinUI color-customized caption with DWM's native caption resolved this sequence. DWM background/text/dark-mode attributes are set once when creating the window; normal red Close hover remains. The final source includes none of the unsuccessful message-reset, child-window enumeration, deferred-hide or presenter-toggle experiments.

The same sequence passed on the candidate and exact installed final EXE: X hides without process exit; actual second invocation exits 0; original process/window restores with dark title bar and unhighlighted Close while cursor is at a neutral body point. Native tray Open routes to the same restore method, separately exercised by the tray diagnostic. Installed app reconnects automatically and live packet/statistics counters update. A final observed underrun count was 1; its cause and audibility were not established.

## Final EXE diagnostics

All ordinary app processes were fully exited through Menu → Exit before diagnostics. All modes exited 0.

| Check | Result | Evidence |
| --- | --- | --- |
| Native tray / lifecycle | 13/13 | `tray-checks.json` |
| Native input | 15/15 | `input-checks.json` |
| Native WinUI layout | 108 cases, no failures, 96 DPI | `layout-checks.json` |
| Core `.NET --ui-loop-check` | 39 PASS, ALL CHECKS PASSED | Terminal output inspected |

Input pump response during a 300 ms UI stall: 0.1453 ms. Core checks include zero measured allocations for publishing 100000 mouse movements. These are software diagnostics, not physical sensor-to-display latency or game FPS measurements. Fullscreen diagnostics were not repeated because protection logic was unchanged; previous 8/8 belongs to `e614282`.

No new physical mouse, keyboard, audible audio or Dota 2 A/B measurement. Prior hardware observations apply to their recorded source versions. Other DPI / 8000 Hz remain pending. No credentials, personal settings, tokens or live private addresses are included.

## Next step

Mac build reads root VERSION, now 1.0.0. Rebuild/check Mac and verify physical input/audio with installed Windows 1.0.0 before packaging both platforms and publishing v1.0.0 using `source/RELEASING.md`. Public release/tag/OTA was not changed by this Windows task.
