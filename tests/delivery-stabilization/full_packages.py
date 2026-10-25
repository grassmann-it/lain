#!/usr/bin/env python3
"""Plan or qualify exact fresh Monitor/Gateway and mixed-baseline full app updates.

Uses existing real installer/Host/schema4 primitives in a private loopback-only
namespace. Does not control systemd, touch installed state, or manufacture the
service-update qualification/admission report. Only --execute runs payloads.
"""
import argparse
import contextlib
import hashlib
import importlib.util
import io
import json
import os
import selectors
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from types import SimpleNamespace
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]


def need(value, message):
    if not value: raise RuntimeError(message)


def digest(path):
    with Path(path).open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    exec(compile(path.read_bytes(), str(path), 'exec'), result.__dict__)
    return result


def write(path, value):
    with path.open('x') as stream:
        json.dump(value, stream, indent=2)
        stream.write('\n')


def verify_monitor_provenance(common, package, files, info, source):
    need(info.get('commit') == source and info.get('compileInputsFrozen') is True and
         info.get('runtime') == 'linux-x64' and info.get('runtimeMode') == 'monitor', 'Fresh Monitor source/mode differs')
    inventory_path = 'source-provenance/inventory.json'
    need(info['sourceProvenanceInventorySha256'] == files[inventory_path], 'Monitor source inventory hash differs')
    inventory = common.load(package / inventory_path)
    need(inventory.get('format') == 1 and inventory.get('sourceCommit') == source, 'Monitor source inventory identity differs')
    seen = set()
    for record in inventory['files']:
        name = record['path']
        need(isinstance(name, str) and name.startswith('source-provenance/') and
             '..' not in Path(name).parts and name not in seen and files.get(name) == record['sha256'], 'Monitor provenance entry differs')
        seen.add(name)
    for component, project in (('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')):
        item, version = info['components'][component], info['componentInformationalVersions'][component]
        properties_path = 'source-provenance/publish-properties/' + component + '.json'
        lock_path = 'source-provenance/source/src/' + project + '/packages.lock.json'
        need(properties_path in seen and lock_path in seen and
             item['sourceCommit'] == info['componentSourceCommits'][component] == source and
             item['informationalVersion'] == version and version.partition('+')[2].split('.')[-1] == source and
             all(7 <= len(part) <= 40 and source.startswith(part) for part in version.partition('+')[2].split('.')) and
             item['assemblySha256'] == files[component + '/' + project + '.dll'] and item['lockfileSha256'] == files[lock_path],
             'Monitor component source/assembly/lock differs: ' + component)
        properties = common.load(package / properties_path)
        values = properties['publishProperties']
        need(properties['sourceCommit'] == source and properties['project'] == project and
             values['InformationalVersion'] == version and values['RuntimeIdentifier'] == 'linux-x64' and
             values['SelfContained'] == 'true', 'Monitor publish properties differ: ' + component)
        shared = {Path(name).stem: value for name, value in files.items()
                  if name.startswith(component + '/Lain.') and name.endswith('.dll') and
                  '/' not in name[len(component) + 1:] and name != component + '/' + project + '.dll'}
        need(item['localSharedAssemblies'] == shared, 'Monitor shared assembly provenance differs: ' + component)


def prepare_return_source(common, previous_app, root):
    inventory = common.verify(previous_app)
    destination = root / 'verified-return-package'
    need(not destination.exists(), 'Fresh external return source required')
    shutil.copytree(previous_app, destination)
    need(common.verify(destination) == inventory, 'External app-only return source differs')
    return destination, inventory


