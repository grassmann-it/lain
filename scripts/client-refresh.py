#!/usr/bin/env python3
"""Stage/install only CLI and Desktop clients over the frozen a66cb2a Host/Core.

Never builds, runs application binaries, changes service state or private data.
"""
import argparse
import ctypes
import fcntl
import gzip
import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tarfile

CORE = 'bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87'
HOST_SOURCE = 'a66cb2a19774d897702724f9f40755b74c82b2a6'
HOST_DLL = '06fe63b0c845348a7949d95afe8ba0f8eb65bc06063d0ad473c55f88f5b57cb9'
ARTWORK = 'b6d32ab02ca21965f6941f61ec209ff756c7ea9841641cf20c0f5bda4aefaba7'
CLIENT_DOCS = ['docs/cli-usage.md', 'skills/lain-usage/SKILL.md']
ITEMS = ['cli', 'desktop', 'notices', 'client-source-provenance', 'client-refresh.py', 'client-refresh.json', 'build-info.json', 'gate-status.json', 'delivery-assets.json', 'SHA256SUMS', *CLIENT_DOCS]
PROJECTS = {'cli': 'Lain.Cli', 'desktop': 'Lain.Desktop'}


def require(value, message):
    if not value:
        raise RuntimeError(message)


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def inventory(root):
    require(root.is_dir() and not root.is_symlink(), 'Expected regular directory: ' + str(root))
    result = {}
    for path in sorted(root.rglob('*')):
        require(not path.is_symlink(), 'Symlinks are not supported: ' + str(path))
        if path.is_file() and path.relative_to(root).as_posix() != '.lain-package':
            result[path.relative_to(root).as_posix()] = digest(path)
    return result


def manifest(root):
    values = inventory(root)
    values.pop('SHA256SUMS', None)
    (root / 'SHA256SUMS').write_text(''.join(f'{value}  ./{name}\n' for name, value in sorted(values.items())))


def verify(root):
    recorded = {}
    for line in (root / 'SHA256SUMS').read_text().splitlines():
        require(len(line) > 68 and line[64:68] == '  ./', 'Invalid checksum manifest line')
        name = line[68:]
        require(name and not name.startswith('/') and '..' not in Path(name).parts and name not in recorded, 'Unsafe/duplicate manifest path')
        recorded[name] = line[:64]
    actual = inventory(root)
    actual.pop('SHA256SUMS', None)
    require(actual == recorded, 'Package file inventory differs from SHA256SUMS: ' + str(root))
    return inventory(root)


def load(path):
    return json.loads(path.read_text())


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n')


def owned_path(name):
    return any(name == item or name.startswith(item + '/') for item in ITEMS)


