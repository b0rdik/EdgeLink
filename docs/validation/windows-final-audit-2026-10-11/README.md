# Windows final code audit — 2026-10-11

Source and installed build: `e6142820946aa12f5d695832b76453d0b3a0edea`, version `0.3.4`, branch `windows/input-validation`. Physical Windows 11 Pro 25H2 / 96 DPI, .NET SDK 10.0.401. The working tree was clean before the audit; the branch was fetched before editing and publication. The previous installed EXE is backed up locally outside Git. This is a working-branch build; no public release, Mac build or OTA update was published.

The review covered Windows input capture/lifecycle, sampled edge policy, foreground protection, mouse mailbox, socket loops, audio buffering, discovery, single-instance handling, WinUI statistics and tray lifecycle. The changes address confirmed redundant work and a shutdown race; they do not establish that all possible bugs or costs have been eliminated.

## Changes

- An unchanged polling interval keeps the existing native timer. Previously another `SetTimer` restarted its countdown. Duplicate polling updates were removed from input configuration.
- Reapplying an unchanged fullscreen setting preserves the current watcher. Starting a watcher no longer performs two consecutive `Stop` operations.
- Query/post publication and the exit message share a lifecycle gate, preventing exit from overtaking an accepted query. Joining the thread and running actions happen outside the gate. Mouse/raw callbacks do not acquire this gate.
- The dashboard statistics timer runs only with a connection and a visible, non-minimized window. Closing into the tray, minimizing or disconnecting stops it; restore restarts it and refreshes the displayed counters. A new connection also resets the throughput measurement timestamp.
- Removed the unused `GetWindowRect` declaration, three unused imports, the duplicate `Disposing` alias/checks and an outdated navigation comment. Existing diagnostics and validation history remain useful and are retained.
- Fullscreen diagnostics now require the expected fixture window to be the actual foreground at each transition. Startup allows 15 seconds for activation; later transitions still have a 3-second timeout. Failure details distinguish incorrect focus from a watcher failure.

Microsoft documents the timer reset behavior in [SetTimer](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-settimer), and describes [Window.Activate](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.window.activate) as an attempt to activate the window. These API checks informed the timer cleanup and diagnostic focus validation.

## Final build results

The source was published, fetched and rebuilt before these native runs. ProductVersion is `0.3.4+e6142820946aa12f5d695832b76453d0b3a0edea`; EXE SHA-256 `e3113d6542f4967f1a4ca8e0d0b6f59659eeb0fac89ec0c57afa2811bf692ab2`, size 234453039 bytes. Release self-contained win-x64 publish with warnings as errors passed without warnings.

| Check | Result | Evidence |
| --- | --- | --- |
| Native input | 15/15 | [input.json](input.json) |
| Native tray / statistics timer | 13/13 | [tray.json](tray.json) |
| Native fullscreen | 8/8 | [fullscreen.json](fullscreen.json) |
| Native WinUI layout | 108 cases, no failures, 96 DPI | [layout.json](layout.json) |
| .NET protocol / mailbox / audio / UI stall | 39 PASS | [core-checks.txt](core-checks.txt) |

New input checks exercise 10 concurrent shutdown/publication cycles, accepted/rejected queries, continued native timer progress under repeated identical configuration, and preservation of the watcher under repeated identical settings. Existing tests cover 1000 queued probes, hook cleanup and edge gating. Input pump response during a 300 ms UI stall was 0.1609 ms. The mailbox check measured zero allocations while publishing 100000 coalesced positions.

The real fullscreen fixture recorded 37 ordinary edge samples in 1006.7326 ms and zero blocked samples in 1013.0445 ms. Neither interval performed repeated foreground inspections. Both thread CPU values were below the accounting resolution (reported 0 ms); this does not measure an exact CPU improvement or prove zero cost.

Ordinary EdgeLink was fully exited through its menu before diagnostics. Background-launched preliminary fullscreen attempts did not obtain foreground. The strengthened diagnostic rejected startup with `foregroundMatches=False`, `actualBlocked=False` and `foregroundFullscreen=False`. The two representative failed preliminary reports are retained in [excluded/](excluded/); they are not passes for the final build. Final fullscreen execution used Computer Use to activate the initial fixture once; subsequent fullscreen, focus, geometry, toggle and cleanup transitions were performed by the diagnostic itself. No mouse/keyboard events were injected by the diagnostic, no real transfer was attempted, and no sockets, pairing, audio playback or clipping were used.

The exact EXE was installed and reconnected automatically. Its live dashboard updated audio packets from 1012 to 2968 across close/restore; the underrun counter stayed at 1. A real second process exited with code 0 and restored the same ordinary process. The final close left that process alive with no visible main window, in the tray. Native UI actions used the Computer Use skill. This verifies session/UI continuity; the origin or audibility of the recorded underrun was not established.

## Limits and next step

The PC still has no synchronous mouse hook or Raw Input capture; capture is installed only during Mac control. The normal desktop uses the existing ordinary 16 ms edge timer, fullscreen protection stops it, and disconnected input does not poll. Network heartbeat, audio processing, the keyboard hook and reconnect handling remain required background behavior.

No fresh physical cursor, keyboard, audible headphone, game frame-time or sensor-to-display measurement was performed after this cleanup. The prior Dota 2 / 4000 Hz comparison applies to `bd1ffc0`, not automatically to this build. Verify this build's physical PC → Mac → PC transitions and game edge protection; perform a new identical on/off measurement if a new performance conclusion is needed. 8000 Hz and other DPI remain unverified. Mac-side code and the wire/audio algorithms were not changed. See [HANDOFF-WINDOWS.md](../../HANDOFF-WINDOWS.md) for coordination and retained history. No pairing keys, tokens or personal configuration were published.
