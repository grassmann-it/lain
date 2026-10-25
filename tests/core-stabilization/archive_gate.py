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
import runpy
import sqlite3
import tempfile
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


def collector(run, dotnet, helper, database):
    envelope = run.root / 'collector-envelope.json'
    batch = run.get('/lain/events?after=0&limit=1000')
    expect(len(batch['events']) == batch['highWatermark'], 'collector fixture exceeds complete envelope bound')
    envelope.write_text(json.dumps(batch))
    result = subprocess.run([dotnet, str(helper), '--collect-journal', run.url + '/', str(envelope), str(database)],
                            capture_output=True, env={**run.env, **{name: os.environ[name] for name in ('DOTNET_ROOT', 'DOTNET_ROOT_X64') if name in os.environ}}, timeout=45)
    expect(result.returncode == 0, 'real Host collector failed: ' + result.stderr.decode()[-2000:])
    summary = json.loads(result.stdout)
    expect(summary['cursor'] == batch['highWatermark'] and summary['complete'], 'collector did not preserve source/cursor/completeness')
    # Inspect the actual store produced by the Host implementation, not a
    # stand-in Python ingestion algorithm. Repeated polls must create no gaps.
    with sqlite3.connect(database.as_uri() + '?mode=ro', uri=True) as db:
        expect(db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok', 'collector SQLite integrity failed')
        sources = db.execute('SELECT source_id,cursor,high_watermark FROM sources').fetchall()
        expect(sources == [(batch['sourceId'], batch['highWatermark'], batch['highWatermark'])], 'collector changed durable source/cursor')
        expect(db.execute('SELECT COUNT(*) FROM journal_events').fetchone()[0] == batch['highWatermark'], 'collector lost or duplicated events')
        expect(db.execute('SELECT COUNT(*) FROM coverage_gaps').fetchone()[0] == 0, 'format transition created coverage gap')
    return summary


def sealed_inventory(path):
    manifest = json.loads((path / 'manifest.json').read_text())
    for part in manifest['sealed']:
        expect(digest(path / part['file']) == part['sha256'], 'actual sealed bytes differ from manifest')
        expect((path / part['file']).stat().st_size == part['bytes'], 'actual sealed byte count differs')
    return {'manifestSha256': digest(path / 'manifest.json'), 'sealed': manifest['sealed'],
            'active': {'file': manifest['active'], 'sha256': digest(path / manifest['active']),
                       'bytes': (path / manifest['active']).stat().st_size}}


def probe(core, dotnet, helper):
    with ArchiveRun(core) as run:
        expect(run.infer('fixture-a', protocol='chat')[0] == 200, 'synthetic seed failed')
        seed = run.events()
        run.stop()
        # Expand only our stopped synthetic fixture, preserving actual Core
        # record shapes. This creates > 512 cursor positions without provider
        # load and without touching any installed/original user journal.
        synthetic = []
        for request in range(270):
            for template in seed['events']:
                event = dict(template)
                event.update(requestId=f'archive-request-{request}', attemptId=f'archive-attempt-{request}', sequence=len(synthetic)+1)
                event['eventId'] = f"{seed['sourceId']}:{event['sequence']}"
                synthetic.append(event)
        expect(600 < len(synthetic) < 950, 'synthetic fixture must span sparse strides within the collector envelope cap')
        lines = [json.dumps(event, separators=(',', ':')).encode()+b'\n' for event in synthetic]
        with run.journal.open('wb') as output:
            output.write(b''.join(lines)); output.flush(); os.fsync(output.fileno())
        run.start()
        baseline = all_events(run, 113)
        database = run.root / 'collector.sqlite'
        baseline_summary = collector(run, dotnet, helper, database)
        original = run.journal
        maintenance(run, 'journal-segment-copy', original, run.root / 'busy', limit=4096, success=False)
        run.stop()
        original_bytes = original.read_bytes()
        sparse_cap = sum(map(len, lines[:600]))
        sparse = maintenance(run, 'journal-segment-copy', original, run.root / 'sparse-archive', limit=sparse_cap)
        run.journal = Path(sparse['destination'])
        sparse_manifest = json.loads((run.journal / 'manifest.json').read_text())
        expect(sparse_manifest['sealed'][0]['last'] == 600, 'sparse fixture did not create a real 600-record sealed segment')
        run.start()
        for cursor in (254, 255, 256, 257, 510, 511, 512, 513, 598, 599, 600, 601):
            page = run.get(f'/lain/events?after={cursor}&limit=17')
            expect(page['events'] == baseline['events'][cursor:cursor+17], 'intra-segment sparse cursor lookup changed')
        expect(collector(run, dotnet, helper, database) == baseline_summary, 'sparse archive collection changed SQLite totals')
        run.stop()
        sparse_inventory = sealed_inventory(run.journal)
        segmented = maintenance(run, 'journal-segment-copy', original, run.root / 'archive', limit=4096)
        expect(segmented['sealedSegments'] >= 2, 'no sealed boundaries created')
        expect(original.read_bytes() == original_bytes, 'original flat modified')
        run.journal = Path(segmented['destination'])
        expect(run.journal.is_dir(), 'archive is not a distinct on-disk format')
        run.start()
        expect(all_events(run, 5) == baseline, 'archive replay changed source, events, sums or cursor')
        expect(collector(run, dotnet, helper, database) == baseline_summary, 'existing SQLite cursor/sums changed at archive adoption')
        maintenance(run, 'journal-export-flat', run.journal, run.root / 'live-export', success=False)
        for cursor in range(baseline['cursor'] + 1):
            page = run.get(f'/lain/events?after={cursor}&limit=7')
            expect(page['events'] == baseline['events'][cursor:cursor+7], 'cross-boundary cursor lookup changed')
        initial = json.loads((run.journal / 'manifest.json').read_text())
        for _ in range(4):
            expect(run.infer('fixture-a', protocol='chat')[0] == 200, 'append through segment rotation failed')
        live = all_events(run, 4)
        live_summary = collector(run, dotnet, helper, database)
        expect(collector(run, dotnet, helper, run.root / 'fresh.sqlite') == live_summary, 'fresh archive collection differs from resumed SQLite')
        rotated = json.loads((run.journal / 'manifest.json').read_text())
        expect(len(rotated['sealed']) > len(initial['sealed']), 'runtime did not rotate real files')
        expect(all(part['bytes'] <= 4096 for part in rotated['sealed']), 'sealed byte cap exceeded')
        expect((run.journal / rotated['active']).stat().st_size <= 4096, 'active byte cap exceeded')
        expect(live['events'][:len(baseline['events'])] == baseline['events'], 'rotation rewrote prior events')
        run.process.kill(); run.process.wait(timeout=10)
        run.start()
        expect(all_events(run, 3) == live, 'abrupt Core restart lost acknowledged archive events')
        expect(collector(run, dotnet, helper, database) == live_summary, 'restart changed collected SQLite totals')
        run.stop()
        archive_path = run.journal
        inventory = sealed_inventory(archive_path)
        # Reuse the existing actual full-tree maintenance functions on this
        # stopped synthetic state, including the real collector database.
        maintenance_api = runpy.run_path(str(ROOT / 'scripts/lain-maintenance.py'))
        with tempfile.TemporaryDirectory(prefix='lain-archive-restore-') as restore_temp:
            backup = Path(restore_temp) / 'backup'
            restored = Path(restore_temp) / 'restored'
            maintenance_api['backup'](run.root, backup)
            maintenance_api['restore'](backup, restored)
            run.journal = restored / archive_path.relative_to(run.root)
            expect(sealed_inventory(run.journal) == inventory, 'whole-tree backup/restore changed archive bytes')
            run.start()
            expect(all_events(run, 4) == live, 'restored archive replay changed')
            expect(collector(run, dotnet, helper, restored / database.relative_to(run.root)) == live_summary, 'matching restored SQLite cursor/sums changed')
            run.stop()
        run.journal = archive_path
        report = maintenance(run, 'journal-export-flat', archive_path, run.root / 'rollback')
        expected_bytes = b''.join((archive_path / p['file']).read_bytes() for p in rotated['sealed']) + (archive_path / rotated['active']).read_bytes()
        run.journal = Path(report['destination'])
        expect(run.journal.read_bytes() == expected_bytes, 'legacy-flat export changed exact bytes')
        expect(report['sha256'] == hashlib.sha256(expected_bytes).hexdigest(), 'export hash differs')
        run.start()
        expect(all_events(run, 2) == live, 'flat rollback replay differs')
        expect(collector(run, dotnet, helper, database) == live_summary, 'flat export changed existing collector SQLite')
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
                'sealedInventory': inventory, 'sparseInventory': sparse_inventory, 'sparseCheckpointCursorsCompared': 12, 'baselineSqliteSummary': baseline_summary, 'rotatedSqliteSummary': live_summary,
                'checks': ['writer-exclusion', 'original-unchanged', 'bounded-real-segments',
                           'cross-boundary-identical-replay', 'intra-segment-sparse-checkpoints', 'runtime-rotation', 'abrupt-process-restart',
                           'exact-flat-export', 'flat-replay-and-continuation', 'corruption-refused',
                           'actual-host-collector-resume-and-fresh-replay', 'matching-whole-tree-snapshot-and-sqlite-restore'],
                'oldBinaryExecutionTested': False, 'fullHostLifecycleRetested': False, 'realHostCollectorAndSqliteTested': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--core', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('--core-sha256', required=True)
    parser.add_argument('--source-manifest-sha256', required=True)
    parser.add_argument('--usage-helper', required=True, type=Path, help='fresh Lain.Usage.Tests.dll with --collect-journal support')
    parser.add_argument('--dotnet', required=True, type=Path)
    args = parser.parse_args()
    expect([name for _, name in socket.if_nameindex()] == ['lo'], 'private loopback-only namespace required')
    core = args.core.resolve(strict=True)
    helper, dotnet = args.usage_helper.resolve(strict=True), args.dotnet.resolve(strict=True)
    manifest = ROOT / 'proxy/source-manifest.json'
    expect(digest(core) == args.core_sha256 and digest(manifest) == args.source_manifest_sha256, 'supplied binary/source pins differ')
    helper_files = {p.name: digest(p) for p in sorted(helper.parent.iterdir()) if p.is_file() and p.suffix in ('.dll', '.json')}
    result = probe(core, str(dotnet), helper)
    expect(digest(core) == args.core_sha256 and digest(manifest) == args.source_manifest_sha256, 'binary/source changed during gate')
    expect(helper_files == {p.name: digest(p) for p in sorted(helper.parent.iterdir()) if p.is_file() and p.suffix in ('.dll', '.json')}, 'collector helper changed during gate')
    report = {'schemaVersion': 1, 'passed': True, 'syntheticOnly': True,
              'binarySha256': digest(core), 'sourceManifestSha256': digest(ROOT / 'proxy/source-manifest.json'),
              'harnessSha256': digest(__file__), 'collectorHelperFiles': helper_files, 'result': result}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
