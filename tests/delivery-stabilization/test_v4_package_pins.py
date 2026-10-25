#!/usr/bin/env python3
"""Filesystem-only package-pin checks; never execute the synthetic payloads."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('v4_package_pins', HERE / 'v4_update.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
PREVIOUS = 'd7200d7d700bd2d23abf8bf028d40526eb235bf7'
NEW = 'e48bc118a5ecd2ada80648370ed2b8ef102fd01e'
HISTORICAL = '793822c17a9bfa6a950bf82c8d2cdb6afa648d7c'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(package, name, value):
    path = package / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value) if isinstance(value, dict) else value)


def seal(package):
    manifest = package / 'SHA256SUMS'
    manifest.write_text(''.join(sha(path) + '  ' + path.relative_to(package).as_posix() + '\n'
                              for path in sorted(package.rglob('*'))
                              if path.is_file() and path != manifest))
    return sha(manifest)


def synthetic_package(package, mode, source, core_label):
    components = {}
    for component, project in (('cli', 'Lain.Cli'), ('host', 'Lain.Host'), ('desktop', 'Lain.Desktop')):
        # No valid executable code, although executable permission is required by
        # package inspection. Tests call package_plan only, never run/install.
        write(package, component + '/' + project, 'NONEXECUTABLE SYNTHETIC PAYLOAD ' + source)
        (package / component / project).chmod(0o700)
        components[component] = {'sourceCommit': source, 'assemblySha256': sha(package / component / project)}
    write(package, 'build-info.json', {'commit': source, 'components': components})
    write(package, 'package-metadata.json', {'format': 1, 'runtimeMode': mode, 'sourceCommit': source})
    if mode == 'gateway':
        write(package, 'proxy/lain-proxy', 'NONEXECUTABLE SYNTHETIC CORE ' + core_label)
        (package / 'proxy/lain-proxy').chmod(0o700)
        core = sha(package / 'proxy/lain-proxy')
        source_hash = hashlib.sha256(core_label.encode()).hexdigest()
        write(package, 'proxy/binary-manifest.json', {'sha256': core, 'sourceManifestSha256': source_hash,
              'upstreamCommit': 'a' * 40, 'patchSetSha256': 'b' * 64})
        write(package, 'proxy/qualification.json', {'passed': True, 'sha256': core,
              'sourceManifestSha256': source_hash, 'sourceCommit': 'c' * 40})
        write(package, 'gate-status.json', {'backendQualified': True, 'releaseQualified': False})
    return seal(package)


class PackagePins(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='lain-v4-pins-')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.args = argparse.Namespace(previous_source_commit=PREVIOUS, new_source_commit=NEW)
        for prefix, source in (('previous', PREVIOUS), ('new', NEW)):
            for mode in ('monitor', 'gateway'):
                path = self.root / (prefix + '-' + mode)
                pin = synthetic_package(path, mode, source, prefix)
                setattr(self.args, prefix + '_' + mode + '_package', path)
                setattr(self.args, prefix + '_' + mode + '_manifest_sha256', pin)
                if mode == 'gateway':
                    setattr(self.args, prefix + '_core_sha256', sha(path / 'proxy/lain-proxy'))

    def reseal(self, prefix, mode):
        path = getattr(self.args, prefix + '_' + mode + '_package')
        setattr(self.args, prefix + '_' + mode + '_manifest_sha256', seal(path))

    def reject(self):
        with self.assertRaises((RuntimeError, fixture.upgrade.GuardError)):
            fixture.package_plan(self.args)

    def test_explicit_current_baseline_and_different_core_are_bound(self):
        plan = fixture.package_plan(self.args)
        self.assertEqual(plan['sourceCommits'], {'previous': PREVIOUS, 'new': NEW})
        self.assertFalse(plan['sameQualifiedCore'])
        self.assertEqual(plan['packages']['previousGateway']['manifestSha256'], self.args.previous_gateway_manifest_sha256)
        self.assertNotEqual(self.args.previous_gateway_manifest_sha256, self.args.new_gateway_manifest_sha256)

    def test_reject_invalid_full_source_pins(self):
        for field in ('previous_source_commit', 'new_source_commit'):
            original = getattr(self.args, field)
            for value in ('', 'd7200d7', 'A' * 40, 'g' * 40, '0' * 39, '0' * 41):
                with self.subTest(field=field, value=value):
                    setattr(self.args, field, value)
                    self.reject()
            setattr(self.args, field, original)

    def test_reject_same_source(self):
        self.args.new_source_commit = PREVIOUS
        self.reject()

    def test_reject_implicit_historical_baseline(self):
        self.args.previous_source_commit = HISTORICAL
        self.reject()

    def test_reject_swapped_previous_manifest_pin(self):
        self.args.previous_gateway_manifest_sha256 = self.args.new_gateway_manifest_sha256
        self.reject()

    def test_reject_different_core_hash(self):
        self.args.previous_core_sha256 = self.args.new_core_sha256
        self.reject()

    def test_reject_changed_payload_without_resealing(self):
        write(self.args.previous_monitor_package, 'build-info.json', {'commit': NEW})
        self.reject()

    def test_reject_resealed_wrong_build_source(self):
        path = self.args.previous_gateway_package / 'build-info.json'
        build = json.loads(path.read_text()); build['commit'] = HISTORICAL
        path.write_text(json.dumps(build)); self.reseal('previous', 'gateway')
        self.reject()

    def test_reject_resealed_wrong_metadata_source(self):
        path = self.args.previous_monitor_package / 'package-metadata.json'
        metadata = json.loads(path.read_text()); metadata['sourceCommit'] = HISTORICAL
        path.write_text(json.dumps(metadata)); self.reseal('previous', 'monitor')
        self.reject()

    def test_reject_resealed_wrong_component_source(self):
        path = self.args.previous_monitor_package / 'build-info.json'
        build = json.loads(path.read_text()); build['components']['host']['sourceCommit'] = HISTORICAL
        path.write_text(json.dumps(build)); self.reseal('previous', 'monitor')
        self.reject()

    def test_reject_cross_mode_component_difference(self):
        path = self.args.previous_monitor_package / 'build-info.json'
        build = json.loads(path.read_text()); build['components']['host']['assemblySha256'] = 'f' * 64
        path.write_text(json.dumps(build)); self.reseal('previous', 'monitor')
        self.reject()

    def reuse_previous_core(self):
        old, new = self.args.previous_gateway_package, self.args.new_gateway_package
        for path in (old / 'proxy').iterdir():
            (new / 'proxy' / path.name).write_bytes(path.read_bytes())
        self.args.new_core_sha256 = self.args.previous_core_sha256
        self.reseal('new', 'gateway')

    def test_allow_exact_qualified_core_reuse(self):
        self.reuse_previous_core()
        self.assertTrue(fixture.package_plan(self.args)['sameQualifiedCore'])

    def test_reject_same_binary_with_changed_qualification(self):
        self.reuse_previous_core()
        path = self.args.new_gateway_package / 'proxy/qualification.json'
        report = json.loads(path.read_text()); report['sourceCommit'] = 'd' * 40
        path.write_text(json.dumps(report)); self.reseal('new', 'gateway')
        self.reject()

    def test_reject_same_binary_with_changed_source_manifest_identity(self):
        self.reuse_previous_core()
        for name in ('binary-manifest.json', 'qualification.json'):
            path = self.args.new_gateway_package / 'proxy' / name
            report = json.loads(path.read_text()); report['sourceManifestSha256'] = 'e' * 64
            path.write_text(json.dumps(report))
        self.reseal('new', 'gateway'); self.reject()

    def test_cli_requires_explicit_previous_source_without_writing_report(self):
        command = [sys.executable, str(HERE / 'v4_update.py')]
        for name, value in vars(self.args).items():
            if name != 'previous_source_commit':
                command.extend(['--' + name.replace('_', '-'), str(value)])
        report = self.root / 'not-created.json'
        result = subprocess.run([*command, '--report', str(report)], capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 2)
        self.assertIn('--previous-source-commit', result.stderr)
        self.assertFalse(report.exists())

    def test_cli_plan_binds_both_sources_without_execution(self):
        command = [sys.executable, str(HERE / 'v4_update.py')]
        for name, value in vars(self.args).items():
            command.extend(['--' + name.replace('_', '-'), str(value)])
        report = self.root / 'plan.json'
        result = subprocess.run([*command, '--report', str(report)], capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        plan = json.loads(report.read_text())
        self.assertEqual(plan['sourceCommits'], {'previous': PREVIOUS, 'new': NEW})
        self.assertEqual(plan['status'], 'not_run')
        self.assertTrue(plan['planOnly'])
        self.assertEqual(plan['checks'], [])


if __name__ == '__main__':
    unittest.main()
