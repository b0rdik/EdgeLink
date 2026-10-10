from pathlib import Path
import subprocess, struct, sys

assets = Path(__file__).resolve().parent
subprocess.run(['swift', str(assets / 'generate-icons.swift'), str(assets)], check=True)
iconset = assets / 'EdgeLink.iconset'
iconset.mkdir(exist_ok=True)
for logical in (16, 32, 128, 256, 512):
    for scale in (1, 2):
        source = assets / f'icon-{logical * scale}.png'
        suffix = '@2x' if scale == 2 else ''
        (iconset / f'icon_{logical}x{logical}{suffix}.png').write_bytes(source.read_bytes())
subprocess.run(['iconutil', '-c', 'icns', str(iconset), '-o', str(assets / 'EdgeLink.icns')], check=True)
images = [(size, (assets / f'icon-{size}.png').read_bytes()) for size in (16, 24, 32, 48, 64, 128, 256)]
offset = 6 + 16 * len(images)
entries, blobs = [], []
for size, data in images:
    entries.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    blobs.append(data); offset += len(data)
(assets / 'EdgeLink.ico').write_bytes(struct.pack('<HHH', 0, 1, len(images)) + b''.join(entries + blobs))
print('Icons generated: ICNS, ICO, PNG')
