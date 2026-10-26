#!/usr/bin/env python3
"""Offline evidence inventory and command printer. Never invokes CLI or inference."""
import argparse
import hashlib
import json
from pathlib import Path
import shlex

ROOT = Path(__file__).resolve().parents[2]
HIST = ROOT / 'tests/live/reports/realistic-models-20261026.json'
FOLLOW = ROOT / 'tests/live/reports/provider-tools-context-20261026.json'


def inventory():
    old = json.loads(HIST.read_bytes())['mainSweep']['rows']
    follow = json.loads(FOLLOW.read_bytes())
    passed = {r['alias'] for run in follow['runs'] for r in run['rows']
              if r['state'] == 'passed' and r.get('quality', {}).get('passed')}
    assert len(old) == 59 and len({r['alias'] for r in old}) == 59
    rows = []
    for r in old:
        state = ('auto_quality_passed' if r['alias'] in passed else
                 'historical_only' if r['state'] == 'passed' else 'open')
        rows.append({'alias': r['alias'], 'provider': r['provider'], 'status': state,
                     'historicalCategory': r.get('category'),
                     'runner': 'cohere_candidate.py' if r['provider'] == 'cohere' else 'realistic_models.py'})
    assert [sum(r['status'] == s for r in rows) for s in
            ('open', 'auto_quality_passed', 'historical_only')] == [23, 7, 29]
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--group', choices=['open', 'auto_quality_passed', 'historical_only'], default='open')
    parser.add_argument('--output-root', type=Path, default=Path('/home/tomka/.cache/lain-release-1.0-model-acceptance'))
    args = parser.parse_args()
    rows = inventory()
    pins = [HIST, FOLLOW, Path(__file__), ROOT/'tests/live/realistic_models.py',
            ROOT/'tests/live/model_boundaries.py', ROOT/'tests/live/cohere_candidate.py',
            ROOT/'tests/backend/qualify.py', ROOT/'proxy/source-manifest.json']
    result = {'offlineOnly': True, 'rows': rows, 'sha256': {
        str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in pins},
        'commands': [], 'separateRunner': []}
    for row in rows:
        if row['status'] != args.group:
            continue
        if row['provider'] == 'cohere':
            result['separateRunner'].append({'alias': row['alias'], 'reason':
                'Standard runner has unsupported controls; use the documented authorized two-request subscription candidate flow after release installation.'})
            continue
        # Numbered directories avoid slashes and shell metacharacters in aliases.
        output = args.output_root / f"{args.group}-{len(result['commands'])+1:02d}"
        result['commands'].append({'alias': row['alias'], 'maximumRequests': 2,
            'prepare': shlex.join(['python3', '-B', 'tests/live/realistic_models.py',
                '--output', str(output), '--tool-mode', 'auto-v2', '--only-alias', row['alias']]),
            'executeTemplate': shlex.join(['flock', '/home/tomka/.cache/lain-verification-lane.lock',
                'python3', '-B', 'tests/live/realistic_models.py', '--output', str(output),
                '--execute', '--expect-plan-sha256', 'REPLACE_WITH_REVIEWED_PLAN_SHA256', '--workers', '1'])})
    result['maximumRequests'] = 2 * len(result['commands'])
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
