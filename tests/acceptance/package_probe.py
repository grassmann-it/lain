#!/usr/bin/env python3
"""Install an actual package into temporary own paths; start only its empty host.
No systemd operation, existing data access, or provider inference is performed.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen


def expect(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', required=True)
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    package = Path(args.package).resolve(strict=True)
    with socket.socket() as probe:
        probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        probe.bind(('127.0.0.1', 18418))
    checks = []
    with tempfile.TemporaryDirectory(prefix='lain-w11-package-') as temporary:
        root = Path(temporary)
        data, prefix, units = root / 'data', root / 'prefix', root / 'units'
        env = dict(os.environ, LAIN_HOME=str(data), LAIN_SYSTEMD_DIR=str(units))
        install = ['bash', str(package / 'install-linux.sh'), str(package), str(prefix)]
        def run(command, *, environment=env):
            return subprocess.run(command, cwd='/', env=environment, capture_output=True, text=True, timeout=60)
        result = run(install)
        expect(result.returncode == 0, 'fresh real-package installation failed')
        expect((units / 'lain.service').is_file(), 'staged unit missing')
        expect(data.stat().st_mode & 0o077 == 0, 'staged data permissions unsafe')
        checks.append('fresh actual-package installation with private state')
        client_env = dict(env)
        client_env.pop('LAIN_HOME')  # The installed wrapper must remember its selected home.
        launcher = str(prefix / 'bin/lain')
        expected_hash = hashlib.sha256((package / 'host/Lain.Host').read_bytes()).hexdigest()
        def start_and_check():
            host = data / 'app/host/Lain.Host'
            expect(hashlib.sha256(host.read_bytes()).hexdigest() == expected_hash, 'installed host hash differs')
            with (root / 'host.log').open('a') as log:
                process = subprocess.Popen([str(host)], cwd='/', env=env, stdout=log, stderr=log)
                try:
                    deadline = time.monotonic() + 25
                    while time.monotonic() < deadline:
                        expect(process.poll() is None, 'installed host exited early')
                        try:
                            token = (data / 'credentials/host.token').read_text().strip()
                            request = Request('http://127.0.0.1:18418/api/v1/status', headers={'Authorization': 'Bearer ' + token})
                            with urlopen(request, timeout=1) as response:
                                expect(response.status == 200, 'installed API not healthy')
                                break
                        except (OSError, HTTPError):
                            time.sleep(.1)
                    else:
                        raise AssertionError('installed host never became available')
                    result = run([launcher, 'status', '--json'], environment=client_env)
                    expect(result.returncode == 0, 'installed CLI failed against own host from unrelated cwd')
                    status = json.loads(result.stdout)
                    expect(status['contractVersion'] == 1, 'installed CLI returned wrong contract')
                    expect(token not in result.stdout, 'installed CLI exposed management token')
                    expect((data / 'keep-me').read_text() == 'persistent user data', 'update/rollback lost own user data')
                finally:
                    if process.poll() is None:
                        process.terminate()
                        try:
                            process.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            process.kill(); process.wait(timeout=5)
        (data / 'keep-me').write_text('persistent user data')
        start_and_check()
        original_token = (data / 'credentials/host.token').read_bytes()
        checks.append('actual installed host and CLI from unrelated cwd')
        expect(run(install).returncode == 0, 'actual package update failed')
        expect((data / 'app.previous/.lain-package').is_file(), 'update backup missing')
        start_and_check()
        checks.append('actual update with preserved state and restart')
        (data / 'app').rename(data / 'app.failed')
        (data / 'app.previous').rename(data / 'app')
        start_and_check()
        expect((data / 'credentials/host.token').read_bytes() == original_token, 'host token changed after rollback')
        checks.append('actual rollback with preserved state and restart')
        result = run([launcher, 'status', '--json'], environment=client_env)
        expect(result.returncode != 0 and not result.stdout.strip(), 'offline CLI must fail without fake stdout snapshot')
        checks.append('offline CLI fails explicitly')
    report = {'passed': True, 'checks': checks, 'providerCalls': 0, 'systemdOperations': 0, 'hostExecutableSha256': expected_hash}
    Path(args.report).write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    main()
