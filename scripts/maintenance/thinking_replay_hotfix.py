#!/usr/bin/env python3
"""Guarded config-only mitigation for the qualified legacy CPA binary.

Scope: configured Command Code Sonnet/Opus 5.5, Responses -> Claude, adaptive
thinking with signed/redacted assistant history. No inference or service control.
Caller policy wins. Omitted/disabled thinking and direct Messages are excluded.
PyYAML is required. YAML formatting is normalized; exact original bytes are backed up.
"""
import argparse
import copy
from contextlib import contextmanager
import fcntl
import fnmatch
import hashlib
import json
import os
import stat
from pathlib import Path
import tempfile
import time
import yaml

BETA = 'thinking-binding-controls-2026-08-01'
POLICY = 'thinking.block_binding.prefix_mismatch_behavior'
QUALIFIED_BINARY_SHA256 = '8b2e25d6546e2d84ab42d2f2a843fa1efc106adef27e2b9477d1cc2171bb0cb9'
DEFAULT_BINARY = Path('/home/tomka/.local/share/easycliproxyapi/cpa-core/cli-proxy-api')
ADMIN_LOCK = Path('/home/tomka/.local/share/easycliproxyapi/.codex-proxy-admin.lock')
MODEL_NAMES = {'claude-sonnet-5-5', 'claude-opus-5-5'}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def targets(config):
    result = set()
    qualified = set()
    for account in config.get('claude-api-key', []):
        if account.get('base-url', '').rstrip('/') != 'https://api.commandcode.ai/provider':
            continue
        for model in account.get('models', []):
            if model.get('name') in MODEL_NAMES and model.get('is-compat') is True:
                alias = model.get('alias')
                if alias != 'cmd-' + model['name']:
                    raise ValueError('qualified route requires its distinct canonical cmd alias')
                result.add(alias)
                qualified.add(id(model))
    # Runtime rules also match upstream basemodel candidates. Distinct aliases
    # and collision rejection keep this config-only patch provider-scoped.
    for accounts in config.values():
        if not isinstance(accounts, list):
            continue
        for account in accounts:
            if not isinstance(account, dict):
                continue
            for model in account.get('models', []):
                if isinstance(model, dict) and id(model) not in qualified:
                    if {model.get('alias'), model.get('name')} & result:
                        raise ValueError('target alias also belongs to another route')
    return sorted(result)


def check_later_rules(config, names):
    candidates = set(names) | MODEL_NAMES
    for section in ['override', 'override-raw', 'filter']:
        for rule in config.get('payload', {}).get(section, []):
            overlaps = not rule.get('models') or any(
                any(fnmatch.fnmatchcase(name, entry.get('name', '*')) for name in candidates)
                for entry in rule.get('models', []))
            if not overlaps:
                continue
            for path in rule.get('params', {}):
                if path == 'model' or path == 'thinking' or path.startswith('thinking.'):
                    raise ValueError('later payload rule can invalidate the qualified thinking scope')


def make_rules(names):
    entries = []
    for name in names:
        for history in [
            'messages.#(role=="assistant")#.content.#(type=="thinking"&&signature!="")#.signature',
            'messages.#(role=="assistant")#.content.#(type=="redacted_thinking"&&data!="")#.data',
        ]:
            entries.append({'name': name, 'protocol': 'claude', 'from-protocol': 'responses',
                            'match': [{'thinking.type': 'adaptive'}],
                            'not-match': [{'tool_choice.type': 'any'}, {'tool_choice.type': 'tool'}],
                            'exist': [history]})
    rules = [{'models': copy.deepcopy(entries), 'params': {POLICY: 'drop_block'}}]
    missing = copy.deepcopy(entries)
    for entry in missing:
        entry['not-exist'] = ['betas']
    rules.append({'models': missing, 'params': {'betas': [BETA]}})
    arrays = copy.deepcopy(entries)
    for entry in arrays:
        entry['exist'].append('betas.#')
        entry['not-exist'] = ['betas.#(=="' + BETA + '")']
    rules.append({'models': arrays, 'params': {'betas.-1': BETA}})
    return rules


