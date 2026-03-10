#!/usr/bin/env python3
"""Generate C# asset files from binary files in Assets/."""

import os
import sys

ASSETS_DIR = os.path.join(os.path.dirname(__file__), "Assets")
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "MU3.Mod.Assets")
NAMESPACE = "MU3.Mod.Assets"
BYTES_PER_LINE = 16


def to_csharp_bytes(data: bytes) -> str:
    if not data:
        return "[]"
    chunks = [
        ", ".join(f"0x{b:02X}" for b in data[i : i + BYTES_PER_LINE])
        for i in range(0, len(data), BYTES_PER_LINE)
    ]
    inner = ",\n        ".join(chunks)
    return f"[\n        {inner}\n    ]"


def generate(asset_path: str, output_path: str, class_name: str) -> None:
    with open(asset_path, "rb") as f:
        data = f.read()

    content = (
        f"namespace {NAMESPACE};\n"
        f"\n"
        f"public static class {class_name}\n"
        f"{{\n"
        f"    public static readonly byte[] Data = {to_csharp_bytes(data)};\n"
        f"}}\n"
    )

    with open(output_path, "w", newline="\n") as f:
        f.write(content)

    print(f"  {class_name} ({len(data):,} bytes) -> {os.path.basename(output_path)}")


def main() -> None:
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    entries = [
        e for e in os.scandir(ASSETS_DIR) if e.is_file()
    ]

    if not entries:
        print(f"No files found in {ASSETS_DIR}")
        sys.exit(1)

    print(f"Generating {len(entries)} asset(s)...")
    for entry in sorted(entries, key=lambda e: e.name):
        class_name = os.path.splitext(entry.name)[0]
        output_path = os.path.join(OUTPUT_DIR, f"{class_name}.cs")
        generate(entry.path, output_path, class_name)

    print("Done.")


if __name__ == "__main__":
    main()
