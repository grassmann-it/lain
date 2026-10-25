#!/usr/bin/env python3
"""Actual Core archive/rollback fixture. Requires a supplied Core and private loopback namespace.

No build, installed service, provider account or original journal is accessed.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tests/core-stabilization'))
from process_probe import PrivateRun, expect, wait_for


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


class ArchiveRun(PrivateRun):
    def ready(self):
        def check():
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError('private Core exited: ' + (self.root / 'core.log').read_text()[-1800:])
            try:
                if self.journal.is_dir():
                    source = json.loads((self.journal / 'manifest.json').read_text())['sourceId']
                else:
                    source = Path(str(self.journal) + '.source').read_text()
                return self.get('/lain/health')['ready'] and self.events()['sourceId'] == source
            except (OSError, ValueError):
                return False
        wait_for(check, 'private archive Core did not become ready', 20)


def maintenance(run, command, path, destination=None, *, limit=None, success=True):
    args = [run.core, command, '--journal', str(path)]
    if destination is not None:
        args += ['--destination', str(destination)]
    if limit is not None:
        args += ['--max-segment-bytes', str(limit)]
    result = subprocess.run(args, capture_output=True, env=run.env, timeout=30)
    expect((result.returncode == 0) == success, f'{command}: {result.stderr.decode()}')
    return json.loads(result.stdout) if success else None


def all_events(run, limit):
    events, cursor = [], 0
    while True:
        page = run.get(f'/lain/events?after={cursor}&limit={limit}')
        events += page['events']
        if page['nextCursor'] == page['highWatermark']:
            return {'events': events, 'sourceId': page['sourceId'], 'cursor': page['nextCursor']}
        expect(page['nextCursor'] > cursor, 'replay cursor did not advance')
        cursor = page['nextCursor']


def probe(core):
    with ArchiveRun(core) as run:
        for _ in range(4):
            expect(run.infer('fixture-a', protocol='chat')[0] == 200, 'synthetic seed failed')
        baseline = all_events(run, 5)
        original = run.journal
        maintenance(run, 'journal-segment-copy', original, run.root / 'busy', limit=4096, success=False)
        run.stop()
        original_bytes = original.read_bytes()
        segmented = maintenance(run, 'journal-segment-copy', original, run.root / 'archive', limit=4096)
        expect(segmented['sealedSegments'] >= 2, 'no sealed boundaries created')
        expect(original.read_bytes() == original_bytes, 'original flat modified')
        run.journal = Path(segmented['destination'])
        expect(run.journal.is_dir(), 'archive is not a distinct on-disk format')
        run.start()
        expect(all_events(run, 5) == baseline, 'archive replay changed source, events, sums or cursor')
        maintenance(run, 'journal-export-flat', run.journal, run.root / 'live-export', success=False)
        for cursor in range(baseline['cursor'] + 1):
            page = run.get(f'/lain/events?after={cursor}&limit=7')
            expect(page['events'] == baseline['events'][cursor:cursor+7], 'cross-boundary cursor lookup changed')
        initial = json.loads((run.journal / 'manifest.json').read_text())
        for _ in range(4):
            expect(run.infer('fixture-a', protocol='chat')[0] == 200, 'append through segment rotation failed')
        live = all_events(run, 4)
        rotated = json.loads((run.journal / 'manifest.json').read_text())
        expect(len(rotated['sealed']) > len(initial['sealed']), 'runtime did not rotate real files')
        expect(all(part['bytes'] <= 4096 for part in rotated['sealed']), 'sealed byte cap exceeded')
        expect((run.journal / rotated['active']).stat().st_size <= 4096, 'active byte cap exceeded')
        expect(live['events'][:len(baseline['events'])] == baseline['events'], 'rotation rewrote prior events')
        run.process.kill(); run.process.wait(timeout=10)
        run.start()
        expect(all_events(run, 3) == live, 'abrupt Core restart lost acknowledged archive events')
        run.stop()
        archive_path = run.journal
        report = maintenance(run, 'journal-export-flat', archive_path, run.root / 'rollback')
        expected_bytes = b''.join((archive_path / p['file']).read_bytes() for p in rotated['sealed']) + (archive_path / rotated['active']).read_bytes()
        run.journal = Path(report['destination'])
        expect(run.journal.read_bytes() == expected_bytes, 'legacy-flat export changed exact bytes')
        expect(report['sha256'] == hashlib.sha256(expected_bytes).hexdigest(), 'export hash differs')
        run.start()
        expect(all_events(run, 2) == live, 'flat rollback replay differs')
        expect(run.infer('fixture-a', protocol='chat')[0] == 200, 'flat continuation failed')
        continued = all_events(run, 7)
        expect(continued['sourceId'] == live['sourceId'] and continued['cursor'] > live['cursor'], 'flat continuation reset identity')
        run.stop()
        first = archive_path / rotated['sealed'][0]['file']
        data = first.read_bytes()
        first.write_bytes(data.replace(b'"kind":', b'"kInd":', 1))
        expect(first.read_bytes() != data, 'corruption fixture ineffective')
        maintenance(run, 'journal-inspect', archive_path, success=False)
        maintenance(run, 'journal-export-flat', archive_path, run.root / 'corrupt-export', success=False)
        expect(original.read_bytes() == original_bytes, 'source changed during later operations')
        return {'baselineEvents': len(baseline['events']), 'rotatedEvents': len(live['events']),
                'sealedSegmentsAfterRotation': len(rotated['sealed']), 'segmentByteLimit': 4096,
                'allCursorBoundariesCompared': baseline['cursor'] + 1,
                'sourceId': live['sourceId'], 'exportSha256': report['sha256'],
                'checks': ['writer-exclusion', 'original-unchanged', 'bounded-real-segments',
                           'cross-boundary-identical-replay', 'runtime-rotation', 'abrupt-process-restart',
                           'exact-flat-export', 'flat-replay-and-continuation', 'corruption-refused'],
                'oldBinaryExecutionTested': False, 'fullHostBackupRestoreRetested': False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--core', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args()
    expect([name for _, name in socket.if_nameindex()] == ['lo'], 'private loopback-only namespace required')
    core = args.core.resolve(strict=True)
    result = probe(core)
    report = {'schemaVersion': 1, 'passed': True, 'syntheticOnly': True,
              'binarySha256': digest(core), 'sourceManifestSha256': digest(ROOT / 'proxy/source-manifest.json'),
              'harnessSha256': digest(__file__), 'result': result}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