def prepare(args, fresh=True):
    operator = load(ROOT / 'scripts/service-update.py', 'exact_full_operator')
    for path, expected in ((ROOT / 'scripts/service-update.py', args.operator_sha256),
                           (args.installer, args.installer_sha256), (args.core_helper, args.core_helper_sha256),
                           (args.metadata_reader, args.metadata_reader_sha256)):
        operator.pinned(path, expected)
    for path in (args.gateway_package, args.monitor_package, args.baseline_package, args.output, args.temporary_parent, args.dotnet):
        operator.safe(path)
    need((not fresh or not args.output.exists()) and args.output.parent.is_dir() and args.temporary_parent.is_dir(), 'Fresh output and existing parents required')
    for source in (args.gateway_package, args.monitor_package, args.baseline_package, ROOT):
        need(args.temporary_parent != source and source not in args.temporary_parent.parents, 'Temporary parent overlaps input/checkout')
        need(args.output != source and source not in args.output.parents and args.output not in source.parents,
             'Output overlaps package or checkout')
    common = load(args.core_helper, 'exact_full_core_helper')
    api = load(args.installer, 'exact_full_installer')
    actual = load(ROOT / 'tests/delivery-stabilization/actual_packages.py', 'exact_full_actual')
    v4 = load(ROOT / 'tests/delivery-stabilization/v4_update.py', 'exact_full_v4')
    packages = {}
    for name, mode, path, pin, core in (
        ('gateway', 'gateway', args.gateway_package, args.gateway_manifest_sha256, args.core_sha256),
        ('monitor', 'monitor', args.monitor_package, args.monitor_manifest_sha256, None),
        ('baseline', 'gateway', args.baseline_package, args.baseline_manifest_sha256, args.baseline_core_sha256)):
        packages[name] = actual.package_info(path, pin, mode, core)
        need(common.verify(path)['SHA256SUMS'] == pin, 'Complete package differs: ' + name)
    info = common.load(args.gateway_package / 'build-info.json')
    operator.verify_full_provenance(common, args.gateway_package, common.verify(args.gateway_package), info, args.source_commit)
    common.check_core(args.gateway_package)
    monitor = common.load(args.monitor_package / 'build-info.json')
    verify_monitor_provenance(common, args.monitor_package, common.verify(args.monitor_package), monitor, args.source_commit)
    # No Core-changed requirement and no invented single-source baseline: its
    # independently pinned complete component map is recorded exactly as shipped.
    return {'format': 1, 'kind': 'exact-full-package-runtime', 'sourceCommit': args.source_commit,
            'packages': packages, 'operatorSha256': args.operator_sha256, 'installerSha256': args.installer_sha256,
            'coreHelperSha256': args.core_helper_sha256, 'metadataReaderSha256': args.metadata_reader_sha256,
            'checks': [], 'scope': 'Synthetic exact package/file/Host/Core/SQLite/restore only; no systemd or own-installation acceptance'}, common, api, actual, v4


