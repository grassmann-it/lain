#!/usr/bin/env python3
"""Prepare, and only with --run execute, isolated synthetic native /model acceptance."""
from __future__ import annotations
import argparse
import codecs
import copy
import fcntl
import hashlib
import http.server
import json
import os
from pathlib import Path
import pty
import select
import shutil
import signal
import struct
import subprocess
import sys
import termios
import threading
import time
import unicodedata

COLS, ROWS = 120, 45
PORT = 18499


class Terminal:
    """Small VT screen recorder for assertions; raw terminal bytes are retained too."""
    def __init__(self):
        self.grid = [[" "] * COLS for _ in range(ROWS)]
        self.row = self.col = 0
        self.saved = (0, 0)
        self.state = "text"
        self.sequence = ""
        self.decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self.responses = []

    def feed(self, data):
        for char in self.decoder.decode(data):
            if self.state == "osc":
                if char == "\a": self.state = "text"
                elif char == "\x1b": self.state = "osc_escape"
                continue
            if self.state == "osc_escape":
                self.state = "text" if char == "\\" else "osc"
                continue
            if self.state == "charset":
                self.state = "text"
                continue
            if self.state == "escape":
                if char == "7": self.saved = (self.row, self.col)
                elif char == "8": self.row, self.col = self.saved
                self.state = {"[": "csi", "]": "osc", "P": "osc", "(": "charset", ")": "charset"}.get(char, "text")
                self.sequence = ""
                continue
            if self.state == "csi":
                if "@" <= char <= "~":
                    self.csi(self.sequence, char)
                    self.state = "text"
                else: self.sequence += char
                continue
            if char == "\x1b": self.state = "escape"
            elif char == "\r": self.col = 0
            elif char == "\n": self.newline()
            elif char == "\b": self.col = max(0, self.col - 1)
            elif char == "\t": self.col = min(COLS - 1, (self.col // 8 + 1) * 8)
            elif ord(char) >= 32 and not unicodedata.combining(char):
                width = 2 if unicodedata.east_asian_width(char) in ("W", "F") else 1
                if self.col + width > COLS:
                    self.col = 0
                    self.newline()
                self.grid[self.row][self.col] = char
                if width == 2: self.grid[self.row][self.col + 1] = ""
                self.col += width

    def newline(self):
        self.row += 1
        if self.row >= ROWS:
            self.grid.pop(0)
            self.grid.append([" "] * COLS)
            self.row = ROWS - 1

    def csi(self, raw, command):
        private = raw.startswith("?")
        raw = raw.lstrip("?>")
        try: values = [int(item) if item else 0 for item in raw.split(";")]
        except ValueError: return
        n = values[0] or 1
        if command in "Hf":
            self.row = min(ROWS - 1, max(0, n - 1))
            self.col = min(COLS - 1, max(0, (values[1] or 1) - 1)) if len(values) > 1 else 0
        elif command == "A": self.row = max(0, self.row - n)
        elif command == "B": self.row = min(ROWS - 1, self.row + n)
        elif command == "C": self.col = min(COLS - 1, self.col + n)
        elif command == "D": self.col = max(0, self.col - n)
        elif command == "E": self.row = min(ROWS - 1, self.row + n); self.col = 0
        elif command == "F": self.row = max(0, self.row - n); self.col = 0
        elif command == "s": self.saved = (self.row, self.col)
        elif command == "u": self.row, self.col = self.saved
        elif command == "L":
            for _ in range(min(n, ROWS - self.row)):
                self.grid.insert(self.row, [" "] * COLS); self.grid.pop()
        elif command == "M":
            for _ in range(min(n, ROWS - self.row)):
                self.grid.pop(self.row); self.grid.append([" "] * COLS)
        elif command == "S":
            for _ in range(min(n, ROWS)):
                self.grid.pop(0); self.grid.append([" "] * COLS)
        elif command == "T":
            for _ in range(min(n, ROWS)):
                self.grid.insert(0, [" "] * COLS); self.grid.pop()
        elif command == "@":
            for _ in range(min(n, COLS - min(self.col, COLS))):
                self.grid[self.row].insert(self.col, " "); self.grid[self.row].pop()
        elif command == "P":
            for _ in range(min(n, COLS - min(self.col, COLS))):
                self.grid[self.row].pop(self.col); self.grid[self.row].append(" ")
        elif command == "X":
            end = min(COLS, self.col + n)
            self.grid[self.row][self.col:end] = [" "] * (end - self.col)
        elif command == "G": self.col = min(COLS - 1, n - 1)
        elif command == "d": self.row = min(ROWS - 1, n - 1)
        elif command == "J":
            if values[0] in (2, 3): self.grid = [[" "] * COLS for _ in range(ROWS)]
            elif values[0] == 0:
                self.grid[self.row][min(self.col, COLS):] = [" "] * (COLS - min(self.col, COLS))
                for row in range(self.row + 1, ROWS): self.grid[row] = [" "] * COLS
        elif command == "K":
            start, end = (0, COLS) if values[0] == 2 else (0, min(self.col + 1, COLS)) if values[0] == 1 else (min(self.col, COLS), COLS)
            self.grid[self.row][start:end] = [" "] * (end - start)
        elif command == "n" and values[0] == 6:
            self.responses.append(f"\x1b[{self.row + 1};{min(self.col + 1, COLS)}R".encode())
        elif command == "c": self.responses.append(b"\x1b[?1;2c")
        elif command == "h" and private and 1049 in values:
            self.grid = [[" "] * COLS for _ in range(ROWS)]
            self.row = self.col = 0

    def text(self):
        return "\n".join("".join(row).rstrip() for row in self.grid)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def catalog(source):
    # This public tag stores the authoritative seed under models-manager, not core/models.json.
    seed = json.loads((source / "codex-rs/models-manager/models.json").read_text())["models"][0]
    records = []
    for slug, label, visible in [("synthetic/free-a", "Visible Free", True), ("synthetic/shared", "Shared Model", True),
                                 ("synthetic/security", "Security Model", True), ("synthetic/hidden", "Hidden Model", False),
                                 ("codex-auto-review", "Native review marker", True)]:
        model = copy.deepcopy(seed)
        model.update(slug=slug, display_name=label, description="Synthetic native picker fixture; no inference",
                     visibility="list" if visible else "hide", supported_in_api=True, priority=len(records),
                     default_reasoning_level="none", supported_reasoning_levels=[], base_instructions="Synthetic fixture.",
                     multi_agent_version=None, multi_agent_reasoning_effort=None, prefer_websockets=False,
                     availability_nux=None)
        records.append(model)
    return {"models": records, "lain_model_picker": {"schema_version": 1, "groups": [
        {"id": "free", "name": "Free", "router_model": None, "models": ["synthetic/free-a", "synthetic/shared", "synthetic/hidden"]},
        {"id": "it-sec", "name": "IT Sec", "models": ["synthetic/shared", "synthetic/security"]},
        {"id": "provider", "name": "Provider", "models": ["synthetic/free-a", "synthetic/shared", "synthetic/security"]},
    ]}}


def config(home, provider):
    return f'''# Synthetic main sentinel: unchanged across native /model operations.
model = "synthetic/free-a"
model_provider = {json.dumps(provider)}
model_catalog_json = {json.dumps(str(home / ".codex/models.json"))}
check_for_update_on_startup = false
cli_auth_credentials_store = "file"

[model_providers.{provider}]
name = "Synthetic loopback only"
base_url = "http://127.0.0.1:{PORT}/v1"
wire_api = "responses"
env_key = "LAIN_NATIVE_TEST_KEY"
requires_openai_auth = false
request_max_retries = 0
stream_max_retries = 0

[projects."/mnt/work"]
trust_level = "trusted"

[history]
persistence = "none"
'''


def prepare(args):
    args.output.mkdir(parents=True, exist_ok=False)
    current_home = Path.home()
    if not current_home.is_absolute() or str(current_home) == "/":
        raise SystemExit("a normal absolute existing HOME path is required")
    fixture = catalog(args.source)
    cases = [("lain-groups", "lain", "lain", False), ("normal-provider-lain", None, "lain", False),
             ("other-profile-provider-lain", "other", "lain", False), ("lain-profile-other-provider", "lain", "fixture", False),
             ("normal-invalid-extension", None, "lain", True), ("other-profile-invalid-extension", "other", "lain", True),
             ("lain-profile-other-provider-invalid-extension", "lain", "fixture", True)]
    for name, profile, provider, invalid in cases:
        case = args.output / name
        private = case / "private-home"
        (private / ".codex").mkdir(parents=True)
        (case / "work").mkdir()
        data = copy.deepcopy(fixture)
        if invalid: data["lain_model_picker"] = "deliberately invalid; upstream must ignore outside exact scope"
        (private / ".codex/models.json").write_text(json.dumps(data))
        main = config(current_home, provider)
        (private / ".codex/config.toml").write_text(main)
        for selected in ("lain", "other"):
            (private / f".codex/{selected}.config.toml").write_text(config(current_home, provider))
        (case / "codex").touch()
        shutil.copyfile(__file__, case / "native.py")
        plan = {"case": name, "profile": profile, "provider": provider, "grouped": name == "lain-groups",
                "homePath": str(current_home), "mainSentinelSha256": digest(private / ".codex/config.toml")}
        (case / "plan.json").write_text(json.dumps(plan, indent=2) + "\n")
    (args.output / "prepared.json").write_text(json.dumps({"status": "prepared-not-executed", "binary": str(args.binary),
        "binarySha256": digest(args.binary), "source": str(args.source), "cases": [item[0] for item in cases]}, indent=2) + "\n")
    return cases


def inner():
    plan = json.loads(Path("/mnt/plan.json").read_text())
    home = Path(plan["homePath"])
    requests = []
    class Deny(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            requests.append({"method": self.command, "path": self.path})
            self.send_error(503, "Synthetic fixture denies all provider traffic")
        do_POST = do_PUT = do_DELETE = do_PATCH = do_GET
        def log_message(self, *_): pass
    server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Deny)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    env = {key: os.environ[key] for key in ("PATH", "HOME", "USER", "LOGNAME", "SHELL", "LANG", "LC_ALL", "TZ") if key in os.environ}
    # HOME retains its original value. bwrap mounts the private synthetic tree at that exact path.
    env.update(TERM="xterm-256color", LAIN_NATIVE_TEST_KEY="synthetic-no-inference")
    command = ["/mnt/codex", "--no-daemon", "--no-alt-screen"]
    if plan["profile"]: command += ["--profile", plan["profile"]]
    screen = Terminal()
    pid, master = pty.fork()
    if pid == 0:
        os.chdir("/mnt/work")
        os.execve(command[0], command, env)
    fcntl.ioctl(master, termios.TIOCSWINSZ, struct.pack("HHHH", ROWS, COLS, 0, 0))
    raw = Path("/mnt/session.ansi").open("wb")
    captures = []
    def pump(timeout=0.1):
        if select.select([master], [], [], timeout)[0]:
            try: data = os.read(master, 262144)
            except OSError: data = b""
            if not data: raise RuntimeError("native client exited before the expected UI state")
            raw.write(data); raw.flush(); screen.feed(data)
            for response in screen.responses: os.write(master, response)
            screen.responses.clear()
    def wait(predicate, label, timeout=25):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if predicate():
                captures.append(label)
                Path(f"/mnt/{len(captures):02d}-{label}.txt").write_text(screen.text())
                return
            pump()
        raise AssertionError(f"timed out waiting for {label}\n{screen.text()}")
    def send(data): os.write(master, data.encode() if isinstance(data, str) else data)
    def has(*labels): return all(label in screen.text() for label in labels)
    def choose(index): send(b"\x1b[H" + b"\x1b[B" * index + b"\r")
    report = {"case": plan["case"], "status": "failed", "command": command, "captures": captures}
    try:
        wait(lambda: "›" in screen.text(), "ready")
        send("/model\r")
        if plan["grouped"]:
            wait(lambda: has("Modellgruppe wählen", "Free", "IT Sec", "Provider", "Alle"), "groups")
            if has("Visible Free") or has("Hidden Model"): raise AssertionError("members leaked into group list")
            choose(0)
            wait(lambda: has("Free · Modell wählen", "Automatisch", "Noch nicht eingerichtet", "Visible Free", "Shared Model"), "free")
            if has("Hidden Model") or has("Security Model"): raise AssertionError("free membership/visibility mismatch")
            # Disabled Automatisch is skipped. First selectable model is Visible Free; next is Shared Model.
            choose(1)
            profile_file = home / ".codex/lain.config.toml"
            wait(lambda: 'model = "synthetic/shared"' in profile_file.read_text(), "wire-slug-persisted")
            send("/model\r")
            wait(lambda: has("Modellgruppe wählen"), "groups-after-persist")
            choose(1)
            wait(lambda: has("IT Sec · Modell wählen", "Shared Model", "Security Model"), "overlap")
            if has("Visible Free") or has("Hidden Model"): raise AssertionError("IT Sec membership mismatch")
            send(b"\x1b")
            wait(lambda: has("Modellgruppe wählen"), "escape-to-groups")
            choose(2)
            wait(lambda: has("Provider · Modell wählen", "Visible Free", "Shared Model", "Security Model"), "provider")
            send(b"\x1b")
            wait(lambda: has("Modellgruppe wählen"), "escape-provider")
            choose(3)
            wait(lambda: has("Alle · Modell wählen", "Visible Free", "Shared Model", "Security Model", "Native review marker"), "all")
            if has("Hidden Model"): raise AssertionError("hidden model appeared in Alle")
        else:
            wait(lambda: has("All models", "Native review marker"), "upstream-quick-picker")
            if has("Modellgruppe wählen"): raise AssertionError("group extension escaped its exact profile/provider scope")
        if digest(home / ".codex/config.toml") != plan["mainSentinelSha256"]:
            raise AssertionError("synthetic ordinary config sentinel changed")
        if requests: raise AssertionError(f"provider traffic attempted: {requests}")
        report["status"] = "passed"
        report["mainSentinelUnchanged"] = True
    except Exception as error:
        report["error"] = str(error)
    finally:
        Path("/mnt/final-screen.txt").write_text(screen.text())
        report["providerRequests"] = requests
        report["networkNamespace"] = "isolated"
        Path("/mnt/result.json").write_text(json.dumps(report, indent=2) + "\n")
        server.shutdown()
        try: os.killpg(pid, signal.SIGTERM)
        except ProcessLookupError: pass
        try: os.waitpid(pid, 0)
        except ChildProcessError: pass
        os.close(master); raw.close()
    return 0 if report["status"] == "passed" else 1


def main():
    if sys.argv[1:] == ["--inner"]: return inner()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", required=True, type=Path, help="exact newly built patched binary, never production Codex")
    parser.add_argument("--source", required=True, type=Path, help="prepared pinned public source")
    parser.add_argument("--output", required=True, type=Path, help="new artifact directory, never overwritten")
    parser.add_argument("--run", action="store_true", help="explicitly run native cases; otherwise prepare fixtures only")
    args = parser.parse_args()
    args.binary = args.binary.resolve(strict=True); args.source = args.source.resolve(strict=True); args.output = args.output.absolute()
    if not os.access(args.binary, os.X_OK): parser.error("--binary must be executable")
    if not (args.source / "lain-picker-source.json").is_file(): parser.error("--source must have pinned preparer provenance")
    cases = prepare(args)
    if not args.run:
        print(f"Prepared {len(cases)} cases; no client started: {args.output}")
        return 0
    statuses = []
    for name, *_ in cases:
        case = args.output / name
        command = ["bwrap", "--die-with-parent", "--new-session", "--unshare-pid", "--unshare-net", "--unshare-ipc", "--unshare-uts",
                   "--ro-bind", "/", "/", "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp",
                   "--bind", str(case / "private-home"), str(Path.home()), "--bind", str(case), "/mnt",
                   "--ro-bind", str(args.binary), "/mnt/codex", "--chdir", "/mnt/work", sys.executable, "/mnt/native.py", "--inner"]
        # No user shell, DISPLAY, credential environment, HOME change, or CODEX_HOME override.
        env = {key: os.environ[key] for key in ("PATH", "HOME", "USER", "LOGNAME", "SHELL", "LANG", "LC_ALL", "TZ") if key in os.environ}
        try:
            result = subprocess.run(command, env=env, capture_output=True, timeout=100)
            (case / "supervisor.stderr").write_bytes(result.stderr)
            statuses.append({"case": name, "exitCode": result.returncode})
        except subprocess.TimeoutExpired:
            statuses.append({"case": name, "exitCode": "timeout"})
    summary = {"status": "passed" if all(item["exitCode"] == 0 for item in statuses) else "failed", "cases": statuses}
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps(summary))
    return 0 if summary["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
