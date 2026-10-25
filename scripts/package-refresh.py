#!/usr/bin/env python3
"""Replace only the complete app tree from an exact package; never services/state.

Explicit --apply --stopped is required. The old app and transaction journal are
retained under a fresh backup. No credentials, databases or integration files
are replaced, and rollback reverses application files only.
"""
import argparse
import ctypes
from contextlib import contextmanager
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import stat
import tempfile


def need(value, message):
    if not value: raise RuntimeError(message)


def safe(path):
    path = Path(path)
    need(path.is_absolute() and '..' not in path.parts, 'Absolute nontraversing paths required')
    need(not any(p.is_symlink() for p in (path, *path.parents)), 'Symlink path refused')
    return path


def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def pinned(path, expected):
    path = safe(path)
    need(re.fullmatch('[0-9a-f]{64}', expected) and sha(path) == expected, 'Input SHA256 pin differs')
    return path


def load_helper(path, expected):
    path = pinned(path, expected)
    spec = importlib.util.spec_from_file_location('full_package_core_helper', path)
    common = importlib.util.module_from_spec(spec)
    exec(compile(path.read_bytes(), str(path), 'exec'), common.__dict__)
    return common


@contextmanager
def lease(home):
    info = home.stat()
    need(info.st_uid == os.getuid() and not info.st_mode & 0o022, 'Owned protected home required')
    path = home / '.package-refresh.lock'
    fd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    try:
        details, current = os.fstat(fd), path.lstat()
        need(stat.S_ISREG(details.st_mode) and details.st_uid == os.getuid() and details.st_nlink == 1 and
             stat.S_IMODE(details.st_mode) == 0o600 and
             (details.st_dev, details.st_ino) == (current.st_dev, current.st_ino), 'Unsafe transaction lease')
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield
    finally:
        os.close(fd)


def no_payloads(common, app):
    common.refuse_running_backend(app)
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit() or int(proc.name) == os.getpid(): continue
        try:
            if proc.stat().st_uid != os.getuid(): continue
            executable = os.readlink(proc / 'exe').removesuffix(' (deleted)')
            args = (proc / 'cmdline').read_bytes().split(b'\0')
            need(not executable.startswith(str(app) + '/') and
                 not any(os.fsdecode(arg).startswith(str(app) + '/') for arg in args),
                 'Installed application process remains')
        except (FileNotFoundError, ProcessLookupError): pass
        except PermissionError:
            # The service operator performs the stronger finite platform audit.
            # Never exempt a known installed application candidate here.
            comm = (proc / 'comm').read_text().strip()
            need(comm not in ('Lain.Host', 'Lain.Cli', 'Lain.Desktop', 'lain-proxy', 'dotnet'),
                 'Unreadable application candidate; replacement refused')


def exchange(left, right):
    renameat2 = ctypes.CDLL(None, use_errno=True).renameat2
    renameat2.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    renameat2.restype = ctypes.c_int
    if renameat2(-100, os.fsencode(left), -100, os.fsencode(right), 2):
        error = ctypes.get_errno()
        raise OSError(error, os.strerror(error))


def sync_tree(common, root):
    for path in sorted(root.rglob('*')):
        if path.is_file():
            with path.open('rb') as stream: os.fsync(stream.fileno())
    for path in sorted((p for p in root.rglob('*') if p.is_dir()), reverse=True):
        common.sync_directory(path)
    common.sync_directory(root)
    common.sync_directory(root.parent)


