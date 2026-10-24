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
parser.add_argument("--host", type=Path, help="optional retained Lain.Host.dll from the joint qualification")
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

joint_directory = ROOT / "tests/core-stabilization/evidence/host-core"
joint_path = joint_directory / "qualification.json"
joint = read(joint_path)
assert digest(joint_path) == joint_path.with_suffix(".json.sha256").read_text().strip()
assert joint["passed"] and joint["actualHostAndCoreProcesses"] and joint["syntheticOnly"]
assert joint["coreSha256"] == build["binarySha256"]
assert joint["coreSourceManifestSha256"] == build["sourceManifestSha256"]
raw_reports = {}
for name, reference in joint["sourceReports"].items():
    path = joint_directory / name
    assert digest(path) == reference["sha256"] == path.with_suffix(".json.sha256").read_text().strip()
    report = read(path)
    assert report["passed"] == reference["passed"]
    assert report["coreSha256"] == joint["coreSha256"]
    assert report["coreSourceManifestSha256"] == joint["coreSourceManifestSha256"]
    assert report["networkIsolation"] == joint["networkIsolation"]
    if report.get("actualHostAndCoreProcesses"):
        assert report["hostInputsUnchanged"] and report["hostInputSha256"] == joint["hostInputSha256"]
        assert all(check["evidence"]["hostVersion"].endswith("+" + joint["hostObservedBuildCommit"])
                   for check in report["checks"] if check["passed"])
    raw_reports[name] = report
assert {check["name"] for check in joint["checks"]} == {"legacy-repair", "interrupted", "valid-restored"}
assert len(joint["checks"]) == 3
for selected in joint["checks"]:
    assert selected["passed"]
    name = selected["sourceReport"]
    assert selected["sourceReportSha256"] == joint["sourceReports"][name]["sha256"]
    original = next(check for check in raw_reports[name]["checks"] if check["name"] == selected["name"])
    assert {key: value for key, value in selected.items() if key not in ("sourceReport", "sourceReportSha256")} == original
filesystem_reference = joint["actualFilesystemExhaustion"]
filesystem = raw_reports[filesystem_reference["sourceReport"]]
assert filesystem_reference["sourceReportSha256"] == joint["sourceReports"][filesystem_reference["sourceReport"]]["sha256"]
assert filesystem["passed"] and filesystem["actualKernelENOSPC"] and filesystem["binaryUnchanged"]
assert filesystem["evidence"]["actualErrno"] == 28 and filesystem["evidence"]["capacityBytes"] == 1048576
if args.host:
    assert args.host.is_absolute(), "explicit absolute Host DLL path required"
    assert args.host.name == "Lain.Host.dll", "explicit Lain.Host.dll required"
    assert all(digest(args.host.parent / name) == sha for name, sha in joint["hostInputSha256"].items()), "Host input differs from joint qualification"
print("Verified joint evidence chain: 3 actual Host/Core cases and private 1 MiB tmpfs ENOSPC")
