"""Run exported production helper with Windows PowerShell 5.1 and NTFS fixtures."""
import ctypes
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import uuid

fixture, helper, report = map(Path, sys.argv[1:4])
results = []
with tempfile.TemporaryDirectory(prefix="EdgeLink-installer-check-") as root:
    base = Path(root)
    for mode in [b"NEW!", b"HANG", b"corrupt", b"locked"]:
        case = base / mode.decode().replace("!", "")
        case.mkdir()
        target = case / "EdgeLink.exe"
        target.write_bytes(fixture.read_bytes() + b"OLD!")
        old_hash = hashlib.sha256(target.read_bytes()).hexdigest()
        folder = Path(tempfile.gettempdir()) / ("EdgeLink-update-" + str(uuid.uuid4()))
        folder.mkdir()
        lock = None
        try:
            staged = folder / "EdgeLink.exe"
            content = fixture.read_bytes() + (b"NEW!" if mode == b"locked" else mode)
            staged.write_bytes(content)
            expected_hash = hashlib.sha256(content).hexdigest()
            if mode == b"corrupt":
                staged.write_bytes(b"invalid executable")
            script = helper.read_text().replace(".AddSeconds(30)", ".AddSeconds(5)")
            # Only suppress failure dialogs/relaunch and redirect the log; leave
            # copy/hash/replace/receipt/kill/cleanup code intact.
            script = script.replace("Add-Type -AssemblyName System.Windows.Forms", "# isolated test: no dialog")
            script = script.replace("[Windows.Forms.MessageBox]::Show('EdgeLink update failed. ' + $failure, 'EdgeLink') | Out-Null", "Write-Output $failure")
            script = script.replace("if (-not $committed -and [IO.File]::Exists($Target)) { Start-Process -FilePath $Target }", "# isolated test: no relaunch")
            log = str(case / "update.log").replace("'", "''")
            script = script.replace("$log = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EdgeLink/update.log'", "$log = '" + log + "'")
            install = folder / "install.ps1"
            install.write_text(script, encoding="utf-8")
            if mode == b"locked":
                api = ctypes.WinDLL("kernel32", use_last_error=True)
                api.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
                api.CreateFileW.restype = ctypes.c_void_p
                api.CloseHandle.argtypes = [ctypes.c_void_p]
                lock = api.CreateFileW(str(target), 0x80000000, 1, None, 3, 0, None)
                assert lock not in (None, ctypes.c_void_p(-1).value), ctypes.get_last_error()
            result = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(install), "-ParentId", "9999999", "-Target", str(target), "-Staged", str(staged), "-ExpectedHash", expected_hash], timeout=30, capture_output=True)
            assert result.returncode == (0 if mode == b"NEW!" else 1), (result.stdout, result.stderr)
            expected = b"OLD!" if mode in (b"corrupt", b"locked") else mode
            assert target.read_bytes().endswith(expected), (case / "update.log").read_text()
            if expected == b"OLD!":
                assert hashlib.sha256(target.read_bytes()).hexdigest() == old_hash
            assert not folder.exists(), "staging left behind"
            assert not list(case.glob("*.previous*")) and not list(case.glob("*.incoming*")), "reserve left behind"
            results.append({"scenario": mode.decode(), "passed": True})
        finally:
            if lock is not None: api.CloseHandle(lock)
            shutil.rmtree(folder, ignore_errors=True)
    case = base / "legacy"
    case.mkdir()
    target = case / "EdgeLink.exe"
    old = case / "EdgeLink.exe.previous-20261013T120000123.exe"
    unrelated = case / "EdgeLink.exe.previous-personal.exe"
    for path in (target, old, unrelated): shutil.copy2(fixture, path)
    subprocess.run([str(fixture), "--clean-legacy", str(target)], check=True, timeout=10)
    assert target.exists() and unrelated.exists() and not old.exists()
    results.append({"scenario": "legacy backup cleanup without update arguments", "passed": True})
report.write_text(json.dumps({"platform": "Windows PowerShell 5.1 / NTFS", "passed": True, "results": results}, indent=2) + "\n")
print("PASS: 5 native Windows installer/legacy cleanup scenarios; no backup created")
