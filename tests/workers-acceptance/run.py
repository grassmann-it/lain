#!/usr/bin/env python3
"""Serial offline worker-release checks. No installed service, profile, credential, or provider mutation."""
import argparse
import hashlib
import json
import os
import signal
from pathlib import Path
import subprocess
import time
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="new private evidence directory")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(mode=0o700, parents=True, exist_ok=False)
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1")
    commands = [
        ["python3", "scripts/clients/test_codex_dispatch.py", "-v"],
        ["python3", "tests/codex-picker/test_prepare.py"],
        ["python3", "tests/codex-picker/test_native_code_mode_host.py"],
        ["python3", "-B", "-m", "unittest", "discover", "-s", "tests/workers-acceptance", "-p", "test_native_runner.py", "-v"],
    ]
    for project in (
        "workers-cli/Workers.Cli.Tests.csproj", "workers-host/WorkersHost.Tests.csproj",
        "model-compatibility/ModelCompatibility.Tests.csproj",
        "codex-gateway-profile/CodexGatewayProfile.Tests.csproj",
        "profile-application/ProfileApplication.Tests.csproj",
        "client-cli/ClientCli.Tests.csproj", "applied-profile/AppliedProfile.Tests.csproj",
    ):
        commands.append(["dotnet", "run", "--no-restore", "--project", "tests/" + project, "-m:1", "-p:UseSharedCompilation=false"])
    sources = sorted(set(
        [ROOT / "scripts/build-codex-picker.sh", ROOT / "src/Lain.Host/Program.cs", ROOT / "src/Lain.Cli/CliApplication.cs",
         ROOT / "clients/codex/qualified-releases.json"] +
        [p for base in ("src/Lain.Cli", "src/Lain.Client", "src/Lain.Core", "src/Lain.Host/Workers", "src/Lain.Host/Models", "scripts/clients", "skills/lain-workers",
                        "tests/workers-cli", "tests/workers-host", "tests/workers-acceptance", "tests/codex-picker", "tests/model-compatibility",
                        "tests/codex-gateway-profile", "tests/profile-application", "tests/client-cli", "tests/applied-profile", "clients/codex")
         for p in (ROOT / base).rglob("*") if p.is_file() and p.suffix in (".cs", ".csproj", ".py", ".md", ".json", ".patch") and not {"bin", "obj", "__pycache__"}.intersection(p.parts)] +
        [ROOT / "scripts/prepare-codex-picker.py", ROOT / "docs/workers-v1.md"]
    ))
    report = {"version": 1, "startedAt": datetime.now(timezone.utc).isoformat(), "state": "running",
              "providerInference": False, "installedStateChanged": False,
              "sourceHashes": {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}, "checks": []}
    def save():
        path = output / "report.json"
        descriptor = os.open(path, os.O_CREAT | os.O_TRUNC | os.O_WRONLY, 0o600)
        with os.fdopen(descriptor, "w") as file:
            json.dump(report, file, indent=2)
            file.write("\n")
    save()
    for index, command in enumerate(commands, 1):
        start = time.monotonic()
        try:
            with subprocess.Popen(command, cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE,
                                  stderr=subprocess.STDOUT, start_new_session=True) as process:
                try:
                    log, _ = process.communicate(timeout=300)
                    code = process.returncode
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    log, _ = process.communicate(timeout=10)
                    code, log = -1, "Timed out after 300 seconds.\n" + log
                finally:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
        except OSError as error:
            code, log = -1, f"Could not run check: {error}\n"
        name = f"{index:02d}.log"
        descriptor = os.open(output / name, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        with os.fdopen(descriptor, "w") as file:
            file.write(log)
        report["checks"].append({"command": command, "exitCode": code, "passed": code == 0,
                                 "durationSeconds": round(time.monotonic() - start, 3), "log": name})
        save()
        print(f"{'PASS' if code == 0 else 'FAIL'} {index}/{len(commands)}: {' '.join(command)}", flush=True)
        if code:
            print(log[-5000:], flush=True)
    report["state"] = "passed" if all(check["passed"] for check in report["checks"]) else "failed"
    report["finishedAt"] = datetime.now(timezone.utc).isoformat()
    save()
    print(f"{report['state']}: {output / 'report.json'}")
    return 0 if report["state"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
