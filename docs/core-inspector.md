# Own-Core Inspector instrumentation

The Core exposes `GET /lain/inspector/events?after=0&limit=1000` only under its
management bearer. The inference credential cannot read this endpoint. Responses
have `Cache-Control: no-store`. This is a non-destructive RAM feed; it never reads
or consumes the legacy proxy queue, and it is independent of the durable journal.

The envelope contains `instanceId`, `earliestCursor`, `nextCursor`,
`highWatermark`, `droppedCount` and `events`. A process restart creates a new
instance UUID. Compare the instance before cursors. If an old cursor exceeds a
new instance's high watermark, the endpoint returns that new identity, an empty
page, and `nextCursor: 0`. Normal pages advance only to actual returned events;
there are no synthetic cursor-consuming gap events. An earliest cursor above the
requested successor exposes eviction. `droppedCount` is cumulative for the Core
instance, not a per-viewer missing-frame count.

Every emitted event has `scope: attempt`, the exact parent `requestId` allocated
by the existing handler, and its independent executor `attemptId`. Retries remain
distinct attempts of that request. The feed does not invent handler-level
request lifecycle events or claim that a tool call/spawn is a running inference.
Kinds are `attempt_started`, `metadata`, `text_delta`, `reasoning_delta`,
`tool_delta`, and `attempt_finished`. A `metadata` upsert enriches the existing
attempt; it does not create a conversation. Final live events use actual wrapper
`outcome` and retain known reported tokens/status separately from that outcome.

## Provenance

`dimensions` has optional values plus `evidence: [{field, source, key}]`.
Sources are `route_binding`, `harness_header`, `request_body`, `provider_response`
and `core`. Values are bounded to 192 ASCII characters and reject credential-like
material. Unknown values are omitted. No alias parsing, time-nearness, content
fingerprint, prompt cache key, LCP-derived session, or canonical inferred session
is admitted as grouping evidence.

Catalog facts enter through optional `Route.dimensions`: `modelFamily`,
`manufacturer`, `inferenceProvider`, `gateway`, `mode`, `profileId`.
`Bindings.accountDimensions[logicalAccount].billingAccountId` can supply an
explicit billing account. A logical routing account is not automatically a
billing account. These values require explicit Host configuration; the Core's
transport provider name cannot establish them.

Recognized request evidence:

- Claude headers `X-Claude-Code-Session-Id`, `X-Claude-Code-Agent-Id`, and
  `X-Claude-Code-Parent-Agent-Id` establish conversation/worker/parent worker.
  JSON-encoded `metadata.user_id` can supply only its explicit `session_id`,
  `agent_id`/`subagent_id`, and `parent_agent_id`. The exact legacy
  `user_…_account_…_session_<UUID>` shape supplies only its session UUID. Generic
  user IDs, surrounding device/account values and ambiguous parent-session values
  are never copied or promoted to worker identities.
- `Thread-Id`/`Thread_id` (preferred) or `Session-Id`/`Session_id` establish the
  conversation. JSON `X-Codex-Turn-Metadata` may provide `thread_id`, `session_id`,
  `turn_id`, `agent_id`, and `parent_agent_id`. A thread is not automatically a
  worker. Only an explicit `subagent_kind: thread_spawn` together with
  `thread_id` and `parent_thread_id` promotes those IDs to worker/parent-worker
  evidence. Ordinary forks and agent display names do not establish workers.
- Body `thread_id`, `metadata.thread_id`, `metadata.conversation_id`, `turn_id`,
  `metadata.turn_id`, `metadata.agent_id`, `metadata.subagent_id`,
  `metadata.parent_agent_id`, `metadata.parent_request_id`, `previous_response_id`
  and explicit `reasoning.effort`/`reasoning_effort` map to their corresponding
  dimensions. `reasoningEffort` means requested effort. The usage reporter's
  translated level fills separate `effectiveReasoningEffort` with `core` evidence
  key `usage.reasoningEffort`; it never overwrites the requested value.
- A single explicit Responses `function_call_output.call_id`, Chat tool message
  `tool_call_id`, or Claude `tool_result.tool_use_id` can supply `toolCallId`.
  Multiple tool results do not collapse to a made-up single ID.
- Explicit Responses response IDs, Chat completion IDs, or Claude message IDs
  supply `responseId` with `provider_response` evidence. Tool arguments and
  response event IDs are not conversation IDs.

