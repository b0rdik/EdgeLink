"""Exercise exported no-backup helpers with disposable headless executables.

Usage: python3 source/checks/check-installers.py SCRATCH_NOINDEX FIXTURE_BINARY PWSH
SCRATCH contains install-mac.sh and install-windows.ps1 exported by run-checks.sh.
The fixture acknowledges --update-ready only in NEW! mode and sleeps in HANG mode.
Mac fixture reads Contents/Resources/mode.txt; Windows fixture reads a 4-byte trailer.
PowerShell on Mac validates transactions/syntax, not native Windows file locking/UI.
"""
import hashlib
import json
import os
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import uuid

base, fixture, pwsh = map(Path, sys.argv[1:4])
results = []
run = base / ("transactions-" + str(uuid.uuid4()))
run.mkdir()


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def bundle(path, mode):
    (path / "Contents/MacOS").mkdir(parents=True)
    (path / "Contents/Resources").mkdir()
    shutil.copy2(fixture, path / "Contents/MacOS/EdgeLink")
    (path / "Contents/Resources/mode.txt").write_text(mode)
    (path / "Contents/Info.plist").write_bytes(plistlib.dumps({
        "CFBundleIdentifier": "test.edgelink.installer", "CFBundleExecutable": "EdgeLink",
        "CFBundlePackageType": "APPL", "CFBundleShortVersionString": "1.0.1", "CFBundleVersion": "1.0.1"}))
    subprocess.run(["codesign", "--force", "--sign", "-", str(path)], check=True, capture_output=True)


for mode in ["NEW!", "HANG", "corrupt"]:
    case = run / ("mac " + mode.replace("!", ""))
    case.mkdir(exist_ok=True)
    target = case / "EdgeLink.app"
    bundle(target, "OLD!")
    old_hash = digest(target / "Contents/MacOS/EdgeLink")
    folder = Path(tempfile.gettempdir()) / ("EdgeLink-update-" + str(uuid.uuid4()))
    folder.mkdir(mode=0o700)
    try:
        staged = folder / "unpacked/EdgeLink.app"
        bundle(staged, mode)
        if mode == "corrupt":
            (staged / "Contents/Resources/mode.txt").write_text("altered after signing")
        nonce = str(uuid.uuid4())
        ready = folder / ("ready-" + nonce + ".txt")
        # Limit timeout to 1.5s and replace rollback's GUI relaunch. Production
        # copy, codesign, replace, receipt, kill and cleanup remain intact.
        script = (base / "install-mac.sh").read_text().replace("seq 1 300", "seq 1 15").replace('/usr/bin/open "$target"', '/usr/bin/true "$target"')
        helper = folder / "install.sh"
        helper.write_text(script)
        log = case / "update.log"
        proc = subprocess.run(["bash", str(helper), "999999", str(staged), str(target), str(log), str(ready), nonce, str(folder)], timeout=15)
        actual_mode = (target / "Contents/Resources/mode.txt").read_text()
        assert actual_mode == ("OLD!" if mode == "corrupt" else mode), log.read_text()
        assert proc.returncode == (0 if mode == "NEW!" else 1), log.read_text()
        if mode == "corrupt":
            assert digest(target / "Contents/MacOS/EdgeLink") == old_hash
        assert not folder.exists(), "staging/rollback files left after transaction"
        assert not list(case.glob("*.previous*")) and not list(case.glob("*.incoming*"))
        results.append({"platform": "Mac", "scenario": mode, "passed": True})
    finally:
        if folder.exists():
            shutil.rmtree(folder)

for mode in [b"NEW!", b"HANG", b"corrupt"]:
    case = run / ("windows " + mode.decode().replace("!", ""))
    case.mkdir(exist_ok=True)
    target = case / "EdgeLink.exe"
    target.write_bytes(fixture.read_bytes() + b"OLD!")
    target.chmod(0o755)
    old_hash = digest(target)
    folder = Path(tempfile.gettempdir()) / ("EdgeLink-update-" + str(uuid.uuid4()))
    folder.mkdir(mode=0o700)
    try:
        staged = folder / "EdgeLink.exe"
        staged.write_bytes(b"invalid executable" if mode == b"corrupt" else fixture.read_bytes() + mode)
        staged.chmod(0o755)
        helper = folder / "install.ps1"
        # Redirect only the test log and shorten the readiness timeout.
        script = (base / "install-windows.ps1").read_text().replace(".AddSeconds(30)", ".AddSeconds(1)")
        script = script.replace("$log = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'EdgeLink/update.log'", "$log = '" + str(case / "update.log").replace("'", "''") + "'")
        helper.write_text(script)
        expected_hash = hashlib.sha256(fixture.read_bytes() + b"NEW!").hexdigest() if mode == b"corrupt" else digest(staged)
        result=subprocess.run([str(pwsh), "-NoLogo", "-NoProfile", "-File", str(helper), "-ParentId", "999999", "-Target", str(target), "-Staged", str(staged), "-ExpectedHash", expected_hash], timeout=15, capture_output=True)
        assert result.returncode == (0 if mode == b"NEW!" else 1), result.stderr.decode()
        assert target.read_bytes().endswith(b"OLD!" if mode == b"corrupt" else mode), (case / "update.log").read_text()
        if mode == b"corrupt":
            assert digest(target) == old_hash
        assert not folder.exists(), "staging/rollback files left after transaction"
        assert not list(case.glob("*.previous*")) and not list(case.glob("*.incoming*"))
        results.append({"platform": "PowerShell on Mac", "scenario": mode.decode(), "passed": True})
    finally:
        if folder.exists():
            shutil.rmtree(folder)

(base / "installer-checks.json").write_text(json.dumps({"passed": True, "results": results}, indent=2) + "\n")
print("PASS: 6 updater atomic replacement/validation/startup/cleanup scenarios; no backup or production app launched")
