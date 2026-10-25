#!/usr/bin/env python3
"""Run existing binary gates and ten real SQLite replays, without building anything."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def validate_consumer(result, batch):
    expected = {"requests": 200, "attempts": 200, "errors": 0, "unknownUsageAttempts": 0}
    if result.get("cursor") != batch["highWatermark"] or result.get("complete") is not True:
        raise AssertionError("consumer cursor/complete does not match full committed batch")
    totals = result["totals"]
    if any(totals.get(key) != value for key, value in expected.items()):
        raise AssertionError("consumer request/attempt/error aggregates changed after replay")
    if totals.get("tokens") != {"input": 2200, "output": 1400, "cached": None, "reasoning": None, "total": 3600, "cacheCreation": None}:
        raise AssertionError("consumer token aggregates changed or unknown quantities became zero")
    if result.get("accounts") != 2 or result.get("models") != 1:
        raise AssertionError("consumer account/model attribution changed")
    if len(batch["events"]) != 600 or batch["highWatermark"] != 600:
        raise AssertionError("batch lacks required 200 start/usage/finalization facts")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True, help="already built single-file launcher")
    parser.add_argument("--consumer", required=True, help="already built Lain.Usage.Tests.dll (adjacent deps/runtimeconfig required)")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--host-exporter", help="optional already built HostCoreContract.Tests.dll")
    parser.add_argument("--baseline-core", help="optional old binary for synthetic journal upgrade/rollback continuation")
    parser.add_argument("--output", required=True, help="new or empty report directory")
    args = parser.parse_args()
    core = Path(args.core).resolve(strict=True)
    consumer = Path(args.consumer).resolve(strict=True)
    output = Path(args.output).resolve()
    output.mkdir(mode=0o700, parents=True, exist_ok=True)
    if any(output.iterdir()):
        parser.error("--output must be empty; previous qualification evidence is never overwritten")
    source = Path(__file__).resolve().parents[2]
    data = core.read_bytes()
    core_hash = hashlib.sha256(data).hexdigest()
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": core_hash,
              "scope": "single-file launcher binary boundaries plus real SQLite consumer replay",
              "checks": [], "passed": False}
    # Freeze once across all independent scripts. None may observe a rebuilt path.
    with tempfile.TemporaryDirectory(prefix="lain-upgrade-gate-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(data)
        frozen.chmod(0o700)
        batch_path = output / "source-batch.json"
        steps = [
            ("protocol-durability", "backend/qualify.py", ["--examples", str(output / "event-examples.json"), "--batch-fixture", str(batch_path)]),
            ("independent-auth-crash", "acceptance/core_probe.py", []),
            ("inspector", "inspection/core_probe.py", []),
            ("upgrade-boundaries", "backend/upgrade_boundaries.py", []),
            ("native-tool-boundary", "backend/native_tool_boundary.py", []),
        ]
        if args.host_exporter:
            steps.append(("hostwriter-transports", "backend/host_contract_probe.py",
                          ["--exporter", str(Path(args.host_exporter).resolve(strict=True)), "--dotnet", args.dotnet]))
        if args.baseline_core:
            steps.append(("version-transition", "backend/version_transition_probe.py",
                          ["--baseline-core", str(Path(args.baseline_core).resolve(strict=True)),
                           "--consumer", str(consumer), "--dotnet", args.dotnet,
                           "--batch-fixture", str(output / "mixed-source-batch.json")]))
        for name, script, extra in steps:
            path = output / (name + ".json")
            log = output / (name + ".log")
            with log.open("wb") as handle:
                result = subprocess.run([sys.executable, str(source / "tests" / script), "--core", str(frozen),
                                         "--report", str(path), *extra], stdout=handle, stderr=subprocess.STDOUT)
            try:
                evidence = json.loads(path.read_text())
                passed = result.returncode == 0 and evidence.get("passed") is True and evidence.get("coreSha256") == core_hash
                detail = {"report": path.name, "binaryHashMatches": evidence.get("coreSha256") == core_hash,
                          "cases": len(evidence.get("checks", []))}
            except (OSError, ValueError):
                passed, detail = False, {"report": path.name, "error": "gate did not produce a valid report"}
            report["checks"].append({"name": name, "passed": passed, **detail})
            print(f"{'PASS' if passed else 'FAIL'} {name}", flush=True)

        consumer_report = {"syntheticOnly": True, "coreSha256": core_hash, "consumerDllSha256": digest(consumer),
                           "replays": 10, "storeReopenedEachReplay": True, "passed": False}
        try:
            batch = json.loads(batch_path.read_text())
            batch_hash = digest(batch_path)
            result = subprocess.run([args.dotnet, str(consumer), "--ingest-journal", str(batch_path),
                                     str(Path(temporary) / "usage.sqlite")], capture_output=True, text=True, timeout=120)
            (output / "consumer.log").write_text(result.stdout + result.stderr)
            if result.returncode != 0:
                raise AssertionError("already built SQLite consumer exited unsuccessfully")
            summary = json.loads(result.stdout)
            validate_consumer(summary, batch)
            if digest(consumer) != consumer_report["consumerDllSha256"] or digest(batch_path) != batch_hash:
                raise AssertionError("consumer DLL or input batch changed during replay")
            consumer_report.update({"passed": True, "inputJournalSha256": batch_hash, "result": summary})
        except Exception as error:
            consumer_report["error"] = str(error)
        (output / "consumer-replay.json").write_text(json.dumps(consumer_report, indent=2) + "\n")
        report["checks"].append({"name": "consumer-replay-10", "passed": consumer_report["passed"], "report": "consumer-replay.json"})
        print(f"{'PASS' if consumer_report['passed'] else 'FAIL'} consumer-replay-10", flush=True)
        report["passed"] = all(check["passed"] for check in report["checks"])
        if report["passed"]:
            shutil.copyfile(frozen, output / "qualified-lain-proxy")
            (output / "qualified-lain-proxy").chmod(0o700)
    report["requiredSeparateGates"] = [
        "adapter journal fsync/fault and durability-before-queue unit tests",
        "qualified parser component-presence unit tests",
        "owned OAuth login cancellation/persistence and all refresh gate unit tests",
        "Inspector capacity/expiry/redaction and measurement provenance unit tests",
        "free-only fresh zero-cap proof and final translated outbound request transport tests",
        "unchanged upstream dependency and documented build-patch provenance",
    ]
    report["notTested"] = ["real provider inference", "real OAuth login/refresh", "new upstream capabilities", "installed service workflow"]
    (output / "qualification.json").write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
