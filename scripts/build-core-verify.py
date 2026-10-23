#!/usr/bin/env python3
"""Verify the vendored build inputs against the checked-in source manifest."""
import hashlib
import json
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
manifest = json.loads((root / "proxy/source-manifest.json").read_text())
source = root / "proxy/core"
errors = []
for relative, expected in manifest["files"].items():
    path = source / relative
    if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
        errors.append(relative)
actual = {str(path.relative_to(source)) for path in source.rglob("*") if path.is_file()}
extra = actual - manifest["files"].keys()
if errors or extra:
    print("Core source verification failed: " + ", ".join(sorted(errors + list(extra))), file=sys.stderr)
    sys.exit(1)
print(f"Verified {len(actual)} pinned core build inputs")
