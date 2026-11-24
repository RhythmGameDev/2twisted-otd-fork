#!/usr/bin/env bash
# The 2Twisted fork's Linux and macOS releases, into dist/ (the Windows one: eng/windows/package-2twisted.ps1).
#   bash eng/bash/package-2twisted.sh [suffix]          (default suffix 2twisted.1)
# - Linux: upstream's BinaryTarBall, opentabletdriver-<version>-<suffix>_linux-x64_binary.tar.gz
# - macOS: upstream's app bundle for Intel (osx-x64), OpenTabletDriver-<version>-<suffix>_osx-x64.tar.gz. Unsigned:
#   Apple Silicon runs it through Rosetta 2. An arm64 build needs signing (rcodesign or codesign), which upstream's
#   script does with `macos-signed` when one of them is installed.
# Built on Windows (Git Bash) the tarballs lose the executable bit, so fix-tarball-modes.py rewrites them. On
# Windows, upstream's scripts also want `jq` and `rev`; put stand-ins for them on PATH if they are missing.
set -eu
suffix="${1:-2twisted.1}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
mkdir -p dist

rm -rf out-linux out-macos
VERSION_SUFFIX="-${suffix}" bash ./build.sh --output out-linux --runtime linux-x64 --package BinaryTarBall
VERSION_SUFFIX="-${suffix}" bash ./build.sh --output out-macos --runtime osx-x64

for t in out-linux/*.tar.gz out-macos/*.tar.gz; do
  python eng/bash/fix-tarball-modes.py "$t" > /dev/null
  cp "$t" dist/
  echo "Release: dist/$(basename "$t")"
done
