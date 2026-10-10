#!/bin/bash
set -euo pipefail
SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
DOTNET_CMD="${DOTNET_CMD:-dotnet}"
SCRATCH_DIR="$(mktemp -d)"
trap 'rm -rf "$SCRATCH_DIR"' EXIT
cp "$SOURCE_DIR/checks/server.swift" "$SCRATCH_DIR/main.swift"
swiftc -swift-version 5 "$SOURCE_DIR/mac/Wire.swift" "$SOURCE_DIR/mac/Input.swift" "$SOURCE_DIR/mac/Link.swift" "$SOURCE_DIR/mac/PCM.swift" "$SOURCE_DIR/mac/OutputMute.swift" "$SOURCE_DIR/mac/Discovery.swift" "$SOURCE_DIR/mac/Updates.swift" "$SCRATCH_DIR/main.swift" -o "$SCRATCH_DIR/server"
"$DOTNET_CMD" build "$SOURCE_DIR/checks/Checks.csproj" -c Release --artifacts-path "$SCRATCH_DIR/artifacts" -o "$SCRATCH_DIR/client"
export SCRATCH_DIR DOTNET_CMD
python3 - <<'PY'
import os, pathlib, subprocess
scratch = pathlib.Path(os.environ['SCRATCH_DIR'])
server = subprocess.Popen([str(scratch / 'server')], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
try:
    while True:
        line = server.stdout.readline()
        if not line: raise RuntimeError('Swift server did not start')
        print(line, end='')
        if line.strip() == 'READY': break
    client = subprocess.run([os.environ['DOTNET_CMD'], str(scratch / 'client/Checks.dll'), '--integration'], timeout=10)
    rest, _ = server.communicate(timeout=10)
    print(rest, end='')
    if client.returncode or server.returncode or 'FAIL:' in rest: raise SystemExit(1)
finally:
    if server.poll() is None: server.terminate()
PY
