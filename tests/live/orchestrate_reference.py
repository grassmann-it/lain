#!/usr/bin/env python3
"""Prepare or run the approved four-call reference check in private temporary state.

Do not pass --execute until root confirms W2/W11 synthetic qualification of the
explicit --approved-sha256. This script never creates a qualification marker.
"""
import argparse
import collections
import datetime as dt
import importlib.util
import json
import os
import pathlib
import signal
import socket
import subprocess
import sys
import tempfile
import time
import urllib.parse

import reference_probe as probe

CODEX_ALIAS = "live-codex-gpt-6-luna"
COMMAND_ALIAS = "cmd-gpt-6-luna"
PROFILE = "live-reference"


def require(condition, message):
    if not condition:
        raise probe.ProbeError(message)


def api(path, token, value=None, method=None):
    headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json", "User-Agent": probe.CLIENT_USER_AGENT}
    request = probe.urllib.request.Request(probe.HOST + path, data=json.dumps(value).encode() if value is not None else None,
                                          headers=headers, method=method or ("POST" if value is not None else "GET"))
    try:
        with probe.OPENER.open(request, timeout=35) as response:
            return json.loads(probe.bounded_read(response))
    except probe.urllib.error.HTTPError as error:
        raise probe.ProbeError("Own host API failed with HTTP " + str(error.code) + ".") from None


