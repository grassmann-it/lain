#!/usr/bin/env python3
"""Qualify actual Host-rendered config against a supplied isolated Core.

The exporter is an already built synthetic fixture DLL, never the installed Host.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from urllib.request import Request, urlopen

from fake_provider import FakeProvider, marker
from qualify import Run, assert_complete, expect


class HostRun(Run):
    def __init__(self, core, exporter, dotnet, protocol):
        provider = FakeProvider(keys={"acct-fixture-a-key": "acct-fixture-a", "acct-fixture-b-key": "acct-fixture-b"})
        super().__init__(core, provider=provider)
        try:
            directory = self.root / "host-export"
            result = subprocess.run([dotnet, str(exporter), "--export", str(directory), "--provider-url", self.provider.url,
                                     "--proxy-port", str(self.port), "--upstream-protocol", protocol],
                                    capture_output=True, text=True, timeout=30)
            expect(result.returncode == 0, "synthetic Hostwriter export failed: " + result.stdout + result.stderr)
            self.manifest = json.loads((directory / "fixture.json").read_text())
            expect(self.manifest["syntheticOnly"] is True and self.manifest["providerUrl"] == self.provider.url,
                   "Hostwriter export was not bound to this synthetic provider")
            expect(self.manifest["upstreamProtocol"] == protocol, "Hostwriter lost requested transport")
            # Every consumed path must belong to this fixture's private tree.
            for field in ("config", "bindings", "journal", "ownedAuthDir", "managementTokenFile"):
                expect(Path(self.manifest[field]).is_relative_to(directory), "export contains a path outside the fixture")
            self.journal = Path(self.manifest["journal"])
            self.management_key = Path(self.manifest["managementTokenFile"]).read_text().strip()
            self.client_key = (directory / "credentials" / "inference.token").read_text().strip()
        except Exception:
            self.__exit__()
            raise

    def command(self):
        manifest = self.manifest
        return [self.core, "--config", manifest["config"], "--bindings", manifest["bindings"],
                "--journal", manifest["journal"], "--owned-auth-dir", manifest["ownedAuthDir"],
                "--management-token-file", manifest["managementTokenFile"]]

    def get(self, path):
        with urlopen(Request(self.url + path, headers={"Authorization": "Bearer " + self.management_key}), timeout=5) as response:
            return json.load(response)

    def request(self, path, body=None):
        data = None if body is None else json.dumps(body).encode()
        return urlopen(Request(self.url + path, data, {"Authorization": "Bearer " + self.client_key,
                              "Content-Type": "application/json"}), timeout=15)


def check_protocol(core, exporter, dotnet, protocol):
    with HostRun(core, exporter, dotnet, protocol) as run:
        with run.request("/v1/models") as response:
            visible = {model["id"] for model in json.load(response)["data"]}
        expect(visible == {"fixture-strict-a", "fixture-strict-b", "fixture-pool"}, "Host alias visibility changed")
        for alias in ("fixture-strict-a", "fixture-strict-b", "fixture-pool", "fixture-hidden"):
            with run.request("/v1/responses", {"model": alias, "input": marker("text", alias), "stream": True}) as response:
                content = response.read().decode()
            expect("response.completed" in content and "synthetic response" in content, "Host-bound Responses stream failed")
        rows = run.provider.snapshot()
        expected_path = "/v1/responses" if protocol == "openai-responses" else "/v1/chat/completions"
        expect(len(rows) == 4 and all(row["path"] == expected_path for row in rows),
               "explicit Host upstream-protocol was lost or dispatched to the wrong endpoint")
        for alias, account in (("fixture-strict-a", "acct-fixture-a"), ("fixture-strict-b", "acct-fixture-b"), ("fixture-hidden", "acct-fixture-a")):
            expect(next(row for row in rows if row["request_id"] == alias)["account"] == account, "Host-generated strict binding borrowed another account")
        starts, usages = assert_complete(run.events()["events"], 4)
        expect(all(event["accountId"] in ("acct-fixture-a", "acct-fixture-b") for event in starts), "disabled/unselected account entered journal")
        expect(all(event["totalTokens"] == 18 for event in usages), "native transport dropped usage")
        for event in starts:
            expect(event["credentialId"] == run.manifest["accounts"][event["accountId"]], "Host/core credential identity differs")
        return {"upstreamProtocol": protocol, "observedEndpoint": expected_path, "requests": 4,
                "hostWriterCredentialIdsMatch": True, "hiddenAliasCallable": True, "hiddenAliasListed": False,
                "disabledAccountRegistered": False, "durableUsage": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--exporter", required=True, help="already built HostCoreContract.Tests.dll")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    data = Path(args.core).resolve(strict=True).read_bytes()
    exporter = Path(args.exporter).resolve(strict=True)
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": hashlib.sha256(data).hexdigest(),
              "hostExporterDllSha256": hashlib.sha256(exporter.read_bytes()).hexdigest(), "checks": []}
    with tempfile.TemporaryDirectory(prefix="lain-host-contract-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(data)
        frozen.chmod(0o700)
        for protocol in ("openai-chat", "openai-responses"):
            try:
                evidence = check_protocol(frozen, exporter, args.dotnet, protocol)
                report["checks"].append({"name": protocol, "passed": True, "evidence": evidence})
            except Exception as error:
                report["checks"].append({"name": protocol, "passed": False, "error": str(error)})
            print(f"{'PASS' if report['checks'][-1]['passed'] else 'FAIL'} Hostwriter {protocol}", flush=True)
    report["passed"] = all(check["passed"] for check in report["checks"])
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
