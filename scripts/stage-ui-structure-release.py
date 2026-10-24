#!/usr/bin/env python3
"""Stage frozen application updates while retaining the exact expansion Core.

No builds, application execution, installation, service actions or release approval.
Historical expansion evidence is retained under historical/ rather than reused.
"""
import argparse
import gzip
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tarfile

QUALIFIED_CORE = 'bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87'
ARTWORK_SHA256 = 'd36cb05911456a363b275631ad7a3b1176770f0f052d2e9747593e33f947a6fd'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('base', type=Path, help='accepted expansion package, read only')
    parser.add_argument('output', type=Path, help='absolute nonexistent output directory')
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--informational-version', required=True)
    for name in ('cli', 'host', 'desktop'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    common = runpy.run_path(str(root / 'scripts/stage-component-refresh.py'))
    digest, files, serialized_version = (common[k] for k in ('digest', 'files', 'serialized_version'))
    projects = common['PROJECTS']

    def git(*command):
        return subprocess.check_output(['git', *command], cwd=root)

    revision = git('rev-parse', args.source_commit + '^{commit}').decode().strip()

    def source(path):
        return git('show', revision + ':' + path)

    def record(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + '\n')

    suffixes = args.informational_version.partition('+')[2].split('.')
    if not (len(suffixes) in (1, 2) and all(7 <= len(s) <= 40 and revision.startswith(s) for s in suffixes)
            and (len(suffixes) == 1 or suffixes[1] == revision)):
        parser.error('informational version must identify the frozen application source commit')
    base = args.base.resolve()
    if not args.output.is_absolute():
        parser.error('output must be absolute')
    out = args.output.resolve()
    archive = Path(str(out) + '.tar.gz')
    checksum = Path(str(archive) + '.sha256')
    if any(p.exists() or p.is_symlink() for p in (out, archive, checksum)):
        parser.error('output/archive/checksum must not exist')
    publishes = {name: getattr(args, name).resolve() for name in projects}
    if any(out == p or p in out.parents or out in p.parents for p in [root, base, *publishes.values()]):
        parser.error('output must not overlap checkout or input directories')
    # Freeze source used by both publish validation and notice collection.
    frozen_paths = ['src', 'scripts', 'packaging', 'Directory.Build.props', 'global.json', 'docs']
    subprocess.run(['git', 'diff', '--exit-code', revision, '--', *frozen_paths], cwd=root,
                   check=True, stdout=subprocess.DEVNULL)
    if git('ls-files', '--others', '--exclude-standard', 'src', 'scripts', 'packaging').strip():
        parser.error('untracked application or packaging sources must be resolved first')
    subprocess.run(['sha256sum', '--check', '--strict', '--quiet', 'SHA256SUMS'], cwd=base, check=True)
    original = files(base)
    proxy = files(base / 'proxy')
    if digest(base / 'proxy/lain-proxy') != QUALIFIED_CORE:
        parser.error('baseline must contain the exact accepted expansion Core')
    gate = json.loads((base / 'gate-status.json').read_text())
    qualification = json.loads((base / 'proxy/qualification.json').read_text())
    if (gate.get('backendQualified') is not True or gate.get('proxySha256') != QUALIFIED_CORE
            or qualification.get('passed') is not True or qualification.get('sha256') != QUALIFIED_CORE):
        parser.error('baseline gate and independent report must qualify the exact retained Core')
    manifest = base / 'proxy/source-manifest.json'
    if source('proxy/source-manifest.json') != manifest.read_bytes():
        parser.error('frozen application source manifest differs from retained Core')
    if json.loads((base / 'proxy/build-manifest.json').read_text()).get('binarySha256') != QUALIFIED_CORE:
        parser.error('baseline build manifest differs from retained Core')
    source_paths = git('ls-tree', '-r', '--name-only', revision).decode().splitlines()
    provider_paths = [p for p in source_paths if p.startswith('src/Lain.Host/Providers/Catalog/') and p.endswith('.json')]
    publish_hashes, asset_hashes, components = {}, {}, {}
    for name, project in projects.items():
        publish = publishes[name]
        for filename in (project, project + '.dll', project + '.deps.json', project + '.runtimeconfig.json'):
            if not (publish / filename).is_file():
                parser.error('incomplete publish: ' + str(publish / filename))
        if not os.access(publish / project, os.X_OK):
            parser.error('published launcher must be executable: ' + name)
        assembly = (publish / (project + '.dll')).read_bytes()
        if serialized_version(args.informational_version) not in assembly:
            parser.error('complete informational version missing from assembly: ' + name)
        deps = json.loads((publish / (project + '.deps.json')).read_text())
        if not deps.get('runtimeTarget', {}).get('name', '').endswith('/linux-x64'):
            parser.error('publish must target linux-x64: ' + name)
        if not json.loads((publish / (project + '.runtimeconfig.json')).read_text()).get('runtimeOptions', {}).get('includedFrameworks'):
            parser.error('publish must be self-contained: ' + name)
        assets_path = root / 'src' / project / 'obj/project.assets.json'
        assets = json.loads(assets_path.read_text())
        asset_hashes[assets_path] = digest(assets_path)
        lock_path = 'src/' + project + '/packages.lock.json'
        lock = json.loads(source(lock_path))
        locked = {(key.lower(), entry['resolved']) for target in lock['dependencies'].values()
                  for key, entry in target.items() if entry['type'].lower() != 'project'}
        restored = {(key.rsplit('/', 1)[0].lower(), key.rsplit('/', 1)[1]) for key, entry in assets['libraries'].items()
                    if entry['type'] == 'package'}
        if locked != restored:
            parser.error('restored graph differs from frozen lockfile: ' + name)
        if name == 'host':
            for path in provider_paths:
                if source(path) not in assembly:
                    parser.error('provider catalog not embedded unchanged: ' + path)
        publish_hashes[name] = files(publish)
        components[name] = {'sourceCommit': revision, 'informationalVersion': args.informational_version,
                            'assemblySha256': digest(publish / (project + '.dll')), 'lockfileSha256': digest(root / lock_path)}
    artwork = 'src/Lain.Desktop/Assets/Brand/lain-cat.png'
    published_artwork = publishes['desktop'] / 'Assets/Brand/lain-cat.png'
    if (not published_artwork.is_file() or digest(published_artwork) != ARTWORK_SHA256
            or published_artwork.read_bytes() != source(artwork)
            or source(artwork) not in (publishes['desktop'] / 'Lain.Desktop.dll').read_bytes()):
        parser.error('original artwork must occur unchanged in publish and embedded assembly')
    for path in [p for p in source_paths if p.startswith('src/Lain.Desktop/Assets/Fonts/') and p.endswith('/OFL.txt')]:
        published = publishes['desktop'] / path.removeprefix('src/Lain.Desktop/')
        if not published.is_file() or published.read_bytes() != source(path):
            parser.error('missing or changed published font license: ' + path)
    shutil.copytree(base, out)
    # Previous positive UI evidence is historical, never current acceptance.
    history = out / 'historical/expansion-baseline'
    history.mkdir(parents=True)
    for filename in ('build-info.json', 'gate-status.json', 'SHA256SUMS'):
        shutil.copy2(base / filename, history / filename)
    if (out / 'acceptance').exists():
        shutil.move(str(out / 'acceptance'), str(history / 'acceptance'))
    for name, publish in publishes.items():
        shutil.rmtree(out / name)
        shutil.copytree(publish, out / name)
    for path in ['packaging/lain', 'packaging/lain.service.in', 'packaging/lain.desktop.in', 'scripts/install-linux.sh']:
        target = out / ('install-linux.sh' if path.startswith('scripts/') else path)
        target.write_bytes(source(path))
        if path in ('packaging/lain', 'scripts/install-linux.sh'):
            target.chmod(0o700)
    for path in [p for p in source_paths if p.startswith('docs/') and not p.endswith('.png')]:
        target = out / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source(path))
    shutil.rmtree(out / 'source-provenance')
    provenance = []
    provenance_paths = [p for p in source_paths if p.startswith('src/') and
                        (p.endswith('/packages.lock.json') or p.endswith('.csproj') or p.startswith('src/Lain.Host/Providers/'))]
    provenance_paths += ['global.json', 'Directory.Build.props', artwork, 'src/Lain.Desktop/Assets/Brand/README.md']
    for path in sorted(set(provenance_paths)):
        target = out / 'source-provenance' / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source(path))
        provenance.append({'sourcePath': path, 'sourceCommit': revision, 'path': str(target.relative_to(out)), 'sha256': digest(target)})
    record(out / 'source-provenance/inventory.json', {'format': 1, 'sourceCommit': revision, 'files': provenance})
    shutil.rmtree(out / 'notices')
    subprocess.run([sys.executable, str(root / 'scripts/collect-notices.py'), str(out / 'notices')], check=True)
    if json.loads((out / 'notices/go/inventory.json').read_text()).get('binarySha256') != QUALIFIED_CORE:
        raise SystemExit('Go notices do not describe retained Core')
    info = json.loads((base / 'build-info.json').read_text())
    info.pop('releaseAcceptance', None)
    info.update(packagingCommit=git('rev-parse', 'HEAD').decode().strip(), packagingSourceDirty=bool(git('status', '--porcelain').strip()),
                componentSourceCommits={name: revision for name in projects}, components=components,
                componentInformationalVersions={name: args.informational_version for name in projects},
                sourceProvenanceInventorySha256=digest(out / 'source-provenance/inventory.json'),
                componentRefresh={'baseManifestSha256': original['SHA256SUMS'], 'proxySha256': QUALIFIED_CORE,
                                  'sourceCommit': revision, 'artworkSha256': ARTWORK_SHA256})
    info['packagingToolSha256'] = {name: digest(root / 'scripts' / name) for name in
                                 ('stage-ui-structure-release.py', 'stage-component-refresh.py', 'collect-notices.py', 'collect-go-notices.py')}
    record(out / 'build-info.json', info)
    for key in ('acceptance', 'stagingManifestSha256'):
        gate.pop(key, None)
    gate.update(releaseQualified=False, expansionQualified=False, status='awaiting-ui-structure-package-and-installed-acceptance',
                previousAcceptance='historical/expansion-baseline/acceptance', uiBaselineSuperseded=True)
    record(out / 'gate-status.json', gate)
    for name, hashes in publish_hashes.items():
        if files(publishes[name]) != hashes or files(out / name) != hashes:
            raise SystemExit('publish input changed during staging: ' + name)
    if files(base) != original or files(out / 'proxy') != proxy:
        raise SystemExit('baseline or retained Core directory changed during staging')
    if any(digest(path) != expected for path, expected in asset_hashes.items()):
        raise SystemExit('restored graph changed during notice collection')
    subprocess.run(['git', 'diff', '--exit-code', revision, '--', *frozen_paths], cwd=root,
                   check=True, stdout=subprocess.DEVNULL)
    record(out / 'delivery-assets.json', {'format': 1, 'sourceCommit': revision, 'artworkSha256': ARTWORK_SHA256,
        'artwork': 'desktop/Assets/Brand/lain-cat.png', 'desktopTemplate': 'packaging/lain.desktop.in'})
    (out / 'SHA256SUMS').write_text(''.join(f'{digest(p)}  ./{p.relative_to(out)}\n' for p in sorted(out.rglob('*'))
                                         if p.is_file() and p != out / 'SHA256SUMS'))
    subprocess.run(['sha256sum', '--check', '--strict', '--quiet', 'SHA256SUMS'], cwd=out, check=True)
    epoch = int(os.environ.get('SOURCE_DATE_EPOCH') or git('show', '-s', '--format=%ct', revision).decode().strip())

    def normalized(member):
        member.uid = member.gid = 0
        member.uname = member.gname = ''
        member.mtime = epoch
        return member

    with archive.open('xb') as raw:
        with gzip.GzipFile(filename='', fileobj=raw, mode='wb', mtime=0, compresslevel=3) as compressed:
            with tarfile.open(fileobj=compressed, mode='w|') as tar:
                tar.add(out, arcname=out.name, filter=normalized)
    checksum.write_text(f'{digest(archive)}  {archive.name}\n')
    print(f'Staged {out}; exact expansion Core retained; release gates false. Nothing installed.')


if __name__ == '__main__':
    main()
