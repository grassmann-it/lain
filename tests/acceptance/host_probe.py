#!/usr/bin/env python3
"""Independent black-box host probe. Synthetic credentials only; no upstream calls.
Usage: python3 tests/acceptance/host_probe.py -- dotnet /absolute/path/Lain.Host.dll
Requires free own Core/management ports 18417 and 18418. Never connects to the legacy service.
"""
import concurrent.futures
import datetime
import hashlib
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
import urllib.parse

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
    # Refuse an existing Host or Core; collectors must not probe another owned
    # session with this test's synthetic management credential.
    for port in [18417, 18418]:
        with socket.socket() as reserve:
            reserve.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            reserve.bind(('127.0.0.1', port))
    with tempfile.TemporaryDirectory(prefix='lain-w11-host-') as temporary:
        root = Path(temporary)
        state = root / 'state'
        env = dict(os.environ, LAIN_HOME=str(state), LAIN_PROXY_PATH=str(root / 'unqualified-proxy'),
                   LAIN_REFERENCE_QUALIFICATION='1', LAIN_REFERENCE_DISABLE_QUOTA='1')
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
                for path in ['/providers', '/providers/poe/onboarding', '/usage/analysis', '/inspector', '/inspector/events']:
                    check(request(path)[0] == 401, 'expansion endpoint bypassed management authentication')
                checks.append('management authentication and audience isolation')
                code, initial = request('/configuration', token=token)
                check(code == 200 and initial['revision'] == 0, 'unexpected initial configuration')
                code, catalog = request('/providers', token=token)
                expected_providers = {'nvidia-build', 'stepfun', 'poe', 'opencode-go', 'gemini-api', 'groq',
                                      'mistral', 'openrouter', 'requesty', 'cloudflare-workers-ai', 'inception',
                                      'alibaba-model-studio', 'nebius', 'synthetic', 'xai'}
                check(code == 200 and expected_providers <= {provider['id'] for provider in catalog['providers']},
                      'embedded provider registry was not wired completely')
                providers = {provider['id']: provider for provider in catalog['providers']}
                for provider_id in expected_providers:
                    code, onboarding = request('/providers/' + provider_id + '/onboarding', token=token)
                    check(code == 200 and onboarding['state'] == 'credentials_required' and
                          onboarding['liveVerification'] == 'not_tested' and onboarding['accountIds'] == [],
                          'missing credentials were not shown honestly')
                check(request('/providers/not-present', token=token)[0] == 404, 'unknown provider did not return 404')
                check(request('/routes/not-present/cost-policy', token=token)[0] == 404, 'unknown route policy did not return 404')
                checks.append('embedded provider registry and no-key onboarding')

                code, analysis = request('/usage/analysis?groupBy=accountId%2C%20effectiveReasoningEffort&groupLimit=500', token=token)
                check(code == 200 and analysis['totals']['attempts'] == 0 and
                      [group['dimension'] for group in analysis['dimensions']] == ['accountId', 'effectiveReasoningEffort'],
                      'analysis endpoint or normalized grouping was not wired')
                for name in ['firstOutputLatencyMs', 'totalDurationMs', 'outputTokensPerSecond']:
                    metric = analysis['performance'][name]
                    check(metric['samples'] == metric['eligible'] == 0 and
                          all(metric[field] is None for field in ['minimum', 'mean', 'p50', 'p95', 'maximum']),
                          'empty analysis invented measured performance')
                code, dated = request('/usage/analysis?from=2026-10-01&to=2026-10-02&timeZone=Europe%2FBerlin', token=token)
                check(code == 200 and dated['timeZone'] == 'Europe/Berlin' and
                      datetime.datetime.fromisoformat(dated['from'].replace('Z', '+00:00')).utcoffset() == datetime.timedelta(),
                      'date-only query did not resolve to UTC with explicit display time zone')
                code, inspector = request('/inspector', token=token)
                check(code == 200 and inspector['streams'] == [] and inspector['sourceId'] and
                      inspector['limits']['maxStreams'] <= 256 and inspector['limits']['maxPageSize'] <= 500,
                      'bounded inspector snapshot was not wired')
                code, events = request('/inspector/events?' + urllib.parse.urlencode({'sourceId': inspector['sourceId'], 'limit': 500}), token=token)
                check(code == 200 and events['events'] == [] and events['sourceId'] == inspector['sourceId'],
                      'inspector cursor endpoint was not wired')
                checks.append('analysis null measurements and bounded inspector endpoints')
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
                for path in ['/configuration', '/accounts', '/usage', '/usage/analysis', '/attempts', '/quotas',
                             '/providers', '/providers/poe/onboarding', '/inspector', '/inspector/events']:
                    code, body = request(path, token=token)
                    check(code == 200, 'read endpoint failed: ' + path)
                    check(SECRET not in json.dumps(body) and token not in json.dumps(body), 'secret leaked in public data')
                checks.append('desired/applied separation and public secret exclusion')
                for query in ['?limit=501', '?offset=-1', '?timeZone=not-a-zone', '?limit=abc', '?from=not-a-date']:
                    check(request('/usage' + query, token=token)[0] == 400, 'invalid usage query must return 400: ' + query)
                for query in ['?groupLimit=0', '?groupLimit=501', '?groupBy=accountId,accountId',
                              '?groupBy=accountId,model,routeId,provider', '?groupBy=notADimension', '?groupBy=accountId,',
                              '?workerId=', '?workerId=a%0Ab', '?workerId=' + 'x' * 257, '?limit=1&limit=2',
                              '?groupBy=model&groupBy=accountId', '?from=2025-01-01&to=2026-10-24',
                              '?from=2026-10-24&to=2026-10-24']:
                    check(request('/usage/analysis' + query, token=token)[0] == 400, 'invalid analysis query must return 400')
                filters = {key: 'unobserved' for key in ['routeId', 'modelFamily', 'manufacturer', 'inferenceProvider',
                           'gateway', 'billingAccountId', 'reasoningEffort', 'effectiveReasoningEffort', 'mode',
                           'conversationId', 'turnId', 'workerId', 'harness', 'profileId']}
                for endpoint in ['/usage', '/attempts', '/usage/analysis']:
                    check(request(endpoint + '?' + urllib.parse.urlencode(filters), token=token)[0] == 200,
                          'dimension filter did not reach the shared usage adapter')
                for endpoint, queries in [('/inspector', ['?limit=0', '?limit=257']),
                                          ('/inspector/events', ['?after=-1', '?limit=0', '?limit=501'])]:
                    for query in queries:
                        check(request(endpoint + query, token=token)[0] == 400, 'invalid inspector query must return 400')
                checks.append('bounded query validation')

                # Enrollment is local and quota polling is disabled explicitly. A
                # read must show registered adapters with unknown balances without
                # ever transmitting these synthetic keys to a provider.
                for provider_id in ['stepfun', 'poe', 'openrouter', 'synthetic']:
                    provider_route = next(route for route in providers[provider_id]['routes'] if route['protocol'] == 'openai-chat'
                                          and (provider_id != 'stepfun' or 'payg' in route['billingPlanIds']))
                    current = request('/configuration', token=token)[1]
                    enrollment = {'revision': current['revision'], 'value': {'name': 'Synthetic ' + provider_id,
                                  'provider': provider_id, 'apiKey': SECRET + '-' + provider_id,
                                  'baseUrl': provider_route['baseUrl'], 'providerRouteId': provider_route['id']}}
                    code, enrolled = request('/accounts', 'POST', enrollment, token)
                    check(code == 200, 'provider-specific enrollment was not wired')
                    provider_account = next(item['id'] for item in enrolled['accounts'] if item['provider'] == provider_id)
                    rows = request('/quotas', token=token)[1]
                    row = next(item for item in rows if item['accountId'] == provider_account)
                    check(row['state'] == 'unknown' and row['source'] != 'unavailable' and row['remaining'] is None,
                          'prepared quota adapter missing or balance fabricated')
                checks.append('registered quota adapters with local synthetic enrollment and no fetch')
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
                code, decision = request('/routes/acceptance-route/cost-policy?costPolicy=free_only', token=token)
                check(code == 200 and decision['allowed'] is False and decision['pricingClass'] == 'unknown',
                      'unknown-price route passed strict free-only inspection')
                revision_before_policy = current['revision']
                credential_hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in (state / 'credentials').iterdir()}
                for path, rejected in [('/routes/acceptance-route', dict(route, policy={'costPolicy': 'free_only'})),
                                       ('/profiles/acceptance-profile', dict(profile, policy={'costPolicy': 'free_only'})),
                                       ('/routes/acceptance-route', dict(route, policy={'fallbackRouteIds': ['missing']}))]:
                    check(request(path, 'PUT', {'revision': revision_before_policy, 'value': rejected}, token)[0] == 400,
                          'invalid route/profile cost policy was persisted')
                current = request('/configuration', token=token)[1]
                check(current['revision'] == revision_before_policy and credential_hashes ==
                      {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in (state / 'credentials').iterdir()},
                      'rejected policy changed revision or source credentials')
                checks.append('route/profile free-only guards preserve revision and credentials')
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
