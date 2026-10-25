#!/usr/bin/env python3
"""Disposable files and a temporary Git repository; no build/Core/service calls."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('qualification_wrapper', HERE / 'qualification_wrapper.py')
wrapper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(wrapper)


class QualificationWrapper(unittest.TestCase):
    def write(self, name, value):
        path = self.root / name; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value) if isinstance(value, dict) else value)
        return path

    def repin(self, name):
        path = getattr(self.args, name)
        setattr(self.args, name + '_sha256', wrapper.digest(path))

    def change(self, name, mutate):
        path = getattr(self.args, name)
        data = json.loads(path.read_text()); mutate(data); path.write_text(json.dumps(data)); self.repin(name)

    def git(self, *args):
        result = subprocess.run(['git', '-C', str(self.repository), '-c', 'user.name=Synthetic Fixture',
                                 '-c', 'user.email=fixture@example.invalid', *args], capture_output=True, check=True)
        return result.stdout.decode().strip()

    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix='lain-wrapper-fixture-')
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repository = self.root / 'repo'; self.repository.mkdir()
        toolchain = {'goVersion': 'go1.27.2'}
        tool = self.write('repo/proxy/toolchain.json', toolchain)
        for name in (*wrapper.FIXED_BUILD_INPUTS, 'proxy/adapter/synthetic.go', 'proxy/patches/synthetic.patch'):
            if name != 'proxy/toolchain.json':
                self.write('repo/' + name, 'SYNTHETIC SOURCE INPUT ' + name)
        source = {'schemaVersion': 2, 'snapshotVersion': '8.0.41', 'lainPatchVersion': 3,
                  'upstreamSourceModified': True, 'patchSetSha256': 'b' * 64,
                  'upstreamDependency': {'commit': 'c' * 40, 'version': 'v8.0.41', 'archiveSha256': 'd' * 64},
                  'buildInputs': {name: wrapper.digest(self.repository / name) for name in
                                  (*wrapper.FIXED_BUILD_INPUTS, 'proxy/adapter/synthetic.go', 'proxy/patches/synthetic.patch')}}
        source_path = self.write('repo/proxy/source-manifest.json', source)
        self.git('init', '-q'); self.git('add', '.'); self.git('commit', '-qm', 'Synthetic Go source')
        go_source = self.git('rev-parse', 'HEAD')
        self.write('repo/app-note.txt', 'Synthetic later application source')
        self.git('add', '.'); self.git('commit', '-qm', 'Synthetic application source')
        final_source = self.git('rev-parse', 'HEAD')
        core = self.write('core/lain-proxy', 'NONEXECUTABLE SYNTHETIC CORE')
        core_hash, source_hash = wrapper.digest(core), wrapper.digest(source_path)
        build = {'schemaVersion': 2, 'binary': 'lain-proxy', 'binarySha256': core_hash,
                 'sourceManifestSha256': source_hash, 'snapshotVersion': '8.0.41', 'lainPatchVersion': 3,
                 'upstreamCommit': 'c' * 40, 'upstreamVersion': 'v8.0.41', 'upstreamArchiveSha256': 'd' * 64,
                 'patchSetSha256': 'b' * 64, 'upstreamSourceModified': True,
                 'goVersion': 'go version go1.27.2 linux/amd64'}
        prepared = {'schemaVersion': 1, 'sourceManifestSha256': source_hash,
                    'upstreamCommit': 'c' * 40, 'upstreamVersion': 'v8.0.41', 'upstreamArchiveSha256': 'd' * 64,
                    'patchSetSha256': 'b' * 64, 'files': {'adapter/synthetic.go': 'e' * 64}}
        self.args = argparse.Namespace(repository=self.repository, source_commit=final_source,
                                       go_unit_source_commit=go_source, core=core, core_sha256=core_hash,
                                       source_manifest=source_path, source_manifest_sha256=source_hash)
        for name, data in (('build_manifest', build), ('prepared_manifest', prepared)):
            setattr(self.args, name, self.write(name + '.json', data)); self.repin(name)

        def base(**extra):
            return {'passed': True, 'syntheticOnly': True, 'coreSha256': core_hash, **extra}

        upgrade = base(checks=[])
        for name in wrapper.UPGRADE:
            if name == 'consumer-replay-10':
                child = base(replays=10, storeReopenedEachReplay=True)
                check = {'name': name, 'passed': True, 'report': name + '.json'}
            else:
                key = 'case' if name == 'inspector' else 'name'
                child = base(checks=[{key: case, 'passed': True} for case in wrapper.UPGRADE_CASES[name]])
                check = {'name': name, 'passed': True, 'report': name + '.json',
                         'binaryHashMatches': True, 'cases': len(child['checks'])}
            check['report'] = wrapper.UPGRADE_FILES[name]
            self.write('upgrade/' + wrapper.UPGRADE_FILES[name], child)
            upgrade['checks'].append(check)
        process = base(binaryUnchanged=True, actualCoreProcesses=True, sourceManifestSha256=source_hash,
                       networkIsolation='private_network_namespace_loopback_only',
                       checks=[{'name': name, 'passed': True} for name in wrapper.PROCESS])
        providers = base(binaryUnchanged=True, checks=[{'provider': provider, 'case': case, 'passed': True}
                         for provider in ('cohere', 'kilo') for case in
                         ('json', 'stream', 'tool', 'status_401', 'status_403', 'status_429', 'status_404', 'cancel', 'truncated')])
        providers['checks'].append({'provider': 'kilo', 'case': 'status_402', 'passed': True})
        providers['checks'] += [{'provider': 'cohere', 'case': case, 'passed': True} for case in
                               ('responses_json', 'responses_stream', 'responses_tool', 'responses_store_true',
                                'responses_previous_response_id', 'responses_native_search',
                                'responses_additional_native_search', 'responses_native_file_search')]
        context = base(binaryUnchanged=True, checks=[{'protocol': protocol, 'stream': stream, 'status': status,
                       'contextCodePreserved': True, 'completeConversationAndTools': True, 'upstreamRequests': 1}
                       for protocol in ('chat', 'responses') for stream in (False, True) for status in (400, 413, 422)])
        context['checks'] += [{'case': name, 'passed': True} for name in ('oversize', 'unrelated-small')]
        archive = base(sourceManifestSha256=source_hash, binarySha256=core_hash,
                       result={'checks': list(wrapper.ARCHIVE), 'realHostCollectorAndSqliteTested': True})
        del archive['coreSha256']
        events = [{'Action': 'pass', 'Package': package} for package in sorted(wrapper.GO_PACKAGES)]
        events.append({'Action': 'pass', 'Package': 'lain.local/proxy/internal/lain', 'Test': 'SyntheticGuard'})
        log = self.write('unit/go-test.jsonl', '\n'.join(json.dumps(e) for e in events) + '\n')
        python_results = []
        for name, (directory, pattern, module, count) in wrapper.PYTHON_GATES.items():
            text = f'test_synthetic ({module}.Synthetic.test_synthetic) ... ok\n\nRan {count} tests in 0.001s\n\nOK\n'
            self.write('unit/' + name, text)
            python_results.append({'command': wrapper.python_command(directory, pattern), 'exitCode': 0, 'summary': text[-500:]})
        unit = {'passed': True, 'sourceCommit': go_source, 'sourceManifestSha256': source_hash,
                'preparedManifestSha256': self.args.prepared_manifest_sha256, 'goExitCode': 0, 'goVersion': 'go1.27.2',
                'goCommand': ['/synthetic/go', 'test', '-json', '-mod=readonly', '-p=1', './internal/...', './cmd/lain-proxy', '-count=1', '-timeout=90s'],
                'summary': {'failures': [], 'testPasses': 1, 'topLevelPasses': 1, 'packages': events[:3]},
                'pythonResults': python_results, 'qualification': 'Synthetic filesystem unit fixture',
                'rawFiles': {name: wrapper.digest(self.root / 'unit' / name)
                             for name in ('go-test.jsonl', 'upgrade-tests.txt', 'legacy-tests.txt')}}
        for name, data in (('upgrade', upgrade), ('process', process), ('providers', providers),
                           ('context', context), ('archive', archive), ('go-unit', unit)):
            attr = name.replace('-', '_') + '_report'
            path = self.write('upgrade/qualification.json' if name == 'upgrade' else
                              'unit/unit.json' if name == 'go-unit' else name + '.json', data)
            setattr(self.args, attr, path); self.repin(attr)

    def refuse(self):
        with self.assertRaises((ValueError, OSError, KeyError, TypeError)):
            wrapper.seal(self.args)

    def test_complete_evidence_with_distinct_git_sources_same_manifest(self):
        result = wrapper.seal(self.args)
        self.assertIs(result['passed'], True)
        self.assertEqual(result['sha256'], self.args.core_sha256)
        self.assertNotEqual(result['sourceCommit'], result['goUnitSourceCommit'])
        self.assertEqual(len(result['rawReports']), 17)

    def test_false_or_nonboolean_passed_rejected_in_every_top_report(self):
        for name in wrapper.REPORTS:
            attr = name.replace('-', '_') + '_report'; path = getattr(self.args, attr); original = path.read_bytes()
            for value in (False, 1, 'true', None):
                with self.subTest(report=name, passed=value):
                    path.write_bytes(original); self.change(attr, lambda j: j.update(passed=value)); self.refuse()
            path.write_bytes(original); self.repin(attr)

    def test_missing_report_or_wrong_reviewed_hash(self):
        path = self.args.context_report; content = path.read_bytes(); path.unlink(); self.refuse()
        path.write_bytes(content); self.args.context_report_sha256 = '0' * 64; self.refuse()

    def test_wrong_core_build_and_source_pins(self):
        for field in ('core_sha256', 'build_manifest_sha256', 'source_manifest_sha256', 'prepared_manifest_sha256'):
            original = getattr(self.args, field); setattr(self.args, field, '0' * 64)
            with self.subTest(field=field): self.refuse()
            setattr(self.args, field, original)

    def test_report_binary_or_source_disagrees_despite_resealed_report(self):
        self.change('process_report', lambda j: j.update(sourceManifestSha256='0' * 64)); self.refuse()

    def test_raw_report_from_another_core_refused(self):
        self.change('providers_report', lambda j: j.update(coreSha256='0' * 64)); self.refuse()

    def test_legacy_nineteen_provider_cases_are_no_longer_sufficient(self):
        self.change('providers_report', lambda j: j.update(checks=[c for c in j['checks'] if not c['case'].startswith('responses_')]))
        self.refuse()

    def test_each_new_cohere_responses_case_is_mandatory(self):
        path = self.args.providers_report; original = path.read_bytes()
        for case in wrapper.COHERE_RESPONSES:
            with self.subTest(case=case):
                path.write_bytes(original)
                self.change('providers_report', lambda j: j.update(checks=[c for c in j['checks'] if c['case'] != case]))
                self.refuse()

    def test_build_provenance_disagrees_despite_resealed_manifest(self):
        self.change('build_manifest', lambda j: j.update(patchSetSha256='0' * 64)); self.refuse()

    def test_go_source_commit_cannot_be_relabelled(self):
        self.args.go_unit_source_commit = self.args.source_commit; self.refuse()

    def test_git_commit_must_contain_same_manifest(self):
        path = self.repository / 'proxy/source-manifest.json'; original = path.read_bytes()
        path.write_bytes(original + b'\n'); self.git('add', '.'); self.git('commit', '-qm', 'Different source manifest')
        self.args.source_commit = self.git('rev-parse', 'HEAD'); self.refuse()

    def test_stale_manifest_cannot_hide_changed_committed_adapter(self):
        self.write('repo/proxy/adapter/synthetic.go', 'CHANGED INPUT WITH STALE MANIFEST')
        self.git('add', '.'); self.git('commit', '-qm', 'Changed adapter with stale manifest')
        self.args.source_commit = self.git('rev-parse', 'HEAD'); self.refuse()

    def test_stale_manifest_cannot_hide_new_committed_adapter_file(self):
        self.write('repo/proxy/adapter/new.go', 'NEW INPUT ABSENT FROM MANIFEST')
        self.git('add', '.'); self.git('commit', '-qm', 'Additional adapter with stale manifest')
        self.args.source_commit = self.git('rev-parse', 'HEAD'); self.refuse()

    def test_stale_manifest_cannot_hide_removed_input_at_go_commit(self):
        self.git('rm', 'proxy/adapter/synthetic.go'); self.git('commit', '-qm', 'Removed adapter with stale manifest')
        self.args.go_unit_source_commit = self.git('rev-parse', 'HEAD')
        self.change('go_unit_report', lambda j: j.update(sourceCommit=self.args.go_unit_source_commit)); self.refuse()

    def test_all_eight_upgrade_checks_required_without_duplicates(self):
        self.change('upgrade_report', lambda j: j['checks'].pop()); self.refuse()

    def test_duplicate_upgrade_summary_check_refused(self):
        self.change('upgrade_report', lambda j: j['checks'].__setitem__(0, dict(j['checks'][1]))); self.refuse()

    def test_inner_upgrade_failure_cannot_be_hidden_by_outer_success(self):
        path = self.args.upgrade_report.parent / 'protocol-durability.json'
        child = json.loads(path.read_text()); child['checks'][0]['passed'] = False
        path.write_text(json.dumps(child)); self.refuse()

    def test_same_count_other_gate_cannot_replace_version_transition(self):
        self.change('upgrade_report', lambda j: next(c for c in j['checks'] if c['name'] == 'version-transition').update(report='independent-auth-crash.json'))
        self.refuse()

    def test_substituted_child_cannot_hide_behind_correct_filename(self):
        path = self.args.upgrade_report.parent
        (path / 'version-transition.json').write_bytes((path / 'independent-auth-crash.json').read_bytes())
        self.refuse()

    def test_inspector_case_identities_are_mandatory(self):
        path = self.args.upgrade_report.parent / 'inspector.json'
        data = json.loads(path.read_text()); data['checks'][0]['case'] = 'different-case'
        path.write_text(json.dumps(data)); self.refuse()

    def test_missing_inner_report_and_traversal_refused(self):
        path = self.args.upgrade_report.parent / 'inspector.json'; data = path.read_bytes(); path.unlink(); self.refuse()
        path.write_bytes(data)
        self.change('upgrade_report', lambda j: j['checks'][0].update(report='../other.json')); self.refuse()

    def test_go_inner_failure_and_raw_log_failure_refused(self):
        self.change('go_unit_report', lambda j: j['summary'].update(failures=['synthetic failure'])); self.refuse()

    def test_raw_go_failure_refused_even_if_unit_passed_and_resealed(self):
        path = self.args.go_unit_report.parent / 'go-test.jsonl'
        path.write_text(path.read_text() + '{"Action":"fail","Package":"synthetic"}\n')
        self.change('go_unit_report', lambda j: j['rawFiles'].update({'go-test.jsonl': wrapper.digest(path)})); self.refuse()

    def test_arbitrary_single_python_success_is_not_required_two_suites(self):
        self.change('go_unit_report', lambda j: j.update(pythonResults=[{'command': ['python3', '-c', 'pass'], 'exitCode': 0}]))
        self.refuse()

    def test_duplicate_python_command_cannot_replace_legacy_suite(self):
        self.change('go_unit_report', lambda j: j['pythonResults'].__setitem__(1, dict(j['pythonResults'][0])))
        self.refuse()

    def test_python_logs_cannot_be_swapped_even_when_hashes_and_summaries_are_updated(self):
        directory = self.args.go_unit_report.parent
        upgrade, legacy = directory / 'upgrade-tests.txt', directory / 'legacy-tests.txt'
        first, second = upgrade.read_bytes(), legacy.read_bytes()
        upgrade.write_bytes(second); legacy.write_bytes(first)
        def swap(data):
            for name in ('upgrade-tests.txt', 'legacy-tests.txt'):
                data['rawFiles'][name] = wrapper.digest(directory / name)
            data['pythonResults'][0]['summary'] = upgrade.read_text()[-500:]
            data['pythonResults'][1]['summary'] = legacy.read_text()[-500:]
        self.change('go_unit_report', swap); self.refuse()

    def test_prepared_source_pin_cannot_be_substituted(self):
        self.change('prepared_manifest', lambda j: j.update(upstreamCommit='0' * 40)); self.refuse()

    def test_archive_cannot_omit_actual_process_or_collector_checks(self):
        self.change('archive_report', lambda j: j['result']['checks'].pop()); self.refuse()

    def test_context_cannot_omit_protocol_or_diagnostic_cases(self):
        self.change('context_report', lambda j: j['checks'].pop()); self.refuse()

    def test_filtered_go_unit_command_refused(self):
        self.change('go_unit_report', lambda j: j['goCommand'].append('-run=SyntheticOnly')); self.refuse()

    def test_duplicate_json_passed_key_refused(self):
        path = self.args.providers_report
        path.write_text(path.read_text()[:-1] + ',"passed":false}')
        self.repin('providers_report'); self.refuse()


if __name__ == '__main__':
    unittest.main()
