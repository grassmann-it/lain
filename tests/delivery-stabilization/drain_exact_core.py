#!/usr/bin/env python3
"""Plan or test a pinned Core child with synthetic loopback streaming/drain only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
import time
from urllib.error import HTTPError
from urllib.request import ProxyHandler, Request, build_opener

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--core', type=Path, required=True)
    p.add_argument('--core-sha256', required=True)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--parent-netns', required=True)
    p.add_argument('--execute', action='store_true')
    args = p.parse_args()
    safe = lambda path: path.is_absolute() and '..' not in path.parts and not any(p.is_symlink() for p in (path, *path.parents))
    if not safe(args.core) or not safe(args.report) or not args.report.parent.is_dir() or digest(args.core) != args.core_sha256 or args.report.exists():
        p.error('Pinned absolute Core and fresh report required')
    if not args.execute:
        print(json.dumps({'phase': 'planned', 'mutations': False, 'coreSha256': args.core_sha256,
                          'scope': 'Exact new child and loopback provider; no installed service'}))
        return
    sys.path.insert(0, str(ROOT / 'tests/backend'))
    from qualify import Run, MANAGEMENT_KEY, expect
    from actual_packages import require_loopback_namespace
    require_loopback_namespace(args.parent_netns)
    os.umask(0o077)
    opener, states = build_opener(ProxyHandler({})), []
    with Run(args.core) as run:
        def state(method='GET', route='/lain/activity'):
            with opener.open(Request(run.url + route, headers={'Authorization': 'Bearer ' + MANAGEMENT_KEY}, method=method), timeout=5) as response:
                value = json.load(response)
                expect(response.headers.get('Cache-Control') == 'no-store', 'Management response cacheable')
                states.append({'method': method, 'route': route, **value})
                return value
        stream = run.post('gated_usage', 'drain-active', route='fixture-a', stream=True)
        try:
            expect(stream.readline().startswith(b'data:'), 'No actual stream opened')
            expect(state()['activeRequests'] == 1, 'Admitted request not active')
            value = state('POST', '/lain/drain')
            expect(value['draining'] and value['activeRequests'] == 1, 'Drain cut admitted stream')
            try:
                run.post('text', 'drain-refused', route='fixture-a')
            except HTTPError as error:
                expect(error.code == 503 and b'runtime_draining' in error.read(), 'New request not refused by drain')
                error.close()
            else:
                raise AssertionError('New inference admitted during drain')
            run.provider.usage_gate.set()
            expect(b'[DONE]' in stream.read(), 'Admitted stream did not finish')
        finally:
            stream.close()
            run.provider.usage_gate.set()
        deadline = time.monotonic() + 5
        while state()['activeRequests']:
            expect(time.monotonic() < deadline, 'Completed stream remains active')
            time.sleep(.05)
        expect(state()['draining'], 'Barrier silently disappeared')
        expect(state('POST', '/lain/drain')['activeRequests'] == 0, 'Final barrier not quiet')
        expect(not state('DELETE', '/lain/drain')['draining'], 'Admission not reopened')
        with run.post('text', 'drain-resumed', route='fixture-a') as response:
            expect(response.status == 200, 'No inference after reopening')
        expect(len({item['requestId'] for item in run.events()['events']}) == 2, 'Unexpected dispatched request count')
    if digest(args.core) != args.core_sha256:
        raise RuntimeError('Pinned Core changed during execution')
    with args.report.open('x') as stream:
        json.dump({'format': 1, 'kind': 'exact-core-drain-runtime', 'passed': True, 'syntheticOnly': True,
                   'coreSha256': args.core_sha256, 'states': states,
                   'scope': 'Actual pinned child, admitted stream, refusal, quiet drain and reopening; no systemd/installed service'}, stream, indent=2)
        stream.write('\n')


if __name__ == '__main__': main()
