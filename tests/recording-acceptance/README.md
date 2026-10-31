# Recording acceptance: fixture generator and actual Host/Core harness

The original graph generator below remains synthetic-only. The separate
`host_core_capture.py` harness now exercises actual candidate Host/Core processes,
a loopback fake provider and durable recorder APIs. These evidence classes are
separate; see [execution ledger](../../docs/recording-execution-2026-10-30.md).
No harness scans or imports production chats or uses a paid provider.

## Actual Host/Core qualification

Run in the project serial verification lane, with private TMPDIR and explicit
candidate artifacts:

```sh
python3 tests/recording-acceptance/host_core_capture.py \
  --host /absolute/candidate/host/Lain.Host.dll \
  --core /absolute/candidate/proxy/lain-proxy \
  --output /absolute/private-evidence --latency-rounds 12
```

The harness uses ephemeral ports, synthetic keys/content and an isolated state
root. It checks default off, consent, real Responses/SSE/tool forwarding,
secret exclusion before persistence, exact-source paging, tool-result/model/
worker correlation, stop, process restart and archive clear. It samples full HTTP
latency and RSS at concurrency1/4/8, records actual dropped/unknown-loss counters,
and never converts this small measurement into a TTFT/performance pass. Tool
results are client-reported fixture content, not executed commands. Preserve the
artifact hashes in its receipt. Failed acceptance remains failure; stale cursors
can be retried only where the API explicitly returns that code.

## Original synthetic-only graph tooling

Its internal acceptance graph is not the shared recorder DTO or a real execution
trace. The following utilities retain their original narrower scope.

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
- The graph utilities do not import production APIs; the explicit Host/Core harness above does. Neither scans a user directory.

## Commands already run by this worker

```bash
python3 tests/recording-acceptance/generate_and_validate.py --generate
python3 tests/recording-acceptance/handoff_readiness.py --allow-synthetic-input
python3 tests/recording-acceptance/resource_benchmark.py
```

These are synthetic harness checks only. They are not DotNet/Go/Host/Core tests and
do not use providers or services.

## Future graph adapter seam

A future contract/integration adapter, owned outside this qualification boundary,
must accept typed objects from the official shared recording contract. It must
translate into causal acceptance assertions without equating local harness IDs with
provider IDs, filling unknown IDs, hiding representation gaps, or claiming client-
reported tool results prove local execution. The generated graph report retains `not_run`; it must not be presented as the
separate actual Host/Core acceptance receipt.

To add measured local benchmark data after real wiring, copy and fill the explicit
metrics template at 1/4/8 concurrency for baseline/off/on. The interpreter reports
quantiles, means/peaks and completeness, but always leaves the performance decision
to explicit human review against a then-approved budget.
