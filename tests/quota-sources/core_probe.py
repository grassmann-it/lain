#!/usr/bin/env python3
"""Prove passive Groq capture through a real, explicitly supplied Core process.

Only synthetic credentials, a private temporary Core and loopback HTTP are used.
No installed service, user state, provider endpoint or inference key is read.
Requires Main's serialized runtime slot; this script does not build the Core.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from fake_provider import FakeProvider, KEYS, MODEL, USAGE, marker, scenario
from qualify import CLIENT_KEY, MANAGEMENT_KEY, Run, assert_complete, expect

QUALIFIED_CORE_SHA256 = "a03e0ba2f4d2723a6a26fb10c89d838de75d6b14119b6ec4be3a2f1d7af4a185"
OBSERVATIONS = "/lain/quota/observations"
HEADER_NAMES = (
    "x-ratelimit-limit-requests", "x-ratelimit-remaining-requests", "x-ratelimit-reset-requests",
    "x-ratelimit-limit-tokens", "x-ratelimit-remaining-tokens", "x-ratelimit-reset-tokens",
)
HEADER_VALUES = {
    ("account-a", "success"): ("14400", "14399", "1h2m3.5s", "6000", "5990", "7.66s"),
    ("account-b", "success"): ("25000", "24990", "3h4m", "12000", "11950", "9s"),
    ("account-a", "limited"): ("14400", "0", "10s", "6000", "0", "1.25s"),
}


def utc_now():
    return datetime.now(timezone.utc)


def timestamp(value):
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    expect(parsed.tzinfo is not None, "observation timestamp has no timezone")
    return parsed


class GroqProvider(FakeProvider):
    def __init__(self):
        self.contract_errors = []
        super().__init__(keys=KEYS)

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server.server_port}/openai/v1"

    def _handler(self):
        owner = self
        base = super()._handler()

        class Handler(base):
            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
                case, request_marker = scenario(body)
                key = self.headers.get("Authorization", "").removeprefix("Bearer ")
                account = KEYS.get(key)
                errors = []
                if self.path != "/openai/v1/chat/completions":
                    errors.append("wrong Groq-compatible endpoint")
                if account is None or self.headers.get("x-api-key"):
                    errors.append("wrong synthetic credential transport")
                if body.get("model") != MODEL or body.get("max_tokens") != 64:
                    errors.append("concrete model or output bound lost")
                values = HEADER_VALUES.get((account, case))
                if values is None:
                    errors.append("unplanned synthetic request")
                if errors:
                    with owner.lock:
                        owner.contract_errors.extend(errors)
                    return self.reply(400, {"error": {"message": "synthetic contract rejected request"}})
                status = 429 if case == "limited" else 200
                headers = dict(zip(HEADER_NAMES, values))
                row = {"account": account, "case": case, "requestMarker": request_marker,
                       "model": body["model"], "status": status, "headers": headers,
                       "sentAt": utc_now().isoformat()}
                with owner.lock:
                    owner.observations.append(row)
                if status == 429:
                    response = {"error": {"message": "synthetic quota exhausted", "type": "rate_limit_error"}}
                else:
                    response = {"id": "upstream-id-must-not-be-local-request-id", "object": "chat.completion",
                                "model": MODEL, "choices": [{"index": 0, "message": {"role": "assistant",
                                "content": "fixture-private-response-payload"}, "finish_reason": "stop"}], "usage": USAGE}
                payload = json.dumps(response).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                for name, value in headers.items():
                    self.send_header(name, value)
                self.send_header("Authorization", "Bearer fixture-private-response-header")
                self.send_header("Set-Cookie", "fixture-private-cookie=omitted")
                self.send_header("X-Request-ID", "upstream-id-must-not-be-local-request-id")
                self.end_headers()
                self.wfile.write(payload)
        return Handler


class GroqRun(Run):
    def __init__(self, core):
        provider = GroqProvider()
        super().__init__(core, provider=provider)
        try:
            kind = "openai-compatibility:groq-openai-chat"
            self.accounts = {account: kind + ":" + hashlib.sha256(
                (kind + "\0" + key + "\0" + provider.url + "\0").encode()).hexdigest()[:12]
                for key, account in KEYS.items()}
            self.bindings = {"accounts": self.accounts, "routes": {
                "groq-a": {"id": "groq-route-a", "model": MODEL, "provider": "groq",
                           "accounts": ["account-a"], "selection": "strict"},
                "groq-b": {"id": "groq-route-b", "model": MODEL, "provider": "groq",
                           "accounts": ["account-b"], "selection": "strict"}}}
            self.write_bindings()
            config = self.root / "core.yaml"
            text = config.read_text()
            expect(text.count("  - name: fixture\n") == 1, "provider fixture YAML anchor changed")
            text = text.replace("  - name: fixture\n", "  - name: groq-openai-chat\n")
            text = text.replace("    base-url:", "    upstream-protocol: openai-chat\n    base-url:")
            # Core-private alias remains MODEL; public aliases belong to bindings.
            config.write_text(text)
        except Exception:
            self.__exit__()
            raise

    def quota_wire(self):
        with urlopen(Request(self.url + OBSERVATIONS, headers={"Authorization": "Bearer " + MANAGEMENT_KEY}), timeout=5) as response:
            expect(response.status == 200, "passive quota source failed")
            expect(response.headers.get("Cache-Control") == "no-store", "private quota source is cacheable")
            wire = response.read((1 << 20) + 1)
        expect(len(wire) <= 1 << 20, "quota response exceeds 1 MiB")
        batch = json.loads(wire)
        expect(set(batch) == {"version", "observations"} and batch["version"] == 1, "quota envelope changed")
        expect(isinstance(batch["observations"], list) and len(batch["observations"]) <= 512, "quota observations are not bounded")
        return wire, batch

    def dispatch(self, account, case):
        alias = "groq-a" if account == "account-a" else "groq-b"
        body = {"model": alias, "messages": [{"role": "user", "content": marker(case, account + "-" + case)}],
                "max_tokens": 64, "stream": False}
        request = Request(self.url + "/v1/chat/completions", json.dumps(body).encode(),
                          {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})
        before = len(self.events()["events"])
        try:
            with urlopen(request, timeout=10) as response:
                status = response.status
                response.read()
        except HTTPError as error:
            status = error.code
            error.read()
            error.close()
        received_at = utc_now()
        expect(status == (429 if case == "limited" else 200), "provider status changed")
        starts, usages = assert_complete(self.events()["events"][before:], 1)
        expect(starts[0]["accountId"] == account and starts[0]["provider"] == "groq", "journal identity changed")
        expect(usages[0]["outcome"] == ("failed" if case == "limited" else "success"), "journal outcome changed")
        if case == "limited":
            expect(usages[0]["totalTokens"] is None, "429 invented token usage")
        _, batch = self.quota_wire()
        observations = [row for row in batch["observations"] if row["accountId"] == account]
        expect(len(observations) == 1, "real Core did not capture the bound account exactly once")
        observation = observations[0]
        expect(set(observation) == {"accountId", "provider", "model", "requestId", "observedAt", "statusCode", "headers"}, "observation leaked noncontract fields")
        expect(observation["provider"] == "groq" and observation["model"] == MODEL, "observation provider/model is unproven")
        expect(observation["requestId"] == starts[0]["requestId"], "observation did not use local durable request identity")
        expect(observation["statusCode"] == status, "observation lost upstream status")
        sent = self.provider.snapshot()[-1]
        expect(sent["account"] == account and sent["case"] == case, "strict routing crossed accounts or retried")
        expect(observation["headers"] == {name: [value] for name, value in sent["headers"].items()}, "six actual response headers were not captured exactly")
        expect(timestamp(sent["sentAt"]) <= timestamp(observation["observedAt"]) <= received_at, "observation time is outside the actual HTTP response")
        expect(timestamp(starts[0]["observedAt"]) <= timestamp(observation["observedAt"]), "observation precedes its dispatch")
        return observation


def probe(core, checks):
    with GroqRun(core) as run:
        registered = {row["credentialId"]: row["provider"] for row in run.get("/lain/accounts")}
        expect(all(registered.get(identity) == "openai-compatible-groq-openai-chat" for identity in run.accounts.values()), "real SDK provider registration differs from Groq binding")
        checks.append("real_sdk_credential_registration")
        _, empty = run.quota_wire()
        expect(empty["observations"] == [] and not run.provider.snapshot(), "quota read before inference created data or a request")
        for method, token, expected_status in (("GET", CLIENT_KEY, 401), ("GET", "", 401), ("POST", MANAGEMENT_KEY, 405)):
            request = Request(run.url + OBSERVATIONS, headers={"Authorization": "Bearer " + token}, method=method)
            try:
                with urlopen(request, timeout=5) as response:
                    status = response.status
            except HTTPError as error:
                status = error.code
                error.close()
            expect(status == expected_status, "quota endpoint management authentication or method gate changed")
        checks.append("authenticated_passive_empty_source")
        first_a = run.dispatch("account-a", "success")
        checks.append("account_a_200_six_headers_local_request_time")
        first_b = run.dispatch("account-b", "success")
        checks.append("account_b_same_model_separate_observation")
        _, separate = run.quota_wire()
        expect(len(separate["observations"]) == 2 and first_a in separate["observations"] and first_b in separate["observations"], "same-model accounts were merged")
        limited_a = run.dispatch("account-a", "limited")
        expect(limited_a["requestId"] != first_a["requestId"], "new response retained an old request identity")
        expect(timestamp(limited_a["observedAt"]) >= timestamp(first_a["observedAt"]), "new response regressed observation time")
        wire, latest = run.quota_wire()
        expect(len(latest["observations"]) == 2 and limited_a in latest["observations"] and first_b in latest["observations"], "429 replacement crossed account scope")
        checks.append("account_a_429_replaces_only_its_latest_model")
        for forbidden in (*KEYS, CLIENT_KEY, MANAGEMENT_KEY, "fixture-private-response-payload", "fixture-private-response-header", "fixture-private-cookie", "upstream-id-must-not-be-local-request-id", "LAIN_FIXTURE:"):
            expect(forbidden.encode() not in wire, "private nonquota data escaped into passive observations")
        checks.append("no_credentials_payloads_or_upstream_request_identity")
        journal = run.events()
        expect(len(run.provider.snapshot()) == 3 and not run.provider.contract_errors, "unexpected provider call or wire-contract violation")
        for _ in range(5):
            expect(run.quota_wire()[0] == wire, "quota polling refreshed or changed observations")
        expect(len(run.provider.snapshot()) == 3 and run.events() == journal, "quota polling performed inference or wrote usage")
        checks.append("repeated_get_no_inference_no_timestamp_refresh")
        snapshot = Path(str(run.journal) + ".quota-observations.json")
        expect(snapshot.is_file() and snapshot.stat().st_mode & 0o777 == 0o600, "passive snapshot is not privately persisted")
        expect(snapshot.read_bytes() == wire, "persisted snapshot differs from authenticated source")
        run.stop()
        run.start()
        expect(run.quota_wire()[0] == wire, "real process restart changed observation identity or time")
        expect(run.events() == journal and len(run.provider.snapshot()) == 3, "restart replay created usage or inference")
        checks.append("process_restart_preserves_exact_private_snapshot")
        return {"upstreamCalls": 3, "accounts": 2, "models": 1, "observations": 2,
                "statuses": [200, 200, 429], "repeatedQuotaReads": 5, "restartWireEqual": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--expected-sha256", default=QUALIFIED_CORE_SHA256)
    args = parser.parse_args()
    core = args.core.resolve(strict=True)
    digest = hashlib.sha256(core.read_bytes()).hexdigest()
    report = {"syntheticOnly": True, "liveProvider": False, "coreSha256": digest,
              "expectedCoreSha256": args.expected_sha256, "passed": False, "checks": []}
    try:
        expect(digest == args.expected_sha256, "supplied Core does not match the explicit qualification pin")
        report["evidence"] = probe(core, report["checks"])
        expect(hashlib.sha256(core.read_bytes()).hexdigest() == digest, "Core binary changed during qualification")
        report["binaryUnchanged"] = True
        report["passed"] = True
    except Exception as error:
        report["error"] = type(error).__name__
        report["detail"] = str(error) if isinstance(error, AssertionError) else "synthetic Core bridge qualification failed"
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
