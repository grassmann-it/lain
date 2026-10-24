#!/usr/bin/python3
"""Synthetic CLI only: no network, user config, environment credentials or providers."""
import json, os, pathlib, subprocess, sys, time
args = sys.argv[1:]
if args == ['--version']:
    print('lain-cli-fixture 1.0')
    raise SystemExit(0)
root = pathlib.Path(os.environ['LAIN_CLI_FIXTURE_DIR'])
mode = args[-1]
if args[0] == 'inspect-env':
    print(json.dumps(dict(os.environ)))
elif args[0] == 'overflow':
    sys.stdout.write('fixture-secret-' * 90000)
elif args[0] == 'stderr-overflow':
    sys.stderr.write('fixture-secret-' * 90000)
elif args[0] == 'exit-error':
    sys.stderr.write('fixture-private-stderr')
    raise SystemExit(17)
elif args[0] == 'sleep':
    time.sleep(30)
elif args[0] == 'fork':
    child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(30)'])
    (root / 'child.pid').write_text(str(child.pid))
    os.chmod(root / 'child.pid', 0o600)
    time.sleep(30)
elif args[0] == 'login':
    if mode == 'sleep':
        time.sleep(30)
    value = {'kind': 'api-key', 'apiKey': 'fixture-private-api-key', 'team': 'different-team' if mode == 'wrong-scope' else 'fixture-team'}
    path = root / 'result.json'
    with os.fdopen(os.open(path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600), 'w') as output:
        output.write('{' if mode == 'malformed' else json.dumps(value))
    print('Browser callback code fixture-private-code; never display raw output')
else:
    print(json.dumps({'apiKey': 'fixture-private-api-key'}))
