#!/usr/bin/env python3
"""Root-authorized two-call test of the existing imported Command GLM route.

This is an explicit test-route selection, not runtime automatic fallback.
"""
import argparse
import json
import os
import pathlib
import sys
import urllib.error
import urllib.request

import diagnose_reference as diagnostic
import orchestrate_reference as orchestration
import reference_probe as probe

ALIAS = "cmd-glm-5.3"
MODEL = "zai-org/GLM-5.3"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--state", type=pathlib.Path)
    parser.add_argument("--host-dll", type=pathlib.Path)
    parser.add_argument("--cli-dll", type=pathlib.Path)
    parser.add_argument("--proxy-binary", type=pathlib.Path)
    parser.add_argument("--approved-sha256")
    parser.add_argument("--identified-client-check", action="store_true", help="Separately approved truthful client identification pair, preserving earlier evidence.")
    args = parser.parse_args()
    report = {"state": "planned", "calls": [], "route": ALIAS, "upstreamModel": MODEL, "maximumInferenceRequests": 2, "quotaRequests": 0}
    if not args.execute:
        print(json.dumps(report, indent=2))
        return 0
    host = path = None
    original_open = probe.open_request
    try:
        orchestration.require(all((args.state, args.host_dll, args.cli_dll, args.proxy_binary, args.approved_sha256)), "Explicit approved paths and SHA are required.")
        state = args.state.resolve(strict=True)
        orchestration.require(state.parent == pathlib.Path("/tmp") and state.name.startswith("lain-reference-"), "Expected preserved own reference state.")
        path = state / ("sanitized-command-glm-identified-client-report.json" if args.identified_client_check else "sanitized-command-glm-report.json")
        actual = probe.validate_gate(args.proxy_binary)
        orchestration.require(actual == args.approved_sha256, "Approved SHA mismatch.")
        report.update(qualifiedSha256=actual, privateStateDirectory=str(state), stage="host-start", clientUserAgent=probe.CLIENT_USER_AGENT)
        previous = json.loads((state / "sanitized-diagnostic-report.json").read_text())
        window = previous["cliBeforeRestart"]["window"]
        secrets = diagnostic.redaction_values(state)
        host = orchestration.Host(args.host_dll.resolve(strict=True), args.proxy_binary.resolve(strict=True), state)
        host.start()
        baseline = orchestration.wait_until(lambda: diagnostic.usage_snapshot(host, window))
        previous_count = 5 if args.identified_client_check else 4
        orchestration.require(len(baseline["attempts"]) == previous_count, "Unexpected preserved attempt count before this explicit model test.")
        report["baseline"] = baseline
        config = orchestration.api("/configuration", host.token)
        routes = [route for route in config["routes"] if route["alias"] == ALIAS and route["upstreamModel"] == MODEL]
        orchestration.require(len(routes) == 1, "Approved imported GLM route unavailable or ambiguous.")
        route = routes[0]
        original_command = probe.route_account(config, orchestration.COMMAND_ALIAS, False)
        orchestration.require(route["enabled"] and not route["pooled"] and route["accountIds"] == [original_command], "GLM must bind the same strict imported Command account.")
        codex = next(item for item in config["routes"] if item["alias"] == orchestration.CODEX_ALIAS)
        profile = {"id": orchestration.PROFILE, "name": "Explicit Command GLM reference", "routeIds": [codex["id"], route["id"]], "defaultRouteId": codex["id"]}
        config = orchestration.api("/profiles/" + orchestration.PROFILE, host.token, {"revision": config["revision"], "value": profile}, "PUT")
        config = orchestration.api("/profiles/" + orchestration.PROFILE + "/apply", host.token, {"revision": config["revision"]})
        report.update(revision=config["revision"], selectedAccount=original_command, selectedRouteId=route["id"], stage="two-glm-requests")
        token = orchestration.api("/auth/token?audience=codex", host.token)["token"]
        def capturing_open(url, bearer, payload=None, account_id=None):
            headers = {"Authorization": "Bearer " + bearer, "Content-Type": "application/json", "X-Lain-Account-ID": account_id,
                       "User-Agent": probe.CLIENT_USER_AGENT}
            request = urllib.request.Request(url, json.dumps(payload).encode(), headers=headers, method="POST")
            try:
                return probe.OPENER.open(request, timeout=45)
            except urllib.error.HTTPError as error:
                with error:
                    raw = probe.bounded_read(error)
                    report["providerError"] = {"statusCode": error.code, **diagnostic.error_fields(raw, secrets),
                        "bodyClassification": diagnostic.html_error_classification(raw),
                        "retryAfter": diagnostic.sanitize(error.headers.get("Retry-After"), secrets),
                        "cfMitigatedChallenge": error.headers.get("cf-mitigated", "").lower() == "challenge",
                        "proxyNormalizes403Code": error.code == 403}
                raise probe.ProbeError("GLM request failed; no continuation or retry sent.") from None
        probe.open_request = capturing_open
        probe.run_pair(ALIAS, original_command, token, report)
        probe.open_request = original_open
        def settled():
            snapshot = diagnostic.usage_snapshot(host, window)
            return snapshot if snapshot and len(snapshot["attempts"]) >= previous_count + 2 else None
        before = orchestration.wait_until(settled)
        orchestration.require(len(before["attempts"]) == previous_count + 2, "Unexpected total terminal records after the bounded pair.")
        new = [item for item in before["attempts"] if item["id"] not in {old["id"] for old in baseline["attempts"]}]
        orchestration.require(len(new) == 2 and all(item["accountId"] == original_command and item["routeId"] == route["id"] and item["model"] == MODEL and item["statusCode"] == 200 for item in new), "New GLM attempt identity or status differs from the explicit strict route.")
        report["beforeRestart"] = before
        report["cliBeforeRestart"] = orchestration.compare_cli_usage(host, args.cli_dll.resolve(strict=True), window)
        report["stage"] = "host-restart"
        host.stop()
        host.start()
        after = orchestration.wait_until(settled)
        report["afterRestart"] = after
        report["cliAfterRestart"] = orchestration.compare_cli_usage(host, args.cli_dll.resolve(strict=True), window)
        orchestration.require(before["totals"] == after["totals"] and before["attempts"] == after["attempts"], "Records changed across own Host restart.")
        after_by_id = {item["id"]: item for item in after["attempts"]}
        orchestration.require(all(after_by_id.get(item["id"]) == item for item in baseline["attempts"]), "An original success or failed unknown-usage record changed.")
        report.update(state="passed-explicit-route-reference", stage="complete", usagePersistencePassed=True, existingRecordCountPreserved=previous_count, qualificationNotClaimed=True)
        return 0
    except probe.ProbeError as error:
        report.update(state="failed", error=str(error), qualificationNotClaimed=True)
        return 1
    except Exception as error:
        report.update(state="failed", errorType=type(error).__name__, error="Explicit GLM reference failed; raw data withheld.", qualificationNotClaimed=True)
        return 1
    finally:
        probe.open_request = original_open
        if host is not None:
            host.stop()
        output = json.dumps(report, indent=2)
        if path is not None:
            descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
            with os.fdopen(descriptor, "w") as file:
                file.write(output + "\n")
        print(output)


if __name__ == "__main__":
    sys.exit(main())
