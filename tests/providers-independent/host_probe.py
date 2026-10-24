#!/usr/bin/env python3
"""Packaged Host HTTP acceptance; synthetic state, no inference or OAuth.

Run only in Main's serialized slot in a private user/network namespace with lo up:
  python3 host_probe.py --host /abs/Lain.Host.dll --proxy /abs/proxy/lain-proxy --report /abs/report.json
Requires the real catalog beside --proxy. Does not start/apply the Core or refresh quotas.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

BASE = "http://127.0.0.1:18418/api/v1"
DEFAULT_TOKEN = object()
EXPECTED = {"nvidia-build", "stepfun", "poe", "opencode-go", "gemini-api", "groq", "mistral",
            "openrouter", "requesty", "cloudflare-workers-ai", "inception", "alibaba-model-studio",
            "nebius", "synthetic", "xai", "sambanova", "upstage", "cohere", "kilo", "fireworks"}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


class Probe:
    def __init__(self, root):
        self.root = root
        self.state = root / "state"
        self.token = None
        self.secrets = []
        self.checks = []
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def secret(self, suffix):
        value = "synthetic-independent-provider-probe-" + suffix
        self.secrets.append(value)
        return value

    def call(self, path, method="GET", value=None, token=DEFAULT_TOKEN):
        selected = self.token if token is DEFAULT_TOKEN else token
        headers = {"Content-Type": "application/json"}
        if selected is not None:
            headers["Authorization"] = "Bearer " + selected
        req = urllib.request.Request(BASE + path, data=None if value is None else json.dumps(value).encode(),
                                     headers=headers, method=method)
        try:
            response = self.http.open(req, timeout=10)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            body = response.read(2 * 1024 * 1024 + 1)
            require(len(body) <= 2 * 1024 * 1024, "public response exceeds probe bound")
            text = body.decode()
            require(not any(secret in text for secret in self.secrets), "secret appeared in public response")
            return response.status, json.loads(text) if text else None

    def get(self, path):
        code, body = self.call(path)
        require(code == 200, "authenticated read failed: " + path)
        return body

    def configuration(self):
        return self.get("/configuration")

    def private_state(self):
        return {str(path.relative_to(self.state)): digest(path)
                for path in (self.state / "credentials").rglob("*") if path.is_file()}

    def reject_unchanged(self, path, method, value, expected):
        public, private = self.configuration(), self.private_state()
        require(self.call(path, method, value)[0] == expected, "invalid or stale mutation did not reject: " + path)
        require(self.configuration() == public and self.private_state() == private,
                "rejected mutation changed public or private state: " + path)

    def add(self, value):
        previous = self.configuration()
        code, current = self.call("/accounts", "POST", {"revision": previous["revision"], "value": value})
        require(code == 200 and current["revision"] == previous["revision"] + 1, "account mutation did not save exactly once")
        require(current["appliedRevision"] == 0, "account setup unexpectedly applied a runtime")
        return current

    def input(self, provider, key, settings=None, connection=None, plan=None):
        connection = connection or provider["connections"][0]
        values = dict(settings or {}, billingPlan=plan or connection["billingPlanIds"][0])
        endpoint = connection["baseUrl"]
        for name, value in values.items():
            endpoint = endpoint.replace("{" + name + "}", value)
        return {"name": "Independent " + provider["id"], "provider": provider["id"], "apiKey": key,
                "baseUrl": endpoint, "providerConnectionId": connection["id"], "settings": values,
                "selectedModelRouteIds": []}

    def run(self, catalog_models):
        self.token = (self.state / "credentials/host.token").read_text().strip()
        inference = (self.state / "credentials/inference.token").read_text().strip()
        self.secrets.extend([self.token, inference])
        require(self.configuration()["revision"] == 0, "state was not fresh")
        providers = {item["id"]: item for item in self.get("/providers")["providers"]}
        require(set(providers) == EXPECTED, "packaged catalog does not contain exactly the expected 20 providers")
        require(all(item.get("connections") for item in providers.values()), "provider connections missing from Host catalog")
        for provider_id in ("sambanova", "upstage", "cohere", "kilo", "fireworks"):
            onboarding = self.get("/providers/" + provider_id + "/onboarding")
            require(onboarding["accountIds"] == [] and onboarding["liveVerification"] == "not_tested",
                    "empty onboarding incorrectly claims live verification")
        for provider_id in ("fireworks", "gemini-api", "cloudflare-workers-ai"):
            require(providers[provider_id].get("monitorAccessOptions"), "supported manual monitor setup absent from catalog")
        self.checks.append("20 embedded providers, connection groups and supported monitor options")

        provider = providers["groq"]
        options = [(connection, plan, [route["id"] for route in provider["routes"]
                    if route["id"] in connection["providerRouteIds"] and plan in route["billingPlanIds"]])
                   for connection in provider["connections"] for plan in connection["billingPlanIds"]]
        connection, plan, models = next(item for item in options if item[1] == "free" and len(item[2]) >= 2)
        setup = self.input(provider, self.secret("groq"), connection=connection, plan=plan)
        current = self.add(setup)
        account = next(item for item in current["accounts"] if item["provider"] == "groq")
        require(current["routes"] == [] and account["state"] == "unverified", "connection-only setup activated routes or claimed validation")
        chosen = dict(setup, name="Must not overwrite custom name", selectedModelRouteIds=models[:2])
        current = self.add(chosen)
        require(len(current["accounts"]) == 1 and len(current["routes"]) == 2,
                "adding two models duplicated the account or missed routes")
        require(current["accounts"][0]["name"] == setup["name"] and
                all(route["accountIds"] == [account["id"]] for route in current["routes"]), "model setup changed account identity/name")
        require({route["providerRouteId"] for route in current["routes"]} == set(models[:2]), "wrong selected model routes saved")
        for value, revision, code in [(dict(setup, apiKey=self.secret("invalid-model"), selectedModelRouteIds=["not-a-template"]), current["revision"], 400),
                                      (dict(setup, apiKey=self.secret("stale-write")), current["revision"] - 1, 409)]:
            self.reject_unchanged("/accounts", "POST", {"revision": revision, "value": value}, code)
        self.checks.append("connection-only account, two model routes, stable identity/name and atomic invalid/stale rejection")

        current = self.add(self.input(providers["fireworks"], self.secret("fireworks-gateway"), {"accountId": "review-fireworks"}))
        fw = next(item for item in current["accounts"] if item["provider"] == "fireworks")
        path = "/accounts/" + fw["id"] + "/monitor"
        gateway_before = digest(self.state / "credentials" / (fw["id"] + ".json"))
        monitor = {"kind": "api-token", "secret": self.secret("fireworks-monitor"), "resourceId": "review-fireworks"}
        before_auth = (self.configuration(), self.private_state())
        for supplied in (None, "", "wrong", inference):
            for method, target, value in [("GET", path, None), ("PUT", path, {"revision": current["revision"], "value": monitor}),
                                          ("DELETE", path + "?revision=" + str(current["revision"]), None)]:
                require(self.call(target, method, value, token=supplied)[0] == 401, "monitor API bypassed management authentication")
        require((self.configuration(), self.private_state()) == before_auth, "unauthorized monitor operation mutated state")
        require(self.get(path)["state"] == "not-configured", "fresh monitor was not unconfigured")
        code, current = self.call(path, "PUT", {"revision": current["revision"], "value": monitor})
        require(code == 200, "authorized monitor PUT failed")
        public = self.get(path)
        require(public["state"] == "configured-unverified" and public["accountId"] == fw["id"] and
                public["provider"] == "fireworks" and not {"secret", "accessToken", "refreshToken"} & set(public),
                "public monitor status leaked secret fields, changed ownership or claimed verification")
        self.reject_unchanged(path, "PUT", {"revision": current["revision"] - 1, "value": monitor}, 409)
        self.reject_unchanged(path, "PUT", {"revision": current["revision"], "value": dict(monitor, resourceId="other-account")}, 400)
        self.reject_unchanged(path + "?revision=" + str(current["revision"] - 1), "DELETE", None, 409)
        code, current = self.call(path + "?revision=" + str(current["revision"]), "DELETE")
        require(code == 200 and self.get(path)["state"] == "not-configured", "monitor DELETE did not remove capability")
        require(digest(self.state / "credentials" / (fw["id"] + ".json")) == gateway_before, "monitor operations changed gateway credential")
        require(self.call("/accounts/not-present/monitor")[0] == 404, "unknown monitor account did not return 404")
        self.checks.append("monitor GET/PUT/DELETE auth, audience, revision, ownership, secret-free status and gateway preservation")

        current = self.add(self.input(providers["gemini-api"], self.secret("gemini-gateway")))
        gemini = next(item for item in current["accounts"] if item["provider"] == "gemini-api")
        path = "/accounts/" + gemini["id"] + "/monitor"
        expiry = (dt.datetime.now(dt.timezone.utc) + dt.timedelta(hours=1)).isoformat()
        snapshot = {"kind": "access-token-snapshot", "secret": self.secret("google-monitor"), "resourceId": "review-project", "expiresAt": expiry}
        self.reject_unchanged(path, "PUT", {"revision": current["revision"], "value": dict(snapshot, expiresAt="2000-01-01T00:00:00Z")}, 400)
        code, current = self.call(path, "PUT", {"revision": current["revision"], "value": snapshot})
        require(code == 200 and self.get(path)["state"] == "configured-unverified", "manual Google access snapshot setup failed")
        rows = self.get("/quotas")
        require(all(row["remaining"] is None and row["lastSuccessAt"] is None for row in rows), "disabled polling fabricated a quota observation")
        for identity in (fw["id"], gemini["id"]):
            require(next(row for row in rows if row["accountId"] == identity)["source"] != "unavailable", "new quota adapter not registered")
        self.checks.append("manual Google access snapshot with expiry and registered unpolled quota adapters")

        legacy = self.root / "synthetic-legacy"
        legacy.mkdir(mode=0o700)
        config = legacy / "config.yaml"
        config.write_text("{}\n")
        auth = legacy / "auth"
        auth.mkdir(mode=0o700)
        source = auth / "codex.json"
        refresh, id_token = self.secret("never-import-refresh"), self.secret("never-import-id")
        source.write_text(json.dumps({"type": "codex", "account_id": "independent-codex-account",
            "display_name": "Synthetic Codex", "access_token": self.secret("codex-access"),
            "refresh_token": refresh, "id_token": id_token, "expired": "2099-01-01T00:00:00Z"}))
        source.chmod(0o600)
        external_before = digest(source)
        before = self.configuration()
        code, imported = self.call("/import", "POST", {"revision": before["revision"], "configPath": str(config), "authDirectory": str(auth)})
        require(code == 200 and imported["added"] == 1, "synthetic no-alias Codex import failed")
        current = self.configuration()
        codex = next(item for item in current["accounts"] if item["provider"] == "codex")
        routes = [route for route in current["routes"] if route["accountIds"] == [codex["id"]]]
        require(len(routes) == 8 and {route["upstreamModel"] for route in routes} == set(catalog_models) and
                all(route["alias"] == route["upstreamModel"] for route in routes), "packaged adjacent catalog did not create exactly 8 default Codex routes")
        copied = (self.state / "credentials" / (codex["id"] + ".json")).read_text()
        require(refresh not in copied and id_token not in copied and digest(source) == external_before,
                "import copied refresh material or mutated external snapshot")
        require(current["appliedRevision"] == 0 and len(current["routes"]) == len(before["routes"]) + 8,
                "fresh import applied a runtime or changed unrelated routes")
        for target in ("/configuration", "/accounts", "/quotas", "/providers"):
            self.get(target)
        self.checks.append("packaged adjacent catalog creates 8 Codex routes without aliases, refresh material or OAuth calls")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", required=True, type=Path)
    parser.add_argument("--proxy", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    report = {"passed": False, "checks": [], "qualification": "synthetic packaged Host HTTP only", "provider_calls": 0}
    process = None
    try:
        require(args.host.is_absolute() and args.host.is_file() and args.proxy.is_absolute() and args.proxy.is_file(), "provide absolute packaged Host and Core file paths")
        # Fail closed outside the dedicated network namespace; no external interface
        # is needed by this probe or any Host collector. Main brings loopback up.
        interfaces = {line.split(":", 1)[0].strip() for line in Path("/proc/net/dev").read_text().splitlines()[2:] if ":" in line}
        require(interfaces == {"lo"}, "probe requires a private loopback-only network namespace")
        for port in (18417, 18418):
            with socket.socket() as reserve:
                reserve.bind(("127.0.0.1", port))
        catalog_path = args.proxy.parent / "codex-model-catalog.json"
        catalog = json.loads(catalog_path.read_text())
        models = [item["id"] for item in catalog["models"]]
        require(len(models) == 8 and len(set(models)) == 8, "packaged Codex catalog must contain 8 distinct models")
        report["artifacts"] = {"host_sha256": digest(args.host), "proxy_sha256": digest(args.proxy),
                               "catalog_sha256": digest(catalog_path), "catalog_model_count": len(models)}
        with tempfile.TemporaryDirectory(prefix="lain-independent-host-", dir=os.environ.get("TMPDIR")) as temporary:
            root = Path(temporary)
            probe = Probe(root)
            report["checks"] = probe.checks
            env = dict(os.environ, LAIN_HOME=str(probe.state), LAIN_PROXY_PATH=str(args.proxy),
                       LAIN_REFERENCE_QUALIFICATION="1", LAIN_REFERENCE_DISABLE_QUOTA="1")
            with (root / "host.log").open("w") as log:
                process = subprocess.Popen(["dotnet", str(args.host)], env=env, cwd=root, stdout=log, stderr=log)
                try:
                    deadline = time.monotonic() + 30
                    while time.monotonic() < deadline:
                        require(process.poll() is None, "packaged Host exited before readiness")
                        try:
                            if probe.call("/status", token=None)[0] == 401:
                                break
                        except OSError:
                            pass
                        time.sleep(0.1)
                    else:
                        raise AssertionError("packaged Host did not become ready")
                    probe.run(models)
                    report["passed"] = True
                finally:
                    if process.poll() is None:
                        process.terminate()
                        try:
                            process.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait(timeout=5)
    except Exception as error:
        report["error"] = type(error).__name__
        report["detail"] = str(error) if isinstance(error, AssertionError) else "probe execution failed; private logs and response bodies withheld"
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
