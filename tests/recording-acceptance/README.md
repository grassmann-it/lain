# Independent synthetic recording acceptance preparation

Status: **bounded harness preparation only**. No recorder source, policy, storage,
normalization, API, UI, configuration, provider, live capture, build or service was
run or changed. These files do not claim integration qualification.

The REC-00 shared contract (`src/Lain.Core/RecordingContracts.cs`) is not published
in this worktree. Therefore the JSON here is an **internal acceptance graph**, not a
fixture API, DTO or schema for other workers. Do not bind Source/Policy/Store/
Normalization/API/UI code to these field names.

## Files

- `generate_and_validate.py` deterministically emits one bounded graph with 200
  model calls, 100 tool requests and 100 client-reported results, root plus two
  evidenced workers, fork, retry/model change, unknown identities, skill v1/v2
  provenance and synthetic secret canaries. It validates only basic causal expectations.
- `fixtures/synthetic-recording-v1.json` is the generated, clearly synthetic input.
- `reports/synthetic-validation-report.json` records harness checks and explicitly
  marks integration `not_run`.
- `handoff_readiness.py` checks only explicitly supplied synthetic report/fixture
  schema, coverage, hashes and leak canaries.
- `resource_benchmark.py` interprets an explicit baseline/off/on metrics file.
  The committed template has empty cells and cannot produce a performance pass.
- No file here imports production recording APIs or scans any user directory.

## Commands already run by this worker

```bash
python3 tests/recording-acceptance/generate_and_validate.py --generate
python3 tests/recording-acceptance/handoff_readiness.py --allow-synthetic-input
python3 tests/recording-acceptance/resource_benchmark.py
```

These are synthetic harness checks only. They are not DotNet/Go/Host/Core tests and
do not use providers or services.

## Required adapter seam

A future contract/integration adapter, owned outside this qualification boundary,
must accept typed objects from the official shared recording contract. It must
translate into causal acceptance assertions without equating local harness IDs with
provider IDs, filling unknown IDs, hiding representation gaps, or claiming client-
reported tool results prove local execution. Until that contract exists, every
integration result remains `not_run`.

To add measured local benchmark data after real wiring, copy and fill the explicit
metrics template at 1/4/8 concurrency for baseline/off/on. The interpreter reports
quantiles, means/peaks and completeness, but always leaves the performance decision
to explicit human review against a then-approved budget.