def stage(args):
    root = Path(__file__).resolve().parents[1]
    git = lambda *parts: subprocess.check_output(['git', *parts], cwd=root)
    revision = git('rev-parse', args.source_commit + '^{commit}').decode().strip()
    source = lambda path: git('show', revision + ':' + path)
    base = args.base.resolve(strict=True)
    require(args.output.is_absolute(), 'Output must be absolute')
    out = args.output.resolve()
    archive, checksum = Path(str(out) + '.tar.gz'), Path(str(out) + '.tar.gz.sha256')
    require(not any(path.exists() or path.is_symlink() for path in (out, archive, checksum)), 'Output/archive/checksum must not exist')
    publishes = {name: getattr(args, name).resolve(strict=True) for name in PROJECTS}
    require(not any(out == path or path in out.parents or out in path.parents for path in [root, base, *publishes.values()]), 'Output overlaps checkout/input')
    frozen = ['src', 'Directory.Build.props', 'global.json', 'scripts/client-refresh.py', 'scripts/collect-notices.py', 'scripts/collect-go-notices.py', 'scripts/stage-component-refresh.py', 'packaging/licenses', 'docs/tray-client-delivery.md', *CLIENT_DOCS]
    subprocess.run(['git', 'diff', '--exit-code', revision, '--', *frozen], cwd=root, check=True, stdout=subprocess.DEVNULL)
    require(not git('ls-files', '--others', '--exclude-standard', 'src').strip(), 'Untracked application sources must be resolved')
    suffix = args.informational_version.partition('+')[2].split('.')
    require(len(suffix) in (1, 2) and all(7 <= len(value) <= 40 and revision.startswith(value) for value in suffix)
            and (len(suffix) == 1 or suffix[1] == revision), 'Version must identify the frozen client commit')
    base_files = verify(base)
    info, gate = load(base / 'build-info.json'), load(base / 'gate-status.json')
    require(info['componentSourceCommits']['host'] == HOST_SOURCE and digest(base / 'host/Lain.Host.dll') == HOST_DLL, 'Baseline must retain the installed a66cb2a Host')
    require(digest(base / 'proxy/lain-proxy') == CORE and gate.get('backendQualified') is True, 'Baseline must retain qualified exact Go Core')
    qualification = load(base / 'proxy/qualification.json')
    require(qualification.get('passed') is True and qualification.get('sha256') == CORE, 'Exact Core qualification is missing')
    # Shared clients may be republished locally, but cannot introduce new contracts
    # over the old running Host. RID-only lock changes are verified via graphs below.
    changed = git('diff', '--name-only', HOST_SOURCE, revision, '--', 'src/Lain.Host', 'src/Lain.Client', 'src/Lain.Core', 'Directory.Build.props', 'global.json').decode().splitlines()
    require(not [name for name in changed if not name.endswith('/packages.lock.json')], 'Host/shared contract sources changed; this narrow no-restart refresh is unsuitable')
    common = runpy.run_path(str(root / 'scripts/stage-component-refresh.py'))
    blob = common['serialized_version'](args.informational_version)
    hashes, components, assets_hashes = {}, {}, {}
    for name, project in PROJECTS.items():
        publish = publishes[name]
        hashes[name] = inventory(publish)
        for filename in [project, project + '.dll', project + '.deps.json', project + '.runtimeconfig.json', 'Lain.Client.dll', 'Lain.Core.dll']:
            require((publish / filename).is_file(), 'Incomplete publish: ' + str(publish / filename))
        require(os.access(publish / project, os.X_OK), 'Published apphost is not executable: ' + name)
        for assembly in [project, 'Lain.Client', 'Lain.Core']:
            require(blob in (publish / (assembly + '.dll')).read_bytes(), 'Wrong complete assembly version: ' + name + '/' + assembly)
        require(load(publish / (project + '.deps.json')).get('runtimeTarget', {}).get('name', '').endswith('/linux-x64'), 'Publish must target linux-x64')
        require(load(publish / (project + '.runtimeconfig.json')).get('runtimeOptions', {}).get('includedFrameworks'), 'Publish must be self-contained')
        assets_path = root / 'src' / project / 'obj/project.assets.json'
        assets, lock = load(assets_path), json.loads(source('src/' + project + '/packages.lock.json'))
        assets_hashes[assets_path] = digest(assets_path)
        restored = {(key.rsplit('/', 1)[0].lower(), key.rsplit('/', 1)[1]) for key, value in assets['libraries'].items() if value['type'] == 'package'}
        locked = {(key.lower(), value['resolved']) for target in lock['dependencies'].values() for key, value in target.items() if value['type'].lower() != 'project'}
        require(restored == locked, 'Restored graph differs from frozen lock: ' + name)
        components[name] = {'sourceCommit': revision, 'informationalVersion': args.informational_version,
                            'assemblySha256': digest(publish / (project + '.dll')), 'lockfileSha256': digest(root / 'src' / project / 'packages.lock.json'),
                            'localSharedAssemblies': {assembly: digest(publish / (assembly + '.dll')) for assembly in ['Lain.Client', 'Lain.Core']}}
    for assembly in ['Lain.Client.dll', 'Lain.Core.dll']:
        require(digest(publishes['cli'] / assembly) == digest(publishes['desktop'] / assembly), 'Client publishes contain different local shared libraries')
    artwork_file = publishes['desktop'] / 'Assets/Brand/lain-cat.png'
    require(digest(artwork_file) == ARTWORK and artwork_file.read_bytes() == source('src/Lain.Desktop/Assets/Brand/lain-cat.png'),
            'Original artwork differs from the explicit new original/frozen source')
    for path in (root / 'src/Lain.Desktop/Assets/Fonts').glob('*/OFL.txt'):
        require((publishes['desktop'] / path.relative_to(root / 'src/Lain.Desktop')).read_bytes() == path.read_bytes(), 'Published font notice differs')
    shutil.copytree(base, out, ignore=shutil.ignore_patterns('.lain-package'))
    for name, publish in publishes.items():
        shutil.rmtree(out / name)
        shutil.copytree(publish, out / name)
    for path in CLIENT_DOCS:
        target = out / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source(path))
    provenance = out / 'client-source-provenance'
    require(not provenance.exists(), 'Baseline already contains this client-refresh provenance; use a dedicated next-release procedure')
    records = []
    for path in git('ls-tree', '-r', '--name-only', revision).decode().splitlines():
        if (path.startswith(('src/Lain.Cli/', 'src/Lain.Desktop/', 'src/Lain.Client/', 'src/Lain.Core/')) and path.endswith(('.cs', '.csproj', 'packages.lock.json', 'OFL.txt', 'Assets/Brand/README.md'))
                or path in ['scripts/client-refresh.py', 'docs/tray-client-delivery.md', 'Directory.Build.props', 'global.json', *CLIENT_DOCS]):
            target = provenance / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(source(path))
            records.append({'sourcePath': path, 'sourceCommit': revision, 'sha256': digest(target)})
    write(provenance / 'inventory.json', {'sourceCommit': revision, 'scope': 'New CLI/Desktop and unchanged shared client source; retained Host source provenance stays in source-provenance/', 'files': records})
    write(provenance / 'baseline-build-info.json', info)
    write(provenance / 'baseline-gate-status.json', gate)
    delivery_assets = load(base / 'delivery-assets.json')
    write(provenance / 'baseline-delivery-assets.json', delivery_assets)
    delivery_assets.update(sourceCommit=revision, artworkSha256=ARTWORK)
    write(out / 'delivery-assets.json', delivery_assets)
    old_inventory = load(base / 'notices/inventory.json')
    shutil.rmtree(out / 'notices')
    subprocess.run([sys.executable, str(root / 'scripts/collect-notices.py'), str(out / 'notices')], check=True)
    new_inventory = load(out / 'notices/inventory.json')
    host_graph = lambda value: sorted((entry['id'].lower(), entry['version']) for entry in value['packages'] if 'Lain.Host' in entry['projects'])
    require(host_graph(old_inventory) == host_graph(new_inventory), 'Retained Host notice graph differs; restore its exact locked RID graph')
    # The actual unchanged Go component retains its original inventory/notices.
    shutil.rmtree(out / 'notices/go')
    shutil.copytree(base / 'notices/go', out / 'notices/go')
    for name, component in components.items():
        info['componentSourceCommits'][name] = revision
        info['componentInformationalVersions'][name] = args.informational_version
        info['components'][name] = component
    info.pop('releaseAcceptance', None)
    info.update(packagingCommit=revision, packagingSourceDirty=bool(git('status', '--porcelain').strip()),
                clientRefresh={'baseManifestSha256': base_files['SHA256SUMS'], 'sourceCommit': revision, 'hostUnchanged': True,
                               'proxyUnchanged': True, 'serviceRestartRequired': False, 'installerSha256': digest(root / 'scripts/client-refresh.py'),
                               'previousArtworkSha256': base_files['desktop/Assets/Brand/lain-cat.png'], 'artworkSha256': ARTWORK})
    write(out / 'build-info.json', info)
    gate.pop('acceptance', None)
    gate.update(releaseQualified=False, uiStructureQualified=False, clientRefreshQualified=False,
                status='awaiting-tray-cli-package-and-installed-client-acceptance',
                inheritedClientScope='Prior application qualification describes its frozen versions; exact Host/Core scope is retained.',
                previousClientGate='client-source-provenance/baseline-gate-status.json')
    write(out / 'gate-status.json', gate)
    (out / 'client-refresh.py').write_bytes(source('scripts/client-refresh.py'))
    (out / 'client-refresh.py').chmod(0o700)
    write(out / 'client-refresh.json', {'format': 1, 'kind': 'cli-desktop-only-a66cb2a-host', 'baseFiles': base_files,
        'installItems': ITEMS, 'sourceCommit': revision, 'informationalVersion': args.informational_version,
        'proxySha256': CORE, 'hostAssemblySha256': HOST_DLL, 'protectedHostSourceCommit': HOST_SOURCE,
        'artworkReplacement': {'source': 'desktop/Assets/Brand/lain-cat.png',
                               'previousSha256': base_files['desktop/Assets/Brand/lain-cat.png'], 'sha256': ARTWORK}})
    for name in PROJECTS:
        require(inventory(publishes[name]) == hashes[name] == inventory(out / name), 'Publish changed during staging')
    require(inventory(base) == base_files, 'Baseline changed during staging')
    require(all(digest(path) == expected for path, expected in assets_hashes.items()), 'Restored graph changed during staging')
    subprocess.run(['git', 'diff', '--exit-code', revision, '--', *frozen], cwd=root, check=True, stdout=subprocess.DEVNULL)
    manifest(out)
    target_files = verify(out)
    require(all(owned_path(name) or target_files.get(name) == value for name, value in base_files.items()), 'Protected baseline file changed')
    require(all(owned_path(name) or base_files.get(name) == value for name, value in target_files.items()), 'Unexpected new protected file')
    epoch = int(os.environ.get('SOURCE_DATE_EPOCH') or git('show', '-s', '--format=%ct', revision).decode().strip())
    def normalize(member):
        member.uid = member.gid = 0
        member.uname = member.gname = ''
        member.mtime = epoch
        return member
    with archive.open('xb') as raw, gzip.GzipFile(filename='', fileobj=raw, mode='wb', mtime=0, compresslevel=3) as compressed:
        with tarfile.open(fileobj=compressed, mode='w|') as tar:
            tar.add(out, arcname=out.name, filter=normalize)
    checksum.write_text(f'{digest(archive)}  {archive.name}\n')
    print('Staged client refresh; Host/Core unchanged, current client acceptance false. Nothing installed.')


