#!/usr/bin/env python3
"""Exercise fresh publish metadata blocks with fake bytes; no builds/apps/services."""
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SHELL = (ROOT / 'scripts/publish-linux.sh').read_text()


def block(name):
    result = re.search(r"<<'" + name + r"'\n(.*?)\n" + name + r"(?:\n|$)", SHELL, re.S)
    assert result, name
    return result[1]


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n' if isinstance(value, dict) else value)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, *, expected=True, contains=None, **kwargs):
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, **kwargs)
    assert (result.returncode == 0) == expected, result.stdout
    assert contains is None or contains in result.stdout, result.stdout
    return result.stdout.strip()


with tempfile.TemporaryDirectory(prefix='lain-fresh-metadata-') as temporary:
    folder = Path(temporary)
    repo, out, properties = folder / 'repo', folder / 'out', folder / 'properties'
    repo.mkdir()
    projects = {'cli': 'Lain.Cli', 'host': 'Lain.Host', 'desktop': 'Lain.Desktop'}
    for name, project in projects.items():
        write(repo / 'src' / project / (project + '.csproj'), '<Project Sdk="Microsoft.NET.Sdk"/>\n')
        write(repo / 'src' / project / 'packages.lock.json', {'version': 1, 'dependencies': {}})
    write(repo / 'Directory.Build.props', '<Project/>\n')
    write(repo / 'global.json', {'sdk': {'version': '10.0.111'}})
    for name in ('publish-linux.sh', 'build-core.sh', 'prepare-core.py', 'build-core-verify.py', 'collect-notices.py', 'collect-go-notices.py'):
        write(repo / 'scripts' / name, '# fixture tool source\n')
    shutil.copyfile(ROOT / 'scripts/stage-component-refresh.py', repo / 'scripts/stage-component-refresh.py')
    shutil.copyfile(ROOT / 'scripts/core-refresh.py', repo / 'scripts/core-refresh.py')
    write(repo / 'proxy/source-manifest.json', {'schemaVersion': 2, 'fixture': True, 'buildInputs': {}})
    write(repo / 'proxy/upstream.json', {'fixture': True})
    write(repo / 'proxy/toolchain.json', {'goVersion': 'go1.27.2'})
    write(repo / 'proxy/LICENSE.upstream', 'Fixture license\n')
    catalog_source = repo / 'proxy/codex-model-catalog.json'
    shutil.copyfile(ROOT / 'proxy/codex-model-catalog.json', catalog_source)
    catalog_bytes = catalog_source.read_bytes()
    run(['git', 'init', '-q', str(repo)])
    run(['git', 'add', '.'], cwd=repo)
    run(['git', '-c', 'user.name=Fixture', '-c', 'user.email=tom@grassmann-it.de', 'commit', '-qm', 'Fixture'], cwd=repo)
    revision = run(['git', 'rev-parse', 'HEAD'], cwd=repo)
    run([sys.executable, '-c', block('PYCATALOG')], cwd=repo)
    serialized = runpy.run_path(str(repo / 'scripts/stage-component-refresh.py'))['serialized_version']
    versions = {'cli': '1.2.3', 'host': '1.2.4-rc.2', 'desktop': '1.2.5+custom'}
    actual_versions = {}
    for name, project in projects.items():
        evaluation = properties / (name + '.evaluated.json')
        record_path = properties / (name + '.json')
        write(evaluation, {'Properties': {'Version': versions[name], 'InformationalVersion': versions[name]}})
        run([sys.executable, '-c', block('PYPUBLISHVERSION'), str(evaluation), str(record_path), revision, project])
        record = json.loads(record_path.read_text())
        version = versions[name] + ('.' if '+' in versions[name] else '+') + revision
        assert record['publishProperties']['InformationalVersion'] == version
        assert record['publishProperties']['IncludeSourceRevisionInInformationalVersion'] == 'false'
        actual_versions[name] = version
        assembly = out / name / (project + '.dll')
        assembly.parent.mkdir(parents=True)
        assembly.write_bytes(b'fake assembly bytes\0' + serialized(version))
        (out / name / 'Lain.Core.dll').write_bytes(('distinct shared ' + name).encode())
    write(out / 'proxy/lain-proxy', 'fake Core bytes never executed\n')
    write(out / 'proxy/source-manifest.json', json.loads((repo / 'proxy/source-manifest.json').read_text()))
    write(out / 'proxy/qualification.json', {'passed': True, 'sha256': digest(out / 'proxy/lain-proxy'), 'scope': 'Fixture only'})
    build = {'binarySha256': digest(out / 'proxy/lain-proxy'), 'sourceManifestSha256': digest(out / 'proxy/source-manifest.json'),
             'goVersion': 'go version go1.27.2 linux/amd64', 'snapshotVersion': '8.0.41', 'lainPatchVersion': 1,
             'upstreamVersion': 'v8.0.41', 'upstreamCommit': 'a' * 40, 'upstreamSourceModified': True,
             'upstreamArchiveSha256': 'b' * 64, 'patchSetSha256': 'c' * 64}
    write(out / 'proxy/lain-proxy.manifest.json', build)
    # Execute the publisher's real static payload copy block. Testing metadata
    # with a manually seeded target catalog would miss the original omission.
    copies = SHELL.split('scripts/build-core.sh "$out/proxy/lain-proxy"\n', 1)[1].split('python3 scripts/prepare-core.py ', 1)[0]
    run(['bash', '-eu', '-c', copies], cwd=repo, env=dict(os.environ, out=str(out)))
    assert (out / 'proxy/codex-model-catalog.json').read_bytes() == catalog_bytes
    run([sys.executable, '-c', block('PYSOURCE'), str(out / 'core-source-provenance')], cwd=repo)
    assert (out / 'core-source-provenance/proxy/codex-model-catalog.json').read_bytes() == catalog_bytes
    command = [sys.executable, '-c', block('PYBUILDINFO'), str(out), '10.0.111', revision, str(properties)]
    run(command, cwd=repo)
    info = json.loads((out / 'build-info.json').read_text())
    assert info['componentSourceCommits'] == {name: revision for name in projects}
    assert info['componentInformationalVersions'] == actual_versions
    assert info['compileInputsFrozen'] is True
    assert info['coreSourceCommit'] == revision and info['proxySha256'] == build['binarySha256']
    assert info['coreBuild']['upstreamVersion'] == 'v8.0.41'
    assert info['qualificationSha256'] == digest(out / 'proxy/qualification.json')
    check_catalog = runpy.run_path(str(repo / 'scripts/core-refresh.py'))['check_codex_catalog']
    assert info['codexModelCatalog'] == check_catalog(catalog_source)
    assert info['codexModelCatalog']['snapshotVersion'].startswith('7.')
    assert info['codexModelCatalog']['qualification'] == 'static-catalog-not-provider-entitlement'
    assert info['packagingToolSha256']['core-refresh.py'] == digest(repo / 'scripts/core-refresh.py')
    for name, project in projects.items():
        component = info['components'][name]
        assert component['assemblySha256'] == digest(out / name / (project + '.dll'))
        assert component['lockfileSha256'] == digest(repo / 'src' / project / 'packages.lock.json')
        assert component['localSharedAssemblies']['Lain.Core'] == digest(out / name / 'Lain.Core.dll')
        assert json.loads((out / 'source-provenance/publish-properties' / (name + '.json')).read_text())['sourceCommit'] == revision
    inventory = json.loads((out / 'source-provenance/inventory.json').read_text())
    assert inventory['sourceCommit'] == revision
    assert all(digest(out / entry['path']) == entry['sha256'] for entry in inventory['files'])
    # Existing custom version metadata is preserved, not replaced with a guessed default.
    assert info['componentInformationalVersions']['desktop'] == '1.2.5+custom.' + revision
    # Fresh metadata must not seal a missing, empty, or changed catalog even if
    # the rest of the payload and component provenance remain self-consistent.
    target_catalog = out / 'proxy/codex-model-catalog.json'
    target_catalog.unlink()
    run(command, cwd=repo, expected=False, contains='Codex model catalog is missing')
    write(target_catalog, {**json.loads(catalog_bytes), 'models': []})
    run(command, cwd=repo, expected=False, contains='must contain models')
    write(target_catalog, {**json.loads(catalog_bytes), 'models': [{'id': 'different-safe-model'}]})
    run(command, cwd=repo, expected=False, contains='differs from frozen source')
    target_catalog.write_bytes(catalog_bytes)
    # Missing source is detected before any restore/build in the real publisher.
    catalog_source.unlink()
    run([sys.executable, '-c', block('PYCATALOG')], cwd=repo, expected=False, contains='Codex model catalog is missing')
    catalog_source.write_bytes(catalog_bytes)
    # A claimed version absent from the actual assembly must fail.
    original = (out / 'host/Lain.Host.dll').read_bytes()
    (out / 'host/Lain.Host.dll').write_bytes(b'wrong version')
    run(command, cwd=repo, expected=False, contains='informational version missing')
    (out / 'host/Lain.Host.dll').write_bytes(original)
    # A stale Core sidecar must fail rather than receiving a fresh source pin.
    write(out / 'proxy/lain-proxy', 'changed Core bytes')
    run(command, cwd=repo, expected=False, contains='Core build provenance changed')
    write(properties / 'unsafe.json', {'Properties': {'Version': '1.2.3', 'InformationalVersion': '1.2.3;Unsafe=1'}})
    run([sys.executable, '-c', block('PYPUBLISHVERSION'), str(properties / 'unsafe.json'), str(properties / 'unsafe.out.json'), revision, 'Lain.Cli'],
        expected=False, contains='informational version or frozen revision is invalid')
print('Fresh payload catalog copy/static provenance/omission rejection and exact component/Core build-info checks passed.')
