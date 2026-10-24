# ST-01 model compatibility

`dotnet run --project tests/model-compatibility/ModelCompatibility.Tests.csproj`
checks the bounded Host compatibility matrix, fact provenance, explicit unknown
states, exact provider/model/protocol scope, preview request diagnostics and pure
Codex export. These are synthetic tests. Core dispatch enforcement is a separate
adapter fixture and must pass before this package is considered integrated.

Client schema/tool metadata is qualified only for Codex CLI 0.164.0. A known
compiled route limitation still applies when the client schema is unqualified;
client-supplied version claims cannot bypass actual Core validation. The exporter
keeps native web search disabled, preserves the selected model and all configured
MCP/plugin tools, and exposes a separate compatibility manifest. A model template
needs a checked route-specific source and explicit runtime transport qualification
before its documented limits are exposed. Template identity alone does not qualify
the selected account endpoint.
Imported models do not borrow capabilities by family or display alias.

For canonical provider `xiaomi-mimo` and exact models `mimo-v2.6-pro` and
`mimo-v2.6-flash`, the known MiMo web-search failure is a local Responses-to-Chat translation
constraint, not evidence that MiMo's native endpoint rejects search. Glyph's
observed provider-defined tool rejection and Beam's 128 transmitted-tool limit
apply only to exact CommandCode upstream models `stealth/glyph-cluster:free` and
`reflection/beam:free` on the qualified bridge. Display aliases never select policy.
The tool limit is checked only with `finalWireRequest: true`, against the final
transmitted top-level tools array. Original request cardinality may change during
translation and is not a definitive wire count; nested declarations
are separately counted for diagnostics and types include namespace/custom.

The `fixtures/client-capture.json` evidence, when present, is produced by the
existing `tests/codex-gateway-profile/capture_tools.py` runner against disposable
loopback responses using empty HOME/CODEX_HOME and a synthetic key. No user config,
credentials, tools, provider inference or running Core is used. This does not
qualify real Glyph/Beam/MiMo behavior or the user's complete MCP/plugin toolset.

Context limits remain source-scoped documented facts. A serialized request byte
count is not a token count, and no token estimation/context claim is invented.
Streaming/tool continuation provider behavior still requires separate adapter and
real-provider qualification. Unknown capabilities are allowed, never converted
into false support or automatic tool removal/paid fallback.

Local result on 2026-10-24: **49 new assertions + 14 existing export assertions
passed**. Four fresh Codex 0.164.0 captures matched tool counts 9/8/8/7 and
namespace/custom types. See [qualification.json](qualification.json) for the
remaining integration/provider/context qualification boundaries. These checks
used no provider inference or installed configuration writes.
