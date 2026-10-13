# Mac: restrained terminal UI, review build 1.0.2

Date: 2026-10-13, Asia/Dubai. Branch: `mac/terminal-ui`.
Source commit: `8a04f4ed51f224a556931bc5110b07abfc9133f4`.

## Scope

Mac first, at the user's explicit request. Windows terminal styling and publishing
1.0.2 wait for the user's approval of this Mac design. The public release remains
1.0.1. Earlier Windows design work inherited from `mac/fluent-ui` is preserved,
but does not validate a Windows terminal design.

After native inspection the user rejected the left sidebar. The final Mac layout
uses a native segmented Picker at the top for Connection, Setup and Preferences,
a standard AppKit title bar, system colors, SF Symbols, bordered system buttons,
switches, pickers and menus. Monospaced device/status text and a restrained green
accent provide the terminal styling. Marketing cards and custom button rendering
have been removed. Animations respect Reduce Motion. Connection/audio/input and
permission mechanisms are unchanged by this UI revision.

## Programmatic validation

- `bash source/build-mac.sh`: PASS on native arm64 macOS.
- AppKit icon/resource audit: 11/11 PASS (`icons.txt`).
- Strict codesign validation: PASS; designated requirement matches the previously
  installed app, so the signing identity is preserved.
- Atomic replacement into `/Applications/EdgeLink.app`: PASS, no backup created,
  incoming staging bundle removed. Binary SHA-256 is in `install.json`.
- `git diff --check`: PASS.

## Native validation

- Final isolated preview opened natively at 680×620 content size. The three
  top segments switch pages correctly. Sidebar and its toolbar are absent.
- Dark and light home pages inspected; native controls, device symbols and status
  text are visible without overlap (`home-dark.png`, `home-light.png`).
- Setup permission action immediately shows a checkmark and enables Continue
  in the isolated mock UI (`setup-dark.png` captures the initial permission state).
- Connected mock state updates the connection button and keyboard/mouse/audio
  statuses. No real connection is established by preview mode.
- Preferences scroll to the bottom; permission rows and Check for updates remain
  reachable (`preferences-light.png`).
- Installed production app opened normally at its default size. It shows version
  1.0.2, the top segmented control, existing Keep Awake preference and actual
  disconnected state (`installed-home.png`). No permission warning is displayed.
- Application left open on Connection for user review. Preview terminated and
  temporary app bundles removed. Spotlight finds only `/Applications/EdgeLink.app`.

## Limits / next step

Visual approval of the final top-navigation design has not yet been obtained.
The isolated UI preview does not use pairing secrets, sockets, audio, input hooks,
permissions or persistent preferences. Its connected/permission states are mock
UI states, not hardware validation. Ethernet connectivity, physical cursor/key
transfers and audible quality are not retested for this UI-only change.

Wait for the user's approval of Mac before porting this style to Windows or
publishing/merging a release. No public 1.0.2 release has been created.