def patched_bytes(raw):
    config = yaml.safe_load(raw)
    if not isinstance(config, dict):
        raise ValueError('configuration must be a mapping')
    names = targets(config)
    if not names:
        raise ValueError('no qualified Command Code Claude 5.5 routes found')
    check_later_rules(config, names)
    payload = config.setdefault('payload', {})
    defaults = payload.setdefault('default', [])
    if not isinstance(defaults, list):
        raise ValueError('payload.default must be a list')
    changed = False
    for rule in make_rules(names):
        if rule not in defaults:
            defaults.append(rule)
            changed = True
    if not changed:
        return raw, names
    return yaml.safe_dump(config, sort_keys=False, allow_unicode=True).encode(), names


def atomic_private(path, data):
    path = Path(path)
    fd, temporary = tempfile.mkstemp(prefix='.' + path.name + '.', dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


class RecoveryRequired(RuntimeError):
    pass


def preserve_inode_write(path, expected, data, receipt):
    """Non-atomic visibility, deliberately retaining the inode watched by v7 CPA."""
    path = Path(path)
    fd = os.open(path, os.O_RDWR | os.O_NOFOLLOW)
    mutated = False
    try:
        info = os.fstat(fd)
        current = path.lstat()
        identity = (info.st_dev, info.st_ino)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or info.st_nlink != 1:
            raise ValueError('in-place target must be an owned regular file without hardlinks')
        if identity != (current.st_dev, current.st_ino):
            raise ValueError('target inode changed')
        with os.fdopen(os.dup(fd), 'rb') as stream:
            before = stream.read()
        if digest(before) != expected:
            raise ValueError('configuration hash changed before in-place write')
        # One complete write; never retry a short write. When shrinking, replace
        # the old suffix with YAML whitespace before truncation so a completed
        # write already contains valid target YAML even before ftruncate.
        wire = data + b'\n' * max(0, len(before) - len(data))
        mutated = True
        if os.pwrite(fd, wire, 0) != len(wire):
            raise OSError('short in-place write; not retried')
        os.ftruncate(fd, len(data))
        os.fsync(fd)
        current = path.lstat()
        if identity != (current.st_dev, current.st_ino) or digest(path.read_bytes()) != digest(data):
            raise OSError('post-write inode or hash verification failed')
    except (OSError, ValueError) as error:
        if mutated:
            raise RecoveryRequired('In-place write incomplete; no retry performed. Exact backup/receipt: ' + str(receipt)) from error
        raise
    finally:
        try:
            os.close(fd)
        except OSError as error:
            if mutated:
                raise RecoveryRequired('Close failed after in-place write; no retry performed. Exact backup/receipt: ' + str(receipt)) from error
            raise


def checked_read(path, expected):
    path = Path(path)
    info = path.lstat()
    if not path.is_file() or path.is_symlink() or info.st_uid != os.getuid():
        raise ValueError('configuration must be an owned regular file, without symlink')
    raw = path.read_bytes()
    if digest(raw) != expected:
        raise ValueError('configuration hash changed; refusing write')
    return raw


def check_binary(binary=DEFAULT_BINARY):
    if digest(Path(binary).read_bytes()) != QUALIFIED_BINARY_SHA256:
        raise ValueError('binary hash differs from offline-qualified version')


@contextmanager
def admin_locked(path):
    fd = os.open(path, os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'a') as handle:
        if os.fstat(handle.fileno()).st_uid != os.getuid():
            raise ValueError('admin lock must be owned by current user')
        fcntl.flock(handle, fcntl.LOCK_EX)
        yield


def apply(config_path, expected, backup_dir, binary=DEFAULT_BINARY, admin_lock=ADMIN_LOCK, preserve_config_inode=False):
    path = Path(config_path).absolute()
    backup_dir = Path(backup_dir).absolute()
    backup_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
    if backup_dir.is_symlink() or backup_dir.stat().st_uid != os.getuid() or backup_dir.stat().st_mode & 0o077:
        raise ValueError('backup directory must be owned and private')
    with admin_locked(admin_lock):
        check_binary(binary)
        raw = checked_read(path, expected)
        updated, names = patched_bytes(raw)
        if updated == raw:
            return {'changed': False, 'sha256': expected, 'models': names}
        stem = str(time.time_ns())
        backup = backup_dir / (stem + '.yaml')
        receipt = backup_dir / (stem + '.json')
        applied_backup = backup_dir / (stem + '.applied.yaml')
        atomic_private(backup, raw)
        atomic_private(applied_backup, updated)
        record = {'config': str(path), 'backup': str(backup), 'applied_backup': str(applied_backup), 'before_sha256': expected,
                  'after_sha256': digest(updated), 'models': names,
                  'admin_lock': str(Path(admin_lock).absolute()),
                  'preserve_config_inode': preserve_config_inode}
        atomic_private(receipt, json.dumps(record, indent=2).encode())
        check_binary(binary)
        checked_read(path, expected)
        if preserve_config_inode:
            preserve_inode_write(path, expected, updated, receipt)
        else:
            atomic_private(path, updated)
        return {'changed': True, 'sha256': record['after_sha256'], 'receipt': str(receipt), 'models': names}


def rollback(receipt_path, admin_lock=ADMIN_LOCK, preserve_config_inode=False):
    receipt_path = Path(receipt_path).absolute()
    info = receipt_path.lstat()
    if receipt_path.is_symlink() or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise ValueError('receipt must be owned and private')
    record = json.loads(receipt_path.read_bytes())
    if record['admin_lock'] != str(Path(admin_lock).absolute()):
        raise ValueError('rollback must use the original admin lock')
    if record.get('preserve_config_inode', False) != preserve_config_inode:
        raise ValueError('rollback must explicitly choose the original write mode')
    with admin_locked(admin_lock):
        config = Path(record['config'])
        backup = checked_read(record['backup'], record['before_sha256'])
        checked_read(record['applied_backup'], record['after_sha256'])
        checked_read(config, record['after_sha256'])
        if preserve_config_inode:
            preserve_inode_write(config, record['after_sha256'], backup, receipt_path)
        else:
            atomic_private(config, backup)
    return {'restored': True, 'sha256': record['before_sha256']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    prepare = sub.add_parser('prepare')
    prepare.add_argument('--config', required=True, type=Path)
    prepare.add_argument('--expected-sha256', required=True)
    prepare.add_argument('--output', required=True, type=Path)
    prepare.add_argument('--binary', type=Path, default=DEFAULT_BINARY)
    change = sub.add_parser('apply')
    change.add_argument('--config', required=True, type=Path)
    change.add_argument('--expected-sha256', required=True)
    change.add_argument('--backup-dir', required=True, type=Path)
    change.add_argument('--binary', type=Path, default=DEFAULT_BINARY)
    change.add_argument('--admin-lock', type=Path, default=ADMIN_LOCK)
    change.add_argument('--preserve-config-inode', action='store_true', help='non-atomic in-place write for the legacy file watcher')
    restore = sub.add_parser('rollback')
    restore.add_argument('--receipt', required=True, type=Path)
    restore.add_argument('--admin-lock', type=Path, default=ADMIN_LOCK)
    restore.add_argument('--preserve-config-inode', action='store_true')
    args = parser.parse_args()
    if args.action == 'prepare':
        if args.output.exists() or args.output.is_symlink() or args.output.absolute() == args.config.absolute():
            raise ValueError('prepare output must be a new path')
        check_binary(args.binary)
        raw = checked_read(args.config, args.expected_sha256)
        updated, names = patched_bytes(raw)
        atomic_private(args.output, updated)
        result = {'changed': updated != raw, 'sha256': digest(updated), 'models': names}
    elif args.action == 'apply':
        result = apply(args.config, args.expected_sha256, args.backup_dir, args.binary, args.admin_lock, args.preserve_config_inode)
    else:
        result = rollback(args.receipt, args.admin_lock, args.preserve_config_inode)
    print(json.dumps(result))


if __name__ == '__main__':
    try:
        main()
    except RecoveryRequired as error:
        raise SystemExit(str(error))
    except (ValueError, OSError, yaml.YAMLError) as error:
        # Do not include configuration content or parser snippets in errors.
        raise SystemExit('Hotfix refused: ' + type(error).__name__)
