# Mac: restrained terminal UI, review build 1.0.2

Date: 2026-10-13, Asia/Dubai. Branch: `mac/terminal-ui`.
Source commit: `3760b6ea596317509d318279fe00218ea272a2b0`.

## Scope

Mac first, at the user's explicit request. Windows terminal styling and publishing
1.0.2 wait for the user's approval of this Mac design. The public release remains
1.0.1. Earlier Windows design work inherited from `mac/fluent-ui` is preserved,
but does not validate a Windows terminal design.

The Mac uses SwiftUI NavigationSplitView and sidebar List, a native compact
window toolbar, system colors, SF Symbols, bordered system buttons, switches,
pickers and menus. Monospaced device/status text and a restrained green accent
provide the terminal styling. Marketing cards and custom button rendering have
been removed. Animations respect Reduce Motion. Connection/audio/input and
permission mechanisms are unchanged by this commit.

## Programmatic validation

- `bash source/build-mac.sh`: PASS on native arm64 macOS.
- AppKit icon/resource audit: 11/11 PASS (`icons.txt`).
- Strict codesign validation: PASS; designated requirement matches the previously
  installed app, so the signing identity is preserved.
- Atomic replacement into `/Applications/EdgeLink.app`: PASS, no backup created,
  incoming staging bundle removed. Binary SHA-256 is in `install.json`.
- `git diff --check`: PASS.

## Native and user validation

Pending final native inspection: the Mac screen was locked before the rebuilt
compact-toolbar version could be opened. The installed application has been
replaced, but visual approval has not yet been obtained.

The isolated UI preview does not use pairing secrets, sockets, audio, input hooks,
permissions or persistent preferences. Its connected/permission states are mock
UI states, not hardware validation. Ethernet connectivity, physical cursor/key
transfers and audible quality are not retested for this UI-only change.
