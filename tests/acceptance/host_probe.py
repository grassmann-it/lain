#!/usr/bin/env python3
"""Independent black-box host probe. Synthetic credentials only; no upstream calls.
Usage: python3 tests/acceptance/host_probe.py -- dotnet /absolute/path/Lain.Host.dll
Requires free own management port 18418. Never connects to the legacy service.
"""
import concurrent.futures
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

LOCAL_HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))
BASE = 'http://127.0.0.1:18418/api/v1'
SECRET = 'synthetic-acceptance-key-not-real-001'

def request(path, method='GET', value=None, token=None):
    headers = {'Content-Type': 'application/json'}
    if token is not None:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(BASE + path, data=None if value is None else json.dumps(value).encode(), headers=headers, method=method)
    try:
        response = LOCAL_HTTP.open(req, timeout=10)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        body = response.read().decode()
        return response.status, json.loads(body) if body else None

def check(condition, message):
    if not condition:
        raise AssertionError(message)

def main():
    command = sys.argv[1:]
    if command[:1] == ['--']:
        command = command[1:]
    if not command:
        raise ValueError('Provide host launch command after --')
    with socket.socket() as reserve:
        reserve.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        reserve.bind(('127.0.0.1', 18418))
    with tempfile.TemporaryDirectory(prefix='lain-w11-host-') as temporary:
        root = Path(temporary)
        state = root / 'state'
        env = dict(os.environ, LAIN_HOME=str(state), LAIN_PROXY_PATH=str(root / 'unqualified-proxy'))
        with (root / 'host.log').open('w') as log:
            process = subprocess.Popen(command, env=env, cwd=root, stdout=log, stderr=log)
            checks = []
            try:
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline:
                    check(process.poll() is None, 'host exited before becoming reachable')
                    try:
                        if request('/status')[0] == 401:
                            break
                    except OSError:
                        pass
                    time.sleep(.1)
                else:
                    raise AssertionError('host did not become reachable')
                token = (state / 'credentials/host.token').read_text().strip()
                inference = (state / 'credentials/inference.token').read_text().strip()
                for supplied in [None, '', 'invalid', inference]:
                    check(request('/status', token=supplied)[0] == 401, 'management authorization or audience isolation failed')
                check(request('/status', token=token)[0] == 200, 'valid host token rejected')
                checks.append('management authentication and audience isolation')
                code, initial = request('/configuration', token=token)
                check(code == 200 and initial['revision'] == 0, 'unexpected initial configuration')
                add = {'revision': 0, 'value': {'name': 'Acceptance A', 'provider': 'acceptance', 'apiKey': SECRET, 'baseUrl': 'http://127.0.0.1:9/v1'}}
                code, config = request('/accounts', 'POST', add, token)
                check(code == 200, 'synthetic account creation failed')
                check(SECRET not in json.dumps(config), 'credential leaked in account response')
                account = config['accounts'][0]['id']
                check(request('/accounts', 'POST', add, token)[0] == 409, 'stale revision accepted')
                next_revision = config['revision']
                with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
                    results = list(pool.map(lambda enabled: request('/accounts/' + account + '/enabled', 'PUT', {'revision': next_revision, 'value': enabled}, token)[0], [False, True]))
                check(sorted(results) == [200, 409], 'competing writers did not yield one revision conflict')
                checks.append('revision race and stale-write rejection')
                code, current = request('/configuration', token=token)
                check(current['appliedRevision'] < current['revision'], 'unapplied config reported active')
                for path in ['/configuration', '/accounts', '/usage', '/attempts', '/quotas']:
                    code, body = request(path, token=token)
                    check(code == 200, 'read endpoint failed: ' + path)
                    check(SECRET not in json.dumps(body) and token not in json.dumps(body), 'secret leaked in public data')
                checks.append('desired/applied separation and public secret exclusion')
                for query in ['?limit=501', '?offset=-1', '?timeZone=not-a-zone', '?limit=abc', '?from=not-a-date']:
                    check(request('/usage' + query, token=token)[0] == 400, 'invalid usage query must return 400: ' + query)
                checks.append('bounded query validation')
                # Applying against the explicitly absent/unqualified Core must not
                # claim that the requested route became active.
                current = request('/configuration', token=token)[1]
                code, current = request('/accounts/' + account + '/enabled', 'PUT', {'revision': current['revision'], 'value': True}, token)
                check(code == 200, 'could not enable synthetic account for failed-apply probe')
                route = {'id': 'acceptance-route', 'alias': 'acceptance-alias', 'upstreamModel': 'synthetic-model', 'provider': 'acceptance', 'accountIds': [account], 'pooled': False, 'visible': True, 'enabled': True}
                code, current = request('/routes/acceptance-route', 'PUT', {'revision': current['revision'], 'value': route}, token)
                check(code == 200, 'synthetic route creation failed')
                profile = {'id': 'acceptance-profile', 'name': 'Acceptance', 'routeIds': [route['id']], 'defaultRouteId': route['id']}
                code, current = request('/profiles/acceptance-profile', 'PUT', {'revision': current['revision'], 'value': profile}, token)
                check(code == 200, 'synthetic profile creation failed')
                applied_before = current['appliedRevision']
                code, _ = request('/profiles/acceptance-profile/apply', 'POST', {'revision': current['revision']}, token)
                check(code in [409, 503], 'unqualified Core apply must fail explicitly')
                after_failure = request('/configuration', token=token)[1]
                check(after_failure['appliedRevision'] == applied_before and after_failure['revision'] > applied_before, 'failed apply advanced applied revision')
                check(json.loads((state / 'proxy/runtime.json').read_text())['valid'] is False, 'failed generation not invalidated on disk')
                for action in ['start', 'restart']:
                    check(request('/service/' + action, 'POST', {}, token)[0] in [409, 503], 'failed generation could be started')
                check(request('/status', token=token)[1]['proxyReady'] is False, 'failed runtime reported ready')
                checks.append('failed apply preserves applied revision and invalidates runtime')
                before = (state / 'credentials/host.token').read_bytes()
                second = subprocess.Popen(command, env=env, cwd=root, stdout=log, stderr=log)
                try:
                    check(second.wait(timeout=10) != 0, 'second host acquired same ownership')
                finally:
                    if second.poll() is None:
                        second.terminate(); second.wait(timeout=5)
                check((state / 'credentials/host.token').read_bytes() == before, 'second host changed host token')
                checks.append('exclusive state ownership')
                for path in (state / 'credentials').iterdir():
                    check(path.stat().st_mode & 0o077 == 0, 'credential permissions are not private')
                checks.append('private credential permissions')
                print(json.dumps({'passed': True, 'checks': checks, 'provider_calls': 0}, indent=2))
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill(); process.wait(timeout=5)

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Test failures use fixed diagnostic labels. Never print bodies or host logs.
        print(json.dumps({'passed': False, 'error': type(error).__name__, 'detail': str(error) if isinstance(error, (AssertionError, ValueError)) else 'probe execution failed'}), file=sys.stderr)
        sys.exit(1)
