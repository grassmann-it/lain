#!/usr/bin/env python3
"""Synthetic v7 to v8 journal continuation and optional rollback/consumer check.

Never reads a journal or database outside this probe's private temporary tree.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

from qualify import Run, assert_complete, expect


def transition(baseline, upgrade, *, rollback=True):
    with Run(baseline) as run:
        for suffix in ("a", "b"):
            with run.post("text", "before-upgrade-" + suffix, route="fixture-" + suffix) as response:
                response.read()
        old = run.events()
        old_starts, _ = assert_complete(old["events"], 2)
        run.stop()
        old_bytes = run.journal.read_bytes()
        sidecar = Path(str(run.journal) + ".source")
        old_source_bytes = sidecar.read_bytes()
        run.core = str(upgrade)
        run.start()
        expect(run.events() == old, "upgrade changed existing journal identity, cursor or events")
        expect(run.journal.read_bytes() == old_bytes and sidecar.read_bytes() == old_source_bytes,
               "upgrade rewrote the existing journal or source sidecar")
        for suffix in ("a", "b"):
            with run.post("text", "after-upgrade-" + suffix, route="fixture-" + suffix) as response:
                response.read()
        combined = run.events()
        starts, usages = assert_complete(combined["events"], 4)
        expect(combined["sourceId"] == old["sourceId"], "upgrade replaced source UUID")
        expect(combined["events"][:len(old["events"])] == old["events"], "new dispatch changed old events")
        expect([event["sequence"] for event in combined["events"]] == list(range(1, len(combined["events"]) + 1)),
               "upgrade introduced sequence gaps or replay duplicates")
        expect(combined["highWatermark"] == combined["nextCursor"] == len(combined["events"]), "upgrade cursor is inconsistent")
        old_attempts = {event["attemptId"] for event in old_starts}
        old_requests = {event["requestId"] for event in old_starts}
        new_starts = [event for event in starts if event["sequence"] > old["highWatermark"]]
        expect(len(new_starts) == 2 and all(event["attemptId"] not in old_attempts and event["requestId"] not in old_requests for event in new_starts),
               "upgrade reused old request or attempt identities")
        expect(sum(event["totalTokens"] for event in usages) == 72, "mixed-version token facts changed")
        expect(run.journal.read_bytes().startswith(old_bytes), "upgrade modified the original journal bytes")
        expect(sidecar.read_bytes() == old_source_bytes, "upgrade changed the persisted source UUID")
        expect(run.events() == combined, "mixed-version journal replay changed events")
        evidence = {"sourceIdRetained": True, "oldJournalBytesRetained": True, "oldEventsUnchanged": True,
                    "beforeCursor": old["highWatermark"], "afterCursor": combined["highWatermark"],
                    "requests": 4, "attempts": 4, "tokens": 72, "newIdentitiesDisjoint": True,
                    "mixedReplayUnchanged": True, "rollbackReadTested": rollback}
        run.stop()
        if rollback:
            combined_bytes = run.journal.read_bytes()
            run.core = str(baseline)
            run.start()
            expect(run.events() == combined, "v7 rollback could not replay the unchanged mixed-version journal")
            expect(run.journal.read_bytes() == combined_bytes and sidecar.read_bytes() == old_source_bytes,
                   "v7 rollback rewrote mixed-version journal bytes or source")
            evidence["rollbackReadCompatible"] = True
        return evidence, combined


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline-core", required=True)
    parser.add_argument("--core", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--batch-fixture", help="optional sanitized complete mixed-version source batch")
    parser.add_argument("--consumer", help="optional already built Lain.Usage.Tests.dll")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--no-rollback", action="store_true", help="omit v7 reading mixed journal if backward compatibility is explicitly out of scope")
    args = parser.parse_args()
    old_data = Path(args.baseline_core).resolve(strict=True).read_bytes()
    new_data = Path(args.core).resolve(strict=True).read_bytes()
    report = {"schemaVersion": 1, "syntheticOnly": True,
              "baselineCoreSha256": hashlib.sha256(old_data).hexdigest(),
              "coreSha256": hashlib.sha256(new_data).hexdigest(), "passed": False, "checks": []}
    with tempfile.TemporaryDirectory(prefix="lain-version-transition-") as temporary:
        root = Path(temporary)
        baseline, upgrade = root / "v7-lain-proxy", root / "v8-lain-proxy"
        for path, data in ((baseline, old_data), (upgrade, new_data)):
            path.write_bytes(data)
            path.chmod(0o700)
        try:
            evidence, batch = transition(baseline, upgrade, rollback=not args.no_rollback)
            report["checks"].append({"name": "journal-upgrade-and-rollback-read", "passed": True, "evidence": evidence})
            batch_path = root / "mixed-source-batch.json"
            batch_data = (json.dumps(batch, indent=2) + "\n").encode()
            batch_path.write_bytes(batch_data)
            if args.batch_fixture:
                Path(args.batch_fixture).write_bytes(batch_data)
            if args.consumer:
                consumer = Path(args.consumer).resolve(strict=True)
                consumer_hash = hashlib.sha256(consumer.read_bytes()).hexdigest()
                result = subprocess.run([args.dotnet, str(consumer), "--ingest-journal", str(batch_path), str(root / "usage.sqlite")],
                                        capture_output=True, text=True, timeout=120)
                expect(result.returncode == 0, "mixed-version SQLite consumer failed")
                summary = json.loads(result.stdout)
                expect(summary.get("cursor") == batch["highWatermark"] and summary.get("complete") is True,
                       "mixed-version consumer did not commit the full cursor")
                totals = summary["totals"]
                expect(totals["requests"] == 4 and totals["attempts"] == 4 and totals["errors"] == 0,
                       "mixed-version consumer duplicate/request count mismatch")
                expect(totals["tokens"] == {"input": 44, "output": 28, "cached": None, "reasoning": None, "total": 72, "cacheCreation": None},
                       "mixed-version consumer changed known or unknown token quantities")
                expect(summary["accounts"] == 2 and summary["models"] == 1, "mixed-version consumer account/model attribution mismatch")
                expect(hashlib.sha256(consumer.read_bytes()).hexdigest() == consumer_hash, "consumer DLL changed during replay")
                report["checks"].append({"name": "mixed-version-consumer-replay-10", "passed": True,
                    "evidence": {"consumerDllSha256": consumer_hash, "inputJournalSha256": hashlib.sha256(batch_data).hexdigest(),
                                 "replays": 10, "storeReopenedEachReplay": True, "result": summary}})
            report["passed"] = True
        except Exception as error:
            report["checks"].append({"name": "version-transition", "passed": False, "error": str(error)})
    report["scope"] = "fresh synthetic journal only; no installed journal, database, credentials or live provider accessed"
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    print(f"{'PASS' if report['passed'] else 'FAIL'} synthetic journal version transition", flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
