# External CLIProxyAPI dependency and lain adapter

The inference implementation is **CLIProxyAPI v8.0.41**, released by
[router-for-me/CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.41),
commit `8289ec4c71ad914d0612c2776352b76ba3d355a6`, under the MIT licence in
[LICENSE.upstream](LICENSE.upstream). It is an external dependency, not a lain
implementation. The independent `lain.local/proxy` module in `adapter/` owns
lain's launch interface, binding policy, journal, replay, login ownership and
bounded inspector. Provider execution and protocol translation remain upstream.

The dependency is **not byte-identical to the official source after preparation**.
Two explicit patch files preserve contracts that the official SDK does not expose.
The official archive and Go module checksums, commit, exact patch hashes and changed
upstream paths are pinned in `upstream.json`. A build downloads/verifies the official
archive and applies those patches in a private temporary tree. It never changes the
module cache and does not vendor the upstream source into this repository.

`baseline.json` is historical metadata about the earlier imported local snapshot.
It is not the provenance of this build and its prior adaptations are not presented
as newly developed lain features.

## Why the official release binary and unmodified SDK are insufficient

The release executable does not implement lain's launch flags or `/lain/*` journal
and account boundaries. The SDK offers useful middleware, selectors, stores and a
final HTTP transport decorator, but its normal usage callback is asynchronous after
a RAM queue. Its numeric usage fields discard absent-versus-zero evidence. Service
startup also starts refresh before the public before-start hook, and later executor
re-registration replaces wrappers installed only once.

| Patch boundary | Missing external seam and smallest retained extension | Evidence |
| --- | --- | --- |
| `sdk/cliproxy/auth/{conductor,conductor_lifecycle,executor_decorator}.go` | A manager decorator runs inside every executor registration before publication, including initial refresh and later rebinding. Installed before registration; late installation is rejected. No lain-specific dispatcher calls in upstream. | Adapter `TestEveryExecutorRegistrationAndAPIKeyViewIsBound`, owned/external refresh and preparation tests. |
| `sdk/cliproxy/auth/conductor_selection.go` | Optional `SelectorCandidateFilter` narrows candidates before priority reduction; a normal selector runs too late to prevent an unrelated high-priority credential hiding a valid bound member. | `TestBoundLowerPriorityCredentialRemainsSelectable`; strict and pool 429 qualification. |
| `sdk/cliproxy/usage/{manager,durable}.go` | A request-context `DurableSink` runs synchronously before RAM enqueue. Sink failure suppresses enqueue; the adapter latches journal failure and withholds ordinary completion. Additive `ReportedDetail` retains nullable raw fields. | `TestDurableSinkPrecedesQueueAndFailureCannotEnqueue`; journal start/terminal fault blackbox tests. |
| `internal/runtime/executor/helps/{usage_helpers,reported_usage,plugin_executor_usage}.go` | Reporter honours the execution ID already supplied by its caller. Qualified OpenAI/Codex/Claude parsers carry raw field presence before upstream normalization; Claude merge and the streaming service-tier buffer retain explicit zeros. Original upstream normalized accounting stays intact. | `TestReportedUsageDoesNotInventTotalsOrEraseZero`; null/zero and retry identity qualification. |
| `sdk/oauth/codex/codex.go` | Public type/function reexports expose the existing internal OAuth exchange and token bundle to the host's owned-login lifecycle. No copied OAuth implementation. | Adapter login cancellation, durable owned persistence and unregistered-copy rejection tests. |
| `internal/config/config_types.go`, `internal/watcher/synthesizer/config.go` | Preserve existing `upstream-protocol` and Gemini `auth-header` settings; unmodified v8 otherwise ignores them. Stable credential ID calculation is unchanged. | Host configuration probes and provider fixtures. |
| `internal/runtime/executor/openai_compat_executor.go` | Choose the configured native Responses versus Chat protocol before upstream translation, preserving native continuation and terminal events. An HTTP decorator is too late to select the translation format. | `TestEmbeddingNativeResponsesTransport`; Host Chat/Responses continuation probe. |

