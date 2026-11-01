#!/usr/bin/env python3
"""Interpret explicit baseline/off/on recording benchmark metrics without claiming a pass."""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
DEFAULT_METRICS = HERE / "fixtures" / "resource-metrics-template.json"
DEFAULT_OUTPUT = HERE / "reports" / "resource-benchmark-skeleton.json"
KIND = "lain.recording.acceptance.resource-benchmark-interpretation"
PHASES = ("baseline", "off", "on")
CONCURRENCIES = (1, 4, 8)
LATENCY_FIELDS = ("ttftMs", "interFrameGapMs")
RESOURCE_FIELDS = ("cpuSeconds", "rssMiB", "queuePeakBytes", "diskBytes", "writeAmplification")
THROUGHPUT_FIELD = "throughputOpsPerSecond"
MIN_SAMPLES = 5


def write(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temp, path)


def percentile(values: list[float], percentile_value: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    position = (len(ordered) - 1) * percentile_value
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return ordered[lower]
    fraction = position - lower
    return ordered[lower] * (1 - fraction) + ordered[upper] * fraction


def mean(values: list[float]) -> float | None:
    return sum(values) / len(values) if values else None


def interpret_run(run: dict[str, Any]) -> dict[str, Any]:
    samples = run.get("samples", [])
    enough = len(samples) >= MIN_SAMPLES
    result: dict[str, Any] = {
        "phase": run.get("phase"),
        "concurrency": run.get("concurrency"),
        "sampleCount": len(samples),
        "metricsComplete": enough,
        "providerCalls": 0,
    }
    for field in LATENCY_FIELDS:
        values = [float(sample[field]) for sample in samples if isinstance(sample.get(field), (int, float))]
        result[field] = {
            "p50": percentile(values, 0.50),
            "p95": percentile(values, 0.95),
            "p99": percentile(values, 0.99),
        } if values else None
    for field in RESOURCE_FIELDS:
        values = [float(sample[field]) for sample in samples if isinstance(sample.get(field), (int, float))]
        result[field] = {"mean": mean(values), "peak": max(values) if values else None} if values else None
    throughput = [float(sample[THROUGHPUT_FIELD]) for sample in samples if isinstance(sample.get(THROUGHPUT_FIELD), (int, float))]
    result[THROUGHPUT_FIELD] = {"mean": mean(throughput), "minimum": min(throughput) if throughput else None} if throughput else None
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metrics", type=Path, default=DEFAULT_METRICS, help="explicit synthetic metrics JSON; no directories are scanned")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    metrics_path = args.metrics.resolve()
    try:
        metrics = json.loads(metrics_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise SystemExit(f"cannot read explicit metrics {metrics_path}: {error}") from error

    structural = metrics.get("synthetic") is True and metrics.get("kind") == "lain.recording.acceptance.resource-metrics"
    runs = metrics.get("runs", [])
    expected_pairs = {(phase, concurrency) for phase in PHASES for concurrency in CONCURRENCIES}
    actual_pairs = {(run.get("phase"), run.get("concurrency")) for run in runs}
    structure_ok = structural and actual_pairs == expected_pairs and len(runs) == len(expected_pairs)
    interpretations = [interpret_run(run) for run in runs]
    metrics_complete = structure_ok and all(run["metricsComplete"] for run in interpretations)
    output = {
        "kind": KIND,
        "version": 1,
        "synthetic": True,
        "status": "metrics_interpreted" if metrics_complete else "awaiting_metrics",
        "metricsComplete": metrics_complete,
        "performancePassClaimed": False,
        "decision": "manual_review_required",
        "providerCalls": 0,
        "input": {"path": str(metrics_path), "scanBoundary": "explicit --metrics file only"},
        "schemaValid": structure_ok,
        "minimumSamplesPerCell": MIN_SAMPLES,
        "runs": interpretations,
        "notes": [
            "Empty template cells are intentionally not filled with invented measurements.",
            "Baseline/off/on deltas and noise must be reviewed together; no approved budget pass is inferred.",
            "Actual provider calls and source integration are forbidden in this skeleton.",
        ],
    }
    write(args.output.resolve(), output)
    print(f"resource benchmark interpretation: {output['status']}; performancePassClaimed=false")
    print(f"output={args.output.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
