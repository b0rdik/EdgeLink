# EdgeLink 1.0.2 — release validation

Date: 2026-10-13, Asia/Dubai. Native Windows build:
`0c0c635ac7f5fab6cf11044d6a1ae8e1a19b1a9a`.
[Final successful Windows CI](https://github.com/b0rdik/EdgeLink/actions/runs/38382420141).

## Result

The user approved the Mac design with native top navigation (no sidebar).
The style is now ported to native WinUI: compact laptop/monitor diagram, vertical
keyboard/mouse/audio status rows, Consolas status text, restrained green accent,
standard navigation/buttons/menus and system caption. Native light/dark resources
are resolved through ThemeResource probes attached to the window's visual tree;
manual application-dictionary lookups had left code-created colors in the old
appearance. The final dark and light snapshots were inspected and are readable.
Native glyphs now depict MacBook as a laptop and PC as a monitor.

## Checks

- Windows self-contained Release x64: PASS, warnings as errors, SDK 10.0.401.
- Native Windows input: 15/15; tray/lifecycle/statistics policy: 15/15.
- Native WinUI layout: 111 cases, 0 failures, 96 DPI. Includes explicit light/dark
  appearance switching and background/text contrast checks, four sizes and wizard
  stages, long values, rapid navigation and bottom-of-page scrolling.
- Windows PowerShell 5.1 / NTFS replacement: 5/5 scenarios.
- Windows protocol/queues/discovery/update receipts/UI-stall: 43 PASS.
- Mac Swift/.NET integration and lifecycle: 98 PASS. Discovery was explicitly
  skipped because the ordinary installed Mac app remained running.
- Mac build/signature/resources and native visual review: see
  `../mac-terminal-ui-2026-10-13/`. Installed binary SHA-256 is unchanged:
  `b316b1efaf4710b50c21381fdc9537385a6e538c0952e95d4605f9f8bd3f428b`.
  Its source is `8a04f4ed51f224a556931bc5110b07abfc9133f4`; Mac/assets/VERSION
  are identical in the Windows CI commit. Stable designated requirement retained.
- Downloaded EXE matches the CI SHA-256 and version; build metadata and full native
  reports/snapshots are under `windows/`.

The earlier CI at `5af8a43` passed size checks but its dark snapshot revealed mixed
light/dark colors. That candidate is excluded from release. Only the final
`0c0c635` executable is packaged.

## Limits / publication

These Windows native checks ran on GitHub's Windows server, not a new installation
on the user's Windows 11 25H2 machine. Physical transfers, sound quality, games,
4000/8000 Hz and other DPI were not retested for this UI release. Mac design was
approved by the user; the Windows port awaits their installed visual assessment.
No credentials, personal configuration or pairing files are in these reports.

Stable [v1.0.2](https://github.com/b0rdik/EdgeLink/releases/tag/v1.0.2) is published
as latest. PR #1 was merged; tag target is
`664bf2b005d00932ea43521ebf99224470b5eea7`. Main CI 38383116635 also passed;
application sources are identical to the packaged CI commit. Four assets match
local sizes and GitHub SHA-256 digests. The unauthenticated public latest endpoint
returns v1.0.2 with the exact download URLs required by both updaters.
Publication metadata is in `published-release.json`.
The updater keeps the existing no-backup replacement policy and preserves settings.