def execute(args, report, common, api, actual, v4):
    actual.require_loopback_namespace(args.parent_netns)
    need(Path(tempfile.gettempdir()).resolve() == args.temporary_parent.resolve(), 'Set TMPDIR to explicit temporary-parent before starting Python')
    os.umask(0o077)
    args.output.mkdir(mode=0o700)
    scratch = Path(tempfile.mkdtemp(prefix='exact-full-package-', dir=args.temporary_parent))
    report.update(status='running', planOnly=False, syntheticOnly=True, privateFixture=str(scratch))
    checks = report['checks']
    def checked(name, evidence):
        checks.append({'name': name, 'passed': True, 'evidence': evidence})
    def persist():
        temporary = args.output / 'report.new'
        temporary.write_text(json.dumps(report, indent=2) + '\n')
        temporary.replace(args.output / 'report.json')
    def exact_install(home, package, base_manifest, name, apply=True):
        backup = home / 'backups' / name
        backup.parent.mkdir(mode=0o700, exist_ok=True)
        options = SimpleNamespace(package=package, home=home, backup=backup,
            manifest_sha256=digest(package / 'SHA256SUMS'), baseline_manifest_sha256=base_manifest,
            helper=args.core_helper, helper_sha256=args.core_helper_sha256, stopped=True, apply=apply)
        with contextlib.redirect_stdout(io.StringIO()): api.install(options)
        return backup
    def assert_version(host, package):
        expected = common.load(package / 'build-info.json')['componentInformationalVersions']['host']
        need(host.api('/status')['version'] == expected, 'Running Host version differs')
    persist()
    try:
        for name, package in (('gateway', args.gateway_package), ('monitor', args.monitor_package)):
            command = [str(args.dotnet), str(args.metadata_reader)] + [label + '=' + str(package / label / ('Lain.' + project + '.dll'))
                for label, project in (('cli', 'Cli'), ('host', 'Host'), ('desktop', 'Desktop'))]
            result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45, cwd='/')
            (args.output / ('metadata-' + name + '.stderr')).write_bytes(result.stderr)
            need(result.returncode == 0, 'Actual PE reader failed: ' + name)
            metadata = json.loads(result.stdout)
            info = common.load(package / 'build-info.json')
            need(set(metadata) == {'cli', 'host', 'desktop'}, 'Missing actual PE metadata components')
            for label, project in (('cli', 'Cli'), ('host', 'Host'), ('desktop', 'Desktop')):
                need(metadata[label] == {'method': 'assembly-metadata-reader', 'attribute': 'System.Reflection.AssemblyInformationalVersionAttribute',
                     'dllSha256': digest(package / label / ('Lain.' + project + '.dll')),
                     'informationalVersion': info['componentInformationalVersions'][label], 'passed': True}, 'Actual PE metadata differs: ' + label)
            (args.output / ('metadata-' + name + '.json')).write_bytes(result.stdout)
            checked('actual-pe-metadata-' + name, 'metadata-' + name + '.json')
        for mode, package in (('monitor', args.monitor_package), ('gateway', args.gateway_package)):
            root = scratch / ('fresh-' + mode)
            home = root / 'state'
            actual.install(package, root, home)
            need((home / 'initial-runtime-mode').read_text().strip() == mode, 'Fresh saved mode differs')
            for _ in range(2):
                with actual.Host(root, home) as host:
                    assert_version(host, package)
                    status = host.api('/status')
                    need(status['runtimeMode'] == mode, 'Fresh Host intent differs')
                    if mode == 'monitor':
                        need(status['proxyReady'] is False and not (home / 'app/proxy').exists(), 'Monitor started/contains Core')
            checked('fresh-' + mode + '-install-start-restart', 'Exact installed app, empty fresh saved intent and actual Host versions checked twice; no Apply/provider requests')
            shutil.rmtree(root)
        root, home = scratch / 'upgrade', scratch / 'upgrade/state'
        actual.install(args.baseline_package, root, home)
        host = actual.Host(root, home)
        v4.seed(home, 'gateway', host)
        with host:
            assert_version(host, args.baseline_package)
            v4.assert_live(host, common.load(args.baseline_package / 'build-info.json')['componentInformationalVersions']['host'], args.baseline_core_sha256, 'gateway')
        preserved, history = v4.fingerprints(home), v4.database(home)
        maintenance = load(args.gateway_package / 'packaging/lain-maintenance.py', 'exact_full_maintenance')
        snapshot = root / 'stopped-snapshot'
        maintenance.backup(home, snapshot)
        need(v4.fingerprints(snapshot / 'state') == preserved, 'Stopped full snapshot omitted protected state')
        snapshot_inventory = maintenance.inventory(snapshot)
        checked('stopped-full-snapshot', 'Real maintenance backup; entire synthetic home retained, protected-state fingerprint equals baseline')
        original = common.inventory(home)
        exact_install(home, args.gateway_package, args.baseline_manifest_sha256, 'plan', apply=False)
        need(common.inventory(home) == original, 'Full installer plan changed synthetic state')
        old_app = exact_install(home, args.gateway_package, args.baseline_manifest_sha256, 'forward') / 'app'
        need(common.verify(home / 'app') == common.verify(args.gateway_package), 'Actual full target differs')
        need(v4.fingerprints(home) == preserved and v4.database(home) == history, 'Full installer changed state/history')
        with host:
            assert_version(host, args.gateway_package)
            v4.assert_live(host, args.source_commit, args.core_sha256, 'gateway')
        need(v4.fingerprints(home) == preserved and v4.database(home) == history, 'New Host changed protected state/history')
        checked('baseline-to-full-update', 'Actual app-only atomic installer and new Host/Core; desired19/applied2, synthetic identity/grants and schema4/history retained')
        actual.write(home, 'proxy/auth-owned/synthetic-owned.json', {'type': 'codex', 'account_id': 'fixture-subject',
                     'access_token': 'synthetic-current-access', 'refresh_token': 'synthetic-current-refresh', 'expired': '2099-01-01T00:00:00Z'})
        latest = v4.fingerprints(home)
        return_source, old_app_inventory = prepare_return_source(common, old_app, root)
        exact_install(home, return_source, args.gateway_manifest_sha256, 'return')
        with host:
            assert_version(host, args.baseline_package)
            v4.assert_live(host, common.load(args.baseline_package / 'build-info.json')['componentInformationalVersions']['host'], args.baseline_core_sha256, 'gateway')
        need(v4.fingerprints(home) == latest and v4.database(home) == history, 'App-only return changed latest grants/history')
        exact_install(home, args.gateway_package, args.baseline_manifest_sha256, 'forward-again')
        with host:
            assert_version(host, args.gateway_package)
            v4.assert_live(host, args.source_commit, args.core_sha256, 'gateway')
        need(v4.fingerprints(home) == latest and v4.database(home) == history, 'Forward return changed latest state/history')
        need(common.verify(old_app) == old_app_inventory and common.verify(return_source) == old_app_inventory,
             'Original previous-app backup or external return source changed')
        checked('bounded-app-only-return-forward', 'Actual previous/new applications; latest synthetic grants retained, no snapshot grant restoration')
        recovery = root / 'quarantined-recovery'
        maintenance.restore(snapshot, recovery)
        need((recovery / 'restore-requires-reconnect').is_file() and v4.fingerprints(recovery) == preserved,
             'Fresh full recovery lost state or quarantine')
        need(v4.fingerprints(home) == latest and v4.database(home) == history, 'Recovery changed current/latest home or history')
        need(maintenance.inventory(snapshot) == snapshot_inventory, 'Stopped snapshot changed')
        checked('quarantined-full-recovery', 'Real helper restores into a fresh home with reconnect marker; current/latest home untouched')
        fault_home = scratch / 'fault/state'
        actual.install(args.baseline_package, scratch / 'fault', fault_home)
        original_fault = common.inventory(fault_home)
        baseline = common.verify(fault_home / 'app')
        injected = False
        real_write = common.write
        def fault(path, value):
            nonlocal injected
            if value.get('status') == 'app-exchanged' and not injected:
                injected = True
                raise OSError('intentional exact full package journal writefault after atomic exchange')
            return real_write(path, value)
        # The sole semantic injection is this writefault. Reuse the already
        # hash-checked real helper so its journal writer can be faulted; all
        # process/lease/copy/fsync/exchange/verify/reversal logic remains real.
        with patch.object(api, 'load_helper', return_value=common), patch.object(common, 'write', side_effect=fault):
            try: exact_install(fault_home, args.gateway_package, args.baseline_manifest_sha256, 'fault')
            except OSError as error:
                need('intentional exact full package' in str(error), 'Unrelated error mistaken for injected fault')
            else: raise RuntimeError('Injected writefault did not propagate')
        need(injected and common.verify(fault_home / 'app') == baseline, 'Exact full file reversal differs')
        for name, value in original_fault.items():
            if not name.startswith('app/'):
                need(common.inventory(fault_home).get(name) == value, 'Writefault changed non-app file')
        checked('exact-full-writefault-reversal', 'One post-exchange journal-write fault; real directory reverse and baseline manifest/non-app files preserved')
        original_fault = common.inventory(fault_home)
        process = subprocess.Popen([sys.executable, '-c', 'import time; print("ready",flush=True); time.sleep(45)',
                                   str(fault_home / 'app/desktop/Lain.Desktop.dll')],
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, cwd='/')
        try:
            with selectors.DefaultSelector() as selector:
                selector.register(process.stdout, selectors.EVENT_READ)
                need(selector.select(timeout=5) and process.stdout.readline().strip() == b'ready', 'Own argv guard fixture did not start')
            try: exact_install(fault_home, args.gateway_package, args.baseline_manifest_sha256, 'process-refused')
            except RuntimeError as error:
                need('application process remains' in str(error), 'Unrelated error mistaken for argv guard')
            else: raise RuntimeError('Installed-app argv candidate accepted')
            need(common.inventory(fault_home) == original_fault, 'Process refusal changed fixture')
        finally:
            process.terminate()
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait(timeout=5)
            process.stdout.close()
        checked('actual-app-argv-process-refusal', 'Real owned Python child references installed app DLL; real scanner refuses before staging; no product process or systemd')
        tampered = fault_home / 'app/host/Lain.Host.dll'
        tampered.write_bytes(tampered.read_bytes() + b'isolated baseline tamper')
        tamper_inventory = common.inventory(fault_home)
        try: exact_install(fault_home, args.gateway_package, args.baseline_manifest_sha256, 'tamper-refused')
        except RuntimeError as error:
            need('inventory differs' in str(error), 'Unrelated error mistaken for baseline refusal')
        else: raise RuntimeError('Changed installed baseline accepted')
        need(common.inventory(fault_home) == tamper_inventory, 'Rejected baseline changed fixture')
        checked('changed-installed-baseline-no-writes', 'Exact staged installer rejects changed installed DLL before copy/exchange; full tampered fixture inventory unchanged')
        need(prepare(args, fresh=False)[0]['packages'] == report['packages'], 'Pinned input packages changed during qualification')
        report.update(status='passed', privateFixtureRetained=False)
        persist()
        shutil.rmtree(scratch)
    except BaseException as error:
        report.update(status='failed', errorType=type(error).__name__, errorMessage=str(error), privateFixtureRetained=True)
        persist()
        raise


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ('gateway-package', 'monitor-package', 'baseline-package', 'installer', 'core-helper', 'metadata-reader', 'dotnet', 'output', 'temporary-parent'):
        p.add_argument('--' + name, type=Path, required=True)
    for name in ('gateway-manifest-sha256', 'monitor-manifest-sha256', 'baseline-manifest-sha256', 'core-sha256', 'baseline-core-sha256',
                 'operator-sha256', 'installer-sha256', 'core-helper-sha256', 'metadata-reader-sha256', 'source-commit'):
        p.add_argument('--' + name, required=True)
    p.add_argument('--parent-netns')
    p.add_argument('--execute', action='store_true')
    args = p.parse_args()
    report, common, api, actual, v4 = prepare(args)
    if not args.execute:
        print(json.dumps(dict(report, status='planned', planOnly=True, mutations=False), indent=2))
        return
    execute(args, report, common, api, actual, v4)


if __name__ == '__main__': main()
