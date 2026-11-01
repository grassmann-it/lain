# Observability audit for the proposed passive recorder

30 October 2026 · **Planning/read-only audit. No recorder implementation.**
The user's expanded training-logging brief requests audit followed by Sprint 1;
the subsequent instruction explicitly changes the present task to **plan before
building**. No new recorder source, configuration, migration or capture enablement
has been applied. Existing unrelated work and the earlier installed Desktop fix
are preserved.

## Current scope revision

The user explicitly removed archive encryption/key management from Sprint1 and
prioritizes local human inspection across all recorded models, including comparing
skill versions. Preserve private file permissions and secret exclusion. Unknown
training rights do not block ordinary local inspection; training/export remain
separate. The earlier encrypted-archive design is historical, not the current
requirement. See [the revised plan](training-logging-plan.md).

## Verified baseline

- Installed CLIProxyAPI v8.0.41, upstream
  `8289ec4c71ad914d0612c2776352b76ba3d355a6`, lain patch **4**.
  Binary SHA-256 `191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`
  freshly matches its installed build manifest. Checkout is patch **5**, not the
  same runtime. Installed provenance contains its adapter source.
- Go 1.27.2; embedded build metadata: exe/gc, trimpath=true, CGO_ENABLED=1,
  linux/amd64. This metadata does not prove every compiler/linker flag or an
  unrecorded build environment; retain the actual build manifest when freezing.
- `lain.service` remains active. Host version
  `1.0.0+835d2e2260dfa3f1b072eb8fcc1541c33c6dd665`; Gateway ready,
  desired919/applied917 at the audit read. No Apply or service restart performed.
- Core/Host on loopback18417/18418. Private config/bindings/journal paths belong
  to lain. Safe config projection confirms ordinary request logging, file logging
  and SDK usage statistics off. Adapter launcher additionally disables dynamic
  plugins/discovery/control panel. No auth values were printed.
- The installed package checksum manifest was verified during Desktop delivery.
  The Desktop-only update did not replace Host/Core or add recording.

The detailed prior [gap analysis](TRACE_GAP_ANALYSIS.md) records client/config,
provider inventory, durable accounting, RAM Inspector and error-log distinctions.
Its runtime observations are dated; do not treat desired inventory as applied or
all configured clients as proven live paths.

## Current paths and representation boundaries

| Observation point | Existing source | Useful evidence | Limit |
| --- | --- | --- | --- |
| Client ingress | adapter `http.go` existing bounded body parsing, original-request context | API variant, submitted controls/body, reported harness identities | No persistent conversation content; credentials/headers aren't safe capture data |
| Normalized handler/executor request | preserved original vs rewritten model, SDK translation boundary | Correlation with selected route and actual attempt | Original, normalized and final provider request differ; tag each observed phase |
| Final upstream HTTP | adapter executor transport and provider SDK | Potential provider-boundary body/status/request-ID hooks | Must qualify per executor/transport; cannot infer WebSocket coverage from HTTP RoundTrip |
| Executor response/chunks | `boundExecutor.Execute/ExecuteStream`, `Integration.Observe` | Returned content before downstream translation, first-output measurements | Not guaranteed final client bytes; terminal suffix/error rewriting remains existing behavior |
| Client egress | SDK response writer/handler boundary | Final emitted body/frames and status | No current complete passive archive; wrapper must preserve optional interfaces/flush/cancel/errors |
| Usage completion | Integration.Before/Publish/After and durable journal | Stable request/attempt, nullable reported tokens, route/account, completion/provenance | Accounting is fail closed; recorder must not change it or acknowledge its journal |
| Display | Core Inspector + Host collector/store + Desktop | Bounded redacted selected output with gap/source cursors | Deliberately omits prompts/tool args/results, transient and not byte-original |
| Local execution | Future client adapter | Command execution, approval, patches/tests/user acceptance | Not observed by proxy; client-submitted result alone is a claim |

## Plugins and hook suitability

