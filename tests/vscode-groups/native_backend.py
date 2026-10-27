#!/usr/bin/env python3
"""No-turn proof of the exact IDE appserver; synthetic HOME mounted in bwrap."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import select
import subprocess
import sys


def inner():
    plan = json.loads(Path('/mnt/plan.json').read_text())
    assert os.environ['HOME'] == plan['home'] and os.environ.get('CODEX_HOME') == plan['codexHome']
    binary = '/mnt/extension/bin/linux-x86_64/codex'
    proc = subprocess.Popen([binary, 'app-server'], stdin=subprocess.PIPE,
                            stdout=subprocess.PIPE, stderr=open('/mnt/stderr.log', 'wb'), bufsize=0)
    pending = b''
    serial = 0

    def request(method, params):
        nonlocal serial, pending
        serial += 1
        proc.stdin.write((json.dumps(dict(id=serial, method=method, params=params))+'\n').encode())
        while True:
            if b'\n' not in pending:
                if not select.select([proc.stdout], [], [], 20)[0]:
                    raise TimeoutError(method)
                chunk = os.read(proc.stdout.fileno(), 1024*1024)
                if not chunk:
                    raise RuntimeError('appserver exited')
                pending += chunk
                continue
            line, pending = pending.split(b'\n', 1)
            msg = json.loads(line)
            if msg.get('id') == serial:
                return msg

    report = {'binarySha256': hashlib.sha256(Path(binary).read_bytes()).hexdigest(),
              'HOME': os.environ['HOME'], 'CODEX_HOME': os.environ.get('CODEX_HOME')}
    try:
        report['initialize'] = request('initialize', {'clientInfo': {'name': 'lain-groups-proof', 'version': '1'},
                                                     'capabilities': {'experimentalApi': True}})
        for name, params in [('global', {'includeLayers': True}),
                             ('project', {'cwd': '/mnt/work', 'includeLayers': True})]:
            report['config-'+name] = request('config/read', params)
        report['models'] = request('model/list', {'includeHidden': True, 'limit': 2})
        report['models-other-cwd-param'] = request('model/list', {'includeHidden': True, 'limit': 100, 'cwd': str(Path.home())})
        cursor = report['models'].get('result', {}).get('nextCursor')
        report['second-page'] = request('model/list', {'includeHidden': True, 'limit': 2, 'cursor': cursor})
        assert all('error' not in value for value in report.values() if isinstance(value, dict))
        if plan.get('customCatalog'):
            all_models = list(report['models']['result']['data'])
            cursor = report['models']['result']['nextCursor']
            while cursor is not None:
                page = request('model/list', {'includeHidden':True, 'limit':100, 'cursor':cursor})
                assert 'error' not in page
                all_models.extend(page['result']['data']);cursor=page['result']['nextCursor']
            astra = next(model for model in all_models if model['model']=='gpt-6-astra')
            assert astra['defaultReasoningEffort']=='low'
            assert [effort['reasoningEffort'] for effort in astra['supportedReasoningEfforts']]==['low','medium','high','xhigh','max']
            report['repairedAstra'] = astra
            report['catalogModelCount'] = len(all_models)
        else:
            expected = ['project-only'] if plan['projectOverride'] else ['fixture-a', 'fixture-shared', 'fixture-router', 'fixture-hidden']
            assert [model['model'] for model in report['models-other-cwd-param']['result']['data']] == expected
            assert [model['model'] for model in report['models']['result']['data']] == expected[:2]
            if not plan['projectOverride']:
                assert [model['model'] for model in report['second-page']['result']['data']] == expected[2:]
                assert report['second-page']['result']['data'][-1]['hidden'] is True
        assert report['config-global']['result']['config']['model_catalog_json'].endswith('/models.json')
        assert report['config-project']['result']['config']['model_catalog_json'].endswith('/other.json' if plan['projectOverride'] else '/models.json')
        report.update(status='passed', syntheticOnly=True, inferenceRequests=0)
        Path('/mnt/result.json').write_text(json.dumps(report, indent=2)+'\n')
    finally:
        proc.terminate()
        proc.wait(timeout=5)


def main():
    if sys.argv[1:] == ['--inner']:
        inner()
        return
    p = argparse.ArgumentParser()
    p.add_argument('--extension', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--no-project-override', action='store_true')
    p.add_argument('--catalog', type=Path, help='exact prepared Astra catalog, tested only with --no-project-override')
    args = p.parse_args()
    out = args.output.absolute()
    out.mkdir(parents=True, exist_ok=False)
    home = Path(os.environ['HOME'])
    if os.environ.get('CODEX_HOME') is not None:
        p.error('this backend harness requires an unset CODEX_HOME; it never reassigns it')
    private = out/'home'
    config = private/'.codex'
    config.mkdir(parents=True)
    (out/'work/.codex').mkdir(parents=True)
    seed = json.loads(Path('/home/tomka/.cache/lain-codex-sources/prepared-picker-default-0.164.0-r1/codex-rs/models-manager/models.json').read_text())['models'][0]
    models = []
    for slug in ['fixture-a', 'fixture-shared', 'fixture-router', 'fixture-hidden']:
        model = copy.deepcopy(seed)
        model.update(slug=slug, display_name=slug, description='Synthetic IDE fixture',
                     visibility='hide' if slug.endswith('hidden') else 'list',
                     supported_in_api=True, priority=len(models),
                     default_reasoning_level='low', supported_reasoning_levels=[{'effort': 'low', 'description': 'Low'}, {'effort': 'high', 'description': 'High'}],
                     availability_nux=None, multi_agent_version=None)
        models.append(model)
    catalog = {'models': models, 'lain_model_picker': {'schema_version': 1, 'groups': [
        {'id': 'free', 'name': 'Free', 'models': ['fixture-a', 'fixture-shared'], 'router_model': 'fixture-router'},
        {'id': 'sec', 'name': 'IT Sec 🛡', 'models': ['fixture-shared', 'fixture-hidden']}]}}
    (config/'models.json').write_text(json.dumps(catalog))
    if args.catalog:
        if not args.no_project_override:
            p.error('--catalog requires --no-project-override')
        (config/'models.json').write_bytes(args.catalog.read_bytes())
    (config/'other.json').write_text(json.dumps({'models': [dict(models[0], slug='project-only')]}))
    default_model = 'gpt-6-astra' if args.catalog else 'fixture-a'
    (config/'config.toml').write_text(f'''model = "{default_model}"
model_provider = "lain"
model_reasoning_effort = "low"
model_catalog_json = "{home}/.codex/models.json"
[model_providers.lain]
name = "Synthetic local"
base_url = "http://127.0.0.1:18575/v1"
wire_api = "responses"
requires_openai_auth = false
[projects."/mnt/work"]
trust_level = "trusted"
''')
    if not args.no_project_override:
        (out/'work/.codex/config.toml').write_text(f'model_catalog_json = "{home}/.codex/other.json"\n')
    (out/'native_backend.py').write_bytes(Path(__file__).read_bytes())
    (out/'plan.json').write_text(json.dumps({'home':str(home), 'codexHome':os.environ.get('CODEX_HOME'), 'projectOverride':not args.no_project_override,'customCatalog':args.catalog is not None}))
    env = {k:v for k,v in os.environ.items() if k in ('HOME','PATH','USER','LOGNAME','LANG','CODEX_HOME')}
    command = ['bwrap', '--die-with-parent', '--new-session', '--unshare-net', '--unshare-pid',
               '--ro-bind', '/', '/', '--proc', '/proc', '--dev', '/dev', '--tmpfs', '/tmp', '--tmpfs', '/run',
               '--bind', str(private), str(home), '--bind', str(out), '/mnt',
               '--ro-bind', str(args.extension.absolute()), '/mnt/extension',
               '--chdir', '/mnt/work', sys.executable, '/mnt/native_backend.py', '--inner']
    result = subprocess.run(command, env=env, capture_output=True, timeout=100)
    (out/'supervisor.stderr').write_bytes(result.stderr)
    print(json.dumps({'exitCode':result.returncode, 'output':str(out)}))
    raise SystemExit(result.returncode)


if __name__ == '__main__':
    main()
