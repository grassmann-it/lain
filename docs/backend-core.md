# Isolated core integration

The executable `lain-proxy` links the independent module in `proxy/adapter` with
the verified external **CLIProxyAPI v8.0.41** dependency. The complete upstream
source is no longer vendored. `proxy/upstream.json` pins its official archive,
commit, module checksums and explicit patch overlays; the build applies them only
in a private temporary tree. The prepared source is modified, so this is not the
unchanged official release binary. [The adapter boundary](../proxy/README.md)
explains each necessary SDK extension and its tests. `proxy/baseline.json` remains
historical evidence for the earlier v7 source, not the provenance of this build.
The personal proxy is outside this build and upgrade.

Build with `scripts/build-core.sh`, optionally passing an absolute output path.
Exactly Go 1.27.2 is required (`proxy/toolchain.json`); set `LAIN_GO` to its executable when it is outside PATH.
Dependencies are pinned by the adapter's `go.mod` and `go.sum`. Default output is
`artifacts/proxy/lain-proxy`; its adjacent manifest records the binary SHA-256,
source manifest SHA-256, compiler, upstream version and patch-set hash.
Licence: upstream MIT (`proxy/LICENSE.upstream`). Schema-2 source provenance and
Core-only delivery are documented in [the delivery contract](core-upgrade-delivery.md).
Upgrade acceptance is tracked separately in [the v8 report](core-v8-upgrade.md).

## Startup and configuration

All four paths must be absolute and must refer to lain-owned storage:

```sh
lain-proxy --config /own/proxy/config.yaml \
  --bindings /own/proxy/bindings.json \
  --journal /own/proxy/events.jsonl \
  --management-token-file /own/credentials/management.token
```

The YAML is the pinned core's standard schema. Explicit loopback host, nonzero
port, absolute auth directory and at least one inference API key are required.
The management token is a different secret, at least 24 characters. Default
product ports are owned by the Host; tests use free ephemeral ports. A listener
conflict does not terminate or replace an existing process.

```json
{
  "accounts": {"account-a":"openai-compatibility:fixture:0123456789ab"},
  "routes": {
    "my-model": {
      "id":"route-a", "model":"configured-core-model-alias",
      "accounts":["account-a"], "selection":"strict"
    }
  }
}
```

An account has one default core credential and may have transport-specific IDs
through a route `credentials:{"account-a":"transport-credential-id"}` override.
Two account IDs cannot map to the same credential. A strict route has exactly one member. A pool has an explicit
member list and `selection:"pool"`. The HTTP model alias is resolved to this
binding and rewritten to the configured core alias. An optional
`X-Lain-Account-ID` can narrow membership, never enlarge it. Core selection filters
all candidate paths by the allowed credential IDs, and the durable dispatch gate
checks the final credential again. Credential IDs are internal version identities;
rename and key replacement preserve the Host's stable account ID in a new binding
revision. Removal/recreation must receive a new account ID if it represents a new
account. Historical journal records are immutable.

Core configuration, bindings and credentials are startup snapshots. The Host must
stop, atomically replace its configuration files and restart to apply a revision.
Automatic file reload, plugin loading, original management endpoints, discovery,
pprof, request-content logging and management UI updates are disabled. The only
inference endpoints are POST `/v1/chat/completions`, `/v1/responses`,
`/v1/responses/compact`, `/v1/messages` and GET `/v1/models`. Models lists contain
only configured public route aliases with `visible` omitted or true. Hidden
routes remain explicitly callable. Requests are limited to 32 MiB at this
boundary. WebSocket and image endpoints are not exposed by this version.

Native server-side `image_generation` tools and their native tool choice are
also rejected before dispatch. This closes a previously unqualified permeable
path: the upstream reports additional image-model usage under a separate reporter
identity, while the established Host contract permits one primary usage fact per
attempt. The adapter must neither invent an extra dispatch nor discard that usage.
The official disable-image-generation option prevents automatic injection, and a
final outbound guard prevents payload overrides from restoring native image tools.
Ordinary function tools, including image-related names, and vision/image inputs
remain within the existing protocol path. Native image generation needs a separate
accounting extension before it can be enabled.

OAuth import accepts only separate Codex access-token snapshots. A JSON file with
`refresh_token`, another OAuth provider, or a symlink is rejected. All refresh and
request preparation paths are disabled for these snapshots; runtime credential
writes are suppressed. Owned login credentials use the separate directory and
explicit ownership rules described below. Live provider rotation remains untested
unless the qualification report records such a probe.

## Durable attempt and usage contract

The attempt identity is allocated immediately before each concrete executor call,
then `attempt_started` is appended and `fsync` completes **before** dispatch. The
same execution ID is propagated into the provider's usage reporter. Logical request
IDs are the core handler lifecycle UUID, so a retry has a distinct attempt ID and
the same request ID. The startup binding supplies the stable account and route.

The usage publisher calls the durable sink synchronously **before** starting or
appending to the core RAM queue. The sink stores only selected metadata: no request
or response content, headers, API keys, access tokens, error bodies or session text.
A terminal `usage` event retains the start identity. Unknown quantities are JSON
`null`; explicitly reported zero is `0`. For the qualified OpenAI/Codex/Claude parsers,
per-component presence survives parsing and legacy total normalization. OpenAI/Codex cache and
reasoning fields remain subsets and must not be added again to input/output totals.
Claude cache-read/write are separately reported from its raw input tokens; reasoning
is a subset of output. Claude start/delta presence is merged without replacing
explicit zeros with missing values. An absent total is not fabricated from other
fields. Other parser paths lacking component-presence evidence produce unknown
quantities until qualified.

