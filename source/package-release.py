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
# Inspect the actual RT_VERSION tree, not a stale signature elsewhere in the apphost.
sections=[]; optional=pe+24; optional_size=struct.unpack_from('<H',raw,pe+20)[0]
for i in range(struct.unpack_from('<H',raw,pe+6)[0]):
    o=optional+optional_size+i*40
    sections.append((struct.unpack_from('<I',raw,o+12)[0],max(struct.unpack_from('<I',raw,o+8)[0],struct.unpack_from('<I',raw,o+16)[0]),struct.unpack_from('<I',raw,o+20)[0]))
def offset(rva):
    for address,size,position in sections:
        if address <= rva < address+size: return position+rva-address
    raise ValueError('Invalid resource RVA')
resource=offset(struct.unpack_from('<I',raw,optional+112+16)[0]); versions=[]
def walk(relative, path):
    directory=resource+relative; named, numbered=struct.unpack_from('<HH',raw,directory+12)
    for i in range(named+numbered):
        identity, entry=struct.unpack_from('<II',raw,directory+16+i*8)
        if entry & 0x80000000: walk(entry & 0x7fffffff,path+[identity])
        elif path and path[0] == 16:
            rva,length=struct.unpack_from('<II',raw,resource+entry); p=offset(rva); data=raw[p:p+length]
            signature=data.find(bytes.fromhex('bd04effe')); assert signature>=0
            versions.append(struct.unpack_from('<II',data,signature+8))
walk(0,[])
assert versions and all(v == expected for v in versions), 'Rebuild Windows / native RT_VERSION differs'
out=root/'releases'; out.mkdir(exist_ok=True)
for name, files in [('EdgeLink-macos-arm64.zip',[(p,p.relative_to(app.parent).as_posix()) for p in sorted(app.rglob('*')) if p.is_file()]),('EdgeLink-windows-x64.zip',[(exe,'EdgeLink.exe')])]:
    with zipfile.ZipFile(out/name,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
        for path, entry in files:
            assert not path.is_symlink(), 'Symlinks unsupported by updater'
            archive.write(path,entry)
        if 'windows' in name:
            archive.write(root/'README-RU.md','README-RU.md')
            archive.write(root/'source/assets/EdgeLink.ico','EdgeLink.ico')
            for p in sorted((root/'licenses').glob('*.txt')): archive.write(p,'licenses/'+p.name)
    with zipfile.ZipFile(out/name) as archive: assert archive.testzip() is None
    print(name, (out/name).stat().st_size)
lines=[hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name for p in sorted(out.glob('EdgeLink-*.zip'))]
(out/'SHA256.txt').write_text('\n'.join(lines)+'\n')
