#!/usr/bin/env python3
"""Prepare or run an existing native harness in private Xvfb/D-Bus namespaces.

Never builds. Default only prepares. --run requires the coordinator's runtime
slot. Reuses the desktop operators' displayfd/Xauthority/owned-child lifecycle,
with a synthetic filesystem mounted at unchanged HOME instead of assigning it.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import time


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n")


def require(value, message):
    if not value:
        raise ValueError(message)


def selected_environment(source):
    # HOME may be inherited unchanged; no desktop bus, provider credentials,
    # proxy setting, user dotnet startup hook or CODEX_HOME crosses this boundary.
    return {key: source[key] for key in ("PATH", "HOME", "USER", "LOGNAME", "LANG", "LC_ALL", "TZ") if key in source}


def parse_scale(text):
    require(text in ("1", "1.25"), "N09 supports only reviewed 100/125 percent scale")
    return text


def artifact_inventory(directory):
    records = {}
    for path in sorted(Path(directory).rglob("*")):
        require(not path.is_symlink(), "Harness artifact symlinks are not accepted")
        if path.is_file():
            records[str(path.relative_to(directory))] = sha(path)
    return records


def prepare(args):
    assembly = args.assembly.resolve(strict=True)
    require(assembly.is_file() and assembly.suffix == ".dll", "An existing approved harness DLL is required")
    require(args.source_commit and len(args.source_commit) == 40 and all(c in "0123456789abcdef" for c in args.source_commit),
            "--source-commit must be the full source pin used for this build")
    home = Path(os.environ.get("HOME", ""))
    require(home.is_absolute() and home != Path("/") and home == Path.home(), "Unchanged absolute HOME required")
    output = args.output.absolute()
    require(output.is_relative_to(Path.home() / ".cache"), "N09 output/runtime files must stay under the user's cache")
    require(not output.is_relative_to(assembly.parent), "Evidence must be outside the mounted artifact directory")
    output.mkdir(parents=True, mode=0o700, exist_ok=False)
    for name in ("private-home", "artifacts", "runtime", "evidence", "tmp"):
        (output / name).mkdir(mode=0o700)
    shutil.copy2(__file__, output / "runner.py")
    synthetic_codex = output / "private-home/.codex"
    synthetic_codex.mkdir(mode=0o700)
    (synthetic_codex / "config.toml").write_text('# N09 synthetic ordinary Codex config\nmodel = "n09-main-sentinel"\n')
    plan = {"schemaVersion": 1, "state": "prepared-not-executed", "sourceCommit": args.source_commit,
            "homePath": str(home), "assembly": assembly.name, "artifactDirectory": str(assembly.parent),
            "artifacts": artifact_inventory(assembly.parent), "scale": parse_scale(args.scale),
            "arguments": args.argument, "uxdFilter": args.uxd_filter, "nativeEditorFilter": args.native_editor_filter,
            "outerNetNamespace": os.readlink("/proc/self/ns/net"), "outerPidNamespace": os.readlink("/proc/self/ns/pid"),
            "timeoutSeconds": args.timeout, "runnerSha256": sha(output / "runner.py"),
            "visualReview": "not-run", "accessibilityReview": "not-run",
            "scope": "Existing production-assembly fixture harness only; no build, real Host/provider or installed application"}
    write_json(output / "plan.json", plan)
    return plan


def stop_owned(process):
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=3)


def inner():
    root = Path("/mnt")
    plan = json.loads((root / "plan.json").read_text())
    require(os.environ.get("HOME") == plan["homePath"], "HOME changed across private namespace")
    require(os.readlink("/proc/self/ns/net") != plan["outerNetNamespace"] and
            os.readlink("/proc/self/ns/pid") != plan["outerPidNamespace"], "Private network/PID namespaces absent")
    require(artifact_inventory(root / "artifacts") == plan["artifacts"], "Mounted artifacts differ from reviewed inventory")
    # This is the namespace-owned /tmp tmpfs, never the user's X11 socket dir.
    x11_sockets = Path("/tmp/.X11-unix")
    x11_sockets.mkdir(mode=0o1777)
    x11_sockets.chmod(0o1777)
    env = selected_environment(os.environ)
    runtime = root / "runtime"
    for name in ("xdg", "config", "cache", "data", "lain"):
        (runtime / name).mkdir(mode=0o700)
    env.update(TMPDIR=str(root / "tmp"), XDG_RUNTIME_DIR=str(runtime / "xdg"),
               XDG_CONFIG_HOME=str(runtime / "config"), XDG_CACHE_HOME=str(runtime / "cache"),
               XDG_DATA_HOME=str(runtime / "data"), LAIN_HOME=str(runtime / "lain"),
               GDK_BACKEND="x11", QT_QPA_PLATFORM="xcb", AVALONIA_USE_WAYLAND="0",
               AVALONIA_GLOBAL_SCALE_FACTOR=plan["scale"], LAIN_TEST_SCALE=plan["scale"],
               LAIN_TEST_COMMIT=plan["sourceCommit"], LAIN_TEST_OUTPUT="/mnt/evidence",
               DOTNET_CLI_TELEMETRY_OPTOUT="1", LIBGL_ALWAYS_SOFTWARE="1",
               GALLIUM_DRIVER="llvmpipe",
               __EGL_VENDOR_LIBRARY_FILENAMES="/usr/share/glvnd/egl_vendor.d/50_mesa.json")
    if plan["uxdFilter"]:
        env["LAIN_UXD_ONLY"] = plan["uxdFilter"]
    if plan["nativeEditorFilter"]:
        env["LAIN_NATIVE_GATES_ONLY"] = plan["nativeEditorFilter"]
    authority = runtime / "Xauthority"
    authority.touch(mode=0o600)
    xvfb = bus = harness = None
    result = {"schemaVersion": 1, "state": "failed", "sourceCommit": plan["sourceCommit"],
              "scale": plan["scale"], "artifacts": plan["artifacts"], "runnerSha256": plan["runnerSha256"],
              "netNamespace": os.readlink("/proc/self/ns/net"), "pidNamespace": os.readlink("/proc/self/ns/pid"),
              "visualReview": "required-after-capture", "accessibilityReview": "not-certified",
              "note": "Exit status is execution evidence, not automatic visual or full-matrix acceptance"}
    logs = [(root / name).open("wb") for name in ("xvfb.log", "dbus.log", "run.log")]
    display_read, display_write = os.pipe()
    try:
        xvfb = subprocess.Popen(["Xvfb", "-displayfd", str(display_write), "-screen", "0", "1920x1400x24",
                                 "-nolisten", "tcp", "-extension", "GLX", "-auth", str(authority)],
                                env=env, stdout=logs[0], stderr=logs[0], pass_fds=(display_write,))
        os.close(display_write)
        display_write = None
        os.set_blocking(display_read, False)
        allocated = b""
        deadline = time.monotonic() + 10
        while b"\n" not in allocated:
            require(xvfb.poll() is None, "Private Xvfb exited during startup")
            require(time.monotonic() < deadline, "Private Xvfb displayfd timeout")
            try:
                allocated += os.read(display_read, 64)
            except BlockingIOError:
                pass
            time.sleep(.05)
        number = allocated.decode().strip()
        require(number.isdigit(), "Invalid Xvfb displayfd value")
        display = ":" + number
        subprocess.run(["xauth", "-f", str(authority), "add", display, "MIT-MAGIC-COOKIE-1", secrets.token_hex(16)],
                       env=env, check=True, stdout=logs[0], stderr=logs[0], timeout=5)
        socket_path = runtime / "session-bus"
        address = "unix:path=" + str(socket_path)
        bus = subprocess.Popen(["dbus-daemon", "--session", "--nofork", "--address=" + address],
                               env=env, stdout=logs[1], stderr=logs[1])
        deadline = time.monotonic() + 10
        while not socket_path.exists():
            require(bus.poll() is None, "Private session bus exited during startup")
            require(time.monotonic() < deadline, "Private session bus readiness timeout")
            time.sleep(.05)
        env.update(DISPLAY=display, XAUTHORITY=str(authority), LAIN_TEST_DISPLAY=display,
                   LAIN_TEST_XVFB_PID=str(xvfb.pid), DBUS_SESSION_BUS_ADDRESS=address,
                   LAIN_TEST_SESSION_BUS=address, LAIN_TEST_DBUS_PID=str(bus.pid))
        command = ["dotnet", "/mnt/artifacts/" + plan["assembly"], *plan["arguments"]]
        result["command"] = command
        harness = subprocess.Popen(command, cwd=root / "evidence", env=env, stdout=logs[2], stderr=logs[2])
        code = harness.wait(timeout=plan["timeoutSeconds"])
        result.update(exitCode=code, state="completed" if code == 0 else "failed")
    except Exception as error:
        result["error"] = type(error).__name__ + ": " + str(error)
    finally:
        stop_owned(harness)
        stop_owned(bus)
        stop_owned(xvfb)
        os.close(display_read)
        if display_write is not None:
            os.close(display_write)
        for log in logs:
            log.close()
        result["evidenceFiles"] = {str(path.relative_to(root)): sha(path) for path in (root / "evidence").rglob("*") if path.is_file() and not path.is_symlink()}
        result["ordinaryConfigSha256After"] = sha(Path(plan["homePath"]) / ".codex/config.toml") if (Path(plan["homePath"]) / ".codex/config.toml").is_file() else None
        # Some existing synthetic setup scenarios deliberately replace their own
        # sentinel. Report its final hash; their own preservation assertion applies.
        write_json(root / "run-result.json", result)
    return 0 if result["state"] == "completed" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assembly", type=Path)
    parser.add_argument("--output", type=Path, help="new cache-backed evidence directory")
    parser.add_argument("--source-commit")
    parser.add_argument("--scale", default="1", choices=("1", "1.25"))
    parser.add_argument("--argument", action="append", default=[], help="harness argument, use --argument=--flag")
    parser.add_argument("--uxd-filter")
    parser.add_argument("--native-editor-filter")
    parser.add_argument("--timeout", type=int, default=360)
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--inner", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.inner:
        return inner()
    if args.assembly is None or args.output is None or not 1 <= args.timeout <= 480:
        parser.error("--assembly, --output and timeout within 1..480 are required")
    plan = prepare(args)
    if not args.run:
        print(f"Prepared only; no GUI/build/process started: {args.output}")
        return 0
    output = args.output.absolute()
    command = ["bwrap", "--die-with-parent", "--new-session", "--unshare-pid", "--unshare-net", "--unshare-ipc", "--unshare-uts",
               "--ro-bind", "/", "/", "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp",
               "--bind", str(output / "private-home"), plan["homePath"], "--bind", str(output), "/mnt",
               "--ro-bind", plan["artifactDirectory"], "/mnt/artifacts", "--chdir", "/mnt",
               sys.executable, "/mnt/runner.py", "--inner"]
    summary = {"state": "failed", "planSha256": sha(output / "plan.json"), "visualReview": "required-after-capture"}
    try:
        completed = subprocess.run(command, env=selected_environment(os.environ), capture_output=True, timeout=args.timeout + 45)
        (output / "supervisor.stdout").write_bytes(completed.stdout)
        (output / "supervisor.stderr").write_bytes(completed.stderr)
        summary["exitCode"] = completed.returncode
        result = json.loads((output / "run-result.json").read_text())
        require(result["sourceCommit"] == plan["sourceCommit"] and result["artifacts"] == plan["artifacts"], "Evidence pin mismatch")
        require(result["state"] == "completed" and completed.returncode == 0, "Native harness did not complete successfully")
        summary["state"] = "completed"
    except Exception as error:
        summary["error"] = type(error).__name__ + ": " + str(error)
        if isinstance(error, subprocess.TimeoutExpired):
            (output / "supervisor.stderr").write_bytes(error.stderr or b"")
    write_json(output / "summary.json", summary)
    print(json.dumps(summary))
    return 0 if summary["state"] == "completed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
