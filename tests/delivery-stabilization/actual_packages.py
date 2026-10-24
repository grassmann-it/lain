#!/usr/bin/env python3
"""Plan or qualify real packages in new temporary state; never uses systemd.

Only --execute installs packages/starts their processes. No inference, provider
reads, real credentials, ordinary Codex config, or existing state is used.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import signal
import socket
import sqlite3
import subprocess
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, ProxyHandler, build_opener

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('owned_upgrade', ROOT / 'tests/acceptance/upgrade_probe.py')
upgrade = importlib.util.module_from_spec(spec)
spec.loader.exec_module(upgrade)
SOURCE = '11111111-2222-4333-8444-555555555555'
OPENER = build_opener(ProxyHandler({}), upgrade.NoRedirect())


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def package_info(path, manifest_hash, mode, core_hash=None):
    path = upgrade.safe_path(path)
    if mode == 'gateway':
        info = upgrade.package_check(path, manifest_hash, core_hash, True)
    else:
        require(upgrade.digest(path / 'SHA256SUMS') == manifest_hash, 'Monitor manifest pin differs')
        declared = {}
        for line in (path / 'SHA256SUMS').read_text().splitlines():
            m = re.fullmatch(r'([0-9a-f]{64})  (.+)', line)
            require(m is not None, 'Invalid Monitor manifest')
            name = m[2].removeprefix('./')
            p = Path(name)
            require(not p.is_absolute() and '..' not in p.parts and name not in declared, 'Unsafe Monitor manifest')
            declared[name] = m[1]
        actual = {name: record['sha256'] for name, record in upgrade.fingerprint(path).items()
                  if record['sha256'] is not None and name != 'SHA256SUMS'}
        require(actual == declared, 'Monitor manifest coverage differs')
        require(not any((path / name).exists() for name in ('proxy', 'core-source-provenance', 'notices/go'))
                and not any(p.name == 'lain-proxy' for p in path.rglob('*')), 'Monitor contains Core inputs/artifacts')
        info = {'manifestSha256': manifest_hash, 'coreSha256': None}
    metadata = path / 'package-metadata.json'
    if metadata.is_file():
        require(json.loads(metadata.read_text())['runtimeMode'] == mode, 'Package runtime mode differs')
    else:
        require(mode == 'gateway', 'Monitor requires explicit metadata')
    for name in ('cli/Lain.Cli', 'host/Lain.Host', 'desktop/Lain.Desktop'):
        require(os.access(path / name, os.X_OK), 'Application component is not executable')
    build = json.loads((path / 'build-info.json').read_text())
    return dict(info, sourceCommit=build.get('commit'), components=build.get('components'), runtimeMode=mode)


def write(home, name, content):
    path = home / name
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    path.write_text(content if isinstance(content, str) else json.dumps(content))
    path.chmod(0o600)


def seed_history(home):
    # A synthetic v1 database intentionally exercises real Host migration.
    with sqlite3.connect(home / 'usage.db') as db:
        db.executescript((ROOT / 'src/Lain.Host/Usage/Migrations/001_usage.sql').read_text())
        db.execute('INSERT INTO sources VALUES (?,?,?,?,?)', (SOURCE, 2, 2, 1792756800000, 0))
        db.execute('''INSERT INTO attempts (source_id,attempt_id,request_id,account_id,credential_id,route_id,provider,
            requested_model,model,started_ms,finished_ms,status_code,state,usage_reported,input_tokens,output_tokens,
            cached_tokens,cache_creation_tokens,reasoning_tokens,total_tokens,duration_ms,first_token_ms)
            VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)''',
            (SOURCE, 'synthetic-attempt', 'synthetic-request', 'static-fixture', 'synthetic-credential', None, 'fixture-static',
             'synthetic-model', 'synthetic-model', 1792756800000, 1792756801000, 200, 'completed', 1,
             72000, 9986, 18000, None, 2000, 81986, 1000, 100))
    (home / 'usage.db').chmod(0o600)
    events = []
    for seq in (1, 2):
        events.append(json.dumps({'schemaVersion': 1, 'sourceId': SOURCE, 'sequence': seq,
            'eventId': SOURCE + ':' + str(seq), 'kind': 'usage', 'requestId': 'synthetic-request',
            'attemptId': 'synthetic-attempt', 'accountId': 'static-fixture', 'credentialId': 'synthetic-credential',
            'provider': 'fixture-static', 'requestedModel': 'synthetic-model', 'model': 'synthetic-model',
            'observedAt': '2026-10-23T12:00:01Z', 'outcome': 'success', 'usageReported': True,
            'inputTokens': 72000, 'outputTokens': 9986, 'totalTokens': 81986, 'statusCode': 200}))
    write(home, 'proxy/events.jsonl', '\n'.join(events) + '\n')
    write(home, 'proxy/events.jsonl.source', SOURCE)


def seed_configuration(home, mode, rotating=False):
    accounts = []
    if rotating:
        accounts = [dict(Id='legacy-owned', Name='Synthetic owned', Provider='codex', Enabled=True,
                         CredentialKind='codex-owned', State='unverified'),
                    dict(Id='static-fixture', Name='Synthetic static', Provider='fixture-static', Enabled=True,
                         CredentialKind='api-key', State='unverified')]
        write(home, 'credentials/legacy-owned.json', dict(kind='codex-owned', provider='codex',
            ownedAuthFile='synthetic-owned.json', accountId='fixture-subject', expiresAt='2099-01-01T00:00:00Z'))
        write(home, 'proxy/auth-owned/synthetic-owned.json', dict(type='codex', account_id='fixture-subject',
            access_token='synthetic-access', refresh_token='synthetic-spent-refresh', expired='2099-01-01T00:00:00Z'))
        write(home, 'credentials/static-fixture.json', dict(kind='api-key', apiKey='synthetic-static', provider='fixture-static',
            coreProvider='fixture-static', sourceSection='openai-compatibility', baseUrl='https://provider.example.test/v1'))
    write(home, 'configuration.json', dict(Revision=11, AppliedRevision=2, Accounts=accounts,
        Routes=[], Profiles=[], RuntimeMode=mode))
    write(home, 'active-profile.json', {'id': 'legacy', 'revision': 2})
    write(home, 'proxy/runtime.json', {'valid': True, 'revision': 2})
    write(home, 'proxy/bindings.json', {'accounts': {}, 'routes': {}})
    write(home, 'ui/workspace-preferences.json', {'version': 1, 'level': 'Simple', 'densities': {'Accounts': 'Compact'},
        'minimizeToTray': False, 'closeToTray': True, 'notificationsEnabled': False})


def free_ports():
    sockets = []
    try:
        for _ in range(2):
            s = socket.socket(); s.bind(('127.0.0.1', 0)); sockets.append(s)
        return tuple(s.getsockname()[1] for s in sockets)
    finally:
        for s in sockets: s.close()


def environment(root, home):
    return dict(os.environ, LAIN_HOME=str(home), LAIN_SYSTEMD_DIR=str(root / 'units'),
        LAIN_DESKTOP_DATA_DIR=str(root / 'xdg-data'), XDG_CONFIG_HOME=str(root / 'xdg-config'),
        XDG_DATA_HOME=str(root / 'xdg-data'), XDG_RUNTIME_DIR=str(root / 'xdg-runtime'))


def command(arguments, env):
    result = subprocess.run(arguments, cwd='/', env=env, capture_output=True, timeout=90)
    require(result.returncode == 0, 'Isolated helper command failed: ' + Path(arguments[0]).name)
    return result.stdout


def install(package, root, home):
    env = environment(root, home)
    command(['bash', str(package / 'install-linux.sh'), str(package), str(root / 'prefix')], env)
    return env


class Host:
    def __init__(self, root, home):
        self.home = home
        self.host_port, self.core_port = free_ports()
        self.env = dict(environment(root, home), LAIN_REFERENCE_QUALIFICATION='1',
            LAIN_REFERENCE_HOST_PORT=str(self.host_port), LAIN_REFERENCE_PROXY_PORT=str(self.core_port),
            LAIN_REFERENCE_DISABLE_QUOTA='1', LAIN_PROXY_PATH=str(home / 'app/proxy/lain-proxy'))
        self.process = None

    def api(self, path, method='GET'):
        token = (self.home / 'credentials/host.token').read_text().strip()
        request = Request(f'http://127.0.0.1:{self.host_port}/api/v1' + path,
            data=b'{}' if method == 'POST' else None, method=method,
            headers={'Authorization': 'Bearer ' + token, 'Content-Type': 'application/json'})
        with OPENER.open(request, timeout=10) as response: return json.load(response)

    def __enter__(self):
        self.process = subprocess.Popen([str(self.home / 'app/host/Lain.Host')], env=self.env, cwd='/',
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        try:
            deadline = time.monotonic() + 35
            while time.monotonic() < deadline:
                require(self.process.poll() is None, 'Isolated Host exited before readiness')
                try:
                    self.status = self.api('/status')
                    return self
                except (OSError, HTTPError): time.sleep(.1)
            raise RuntimeError('Isolated Host readiness deadline exceeded')
        except BaseException:
            self.__exit__(None, None, None)
            raise

    def __exit__(self, *_):
        if self.process is None: return
        self.process.terminate()
        try: self.process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            os.killpg(self.process.pid, signal.SIGKILL); self.process.wait(timeout=5)
        # Never discover/kill any process by name or occupied port.


def state_assertions(host):
    config = host.api('/configuration')
    require((config['revision'], config['appliedRevision']) == (11, 2), 'Desired/applied11/2 changed')
    usage = host.api('/usage?from=2026-10-23T00:00:00Z&to=2026-10-24T00:00:00Z&timeZone=UTC')
    require(usage['totals']['attempts'] == 1 and usage['totals']['tokens']['total'] == 81986, 'Historical tokens changed')
    require(usage['cursor'] == 2, 'Historical cursor changed')


def execute(args):
    checks = []
    with tempfile.TemporaryDirectory(prefix='lain-release-qualification-') as directory:
        root = Path(directory); root.chmod(0o700)
        # Fresh Monitor package: no Core binary exists before or after Host start.
        monitor_root, monitor_home = root / 'monitor', root / 'monitor/state'
        env = install(args.monitor_package, monitor_root, monitor_home)
        require((monitor_home / 'initial-runtime-mode').read_text().strip() == 'monitor', 'Fresh mode not Monitor')
        with Host(monitor_root, monitor_home) as host:
            require(host.status['runtimeMode'] == 'monitor' and not host.status['proxyReady'], 'Fresh Monitor starts Core')
            require(not (monitor_home / 'app/proxy').exists(), 'Fresh Monitor installed Core')
            with socket.socket() as probe:
                require(probe.connect_ex(('127.0.0.1', host.core_port)) != 0, 'Monitor exposed an inference listener')
            command([str(monitor_root / 'prefix/bin/lain'), '--help'], env)
            client_env = dict(host.env)
            client_env.pop('LAIN_HOME')  # Installed launcher must restore its selected private home.
            cli_status = json.loads(command([str(monitor_root / 'prefix/bin/lain'), 'status', '--json'], client_env))
            require(cli_status['runtimeMode'] == 'monitor', 'Installed CLI did not discover private Monitor Host')
        with Host(monitor_root, monitor_home) as host:
            require(host.status['runtimeMode'] == 'monitor' and not host.status['proxyReady'], 'Monitor restart changed intent')
        checks.append('fresh actual Monitor install/start/restart without Core; installed launcher help/status')
        install(args.gateway_package, monitor_root, monitor_home)
        with Host(monitor_root, monitor_home) as host:
            require(host.status['runtimeMode'] == 'monitor' and not host.status['proxyReady'], 'Gateway package update changed Monitor intent')
        checks.append('actual Monitor-to-Gateway package update retains Monitor intent and leaves Core stopped')
        # Baseline application installs into private paths; never execute its old Host.
        gateway_root, gateway_home = root / 'gateway', root / 'gateway/state'
        env = install(args.previous_gateway, gateway_root, gateway_home)
        seed_configuration(gateway_home, 'gateway'); seed_history(gateway_home)
        host = Host(gateway_root, gateway_home)
        write(gateway_home, 'proxy/config.yaml', f'host: "127.0.0.1"\nport: {host.core_port}\nauth-dir: "{gateway_home / "proxy/auth"}"\napi-keys: ["synthetic-local-inference"]\n')
        old_config = (gateway_home / 'configuration.json').read_bytes()
        runtime_files = {p: (gateway_home / p).read_bytes() for p in
                         ('active-profile.json', 'proxy/runtime.json', 'proxy/config.yaml', 'proxy/bindings.json')}
        install(args.gateway_package, gateway_root, gateway_home)
        require((gateway_home / 'app.previous/.lain-package').exists(), 'Baseline application rollback copy missing')
        require(all((gateway_home / p).read_bytes() == data for p, data in runtime_files.items()),
                'Package update changed legacy runtime configuration')
        with host:
            require(host.status['proxyReady'], 'Updated qualified Gateway Core did not start')
            state_assertions(host)
            children = (Path('/proc') / str(host.process.pid) / 'task' / str(host.process.pid) / 'children').read_text().split()
            require(any(upgrade.digest(Path('/proc') / pid / 'exe') == args.gateway_core_sha256 for pid in children),
                    'Running child does not match qualified updated Core')
            require((host.status.get('application') or {}).get('state') == 'legacy-preserved', 'Legacy runtime not preserved')
            require(host.status.get('activeProfileId') is None, 'Unknown legacy profile falsely inferred')
        require((gateway_home / 'configuration.json').read_bytes() == old_config, 'Gateway startup changed saved configuration')
        require(all((gateway_home / p).read_bytes() == data for p, data in runtime_files.items()), 'Legacy runtime metadata changed')
        checks.append('actual Gateway update starts updated Core preserving desired11/applied2 and historical81986/cursor2')
        # A current marker-aware Monitor package contains both old owned and static grants.
        recovery_root, recovery_home = root / 'recovery', root / 'recovery/state'
        install(args.monitor_package, recovery_root, recovery_home)
        seed_configuration(recovery_home, 'monitor', rotating=True); seed_history(recovery_home)
        snapshot, restored = root / 'snapshot', root / 'restored-state'
        helper = recovery_home / 'app/packaging/lain-maintenance.py'
        command(['python3', str(helper), 'backup', str(recovery_home), str(snapshot), '--stopped'], environment(root, recovery_home))
        command(['python3', str(helper), 'restore', str(snapshot), str(restored), '--stopped'], environment(root, recovery_home))
        require((restored / 'restore-requires-reconnect').exists(), 'Restore recovery marker absent')
        with Host(root / 'restored', restored) as host:
            require(host.status['runtimeMode'] == 'monitor' and not host.status['proxyReady'], 'Restored Monitor starts Core')
            state_assertions(host)
            require(host.api('/accounts/legacy-owned/check', 'POST')['state'] == 'recovery-required', 'Spent rotating grant reactivated')
            require(host.api('/accounts/static-fixture/check', 'POST')['state'] == 'unverified', 'Usable static account quarantined')
            require(not (restored / 'proxy/auth-owned/synthetic-owned.json').exists(), 'Restored Core grant remains active')
        checks.append('full stopped snapshot restores history/identity; rotating grant quarantined, static account retained')
    return checks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('monitor-package', 'gateway-package', 'previous-gateway'):
        parser.add_argument('--' + name, required=True, type=Path)
    for name in ('monitor-manifest-sha256', 'gateway-manifest-sha256', 'previous-manifest-sha256', 'gateway-core-sha256', 'previous-core-sha256'):
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--report', required=True, type=Path)
    args = parser.parse_args()
    os.umask(0o077)
    args.report = upgrade.safe_path(args.report)
    require(not args.report.exists(), 'Report path must be new')
    packages = {'monitor': package_info(args.monitor_package, args.monitor_manifest_sha256, 'monitor'),
        'gateway': package_info(args.gateway_package, args.gateway_manifest_sha256, 'gateway', args.gateway_core_sha256),
        'previous': package_info(args.previous_gateway, args.previous_manifest_sha256, 'gateway', args.previous_core_sha256)}
    require(args.gateway_core_sha256 != args.previous_core_sha256, 'Updated Core must differ from baseline')
    checks = execute(args) if args.execute else []
    args.report.write_text(json.dumps({'format': 1, 'status': 'passed' if args.execute else 'not_run',
        'planOnly': not args.execute, 'packages': packages, 'checks': checks,
        'scope': 'Isolated package/install/Host/Core/stopped restore; zero inference/provider reads/systemd/real-state access',
        'historicalBinaryDowngradeRuntime': 'not_tested: an old Host must prove marker-aware recovery first'}, indent=2) + '\n')
    print('PASS isolated package qualification' if args.execute else 'PLANNED; no install/process has run')


if __name__ == '__main__':
    try: main()
    except (RuntimeError, upgrade.GuardError, OSError, ValueError, KeyError) as error:
        # Never forward helper/Host/credential response bodies.
        print('Package qualification refused: ' + str(error), file=__import__('sys').stderr)
        raise SystemExit(2)
