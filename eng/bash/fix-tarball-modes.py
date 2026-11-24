#!/usr/bin/env python3
# Rewrite a release tarball made on Windows so it unpacks right on Linux and macOS (2Twisted fork).
# Windows' filesystem keeps no executable bit, so tar records the published programs as 0644 and the launchers in
# bin/ could not start them. Every member is copied as is, except: ELF/Mach-O executables and scripts with a "#!"
# line become 0755, directories 0755, everything else 0644, and the owner is root:root (uid/gid 0) as in the
# official tarballs.
#   python eng/bash/fix-tarball-modes.py <file.tar.gz>
import io
import sys
import tarfile

MAGICS = (b"\x7fELF", b"\xcf\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"#!")


def main(path):
    with tarfile.open(path, "r:gz") as src:
        members = [(m, src.extractfile(m).read() if m.isfile() else None) for m in src.getmembers()]
    out = io.BytesIO()
    executables = []
    with tarfile.open(fileobj=out, mode="w:gz") as dst:
        for m, data in members:
            m.uid = m.gid = 0
            m.uname = m.gname = "root"
            if m.isdir():
                m.mode = 0o755
                dst.addfile(m)
            elif m.isfile():
                if data[:4].startswith(MAGICS) or data[:2] == b"#!":
                    m.mode = 0o755
                    executables.append(m.name)
                else:
                    m.mode = 0o644
                dst.addfile(m, io.BytesIO(data))
            else:
                dst.addfile(m)
    with open(path, "wb") as f:
        f.write(out.getvalue())
    for name in executables:
        print("0755", name)


if __name__ == "__main__":
    main(sys.argv[1])
