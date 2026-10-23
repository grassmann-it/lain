# Provider transport boundary

Prepared on 2026-10-24. No live vendor credentials or inference were used.

`ProviderTransportValidation` is invoked before API-account secret persistence and
again from the runtime YAML compiler for catalog-bound routes. It checks route,
model, billing-plan membership, exact official endpoint, protocol and provider
settings. Cloudflare requires the account ID; Alibaba requires matching region,
workspace and service deployment scope. Unknown providers retain the existing
manual/legacy path. Missing catalog route selections fail before secret writes.

Native Chat and Responses use distinct compatibility provider identities even
when the key, model and base endpoint coincide. Responses is an explicit
`upstream-protocol` on the compatibility executor: native continuation IDs and
tool payloads are retained, JSON remains JSON, and streaming requires a native
terminal response event. It does not use the Codex executor's tool injection or
continuation rewriting. NVIDIA/Step reasoning and tool continuation remain on
the existing Chat translation path; their fixture and translator regressions
must run alongside transport tests.

Native Anthropic and Gemini use their existing executors with version segments
normalized once: Anthropic appends `/v1/messages`; Gemini appends
`/v1beta/models/...`. OpenCode requires `User-Agent: lain/1.0` and the dynamic
`x-opencode-session` header from the core session context. Gemini gateway auth
explicitly selects Bearer and removes the direct Google key header. Generated
direct headers participate in the core credential identity hash.

Validation commands (serialized by the integrator):

- `dotnet run --project tests/provider-transport/ProviderTransport.Tests.csproj`
- `go test ./internal/runtime/executor -run 'TestLain(NativeResponses|GatewayGemini|GatewayAnthropic)Transport|TestOpenAICompatExecutorNvidiaStep'`
- Existing OpenAI Responses/Chat translator regressions, particularly reasoning
  and tool continuation.

All HTTP fixtures use loopback fake providers. They prove local wire behavior,
not vendor availability, accepted account terms, live billing state or quota.
Provider catalogs and configured credentials continue to report live verification
as untested. No free-only billing cutoff is established by this transport work.
