"""Passive foreground geometry sampler used alongside the clean mouse observer.

Usage: python foreground-sampler.py foreground-clean.json
No activation, injection, input hooks, titles or process/device identifiers.
Primary-monitor rule, tested at 100% DPI on the user's single Windows monitor.
"""
import ctypes
import json
import pathlib
import sys
import time
from ctypes import wintypes as w

u = ctypes.WinDLL("user32", use_last_error=True)
u.SetProcessDPIAware()
u.GetForegroundWindow.restype = w.HWND
u.GetClientRect.argtypes = [w.HWND, ctypes.POINTER(w.RECT)]
u.ClientToScreen.argtypes = [w.HWND, ctypes.POINTER(w.POINT)]
u.IsWindowVisible.argtypes = [w.HWND]
u.IsIconic.argtypes = [w.HWND]
width, height = u.GetSystemMetrics(0), u.GetSystemMetrics(1)
time.sleep(3)
start = time.monotonic()
covered = ordinary = unavailable = changes = 0
previous = None
while time.monotonic() - start < 20:
    hwnd = u.GetForegroundWindow()
    rect, point = w.RECT(), w.POINT()
    valid = bool(hwnd and u.IsWindowVisible(hwnd) and not u.IsIconic(hwnd)
                 and u.GetClientRect(hwnd, ctypes.byref(rect))
                 and u.ClientToScreen(hwnd, ctypes.byref(point)))
    if valid:
        full = (point.x <= 1 and point.y <= 1
                and point.x + rect.right - rect.left >= width - 1
                and point.y + rect.bottom - rect.top >= height - 1)
        if full:
            covered += 1
        else:
            ordinary += 1
    else:
        unavailable += 1
    if previous is not None and previous != hwnd:
        changes += 1
    previous = hwnd
    time.sleep(.25)
result = {
    "observedSeconds": time.monotonic() - start,
    "warmupSeconds": 3, "sampleIntervalMs": 250,
    "fullscreenClientSamples": covered, "ordinaryClientSamples": ordinary,
    "unavailableSamples": unavailable, "foregroundWindowChanges": changes,
    "rule": "foreground client covers primary Windows monitor; passive geometry samples, not inspection of EdgeLink internal state",
    "note": "Independent sampler runs alongside the same observer; sampling boundaries differ slightly between processes. No titles, process names, coordinates, keys or device identifiers retained. Its CPU overhead was not separately measured.",
}
path = pathlib.Path(sys.argv[1])
path.parent.mkdir(parents=True, exist_ok=True)
path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result))