When an executor ends without reported usage, a terminal event records the outcome
with unknown tokens. Missing usage is not zero usage. If the process dies between
start and terminal persistence, the durable start remains pending and the Host
marks its consumption and coverage uncertain. A terminal success only means the
executor returned successfully; it does not guarantee usage was reported.

Write, short-write, identity or fsync failure latches the journal unhealthy. New
dispatches fail with HTTP 503; a nonstreaming execution cannot return ordinary
success after a journal failure. For an already-started SSE response, the stream
terminates with an error when the transport still permits one. The start record
provides evidence of the unresolved attempt. Consumption already incurred upstream
cannot be undone. No usage record is sent to the volatile queue after a failed
synchronous durable write.

The journal has one exclusive OS-level writer lock. The source UUID is persisted
in a private sidecar; sequences start at 1 and are contiguous. Startup verifies
source/sequence/event IDs and refuses torn or malformed data. It does not discard
an incomplete tail automatically. Recovery must preserve the original artifact,
identify the last valid boundary and explicitly resolve or retain uncertainty.
The journal is append-only and currently has no retention/compaction mechanism.
The sparse offset index grows with history; payload pages remain bounded.

## Private management and replay

These endpoints require the separate management bearer token:

- `GET /lain/health`: readiness after all bound credentials and models are registered,
  plus journal health. A transport listener alone is not readiness.
- `GET /lain/accounts`: sanitized credential ID, provider and disabled state.
- `GET /lain/events?after=0&limit=1000`: immutable non-destructive replay. Limit 1–1000;
  cursor must be between zero and the current high watermark.

The envelope is `{sourceId,events,nextCursor,highWatermark}`. `nextCursor` is the last
returned sequence, or `after` for an empty page. There is deliberately no destructive
pop or source-side acknowledgement. The Host stores events and its cursor in one
SQLite transaction, then resumes using that committed cursor. Repeating a page
must deduplicate by source plus sequence/event ID and reject changed content.
Changing source UUID requires a separate source cursor, never reusing the old cursor.

Each event has schema version 1 and these camelCase fields:

```json
{
  "schemaVersion":1,"sourceId":"UUID","sequence":1,"eventId":"UUID:1",
  "kind":"attempt_started","requestId":"UUID","attemptId":"UUID",
  "accountId":"account-a","credentialId":"core-credential-id","routeId":"route-a",
  "observedAt":"2026-10-23T12:00:00Z","provider":"provider-id",
  "requestedModel":"my-model","model":"upstream-model","outcome":null,
  "inputTokens":null,"outputTokens":null,"totalTokens":null,
  "cacheReadTokens":null,"cacheCreationTokens":null,"reasoningTokens":null,
  "usageReported":false,"statusCode":null,"latencyMs":null,"ttftMs":null
}
```

Terminal `kind` is `usage`; outcome is `success`, `failed` or `canceled`. Known HTTP
status is 100–599. Latency/TTFT are milliseconds or null. Terminal `model` can be the
actual response model; the request, attempt, account, credential, route and provider
identities never change. Unknown coverage and pending starts are interpreted by the
Host rather than silently completed during source replay.

## Verification boundary

Local tests cover fsync-before-visibility, sticky write faults, exclusive ownership,
restart/replay identity, malformed/torn tails, concurrent contiguous sequences,
strict HTTP membership, private management authorization, unknown-token outcomes,
per-component absent-versus-zero fields and durability-before-RAM-queue ordering.
The separate qualification suite exercises the launched binary against synthetic
upstreams. Provider translation and real-provider evidence are reported separately
in `docs/backend-qualification.md`; this document does not itself declare gate A
passed.

## Owned Codex login and refresh

`--owned-auth-dir` selects a separate private directory (default `auth-owned`
beside the snapshot auth directory). Nesting or sharing it with the snapshot
directory is refused. The private `ownership` registry contains only filenames
created by successful local login completion. An arbitrary copied credential file
is never promoted to owned status. Every saved token/registry update uses a 0600
atomic replacement followed by file and directory sync.

The following narrow endpoints require the management token:

- POST `/lain/login/codex` returns a state and authorization URL. Only one pending
  login can own callback port 1455; an existing listener yields a conflict.
- GET `/lain/login/{state}` returns `pending`, `exchanging`, `completed`, `failed`,
  or `canceled`, and sanitized result metadata. Completed results include
  `ownedAuthFile` (basename), `accountId`, and `expiresAt`, never tokens.
- DELETE `/lain/login/{state}` cancels pending/exchanging work. A late token exchange
  cannot persist after cancellation; completed logins cannot be canceled as if
  they had never been saved.

The OAuth callback binds only IPv4 loopback `127.0.0.1:1455`, checks an unpredictable
state, and uses the core's PKCE/token exchange implementation. No browser is opened
automatically. Login expires after ten minutes, state storage is bounded, and
shutdown cancels pending work. The Host adopts the result through an owned-file
reference and restarts with core credential ID `owned/<basename>`. It must not copy
or overwrite rotating refresh tokens. Newly created but unapplied credentials do
not dispatch or refresh.

The ownership registry AND membership in the current route bindings are required
for loading and refreshing an owned credential. Disabled, removed and unselected
credentials stay inert. The source registry file remains private even when the
corresponding public Account is removed. Automatic, request-triggered and explicit
refresh calls all check ownership; external snapshots fail those gates. Local
synthetic tests exercise successful owned persistence, cancellation with late
exchange, unregistered copied-file rejection, and own-only refresh. These tests
prove lifecycle isolation, not real provider refresh behavior.

A route may optionally set `provider` to its canonical commercial provider (for
example Command Code while using a Codex wire transport). Events use that identity
consistently; when absent they retain the core provider identity. The original
public route alias remains in executor requested-model metadata so imported
payload filter rules match it after middleware rewrites the transport model.