def install(args):
    package, home, backup = safe(args.package), safe(args.home), safe(args.backup)
    pinned(package / 'SHA256SUMS', args.manifest_sha256)
    common = load_helper(args.helper, args.helper_sha256)
    target = common.verify(package)
    app = home / 'app'
    need((app / '.lain-package').is_file(), 'Managed installed application required')
    need(sha(app / 'SHA256SUMS') == args.baseline_manifest_sha256, 'Installed predecessor manifest differs')
    base = common.verify(app)
    need(home in backup.parents and app != backup and app not in backup.parents and backup.parent.is_dir() and not backup.exists(),
         'Fresh backup inside home and outside app required')
    need(package != home and home not in package.parents and package not in home.parents, 'Package/home overlap')
    print(json.dumps({'operation': 'full-app-replacement', 'apply': args.apply,
                      'baseManifestSha256': args.baseline_manifest_sha256,
                      'targetManifestSha256': args.manifest_sha256, 'serviceAction': 'none'}))
    if not args.apply: return
    need(args.stopped, 'Explicit --stopped acknowledgement required')
    os.umask(0o077)
    with lease(home):
        no_payloads(common, app)
        need(common.verify(app) == base and common.verify(package) == target, 'Inputs changed before copy')
        temporary = Path(tempfile.mkdtemp(prefix='.package-refresh-', dir=home))
        safe_to_remove = False
        try:
            prepared = temporary / 'app'
            shutil.copytree(package, prepared)
            (prepared / '.lain-package').touch(mode=0o600)
            need(common.verify(prepared) == target, 'Prepared package differs')
            no_payloads(common, app)
            need(common.verify(app) == base and common.verify(package) == target, 'Inputs changed before replacement')
            # Persist the complete prepared payload and its parent before any
            # app rename. The first journal is durable before the atomic swap.
            sync_tree(common, prepared)
            common.sync_directory(home)  # Persist the temporary root entry even for nested backups.
            backup.mkdir(mode=0o700)
            common.sync_directory(backup.parent)
            journal = {'format': 1, 'kind': 'full-app-file-transaction', 'status': 'prepared',
                       'base': base['SHA256SUMS'], 'target': target['SHA256SUMS'],
                       'activeApp': str(app), 'preparedApp': str(prepared),
                       'previousApp': str(backup / 'app'),
                       'recovery': 'Inspect exact manifests at active/prepared/previous paths; never restore credentials or start a service automatically.'}
            common.write(backup / 'transaction.json', journal)
            exchanged = False
            old_location = prepared
            try:
                no_payloads(common, app)
                need(common.verify(app) == base and common.verify(prepared) == target and
                     common.verify(package) == target, 'Inputs changed before atomic exchange')
                exchange(app, prepared)
                exchanged = True
                common.sync_directory(app.parent)
                common.sync_directory(prepared.parent)
                common.write(backup / 'transaction.json', dict(journal, status='app-exchanged'))
                need(common.verify(app) == target and common.verify(prepared) == base,
                     'Exchanged application differs')
                prepared.rename(backup / 'app')
                old_location = backup / 'app'
                common.sync_directory(prepared.parent)
                common.sync_directory(backup)
                common.write(backup / 'transaction.json', dict(journal, status='installed'))
                safe_to_remove = True
            except BaseException as error:
                try:
                    if exchanged:
                        # Atomic reverse of app files only, never a state/grant restore.
                        exchange(app, old_location)
                        common.sync_directory(app.parent)
                        common.sync_directory(old_location.parent)
                        if old_location == prepared:
                            old_location.rename(backup / 'failed-target')
                            common.sync_directory(old_location.parent)
                            common.sync_directory(backup)
                    need(common.verify(app) == base, 'Reversed application differs')
                    common.write(backup / 'transaction.json', dict(journal,
                                 status='file-transaction-reversed' if exchanged else 'failed-before-exchange', error=str(error)))
                    safe_to_remove = True
                except BaseException as reversal:
                    raise RuntimeError(str(error) + '; application reversal failed: ' + str(reversal)) from error
                raise
        finally:
            # A failed reverse may leave the only previous app at preparedApp.
            # Never let automatic temporary-directory cleanup destroy it.
            if safe_to_remove:
                shutil.rmtree(temporary)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('package', 'home', 'backup', 'helper'): parser.add_argument('--' + name, type=Path, required=True)
    for name in ('manifest-sha256', 'baseline-manifest-sha256', 'helper-sha256'): parser.add_argument('--' + name, required=True)
    parser.add_argument('--stopped', action='store_true')
    parser.add_argument('--apply', action='store_true')
    install(parser.parse_args())


if __name__ == '__main__':
    try: main()
    except (RuntimeError, OSError, ValueError, KeyError) as error:
        raise SystemExit('Package replacement refused: ' + str(error))
