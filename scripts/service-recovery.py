#!/usr/bin/env python3
"""Verify a pinned stopped snapshot; explicitly restore to a fresh quarantined home.

Never replaces the current installation, controls a service or starts a backend.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path

HERE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--snapshot', type=Path, required=True)
    parser.add_argument('--snapshot-manifest-sha256', required=True)
    parser.add_argument('--helper-sha256', required=True)
    parser.add_argument('--destination', type=Path, required=True)
    parser.add_argument('--stopped', action='store_true')
    parser.add_argument('--execute', action='store_true')
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('portable_update', HERE / 'service-update.py')
    api = importlib.util.module_from_spec(spec)
    source = HERE / 'service-update.py'
    exec(compile(source.read_bytes(), str(source), 'exec'), api.__dict__)
    source, destination = api.safe(args.snapshot), api.safe(args.destination)
    api.pinned(source / 'snapshot.json', args.snapshot_manifest_sha256)
    helper = api.module(HERE / 'lain-maintenance.py', args.helper_sha256, 'pinned_recovery')
    api.need(not destination.exists() and destination.parent.is_dir(), 'Fresh recovery destination required')
    helper.outside(source, destination)
    helper.inventory(source)
    manifest = json.loads((source / 'snapshot.json').read_text())
    api.need(manifest.get('format') == 1 and manifest.get('kind') == 'lain-stopped-state' and
             helper.inventory(source / 'state') == manifest['files'], 'Snapshot integrity differs')
    print(json.dumps({'phase': 'recovery-planned', 'mutations': args.execute,
                      'destination': str(destination), 'serviceAction': 'none',
                      'credentialPolicy': 'restore-blocked-until-explicit-reconnect-or-generation-recovery'}))
    if args.execute:
        api.need(args.stopped, 'Recovery requires explicit --stopped acknowledgement')
        os.umask(0o077)
        helper.restore(source, destination)


if __name__ == '__main__':
    try: main()
    except (RuntimeError, OSError, ValueError, KeyError) as error:
        raise SystemExit('Recovery refused: ' + str(error))
