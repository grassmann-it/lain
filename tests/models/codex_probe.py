"""Exercise the installed Codex against an isolated home and a synthetic loopback server.

No real credentials, base Codex config, provider, or persistent daemon are used.
Run: python3 tests/models/codex_probe.py [--operations-source /path/to/Operations]
"""
import argparse
import http.server
import json
import os
import pathlib
import subprocess
import tempfile
import threading

parser = argparse.ArgumentParser()
parser.add_argument("--operations-source")
options = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[2]
observed = []


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        observed.append((self.path, self.headers.get("Authorization"), body.get("model")))
        message = {"id": "msg_test", "type": "message", "role": "assistant", "status": "completed", "content": [{"type": "output_text", "text": "ok", "annotations": []}]}
        response = {"id": "resp_test", "object": "response", "created_at": 1, "status": "completed", "model": "lain-test", "output": [message], "usage": {"input_tokens": 1, "output_tokens": 1, "total_tokens": 2}}
        events = [
            {"type": "response.created", "response": dict(response, status="in_progress", output=[])},
            {"type": "response.output_item.added", "output_index": 0, "item": dict(message, status="in_progress", content=[])},
            {"type": "response.output_text.delta", "item_id": "msg_test", "output_index": 0, "content_index": 0, "delta": "ok"},
            {"type": "response.output_item.done", "output_index": 0, "item": message},
            {"type": "response.completed", "response": response},
        ]
        payload = "".join("event: " + e["type"] + "\ndata: " + json.dumps(e) + "\n\n" for e in events).encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


with tempfile.TemporaryDirectory(prefix="lain-codex-probe-") as temporary:
    home = pathlib.Path(temporary)
    helper = home / "lain-helper"
    helper.write_text("#!/usr/bin/python3\nimport pathlib,sys\npathlib.Path(" + repr(str(home / "helper-args.json")) + ").write_text(__import__('json').dumps(sys.argv[1:]))\nprint('synthetic-lain-token')\n")
    helper.chmod(0o700)
    baseline = 'model = "base-sentinel-never-request"\n'
    (home / "config.toml").write_text(baseline)
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        generate = ["dotnet", "run", "--project", str(root / "tests/models/Models.Tests.csproj")]
        if options.operations_source:
            generate.append("-p:OperationsSource=" + str(pathlib.Path(options.operations_source).resolve()))
        generate += ["--", str(home), str(helper), f"http://127.0.0.1:{server.server_port}/v1"]
        subprocess.run(generate, check=True, cwd=root, capture_output=True, text=True, timeout=60)
        environment = dict(os.environ, CODEX_HOME=str(home))
        result = subprocess.run(["codex", "--no-daemon", "--profile", "lain", "--strict-config", "exec", "--skip-git-repo-check", "--ephemeral", "--sandbox", "read-only", "--json", "Reply ok without tools."],
                                cwd=home, env=environment, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, result.stderr[-4000:]
        assert observed == [("/v1/responses", "Bearer synthetic-lain-token", "lain-test")], observed
        assert json.loads((home / "helper-args.json").read_text()) == ["auth", "token", "--audience", "codex"]
        assert (home / "config.toml").read_text() == baseline
        assert '"text":"ok"' in result.stdout.replace(" ", ""), result.stdout[-2000:]
        print("PASS Codex 0.162.1: strict config, layered external profile, custom catalog, exact command auth args, synthetic Responses stream; base config unchanged")
    finally:
        server.shutdown()
        server.server_close()
