#!/usr/bin/env python3
"""Bind existing real publishes and actual PE metadata output; no builds/payloads.

This narrow publish-binding report is not mixed runtime or installed acceptance.
Root must first run the prepared reader on these exact DLLs, not handwrite output.
"""
import argparse
import hashlib
import importlib.util
import json
import os
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]


def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def need(value, message):
    if not value: raise RuntimeError(message)


def safe(path):
    need(path.is_absolute() and '..' not in path.parts, 'Absolute nontraversing path required')
    need(not any(item.is_symlink() for item in (path, *path.parents)), 'Symlink path refused')
    return path


def write_new(path, value):
    safe(path)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    with os.fdopen(descriptor, 'w') as stream:
        json.dump(value, stream, indent=2); stream.write('\n')


def load_api(installer, helper):
    need(sha(REPO / 'scripts/host-cli-core-refresh.py') == installer and sha(REPO / 'scripts/core-refresh.py') == helper,
         'Reviewed source helper pin changed')
    spec = importlib.util.spec_from_file_location('publish_binding_installer', REPO / 'scripts/host-cli-core-refresh.py')
    api = importlib.util.module_from_spec(spec)
    exec(compile(Path(spec.origin).read_bytes(), str(spec.origin), 'exec'), api.__dict__)
    common = api.load_helper(helper)
    return api, common


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('delivery', 'core-package', 'metadata', 'baseline'): parser.add_argument('--' + name, type=Path, required=True)
    for name in ('core-manifest-sha256', 'metadata-sha256', 'baseline-manifest-sha256'): parser.add_argument('--' + name, required=True)
    parser.add_argument('--source-commit', required=True)
    parser.add_argument('--installer-sha256', required=True)
    parser.add_argument('--helper-sha256', required=True)
    parser.add_argument('--desktop-source-commit', required=True)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--stage-dir', type=Path)
    args = parser.parse_args()
    stage_dir = args.stage_dir or args.delivery
    need(re.fullmatch('[0-9a-f]{40}', args.source_commit), 'Full application source commit required')
    source_commit = args.source_commit
    os.umask(0o077)
    for path in (REPO, args.baseline, args.delivery, stage_dir, args.core_package, args.metadata): safe(path)
    INSTALLER, HELPER = args.installer_sha256, args.helper_sha256
    for output_path in (stage_dir / 'package', stage_dir / 'publish-qualification.json', stage_dir / 'mixed-inputs.json'):
        for source in (args.baseline, args.core_package, REPO):
            need(output_path != source and source not in output_path.parents and output_path not in source.parents,
                 'Delivery outputs must not overlap baseline, Core package or checkout')
    api, common = load_api(INSTALLER, HELPER)
    need(sha(args.baseline / 'SHA256SUMS') == args.baseline_manifest_sha256 and sha(args.core_package / 'SHA256SUMS') == args.core_manifest_sha256 and
         sha(args.metadata) == args.metadata_sha256, 'Explicit input pin changed')
    base, core = common.verify(args.baseline), common.verify(args.core_package)
    need(common.load(args.core_package / 'core-refresh.json').get('baseFiles') == base,
         'Core input must be freshly staged over this exact baseline; reuse its binary/evidence, not an old-base package')
    common.check_core(args.core_package)
    output = safe(stage_dir / 'package')
    report_path, inputs_path = stage_dir / 'publish-qualification.json', stage_dir / 'mixed-inputs.json'
    for path in (report_path, inputs_path): safe(path)
    need(not any(os.path.lexists(path) for path in (output, report_path, inputs_path)), 'Fresh outputs required')
    evidence = args.delivery / 'source-evidence'
    config = {'format': 1, 'kind': 'host-cli-core-refresh-inputs', 'sourceCommit': source_commit,
              'base': {'path': str(args.baseline), 'manifestSha256': args.baseline_manifest_sha256},
              'corePackage': {'path': str(args.core_package), 'manifestSha256': args.core_manifest_sha256},
              'helperSha256': HELPER, 'desktopSourceCommit': args.desktop_source_commit, 'output': str(output),
              'sourceEvidence': {'path': str(evidence), 'files': common.inventory(evidence)}}
    metadata = json.loads(args.metadata.read_text())
    for name, project in (('cli', 'Lain.Cli'), ('host', 'Lain.Host')):
        publish = args.delivery / name
        config[name] = {'path': str(publish), 'files': common.inventory(publish), 'informationalVersion': '1.0.0+' + source_commit}
        api.check_publish(common, args.baseline, name, config[name], source_commit)
        need(metadata.get(name) == {'method': 'assembly-metadata-reader',
             'attribute': 'System.Reflection.AssemblyInformationalVersionAttribute',
             'dllSha256': config[name]['files'][project + '.dll'], 'informationalVersion': '1.0.0+' + source_commit, 'passed': True},
             'Actual metadata read does not bind the expected exact DLL/version')
    report = {'kind': 'host-cli-core-publish-qualification', 'passed': True, 'sourceCommit': source_commit,
              'scope': 'Real publish inventories/dependency invariance and actual PE metadata read only; no runtime/installed acceptance.',
              'baseManifestSha256': base['SHA256SUMS'], 'coreManifestSha256': core['SHA256SUMS'],
              'sourceEvidenceFiles': config['sourceEvidence']['files'], 'cliFiles': config['cli']['files'],
              'hostFiles': config['host']['files'], 'assemblyMetadata': metadata,
              'rawAssemblyMetadata': {'path': str(args.metadata), 'sha256': args.metadata_sha256}}
    need(base and core, 'Complete verified packages required')
    need(re.fullmatch('[0-9a-f]{40}', args.desktop_source_commit) and
         common.load(args.baseline / 'build-info.json')['componentSourceCommits']['desktop'] == args.desktop_source_commit,
         'Retained Desktop source pin differs')
    if not args.apply:
        print(json.dumps({'phase': 'planned', 'mutations': False, 'inputs': config, 'publishQualification': report}, indent=2))
        return
    if not stage_dir.exists(): stage_dir.mkdir(mode=0o700)
    write_new(report_path, report)
    config['qualification'] = {'path': str(report_path), 'sha256': sha(report_path)}
    write_new(inputs_path, config)
    print(json.dumps({'inputs': str(inputs_path), 'inputsSha256': sha(inputs_path), 'publishReportSha256': sha(report_path)}, indent=2))


if __name__ == '__main__': main()
