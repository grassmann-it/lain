#!/usr/bin/env python3
"""Synthetic packaged Host startup failures, in a private network namespace.

Run via unshare --user --map-root-user --net after bringing lo up. Never uses
installed state, provider credentials, or provider network access.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--host', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    interfaces = {line.split(':', 1)[0].strip()
                  for line in Path('/proc/net/dev').read_text().splitlines()[2:] if ':' in line}
    if interfaces != {'lo'}:
        raise SystemExit('A private loopback-only network namespace is required.')
    http = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    cases = [
        ('unqualified', 'Proxy backend qualification has not passed.'),
        ('launch', 'The proxy process could not be launched.'),
        ('early-exit', 'The proxy exited before becoming ready (exit code 7).'),
        ('metadata', 'Saved proxy runtime metadata is inconsistent. Review the applied profile.'),
    ]
    checks = []
    with tempfile.TemporaryDirectory(prefix='lain-startup-probe-') as temporary:
        for case, expected in cases:
            root = Path(temporary) / case
            root.mkdir(mode=0o700)
            (root / 'proxy').mkdir(mode=0o700)
            core = root / 'proxy/fake-core'
            core.write_text('#!/bin/sh\nprintf "synthetic-private-output-do-not-forward\\n" >&2\nexit 7\n')
            core.chmod(0o600 if case == 'launch' else 0o700)
            digest = hashlib.sha256(core.read_bytes()).hexdigest()
            (root / 'proxy/binary-manifest.json').write_text(json.dumps({'sha256': digest}))
            (root / 'proxy/qualification.json').write_text(json.dumps({'sha256': digest, 'passed': case != 'unqualified'}))
            (root / 'proxy/config.yaml').write_text('port: 18417\n')
            (root / 'proxy/runtime.json').write_text(json.dumps({'valid': True, 'revision': 0, 'profileId': 'synthetic'}))
            (root / 'active-profile.json').write_text(json.dumps({'id': 'synthetic', 'revision': 1 if case == 'metadata' else 0}))
            env = dict(os.environ, LAIN_HOME=str(root), LAIN_PROXY_PATH=str(core))
            with (root / 'host.log').open('w+') as output:
                host = subprocess.Popen([str(args.host)], env=env, stdout=output, stderr=output)
                try:
                    deadline = time.monotonic() + 30
                    status = None
                    while time.monotonic() < deadline and host.poll() is None:
                        try:
                            token = (root / 'credentials/host.token').read_text().strip()
                            request = urllib.request.Request('http://127.0.0.1:18418/api/v1/status', headers={'Authorization': 'Bearer ' + token})
                            with http.open(request, timeout=1) as response:
                                status = json.load(response)
                            break
                        except (OSError, ValueError):
                            time.sleep(.1)
                    assert status is not None, 'repair API unavailable: ' + case
                    assert status['proxyReady'] is False and status['error'] == expected, 'wrong public startup diagnostic: ' + case
                    output.flush()
                    assert 'synthetic-private-output' not in (root / 'host.log').read_text(), 'child output leaked into Host log'
                    checks.append({'case': case, 'repairApiAvailable': True, 'error': status['error'], 'childOutputRedacted': True})
                finally:
                    if host.poll() is None:
                        host.terminate()
                        try:
                            host.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            host.kill()
                            host.wait()
    report = {'passed': True, 'syntheticOnly': True, 'checks': checks}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'passed': True, 'cases': len(checks)}))


if __name__ == '__main__':
    main()
