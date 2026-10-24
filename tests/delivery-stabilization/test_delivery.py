#!/usr/bin/env python3
"""Synthetic filesystem-only delivery checks; no dotnet, services, network or real state."""
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]


def run(*command, env=None, ok=True):
    result = subprocess.run(command, env=env, cwd='/', text=True, capture_output=True)
    assert (result.returncode == 0) == ok, result.stdout + result.stderr
    return result


def sums(package):
    (package / 'SHA256SUMS').write_text(''.join(
        hashlib.sha256(p.read_bytes()).hexdigest() + '  ' + str(p.relative_to(package)) + '\n'
        for p in sorted(package.rglob('*')) if p.is_file() and p.name != 'SHA256SUMS'))


def package_at(path, mode):
    path.mkdir()
    for name in ['cli/Lain.Cli', 'host/Lain.Host', 'desktop/Lain.Desktop'] + (
            ['proxy/lain-proxy'] if mode == 'gateway' else []):
        p = path / name
        p.parent.mkdir(exist_ok=True)
        p.write_text('#!/bin/sh\nexit 0\n')
        p.chmod(0o700)
    shutil.copytree(ROOT / 'packaging', path / 'packaging')
    brand = path / 'desktop/Assets/Brand'
    brand.mkdir(parents=True)
    (brand / 'lain-cat.png').write_bytes(b'synthetic image')
    (path / 'package-metadata.json').write_text(json.dumps({'format': 1, 'runtimeMode': mode}))
    sums(path)


with tempfile.TemporaryDirectory(prefix='lain-delivery-') as temporary:
    base = Path(temporary)
    monitor, gateway = base / 'monitor', base / 'gateway'
    package_at(monitor, 'monitor')
    package_at(gateway, 'gateway')
    env = dict(os.environ, LAIN_HOME=str(base / 'home'), LAIN_SYSTEMD_DIR=str(base / 'units'),
               LAIN_DESKTOP_DATA_DIR=str(base / 'desktop'))
    install = ['bash', str(ROOT / 'scripts/install-linux.sh')]
    prefix, home = base / 'prefix', base / 'home'
    run(*install, str(monitor), str(prefix), env=env)
    assert (home / 'initial-runtime-mode').read_text() == 'monitor\n'
    assert not (home / 'app/proxy').exists()
    (home / 'accounts.json').write_text('{"synthetic":"retained"}')
    (home / 'configuration.json').write_text('{"runtimeMode":"monitor"}')
    (home / 'history.db').write_bytes(b'synthetic history')
    before = {p.name: p.read_bytes() for p in home.iterdir() if p.is_file()}
    run(*install, str(gateway), str(prefix), env=env)
    assert (home / 'app/proxy/lain-proxy').exists()
    assert (home / 'app.previous/.lain-package').exists()
    assert all((home / name).read_bytes() == data for name, data in before.items())
    run(*install, str(monitor), str(prefix), env=env, ok=False)
    assert (home / 'app/proxy/lain-proxy').exists()
    run(*install, str(gateway), str(prefix), env=env, ok=False)  # rollback copy retained
    # Failed package never changes existing installation.
    (gateway / 'cli/Lain.Cli').write_text('tampered')
    run(*install, str(gateway), str(prefix), env=env, ok=False)
    assert all((home / name).read_bytes() == data for name, data in before.items())
    # Legacy Gateway fresh installation defaults to Gateway and writes no user config.
    (gateway / 'package-metadata.json').unlink()
    sums(gateway)
    legacy_home = base / 'legacy-home'
    legacy_env = dict(env, LAIN_HOME=str(legacy_home), LAIN_SYSTEMD_DIR=str(base / 'legacy-units'),
                      LAIN_DESKTOP_DATA_DIR=str(base / 'legacy-desktop'))
    run(*install, str(gateway), str(base / 'legacy-prefix'), env=legacy_env)
    assert (legacy_home / 'initial-runtime-mode').read_text() == 'gateway\n'
    # Strict package inventory rejects omitted/unlisted and traversal paths.
    (monitor / 'extra').write_text('unlisted')
    run(*install, str(monitor), str(base / 'unused-prefix'), env=env, ok=False)
    (monitor / 'extra').unlink()
    (monitor / 'SHA256SUMS').write_text('0' * 64 + '  ../escape\n')
    run(*install, str(monitor), str(base / 'unused-prefix'), env=env, ok=False)
    # Backup, explicit full-state restore for rollback, corruption and overwrite refusal.
    maintenance = ['python3', str(ROOT / 'scripts/lain-maintenance.py')]
    # Parent tool runner intentionally has no LAIN_HOME; no app process is started.
    snapshot, restored = base / 'snapshot', base / 'restored'
    writer = subprocess.Popen(['python3', '-c', 'import time; time.sleep(30)'], env=env)
    try:
        run(*maintenance, 'backup', str(home), str(base / 'active-refused'), '--stopped', ok=False)
        assert not (base / 'active-refused').exists()
    finally:
        writer.terminate()
        writer.wait()
    run(*maintenance, 'backup', str(home), str(snapshot), '--stopped')
    run(*maintenance, 'restore', str(snapshot), str(restored), '--stopped')
    assert (restored / 'restore-requires-reconnect').is_file()
    second = base / 'restored-again'
    run(*maintenance, 'restore', str(snapshot), str(second), '--stopped')
    assert (second / 'restore-requires-reconnect').read_bytes() != (restored / 'restore-requires-reconnect').read_bytes()
    assert (restored / 'accounts.json').read_bytes() == (home / 'accounts.json').read_bytes()
    assert (restored / 'history.db').read_bytes() == (home / 'history.db').read_bytes()
    assert (restored / 'app.previous/.lain-package').is_file()
    run(*maintenance, 'restore', str(snapshot), str(restored), '--stopped', ok=False)
    run(*maintenance, 'backup', str(home), str(base / 'unconfirmed'), ok=False)
    (snapshot / 'state/accounts.json').write_text('corrupt')
    run(*maintenance, 'restore', str(snapshot), str(base / 'corrupt-restore'), '--stopped', ok=False)
    assert not (base / 'corrupt-restore').exists()
    (home / 'foreign-link').symlink_to('/etc/passwd')
    run(*maintenance, 'backup', str(home), str(base / 'unsafe'), '--stopped', ok=False)
    assert not (base / 'unsafe').exists()

# Parse every embedded Python block, including Gateway paths not run in fixture tests.
source = (ROOT / 'scripts/publish-linux.sh').read_text()
for delimiter, block in re.findall(r"<<'([A-Z]+)'\n(.*?)\n\1", source, re.S):
    ast.parse(block, filename='publish-linux.sh:' + delimiter)
print('PASS fresh Monitor/legacy Gateway, upgrade/data retention, guarded removal, package integrity, stopped snapshot/recovery-marked rollback and corruption checks')
