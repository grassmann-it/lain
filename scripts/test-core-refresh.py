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
    catalog_path = Path('proxy/codex-model-catalog.json')
    catalog_bytes = (ROOT / catalog_path).read_bytes()
    (repo / catalog_path).parent.mkdir(parents=True, exist_ok=True)
    (repo / catalog_path).write_bytes(catalog_bytes)
    catalog = api.check_codex_catalog(repo / catalog_path)
    assert catalog['modelCount'] > 0 and catalog['qualification'] == 'static-catalog-not-provider-entitlement'
    assert catalog['snapshotVersion'].startswith('7.'), 'Retain the historical static catalog provenance'
    # Malformed model lists must not turn successful onboarding into zero routes.
    invalid_catalog = folder / 'invalid-catalog.json'
    original_catalog = json.loads(catalog_bytes)
    invalid_values = [
        ({**original_catalog, 'models': []}, 'must contain models'),
        ({**original_catalog, 'models': {}}, 'must contain models'),
        ({**original_catalog, 'models': [{'id': 'duplicate'}, {'id': 'duplicate'}]}, 'duplicate model IDs'),
        ({**original_catalog, 'models': [{'id': 'Duplicate'}, {'id': 'duplicate'}]}, 'duplicate model IDs'),
        ({**original_catalog, 'qualification': 'provider-entitlement'}, 'static source provenance'),
        ({**original_catalog, 'sourceSha256': ''}, 'static source provenance'),
        ({**original_catalog, 'schemaVersion': True}, 'Unsupported Codex'),
    ]
    for invalid_id in ('', ' ', 'gpt\nunsafe', '../model', 'model/path', '"quoted"', 'm' * 201, None, 42):
        invalid_values.append(({**original_catalog, 'models': [{'id': invalid_id}]}, 'unsafe or empty model ID'))
    for invalid, message in invalid_values:
        write(invalid_catalog, invalid)
        try:
            api.check_codex_catalog(invalid_catalog)
            raise AssertionError('Invalid catalog accepted')
        except RuntimeError as error:
            assert message in str(error), str(error)
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
    (base / catalog_path).write_bytes(catalog_bytes)
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
    assert target_info['codexModelCatalog'] == catalog
    assert (target / catalog_path).read_bytes() == catalog_bytes
    assert updated[str(catalog_path)] == catalog['sha256']
    assert (target / 'core-source-provenance/source' / catalog_path).read_bytes() == catalog_bytes
    provenance = api.load(target / 'core-source-provenance/inventory.json')
    assert any(entry['sourcePath'] == str(catalog_path) and entry['sha256'] == catalog['sha256'] for entry in provenance['files'])
    assert api.load(target / 'core-source-provenance/baseline-gate-status.json')['releaseQualified'] is True
    assert api.load(target / 'gate-status.json')['releaseQualified'] is False
    assert not (target / 'core-source-provenance/old').exists()
    assert api.load(target / 'notices/go/inventory.json')['missingEvidence'] == []
    # A self-consistent manifest must not allow changes outside the allowlist.
    tampered = folder / 'tampered'
    shutil.copytree(target, tampered)
    write(tampered / 'cli/Lain.Cli.dll', 'tampered client\n')
    api.manifest(tampered)
    def home(name, baseline=base):
        result = folder / name
        shutil.copytree(baseline, result / 'app')
        return result
    home_bad = home('home-bad')
    run([sys.executable, str(tampered / 'core-refresh.py'), 'install', '--package', str(tampered), '--home', str(home_bad),
         '--backup', str(home_bad / 'backup')], expected=False, contains='protected application')
    assert api.verify(home_bad / 'app') == original
    # A regenerated checksum inventory cannot make a missing/invalid target
    # catalog acceptable. It must fail before any package replacement or backup.
    for name, payload, message in [
        ('missing-catalog', None, 'Codex model catalog is missing'),
        ('empty-catalog', {**original_catalog, 'models': []}, 'must contain models'),
        ('duplicate-catalog', {**original_catalog, 'models': [{'id': 'gpt'}, {'id': 'gpt'}]}, 'duplicate model IDs'),
        ('unsafe-catalog', {**original_catalog, 'models': [{'id': 'gpt\nunsafe'}]}, 'unsafe or empty model ID'),
        ('changed-catalog', {**original_catalog, 'models': [{'id': 'different-safe-model'}]}, 'frozen package provenance'),
    ]:
        altered = folder / name
        shutil.copytree(target, altered)
        if payload is None:
            (altered / catalog_path).unlink()
        else:
            write(altered / catalog_path, payload)
        api.manifest(altered)
        api.verify(altered)
        untouched = home('home-' + name)
        run([sys.executable, str(altered / 'core-refresh.py'), 'install', '--package', str(altered), '--home', str(untouched),
             '--backup', str(untouched / 'backup')], expected=False, contains=message)
        assert api.verify(untouched / 'app') == original and not (untouched / 'backup').exists()
    # Inject a journal failure after two successful swaps: every old byte returns.
    def check_rollback(package, destination, expected):
        installed_api = module(package / 'core-refresh.py')
        real_write, tripped = installed_api.write, []
        def fail_once(path, value):
            if path.name == 'transaction.json' and value.get('status') == 'replacing' and len(value.get('completed', [])) == 2 and not tripped:
                tripped.append(True)
                raise RuntimeError('injected journal failure')
            real_write(path, value)
        installed_api.write = fail_once
        try:
            installed_api.install(argparse.Namespace(package=package, home=destination, backup=destination / 'backup'))
            raise AssertionError('Injected failure did not fire')
        except RuntimeError as error:
            assert str(error) == 'injected journal failure', str(error)
        assert tripped and api.verify(destination / 'app') == expected
        assert api.load(destination / 'backup/transaction.json')['status'] == 'rolled-back-after-error'
    home_rollback = home('home-rollback')
    check_rollback(target, home_rollback, original)
    assert (home_rollback / 'app' / catalog_path).read_bytes() == catalog_bytes
    # Exact success changes only the permitted paths and retains app source pins.
    home_success = home('home-success')
    run([sys.executable, str(target / 'core-refresh.py'), 'install', '--package', str(target), '--home', str(home_success),
         '--backup', str(home_success / 'backup')])
    assert api.verify(home_success / 'app') == updated
    assert (home_success / 'app' / catalog_path).read_bytes() == catalog_bytes
    # The exact catalog-less v8 package is a valid migration baseline. Its
    # historical checksums stay authoritative, while the replacement repairs it.
    missing_base = folder / 'catalog-less-base'
    shutil.copytree(base, missing_base)
    (missing_base / catalog_path).unlink()
    api.manifest(missing_base)
    missing_original = api.verify(missing_base)
    repaired = folder / 'repaired'
    repair_command = list(command)
    repair_command[repair_command.index('--base') + 1] = str(missing_base)
    repair_command[-1] = str(repaired)
    run(repair_command, env=env)
    assert (repaired / catalog_path).read_bytes() == catalog_bytes
    repair_rollback = home('home-repair-rollback', missing_base)
    check_rollback(repaired, repair_rollback, missing_original)
    assert not (repair_rollback / 'app' / catalog_path).exists()
    repaired_home = home('home-repaired', missing_base)
    run([sys.executable, str(repaired / 'core-refresh.py'), 'install', '--package', str(repaired), '--home', str(repaired_home),
         '--backup', str(repaired_home / 'backup')])
    assert api.verify(repaired_home / 'app') == api.verify(repaired)
    assert (repaired_home / 'app' / catalog_path).read_bytes() == catalog_bytes
    assert api.verify(missing_base) == missing_original
    # A mismatched binary/qualification must never become an accepted package.
    write(folder / 'qualification.json', {**qualification, 'sha256': '0' * 64})
    command[-1] = str(folder / 'bad-qualification')
    run(command, env=env, expected=False, contains='Independent qualification')
    assert api.verify(base) == original
print('Core refresh staging/catalog validation and provenance/catalog-less baseline repair/rejection/rollback/exact install checks passed.')
