#!/usr/bin/env python3
"""Read-only legacy-file fingerprinting. Snapshot files are private, never commit them."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sys


def digest(path):
    result = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def capture(root):
    root = Path(root)
    files = [root / 'cpa-core' / 'cli-proxy-api', root / 'cpa-core' / 'config.yaml']
    auth = root / 'oauth'
    if not auth.is_dir():
        raise ValueError('legacy auth directory is missing')
    files.extend(sorted(p for p in auth.rglob('*') if p.is_file()))
    # Paths and hashes are kept locally only; credential contents never leave memory.
    return {'schema': 1, 'root': str(root.resolve()), 'files': {
        str(p.relative_to(root)): {'sha256': digest(p), 'mode': p.stat().st_mode & 0o777}
        for p in files}}


def compare(before, after):
    old, new = before['files'], after['files']
    return {'added': len(new.keys() - old.keys()), 'removed': len(old.keys() - new.keys()),
            'changed': sum(old[key] != new[key] for key in old.keys() & new.keys())}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['capture', 'compare'])
    parser.add_argument('--root', default=str(Path.home() / '.local/share/easycliproxyapi'))
    parser.add_argument('--snapshot', required=True)
    args = parser.parse_args()
    snapshot = Path(args.snapshot)
    current = capture(args.root)
    if args.action == 'capture':
        fd = os.open(snapshot, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'w') as stream:
            json.dump(current, stream, sort_keys=True)
        print(json.dumps({'captured_files': len(current['files']), 'private_snapshot': True}))
        return 0
    before = json.loads(snapshot.read_text())
    if before['schema'] != 1 or before['root'] != current['root']:
        raise ValueError('snapshot schema/root mismatch')
    result = compare(before, current)
    def persistent(snapshot):
        return {**snapshot, 'files': {key: value for key, value in snapshot['files'].items() if not key.endswith('.tmp')}}
    stable = compare(persistent(before), persistent(current))
    print(json.dumps({'unchanged': not any(result.values()), **result,
                      'persistent_unchanged': not any(stable.values()), 'persistent': stable,
                      'temporary_auth_files_excluded_from_persistent': True}))
    return int(any(result.values()))


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError):
        print('legacy fingerprint check failed; no credential data emitted', file=sys.stderr)
        sys.exit(2)
