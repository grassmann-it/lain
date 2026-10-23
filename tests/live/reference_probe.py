#!/usr/bin/env python3
"""Four bounded real Responses requests, only through the qualified own lain proxy.

Default prints the plan without opening a socket. --execute requires the synthetic SHA gate.
Provider credentials are neither read nor printed by this program.
"""
import argparse
import datetime as dt
import hashlib
import json
import pathlib
import sys
import time
import urllib.error
import urllib.request

HOST = "http://127.0.0.1:18418/api/v1"
PROXY = "http://127.0.0.1:18417/v1/responses"
MODEL = "gpt-6-luna"
MAX_BYTES = 1024 * 1024


class ProbeError(Exception):
    """Only constant, sanitized diagnostic messages belong here."""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, url):
        raise ProbeError("Redirect refused.")


OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())


def plan(codex_route, command_route):
    return [{"number": number, "endpoint": PROXY, "route": route, "upstreamModel": MODEL,
             "protocol": "Responses SSE", "step": step}
            for number, (route, step) in enumerate(
                [(codex_route, "forced echo_probe tool"), (codex_route, "tool result continuation"),
                 (command_route, "forced echo_probe tool"), (command_route, "tool result continuation")], 1)]


def validate_gate(binary):
    binary = pathlib.Path(binary).resolve(strict=True)
    gate = json.loads(binary.with_name("qualification.json").read_text())
    manifest = json.loads(binary.with_name("binary-manifest.json").read_text())
    with binary.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
    if not (gate.get("passed") is True or gate.get("syntheticPassed") is True) or gate.get("sha256") != actual or manifest.get("sha256") != actual:
        raise ProbeError("Synthetic qualification gate is missing or does not match the proxy binary.")
    return actual


def bounded_read(response):
    raw = response.read(MAX_BYTES + 1)
    if len(raw) > MAX_BYTES:
        raise ProbeError("Response exceeded the bounded probe limit.")
    return raw


def open_request(url, token, payload=None, account_id=None):
    headers = {"Authorization": "Bearer " + token, "Content-Type": "application/json"}
    if account_id:
        headers["X-Lain-Account-ID"] = account_id
    request = urllib.request.Request(url, data=json.dumps(payload).encode() if payload is not None else None,
                                     headers=headers, method="POST" if payload is not None else "GET")
    try:
        return OPENER.open(request, timeout=45)
    except urllib.error.HTTPError as error:
        raise ProbeError("HTTP request failed with status " + str(error.code) + ".") from None
    except (urllib.error.URLError, TimeoutError, OSError):
        raise ProbeError("Local lain request unavailable or timed out.") from None


def host_json(path, token):
    with open_request(HOST + path, token) as response:
        return json.loads(bounded_read(response))


def route_account(configuration, alias, snapshot):
    candidates = [r for r in configuration.get("routes", []) if r.get("alias") == alias]
    if len(candidates) != 1:
        raise ProbeError("Required route alias is missing or ambiguous.")
    route = candidates[0]
    ids = route.get("accountIds", [])
    if route.get("pooled") or not route.get("enabled") or len(ids) != 1 or route.get("upstreamModel") != MODEL:
        raise ProbeError("Probe route must select the approved model and one strict enabled account.")
    matches = [a for a in configuration.get("accounts", []) if a.get("id") == ids[0]]
    kind = "codex-access-snapshot" if snapshot else "api-key"
    if len(matches) != 1 or matches[0].get("credentialKind") != kind or not matches[0].get("enabled") or not matches[0].get("sharedWithLegacy"):
        raise ProbeError("Probe account must be enabled and imported into own host storage.")
    account = matches[0]
    if snapshot:
        if account.get("provider") != "codex" or not account.get("expiresAt"):
            raise ProbeError("Codex snapshot identity or expiry is unknown.")
        expiry = dt.datetime.fromisoformat(account["expiresAt"].replace("Z", "+00:00"))
        if expiry.tzinfo is None or expiry <= dt.datetime.now(dt.timezone.utc):
            raise ProbeError("Codex snapshot is expired; refresh is forbidden.")
    return account["id"]


def parse_sse(response):
    """Do not emit provider text, arguments, arbitrary errors, or full response IDs."""
    total = count = 0
    data = []
    final = None
    deadline = time.monotonic() + 60
    while True:
        if time.monotonic() > deadline:
            raise ProbeError("SSE stream exceeded the probe deadline.")
        line = response.readline(MAX_BYTES + 1)
        total += len(line)
        if total > MAX_BYTES:
            raise ProbeError("SSE stream exceeded the bounded probe limit.")
        if not line:
            break
        if line.strip() == b"":
            if not data:
                continue
            raw = b"\n".join(data)
            data = []
            if raw == b"[DONE]":
                continue
            event = json.loads(raw)
            count += 1
            if event.get("type") in ("error", "response.failed", "response.incomplete"):
                raise ProbeError("Provider stream reported failure or incomplete output.")
            if event.get("type") == "response.completed":
                final = event.get("response")
        elif line.startswith(b"data:"):
            data.append(line[5:].lstrip().rstrip(b"\r\n"))
    if not isinstance(final, dict) or final.get("status") != "completed":
        raise ProbeError("SSE stream did not complete successfully.")
    return final, {"sseEvents": count, "responseBytes": total}


