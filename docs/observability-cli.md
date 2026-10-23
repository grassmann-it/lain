# Explore usage, live requests and providers

Run `lain --help` first: these examples require the expanded CLI and matching
Host. Development or a successful test run does not upgrade an installed
service. `lain status --json` reports readiness and configuration revision.
`--json` writes results to stdout; diagnostics use stderr. Human-readable output
uses the same measurements and explicit `unknown` values.

## Compare the same observation window

Use fixed timestamps for reproducible comparisons. These shell variables contain
only public query values:

```sh
lain_from=2026-10-23T00:00:00Z
lain_to=2026-10-24T00:00:00Z
lain usage analysis --from "$lain_from" --to "$lain_to" --timezone UTC \
  --group-by modelFamily,inferenceProvider,gateway --group-limit 100 --json
lain usage analysis --from "$lain_from" --to "$lain_to" --timezone UTC \
  --group-by accountId,billingAccountId,effectiveReasoningEffort --json
lain usage analysis --from "$lain_from" --to "$lain_to" --timezone UTC \
  --group-by conversationId,turnId,workerId --json
```

Each requested dimension is a separate breakdown; three dimensions do not produce
one combined grouping. `--group-by` accepts at most three distinct keys. Its
default is `modelFamily,accountId,reasoningEffort`. `--group-limit` is 1–500 per
dimension; `truncated` marks an incomplete group list while aggregate statistics
still cover the complete matching population. Null keys retain unknown identity.
The full key list is in the [API contract](observability-contracts.md).

Keep model family, concrete model, inference provider, gateway and billing account
separate. `accountId` identifies a local credential account; it does not establish
an independent upstream budget. A worker or conversation ID is useful only with
its recorded evidence. Similar text, timestamps or model names do not establish
relationships.

Filter a chosen population, then inspect its performance:

```sh
lain usage performance --from "$lain_from" --to "$lain_to" --timezone UTC \
  --model-family FAMILY --inference-provider PROVIDER --reasoning high \
  --group-by model,effectiveReasoningEffort,workerId --json
lain attempts --from "$lain_from" --to "$lain_to" --timezone UTC \
  --account ACCOUNT_ID --offset 0 --limit 100 --json
```

Replace uppercase identifiers with exact observed values. `--reasoning` filters
requested effort; `--effective-reasoning-effort` filters the actual upstream
translation when observed. More filters include route, manufacturer, gateway,
billing account, mode, conversation, turn, worker, harness and profile; see help.
Intervals are `[from,to)`, at most 366 days. CLI timestamps require an explicit
UTC offset. `--today --timezone Europe/Berlin` is useful for a local day, but its
window changes with the date.

`analysis`, `aggregate` and `performance` return the same server aggregate.
First-output latency, total duration and output-token rate each include measured
`samples` and `eligible` counts.
Compare counts alongside p50/p95; zero samples leave measurements null. First
output can be text, reasoning or tool activity. Total duration ends at the Core's
completion hook, rather than at the client's final byte. The reported rate basis
uses provider-reported output tokens divided by measured total duration; it is
not a decode-only rate. Never calculate population medians from paginated
attempts. Retain `observedAt`, `cursor`, `complete`, unknown token quantities and
unknown-usage counts with each comparison. Costs are not inferred from tokens,
subscription prices, points or quota balances.

## Follow the owned live inspector

```sh
lain inspector snapshot --limit 100 --json
lain inspector events --request REQUEST_ID --limit 100 --json
lain inspector events --source SOURCE_ID --after NEXT_CURSOR \
  --request REQUEST_ID --limit 100 --json
```

The first event page can omit source and cursor. For each subsequent page, copy
both `sourceId` and `nextCursor` from the previous response. A filtered empty page
may still advance the cursor because unrelated global events were scanned.
Snapshot limits are 1–256; event page limits are 1–500, subject to lower configured
Host limits.

When `sourceId` changes or `gapReason` reports a reset/retention gap, mark the
interruption explicitly and continue with the returned source/cursor pair. Do
not reuse the old numeric cursor under the new source, equate an empty page with
complete capture, or silently join generations. Current source changes are
reported as `source_changed`; clients also retain unfamiliar gap reasons. The
response can contain a retained page from the new generation while reporting
the gap. Refresh the snapshot to see the current streams.

