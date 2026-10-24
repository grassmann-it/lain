#!/usr/bin/env python3
"""Exercise only synthetic wizard Host fixtures in private PTYs."""
import argparse
import errno
import os
from pathlib import Path
import pty
import select
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument("--dll", required=True, type=Path)
args = parser.parse_args()
secret = "SYNTHETIC-WIZARD-KEY"


def exercise(cancel=False):
    master, slave = pty.openpty()
    process = subprocess.Popen(["dotnet", str(args.dll.resolve()), "--wizard-fixture"], stdin=slave, stdout=slave, stderr=slave, close_fds=True)
    os.close(slave)
    output = bytearray()
    cursor = 0

    def until(marker):
        nonlocal cursor
        deadline = time.monotonic() + 10
        while marker.encode() not in output[cursor:]:
            if time.monotonic() >= deadline:
                raise AssertionError("Wizard prompt timed out: " + marker + "\n" + output.decode(errors="replace"))
            if select.select([master], [], [], 0.1)[0]:
                chunk = os.read(master, 65536)
                if not chunk:
                    raise AssertionError("Wizard exited before " + marker)
                output.extend(chunk)
        cursor = len(output)

    try:
        for prompt, answer in [("Provider number", "1"), ("Access / billing plan number", "1"), ("Account ID", "scope-one")]:
            until(prompt + ": ")
            os.write(master, (answer + "\n").encode())
        until("API key (hidden): ")
        os.write(master, secret.encode())
        # Pause within the secret prompt to catch kernel and application echo.
        time.sleep(0.1)
        while select.select([master], [], [], 0)[0]:
            output.extend(os.read(master, 65536))
        assert secret.encode() not in output, "Secret was echoed by terminal"
        os.write(master, b"\x1b" if cancel else b"\n")
        if not cancel:
            until("Display name [Fixture 2]: ")
            os.write(master, b"\n")
            until("Model numbers (comma-separated) (optional): ")
            os.write(master, b"1,2\n")
            until("Save locally? Type yes (optional): ")
            os.write(master, b"yes\n")
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            if select.select([master], [], [], 0.1)[0]:
                try:
                    chunk = os.read(master, 65536)
                except OSError as error:
                    if error.errno == errno.EIO:
                        break
                    raise
                if not chunk:
                    break
                output.extend(chunk)
            elif process.poll() is not None:
                break
        assert process.wait(timeout=2) == 0, output.decode(errors="replace")
        text = output.decode(errors="replace")
        assert secret not in text
        assert ("No account was saved" if cancel else "Account saved locally") in text
    finally:
        if process.poll() is None:
            process.kill()
            process.wait()
        os.close(master)


exercise()
exercise(cancel=True)
result = subprocess.run(["dotnet", str(args.dll.resolve()), "--wizard-fixture"], input=secret + "\n", capture_output=True, text=True, timeout=5)
assert result.returncode == 2 and "--key-stdin" in result.stderr and secret not in result.stdout + result.stderr
print("PASS: private wizard PTY save, hidden secret input, cancel and redirected-input rejection")
