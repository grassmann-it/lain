#!/usr/bin/env python3
"""Check schema/coverage and synthetic leak canaries in explicitly named harness files."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
from typing import Any

from generate_and_validate import CANARIES, REPORT_KIND

HERE = Path(__file__).resolve().parent
DEFAULT_REPORT = HERE / "reports" / "synthetic-validation-report.json"
DEFAULT_FIXTURE = HERE / "fixtures" / "synthetic-recording-v1.json"
DEFAULT_OUTPUT = HERE / "reports" / "handoff-readiness.json"
READINESS_KIND = "lain.recording.acceptance.handoff-readiness-report"


def load_json(path: Path) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"cannot read explicit input {path}: {error}") from error


def write(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temp, path)


def result(name: str, passed: bool, detail: str) -> dict[str, Any]:
    return {"name": name, "passed": passed, "detail": detail}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, default=DEFAULT_REPORT)
    parser.add_argument("--fixture", type=Path, default=DEFAULT_FIXTURE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--allow-synthetic-input", action="store_true", required=True, help="explicitly opt into scanning only these synthetic harness files")
    args = parser.parse_args()
    report_path = args.report.resolve()
    fixture_path = args.fixture.resolve()
    report = load_json(report_path)
    fixture = load_json(fixture_path)
    checks: list[dict[str, Any]] = []

    fixture_bytes = fixture_path.read_bytes()
    fixture_digest = hashlib.sha256(fixture_bytes).hexdigest()
    checks.append(result("explicit-synthetic-fixture", fixture.get("synthetic") is True and fixture.get("containsRealUserOrProviderData") is False, "real data is not permitted"))
    checks.append(result("report-kind", report.get("kind") == REPORT_KIND, REPORT_KIND))
    checks.append(result("report-synthetic-scope", report.get("synthetic") is True and report.get("scope") == "bounded fixture causality and synthetic leak checks only", "scope must stay bounded"))
    checks.append(result("report-fixture-hash", report.get("fixture", {}).get("sha256") == fixture_digest, fixture_digest))
    checks.append(result("validation-passed-but-not-integration", report.get("passed") is True and report.get("integration", {}).get("status") == "not_run", "harness pass cannot become integration evidence"))

    coverage = report.get("coverage", {})
    required = {
        "modelCalls": 200,
        "toolRequests": 100,
        "reportedToolResults": 100,
        "workerCount": 2,
        "forkCount": 1,
        "retryGroupCount": 1,
        "skillVariants": ["skill-v1", "skill-v2"],
        "canaryValuesEmitted": False,
    }
    coverage_ok = all(coverage.get(key) == value for key, value in required.items())
    tags = coverage.get("requiredCaseTags", [])
    tags_ok = len(tags) == len(set(tags)) == 10
    checks.append(result("coverage-schema", coverage_ok and tags_ok, "counts, variants and ten case tags match"))

    names = {item.get("name") for item in report.get("checks", [])}
    required_checks = {
        "fixture-kind", "synthetic-only", "no-shared-contract-claim", "model-call-count",
        "unique-model-call-ids", "tool-request-count", "reported-result-count", "no-execution-proof",
        "causal-parents-exist-or-explicit-gap", "retry-with-model-change", "explicit-fork",
        "root-two-workers", "same-name-parallel-partial-tools", "tool-causality",
        "unknown-ids-preserved", "skills-v1-v2-without-loaded-claim", "synthetic-canaries-absent",
    }
    checks.append(result("validation-report-schema", required_checks.issubset(names), f"{len(required_checks)} required checks"))
    checks.append(result("all-reported-checks-passed", bool(report.get("checks")) and all(item.get("passed") is True for item in report["checks"]), "no failed causal checks"))

    # The intentional scan boundary is these two explicitly supplied synthetic files only.
    report_text = report_path.read_text(encoding="utf-8", errors="strict")
    fixture_text = fixture_bytes.decode("utf-8", errors="strict")
    leaks = [canary for canary in CANARIES if canary in report_text or canary in fixture_text]
    checks.append(result("explicit-input-leak-canaries", not leaks, "no synthetic canary value in report or fixture"))

    passed = all(item["passed"] for item in checks)
    output = {
        "kind": READINESS_KIND,
        "version": 1,
        "passed": passed,
        "readyFor": "contract-adapter handoff only",
        "integrationReady": False,
        "integrationStatus": "not_run",
        "inputs": {
            "report": str(report_path),
            "fixture": str(fixture_path),
            "fixtureSha256": fixture_digest,
            "scanBoundary": "explicit --report and --fixture only",
        },
        "checks": checks,
        "blockingIntegration": [
            "publish sharedRecordingContracts.cs and official fixture API",
            "implement a contract-owned adapter without treating harness IDs as provider IDs",
            "run real integration tests in the root serial lane after source wiring",
        ],
    }
    write(args.output.resolve(), output)
    print(f"handoff readiness: {'ready' if passed else 'not ready'}; integration=not_run")
    print(f"output={args.output.resolve()}")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
