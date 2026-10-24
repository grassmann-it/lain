#!/usr/bin/env python3
"""Verify the current adapter3 source/build/process evidence chain without running Core."""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / "tests/core-stabilization/evidence/adapter3"
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
read = lambda path: json.loads(path.read_text())

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--core", type=Path)
args = parser.parse_args()
source = read(ROOT / "proxy/source-manifest.json")
qualification = read(ROOT / "proxy/qualification.json")
build = read(EVIDENCE / "build-manifest.json")
assert qualification["passed"] and qualification["syntheticPassed"]
assert digest(ROOT / "proxy/source-manifest.json") == qualification["sourceManifestSha256"] == build["sourceManifestSha256"]
assert qualification["sha256"] == build["binarySha256"]
assert source["lainPatchVersion"] == build["lainPatchVersion"] == 3
assert source["patchSetSha256"] == build["patchSetSha256"] and len(source["patches"]) == 2
for patch in source["patches"]:
    assert digest(ROOT / "proxy" / patch["path"]) == patch["sha256"]
for name in ("version", "commit", "archiveSha256"):
    assert source["upstreamDependency"][name] == build["upstream" + name[0].upper() + name[1:]]
checks = 0
for name in ("process.json", "backend.json", "auth-crash.json", "native-tools.json", "inspector.json"):
    report = read(EVIDENCE / name)
    assert report["passed"] and report["coreSha256"] == build["binarySha256"]
    assert all(check["passed"] for check in report["checks"])
    checks += len(report["checks"])
assert checks == qualification["actualProcessChecks"] == 40
process = read(EVIDENCE / "process.json")
assert process["sourceManifestSha256"] == build["sourceManifestSha256"]
assert process["networkIsolation"] == "private_network_namespace_loopback_only"
unit = read(EVIDENCE / "adapter-unit.json")
assert unit["passed"] and unit["sourceManifestSha256"] == build["sourceManifestSha256"]
assert all((ROOT / name).is_file() for name in qualification["evidence"])
if args.core:
    assert args.core.is_absolute(), "explicit absolute Core path required"
    assert digest(args.core) == build["binarySha256"], "binary differs from qualification"
print(f"Verified adapter3 evidence chain: {checks} actual process checks, {unit['passedTopLevelTests']} top-level Go tests")