Final HTTP session-header validation, Gemini bearer headers and the OpenRouter
zero-cap/free-model send guard use the **existing upstream `sdk/httptransport`
decorator** in the adapter. Their original transport, proxy and pooling behavior is
delegated. The fresh budget proof is made directly on the delegated transport and
is not observed as an inference response. Pinned inference rejects every 3xx before
the outer client can follow it, including a 307 to the identical endpoint. Tests
exercise the real decorator boundary, malformed intent, final body pinning,
redirects and unchanged shared client configuration.

The adapter also preserves optional executor capabilities (`ForAPIKey`, preparation,
format resolution, session cleanup, `apply_patch` and native tool search) when it
wraps providers. External snapshots reject refresh tokens at every JSON depth,
including v8 SIWC bundles. Only registry-owned and actively bound credentials can
reach refresh or auth preparation. Home mode, hot reload, dynamic plugins and the
original management surface remain disabled under lain's established contract.

Streaming completion remains gated on durable journal completion, including Chat
`finish_reason`, Responses completed/incomplete/failed events, Claude `message_stop`
and `[DONE]`. From the first terminal or choice-finish chunk, the held suffix is
limited to **8 MiB and 256 chunks**. Exceeding either limit cancels the provider,
drains its channel and returns a failed attempt without releasing success markers.
A later upstream error likewise suppresses held completion. Internal provider
cancellation does not turn that failure into a client-canceled usage event.

Native server-side **`image_generation` tools are deliberately excluded** from the
qualified usage contract: their additional-model usage cannot be represented by
the existing one-attempt journal schema. Automatic upstream image-tool injection
is disabled. Explicit native image tool controls are rejected with HTTP 400 before
dispatch; a final body check also blocks configuration payload overrides before
the provider transport, recording that already-started attempt as failed. Duplicate
`tools`, `tool_choice` or tool `type` controls are rejected to avoid parser ambiguity.
Ordinary function tools (including functions named `image_generation` or
`generate_image`) and Vision image input remain supported. This is an explicit
qualification limit, not a claim that every upstream v8 feature is exposed by lain.

## Build and qualification

Run `scripts/build-core.sh /absolute/output/lain-proxy` from the repository root.
Exactly Go 1.27.2 is required; set `LAIN_GO` if it is outside PATH. The resulting
single executable statically links the SDK. Its adjacent `.manifest.json` explicitly
records `upstreamSourceModified: true`, upstream provenance, patch-set hash, source
manifest hash and binary hash. Relative replacement paths make the private build
location independent of the resulting binary bytes.

`source-manifest.json` schema 2 separates upstream dependency, adapter files and
patches. `buildInputs` pins the complete build inputs, including the assembly
scripts. `scripts/build-core-verify.py` checks them before compilation.

For direct SDK and adapter tests, prepare a new private directory:

```sh
python3 scripts/prepare-core.py --destination /tmp/lain-v8-private-source
python3 scripts/prepare-core.py --verify-prepared /tmp/lain-v8-private-source/prepared-manifest.json
```

The returned `adapter` directory is a ready Go module with the relative replacement
`github.com/router-for-me/CLIProxyAPI/v8 => ../upstream`. The prepared manifest records
all source hashes for qualification and licence collection. The original archive
is cached under `$XDG_CACHE_HOME/lain/core-sources` (default `~/.cache`). An existing
verified module zip may instead be supplied with `--archive`.

Run adapter tests with `go test ./internal/...` in that prepared adapter. The patch
regressions run in the prepared upstream with:

```sh
GOWORK=off go test ./sdk/cliproxy/usage ./sdk/cliproxy/auth ./internal/runtime/executor/helps ./internal/runtime/executor \
  -run 'TestDurableSink|TestReportedUsage|TestEmbeddingNativeResponses'
```

Process qualification uses the built executable and synthetic providers inside a
private Linux network namespace. Successful mocks do not claim real provider or
OAuth refresh qualification. The existing public HTTP allowlist and journal wire
schema remain unchanged; an upstream version upgrade alone does not expose new
inference endpoints.
