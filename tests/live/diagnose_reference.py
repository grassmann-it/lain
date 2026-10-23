#!/usr/bin/env python3
"""One explicitly authorized Command request; preserve partial live evidence.

Default is offline. --execute must be authorized separately from the four-call
reference plan. Reuses only the explicitly supplied own private state directory.
"""
import argparse
import datetime as dt
import json
import pathlib
import re
import sys
import urllib.error
import urllib.request

import orchestrate_reference as orchestration
import reference_probe as probe


def redaction_values(state):
    values = []
    def visit(value):
        if isinstance(value, dict):
            for key, item in value.items():
                normalized = key.lower().replace("-", "").replace("_", "")
                if normalized in ("apikey", "accesstoken", "refreshtoken", "idtoken") and isinstance(item, str) and item:
                    values.append(item)
                visit(item)
        elif isinstance(value, list):
            for item in value:
                visit(item)
    for file in (state / "credentials").iterdir():
        if file.suffix == ".token":
            values.append(file.read_text().strip())
        elif file.suffix == ".json":
            visit(json.loads(file.read_text()))
    return sorted(set(value for value in values if value), key=len, reverse=True)


def sanitize(value, secrets):
    if not isinstance(value, (str, int, float)) or isinstance(value, bool):
        return None
    text = str(value)
    for secret in secrets:
        text = text.replace(secret, "[redacted]")
    text = re.sub(r"(?i)bearer\s+[^\s\"'<>]+", "Bearer [redacted]", text)
    text = re.sub(r"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", "[redacted-jwt]", text)
    text = re.sub(r"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[redacted-email]", text)
    if "<html" in text.lower() or "<!doctype" in text.lower():
        return "HTML error body omitted."
    return " ".join(text.split())[:500]


def error_fields(raw, secrets):
    try:
        document = json.loads(raw)
    except ValueError:
        return {"code": None, "type": None, "message": "Non-JSON error body omitted."}
    error = document.get("error", document) if isinstance(document, dict) else {}
    if isinstance(error, str):
        try:
            error = json.loads(error)
        except ValueError:
            return {"code": None, "type": None, "message": sanitize(error, secrets)}
    if not isinstance(error, dict):
        return {"code": None, "type": None, "message": "Unstructured error body omitted."}
    # Some proxy wrappers carry another JSON error document inside message.
    message = error.get("message")
    if isinstance(message, str) and message.startswith("{"):
        try:
            nested = json.loads(message)
            nested = nested.get("error", nested) if isinstance(nested, dict) else None
            if isinstance(nested, dict):
                error = nested
        except ValueError:
            pass
    return {key: sanitize(error.get(key), secrets) for key in ("code", "type", "message")}


def html_error_classification(raw):
    """Only booleans and a known title class leave this function."""
    text = raw.decode("utf-8", errors="replace")
    try:
        parsed = json.loads(text)
        if isinstance(parsed, dict):
            error = parsed.get("error", parsed)
            if isinstance(error, dict) and isinstance(error.get("message"), str):
                text = error["message"]
    except ValueError:
        pass
    lower = text.lower()
    title = re.search(r"<title[^>]*>(.*?)</title>", text, re.I | re.S)
    known_title = None
    if title:
        candidate = " ".join(title.group(1).lower().split())
        for fragment, label in (("just a moment", "Cloudflare challenge"), ("attention required", "Cloudflare attention required"),
                                ("access denied", "Access denied"), ("403 forbidden", "403 Forbidden"), ("error 1010", "Cloudflare 1010")):
            if fragment in candidate:
                known_title = label
                break
        if known_title is None:
            known_title = "Unrecognized HTML title omitted"
    return {"html": "<html" in lower or "<!doctype" in lower, "cloudflare": "cloudflare" in lower,
            "cloudflare1010": "cloudflare" in lower and bool(re.search(r"\b1010\b", lower)),
            "challenge": "cf-chl" in lower or "just a moment" in lower or "challenge-platform" in lower,
            "accessDenied": "access denied" in lower or "accessdenied" in lower,
            "cdnForbidden": "forbidden" in lower and ("cloudflare" in lower or "cdn" in lower), "titleClass": known_title}


