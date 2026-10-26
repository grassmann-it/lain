#!/usr/bin/env python3
"""Offline user installer. No downloads, credentials, or runtime database reads."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import plistlib
import re
import shlex
import shutil
import stat
import subprocess
import sys
import tempfile

CHECKS = {'fresh-install', 'cli-path', 'desktop', 'host-start-stop',
          'status-json', 'empty-usage', 'update-preservation', 'uninstall-preservation'}


def fail(message):
    raise ValueError(message)


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def safe(path):
    if not path.is_absolute() or any(c in str(path) for c in '\r\n\0'):
        fail('Absolute single-line paths required')
    for ancestor in (path, *path.parents):
        if ancestor.is_symlink() or (hasattr(ancestor, 'is_junction') and ancestor.is_junction()):
            fail('Symlinks/reparse junctions unsupported: ' + str(ancestor))
        if ancestor.exists() and os.name == 'nt' and ancestor.lstat().st_file_attributes & 0x400:
            fail('Reparse points unsupported')


def inventory(root):
    safe(root)
    if not root.is_dir():
        fail('Package must be an extracted directory')
    listed = {}
    for line in (root / 'SHA256SUMS').read_text(encoding='utf-8').splitlines():
        m = re.fullmatch(r'([0-9a-f]{64})  ([A-Za-z0-9_./-]+)', line)
        if not m:
            fail('Invalid SHA256SUMS record')
        checksum, name = m.groups()
        p = PurePosixPath(name)
        if p.is_absolute() or '..' in p.parts or str(p) != name or name == 'SHA256SUMS':
            fail('Unsafe manifest path')
        if name.casefold() in {n.casefold() for n in listed}:
            fail('Duplicate/case-colliding manifest path')
        listed[name] = checksum
    actual = set()
    for p in root.rglob('*'):
        safe(p)
        mode = p.lstat().st_mode
        if not (stat.S_ISREG(mode) or stat.S_ISDIR(mode)):
            fail('Nonregular package entry')
        if p.is_file() and p != root / 'SHA256SUMS':
            actual.add(p.relative_to(root).as_posix())
    if actual != set(listed):
        fail('Manifest must cover every file exactly')
    for name, checksum in listed.items():
        if digest(root / name) != checksum:
            fail('Checksum mismatch: ' + name)
    return listed


def validate(root, target, machine):
    listed = inventory(root)
    # This is a separate, closed package namespace. Existing Linux packages refuse.
    allowed = ('cli/', 'host/', 'desktop/', 'proxy/', 'licenses/')
    top = {'platform-package.json', 'native-qualification.json', 'LICENSE', 'NOTICE'}
    for name in listed:
        if name not in top and not name.startswith(allowed):
            fail('Unapproved payload namespace: ' + name)
        if any(part.lower() in {'credentials', 'configuration.json', '.env', 'auth.json'}
               or part.lower().endswith(('.db', '.sqlite', '.sqlite3', '.pem', '.key'))
               for part in PurePosixPath(name).parts):
            fail('Runtime data/secret-like path forbidden')
    meta = json.loads((root / 'platform-package.json').read_text())
    rid = {'macos': 'osx', 'windows': 'win'}[target] + '-' + machine
    if meta.get('schemaVersion') != 1 or meta.get('runtime') != rid:
        fail('Native platform/architecture mismatch; Linux packages unsupported')
    if meta.get('runtimeMode') not in ('monitor', 'gateway'):
        fail('Invalid runtime mode')
    suffix = '.exe' if target == 'windows' else ''
    entries = [f'{folder}/Lain.{name}{suffix}' for folder, name in
               [('cli', 'Cli'), ('host', 'Host'), ('desktop', 'Desktop')]]
    if meta['runtimeMode'] == 'gateway':
        entries.append('proxy/lain-proxy' + suffix)
    elif any(n.startswith('proxy/') for n in listed):
        fail('Monitor must not contain Core')
    for name in entries:
        p = root / name
        with p.open('rb') as stream:
            header = stream.read(64)
            if target == 'windows':
                if header[:2] != b'MZ' or len(header) < 64:
                    fail('Expected native PE executable')
                stream.seek(int.from_bytes(header[60:64], 'little'))
                pe = stream.read(6)
                expected = b'\x64\x86' if machine == 'x64' else b'\x64\xaa'
                if pe[:4] != b'PE\0\0' or pe[4:6] != expected:
                    fail('PE architecture mismatch')
            else:
                cpu = (0x01000007 if machine == 'x64' else 0x0100000c)
                if header[:4] != b'\xcf\xfa\xed\xfe' or int.from_bytes(header[4:8], 'little') != cpu:
                    fail('Expected thin native Mach-O for target architecture')
                if not os.access(p, os.X_OK):
                    fail('Executable permission missing')
    for name in ('LICENSE', 'NOTICE'):
        if not (root / name).is_file():
            fail('Legal notices required')
    if not any(n.startswith('licenses/') for n in listed):
        fail('Third-party license inventory required')
    proof = json.loads((root / 'native-qualification.json').read_text())
    bound = {n: h for n, h in listed.items() if n != 'native-qualification.json'}
    if (proof.get('schemaVersion') != 1 or proof.get('runtime') != rid
            or proof.get('passed') is not True or proof.get('secretScanPassed') is not True
            or proof.get('files') != bound
            or set(proof.get('checks', [])) != CHECKS
            or proof.get('corePortable') is not True and meta['runtimeMode'] == 'gateway'):
        fail('Missing exact-payload native qualification/Core portability gate')
    return meta


def layout(target):
    home = Path.home()
    if target == 'macos':
        return home / 'Library/Application Support/lain-platform', home / '.local/bin'
    return Path(os.environ['LOCALAPPDATA']) / 'lain-platform', Path(os.environ['LOCALAPPDATA']) / 'lain-platform/bin'


def integration(target, root, app, bindir):
    suffix = '.exe' if target == 'windows' else ''
    env = {'LAIN_HOME': str(root / 'data'), 'ASPNETCORE_URLS': 'http://127.0.0.1:18418',
           'LAIN_PROXY_PATH': str(app / ('proxy/lain-proxy' + suffix))}
    if target == 'macos':
        wrapper = '#!/bin/sh\nset -eu\n' + ''.join(
            'export ' + k + '=' + shlex.quote(v) + '\n' for k, v in env.items())
        cli = wrapper + 'exec ' + shlex.quote(str(app / 'cli/Lain.Cli')) + ' "$@"\n'
        ui = wrapper + 'exec ' + shlex.quote(str(app / 'desktop/Lain.Desktop')) + ' "$@"\n'
        bundle = Path.home() / 'Applications/Lain.app/Contents'
        plist = {'Label': 'local.lain.platform.host',
                 'ProgramArguments': [str(app / 'host/Lain.Host')],
                 'EnvironmentVariables': env, 'WorkingDirectory': str(root / 'data'),
                 'RunAtLoad': True, 'KeepAlive': {'SuccessfulExit': False}}
        return {bindir / 'lain': cli.encode(), bundle / 'MacOS/lain': ui.encode(),
                bundle / 'Info.plist': plistlib.dumps({'CFBundleIdentifier': 'local.lain.platform',
                    'CFBundleName': 'Lain', 'CFBundleExecutable': 'lain', 'CFBundlePackageType': 'APPL'}),
                Path.home() / 'Library/LaunchAgents/local.lain.platform.host.plist': plistlib.dumps(plist)}
    # CMD rejects metacharacters even inside SET quoted values; refuse such homes.
    for p in (root, app, bindir):
        if any(c in str(p) for c in '%!^&|<>"'):
            fail('Unsupported Windows home characters')
    prefix = '@echo off\r\n' + ''.join('set "' + k + '=' + v + '"\r\n' for k, v in env.items())
    def command(exe, args=''):
        return (prefix + '"' + str(app / exe) + '" ' + args + '\r\n').encode()
    startup = Path(os.environ['APPDATA']) / 'Microsoft/Windows/Start Menu/Programs/Startup/Lain Host.cmd'
    return {bindir / 'lain.cmd': command('cli/Lain.Cli.exe', '%*'),
            Path.home() / 'Desktop/Lain.cmd': command('desktop/Lain.Desktop.exe'),
            startup: command('host/Lain.Host.exe')}


def path_update(bindir, remove=False):
    import winreg
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, 'Environment') as key:
        try:
            value, kind = winreg.QueryValueEx(key, 'Path')
        except FileNotFoundError:
            value, kind = '', winreg.REG_EXPAND_SZ
        parts = value.split(';') if value else []
        if remove:
            parts = [p for p in parts if p.casefold() != str(bindir).casefold()]
        elif str(bindir).casefold() not in [p.casefold() for p in parts]:
            parts.append(str(bindir))
        winreg.SetValueEx(key, 'Path', 0, kind, ';'.join(parts))


def offline(target):
    # Deliberately conservative: do not replace launchers while any Lain host/UI runs.
    command = ['tasklist', '/FO', 'CSV', '/NH'] if target == 'windows' else ['ps', '-axo', 'comm=']
    processes = subprocess.check_output(command, text=True).lower()
    if any(name in processes for name in ('lain.host', 'lain.desktop', 'lain-proxy')):
        fail('Stop Lain Host, Desktop and Core before installation/uninstallation')


def mutate(target, action, package=None):
    root, bindir = layout(target)
    ledger = root / 'installer-owned.json'
    safe(root)
    safe(bindir)
    if target == 'macos' and str(bindir) not in os.environ.get('PATH', '').split(os.pathsep):
        fail('Add ' + str(bindir) + ' to your shell PATH before installing')
    root.mkdir(parents=True, exist_ok=True)
    if target == 'macos':
        root.chmod(0o700)
    # Atomic directory lock; stale locks require explicit operator inspection.
    lock = root / '.installer-lock'
    lock.mkdir()
    try:
        safe(ledger)
        if (root / 'integration-pending.json').exists():
            fail('Interrupted integration: inspect integration-pending.json; no automatic overwrite')
        prior = json.loads(ledger.read_text()) if ledger.exists() else {'files': {}, 'pathAdded': False}
        for name, checksum in prior['files'].items():
            p = Path(name)
            safe(p)
            if p.exists() and (not p.is_file() or digest(p) != checksum):
                fail('Managed integration customized; preserve and resolve manually: ' + name)
        offline(target)
        if action != 'uninstall':
            mode = json.loads((package / 'platform-package.json').read_text())['runtimeMode']
            if prior.get('runtimeMode') == 'gateway' and mode == 'monitor':
                fail('Gateway-to-Monitor replacement requires explicit data migration; refused')
        if action == 'uninstall':
            for name in prior['files']:
                Path(name).unlink(missing_ok=True)
            if target == 'windows' and prior['pathAdded']:
                path_update(bindir, remove=True)
            ledger.unlink(missing_ok=True)
            print('Integration removed. All application versions and user data retained at ' + str(root))
            return
        package_id = digest(package / 'SHA256SUMS')
        versions = root / 'versions'
        safe(versions)
        versions.mkdir(exist_ok=True)
        app = versions / package_id
        safe(app)
        if not app.exists():
            with tempfile.TemporaryDirectory(prefix='.stage-', dir=root) as temp:
                stage = Path(temp) / 'app'
                shutil.copytree(package, stage)
                validate(stage, target, architecture())
                stage.rename(app)
        else:
            validate(app, target, architecture())
        files = integration(target, root, app, bindir)
        for p in files:
            safe(p)
            if p.exists() and str(p) not in prior['files']:
                fail('Foreign integration exists: ' + str(p))
        safe(root / 'data')
        (root / 'data').mkdir(exist_ok=True)
        # All conflicts checked before writes. Interrupted writes fail closed on retry.
        initial = root / 'data/initial-runtime-mode'
        safe(initial)
        if not initial.exists():
            initial.write_text(mode + '\n')
        records = {str(p): hashlib.sha256(data).hexdigest() for p, data in files.items()}
        journal = root / 'integration-pending.json'
        journal.write_text(json.dumps({'files': records}, indent=2))
        for p, data in files.items():
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)
            if target == 'macos':
                p.chmod(0o700 if p.name in ('lain',) else 0o600)
        path_added = prior['pathAdded']
        if target == 'windows':
            import winreg
            with winreg.CreateKey(winreg.HKEY_CURRENT_USER, 'Environment') as key:
                try:
                    existing = winreg.QueryValueEx(key, 'Path')[0].split(';')
                except FileNotFoundError:
                    existing = []
            path_added = path_added or str(bindir).casefold() not in [v.casefold() for v in existing]
            path_update(bindir)
        ledger.write_text(json.dumps({'files': records, 'pathAdded': path_added, 'runtimeMode': mode}, indent=2))
        journal.unlink()
        print('Installed user integration; user data and prior versions retained.')
        if target == 'macos':
            print('CLI available as lain in the configured shell PATH.')
            print('Start host: launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/local.lain.platform.host.plist')
            print('Stop before update/removal: launchctl bootout gui/$(id -u)/local.lain.platform.host')
        else:
            print('Open a new terminal/logon for user PATH. Start/stop Host via Startup/Lain Host.cmd and its console; logon starts it automatically.')
    finally:
        lock.rmdir()


def architecture():
    machine = platform.machine().lower()
    if machine in ('x86_64', 'amd64'):
        return 'x64'
    if machine in ('aarch64', 'arm64'):
        return 'arm64'
    fail('Unsupported architecture')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', required=True, choices=['macos', 'windows'])
    parser.add_argument('action', choices=['check', 'install', 'uninstall'])
    parser.add_argument('package', nargs='?', type=Path)
    args = parser.parse_args()
    if args.action != 'uninstall':
        if args.package is None:
            parser.error('package directory required')
        validate(args.package, args.platform, architecture())
    if args.action == 'check':
        print('Offline package contract passed; this is not native runtime qualification.')
        return
    if platform.system() != {'macos': 'Darwin', 'windows': 'Windows'}[args.platform]:
        fail('Installation requires the native target OS')
    if os.name != 'nt' and os.geteuid() == 0:
        fail('Run as ordinary user, not root')
    if os.name == 'nt':
        import ctypes
        if ctypes.windll.shell32.IsUserAnAdmin():
            fail('Run as ordinary user, not Administrator')
    mutate(args.platform, args.action, args.package)


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, subprocess.CalledProcessError) as error:
        print('REFUSED: ' + str(error), file=sys.stderr)
        sys.exit(2)