def wait_until(action, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        result = action()
        if result is not None:
            return result
        time.sleep(.2)
    raise probe.ProbeError("Own host readiness or durable usage deadline exceeded.")


class Host:
    def __init__(self, dll, binary, state):
        self.dll, self.binary, self.state = dll, binary, state
        self.process = None
        self.token = None

    def start(self):
        for port in (18417, 18418):
            with socket.socket() as reserve:
                reserve.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                try:
                    reserve.bind(("127.0.0.1", port))
                except OSError:
                    raise probe.ProbeError("Own reference port is occupied; existing processes left untouched.") from None
        environment = dict(os.environ, LAIN_HOME=str(self.state), LAIN_PROXY_PATH=str(self.binary),
                           LAIN_REFERENCE_QUALIFICATION="1", LAIN_REFERENCE_DISABLE_QUOTA="1")
        self.process = subprocess.Popen(["dotnet", str(self.dll)], env=environment, cwd=self.state,
                                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)

        def ready():
            require(self.process.poll() is None, "Own host exited before readiness.")
            token_path = self.state / "credentials/host.token"
            if not token_path.exists():
                return None
            self.token = token_path.read_text().strip()
            try:
                return api("/status", self.token)
            except (OSError, probe.ProbeError):
                return None
        return wait_until(ready)

    def stop(self):
        if self.process is None or self.process.poll() is not None:
            return
        try:
            if self.token:
                api("/service/stop", self.token, {})
        except Exception:
            pass
        self.process.terminate()
        try:
            self.process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            # This session was created by this script; never discover or kill by port/name.
            os.killpg(self.process.pid, signal.SIGKILL)
            self.process.wait(timeout=5)


def prepare_routes(host, legacy_config):
    config = api("/configuration", host.token)
    require(config["revision"] == 0 and not config["accounts"], "Reference state must start empty.")
    imported = api("/import", host.token, {"revision": 0, "configPath": str(legacy_config)})
    config = api("/configuration", host.token)
    snapshots = [a for a in config["accounts"] if a["credentialKind"] == "codex-access-snapshot" and a["enabled"]]
    require(len(snapshots) == 1, "Expected one enabled imported Codex snapshot.")
    command = [r for r in config["routes"] if r["alias"] == COMMAND_ALIAS and r["upstreamModel"] == probe.MODEL]
    require(len(command) == 1, "Approved Command Code model route missing or ambiguous.")
    command = command[0]
    require(len(command["accountIds"]) == 1 and not command["pooled"], "Command route must bind exactly one account.")
    command_account = next(a for a in config["accounts"] if a["id"] == command["accountIds"][0])
    require(command_account["provider"] == "commandcode" and command_account["credentialKind"] == "api-key", "Command route must select its imported provider account.")
    codex = {"id": "live-codex-luna", "alias": CODEX_ALIAS, "upstreamModel": probe.MODEL, "provider": "codex",
             "accountIds": [snapshots[0]["id"]], "pooled": False, "visible": True, "enabled": True}
    config = api("/routes/" + codex["id"], host.token, {"revision": config["revision"], "value": codex}, "PUT")
    profile = {"id": PROFILE, "name": "Bounded real reference", "routeIds": [codex["id"], command["id"]], "defaultRouteId": codex["id"]}
    config = api("/profiles/" + PROFILE, host.token, {"revision": config["revision"], "value": profile}, "PUT")
    config = api("/profiles/" + PROFILE + "/apply", host.token, {"revision": config["revision"]})
    require(config["revision"] == config["appliedRevision"], "Reference profile application failed.")
    probe.route_account(config, CODEX_ALIAS, True)
    probe.route_account(config, COMMAND_ALIAS, False)
    return config, {snapshots[0]["id"]: codex["id"], command_account["id"]: command["id"]}, imported


def usage_query(window):
    return urllib.parse.urlencode(window or {})


def capture_usage(host, expected, window=None):
    query = usage_query(window)
    page = api("/attempts?limit=100" + ("&" + query if query else ""), host.token)
    if page["total"] < 4 or any(a["finishedAt"] is None for a in page["items"]):
        return None
    require(page["total"] == 4 and len(page["items"]) == 4, "Expected exactly four durable attempts; no retries permitted.")
    counts = collections.Counter(a["accountId"] for a in page["items"])
    require(counts == collections.Counter({account: 2 for account in expected}), "Attempt account identity differs from the strict selected accounts.")
    for item in page["items"]:
        require(item["routeId"] == expected[item["accountId"]] and item["model"] == probe.MODEL,
                "Attempt route or upstream model identity differs from the approved route.")
        require(item["state"] not in ("pending", "failed", "canceled") and item["statusCode"] in range(200, 300),
                "A live attempt was unsuccessful.")
    summary = api("/usage" + ("?" + query if query else ""), host.token)
    require(summary["totals"]["attempts"] == 4 and summary["totals"]["requests"] == 4 and summary["totals"]["errors"] == 0,
            "Independent usage summary differs from the four successful reference requests.")
    return {"totals": summary["totals"], "complete": summary["complete"], "cursor": summary["cursor"],
            "accounts": summary["accounts"], "models": summary["models"],
            "attempts": [{key: item[key] for key in ("id", "requestId", "accountId", "routeId", "provider", "model", "statusCode", "state", "tokens")}
                         for item in sorted(page["items"], key=lambda a: a["id"])]}


def compare_cli_usage(host, cli_dll, window):
    summary = api("/usage?" + usage_query(window), host.token)
    arguments = ["dotnet", str(cli_dll), "usage", "--from", window["from"], "--to", window["to"],
                 "--timezone", window["timeZone"], "--json"]
    result = subprocess.run(arguments, env=dict(os.environ, LAIN_HOME=str(host.state)), cwd=host.state,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=25)
    require(result.returncode == 0, "CLI usage command failed; raw output withheld.")
    cli_summary = json.loads(result.stdout)
    require(cli_summary["totals"] == summary["totals"], "CLI and Host API usage totals differ for the same explicit window.")
    for key in ("daily", "accounts", "models", "timeZone"):
        require(cli_summary[key] == summary[key], "CLI and Host API usage groups or timezone differ.")
    return {"passed": True, "command": "dotnet Lain.Cli.dll usage --from FROM --to TO --timezone UTC --json",
            "window": window, "totals": summary["totals"], "groupsMatch": True}


def legacy_guard(root):
    path = pathlib.Path(__file__).resolve().parents[1] / "acceptance/legacy_guard.py"
    spec = importlib.util.spec_from_file_location("reference_legacy_guard", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module, capture_persistent_legacy(module, root)


def capture_persistent_legacy(module, root):
    # The personal service creates/removes OAuth .tmp files independently.
    # They are not persistent configuration evidence and can vanish during reads.
    files = [root / "cpa-core/cli-proxy-api", root / "cpa-core/config.yaml"]
    files += sorted(path for path in (root / "oauth").rglob("*") if not path.name.endswith(".tmp") and path.is_file())
    return {"schema": 1, "root": str(root.resolve()), "files": {
        str(path.relative_to(root)): {"sha256": module.digest(path), "mode": path.stat().st_mode & 0o777}
        for path in files}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--approved-sha256")
    parser.add_argument("--host-dll", type=pathlib.Path)
    parser.add_argument("--proxy-binary", type=pathlib.Path)
    parser.add_argument("--cli-dll", type=pathlib.Path)
    parser.add_argument("--legacy-root", type=pathlib.Path, default=pathlib.Path.home() / ".local/share/easycliproxyapi")
    args = parser.parse_args()
    report = {"state": "planned", "plan": probe.plan(CODEX_ALIAS, COMMAND_ALIAS), "calls": []}
    if not args.execute:
        print(json.dumps(report, indent=2))
        return 0
    host = None
    report_path = None
    guard = before = None
    try:
        require(args.proxy_binary is not None and args.host_dll is not None and args.approved_sha256 is not None,
                "Explicit built host, proxy and root-approved exact SHA are required.")
        binary = args.proxy_binary.resolve(strict=True)
        dll = args.host_dll.resolve(strict=True)
        cli_dll = args.cli_dll.resolve(strict=True) if args.cli_dll is not None else None
        actual = probe.validate_gate(binary)
        require(args.approved_sha256 == actual, "Root-approved SHA does not match the synthetic-qualified binary.")
        report["qualifiedSha256"] = actual
        state = pathlib.Path(tempfile.mkdtemp(prefix="lain-reference-"))
        state.chmod(0o700)
        report["privateStateDirectory"] = str(state)
        report_path = state / "sanitized-report.json"
        report["stage"] = "legacy-fingerprint"
        guard, before = legacy_guard(args.legacy_root)
        host = Host(dll, binary, state)
        report["stage"] = "host-start"
        host.start()
        report["stage"] = "import-and-profile"
        config, expected, imported = prepare_routes(host, args.legacy_root / "cpa-core/config.yaml")
        report.update(revision=config["revision"], importedAccounts=imported["added"], selectedAccounts=list(expected))
        token = api("/auth/token?audience=codex", host.token)["token"]
        now = dt.datetime.now(dt.timezone.utc)
        window = {"from": (now - dt.timedelta(minutes=1)).isoformat(), "to": (now + dt.timedelta(hours=1)).isoformat(), "timeZone": "UTC"}
        report["usageWindow"] = window
        report["stage"] = "live-four-calls"
        # These are the only inference-producing calls in the orchestrator.
        probe.run_pair(CODEX_ALIAS, probe.route_account(config, CODEX_ALIAS, True), token, report)
        probe.run_pair(COMMAND_ALIAS, probe.route_account(config, COMMAND_ALIAS, False), token, report)
        initial_usage = wait_until(lambda: capture_usage(host, expected, window))
        report["beforeRestart"] = initial_usage
        if cli_dll is not None:
            report["cliBeforeRestart"] = compare_cli_usage(host, cli_dll, window)
        report["stage"] = "host-restart"
        host.stop()
        host.start()
        recovered = wait_until(lambda: capture_usage(host, expected, window))
        report["afterRestart"] = recovered
        if cli_dll is not None:
            report["cliAfterRestart"] = compare_cli_usage(host, cli_dll, window)
        require(initial_usage["totals"] == recovered["totals"] and initial_usage["attempts"] == recovered["attempts"]
                and initial_usage["accounts"] == recovered["accounts"] and initial_usage["models"] == recovered["models"],
                "Usage totals or attempt identity did not persist across own host restart.")
        report.update(state="passed", stage="complete", usagePersistencePassed=True)
        return 0
    except probe.ProbeError as error:
        report.update(state="failed", error=str(error))
        return 1
    except Exception as error:
        report.update(state="failed", errorType=type(error).__name__, error="Reference orchestration failed; no raw data emitted.")
        return 1
    finally:
        if host is not None:
            host.stop()
        if guard is not None and before is not None:
            try:
                comparison = guard.compare(before, capture_persistent_legacy(guard, args.legacy_root))
                report["legacyFiles"] = {"unchanged": not any(comparison.values()), "temporaryFilesExcluded": True, **comparison}
            except Exception:
                report["legacyFiles"] = {"state": "comparison-unavailable"}
        output = json.dumps(report, indent=2)
        if report_path is not None:
            descriptor = os.open(report_path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
            with os.fdopen(descriptor, "w") as stream:
                stream.write(output + "\n")
        print(output)


if __name__ == "__main__":
    sys.exit(main())
