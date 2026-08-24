#!/usr/bin/env python3
from pathlib import Path

MONO_PATH = Path("/opt/unity/Editor/Data/Mono/mono.dll")
PATCHES = (
    (0x14F91C, bytes.fromhex("48 87 75 f5"), bytes.fromhex("48 89 75 f5")),
    (0x14FA30, bytes.fromhex("87 75 fc"), bytes.fromhex("89 75 fc")),
    (0x14FA4F, bytes.fromhex("48 87 34 28"), bytes.fromhex("48 89 34 28")),
)


def patch(mono_path: Path) -> bool:

    data = bytearray(mono_path.read_bytes())
    changed = False
    for offset, expected, replacement in PATCHES:
        actual = bytes(data[offset : offset + len(expected)])
        if actual == replacement:
            continue
        if actual != expected:
            raise RuntimeError(f"unexpected mono.dll bytes at 0x{offset:x}")
        data[offset : offset + len(replacement)] = replacement
        changed = True

    if changed:
        mono_path.write_bytes(data)
    return changed

def main() -> None:
    try:
        if patch(MONO_PATH):
            print("Patched Unity Mono atomics for Rosetta")
    except (OSError, RuntimeError) as error:
        raise SystemExit(f"error: {error}")


if __name__ == "__main__":
    main()
