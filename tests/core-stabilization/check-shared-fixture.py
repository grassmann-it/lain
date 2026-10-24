#!/usr/bin/env python3
"""Check that independently prepared adapter tests use the shared ST-01 matrix."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
shared = root / "tests/model-compatibility/fixtures/dispatch-matrix.json"
prepared_copy = root / "proxy/adapter/internal/lain/testdata/dispatch-matrix.json"
assert json.loads(shared.read_text()) == json.loads(prepared_copy.read_text()), "Core/Host dispatch matrix drift"
print("Core/Host dispatch matrices match")