def refuse_running_clients(app):
    prefixes = [str(app / name) + '/' for name in PROJECTS]
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            executable = os.readlink(entry / 'exe')
            arguments = (entry / 'cmdline').read_bytes().split(b'\0')
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
        if any(executable.startswith(prefix) or any(argument.decode(errors='replace').startswith(prefix) for argument in arguments) for prefix in prefixes):
            raise RuntimeError('Close the installed client completely before update (tray Quit, not Hide); PID ' + entry.name)



def owned_artwork(home, previous_sha):
    """Discover a verified integration pair from the existing map, never a guessed path."""
    ownership_path = home / 'desktop-integration.json'
    require(ownership_path.is_file() and not ownership_path.is_symlink(), 'Desktop integration ownership metadata is missing or a symlink')
    ownership = load(ownership_path)
    require(isinstance(ownership, dict), 'Invalid desktop integration ownership map')
    candidates = [Path(name) for name in ownership if isinstance(name, str) and Path(name).parts[-2:] == ('icons', 'lain.png')]
    require(len(candidates) == 1, 'Expected exactly one owned icons/lain.png entry')
    icon = candidates[0]
    desktop = icon.parent.parent / 'applications/lain.desktop'
    for path in (icon, desktop, ownership_path):
        require(path.is_absolute() and path.is_file() and path.resolve(strict=True) == path and not path.is_symlink(), 'Integration files must be regular canonical absolute paths')
    require(str(desktop) in ownership and ownership[str(desktop)] == digest(desktop), 'Paired desktop entry is foreign or customized')
    require(ownership[str(icon)] == previous_sha == digest(icon), 'Owned launcher icon is foreign or customized')
    escaped = str(icon).replace('\\', '\\\\').replace('\t', '\\t')
    icon_fields = [line[5:] for line in desktop.read_text().splitlines() if line.startswith('Icon=')]
    require(icon_fields == [escaped], 'Paired desktop entry does not name the exact owned icon')
    return icon, desktop, ownership_path, ownership


