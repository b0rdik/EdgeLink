#!/usr/bin/env python3
"""Exercise the actual swap/rollback helper only on temporary dummy apps."""
import pathlib, plistlib, subprocess, tempfile, sys, shutil
script = pathlib.Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix='EdgeLink-installer-check-') as temp:
    root = pathlib.Path(temp)
    c = root / 'dummy.c'; c.write_text('int main(void) { return 0; }\n')
    def bundle(path, version):
        (path / 'Contents/MacOS').mkdir(parents=True)
        with (path / 'Contents/Info.plist').open('wb') as f:
            plistlib.dump({'CFBundleIdentifier':'local.edgelink.installer-check', 'CFBundleName':'EdgeLink Installer Check', 'CFBundleExecutable':'Dummy', 'CFBundlePackageType':'APPL', 'CFBundleVersion':version, 'CFBundleShortVersionString':version}, f)
        subprocess.run(['clang', '-arch', 'arm64', str(c), '-o', str(path/'Contents/MacOS/Dummy')], check=True)
        (path / 'Contents/Resources').mkdir()
        (path / 'Contents/Resources/version.txt').write_text(version)
        subprocess.run(['codesign', '--force', '--sign', '-', str(path)], check=True, capture_output=True)
    target, staged, backup, log = (root/x for x in ['Target.app','Staged.app','Previous.app','install.log'])
    bundle(target, '0.1.0'); bundle(staged, '0.2.0')
    result = subprocess.run(['bash', str(script), '2147483647', str(staged), str(target), str(backup), str(log)], timeout=10)
    assert result.returncode == 0, log.read_text()
    assert (target/'Contents/Resources/version.txt').read_text() == '0.2.0'
    assert (backup/'Contents/Resources/version.txt').read_text() == '0.1.0'
    print('PASS: Mac installer replaces dummy app and preserves previous version')
    (staged/'Contents/Resources/version.txt').write_text('tampered')
    result = subprocess.run(['bash', str(script), '2147483647', str(staged), str(target), str(root/'BadPrevious.app'), str(log)], timeout=10)
    assert result.returncode != 0
    assert (target/'Contents/Resources/version.txt').read_text() == '0.2.0'
    assert not (root/'BadPrevious.app').exists()
    assert not list(root.glob('*.incoming-*.app'))
    print('PASS: Mac installer rejects damaged signature and keeps installed app')
