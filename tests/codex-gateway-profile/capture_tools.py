#!/usr/bin/env python3
"""Capture only tool metadata from installed Codex against a disposable loopback provider.

Never reads personal Codex config, auth, history or MCP/plugin state. No Core is
started. The only bearer value is a fixed synthetic value in the child env.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def capture(binary, temporary_root, apply_patch, *, omit_apply_patch=False, multi_agent=True):
    captured = []

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
            tools = body.get("tools", [])
            captured.append({"path": self.path, "count": len(tools), "tools": [
                {"type": tool.get("type"), "name": tool.get("name", tool.get("function", {}).get("name"))}
                for tool in tools]})
            payload = {"id": "resp_fixture", "object": "response", "status": "completed", "model": "fixture-tools",
                       "output": [{"type": "message", "id": "msg_fixture", "role": "assistant", "status": "completed",
                                   "content": [{"type": "output_text", "text": "fixture", "annotations": []}]}],
                       "usage": {"input_tokens": 1, "output_tokens": 1, "total_tokens": 2}}
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Connection", "close")
            self.end_headers()
            self.wfile.write(("event: response.completed\ndata: " + json.dumps({"type": "response.completed", "response": payload}) + "\n\n").encode())
            self.wfile.flush()

    with tempfile.TemporaryDirectory(prefix="codex-tools-", dir=temporary_root) as directory:
        root = Path(directory)
        home = root / "home"
        home.mkdir()
        workspace = root / "workspace"
        workspace.mkdir()
        temp = root / "tmp"
        temp.mkdir()
        model = {"slug": "fixture-tools", "display_name": "Fixture Tools", "description": "Local synthetic only",
                 "default_reasoning_level": "none", "supported_reasoning_levels": [], "shell_type": "unified_exec",
                 "visibility": "list", "supported_in_api": True, "priority": 0, "base_instructions": "Reply with fixture.",
                 "supports_reasoning_summaries": False, "support_verbosity": False,
                 "truncation_policy": {"mode": "tokens", "limit": 10000}, "experimental_supported_tools": [],
                 "input_modalities": ["text"], "apply_patch_tool_type": apply_patch,
                 "supports_search_tool": False, "tool_mode": None}
        if omit_apply_patch:
            model.pop("apply_patch_tool_type")
        catalog = root / "models.json"
        catalog.write_text(json.dumps({"models": [model]}))
        with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            (home / "config.toml").write_text(
                'model="fixture-tools"\nmodel_provider="fixture"\nweb_search="disabled"\n'
                'approval_policy="never"\nsandbox_mode="read-only"\n'
                'model_catalog_json=' + json.dumps(str(catalog)) + '\n'
                '[model_providers.fixture]\nname="Loopback only"\nwire_api="responses"\n'
                'base_url="http://127.0.0.1:' + str(server.server_port) + '/v1"\n'
                'env_key="LAIN_SYNTHETIC_CODEX_KEY"\nrequest_max_retries=0\nstream_max_retries=0\n'
                + ('[features]\nmulti_agent=false\n' if not multi_agent else ''))
            env = {"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "LANG": "C.UTF-8", "HOME": str(home),
                   "CODEX_HOME": str(home), "TMPDIR": str(temp), "NO_PROXY": "127.0.0.1,localhost",
                   "LAIN_SYNTHETIC_CODEX_KEY": "lain-synthetic-loopback-only"}
            try:
                result = subprocess.run([binary, "--no-daemon", "exec", "--skip-git-repo-check", "--json", "Reply with fixture."],
                                        cwd=workspace, env=env, capture_output=True, text=True, timeout=25)
            finally:
                server.shutdown()
                thread.join(timeout=3)
            return {"apply_patch_tool_type": "omitted" if omit_apply_patch else apply_patch,
                    "multi_agent": multi_agent, "exitCode": result.returncode,
                    "requests": captured,
                    "clientError": result.stderr[-1200:] if result.returncode and not captured else None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--codex", required=True)
    parser.add_argument("--temporary-root", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    root = Path(args.temporary_root).resolve()
    root.mkdir(parents=True, exist_ok=True)
    binary = str(Path(args.codex).resolve())
    reports = [capture(binary, root, value) for value in ("freeform", None)]
    reports.extend([capture(binary, root, None, omit_apply_patch=True),
                    capture(binary, root, None, multi_agent=False)])
    report = {"syntheticOnly": True, "personalStateRead": False, "cases": reports}
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0 if all(r["requests"] for r in reports) else 1


if __name__ == "__main__":
    raise SystemExit(main())
