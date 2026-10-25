#!/usr/bin/env python3
"""Prepare/run the actual mixed package's Host + CLI + Core group contract.

Default is preparation only. --run requires bubblewrap network/PID isolation.
Uses synthetic state and loopback providers; never changes HOME/CODEX_HOME.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
from urllib.error import HTTPError
from urllib.request import ProxyHandler, Request, build_opener
import uuid


def digest(path):
    with Path(path).open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def expect(value, message):
    if not value:
        raise AssertionError(message)


def save(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n")


def inner():
    sys.path.insert(0, "/mnt/harness")
    from fake_provider import FakeProvider, MODEL, marker

    plan = json.loads(Path("/mnt/plan.json").read_text())
    home = Path(plan["homePath"])
    expect(os.environ.get("HOME") == str(home), "original HOME value changed")
    expect(os.readlink("/proc/self/ns/net") != plan["outerNetNamespace"], "network namespace isolation missing")
    expect(os.readlink("/proc/self/ns/pid") != plan["outerPidNamespace"], "PID namespace isolation missing")
    root, package = Path("/mnt"), Path("/mnt/package")
    data, prefix = root / "data", home / ".local"
    sentinel = home / ".codex/config.toml"
    expect(digest(sentinel) == plan["sentinelSha256"], "synthetic main config missing")
    # HOME is inherited byte-for-byte; its filesystem backing is synthetic.
    env = {key: os.environ[key] for key in ("PATH", "HOME", "USER", "LOGNAME", "LANG", "LC_ALL", "TZ") if key in os.environ}
    env.update(LAIN_HOME=str(data), LAIN_SYSTEMD_DIR=str(root / "units"),
               LAIN_DESKTOP_DATA_DIR=str(root / "desktop"),
               XDG_CONFIG_HOME=str(root / "xdg/config"), XDG_CACHE_HOME=str(root / "xdg/cache"),
               XDG_DATA_HOME=str(root / "xdg/data"), TMPDIR=str(root / "tmp"), DOTNET_CLI_TELEMETRY_OPTOUT="1")
    opener = build_opener(ProxyHandler({}))
    host = None
    provider = None
    report = {"status": "failed", "syntheticOnly": True, "systemdOperations": 0, "checks": [],
              "package": plan["package"], "namespace": {"net": os.readlink("/proc/self/ns/net"), "pid": os.readlink("/proc/self/ns/pid")}}
    host_log = (root / "host.log").open("ab")

    def cli(*arguments):
        result = subprocess.run([str(prefix / "bin/lain"), *arguments], cwd="/", env=env,
                                capture_output=True, text=True, timeout=45)
        expect(result.returncode == 0, f"staged CLI failed: {' '.join(arguments)}: {result.stderr[-2000:]}")
        return json.loads(result.stdout)

    def api(path, method="GET", value=None):
        token = (data / "credentials/host.token").read_text().strip()
        request = Request("http://127.0.0.1:18418/api/v1" + path,
                          None if value is None else json.dumps(value).encode(),
                          {"Authorization": "Bearer " + token, "Content-Type": "application/json"}, method=method)
        with opener.open(request, timeout=35) as response:
            return json.load(response)

    def start_host():
        nonlocal host
        executable = data / "app/host/Lain.Host"
        expect(digest(executable) == plan["package"]["executables"]["host/Lain.Host"], "staged Host hash changed")
        env["LAIN_PROXY_PATH"] = str(data / "app/proxy/lain-proxy")
        host = subprocess.Popen([str(executable)], cwd="/", env=env, stdout=host_log, stderr=host_log)
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            expect(host.poll() is None, "staged Host exited during readiness")
            try:
                status = api("/status")
                if status["contractVersion"] == 1:
                    return status
            except (OSError, ValueError):
                pass
            time.sleep(.1)
        raise AssertionError("staged Host readiness timeout")

    def stop_host():
        if host is not None and host.poll() is None:
            host.terminate()
            try:
                host.wait(timeout=12)
            except subprocess.TimeoutExpired:
                host.kill()
                host.wait(timeout=5)

    def verify_core_process():
        expected = str(data / "app/proxy/lain-proxy")
        for entry in Path("/proc").iterdir():
            if not entry.name.isdigit():
                continue
            try:
                stat = (entry / "stat").read_text().rsplit(")", 1)[1].split()
                if int(stat[1]) != host.pid or os.readlink(entry / "exe") != expected:
                    continue
                expect(digest(entry / "exe") == plan["package"]["executables"]["proxy/lain-proxy"], "running Core differs from staged package")
                return {"pid": int(entry.name), "executable": expected, "sha256": digest(entry / "exe")}
            except (OSError, ValueError):
                continue
        raise AssertionError("Host did not launch the exact staged Core as its owned child")

    def inference(thread, continuation=False, alias="package-auto"):
        user = {"role": "user", "content": marker("text", "package-first")}
        inputs = [user]
        if continuation:
            inputs += [{"role": "assistant", "content": "synthetic response"},
                       {"role": "user", "content": "second synthetic turn"}]
        body = {"model": alias, "input": inputs, "store": False, "stream": False}
        token = (data / "credentials/inference.token").read_text().strip()
        request = Request("http://127.0.0.1:18417/v1/responses", json.dumps(body).encode(),
            {"Authorization": "Bearer " + token, "Content-Type": "application/json", "Thread-Id": thread})
        with opener.open(request, timeout=25) as response:
            expect(response.status == 200, "Responses router inference rejected")
            return json.load(response)

    def wait_usage(count):
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            usage = api("/usage")
            if usage["totals"]["attempts"] == count and usage["complete"]:
                attempts = api("/attempts?limit=100")["items"]
                if len(attempts) == count and all(row.get("finishedAt") for row in attempts):
                    return usage, attempts
            time.sleep(.1)
        raise AssertionError("Host did not ingest exact durable router usage")

    try:
        install = subprocess.run(["bash", str(package / "install-linux.sh"), str(package), str(prefix)],
                                 cwd="/", env=env, capture_output=True, text=True, timeout=80)
        (root / "installer.stdout").write_text(install.stdout)
        (root / "installer.stderr").write_text(install.stderr)
        expect(install.returncode == 0, "private staged package installation failed")
        actual = {name: digest(data / "app" / name) for name in plan["package"]["executables"]}
        expect(actual == plan["package"]["executables"], "staged package binary hashes differ")
        expect(digest(data / "app/SHA256SUMS") == plan["package"]["manifestSha256"], "package manifest changed during staging")
        report["stagedExecutables"] = actual
        provider = FakeProvider().__enter__()
        start_host()
        config = api("/configuration")
        expect(config["revision"] == 0 and not config["accounts"], "LAIN_HOME was not fresh")
        account_ids = {}
        for suffix in ("a", "b"):
            config = api("/accounts", "POST", {"revision": config["revision"], "value": {
                "name": "Package synthetic " + suffix, "provider": "fixture", "apiKey": "lain-synthetic-" + suffix,
                "baseUrl": provider.url}})
            account_ids[suffix] = next(account["id"] for account in config["accounts"] if account["name"] == "Package synthetic " + suffix)
            route = {"id": "package-route-" + suffix, "alias": "package-model-" + suffix, "upstreamModel": MODEL,
                     "provider": "fixture", "accountIds": [account_ids[suffix]], "pooled": False, "visible": True, "enabled": True}
            config = api("/routes/" + route["id"], "PUT", {"revision": config["revision"], "value": route})
        profile = {"id": "package-profile", "name": "Package fixture", "routeIds": ["package-route-a", "package-route-b"], "defaultRouteId": "package-route-a"}
        config = api("/profiles/package-profile", "PUT", {"revision": config["revision"], "value": profile})
        collection = {"id": "package-security", "name": "IT Sec", "routeIds": ["package-route-b", "package-route-a"]}
        config = api("/collections/package-security", "PUT", {"revision": config["revision"], "value": collection})
        # Its alphabetically earlier label must not reorder the saved custom views.
        second_collection = {"id": "package-second", "name": "A second stored collection", "routeIds": ["package-route-a"]}
        config = api("/collections/package-second", "PUT", {"revision": config["revision"], "value": second_collection})
        router = {"id": "package-router", "alias": "package-auto", "groupId": "collection:package-security",
                  "profileId": "package-profile", "costPolicy": "configured", "enabled": True}
        stale_revision = config["revision"]
        config = api("/collection-routers/package-router", "PUT", {"revision": stale_revision, "value": router})
        account_router = {"id": "package-account-router", "alias": "package-account-auto", "groupId": "account:" + account_ids["a"],
                          "profileId": "package-profile", "costPolicy": "configured", "enabled": True}
        account_saved = cli("routers", "save", account_router["id"], "--alias", account_router["alias"],
                            "--group", account_router["groupId"], "--profile", account_router["profileId"],
                            "--cost", account_router["costPolicy"], "--revision", str(config["revision"]), "--json")
        expect(account_saved["revision"] == config["revision"] + 1 and account_saved["id"] == account_router["id"],
               "typed CLI account router save did not acknowledge the exact CAS write")
        config = api("/configuration")
        report["configuration"] = config
        report["checks"].append("Host API CAS creates two synthetic accounts/routes, profile, ordered collection and router")
        routers = cli("routers", "--json")
        expect(routers["revision"] == config["revision"] and routers["routers"] == [router, account_router], "staged typed CLI router view differs from Host")
        report["cliRouters"] = routers
        export_request = {"revision": config["revision"], "executablePath": str(prefix / "bin/lain"),
                          "catalogPath": str(home / ".codex/lain.models.json"), "enableLainModelPicker": True}
        exported = api("/profiles/package-profile/codex/export", "POST", export_request)
        catalog = json.loads(exported["catalog"])
        groups = catalog["lain_model_picker"]["groups"]
        account_group_ids = {"account:" + account_id for account_id in account_ids.values()}
        expect(len(groups) == 4 and {group["id"] for group in groups[:2]} == account_group_ids and
               [group["id"] for group in groups[2:]] == ["collection:package-security", "collection:package-second"],
               "export must put exactly two account groups first, then custom collections in saved order")
        for suffix in ("a", "b"):
            account_group = next(group for group in groups if group["id"] == "account:" + account_ids[suffix])
            expect(account_group["models"] == ["package-model-" + suffix], "strict account group leaked another account's model")
            expect("Package synthetic " + suffix in account_group["name"] and "fixture" in account_group["name"].lower(),
                   "account group label lost readable account/provider identity")
            expect(account_group["router_model"] == ("package-account-auto" if suffix == "a" else None),
                   "account group attached another account's router")
        expect(len({group["name"] for group in groups[:2]}) == 2, "account groups have indistinguishable labels")
        group = groups[2]
        expect(group["name"] == "IT Sec" and group["router_model"] == "package-auto" and
               group["models"] == ["package-model-b", "package-model-a"], "export lost IT Sec name/order/router alias")
        expect(groups[3]["name"] == second_collection["name"] and groups[3]["models"] == ["package-model-a"] and
               groups[3]["router_model"] is None, "second custom collection was changed")
        expect(sum(model["slug"] == "package-auto" for model in catalog["models"]) == 1, "router virtual model missing/duplicated")
        expect(sum(model["slug"] == "package-account-auto" for model in catalog["models"]) == 1, "account router virtual model missing/duplicated")
        labels = {}
        for suffix in ("a", "b"):
            model = next(model for model in catalog["models"] if model["slug"] == "package-model-" + suffix)
            labels[suffix] = model["display_name"]
            expect("Package synthetic " + suffix in labels[suffix] and "fixture" in labels[suffix].lower(),
                   "real model display label lost readable account/provider distinction")
        expect(labels["a"] != labels["b"], "same upstream model has indistinguishable account labels")
        account_model = next(model for model in catalog["models"] if model["slug"] == "package-account-auto")
        expect("Package synthetic a" in account_model["display_name"] and "fixture" in account_model["display_name"].lower(),
               "account autorouter display label lost readable account/provider distinction")
        report["accountModelDisplayNames"] = {"package-model-a": labels["a"], "package-model-b": labels["b"],
                                             "package-account-auto": account_model["display_name"]}
        report["exportedGroups"] = groups
        save(root / "host-export.json", exported)
        installed = cli("profiles", "install-codex", "package-profile", "--model-groups", "--revision", str(config["revision"]), "--json")
        expect(json.loads(Path(installed["catalogPath"]).read_text()) == catalog, "CLI installed catalog differs from real Host export")
        expect(digest(sentinel) == plan["sentinelSha256"], "grouped CLI installation changed main Codex config")
        report["cliInstall"] = installed
        ordinary = api("/profiles/package-profile/codex/export", "POST", {**export_request, "enableLainModelPicker": False})
        expect("lain_model_picker" not in json.loads(ordinary["catalog"]) and
               all(model["slug"] not in {"package-auto", "package-account-auto"} for model in json.loads(ordinary["catalog"])["models"]), "ordinary export leaked opt-in router UI")
        applied = api("/profiles/package-profile/apply", "POST", {"revision": config["revision"]})
        expect(applied["appliedRevision"] == config["revision"], "actual package profile apply failed")
        report["runningCoreBeforeRestart"] = verify_core_process()
        thread = str(uuid.uuid4())
        report["firstResponse"] = inference(thread)
        wait_usage(1)
        pin_path = data / "proxy/events.jsonl.router-pins.json"
        pins = json.loads(pin_path.read_text())
        expect(len(pins["pins"]) == 1 and pins["pins"][0]["threadId"] == thread and
               pins["pins"][0]["routeId"] == "package-route-b" and pins["pins"][0]["accountId"] == account_ids["b"], "durable pin lost configured order or real identity")
        report["pinsBeforeRestart"] = pins
        stop_host()
        start_host()
        # Await restored runtime, not just the independently healthy Host API.
        deadline = time.monotonic() + 30
        while not api("/status")["proxyReady"]:
            expect(time.monotonic() < deadline, "Host restart did not restore owned Core")
            time.sleep(.1)
        report["runningCoreAfterRestart"] = verify_core_process()
        expect(json.loads(pin_path.read_text()) == pins, "Host restart reset durable conversation pins")
        report["secondResponse"] = inference(thread, continuation=True)
        usage, attempts = wait_usage(2)
        expect(usage["totals"]["requests"] == 2 and usage["totals"]["errors"] == 0, "Host usage request/error identity changed")
        for row in attempts:
            expect(row["requestedModel"] == "package-auto" and row["routeId"] == "package-route-b" and
                   row["accountId"] == account_ids["b"] and row["model"] == MODEL, "Host usage replaced real route/account with virtual alias")
        expect(cli("history", "--json")["totals"] == usage["totals"], "staged CLI usage differs from Host")
        rows = provider.snapshot()
        expect(len(rows) == 2 and all(row["account"] == "account-b" and row["model"] == MODEL for row in rows), "real upstream sends escaped sticky account/model")
        report.update(usage=usage, attempts=attempts, providerRequests=rows, pinsAfterRestart=json.loads(pin_path.read_text()))
        account_thread = str(uuid.uuid4())
        report["accountRouterResponse"] = inference(account_thread, alias="package-account-auto")
        account_usage, all_attempts = wait_usage(3)
        routed_attempts = [row for row in all_attempts if row["requestedModel"] == "package-account-auto"]
        expect(len(routed_attempts) == 1 and routed_attempts[0]["routeId"] == "package-route-a" and
               routed_attempts[0]["accountId"] == account_ids["a"] and routed_attempts[0]["model"] == MODEL,
               "account router did not retain its own real route/account identity")
        expect(account_usage["totals"]["requests"] == 3 and account_usage["totals"]["errors"] == 0 and
               cli("history", "--json")["totals"] == account_usage["totals"], "account-router Host/CLI usage differs")
        rows = provider.snapshot()
        expect(len(rows) == 3 and rows[-1]["account"] == "account-a" and rows[-1]["model"] == MODEL,
               "account router dispatched through a different account")
        final_pins = json.loads(pin_path.read_text())
        new_pins = [pin for pin in final_pins["pins"] if pin["threadId"] == account_thread]
        expect(len(final_pins["pins"]) == 2 and len(new_pins) == 1 and new_pins[0]["routeId"] == "package-route-a" and
               new_pins[0]["accountId"] == account_ids["a"], "account router pin has the wrong scope")
        report.update(collectionRouterUsage=usage, collectionRouterAttempts=attempts,
                      usage=account_usage, attempts=all_attempts, providerRequests=rows, finalPins=final_pins)
        before = api("/configuration")
        try:
            api("/collection-routers/package-router", "PUT", {"revision": stale_revision, "value": router})
            raise AssertionError("stale router CAS was accepted")
        except HTTPError as error:
            expect(error.code == 409, "stale router mutation did not return HTTP409")
            report["staleCas"] = {"status": error.code, "body": json.load(error)}
            error.close()
        expect(api("/configuration") == before, "stale CAS changed saved configuration")
        report["checks"] += ["real grouped Host export equals staged CLI installed catalog", "ordinary export stays outside group opt-in",
            "actual staged Core dispatches two Responses turns on first ordered account", "Host restart preserves durable pins and usage",
            "staged CLI and Host usage agree on real identities", "stale CAS returns409 without mutation",
            "two readable strict-account groups precede custom collections in saved order",
            "explicit account router exports and dispatches only its own account"]
        report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
        if isinstance(error, HTTPError):
            report["httpErrorBody"] = error.read().decode(errors="replace")[:4000]
            error.close()
    finally:
        stop_host()
        if provider is not None:
            report["providerRequests"] = provider.snapshot()
            provider.__exit__()
        host_log.close()
        report["mainSentinelSha256After"] = digest(sentinel) if sentinel.is_file() else None
        report["mainSentinelUnchanged"] = report["mainSentinelSha256After"] == plan["sentinelSha256"]
        if not report["mainSentinelUnchanged"]:
            report["status"] = "failed"
        save(root / "result.json", report)
    return 0 if report["status"] == "passed" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path)
    parser.add_argument("--output", type=Path, help="new private evidence directory, retained on failure")
    parser.add_argument("--expected-desktop-sha256", help="optional known retained Desktop executable hash")
    parser.add_argument("--run", action="store_true", help="run prepared package in isolated bwrap lane")
    parser.add_argument("--inner", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.inner:
        return inner()
    if args.package is None or args.output is None:
        parser.error("--package and --output are required")
    package, output = args.package.resolve(strict=True), args.output.absolute()
    if os.environ.get("HOME") != str(Path.home()):
        parser.error("an unchanged absolute HOME is required for synthetic bind mount")
    home = Path.home()
    if not home.is_absolute() or home == Path("/"):
        parser.error("refusing unsafe home mount")
    output.mkdir(parents=True, mode=0o700, exist_ok=False)
    (output / "package").mkdir(mode=0o700)
    (output / "tmp").mkdir(mode=0o700)
    private = output / "private-home"
    (private / ".codex").mkdir(parents=True, mode=0o700)
    sentinel = private / ".codex/config.toml"
    sentinel.write_text('# Synthetic original Codex config: never change.\nmodel = "main-sentinel"\n')
    harness = output / "harness"
    harness.mkdir(mode=0o700)
    shutil.copy2(__file__, harness / "host_model_groups.py")
    shutil.copy2(Path(__file__).resolve().parents[1] / "backend/fake_provider.py", harness / "fake_provider.py")
    executables = {name: digest(package / name) for name in ("host/Lain.Host", "cli/Lain.Cli", "proxy/lain-proxy", "desktop/Lain.Desktop")}
    if args.expected_desktop_sha256:
        expect(executables["desktop/Lain.Desktop"] == args.expected_desktop_sha256.lower(), "retained Desktop bytes differ from expected hash")
    manifest = package / "SHA256SUMS"
    shutil.copy2(manifest, output / "package-SHA256SUMS")
    metadata = package / "package-metadata.json"
    if metadata.is_file():
        shutil.copy2(metadata, output / "package-metadata.json")
    plan = {"homePath": str(home), "outerNetNamespace": os.readlink("/proc/self/ns/net"),
            "outerPidNamespace": os.readlink("/proc/self/ns/pid"), "sentinelSha256": digest(sentinel),
            "package": {"source": str(package), "manifestSha256": digest(manifest), "executables": executables,
                        "metadata": json.loads(metadata.read_text()) if metadata.is_file() else None,
                        "expectedRetainedDesktopSha256": args.expected_desktop_sha256}}
    save(output / "plan.json", plan)
    save(output / "prepared.json", {"status": "prepared-not-executed", "package": plan["package"]})
    if not args.run:
        print(f"Prepared only; no package/client started: {output}")
        return 0
    command = ["bwrap", "--die-with-parent", "--new-session", "--unshare-pid", "--unshare-net", "--unshare-ipc", "--unshare-uts",
               "--ro-bind", "/", "/", "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp",
               "--bind", str(private), str(home), "--bind", str(output), "/mnt", "--ro-bind", str(package), "/mnt/package",
               "--chdir", "/mnt", sys.executable, "/mnt/harness/host_model_groups.py", "--inner"]
    env = {key: os.environ[key] for key in ("PATH", "HOME", "USER", "LOGNAME", "LANG", "LC_ALL", "TZ") if key in os.environ}
    summary = {"status": "failed", "package": plan["package"]}
    try:
        completed = subprocess.run(command, env=env, capture_output=True, timeout=240)
        (output / "supervisor.stdout").write_bytes(completed.stdout)
        (output / "supervisor.stderr").write_bytes(completed.stderr)
        summary["exitCode"] = completed.returncode
        result = json.loads((output / "result.json").read_text())
        expect(result["status"] == "passed" and result["mainSentinelUnchanged"] and
               digest(sentinel) == plan["sentinelSha256"] and result["stagedExecutables"] == executables,
               "inner package evidence did not independently verify")
        expect(completed.returncode == 0, "isolated package probe exited unsuccessfully")
        summary["status"] = "passed"
    except Exception as error:
        summary["error"] = f"{type(error).__name__}: {error}"
        if isinstance(error, subprocess.TimeoutExpired):
            (output / "supervisor.stderr").write_bytes(error.stderr or b"")
    save(output / "summary.json", summary)
    print(json.dumps(summary))
    return 0 if summary["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