def one_command_request(token, account, secrets):
    payload = {"model": orchestration.COMMAND_ALIAS, "stream": False, "store": False, "max_output_tokens": 16,
               "instructions": "Reply exactly OK.",
               "input": [{"role": "user", "content": [{"type": "input_text", "text": "Say OK."}]}]}
    request = urllib.request.Request(probe.PROXY, json.dumps(payload).encode(), method="POST",
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json", "X-Lain-Account-ID": account,
                 "User-Agent": probe.CLIENT_USER_AGENT})
    try:
        response = probe.OPENER.open(request, timeout=45)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = probe.bounded_read(response)
        status = response.status
    if status >= 400:
        return {"statusCode": status, "error": error_fields(raw, secrets)}
    final = json.loads(raw)
    return {"statusCode": status, "usage": probe.safe_usage(final), "responseStatus": sanitize(final.get("status"), secrets)}


def usage_snapshot(host, window):
    query = orchestration.usage_query(window)
    page = orchestration.api("/attempts?limit=100&" + query, host.token)
    summary = orchestration.api("/usage?" + query, host.token)
    if any(item["finishedAt"] is None for item in page["items"]):
        return None
    return {"totals": summary["totals"], "accounts": summary["accounts"], "models": summary["models"],
            "complete": summary["complete"], "attempts": [{key: item[key] for key in ("id", "requestId", "accountId", "routeId", "model", "state", "statusCode", "tokens")}
                for item in sorted(page["items"], key=lambda item: item["id"])]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--state", type=pathlib.Path)
    parser.add_argument("--host-dll", type=pathlib.Path)
    parser.add_argument("--cli-dll", type=pathlib.Path)
    parser.add_argument("--proxy-binary", type=pathlib.Path)
    parser.add_argument("--approved-sha256")
    parser.add_argument("--verify-only", action="store_true", help="Local Host/CLI restart checks only; zero inference or quota requests.")
    args = parser.parse_args()
    report = {"state": "planned", "inferenceRequests": 0, "plan": "One nonstream Command gpt-6-luna diagnostic; read-only quota; existing usage persistence and CLI parity."}
    if not args.execute:
        print(json.dumps(report, indent=2))
        return 0
    host = None
    path = None
    try:
        orchestration.require(all((args.state, args.host_dll, args.cli_dll, args.proxy_binary, args.approved_sha256)), "All explicit own paths and approved SHA are required.")
        state = args.state.resolve(strict=True)
        orchestration.require(state.parent == pathlib.Path("/tmp") and state.name.startswith("lain-reference-"), "Diagnostic requires preserved isolated reference state.")
        path = state / "sanitized-diagnostic-report.json"
        actual = probe.validate_gate(args.proxy_binary)
        orchestration.require(actual == args.approved_sha256, "Approved binary SHA mismatch.")
        report.update(qualifiedSha256=actual, privateStateDirectory=str(state), stage="host-start")
        host = orchestration.Host(args.host_dll.resolve(strict=True), args.proxy_binary.resolve(strict=True), state)
        if args.verify_only:
            report = json.loads(path.read_text())
            report.pop("error", None)
            report.pop("errorType", None)
            report.update(stage="local-only-restart-verification", additionalInferenceRequests=0, additionalQuotaRequests=0)
            window = report["cliBeforeRestart"]["window"]
            host.start()
            before = orchestration.wait_until(lambda: usage_snapshot(host, window))
            orchestration.require(before["totals"] == report["beforeRestart"]["totals"] and before["attempts"] == report["beforeRestart"]["attempts"], "Existing records changed after diagnostic shutdown.")
            report["cliBeforeRestart"] = orchestration.compare_cli_usage(host, args.cli_dll.resolve(strict=True), window)
            host.stop()
            host.start()
            after = orchestration.wait_until(lambda: usage_snapshot(host, window))
            report["afterRestart"] = after
            report["cliAfterRestart"] = orchestration.compare_cli_usage(host, args.cli_dll.resolve(strict=True), window)
            orchestration.require(before["totals"] == after["totals"] and before["attempts"] == after["attempts"], "Records changed across local-only restart.")
            original_by_id = {item["id"]: item for item in report["originalThree"]["attempts"]}
            after_by_id = {item["id"]: item for item in after["attempts"]}
            orchestration.require(all(after_by_id.get(key) == value for key, value in original_by_id.items()), "An original terminal record changed after restart.")
            report.update(state="completed-partial-provider-evidence", stage="complete", usagePersistencePassed=True, originalThreePreserved=True, finalQualificationPassed=False)
            return 0
        secrets = redaction_values(state)
        host.start()
        original = json.loads((state / "sanitized-report.json").read_text())
        window = original["usageWindow"]
        config = orchestration.api("/configuration", host.token)
        account = probe.route_account(config, orchestration.COMMAND_ALIAS, False)
        baseline = orchestration.wait_until(lambda: usage_snapshot(host, window))
        orchestration.require(len(baseline["attempts"]) == 3, "Expected original three terminal attempts before diagnostic.")
        report["originalThree"] = baseline
        token = orchestration.api("/auth/token?audience=codex", host.token)["token"]
        report.update(stage="single-command-request", inferenceRequests=1)
        report["diagnostic"] = one_command_request(token, account, secrets)
        # Read-only quota is explicitly authorized and remains behind the existing Host adapter.
        report["stage"] = "quota"
        try:
            quotas = orchestration.api("/quotas/refresh", host.token, {"accountId": account})
            report["commandQuota"] = [row for row in quotas if row["accountId"] == account]
        except Exception:
            report["commandQuota"] = {"state": "unavailable", "error": "Optional quota diagnostic failed; raw data withheld."}
        def settled():
            snapshot = usage_snapshot(host, window)
            return snapshot if snapshot and len(snapshot["attempts"]) >= 4 else None
        before = orchestration.wait_until(settled)
        orchestration.require(len(before["attempts"]) == 4, "Diagnostic created unexpected additional attempts.")
        report["beforeRestart"] = before
        report["cliBeforeRestart"] = orchestration.compare_cli_usage(host, args.cli_dll.resolve(strict=True), window)
        report["stage"] = "host-restart"
        host.stop()
        host.start()
        after = orchestration.wait_until(settled)
        report["afterRestart"] = after
        report["cliAfterRestart"] = orchestration.compare_cli_usage(host, args.cli_dll.resolve(strict=True), window)
        orchestration.require(before["totals"] == after["totals"] and before["attempts"] == after["attempts"], "Usage changed across host restart.")
        old_by_id = {item["id"]: item for item in baseline["attempts"]}
        after_by_id = {item["id"]: item for item in after["attempts"]}
        orchestration.require(all(after_by_id.get(key) == value for key, value in old_by_id.items()), "An original terminal record changed after restart.")
        report.update(state="completed-partial-provider-evidence", stage="complete", usagePersistencePassed=True, originalThreePreserved=True, finalQualificationPassed=False)
        return 0
    except probe.ProbeError as error:
        report.update(state="failed", error=str(error), finalQualificationPassed=False)
        return 1
    except Exception as error:
        report.update(state="failed", errorType=type(error).__name__, error="Diagnostic orchestration failed; raw data withheld.", finalQualificationPassed=False)
        return 1
    finally:
        if host is not None:
            host.stop()
        output = json.dumps(report, indent=2)
        if path is not None:
            import os
            descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
            with os.fdopen(descriptor, "w") as file:
                file.write(output + "\n")
        print(output)


if __name__ == "__main__":
    sys.exit(main())