The pinned v8 SDK actually contains request interceptors before/after auth,
response interceptors and stream interceptors; this is not assumed from current
website documentation. Source: `sdk/api/handlers/handlers_interceptors.go`,
`handlers_stream.go`, `sdk/api/options.go`, `sdk/cliproxy/builder.go`, and
`sdk/cliproxy/usage/manager.go` in the pinned module cache.

- `WithServerOptions(api.WithMiddleware(...))` is present and actively used by
  the adapter. An embedded observer can fit the existing integration boundary.
- SDK interceptor callbacks can rewrite headers/bodies, terminate requests or
  drop chunks. They are not inherently passive or free of stream-path latency.
- `HistoryChunks` is capped at64 chunks/1MiB in this SDK; schema-v5 stream calls
  omit it entirely. It is not a complete archive or recovery mechanism.
- `WithRequestLoggerFactory` exists, but the logger accepts sensitive full
  headers/bodies/errors. It is not a safe metadata DTO or a storage design.
- `usage.Plugin.HandleUsage` exists. Record contains secrets-bearing fields
  including APIKey, response headers and failure body. Its manager's RAM slice
  queue is not a bounded durable archive; never serialize a Record wholesale.
- The actual lain launcher sets `cfg.Plugins.Enabled=false`. This does **not**
  mean all interceptor entry points require enabling dynamic plugins:
  `api.WithRouterConfigurator` receives the `BaseAPIHandler`, whose public
  `SetPluginHost(PluginInterceptorHost)` accepts a narrow static implementation.
  The four callback methods are in `handlers_interceptors.go:21`. **Recommendation:**
  first a synthetic integration spike for a static passive bridge with dynamic
  plugins still disabled, before adding new upstream hooks.
- A static bridge must implement `StreamChunkPayloadIncludesRequestBody=false`
  and `StreamChunkPayloadIncludesHistory=false`, plus precise detector methods;
  unknown hosts otherwise trigger per-chunk history/request copies. Return no
  mutation fields: nil headers preserve them, an allocated empty map may remove
  them. `server_reload.go` sets the handler host again, so reload lifetime is a
  required spike test, not an assumed property.
- After-auth observes a pre-executor representation, not the final provider
  request. Response/stream interceptors run before later validation/writes;
  they are `handler_output`, not proof of bytes emitted to the client. Optional
  CompleteRequest/ObserveWebSocketResponseEvent methods provide additional events,
  not complete WS request capture. Actual client egress needs a qualified send-
  side observation; avoid introducing a new writer wrapper just to make a label
  appear complete. A minimal upstream write hook is a fallback to qualify.

Old error-only logging is separate: upstream request_logging middleware can force
error logs even when regular request-log is false. Existing error artifacts were
counted in the prior audit, not read/imported here. Their policy must be reviewed
before claiming the recorder is the only content store. Do not duplicate that
logger or enable it as a shortcut.

## Planning conclusions

1. Prefer bounded Core observation and independent Host-owned archive persistence;
   preserve both the current accounting journal and RAM Inspector contracts.
2. Correlate conversations/turns/attempts/calls/workers only from scoped IDs and
   explicit provenance. Add local IDs without pretending they are external IDs.
3. Distinguish a forwardable protocol route from a parsed fixture and from a
   qualified recorder path. See [coverage matrix](capture-coverage.md).
4. Repeated request histories can consume substantial disk. Begin with bounded
   compressed/checksummed records, data references and measured retention;
   cross-record CAS is a proposed optimization with its own deletion/commit tests.
5. No archive encryption/key unlock is now requested. Capture identity, retention,
   disk/loss behavior and scope still need explicit design. Private permissions
   and credential exclusion remain; do not turn training eligibility into a
   blanket block on local model/skill inspection.
6. No cloud sink, real transcript replay, inference test call, legacy-log import,
   training, prompt modification, client installation or collector deployment
   is part of the current planning task.

## Proposed implementation and qualification

[The Sprint 1 plan](training-logging-plan.md) specifies ownership, dependencies,
pre-build decisions, functional/fault/latency gates, rollout and rollback. It is a
plan, not evidence of passing recorder tests. The earlier Inspector UI benchmark
measures only that component and cannot substantiate recorder overhead.
