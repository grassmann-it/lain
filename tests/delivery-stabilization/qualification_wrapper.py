#!/usr/bin/env python3
"""Seal existing exact-Core synthetic evidence; never build or execute Core."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

UPGRADE = ('protocol-durability', 'independent-auth-crash', 'inspector', 'upgrade-boundaries',
           'native-tool-boundary', 'hostwriter-transports', 'version-transition', 'consumer-replay-10')
PROCESS = ('drain-sse-reopen', 'qualified-dispatch-tools', 'native-protocol-scope',
           'parent-stdin-eof', 'abrupt-owner-death', 'legacy-journal-recovery-copy')
ARCHIVE = ('writer-exclusion', 'original-unchanged', 'bounded-real-segments',
           'cross-boundary-identical-replay', 'intra-segment-sparse-checkpoints', 'runtime-rotation',
           'abrupt-process-restart', 'exact-flat-export', 'flat-replay-and-continuation',
           'corruption-refused', 'actual-host-collector-resume-and-fresh-replay',
           'matching-whole-tree-snapshot-and-sqlite-restore')
REPORTS = ('upgrade', 'process', 'providers', 'context', 'archive', 'go-unit')
COHERE_RESPONSES = ('responses_json', 'responses_stream', 'responses_tool', 'responses_store_true',
                    'responses_previous_response_id', 'responses_native_search',
                    'responses_additional_native_search', 'responses_native_file_search')
UPGRADE_FILES = {name: name + '.json' for name in UPGRADE[:-1]}
UPGRADE_FILES['consumer-replay-10'] = 'consumer-replay.json'
UPGRADE_CASES = {
    'protocol-durability': ('same-alias-200-replay-restart', 'sse-tool-continuation', 'responses-tool-translation',
        'strict-429', 'pool-429-retry', 'cancel', 'unknown-token-components', 'credential-lifecycle',
        'deactivated-account', 'crash-inflight', 'journal-failures', 'startup-failures',
        'terminal-write-failure', 'responses-terminal-write-failure', 'anthropic-messages-tools'),
    'independent-auth-crash': ('management-and-inference-auth', 'kill-after-dispatch-restart'),
    'inspector': ('management-auth-and-epoch', 'claude-worker-parent', 'codex-turn-metadata',
        'thread-header-precedence', 'chat-singleton-tool-result', 'anthropic-singleton-tool-result',
        'unknown-identities-common-prefix', 'untrusted-header-poison', 'claude-user-id-json-explicit-only',
        'codex-explicit-thread-spawn', 'codex-ordinary-fork-is-not-worker',
        'tool-continuation-one-conversation-one-turn-two-requests', 'client-cancellation-terminal-identity',
        'credentials-absent-and-payload-not-durable', 'new-process-epoch-no-payload-replay-durable-usage-unchanged'),
    'upgrade-boundaries': ('snapshot-no-refresh-import-boundary', 'copied-owned-file-never-borrowed',
        'unqualified-free-only-fails-closed', 'gemini-native-bearer-and-default-key'),
    'native-tool-boundary': ('native-image-generation-exclusion-and-positive-controls',
        'final-payload-override-exclusion-and-recovery'),
    'hostwriter-transports': ('openai-chat', 'openai-responses'),
    'version-transition': ('journal-upgrade-and-rollback-read', 'mixed-version-consumer-replay-10'),
}
PYTHON_GATES = {
    'upgrade-tests.txt': ('tests/acceptance', 'test_upgrade_probe.py', 'test_upgrade_probe', 12),
    'legacy-tests.txt': ('tests/delivery-stabilization', 'test_legacy_rollback.py', 'test_legacy_rollback', 10),
}
GO_PACKAGES = {'lain.local/proxy/internal/freeonly', 'lain.local/proxy/internal/lain', 'lain.local/proxy/cmd/lain-proxy'}
FIXED_BUILD_INPUTS = ('proxy/upstream.json', 'proxy/toolchain.json', 'proxy/LICENSE.upstream',
                      'scripts/build-core.sh', 'scripts/build-core-verify.py', 'scripts/prepare-core.py')


def require(value, description):
    if not value:
        raise ValueError(description)


def pin(value, digits=64):
    require(isinstance(value, str) and re.fullmatch('[0-9a-f]{' + str(digits) + '}', value) is not None,
            'Expected full lowercase source/hash pin')
    return value


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON key')
        result[key] = value
    return result


def decode(data):
    def invalid_constant(_):
        raise ValueError('Nonfinite JSON constant')
    return json.loads(data, object_pairs_hook=unique_object, parse_constant=invalid_constant)


class Inputs:
    def __init__(self):
        self.files = {}

    def read(self, path, expected=None, as_json=True):
        path = Path(path)
        require(path.is_absolute() and not path.is_symlink() and path.is_file(), 'Expected absolute regular input file')
        data = path.read_bytes()
        actual = hashlib.sha256(data).hexdigest()
        if expected is not None:
            require(actual == pin(expected), 'Input SHA-256 differs from reviewed pin')
        if path in self.files:
            require(self.files[path] == actual, 'Input changed during qualification sealing')
        self.files[path] = actual
        return decode(data) if as_json else data

    def check_unchanged(self):
        require(all(digest(path) == value for path, value in self.files.items()), 'Qualification inputs changed')


def no_inner_failure(value):
    if isinstance(value, dict):
        for key, item in value.items():
            if key == 'passed':
                require(item is True, 'Report contains failed or nonboolean passed field')
            if key == 'failures':
                require(item == [], 'Report contains inner failures')
            no_inner_failure(item)
    elif isinstance(value, list):
        for item in value:
            no_inner_failure(item)


def named_checks(report, expected, key='name'):
    checks = report.get('checks')
    require(isinstance(checks, list) and len(checks) == len(expected), 'Required report checks are missing/duplicated')
    require(all(isinstance(check, dict) for check in checks), 'Invalid check record')
    require({check.get(key) for check in checks} == set(expected), 'Required report check names differ')
    require(all(check.get('passed') is True for check in checks), 'Required check did not pass')
    return checks


def python_command(directory, pattern):
    return ['python3', '-m', 'unittest', 'discover', '-s', directory, '-p', pattern, '-v']


def git_bytes(repository, source, name):
    pin(source, 40)
    result = subprocess.run(['git', '-C', str(repository), 'show', source + ':' + name],
                            capture_output=True, timeout=30)
    require(result.returncode == 0, 'Pinned Git source input unavailable')
    return result.stdout


def verify_git_inputs(repository, source_commit, declared):
    require(isinstance(declared, dict) and declared, 'Core build input inventory missing')
    result = subprocess.run(['git', '-C', str(repository), 'ls-tree', '-r', '-z', source_commit, '--',
                             'proxy/adapter', 'proxy/patches', *FIXED_BUILD_INPUTS], capture_output=True, timeout=30)
    require(result.returncode == 0, 'Pinned Git input inventory unavailable')
    actual = set()
    for entry in result.stdout.split(b'\0'):
        if not entry:
            continue
        metadata, name = entry.split(b'\t', 1)
        mode, kind, _ = metadata.split()
        require(kind == b'blob' and mode in (b'100644', b'100755'), 'Core Git input must be a regular file')
        actual.add(name.decode())
    require(actual == set(declared), 'Committed Core input inventory differs from source manifest')
    for name in sorted(actual):
        require(hashlib.sha256(git_bytes(repository, source_commit, name)).hexdigest() == pin(declared[name]),
                'Committed Core input bytes differ from source manifest')


def seal(args):
    inputs = Inputs()
    pin(args.source_commit, 40); pin(args.go_unit_source_commit, 40)
    core_hash = pin(args.core_sha256)
    require(args.core.is_absolute() and args.core.is_file() and not args.core.is_symlink(), 'Expected absolute regular candidate Core')
    require(digest(args.core) == core_hash, 'Candidate Core differs from reviewed pin')
    build = inputs.read(args.build_manifest, args.build_manifest_sha256)
    source = inputs.read(args.source_manifest, args.source_manifest_sha256)
    prepared = inputs.read(args.prepared_manifest, args.prepared_manifest_sha256)
    source_hash = args.source_manifest_sha256
    require(build.get('schemaVersion') == 2 and source.get('schemaVersion') == 2, 'Unsupported Core provenance schema')
    require(build.get('binarySha256') == core_hash and build.get('sourceManifestSha256') == source_hash,
            'Core build manifest does not bind candidate bytes/source')
    require(build.get('binary') == Path(args.core).name, 'Core build manifest binary name differs')
    for source_commit in (args.source_commit, args.go_unit_source_commit):
        require(hashlib.sha256(git_bytes(args.repository, source_commit, 'proxy/source-manifest.json')).hexdigest() == source_hash,
                'Application/Go source commit does not contain the exact Core source manifest')
        verify_git_inputs(args.repository, source_commit, source.get('buildInputs'))
    toolchain_bytes = git_bytes(args.repository, args.source_commit, 'proxy/toolchain.json')
    require(hashlib.sha256(toolchain_bytes).hexdigest() == source['buildInputs']['proxy/toolchain.json'],
            'Pinned toolchain is not bound by source manifest')
    toolchain = decode(toolchain_bytes)
    upstream = source['upstreamDependency']
    expected_build = {'snapshotVersion': source['snapshotVersion'], 'lainPatchVersion': source['lainPatchVersion'],
                      'upstreamCommit': upstream['commit'], 'upstreamVersion': upstream['version'],
                      'upstreamArchiveSha256': upstream['archiveSha256'], 'patchSetSha256': source['patchSetSha256'],
                      'upstreamSourceModified': source['upstreamSourceModified']}
    require(all(build.get(key) == value and type(build.get(key)) is type(value) for key, value in expected_build.items()),
            'Build/upstream/patch identity differs from source manifest')
    require(prepared.get('schemaVersion') == 1 and prepared.get('sourceManifestSha256') == source_hash,
            'Prepared SDK source manifest is not bound to candidate Core source')
    require(all(prepared.get(key) == expected_build[key] for key in
                ('upstreamCommit', 'upstreamVersion', 'upstreamArchiveSha256', 'patchSetSha256')),
            'Prepared SDK dependency/patch identity differs')
    require(isinstance(prepared.get('files'), dict) and prepared['files'], 'Prepared source inventory is missing')
    for name, value in prepared['files'].items():
        require(not Path(name).is_absolute() and '..' not in Path(name).parts, 'Unsafe prepared source path')
        pin(value)

    evidence = {}

    def report(name, path, expected_hash=None, binary_key='coreSha256'):
        data = inputs.read(path, expected_hash)
        require(isinstance(data, dict) and data.get('passed') is True, 'Raw report did not explicitly pass')
        require(data.get('syntheticOnly') is True, 'Raw process report lacks synthetic scope')
        require(data.get(binary_key) == core_hash, 'Raw report belongs to another Core binary')
        if 'sourceManifestSha256' in data:
            require(data['sourceManifestSha256'] == source_hash, 'Raw report belongs to another source manifest')
        if 'binaryUnchanged' in data:
            require(data['binaryUnchanged'] is True, 'Raw report binary changed')
        require(not data.get('error'), 'Raw report contains a terminal error')
        no_inner_failure(data)
        evidence[name] = {'file': str(path), 'sha256': inputs.files[Path(path)],
                          'reportedSourceManifestSha256': data.get('sourceManifestSha256'),
                          'sourceBinding': 'exact candidate binary via build manifest',
                          'scope': {'syntheticOnly': True, **{key: data[key] for key in
                                    ('scope', 'notTested', 'notQualified', 'requiredSeparateGates', 'pending') if key in data}}}
        return data

    reports = {}
    for name in REPORTS[:-1]:
        attr = name.replace('-', '_') + '_report'
        reports[name] = report(name, getattr(args, attr), getattr(args, attr + '_sha256'),
                               'binarySha256' if name == 'archive' else 'coreSha256')
    for name in ('process', 'providers', 'context'):
        require(reports[name].get('binaryUnchanged') is True, 'Process report lacks unchanged-binary proof')

    for check in named_checks(reports['upgrade'], UPGRADE):
        relative = Path(check.get('report', ''))
        require(str(relative) == UPGRADE_FILES[check['name']], 'Upgrade summary references the wrong child report')
        child = report('upgrade/' + check['name'], args.upgrade_report.parent / relative)
        if check['name'] != 'consumer-replay-10':
            require(check.get('binaryHashMatches') is True, 'Upgrade summary lacks exact binary match')
            require(type(check.get('cases')) is int and check['cases'] > 0 and len(child.get('checks', [])) == check['cases'],
                    'Upgrade summary/inner case coverage differs')
            named_checks(child, UPGRADE_CASES[check['name']], 'case' if check['name'] == 'inspector' else 'name')
        else:
            require(child.get('replays') == 10 and child.get('storeReopenedEachReplay') is True,
                    'SQLite consumer replay coverage differs')

    named_checks(reports['process'], PROCESS)
    require(reports['process'].get('actualCoreProcesses') is True and
            reports['process'].get('networkIsolation') == 'private_network_namespace_loopback_only',
            'Core process isolation/actual execution evidence missing')
    cases = reports['providers'].get('checks', [])
    expected_cases = {(provider, case) for provider in ('cohere', 'kilo') for case in
                      ('json', 'stream', 'tool', 'status_401', 'status_403', 'status_429', 'status_404', 'cancel', 'truncated')}
    expected_cases.add(('kilo', 'status_402'))
    expected_cases.update(('cohere', case) for case in COHERE_RESPONSES)
    require(len(cases) == len(expected_cases) and {(c.get('provider'), c.get('case')) for c in cases} == expected_cases and
            all(c.get('passed') is True for c in cases), 'Provider transport coverage differs')
    cases = reports['context'].get('checks', [])
    expected_context = {(protocol, stream, status) for protocol in ('chat', 'responses')
                        for stream in (False, True) for status in (400, 413, 422)}
    ordinary = [c for c in cases if 'protocol' in c]
    require(len(cases) == 14 and len(ordinary) == 12 and
            {(c.get('protocol'), c.get('stream'), c.get('status')) for c in ordinary} == expected_context and
            all(c.get('contextCodePreserved') is True and c.get('completeConversationAndTools') is True and
                c.get('upstreamRequests') == 1 for c in ordinary), 'Context boundary coverage differs')
    require({c.get('case') for c in cases if 'case' in c} == {'oversize', 'unrelated-small'}, 'Context diagnostic cases missing')
    require(all(c.get('passed') is True for c in cases if 'case' in c), 'Context diagnostic case did not pass')
    result = reports['archive'].get('result', {})
    require(isinstance(result.get('checks'), list) and len(result['checks']) == len(ARCHIVE) and set(result['checks']) == set(ARCHIVE),
            'Archive process/collector coverage differs')
    require(result.get('realHostCollectorAndSqliteTested') is True, 'Archive lacks actual collector/SQLite evidence')
    evidence['archive']['scope']['runtimeBoundaries'] = {key: result.get(key) for key in
                                                       ('oldBinaryExecutionTested', 'fullHostLifecycleRetested', 'realHostCollectorAndSqliteTested')}

    unit = inputs.read(args.go_unit_report, args.go_unit_report_sha256)
    require(unit.get('passed') is True and type(unit.get('goExitCode')) is int and unit['goExitCode'] == 0,
            'Go unit gate did not explicitly pass')
    no_inner_failure(unit)
    require(unit.get('sourceCommit') == args.go_unit_source_commit and unit.get('sourceManifestSha256') == source_hash and
            unit.get('preparedManifestSha256') == args.prepared_manifest_sha256, 'Go unit source/prepared pins differ')
    require(unit.get('goVersion') == toolchain['goVersion'] and
            build.get('goVersion') == 'go version ' + toolchain['goVersion'] + ' linux/amd64', 'Go compiler identity differs')
    command = unit.get('goCommand', [])
    require(isinstance(command, list) and len(command) == 9 and Path(command[0]).name == 'go' and
            command[1:] == ['test', '-json', '-mod=readonly', '-p=1', './internal/...', './cmd/lain-proxy', '-count=1', '-timeout=90s'],
            'Go unit report does not cover the complete prepared adapter command')
    summary = unit.get('summary', {})
    require(summary.get('failures') == [] and type(summary.get('testPasses')) is int and summary['testPasses'] > 0 and
            type(summary.get('topLevelPasses')) is int and summary['topLevelPasses'] > 0, 'Go unit summary incomplete')
    packages = summary.get('packages', [])
    require(len(packages) == len(GO_PACKAGES) and {p.get('Package') for p in packages} == GO_PACKAGES and
            all(p.get('Action') == 'pass' for p in packages), 'Go package coverage incomplete')
    python_results = unit.get('pythonResults')
    require(isinstance(python_results, list) and len(python_results) == len(PYTHON_GATES), 'Required Python gate count differs')
    python_logs = {}
    for name, (directory, pattern, module, minimum) in PYTHON_GATES.items():
        matches = [r for r in python_results if isinstance(r, dict) and r.get('command') == python_command(directory, pattern)]
        require(len(matches) == 1 and type(matches[0].get('exitCode')) is int and matches[0]['exitCode'] == 0,
                'Required exact Python command missing, duplicated or failed')
        python_logs[name] = matches[0]
    raw_files = unit.get('rawFiles', {})
    require(set(raw_files) == {'go-test.jsonl', 'upgrade-tests.txt', 'legacy-tests.txt'}, 'Required Go raw evidence files missing')
    for name, value in raw_files.items():
        path = args.go_unit_report.parent / name
        content = inputs.read(path, value, as_json=False)
        evidence['go-unit/' + name] = {'file': str(path), 'sha256': inputs.files[path], 'scope': 'raw unit/command output'}
        if name in PYTHON_GATES:
            text = content.decode()
            command_result = python_logs[name]
            _, _, module, minimum = PYTHON_GATES[name]
            require(isinstance(command_result.get('summary'), str) and command_result['summary'] == text[-500:],
                    'Python summary does not match its exact raw log tail')
            match = re.search(r'\nRan ([1-9][0-9]*) tests? in [0-9.]+s\n\nOK\s*$', text)
            require(match is not None and int(match[1]) >= minimum and '(' + module + '.' in text,
                    'Python raw log does not prove the expected successful suite')
            evidence['go-unit/' + name]['command'] = command_result['command']
            evidence['go-unit/' + name]['exitCode'] = command_result['exitCode']
        if name == 'go-test.jsonl':
            events = [decode(line) for line in content.splitlines() if line.strip()]
            require(events and all(e.get('Action') != 'fail' for e in events), 'Raw Go log contains failures')
            require(sum(e.get('Action') == 'pass' and 'Test' in e for e in events) == summary['testPasses'], 'Go test count differs from raw log')
            require(sum(e.get('Action') == 'pass' and 'Test' in e and '/' not in e['Test'] for e in events) == summary['topLevelPasses'],
                    'Go top-level test count differs from raw log')
            require({e.get('Package') for e in events if e.get('Action') == 'pass' and 'Test' not in e} == GO_PACKAGES,
                    'Raw Go package coverage incomplete')
            require(all(e.get('Test') == 'TestJournalBoundedGrowthMeasurement' for e in events if e.get('Action') == 'skip'),
                    'Required Go test was skipped')
    evidence['go-unit'] = {'file': str(args.go_unit_report), 'sha256': args.go_unit_report_sha256,
                           'scope': unit.get('qualification'), 'sourceCommit': unit['sourceCommit'],
                           'preparedManifestSha256': unit['preparedManifestSha256']}
    inputs.check_unchanged()
    require(digest(args.core) == core_hash, 'Core changed during qualification sealing')
    return {'format': 1, 'passed': True, 'sha256': core_hash, 'sourceManifestSha256': source_hash,
            'sourceCommit': args.source_commit, 'goUnitSourceCommit': unit['sourceCommit'],
            'buildManifestSha256': args.build_manifest_sha256, 'preparedManifestSha256': args.prepared_manifest_sha256,
            'syntheticOnly': True, 'rawReports': evidence,
            'scope': 'Exact-Core synthetic process, durability, protocol, context, archive and Go unit qualification; application source is bound by identical Core manifest bytes.',
            'notQualified': ['live providers or real OAuth rotation', 'installed service update', 'exact package/Desktop/native acceptance',
                             'arbitrary data/schema rollback', 'uninterrupted in-flight live update'],
            'preparedSourceScope': 'Exact prepared-manifest pin and upstream identities; no SDK source tree is regenerated or executed by this wrapper.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--go-unit-source-commit', required=True)
    for name in ('core', 'build-manifest', 'source-manifest', 'prepared-manifest'):
        parser.add_argument('--' + name, type=Path, required=True)
        parser.add_argument('--' + name + '-sha256', required=True)
    for name in REPORTS:
        parser.add_argument('--' + name + '-report', type=Path, required=True)
        parser.add_argument('--' + name + '-report-sha256', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    os.umask(0o077)
    require(args.output.is_absolute() and not args.output.exists(), 'Qualification output must be new and absolute')
    result = seal(args)
    with args.output.open('x') as stream:
        json.dump(result, stream, indent=2); stream.write('\n')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, subprocess.TimeoutExpired):
        raise SystemExit('Qualification wrapper refused: invalid, incomplete or changed evidence; no pass artifact written')
