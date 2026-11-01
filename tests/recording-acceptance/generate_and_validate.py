#!/usr/bin/env python3
"""Generate and causally validate a bounded synthetic recording-acceptance graph.

This is an independent harness input, NOT the shared REC-00 fixture API and NOT
proof that any recorder, provider, client, storage, UI or integration works.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
from pathlib import Path
from typing import Any, Dict

HERE = Path(__file__).resolve().parent
DEFAULT_FIXTURE = HERE / "fixtures" / "synthetic-recording-v1.json"
DEFAULT_REPORT = HERE / "reports" / "synthetic-validation-report.json"
FIXTURE_KIND = "lain.recording.acceptance.synthetic-fixture"
REPORT_KIND = "lain.recording.acceptance.synthetic-validation-report"

# Deliberately non-credential-shaped, unmistakably synthetic leak canaries.
CANARIES = (
    "SYNTHETIC_CANARY_authorization_NOT_A_REAL_SECRET",
    "SYNTHETIC_CANARY_api_key_NOT_A_REAL_SECRET",
    "SYNTHETIC_CANARY_cookie_NOT_A_REAL_SECRET",
)
CANARY_KINDS = ("authorization", "api-key", "cookie")
MODEL_CALLS = 200
TOOL_REQUESTS = 100


def stable_json_bytes(value: Any, *, pretty: bool = False) -> bytes:
    if pretty:
        return (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=True) + "\n").encode()
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def atomic_write(path: Path, data: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    os.replace(temporary, path)


def call_id(number: int) -> str:
    return f"synthetic-model-call-{number:04d}"


def tool_id(number: int) -> str:
    return f"synthetic-tool-request-{number:03d}"


def branch_for(number: int) -> str:
    if number < 80:
        return "synthetic-branch-main"
    if number < 130:
        return "synthetic-branch-fork"
    if number < 155:
        return "synthetic-branch-worker-one"
    return "synthetic-branch-worker-two"


def requested_model(number: int) -> str:
    # Deliberately synthetic labels; no provider, route or GLM request is made.
    return (
        "synthetic-model-a",
        "synthetic-model-b",
        "synthetic-moving-alias-unknown-revision",
        "synthetic-model-c",
    )[number % 4]


def raw_sensitive_body(number: int) -> str:
    values = [
        f"authorization={CANARIES[0]}",
        f"api_key={CANARIES[1]}",
        f"cookie={CANARIES[2]}",
    ]
    return "synthetic body before policy; " + "; ".join(values) + f"; ordinal={number}"


def sanitize_raw_body(raw: str) -> tuple[str, list[str]]:
    sanitized = raw
    omitted: list[str] = []
    replacements = dict(zip(CANARIES, CANARY_KINDS))
    for canary, kind in replacements.items():
        if canary in sanitized:
            sanitized = sanitized.replace(canary, f"[omitted:{kind}]")
            omitted.append(kind)
    return sanitized, omitted


def make_fixture() -> dict[str, Any]:
    model_calls: list[Dict[str, Any]] = []
    tool_requests: list[Dict[str, Any]] = []
    sensitive_ordinals = {3, 41, 97, 151}
    seen_models: list[str] = []

    for number in range(MODEL_CALLS):
        prior_id = call_id(number - 1) if number else None
        branch = branch_for(number)
        requested = requested_model(number)
        reported = requested
        identity_status = "requested_and_reported_equal"
        if number % 29 == 0:
            reported = "synthetic-reported-model-" + chr(ord("d") + number % 7)
            identity_status = "different_provenance_preserved"
        if number % 25 == 0:
            reported = None
            identity_status = "unknown_not_inferred"

        if number in sensitive_ordinals:
            body, omitted = sanitize_raw_body(raw_sensitive_body(number))
        else:
            body = f"SYNTHETIC normalized model call {number:04d}; no provider or local execution ran"
            omitted = []

        current: Dict[str, Any] = {
            "harnessId": call_id(number),
            "sequence": number + 1,
            "conversationId": "synthetic-conversation-root",
            "branchId": branch,
            "logicalTurn": number // 3 + 1,
            "role": "root" if number < 130 else "worker",
            "parentCallId": prior_id,
            "causality": "ordered_synthetic_sequence",
            "model": {
                "requested": requested,
                "reported": reported,
                "identityStatus": identity_status,
            },
            "attempt": {
                "logicalRequestId": f"synthetic-request-{number:04d}",
                "attemptNumber": 1,
                "outcome": "complete_synthetic_response",
            },
            "historyResend": number % 10 == 0 and number > 0,
            "priorCallIds": [call_id(i) for i in range(max(0, number - 3), number)],
            "output": body,
            "coverage": {
                "clientIngress": "synthetic_only",
                "providerRequest": "unobserved",
                "providerResponse": "unobserved",
                "clientEgress": "unobserved",
                "localToolExecution": "not_observed",
            },
            "privacy": {
                "syntheticInputOnly": True,
                "redactedCanaryKinds": omitted,
                "rawSecretsEmitted": False,
            },
        }
        if requested not in seen_models:
            seen_models.append(requested)

        if number == 40:
            current["attempt"].update(
                {
                    "logicalRequestId": "synthetic-retry-request-001",
                    "attemptNumber": 1,
                    "outcome": "partial_output_then_retry",
                }
            )
        elif number == 41:
            current["attempt"].update(
                {
                    "logicalRequestId": "synthetic-retry-request-001",
                    "attemptNumber": 2,
                    "outcome": "complete_after_retry",
                    "followsFailedAttemptId": call_id(40),
                }
            )
            current["model"]["requested"] = requested_model(41)
            current["retryIncludesModelChange"] = True

        if branch == "synthetic-branch-fork" and number == 80:
            current.update(
                {
                    "parentCallId": call_id(79),
                    "fork": {"status": "explicit_synthetic_fork", "sharedPrefixInclusive": call_id(79)},
                }
            )
        if branch == "synthetic-branch-worker-one":
            current["workerEvidence"] = {
                "workerId": "synthetic-worker-one",
                "status": "supplied",
                "parentCallId": call_id(79) if number == 130 else call_id(number - 1),
            }
        if branch == "synthetic-branch-worker-two":
            current["workerEvidence"] = {
                "workerId": "synthetic-worker-two",
                "status": "supplied",
                "parentCallId": call_id(129) if number == 155 else call_id(number - 1),
            }
        if number == 120:
            current["identityConflict"] = {
                "status": "conflicting_parent_evidence",
                "candidateCallIds": [call_id(119), call_id(79)],
                "assignment": "unassigned_no_guess",
            }
            current["parentCallId"] = None
        model_calls.append(current)

    for number in range(TOOL_REQUESTS):
        parent_number = number
        same_name_parallel = number in (7, 8)
        fragmented = same_name_parallel
        name = "synthetic.tool.sameNameParallel" if same_name_parallel else f"synthetic.tool.{number:03d}"
        argument_state = "fragmented" if fragmented else "complete"
        result_kind = "accepted"
        if number == 19:
            result_kind = "rejected"
        elif number == 63:
            result_kind = "unavailable_payload"
        fragments = [
            f'{{"syntheticArgumentPart":"left-{number}"',
            f',"syntheticArgumentPart":"right-{number}"}}',
        ] if fragmented else [f'{{"syntheticArgumentOrdinal":{number}}}']
        result_payload: Dict[str, Any] | None = {
            "syntheticResultOrdinal": number,
            "clientReportedOnly": True,
        }
        if result_kind == "rejected":
            result_payload = {"syntheticRejection": True}
        elif result_kind == "unavailable_payload":
            result_payload = None
        tool_requests.append(
            {
                "harnessId": tool_id(number),
                "parentModelCallId": call_id(parent_number),
                "requestSequence": parent_number + 1.5,
                "name": name,
                "argumentState": argument_state,
                "argumentFragments": fragments,
                "nativeCallId": None if number % 23 == 0 else f"synthetic-native-tool-{number:03d}",
                "nativeIdStatus": "unknown_not_inferred" if number % 23 == 0 else "synthetic_internal",
                "clientReport": {
                    "status": "reported",
                    "reportSequence": parent_number + 1.9,
                    "resultKind": result_kind,
                    "payload": result_payload,
                    "payloadState": "available" if result_payload is not None else "unavailable",
                },
                "executionEvidence": "client_report_only_not_local_execution",
            }
        )

    return {
        "kind": FIXTURE_KIND,
        "version": 1,
        "synthetic": True,
        "containsRealUserOrProviderData": False,
        "contractStatus": {
            "sharedRecordingContractPublished": False,
            "fixtureApiClaim": None,
            "meaning": "internal causal acceptance input only; do not copy as DTO/schema",
            "adapterSeam": {
                "futureInput": "typed object from src/Lain.Core/RecordingContracts.cs",
                "futureAdapterOwner": "contract/integration wave, outside this directory",
                "requiredBehavior": "reject unmapped official types, preserve unknown/null identities, and label representation gaps",
            },
        },
        "scenario": {
            "name": "bounded-recording-causal-graph",
            "seed": 0,
            "providerCallsMade": 0,
            "localCommandsExecuted": 0,
            "description": "Deterministic synthetic graph for harness causality and leak checks",
        },
        "graph": {
            "conversations": [
                {
                    "harnessId": "synthetic-conversation-root",
                    "branches": [
                        "synthetic-branch-main",
                        "synthetic-branch-fork",
                        "synthetic-branch-worker-one",
                        "synthetic-branch-worker-two",
                    ],
                }
            ],
            "runs": [
                {
                    "harnessId": "synthetic-run-skill-v1",
                    "variantId": "skill-v1",
                    "skillRevision": "synthetic-revision-v1",
                    "evidenceStatus": "configured_only_not_loaded_proof",
                    "modelCallIds": [call_id(i) for i in range(0, 40)],
                },
                {
                    "harnessId": "synthetic-run-skill-v2",
                    "variantId": "skill-v2",
                    "skillRevision": "synthetic-revision-v2",
                    "instructionHash": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                    "evidenceStatus": "observed_instruction_hash_not_loaded_proof",
                    "modelCallIds": [call_id(i) for i in range(40, 80)],
                },
            ],
            "modelCalls": model_calls,
            "toolRequests": tool_requests,
            "negativeEdges": [
                {
                    "harnessId": "synthetic-missing-tool-result-probe",
                    "kind": "missing_client_result",
                    "status": "explicit_gap_not_reconstructed",
                },
                {
                    "harnessId": "synthetic-lost-source-interval-probe",
                    "kind": "lost_source_epoch_interval",
                    "status": "visible_gap",
                },
            ],
        },
        "expected": {
            "modelCalls": MODEL_CALLS,
            "distinctRequestedModelsAtLeast": 3,
            "toolRequests": TOOL_REQUESTS,
            "reportedToolResults": TOOL_REQUESTS,
            "sameNameParallelPairs": 1,
            "workerCount": 2,
            "forkCount": 1,
            "retryGroupCount": 1,
            "unknownIdentityCountAtLeast": 1,
            "skillVariants": ["skill-v1", "skill-v2"],
            "canaryValuesEmitted": False,
            "requiredCaseTags": [
                "model-change",
                "fork",
                "retry-model-change",
                "root-two-workers",
                "same-name-parallel-tools",
                "partial-arguments",
                "reported-results",
                "unknown-ids",
                "skills-v1-v2",
                "secret-canaries",
            ],
        },
        "canaryPolicy": {
            "synthetic": True,
            "valuesEmitted": False,
            "kinds": CANARY_KINDS,
            "validatorRule": "scan only the explicitly generated fixture bytes for source-defined synthetic canaries",
        },
    }


def check(name: str, condition: bool, detail: str) -> Dict[str, Any]:
    return {"name": name, "passed": bool(condition), "detail": detail}


def validate_fixture(fixture: Dict[str, Any], fixture_bytes: bytes) -> tuple[bool, list[Dict[str, Any]]]:
    checks: list[Dict[str, Any]] = []
    graph = fixture.get("graph", {})
    calls = graph.get("modelCalls", [])
    tools = graph.get("toolRequests", [])
    call_ids = [item.get("harnessId") for item in calls]
    call_id_set = set(call_ids)
    expected = fixture.get("expected", {})
    checks.append(check("fixture-kind", fixture.get("kind") == FIXTURE_KIND, FIXTURE_KIND))
    checks.append(check("synthetic-only", fixture.get("synthetic") is True and fixture.get("containsRealUserOrProviderData") is False, "real data must be absent"))
    checks.append(check("no-shared-contract-claim", fixture.get("contractStatus", {}).get("sharedRecordingContractPublished") is False, "official contract remains pending"))
    checks.append(check("model-call-count", len(calls) == expected.get("modelCalls") == MODEL_CALLS, f"actual={len(calls)}"))
    checks.append(check("unique-model-call-ids", len(call_ids) == len(call_id_set) == MODEL_CALLS, "stable harness IDs are unique"))
    checks.append(check("tool-request-count", len(tools) == expected.get("toolRequests") == TOOL_REQUESTS, f"actual={len(tools)}"))
    reports = [item for item in tools if item.get("clientReport", {}).get("status") == "reported"]
    checks.append(check("reported-result-count", len(reports) == expected.get("reportedToolResults") == TOOL_REQUESTS, f"actual={len(reports)}"))
    checks.append(check("no-execution-proof", all(item.get("executionEvidence") == "client_report_only_not_local_execution" for item in tools), "client reports are not local execution evidence"))

    sequence_ok = all(isinstance(item.get("sequence"), int) and item["sequence"] == index + 1 for index, item in enumerate(calls))
    checks.append(check("ordered-model-sequences", sequence_ok, "sequence is dense and ascending"))
    causal_ok = True
    for item in calls:
        parent = item.get("parentCallId")
        if item.get("identityConflict", {}).get("status") == "conflicting_parent_evidence":
            causal_ok = causal_ok and parent is None
        elif item["sequence"] > 1:
            causal_ok = causal_ok and parent in call_id_set
        for prior in item.get("priorCallIds", []):
            causal_ok = causal_ok and prior in call_id_set
    checks.append(check("causal-parents-exist-or-explicit-gap", causal_ok, "no invented parent identity"))

    requested = [item.get("model", {}).get("requested") for item in calls]
    changes = sum(left != right for left, right in zip(requested, requested[1:]))
    checks.append(check("model-change", changes > 0, f"changes={changes}"))
    checks.append(check("at-least-three-models", len(set(requested)) >= expected.get("distinctRequestedModelsAtLeast", 3), f"distinct={len(set(requested))}"))
    checks.append(check("at-least-three-turns", max(item.get("logicalTurn", 0) for item in calls) >= 3, "conversation has >=3 logical turns"))

    retry_groups: Dict[str, list[Dict[str, Any]]] = {}
    for item in calls:
        request_id = item.get("attempt", {}).get("logicalRequestId")
        if request_id:
            retry_groups.setdefault(request_id, []).append(item)
    retry = retry_groups.get("synthetic-retry-request-001", [])
    retry_ok = len(retry) == 2 and [item["attempt"]["attemptNumber"] for item in retry] == [1, 2] and retry[1].get("retryIncludesModelChange") is True
    checks.append(check("retry-with-model-change", retry_ok, "failed partial attempt remains distinct from complete retry"))

    fork = [item for item in calls if item.get("fork", {}).get("status") == "explicit_synthetic_fork"]
    checks.append(check("explicit-fork", len(fork) == 1 and fork[0].get("parentCallId") == call_id(79), "fork shares an explicit prefix parent"))

    workers = {item["workerEvidence"]["workerId"] for item in calls if item.get("workerEvidence", {}).get("status") == "supplied"}
    checks.append(check("root-two-workers", workers == {"synthetic-worker-one", "synthetic-worker-two"}, f"workers={sorted(workers)}"))

    same_names: Dict[str, list[Dict[str, Any]]] = {}
    for item in tools:
        same_names.setdefault(item.get("name"), []).append(item)
    parallel_pairs = [items for items in same_names.values() if len(items) >= 2]
    parallel_ok = bool(parallel_pairs) and all(item.get("argumentState") == "fragmented" for item in parallel_pairs[0])
    checks.append(check("same-name-parallel-partial-tools", parallel_ok, "same name has distinct IDs and fragmented arguments"))

    tool_parent_ok = all(item.get("parentModelCallId") in call_id_set for item in tools)
    later_report_ok = all(item.get("clientReport", {}).get("reportSequence", 0) > item.get("requestSequence", 0) for item in tools)
    checks.append(check("tool-causality", tool_parent_ok and later_report_ok, "reported results occur after requests and reference calls"))
    unique_tool_ids = len({item.get("harnessId") for item in tools}) == TOOL_REQUESTS
    checks.append(check("unique-tool-ids", unique_tool_ids, "stable harness tool IDs are unique"))

    unknown_calls = [item for item in calls if item.get("model", {}).get("identityStatus") == "unknown_not_inferred" and item["model"].get("reported") is None]
    unknown_tools = [item for item in tools if item.get("nativeIdStatus") == "unknown_not_inferred" and item.get("nativeCallId") is None]
    checks.append(check("unknown-ids-preserved", bool(unknown_calls and unknown_tools), "unknown remains null/not inferred"))

    variants = {item.get("variantId") for item in graph.get("runs", [])}
    skill_ok = variants == set(expected.get("skillVariants", [])) and all(item.get("evidenceStatus", "").endswith("not_loaded_proof") for item in graph.get("runs", []))
    checks.append(check("skills-v1-v2-without-loaded-claim", skill_ok, f"variants={sorted(variants)}"))

    text = fixture_bytes.decode("utf-8", errors="strict")
    leaked = [canary for canary in CANARIES if canary in text]
    checks.append(check("synthetic-canaries-absent", not leaked, "explicit synthetic canaries were omitted"))
    coverage_tags = expected.get("requiredCaseTags", [])
    checks.append(check("expected-coverage-schema", len(coverage_tags) == 10 and len(set(coverage_tags)) == 10, "all requested harness cases declared"))
    return all(item["passed"] for item in checks), checks


def write_report(path: Path, fixture_path: Path, fixture: Dict[str, Any], fixture_bytes: bytes, passed: bool, checks: list[Dict[str, Any]]) -> None:
    report = {
        "kind": REPORT_KIND,
        "version": 1,
        "synthetic": True,
        "passed": passed,
        "scope": "bounded fixture causality and synthetic leak checks only",
        "fixture": {
            "path": str(fixture_path),
            "sha256": sha256_bytes(fixture_bytes),
            "synthetic": fixture.get("synthetic") is True,
        },
        "coverage": fixture.get("expected", {}),
        "checks": checks,
        "integration": {
            "status": "not_run",
            "productionApiUsed": False,
            "providerCalls": 0,
            "clientStorageUiOrServiceClaimed": False,
            "blocker": "sharedRecordingContracts.cs / official fixture API is not published",
        },
        "canaryPolicy": {"inputScope": "explicit generated fixture only", "syntheticValuesLeaked": any(item["name"] == "synthetic-canaries-absent" and not item["passed"] for item in checks)},
    }
    atomic_write(path, stable_json_bytes(report, pretty=True))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fixture", type=Path, default=DEFAULT_FIXTURE)
    parser.add_argument("--report", type=Path, default=DEFAULT_REPORT)
    parser.add_argument("--generate", action="store_true", help="regenerate the deterministic synthetic fixture")
    args = parser.parse_args()

    fixture_path = args.fixture.resolve()
    if args.generate or not fixture_path.exists():
        fixture = make_fixture()
        fixture_bytes = stable_json_bytes(fixture)
        atomic_write(fixture_path, fixture_bytes)
    else:
        fixture_bytes = fixture_path.read_bytes()
        try:
            fixture = json.loads(fixture_bytes)
        except json.JSONDecodeError as error:
            raise SystemExit(f"invalid fixture JSON: {error}") from error

    passed, checks = validate_fixture(fixture, fixture_bytes)
    write_report(args.report.resolve(), fixture_path, fixture, fixture_bytes, passed, checks)
    print(f"synthetic fixture validation: {'passed' if passed else 'failed'}; integration=not_run")
    print(f"fixture={fixture_path}")
    print(f"report={args.report.resolve()}")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
