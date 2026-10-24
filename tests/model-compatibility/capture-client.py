#!/usr/bin/env python3
"""Version-qualified disposable Codex tool capture; synthetic loopback only."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--codex", required=True)
parser.add_argument("--temporary-root", required=True)
parser.add_argument("--report", required=True)
args = parser.parse_args()
binary = Path(args.codex).resolve(strict=True)
root = Path(args.temporary_root).resolve()
root.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix="codex-version-", dir=root) as directory:
    home = Path(directory)
    environment = {"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "LANG": "C.UTF-8", "HOME": str(home), "CODEX_HOME": str(home), "TMPDIR": str(home)}
    version = subprocess.run([str(binary), "--version"], cwd=home, env=environment,
                             capture_output=True, text=True, timeout=10, check=True).stdout.strip()
if version != "codex-cli 0.164.0":
    raise SystemExit("Unqualified client version: " + version)
source = Path(__file__).resolve().parent.parent / "codex-gateway-profile" / "capture_tools.py"
spec = importlib.util.spec_from_file_location("capture_tools", source)
capture_tools = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture_tools)
cases = [capture_tools.capture(str(binary), root, patch) for patch in ("freeform", None)]
cases.extend([capture_tools.capture(str(binary), root, None, omit_apply_patch=True),
              capture_tools.capture(str(binary), root, None, multi_agent=False)])
report = {"contractVersion": 1, "observedAt": datetime.now(timezone.utc).isoformat(), "client": "codex", "clientVersion": version,
          "resolvedExecutableSha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
          "qualification": "local_fixture", "syntheticOnly": True,
          "personalStateRead": False, "providerInference": False, "cases": cases}
Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
expected = [(9, {"custom", "namespace"}), (8, {"namespace"}), (8, {"namespace"}), (7, set())]
for case, (count, types) in zip(cases, expected):
    if case["exitCode"] or len(case["requests"]) != 1:
        raise SystemExit("Client fixture failed; inspect sanitized report")
    request = case["requests"][0]
    observed = {tool["type"] for tool in request["tools"] if tool["type"] != "function"}
    if request["count"] != count or observed != types:
        raise SystemExit("Client tool metadata changed; qualification must be reviewed")
print(json.dumps({"clientVersion": version, "cases": len(cases), "toolCounts": [case["requests"][0]["count"] for case in cases], "syntheticOnly": True}))
