# REC-06 · Host handoff, local API, shared client and CLI

Owner: API worker. Depends on [REC-00](00-contracts.md); fixtures allow early work,
real integration uses01/02/04/05. Status: plan only, no service/client mutation.

## Result and ownership

Connect independent Core observations to sanitized durable source commits and
paged saved-conversation queries. Supply the same authenticated API/typed client
to Desktop and CLI. New installations remain off; enabled content scope includes
all models by default. Training rights are not the archive-reading gate.

Proposed owned paths:
`src/Lain.Host/Recording/Api/**`,
`src/Lain.Host/Recording/Collection/**`,
`src/Lain.Client/LainApiClient.Recording.cs`,
`src/Lain.Cli/RecordingCommands.cs`,
`tests/recording-api/**`, `tests/recording-cli/**`.
Integrator owns Host Program, API/shared DTOs, project files and existing
CliApplication dispatch. REC-04 owns persistence/migrations; REC-05 query code;
REC-01 policy; REC-02 source endpoint. No existing Usage queries/collector changes.

## Tasks

1. Collect only the owned private source feed. Authenticate it without logging
   headers/tokens, disallow redirects/proxies, validate epoch/sequence/length/
   source policy and explicit gaps. No remote URL from a captured payload. Core
   does not synchronously call this collector on inference callbacks.
2. Enforce bounded consumer concurrency/bytes and restartable cancellation.
   Advance the recorder source checkpoint only after REC-04 durable commit under
   the agreed contract. Projection failures can replay; don't consume/ack Usage
   or Inspector queues. Source evictions/crash uncertainty become capture gaps.
3. Map policy-approved data through01 into04, then committed projection/query
   flow through03/05. Distinguish admitted/enqueued/written/durable/projected in
   health; don't expose green completeness merely because HTTP polling works.
4. Design the recording-only namespace with REC-00. Proposed local operations:
   status/policy, enable/stop, conversation/run lists, selected timeline/worker/
   request pages, selected payload/API record, run variant manifest and retention/
   source deletion. Request/byte/page caps and generation/cursor errors explicit.
5. Keep all mutations recording-scoped with revision checks; no Apply/profile
   selection, provider/client credential replacement or global Codex config edits.
   Validate ownership/path/query arguments, unsupported schemas and old clients.
6. CLI uses the common client. Proposed command family `lain recording ...` for
   status/control/conversations/show/run-label/delete, with bounded JSON output
   and safe error codes. Names are frozen in00 before code, not assumed installed
   commands. Body output is explicit and bounded; status never prints content.
7. Manifest input is explicit/user-selected; preserve configured/reported/observed
   skill/prompt revision provenance. No skill-directory scan or extra model call.
8. Startup capability state distinguishes available archive reads, active capture,
   unavailable Core and unsupported mode/protocol; no automatic service restart,
   recorder enablement or provider test when a read/status command is invoked.

## Acceptance

- Fixture-backed CLI and Desktop client use identical query results and caps.
- Auth failures, redirects, malformed batch/cursor/source reset, stop/detach,
  duplicates and store/index faults don't crash inference or corrupt checkpoints.
- Disabled has no producer/collector content work; runtime outage still permits
  reading already retained conversations and accurate unavailable capture status.
- Stale recording policy revision conflicts do not change routing revision or
  consume pending model/profile edits. Mutation retry after uncertainty requires
  readback, not duplicate destructive operations.
- No raw source content/secret headers in normal errors, logs, metrics or command
  argv; response body access only via explicit bounded archive read.
- Stop, deletion and display pause have distinct behavior. Local path/symlink,
  unauthorized payload reference and source-deletion tests pass.

Return contract version, owned changes, actual API/client/CLI tests, state/cursor
error table, supported input/format cells and wiring requests to integrator.
No production access or real capture is required for this package's fixture work.
