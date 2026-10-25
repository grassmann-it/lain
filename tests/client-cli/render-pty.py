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
line_counts = {}


def fixture_args(command):
    return ["usage", "--full"] if command == "usage-full" else [command]


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
    process = subprocess.Popen(["dotnet", str(dll), "--render-fixture", *fixture_args(command)],
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


for columns in (20, 40, 60, 80, 120):
    commands = ("usage",) if columns < 60 else ("quotas", "status", "usage", "usage-full", "history", "help", "providers")
    for command in commands:
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
                assert "Quota values not reported" in plain and "No separate quota lookup" in plain and "Reset due" in plain
                assert "Remaining 12 USD-Credits" in plain and "Account ID: account-codex" in plain
            if command == "usage":
                words = " ".join(plain.split())
                assert "Personal Codex" in words and "codex" in words and "Coding credits" in words and "Disabled" in words
                assert "Missing credential" in words and "Quota not reported" in words and "40% used" in words
                assert "12 USD-Credits left" in words and "Reset due" in words and "Last known" in words
                assert "ID:" not in plain and "Source " not in plain and "Checked " not in plain
                assert "Details: lain usage --full" in words and "No quota readings" in words
                assert ("━" in plain) == (columns >= 48), "narrow compact output keeps numbers without bars"
                assert "Free credits" not in plain, "zero-only extras stay out of the first view"
                if not no_color:
                    active = ""
                    for chunk in re.split(r"(\x1b\[[0-9;]*m)", initialized):
                        if sgr.fullmatch(chunk):
                            active = chunk[2:-1]
                        elif set(active.split(";")).intersection({"31", "32", "33", "36"}):
                            assert "USD-Credits left" not in chunk and "Reset " not in chunk, "balances and resets do not inherit metric colour"
            if command == "usage-full":
                assert "ID: account-codex" in plain and "source " in plain and "Checked " in plain
                assert "Shared budget with legacy" in plain and "Used 18 USD-Credits / 20 USD-Credits" in plain
            line_counts[(command, columns, no_color)] = len(plain.splitlines())
            name = f"{command}-{columns}-{'plain' if no_color else 'color'}"
            (args.output / (name + ".txt")).write_text(plain)
            (args.output / (name + ".ansi.txt")).write_text(raw)
            checks.append(name)

for columns in (60, 80, 120):
    for no_color in (False, True):
        assert line_counts[("usage", columns, no_color)] < line_counts[("usage-full", columns, no_color)]

for command in ("quotas", "status", "usage", "usage-full", "history", "help", "providers"):
    result = subprocess.run(["dotnet", str(dll), "--render-fixture", *fixture_args(command)], capture_output=True, text=True, check=True)
    assert result.stderr == "" and "\x1b" not in result.stdout and "\t" not in result.stdout, "Pipe output must be plain"
    (args.output / f"{command}-pipe.txt").write_text(result.stdout)
    checks.append(command + "-pipe")

overview_json = None
for command in ("quotas", "status", "usage", "usage-full", "history", "providers"):
    result = subprocess.run(["dotnet", str(dll), "--render-fixture", *fixture_args(command), "--json"], capture_output=True, text=True, check=True)
    assert result.stderr == "" and "\x1b" not in result.stdout
    json.loads(result.stdout)
    if command == "usage":
        overview_json = result.stdout
    if command == "usage-full":
        assert result.stdout == overview_json, "--full must preserve overview JSON bytes"
    checks.append(command + "-json")

(args.output / "checks.json").write_text(json.dumps(checks, indent=2) + "\n")
print(f"PASS: {len(checks)} private PTY/pipe/JSON fixture captures in {args.output}")
