#!/usr/bin/env python3
"""Synthetic staging/install integrity and rollback checks (no Go/.NET build/services)."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n' if isinstance(value, (dict, list)) else value)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, *, expected=True, contains=None, **kwargs):
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, **kwargs)
    assert (result.returncode == 0) == expected, result.stdout
    assert contains is None or contains in result.stdout, result.stdout
    return result


def module(path):
    spec = importlib.util.spec_from_file_location('core_refresh_test', path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


with tempfile.TemporaryDirectory(prefix='lain-core-delivery-test-') as temporary:
    folder = Path(temporary)
    repo, base = folder / 'repo', folder / 'base'
    repo.mkdir()
    for name in ('core-refresh.py', 'collect-go-notices.py'):
        target = repo / 'scripts' / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / 'scripts' / name, target)
    api = module(repo / 'scripts/core-refresh.py')
    for name in ('build-core.sh', 'build-core-verify.py'):
        write(repo / 'scripts' / name, '# fixture: never builds\n')
    write(repo / 'proxy/LICENSE.upstream', 'Fixture upstream MIT license\n')
    write(repo / 'proxy/toolchain.json', {'goVersion': 'go1.27.2'})
    write(repo / 'proxy/adapter/go.mod', 'module example.net/lain-adapter\n')
    write(repo / 'proxy/adapter/go.sum', '')
    write(repo / 'proxy/patches/hook.patch', 'explicit fixture hook\n')
    upstream = {'repository': 'https://github.com/router-for-me/CLIProxyAPI', 'module': 'github.com/router-for-me/CLIProxyAPI/v8',
                'version': 'v8.0.41', 'commit': 'a' * 40, 'archiveSha256': 'b' * 64,
                'patches': [{'path': 'patches/hook.patch', 'sha256': sha(repo / 'proxy/patches/hook.patch')}]}
    write(repo / 'proxy/upstream.json', upstream)
    # Fixture source assembly, independent of network and compilers.
    write(repo / 'scripts/prepare-core.py', '''import argparse, hashlib, json, pathlib, shutil
p=argparse.ArgumentParser();p.add_argument('--destination',type=pathlib.Path);p.add_argument('--archive');a=p.parse_args()
r=pathlib.Path(__file__).resolve().parents[1];d=a.destination;d.mkdir();u=d/'upstream';u.mkdir();shutil.copyfile(r/'proxy/LICENSE.upstream',u/'LICENSE');shutil.copytree(r/'proxy/adapter',d/'adapter')
m=d/'adapter/go.mod';m.write_text(m.read_text()+'replace github.com/router-for-me/CLIProxyAPI/v8 => ../upstream\\n')
s=json.loads((r/'proxy/source-manifest.json').read_text());h=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
record={'schemaVersion':1,'sourceManifestSha256':h(r/'proxy/source-manifest.json'),'upstreamVersion':s['upstreamDependency']['version'],'upstreamCommit':s['upstreamDependency']['commit'],'upstreamArchiveSha256':s['upstreamDependency']['archiveSha256'],'patchSetSha256':s['patchSetSha256'],'files':{str(p.relative_to(d)):h(p) for p in d.rglob('*') if p.is_file()}}
f=d/'prepared-manifest.json';f.write_text(json.dumps(record));print(json.dumps({'adapter':str(d/'adapter'),'upstream':str(u),'preparedManifest':str(f)}))
''')
    patch_hash = hashlib.sha256(json.dumps(upstream['patches'], sort_keys=True).encode()).hexdigest()
    source = {'schemaVersion': 2, 'snapshotVersion': '8.0.41', 'lainPatchVersion': 1, 'upstreamSourceModified': True,
              'licenseSha256': sha(repo / 'proxy/LICENSE.upstream'), 'upstreamDependency': upstream,
              'patches': upstream['patches'], 'patchSetSha256': patch_hash,
              'adapterFiles': {'go.mod': sha(repo / 'proxy/adapter/go.mod'), 'go.sum': sha(repo / 'proxy/adapter/go.sum')}}
    source['buildInputs'] = {str(path.relative_to(repo)): sha(path) for path in repo.rglob('*') if path.is_file()}
    write(repo / 'proxy/source-manifest.json', source)
    run(['git', 'init', '-q', str(repo)])
    run(['git', 'add', '.'], cwd=repo)
    run(['git', '-c', 'user.name=Fixture', '-c', 'user.email=tom@grassmann-it.de', 'commit', '-qm', 'Fixture'], cwd=repo)
    revision = run(['git', 'rev-parse', 'HEAD'], cwd=repo).stdout.strip()
    for component, project in [('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')]:
        write(base / component / (project + '.dll'), component + ' original bits\n')
    for name in ('notices/inventory.json', 'notices/nuget/NOTICE', 'notices/fonts/OFL.txt', 'notices/README.txt',
                 'delivery-assets.json', 'client-source-provenance/original.json', 'proxy/lain-proxy',
                 'proxy/build-manifest.json', 'proxy/source-manifest.json', 'proxy/qualification.json',
                 'notices/go/inventory.json', 'core-source-provenance/old/nested.txt'):
        write(base / name, {'historical': True})
    info = {'format': 1, 'componentSourceCommits': {'cli': '416fe13', 'host': 'a66cb2a', 'desktop': '416fe13'},
            'componentInformationalVersions': {'cli': '1.0+416fe13', 'host': '1.0+a66cb2a', 'desktop': '1.0+416fe13'},
            'components': {name: {'assemblySha256': sha(base / name / (project + '.dll'))} for name, project in
                           [('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')]},
            'coreSourceCommit': 'original-core-pin', 'proxySha256': sha(base / 'proxy/lain-proxy'), 'coreBuild': {'upstreamVersion': 'historical'}}
    write(base / 'build-info.json', info)
    write(base / 'gate-status.json', {'backendQualified': True, 'releaseQualified': True, 'clientRefreshQualified': True})
    write(base / '.lain-package', 'managed\n')
    api.manifest(base)
    original = api.verify(base)
    binary = folder / 'new-core'
    write(binary, 'fixture binary (never executed)\n')
    binary.chmod(0o700)
    build = {'schemaVersion': 2, 'binarySha256': sha(binary), 'sourceManifestSha256': sha(repo / 'proxy/source-manifest.json'),
             'snapshotVersion': '8.0.41', 'lainPatchVersion': 1, 'upstreamSourceModified': True, 'goVersion': 'go version go1.27.2 linux/amd64',
             'upstreamVersion': upstream['version'], 'upstreamCommit': upstream['commit'], 'upstreamArchiveSha256': upstream['archiveSha256'],
             'patchSetSha256': patch_hash}
    write(folder / 'build.json', build)
    qualification = {'passed': True, 'sha256': sha(binary), 'sourceManifestSha256': build['sourceManifestSha256'], 'scope': 'Fixture only'}
    write(folder / 'qualification.json', qualification)
    gomod, goroot = folder / 'gomod', folder / 'goroot'
    write(gomod / 'example.net/leaf@v1.0.0/LICENSE', 'Leaf license\n')
    write(goroot / 'LICENSE', 'Go fixture license\n')
    fake_go = folder / 'fake-go'
    write(fake_go, '#!/usr/bin/env python3\n' + '''import json, pathlib, sys
if sys.argv[1:3] == ['version','-m']:
 print(sys.argv[3]+': go1.27.2');print('\\tdep\\tgithub.com/router-for-me/CLIProxyAPI/v8\\tv8.0.41');print('\\t=>\\t../upstream\\t(devel)');print('\\tdep\\texample.net/leaf\\tv1.0.0\\th1:leaf')
elif sys.argv[1] == 'env':
 print(json.dumps(SETTINGS))
elif sys.argv[1] == 'list':
 print(json.dumps({'Path':'example.net/lain-adapter','Main':True,'Dir':str(pathlib.Path.cwd())}))
 print(json.dumps({'Path':'github.com/router-for-me/CLIProxyAPI/v8','Version':'v8.0.41','Replace':{'Path':'../upstream','Dir':str(pathlib.Path.cwd().parent/'upstream')}}))
 print(json.dumps({'Path':'example.net/leaf','Version':'v1.0.0','Sum':'h1:leaf'}))
else: raise SystemExit('Unexpected fake Go invocation')
'''.replace('SETTINGS', repr({'GOVERSION': 'go1.27.2', 'GOMODCACHE': str(gomod), 'GOROOT': str(goroot)})))
    fake_go.chmod(0o700)
    env = dict(os.environ, LAIN_GO=str(fake_go))
    target = folder / 'target'
    command = [sys.executable, str(repo / 'scripts/core-refresh.py'), 'stage', '--base', str(base), '--core', str(binary),
               '--core-build-manifest', str(folder / 'build.json'), '--qualification', str(folder / 'qualification.json'),
               '--source-commit', revision, '--output', str(target)]
    run(command, env=env)
    updated = api.verify(target)
    api.protected(original, updated)
    target_info = api.load(target / 'build-info.json')
    assert target_info['components'] == info['components']
    assert target_info['coreBuild']['upstreamVersion'] == 'v8.0.41'
    assert target_info['coreRefresh']['previousCore']['coreBuild'] == info['coreBuild']
    assert api.load(target / 'core-source-provenance/baseline-gate-status.json')['releaseQualified'] is True
    assert api.load(target / 'gate-status.json')['releaseQualified'] is False
    assert not (target / 'core-source-provenance/old').exists()
    assert api.load(target / 'notices/go/inventory.json')['missingEvidence'] == []
    # A self-consistent manifest must not allow changes outside the allowlist.
    tampered = folder / 'tampered'
    shutil.copytree(target, tampered)
    write(tampered / 'cli/Lain.Cli.dll', 'tampered client\n')
    api.manifest(tampered)
    def home(name):
        result = folder / name
        shutil.copytree(base, result / 'app')
        return result
    home_bad = home('home-bad')
    run([sys.executable, str(tampered / 'core-refresh.py'), 'install', '--package', str(tampered), '--home', str(home_bad),
         '--backup', str(home_bad / 'backup')], expected=False, contains='protected application')
    assert api.verify(home_bad / 'app') == original
    # Inject a journal failure after two successful swaps: every old byte returns.
    home_rollback = home('home-rollback')
    installed_api = module(target / 'core-refresh.py')
    real_write, tripped = installed_api.write, []
    def fail_once(path, value):
        if path.name == 'transaction.json' and value.get('status') == 'replacing' and len(value.get('completed', [])) == 2 and not tripped:
            tripped.append(True)
            raise RuntimeError('injected journal failure')
        real_write(path, value)
    installed_api.write = fail_once
    try:
        installed_api.install(argparse.Namespace(package=target, home=home_rollback, backup=home_rollback / 'backup'))
        raise AssertionError('Injected failure did not fire')
    except RuntimeError as error:
        assert str(error) == 'injected journal failure', str(error)
    assert tripped and api.verify(home_rollback / 'app') == original
    assert api.load(home_rollback / 'backup/transaction.json')['status'] == 'rolled-back-after-error'
    # Exact success changes only the permitted paths and retains app source pins.
    home_success = home('home-success')
    run([sys.executable, str(target / 'core-refresh.py'), 'install', '--package', str(target), '--home', str(home_success),
         '--backup', str(home_success / 'backup')])
    assert api.verify(home_success / 'app') == updated
    # A mismatched binary/qualification must never become an accepted package.
    write(folder / 'qualification.json', {**qualification, 'sha256': '0' * 64})
    command[-1] = str(folder / 'bad-qualification')
    run(command, env=env, expected=False, contains='Independent qualification')
    assert api.verify(base) == original
print('Core refresh synthetic staging/protected-file rejection/rollback/exact install/qualification checks passed.')
