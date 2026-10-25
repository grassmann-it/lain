#!/usr/bin/env python3
"""File/schema fixtures only; fake binary/evidence bytes never execute product code."""
import contextlib
import copy
import importlib.util
import io
from pathlib import Path
import shutil
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    exec(compile(path.read_bytes(), str(path), 'exec'), result.__dict__)
    return result


fixtures = load(ROOT / 'scripts/test-component-delivery.py', 'full_input_file_fixtures')
harness = load(Path(__file__).with_name('full_packages.py'), 'full_input_harness')


class FullInputFixtures(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.PortablePlanFixtures('test_plan_accepts_explicit_nonhistorical_counts_and_writes_nothing')
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.fixture.full_package()
        self.f, self.common = self.fixture.f, fixtures.common
        self.monitor = self.f.root / 'monitor-package'
        shutil.copytree(self.f.out, self.monitor)
        for name in ('proxy', 'core-source-provenance', 'notices/go'):
            shutil.rmtree(self.monitor / name)
        info = self.common.load(self.monitor / 'build-info.json')
        info['runtimeMode'] = 'monitor'
        self.common.write(self.monitor / 'build-info.json', info)
        self.common.write(self.monitor / 'package-metadata.json', {'runtimeMode': 'monitor'})
        self.common.manifest(self.monitor)
        for package in (self.f.base, self.f.out):
            # These are schema fixtures, not real qualification reports.
            shutil.copyfile(self.f.out / 'proxy/lain-proxy', package / 'proxy/lain-proxy') if package != self.f.out else None
            (package / 'proxy/lain-proxy').chmod(0o700)
            core = fixtures.api.sha(package / 'proxy/lain-proxy')
            self.common.write(package / 'proxy/binary-manifest.json', {'sha256': core})
            self.common.write(package / 'proxy/qualification.json', {'passed': True, 'sha256': core})
            self.common.write(package / 'gate-status.json', {'backendQualified': True, 'releaseQualified': False})
            self.common.manifest(package)
        reader = self.f.root / 'unexecuted-reader.dll'
        reader.write_bytes(b'file fixture; never executed')
        self.args = SimpleNamespace(gateway_package=self.f.out, monitor_package=self.monitor, baseline_package=self.f.base,
            gateway_manifest_sha256=fixtures.api.sha(self.f.out / 'SHA256SUMS'),
            monitor_manifest_sha256=fixtures.api.sha(self.monitor / 'SHA256SUMS'),
            baseline_manifest_sha256=fixtures.api.sha(self.f.base / 'SHA256SUMS'),
            core_sha256=fixtures.api.sha(self.f.out / 'proxy/lain-proxy'), baseline_core_sha256=fixtures.api.sha(self.f.base / 'proxy/lain-proxy'),
            installer=ROOT / 'scripts/package-refresh.py', installer_sha256=fixtures.api.sha(ROOT / 'scripts/package-refresh.py'),
            core_helper=ROOT / 'scripts/core-refresh.py', core_helper_sha256=fixtures.api.sha(ROOT / 'scripts/core-refresh.py'),
            operator_sha256=fixtures.api.sha(ROOT / 'scripts/service-update.py'), metadata_reader=reader,
            metadata_reader_sha256=fixtures.api.sha(reader), dotnet=Path(sys.executable).resolve(),
            temporary_parent=self.f.root / 'temporary', output=self.f.root / 'output', source_commit=self.f.commit)
        self.args.temporary_parent.mkdir()

    def prepare(self, **kwargs):
        original = harness.load
        def imported(path, name):
            return self.common if name == 'exact_full_core_helper' else original(path, name)
        # Core semantic qualification is separate; all package manifests and
        # application provenance guards remain real in these file fixtures.
        with patch.object(harness, 'load', side_effect=imported), patch.object(self.common, 'check_core'), \
             patch('subprocess.run', side_effect=AssertionError('No subprocess in plan')):
            return harness.prepare(self.args, **kwargs)

    def test_same_core_mixed_baseline_plan_writes_nothing(self):
        before = self.common.inventory(self.f.root)
        report = self.prepare()[0]
        self.assertEqual(self.args.core_sha256, self.args.baseline_core_sha256)
        self.assertEqual(report['checks'], [])
        self.assertEqual(self.common.inventory(self.f.root), before)
        self.assertFalse(self.args.output.exists())

    def test_reverify_after_output_creation_is_explicit(self):
        self.args.output.mkdir()
        with self.assertRaisesRegex(RuntimeError, 'Fresh output'): self.prepare()
        self.prepare(fresh=False)

    def test_return_source_is_outside_home_and_preserves_original(self):
        home = self.f.root / 'return-state'
        previous = home / 'backups/previous/app'
        previous.parent.mkdir(parents=True)
        shutil.copytree(self.f.base, previous)
        before = self.common.verify(previous)
        destination, inventory = harness.prepare_return_source(self.common, previous, self.f.root)
        self.assertNotIn(home, destination.parents)
        self.assertEqual(inventory, before)
        self.assertEqual(self.common.verify(destination), before)
        self.assertEqual(self.common.verify(previous), before)

    def test_old_monitor_component_hidden_by_new_global_commit_refused(self):
        info = self.common.load(self.monitor / 'build-info.json')
        info['componentSourceCommits']['desktop'] = 'b' * 40
        self.common.write(self.monitor / 'build-info.json', info)
        self.common.manifest(self.monitor)
        self.args.monitor_manifest_sha256 = fixtures.api.sha(self.monitor / 'SHA256SUMS')
        with self.assertRaisesRegex(RuntimeError, 'Monitor component source'):
            self.prepare()
        self.assertFalse(self.args.output.exists())

    def test_tampered_package_refused_before_any_runtime(self):
        (self.f.out / 'host/Lain.Host.dll').write_bytes(b'changed bytes')
        with self.assertRaisesRegex(Exception, 'manifest|contents|inventory'):
            self.prepare()
        self.assertFalse(self.args.output.exists())


if __name__ == '__main__': unittest.main()
