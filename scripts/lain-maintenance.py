#!/usr/bin/env python3
"""Private stopped-state snapshots. Never stops services or overwrites/deletes state."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import uuid


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def safe_path(path):
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink():
            raise ValueError('Symlink paths are unsupported')


def inventory(root):
    safe_path(root)
    if root.is_symlink() or not root.is_dir() or root.stat().st_uid != os.getuid():
        raise ValueError('Snapshot root must be a directory owned by the current user')
    files = {}
    for p in sorted(root.rglob('*')):
        info = p.lstat()
        if info.st_uid != os.getuid() or stat.S_ISLNK(info.st_mode):
            raise ValueError('Foreign ownership or symlink in snapshot source')
        if not (stat.S_ISREG(info.st_mode) or stat.S_ISDIR(info.st_mode)):
            raise ValueError('Unsupported file type in snapshot source')
        if p.is_file():
            files[str(p.relative_to(root))] = {'sha256': digest(p), 'size': info.st_size,
                                             'mode': stat.S_IMODE(info.st_mode) & 0o700}
    return files


def stopped(home):
    # Operator acknowledgement is mandatory; this additional check catches own
    # active processes even if they use a nonstandard service/unit name.
    needle = os.fsencode(str(home))
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit() or int(proc.name) == os.getpid():
            continue
        try:
            if proc.stat().st_uid != os.getuid():
                continue
            command = (proc / 'cmdline').read_bytes().split(b'\0')
            environment = (proc / 'environ').read_bytes().split(b'\0')
            if b'LAIN_HOME=' + needle in environment or any(
                    value.startswith(needle + b'/app/') for value in command):
                raise ValueError('An active process uses this LAIN_HOME; stop it first')
        except (FileNotFoundError, ProcessLookupError):
            pass
        except PermissionError:
            # Other sandboxed user processes may not expose their environment;
            # the mandatory stopped-state acknowledgement remains necessary.
            continue


def outside(source, target):
    safe_path(source)
    safe_path(target)
    if target == source or source in target.parents:
        raise ValueError('Destination must be outside source')
    if target.exists() or target.is_symlink():
        raise ValueError('Destination must not exist')
    if not target.parent.is_dir():
        raise ValueError('Destination parent must exist')


def backup(home, destination):
    outside(home, destination)
    stopped(home)
    before = inventory(home)
    temporary = Path(tempfile.mkdtemp(prefix='.lain-backup-', dir=destination.parent))
    try:
        shutil.copytree(home, temporary / 'state', copy_function=shutil.copy2)
        after = inventory(home)
        copied = inventory(temporary / 'state')
        if before != after or before != copied:
            raise ValueError('State changed during snapshot; stop all writers and retry')
        for p in (temporary / 'state').rglob('*'):
            p.chmod(0o700 if p.is_dir() else before[str(p.relative_to(temporary / 'state'))]['mode'])
        (temporary / 'state').chmod(0o700)
        (temporary / 'snapshot.json').write_text(json.dumps({
            'format': 1, 'kind': 'lain-stopped-state', 'files': before,
            'credentialPolicy': 'restore-blocked-until-explicit-reconnect-or-generation-recovery',
            'scope': 'Entire LAIN_HOME, including app; external launcher and systemd files excluded'
        }, indent=2) + '\n')
        (temporary / 'snapshot.json').chmod(0o600)
        temporary.rename(destination)
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)


def restore(source, home):
    outside(source, home)
    stopped(home)
    # Include the manifest in ownership/type validation before trusting it.
    inventory(source)
    manifest = json.loads((source / 'snapshot.json').read_text())
    if manifest.get('format') != 1 or manifest.get('kind') != 'lain-stopped-state':
        raise ValueError('Unsupported snapshot format')
    files = inventory(source / 'state')
    if files != manifest['files']:
        raise ValueError('Snapshot integrity mismatch')
    temporary = Path(tempfile.mkdtemp(prefix='.lain-restore-', dir=home.parent))
    try:
        shutil.copytree(source / 'state', temporary / 'state')
        if inventory(temporary / 'state') != files:
            raise ValueError('Restore copy integrity mismatch')
        marker = temporary / 'state/restore-requires-reconnect'
        marker.write_text(json.dumps({'format': 1, 'restoreId': str(uuid.uuid4()),
                                      'reason': 'rotating-credentials-require-explicit-recovery'}, sort_keys=True) + '\n')
        marker.chmod(0o600)
        (temporary / 'state').rename(home)
    finally:
        shutil.rmtree(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('backup', 'restore', 'inspect'))
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path, nargs='?')
    parser.add_argument('--stopped', action='store_true', help='confirm every writer/host/core has been stopped')
    args = parser.parse_args()
    os.umask(0o077)
    # Resolve parent only so symlink roots cannot evade ownership validation.
    source = Path(os.path.abspath(args.source))
    destination = Path(os.path.abspath(args.destination)) if args.destination else None
    if args.action == 'inspect':
        files = inventory(source)
        print(json.dumps({'files': len(files), 'bytes': sum(v['size'] for v in files.values()),
                          'scope': 'all files; no retention/deletion performed'}))
        return
    if not args.stopped or destination is None:
        parser.error('backup/restore require destination and --stopped acknowledgement')
    if args.action == 'backup':
        backup(source, destination)
    else:
        restore(source, destination)
    print('Created private ' + args.action + ': ' + str(destination))


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, json.JSONDecodeError) as error:
        print('Maintenance refused: ' + str(error), file=sys.stderr)
        sys.exit(2)
