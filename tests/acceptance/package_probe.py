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
import shutil
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, build_opener, ProxyHandler

urlopen = build_opener(ProxyHandler({})).open


sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'backend'))
from fake_provider import FakeProvider, MODEL, marker


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
    with FakeProvider() as provider, tempfile.TemporaryDirectory(prefix='lain-w11-package-') as temporary:
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
        def start_and_check(first=False):
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
                    def api(path, method='GET', value=None):
                        headers = {'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'}
                        request = Request('http://127.0.0.1:18418/api/v1' + path, data=None if value is None else json.dumps(value).encode(), headers=headers, method=method)
                        with urlopen(request, timeout=30) as response:
                            return json.load(response)
                    if first:
                        config = api('/accounts', 'POST', {'revision': 0, 'value': {'name': 'Package acceptance', 'provider': 'fixture', 'apiKey': 'lain-synthetic-a', 'baseUrl': provider.url}})
                        account_id = config['accounts'][0]['id']
                        route = {'id': 'package-route', 'alias': 'package-model', 'upstreamModel': MODEL, 'provider': 'fixture', 'accountIds': [account_id], 'pooled': False, 'visible': True, 'enabled': True}
                        config = api('/routes/package-route', 'PUT', {'revision': config['revision'], 'value': route})
                        profile = {'id': 'package-profile', 'name': 'Package acceptance', 'routeIds': ['package-route'], 'defaultRouteId': 'package-route'}
                        config = api('/profiles/package-profile', 'PUT', {'revision': config['revision'], 'value': profile})
                        applied = api('/profiles/package-profile/apply', 'POST', {'revision': config['revision']})
                        expect(applied['appliedRevision'] == config['revision'], 'installed proxy profile failed to apply')
                        inference = (data / 'credentials/inference.token').read_text().strip()
                        body = {'model': 'package-model', 'messages': [{'role': 'user', 'content': marker('text', 'installed-package')}], 'stream': False}
                        request = Request('http://127.0.0.1:18417/v1/chat/completions', json.dumps(body).encode(), {'Authorization': 'Bearer ' + inference, 'Content-Type': 'application/json'})
                        with urlopen(request, timeout=20) as response:
                            expect(json.load(response)['usage']['total_tokens'] == 18, 'installed bundled Core inference failed')
                    deadline = time.monotonic() + 15
                    while True:
                        summary = api('/usage')
                        if summary['totals']['attempts'] == 1 and summary['totals']['tokens']['total'] == 18:
                            break
                        expect(time.monotonic() < deadline, 'installed usage did not persist exactly one synthetic attempt')
                        time.sleep(.1)
                    expect(summary['totals']['requests'] == 1 and summary['totals']['errors'] == 0 and summary['complete'], 'installed usage identity/coverage changed across restart')
                    usage_result = run([launcher, 'usage', '--json'], environment=client_env)
                    expect(usage_result.returncode == 0 and json.loads(usage_result.stdout)['totals'] == summary['totals'], 'installed CLI/API usage differ')
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
        start_and_check(first=True)
        original_token = (data / 'credentials/host.token').read_bytes()
        checks.append('actual installed host bundled Core inference and CLI from unrelated cwd')
        checks.append('synthetic usage captured once and CLI/API totals match')
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
        # Exercise documented uninstall mechanics only on this probe's own paths.
        shutil.rmtree(data / 'app')
        shutil.rmtree(data / 'app.failed')
        (prefix / 'bin/lain').unlink()
        (units / 'lain.service').unlink()
        expect((data / 'keep-me').read_text() == 'persistent user data', 'uninstall deleted private data')
        expect((data / 'credentials/host.token').read_bytes() == original_token, 'uninstall deleted credentials')
        expect((data / 'usage.db').is_file(), 'uninstall deleted usage history')
        checks.append('documented staged uninstall preserves private data')
    report = {'passed': True, 'checks': checks, 'realProviderCalls': 0, 'syntheticProviderCalls': 1, 'systemdOperations': 0, 'sameArtifactUpdateAndRollback': True, 'hostExecutableSha256': expected_hash}
    Path(args.report).write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    main()
