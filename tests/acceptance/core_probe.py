#!/usr/bin/env python3
"""Independent Core authentication and crash-boundary probes, synthetic only.
Reuses the provider/process fixture but asserts boundaries outside its suite.
"""
import argparse
import http.client
import json
from pathlib import Path
import sys
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'backend'))
from qualify import Run, CLIENT_KEY, MANAGEMENT_KEY, assert_complete, expect
from fake_provider import marker


def auth_probe(core):
    with Run(core) as run:
        for token in [None, '', 'invalid', CLIENT_KEY]:
            headers = {} if token is None else {'Authorization': 'Bearer ' + token}
            try:
                with urlopen(Request(run.url + '/lain/events?after=0&limit=1', headers=headers), timeout=5) as response:
                    expect(False, 'journal accepted unauthorized caller')
            except HTTPError as error:
                expect(error.code in [401, 403], 'journal authentication returned unexpected status')
                error.close()
        expect(run.events()['events'] == [], 'authorized journal initial state incorrect')
        for token in [None, 'invalid', MANAGEMENT_KEY]:
            headers = {} if token is None else {'Authorization': 'Bearer ' + token}
            try:
                with urlopen(Request(run.url + '/v1/models', headers=headers), timeout=5) as response:
                    expect(False, 'inference endpoint accepted management/invalid token')
            except HTTPError as error:
                expect(error.code in [401, 403], 'inference authentication returned unexpected status')
                error.close()
        expect(not run.provider.snapshot(), 'authentication probe dispatched upstream')
        return {'managementAudienceSeparated': True, 'inferenceAudienceSeparated': True, 'upstreamCalls': 0}


def crash_probe(core):
    with Run(core) as run:
        connection = http.client.HTTPConnection('127.0.0.1', run.port, timeout=10)
        body = {'model': 'fixture-a', 'stream': True, 'messages': [{'role': 'user', 'content': marker('cancel', 'crash-after-dispatch')}]}
        connection.request('POST', '/v1/chat/completions', json.dumps(body), {'Authorization': 'Bearer ' + CLIENT_KEY, 'Content-Type': 'application/json'})
        response = connection.getresponse()
        expect(response.status == 200 and b'data:' in response.readline(), 'stream did not reach upstream')
        before = run.events()
        expect(len(before['events']) == 1 and before['events'][0]['kind'] == 'attempt_started', 'fixture not at dispatch-without-usage boundary')
        run.process.kill()
        run.process.wait(timeout=5)
        response.close()
        connection.close()
        run.start()
        replay = run.events()
        expect(replay == before, 'crash lost/changed durable dispatch')
        with run.post('text', 'post-crash', route='fixture-a') as later:
            later.read()
        events = run.events()['events']
        old_attempt = before['events'][0]['attemptId']
        expect(len([event for event in events if event['attemptId'] == old_attempt]) == 1, 'restart fabricated terminal usage for crashed request')
        assert_complete([event for event in events if event['attemptId'] != old_attempt], 1)
        return {'durableUnfinishedDispatch': True, 'sourceAndCursorRetained': True, 'inventedCompletion': False, 'subsequentRequestRecorded': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--core', required=True)
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    results = []
    for name, function in [('management-and-inference-auth', auth_probe), ('kill-after-dispatch-restart', crash_probe)]:
        try:
            results.append({'name': name, 'passed': True, 'evidence': function(args.core)})
        except Exception as error:
            results.append({'name': name, 'passed': False, 'error': type(error).__name__, 'detail': str(error) if isinstance(error, AssertionError) else 'probe execution failed'})
    report = {'syntheticOnly': True, 'passed': all(result['passed'] for result in results), 'checks': results}
    Path(args.report).write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
    return 0 if report['passed'] else 1

if __name__ == '__main__':
    sys.exit(main())
