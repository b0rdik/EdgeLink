#!/usr/bin/env python3
"""Package already built binaries for the version in VERSION. Run on macOS."""
import pathlib, plistlib, re, zipfile, hashlib, struct, subprocess
root = pathlib.Path(__file__).resolve().parent.parent
version = (root/'VERSION').read_text().strip()
assert re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version), 'Invalid VERSION'
app = root/'Mac/EdgeLink.app'; exe = root/'Windows/EdgeLink.exe'
with (app/'Contents/Info.plist').open('rb') as f: info = plistlib.load(f)
assert info['CFBundleShortVersionString'] == version, 'Rebuild Mac'
subprocess.run(['codesign','--verify','--deep','--strict',str(app)],check=True)
raw=exe.read_bytes(); assert raw[:2] == b'MZ'
pe=struct.unpack_from('<I',raw,60)[0]; assert raw[pe:pe+6] == b'PE\0\0\x64\x86'
major, minor, patch = map(int, version.split('.'))
expected = ((major<<16)|minor, (patch<<16))
signature=bytes.fromhex('bd04effe'); positions=[m.start() for m in re.finditer(re.escape(signature),raw)]
assert any(struct.unpack_from('<II',raw,p+8) == expected for p in positions if p+16 <= len(raw)), 'Rebuild Windows / FileVersion differs'
out=root/'releases'; out.mkdir(exist_ok=True)
for name, files in [('EdgeLink-macos-arm64.zip',[(p,p.relative_to(app.parent).as_posix()) for p in sorted(app.rglob('*')) if p.is_file()]),('EdgeLink-windows-x64.zip',[(exe,'EdgeLink.exe')])]:
    with zipfile.ZipFile(out/name,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
        for path, entry in files:
            assert not path.is_symlink(), 'Symlinks unsupported by updater'
            archive.write(path,entry)
        if 'windows' in name:
            archive.write(root/'README-RU.md','README-RU.md')
            for p in sorted((root/'licenses').glob('*.txt')): archive.write(p,'licenses/'+p.name)
    with zipfile.ZipFile(out/name) as archive: assert archive.testzip() is None
    print(name, (out/name).stat().st_size)
lines=[hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name for p in sorted(out.glob('EdgeLink-*.zip'))]
(out/'SHA256.txt').write_text('\n'.join(lines)+'\n')