def payload(route, inputs, continuation=False):
    return {"model": route, "stream": True, "store": False, "max_output_tokens": 128,
            "instructions": "Run only the harmless echo protocol test. After the tool result, reply exactly ok.",
            "input": inputs, "include": ["reasoning.encrypted_content"], "parallel_tool_calls": False,
            "tools": [{"type": "function", "name": "echo_probe", "description": "Return the fixed string ok; no side effects.",
                       "strict": True, "parameters": {"type": "object", "properties": {"text": {"type": "string", "enum": ["ok"]}},
                                                        "required": ["text"], "additionalProperties": False}}],
            "tool_choice": "none" if continuation else {"type": "function", "name": "echo_probe"}}


def safe_usage(final):
    usage = final.get("usage") or {}
    return {key: value if isinstance(value, int) and not isinstance(value, bool) and value >= 0 else None
            for key in ("input_tokens", "output_tokens", "total_tokens") for value in [usage.get(key)]}


def run_pair(route, account_id, inference_token, report):
    inputs = [{"role": "user", "content": [{"type": "input_text", "text": "Call echo_probe once with text ok. Then wait for its result."}]}]
    for step in ("tool", "continuation"):
        row = {"route": route, "accountId": account_id, "step": step, "state": "attempted"}
        report["calls"].append(row)
        with open_request(PROXY, inference_token, payload(route, inputs, step == "continuation"), account_id) as response:
            if "text/event-stream" not in response.headers.get("Content-Type", ""):
                raise ProbeError("Expected a Responses SSE stream.")
            final, metadata = parse_sse(response)
        output = final.get("output", [])
        if step == "tool":
            tools = [item for item in output if item.get("type") == "function_call"]
            if len(tools) != 1 or tools[0].get("name") != "echo_probe" or json.loads(tools[0].get("arguments", "null")) != {"text": "ok"} or not tools[0].get("call_id"):
                raise ProbeError("Expected exactly the approved harmless tool call.")
            inputs += output + [{"type": "function_call_output", "call_id": tools[0]["call_id"], "output": "ok"}]
        else:
            text = "".join(c.get("text", "") for item in output if item.get("type") == "message" for c in item.get("content", []) if c.get("type") == "output_text")
            if text.strip().lower() != "ok" or any(item.get("type") == "function_call" for item in output):
                raise ProbeError("Tool continuation did not return the expected fixed answer.")
        row.update(metadata, state="passed", usage=safe_usage(final))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--lain-home", type=pathlib.Path)
    parser.add_argument("--proxy-binary", type=pathlib.Path)
    parser.add_argument("--codex-route", default="live-codex-gpt-6-luna")
    parser.add_argument("--command-route", default="cmd-gpt-6-luna")
    args = parser.parse_args()
    report = {"observedAt": dt.datetime.now(dt.timezone.utc).isoformat(), "plan": plan(args.codex_route, args.command_route), "calls": []}
    if not args.execute:
        print(json.dumps(report, indent=2))
        return 0
    try:
        if not args.lain_home or not args.proxy_binary:
            raise ProbeError("Execution requires explicit own lain home and qualified binary paths.")
        report["qualifiedSha256"] = validate_gate(args.proxy_binary)
        own = args.lain_home.resolve(strict=True)
        if "easycliproxyapi" in str(own) or own == pathlib.Path.home():
            raise ProbeError("The probe requires a separate own lain state directory.")
        host_token = (own / "credentials/host.token").read_text().strip()
        status = host_json("/status", host_token)
        if not status.get("proxyReady") or status.get("revision") != status.get("appliedRevision"):
            raise ProbeError("Own proxy must be ready with the current revision applied.")
        config = host_json("/configuration", host_token)
        if config.get("revision") != status.get("revision"):
            raise ProbeError("Configuration changed during probe preparation.")
        codex = route_account(config, args.codex_route, True)
        command = route_account(config, args.command_route, False)
        if codex == command:
            raise ProbeError("The probes must use distinct imported provider accounts.")
        inference_token = host_json("/auth/token?audience=codex", host_token)["token"]
        report["revision"] = config["revision"]
        run_pair(args.codex_route, codex, inference_token, report)
        run_pair(args.command_route, command, inference_token, report)
        report["state"] = "passed"
        return 0
    except ProbeError as error:
        report.update(state="failed", error=str(error))
        return 1
    except Exception:
        report.update(state="failed", error="Probe metadata or response could not be validated; no raw data emitted.")
        return 1
    finally:
        print(json.dumps(report, indent=2))


if __name__ == "__main__":
    sys.exit(main())