## Measurements and durable finalization

The monotonic stopwatch starts immediately before persisting `attempt_started`
at the dispatch gate. It therefore includes that mandatory fsync. First output
is the first nonempty generated text/reasoning or tool name/ID/argument activity
observed at the executor output boundary. Role-only chunks, creation messages,
pings, usage reports and terminal markers do not establish first output.
The observation happens before display redaction and before forwarding that
chunk to the downstream handler. For non-streamed responses it occurs only when
the executor returns the full response, not at an imagined earlier token time.

`firstOutputLatencyMs` and `totalDurationMs` use monotonic differences.
`firstOutputAt` and `finishedAt` are corresponding UTC timestamps. Duration ends
at the wrapper's final hook, after its source stream has drained, before the
completion journal fsync and before releasing buffered terminal markers. It can
include downstream backpressure on intermediate chunks. It is not provider-only
decode time or final socket-delivery latency. The source is
`core_monotonic_v1`. Unsupported output shapes or individual payloads above 1 MiB
leave first output unknown. Only complete JSON/SSE data frames are interpreted.

Provider usage may be durably persisted before the stream actually closes.
That `usage` event has no invented full duration. The subsequent metadata-only
`attempt_finished` journal event carries final measurements/dimensions and a
separate `completionState` (`success`, `failed`, or `canceled`). It has null token,
outcome and status fields, and `usageReported: false`. Original usage facts stay
immutable. When no reporter produced usage, the final hook emits one unknown-
token `usage` event with the same final measurements and completion state.
Late cancellation thus remains visible without rewriting earlier token facts.

New `dimensions`, `measurements` and `completionState` fields are omitted when
unknown, preserving historical event serialization. Full-duration samples exist
only after the final hook. The Core emits no token-rate estimate or percentile;
consumers may divide known reported output tokens by a known positive full
duration with that denominator explicitly named.

## Capture policy and bounds

Core replay is limited to 4,096 events, 4 MiB serialized event content and 15
minutes, whichever limit evicts first. Host/UI retention may be shorter (the
initial Host content policy is 120 seconds); these are separate buffer lifetimes.
Core retention uses a separate monotonic receipt timestamp; UTC display times
and wall-clock changes cannot extend the content lifetime.
Limits are fixed Core constants, not inferred from Host DTO defaults. Expiration
is enforced lazily when the feed is read or new observations arrive. Pending
content expires at 15 minutes from attempt dispatch and cannot be revived by a
late completion.

Only assistant text and reasoning display fields are captured. Prompts, tool
arguments/results, auth headers, transport envelopes and raw error bodies are
never copied. Tool events show a bounded name/call ID plus an arguments-omitted
marker. Text is buffered until newline or completion, with a 2 KiB per-line cap;
an overlong line is omitted entirely. This intentionally delays a partial line
in the live display. Redaction sees assembled lines, so credentials fragmented
across transport chunks cannot bypass a per-chunk matcher. Known key prefixes,
JWT/bearer/password/token markers and long opaque values omit the entire line;
credential-marked lines also suppress the next line. Control and bidi characters
are removed. Arbitrary unmarked short secrets in model-generated content cannot
be identified exhaustively; this is a conservative display filter, not a general
data-loss-prevention claim.

At most 256 simultaneous attempts hold partial text/reasoning buffers (at most
two 2 KiB pending lines each, approximately 1 MiB total). Further attempts keep
their identities, timing and durable usage, with an explicit capture-capacity
omission marker. Content capture never controls dispatch admission, drops usage,
or relaxes the journal's fail-closed completion gate.

`Route.costPolicy` accepts missing/`configured` for existing routes. `free_only`
fails startup while no runtime cost enforcement mechanism has been qualified;
descriptive `pricingClass`/`freeOnlyEnforcement` strings cannot bypass this gate.
Unknown policies also fail startup.

## Qualification scope

Focused Go fixtures cover replay/reset/eviction, split-secret redaction, content
capacity/expiry, explicit correlation, protocol first-output classification,
early usage followed by canceled completion, metadata-only persistence,
historical optional-field omission, management authorization and dispatch
observation identity. They are source fixtures pending the root's serialized
Core test/build lane. This instrumentation change does not qualify a new provider
adapter, regenerate source manifests, replace the installed binary or run a live
provider request.