def install(args):
    package, home = args.package.resolve(strict=True), args.home.resolve(strict=True)
    app, backup = home / 'app', args.backup
    require(backup.is_absolute() and not backup.exists() and not backup.is_symlink(), 'Backup must be absolute and nonexistent')
    backup = backup.resolve()
    require(home in backup.parents and app not in backup.parents and backup != app, 'Backup must be inside LAIN_HOME, outside app/')
    require((app / '.lain-package').is_file() and not app.is_symlink(), 'Installed app must be managed and regular')
    require(package != app and app not in package.parents and package not in app.parents, 'Package must not overlap installed app')
    require(package != backup and package not in backup.parents and backup not in package.parents, 'Backup must not overlap the input package')
    with (home / '.client-refresh.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        target = verify(package)
        delta = load(package / 'client-refresh.json')
        require(delta.get('format') == 1 and delta.get('kind') == 'cli-desktop-only-a66cb2a-host' and delta.get('installItems') == ITEMS, 'Unsupported refresh manifest')
        base = delta['baseFiles']
        require(verify(app) == base, 'Installed files do not exactly match this refresh baseline')
        require(digest(app / 'host/Lain.Host.dll') == HOST_DLL and digest(app / 'proxy/lain-proxy') == CORE, 'Protected Host/Core differs')
        require(all(owned_path(name) or target.get(name) == value for name, value in base.items()) and
                all(owned_path(name) or base.get(name) == value for name, value in target.items()), 'Refresh changes a protected file')
        require(digest(Path(__file__)) == target['client-refresh.py'], 'Run the exact staged client-refresh.py installer')
        artwork = delta.get('artworkReplacement', {})
        require(artwork.get('source') == 'desktop/Assets/Brand/lain-cat.png' and artwork.get('sha256') == ARTWORK
                and artwork.get('previousSha256') == base['desktop/Assets/Brand/lain-cat.png']
                and digest(package / artwork['source']) == ARTWORK, 'Wrong original artwork replacement provenance')
        icon, desktop_entry, ownership_path, ownership = owned_artwork(home, artwork['previousSha256'])
        desktop_hash, ownership_hash = digest(desktop_entry), digest(ownership_path)
        next_ownership = dict(ownership)
        next_ownership[str(icon)] = ARTWORK
        refuse_running_clients(app)
        renameat2 = ctypes.CDLL(None, use_errno=True).renameat2
        renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
        def exchange(left, right):
            if renameat2(-100, os.fsencode(left), -100, os.fsencode(right), 2):
                code = ctypes.get_errno()
                raise OSError(code, os.strerror(code))
        backup.mkdir(parents=True, mode=0o700)
        require(backup.stat().st_dev == app.stat().st_dev == icon.stat().st_dev == ownership_path.stat().st_dev,
                'Backup, app and owned integration must share a filesystem for atomic replacement')
        prepared = backup / 'previous'
        prepared.mkdir(mode=0o700)
        for name in ITEMS:
            source, destination = package / name, prepared / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            if source.is_dir():
                shutil.copytree(source, destination)
            else:
                shutil.copy2(source, destination)
        external = backup / 'external'
        external.mkdir(mode=0o700)
        shutil.copy2(package / artwork['source'], external / 'lain.png')
        shutil.copymode(icon, external / 'lain.png')
        write(external / 'desktop-integration.json', next_ownership)
        shutil.copymode(ownership_path, external / 'desktop-integration.json')
        require(inventory(app) == base and verify(package) == target, 'Input changed while preparing update')
        require(digest(icon) == artwork['previousSha256'] and digest(ownership_path) == ownership_hash
                and digest(desktop_entry) == desktop_hash, 'Owned integration changed while preparing update')
        refuse_running_clients(app)
        completed = []
        external_completed = []
        integration_record = {'icon': str(icon), 'ownership': str(ownership_path), 'desktopUnchanged': str(desktop_entry),
                              'desktopSha256': desktop_hash, 'previousIconSha256': artwork['previousSha256'], 'newIconSha256': ARTWORK}
        write(backup / 'transaction.json', {'status': 'prepared', 'baseManifestSha256': base['SHA256SUMS'], 'targetManifestSha256': target['SHA256SUMS'], 'items': ITEMS, 'integration': integration_record})
        try:
            for name in ITEMS:
                current, pending = app / name, prepared / name
                existed = current.exists()
                if existed:
                    exchange(current, pending)
                else:
                    pending.rename(current)
                completed.append((name, existed))
                write(backup / 'transaction.json', {'status': 'replacing', 'completed': completed, 'items': ITEMS, 'integration': integration_record})
            # Both swaps retain the old external bytes at backup/external/.
            require(digest(icon) == artwork['previousSha256'] and digest(ownership_path) == ownership_hash
                    and digest(desktop_entry) == desktop_hash, 'Owned integration changed before replacement')
            for name, current, pending in [('icon', icon, external / 'lain.png'), ('ownership', ownership_path, external / 'desktop-integration.json')]:
                exchange(current, pending)
                external_completed.append((name, current, pending))
                write(backup / 'transaction.json', {'status': 'replacing-integration', 'completed': completed,
                    'externalCompleted': [item[0] for item in external_completed], 'integration': integration_record})
            require(digest(icon) == ARTWORK and load(ownership_path) == next_ownership and digest(desktop_entry) == desktop_hash,
                    'Owned icon/ownership replacement differs; paired desktop entry must stay unchanged')
            require(verify(app) == target, 'Installed payload differs from staged package')
            write(backup / 'transaction.json', {'status': 'installed', 'completed': completed, 'baseFiles': base, 'targetManifestSha256': target['SHA256SUMS'],
                                                'integration': integration_record, 'externalCompleted': [item[0] for item in external_completed],
                                                'scope': 'Only client payloads/notices/provenance/metadata and exact owned icon/map changed; paired desktop/Host/proxy/service/private data untouched.'})
        except BaseException:
            for name, current, pending in reversed(external_completed):
                exchange(current, pending)
            for name, existed in reversed(completed):
                if existed:
                    exchange(app / name, prepared / name)
                else:
                    (app / name).rename(prepared / name)
            require(verify(app) == base and digest(icon) == artwork['previousSha256'] and digest(ownership_path) == ownership_hash
                    and digest(desktop_entry) == desktop_hash, 'Rollback inventory/integration differs; preserve backup and inspect transaction.json')
            write(backup / 'transaction.json', {'status': 'rolled-back-after-error', 'completed': completed, 'integration': integration_record})
            raise
    print('Installed CLI/Desktop, release metadata and verified owned icon/map. Desktop entry/Host/Core/service unchanged; no restart performed. Backup: ' + str(backup))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    staging = commands.add_parser('stage')
    for name in ('base', 'cli', 'desktop', 'output'):
        staging.add_argument('--' + name, type=Path, required=True)
    staging.add_argument('--source-commit', required=True)
    staging.add_argument('--informational-version', required=True)
    installing = commands.add_parser('install')
    for name in ('package', 'home', 'backup'):
        installing.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    (stage if args.command == 'stage' else install)(args)


if __name__ == '__main__':
    main()
