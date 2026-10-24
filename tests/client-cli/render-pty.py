#!/usr/bin/env python3
"""Private PTY/capture acceptance against synthetic fixtures, never the real lain host."""
import argparse
import errno
import fcntl
import json
import os
from pathlib import Path
import pty
import re
import select
import struct
import subprocess
import termios
import time
import unicodedata

parser = argparse.ArgumentParser()
parser.add_argument("--dll", required=True, type=Path)
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
dll = args.dll.resolve()
if not dll.is_file():
    parser.error("Build ClientCli.Tests first; the DLL was not found.")
args.output.mkdir(parents=True, exist_ok=True)
ansi = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
sgr = re.compile(r"\x1b\[[0-9;]*m")
checks = []


def cell_width(line):
    return sum(0 if unicodedata.combining(c) or unicodedata.category(c) == "Cf" else
               2 if unicodedata.east_asian_width(c) in ("W", "F") else 1 for c in line)


def capture(command, columns, no_color):
    master, slave = pty.openpty()
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 40, columns, 0, 0))
    env = os.environ.copy()
    env["TERM"] = "xterm-256color"
    env.pop("NO_COLOR", None)
    if no_color:
        env["NO_COLOR"] = "1"
    process = subprocess.Popen(["dotnet", str(dll), "--render-fixture", command],
                               stdin=slave, stdout=slave, stderr=slave, env=env, close_fds=True)
    os.close(slave)
    data = bytearray()
    deadline = time.monotonic() + 15
    try:
        while True:
            if time.monotonic() > deadline:
                process.kill()
                raise AssertionError("Fixture PTY timed out")
            ready, _, _ = select.select([master], [], [], 0.2)
            if not ready:
                if process.poll() is None:
                    continue
                break
            try:
                chunk = os.read(master, 65536)
            except OSError as error:
                if error.errno == errno.EIO:
                    break
                raise
            if not chunk:
                break
            data.extend(chunk)
        assert process.wait(timeout=2) == 0, "Fixture process failed"
    finally:
        os.close(master)
        if process.poll() is None:
            process.kill()
            process.wait()
    return data.decode("utf-8").replace("\r\n", "\n")


for columns in (60, 80, 120):
    for command in ("quotas", "status", "usage", "history", "help", "providers"):
        for no_color in (False, True):
            raw = capture(command, columns, no_color)
            # .NET initializes a new TTY's keypad mode before application output.
            # Strip only that known initial prefix, not arbitrary ESC commands.
            initialized = raw.removeprefix("\x1b[?1h\x1b=")
            plain = ansi.sub("", initialized)
            assert "\t" not in plain and "\x1b" not in plain, "Unexpected terminal controls"
            assert all(cell_width(line) <= columns for line in plain.splitlines()), (command, columns, plain)
            assert bool(sgr.search(initialized)) == (not no_color and command != "help"), "TTY/NO_COLOR policy mismatch"
            if command == "quotas":
                assert "40% used" in plain and "96% used" in plain and "Last known" in plain
                assert "Percentage unknown" in plain and "Quota lookup not supported" in plain and "Reset due" in plain
                assert "Remaining 12 USD-Credits" in plain and "Account ID: account-codex" in plain
            if command == "usage":
                assert "Personal Codex" in plain and "codex" in plain and "Coding credits" in plain and "Disabled" in plain
                assert "Missing credential" in plain and "Quota not reported" in plain and "40% used" in plain
            name = f"{command}-{columns}-{'plain' if no_color else 'color'}"
            (args.output / (name + ".txt")).write_text(plain)
            (args.output / (name + ".ansi.txt")).write_text(raw)
            checks.append(name)

for command in ("quotas", "status", "usage", "history", "help", "providers"):
    result = subprocess.run(["dotnet", str(dll), "--render-fixture", command], capture_output=True, text=True, check=True)
    assert result.stderr == "" and "\x1b" not in result.stdout and "\t" not in result.stdout, "Pipe output must be plain"
    (args.output / f"{command}-pipe.txt").write_text(result.stdout)
    checks.append(command + "-pipe")

for command in ("quotas", "status", "usage", "history", "providers"):
    result = subprocess.run(["dotnet", str(dll), "--render-fixture", command, "--json"], capture_output=True, text=True, check=True)
    assert result.stderr == "" and "\x1b" not in result.stdout
    json.loads(result.stdout)
    checks.append(command + "-json")

(args.output / "checks.json").write_text(json.dumps(checks, indent=2) + "\n")
print(f"PASS: {len(checks)} private PTY/pipe/JSON fixture captures in {args.output}")