Streams carry actual attempt scope plus request IDs and evidenced conversation,
turn, worker, parent and tool-continuation dimensions. A tool event is not a new
chat. Unknown or conflicting relationships stay unknown. Inspect `state`,
`complete`, `droppedEvents`, `truncated`, `contentPolicy` and `redactionState`
together. Host display retention and source-ring retention have separate bounds;
expired output is not reconstructed from usage history. Text previews stop at
2,000 characters per event; JSON retains the bounded event response. Neither
view is a durable payload archive.

## Discover providers and prepare an account

```sh
lain providers catalog
lain providers onboarding nvidia-build
lain providers routes --provider nvidia-build --json
```

The directory supplies provider IDs, signup/account links, documentation and
setup steps. The route matrix supplies exact endpoint, protocol, model family,
manufacturer, inference provider, gateway, capabilities and billing references.
Use the returned setup links for your own account. Catalog inclusion and
`adapterState=prepared` describe preparation; `liveVerification=not_tested` means
real access has not been established. Missing keys leave onboarding available.
Unknown capabilities stay unknown. Free allowances, trials, subscriptions and
PAYG retain their own units, scope, reset and expiry; catalog amounts are not
observed account balances.

After the user requests adding an account, read `lain status --json` and replace
`N` below with its revision. `NVIDIA_API_KEY` names an already populated secret
environment variable; its value never appears in this command:

```sh
lain accounts add-api --name "NVIDIA Build" --provider nvidia-build \
  --provider-route nvidia-build:step-5-preview \
  --base-url https://integrate.api.nvidia.com/v1 \
  --key-env NVIDIA_API_KEY --revision N --json
```

Alternatively, `--key-stdin` reads one secret line. Do not put a key on argv, echo
it into a generated shell command, or include it in a report. Repeat
`--setting NAME=VALUE` only for declared nonsecret fields and supported provider
settings: for example `accountId`, `region`, `workspaceId`,
`serviceDeploymentScope` or `billingPlan`. Resolve endpoint templates such as
Cloudflare's account ID using onboarding instructions. The Host validates route,
model, protocol, endpoint and provider-specific settings before saving.
OpenCode Contributor training routes additionally require explicit workspace
privacy permission through the declared `allowTrainingData=true` setting;
absence or false does not grant permission. This is not a free-only switch.

Account creation and `accounts check` do not prove a working inference
connection. Schema checks, mock success, catalog claims and policy inspection
also have distinct scopes. Perform real connection or inference checks only
when authorized and credentials are available; keep live verification unknown
until its actual scope is established. Configuration conflicts use exit 5:
reload the current revision and reassess, without forcing a write or retrying an
alternate route. No signup, subscription or purchase is performed by onboarding.

## Inspect strict free-only prerequisites

```sh
lain providers routes --provider openrouter --json
lain providers check-free CONFIGURED_ROUTE_ID --json
```

`CONFIGURED_ROUTE_ID` identifies a saved local route, rather than a catalog model
entry. This read-only command evaluates that route under `free_only`; it does
not set policy, create a key, run inference or prove access. Route/profile policy
is part of saved configuration. An applicable `free_only` policy cannot be
weakened by a permissive profile or paid-fallback opt-in. Paid, trial,
unknown-priced and unqualified routes remain blocked. Explicit cross-route
fallback execution is currently rejected when applying a profile.

Profile export preserves provider-route links and route/profile policies while
replacing account IDs with placeholders. Generated Codex files are launcher and
catalog artifacts produced after Host policy validation; policy enforcement
remains in the saved Host/Core configuration. The CLI installs those returned
strings without rebuilding route or policy records.

The current narrow OpenRouter path supports only the pinned Llama 3.3 70B and
Qwen3 Coder `:free` entries. It also requires the exact prepared transport and a
fresh read of the actual outgoing key's provider-side zero-USD cap, including
BYOK, before every dispatch. The final request guard pins the model and rejects
paid extras/overrides; the random free router is outside this path. No CLI
boolean, cached quota or `allowed=true` policy response replaces those runtime
checks. The user configures the dedicated key; lain does not create or change it.
A zero-cap key may also deny free requests. Real usability remains untested, and
prerequisite failure stays visible without a paid fallback. See the detailed
[OpenRouter controls and evidence boundary](providers/openrouter-free-only.md).
