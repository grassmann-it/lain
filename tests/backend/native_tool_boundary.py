#!/usr/bin/env python3
"""Independently check the explicit native image-generation exclusion.

This is a newly enforced boundary, not a claim that the baseline already blocked it.
Only ordinary synthetic function tools and a literal tiny image input are dispatched.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import tempfile
from urllib.error import HTTPError

from fake_provider import marker
from qualify import Run, assert_complete, expect


# Public, fixed 1x1 PNG data; never loaded from the user's files or a network URL.
IMAGE = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ioAAAAASUVORK5CYII="


def probe(core):
    with Run(core) as run:
        rejected = []
        for name, fields in [
            ("native-tool", {"tools": [{"type": "image_generation"}]}),
            ("native-tool-choice", {"tool_choice": {"type": "image_generation"}}),
            ("native-tool-choice-string", {"tool_choice": "image_generation"}),
            ("native-tool-and-choice", {"tools": [{"type": "image_generation"}], "tool_choice": {"type": "image_generation"}}),
        ]:
            try:
                with run.responses({"model": "fixture-a", "input": marker("text", name), **fields}) as response:
                    response.read()
                raise AssertionError(f"{name}: unsupported native tool was accepted")
            except HTTPError as error:
                detail = error.read().decode(errors="replace")
                expect(error.code == 400, f"{name}: expected clear HTTP 400, got {error.code}")
                expect("image_generation" in detail, f"{name}: rejection did not identify the unsupported tool")
                error.close()
            expect(not run.provider.snapshot() and not run.events()["events"], f"{name}: rejected native tool dispatched upstream")
            rejected.append(name)

        for name in ("image_generation", "generate_image"):
            body = {"model": "fixture-a", "input": marker("text", "function-" + name),
                    "tools": [{"type": "function", "name": name, "parameters": {"type": "object", "properties": {}}}],
                    "tool_choice": {"type": "function", "name": name}}
            with run.responses(body) as response:
                expect(response.status == 200, "ordinary image-named function tool was rejected")
                response.read()
            expect(name in run.provider.snapshot()[-1]["functionToolNames"], "ordinary function name was stripped before upstream")

        with run.responses({"model": "fixture-a", "input": [{"role": "user", "content": [
            {"type": "input_text", "text": marker("text", "vision-input-remains")},
            {"type": "input_image", "image_url": IMAGE}]}]}) as response:
            expect(response.status == 200, "ordinary image input was rejected")
            response.read()
        rows = run.provider.snapshot()
        expect(len(rows) == 3 and rows[-1]["imageInputPresent"], "Vision input did not reach upstream")
        starts, _ = assert_complete(run.events()["events"], 3)
        expect(all(event["accountId"] == "account-a" for event in starts), "positive control escaped its strict account")
        return {"rejectedCases": rejected, "rejectedStatus": 400, "nativeToolUpstreamAttempts": 0,
                "nativeToolDurableAttempts": 0, "ordinaryImageNamedFunctionsPreserved": ["image_generation", "generate_image"],
                "visionImageInputPreserved": True, "positiveControlAttempts": 3,
                "scope": "new explicit image_generation exclusion; no image output or real provider used"}


def override_probe(core):
    run = Run(core)
    try:
        config = run.root / "core.yaml"
        config.write_text(config.read_text() + '''payload:
  override-raw:
    - models:
        - name: "fixture-a"
          protocol: "openai"
      params:
        tools: '[{"type":"image_generation"}]'
''')
        run.start()
        # The client body is ordinary. Only the configured, post-translation
        # override introduces a server-side tool at the final send boundary.
        try:
            with run.responses({"model": "fixture-a", "input": marker("text", "final-override-injection")}) as response:
                response.read()
            raise AssertionError("payload override injected a native image tool into upstream")
        except HTTPError as error:
            status = error.code
            detail = error.read().decode(errors="replace")
            expect(400 <= status <= 599 and "image_generation" in detail, "final guard rejection was not a clear HTTP error")
            error.close()
        expect(not run.provider.snapshot(), "final payload override reached the fake provider")
        _, usages = assert_complete(run.events()["events"], 1)
        failed = usages[0]
        expect(failed["outcome"] == "failed" and failed["usageReported"] is False,
               "final guard failure did not retain a failed unknown-usage attempt")
        for field in ("inputTokens", "outputTokens", "totalTokens", "cacheReadTokens", "cacheCreationTokens", "reasoningTokens"):
            expect(failed.get(field) is None, "final guard failure invented consumed tokens")
        with run.post("text", "ordinary-route-after-rejection", route="fixture-b") as response:
            response.read()
        assert_complete(run.events()["events"], 2)
        expect(len(run.provider.snapshot()) == 1 and run.provider.snapshot()[0]["account"] == "account-b",
               "ordinary route could not recover after the blocked final payload")
        expect(run.get("/lain/health")["journalHealthy"] is True, "final guard rejection poisoned the durable journal")
        return {"payloadOverrideAppliedAfterClientBoundary": True, "injectedNativeToolUpstreamAttempts": 0,
                "rejectedStatus": status, "failedDurableAttemptRetained": True, "usageUnknown": True,
                "ordinaryRouteStillDispatches": True, "journalHealthy": True}
    finally:
        run.__exit__()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    data = Path(args.core).resolve(strict=True).read_bytes()
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": hashlib.sha256(data).hexdigest(),
              "behaviorChange": "explicit exclusion of unqualified native image_generation", "passed": False, "checks": []}
    with tempfile.TemporaryDirectory(prefix="lain-native-tool-boundary-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(data)
        frozen.chmod(0o700)
        for name, function in [("native-image-generation-exclusion-and-positive-controls", probe),
                               ("final-payload-override-exclusion-and-recovery", override_probe)]:
            try:
                report["checks"].append({"name": name, "passed": True, "evidence": function(frozen)})
            except Exception as error:
                report["checks"].append({"name": name, "passed": False, "error": str(error)})
        report["passed"] = all(check["passed"] for check in report["checks"])
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    print(f"{'PASS' if report['passed'] else 'FAIL'} native image-generation boundary", flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
