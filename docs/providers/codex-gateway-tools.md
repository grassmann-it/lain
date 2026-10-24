# Codex Gateway tool compatibility

Diagnosis on 2026-10-24, installed Codex CLI **0.164.0**. No provider requests,
personal credentials, token files, installed config writes, or Core processes
were used by this worker. Root separately owns the user's installed lain profile.

The selected `lain.config.toml` profile uses `mimo-v2.6-pro`, a local Responses
endpoint and a local 45-entry catalog. It originally had no web-search or MCP
override. All relevant MiMo/Glyph/Beam/Laguna entries used `unified_exec` with
`experimental_supported_tools: []`; they omitted explicit search/tool-mode/patch
metadata. That empty list does not disable all Codex built-in tools.

## Distinct failures

| Reported route | Failure | Appropriate action |
| --- | --- | --- |
| `mimo-v2.6-flash`, `mimo-v2.6-pro` | Native Responses `web_search` unsupported | Explicitly disable native search in the existing separate lain profile. Keep the user's selected model. |
| `cmd-glyph-cluster-free` | Provider-defined tool unsupported | Inspect actual tool types. Codex can emit a `namespace` even when custom patch tools are absent. Do not claim a function-format patch setting exists. |
| `cmd-beam-free` | More than 128 tools | Limit the exposed MCP/plugin tool set deliberately before a session using documented server allow/deny lists. No proxy truncation. |
| `cmd-laguna-s-2.1-free` | HTTP 429 | Separate capacity/rate-limit state; changing tool schemas does not repair it. No paid fallback. |

The export generator now emits top-level `web_search = "disabled"` in the same
`lain.config.toml`, plus `supports_search_tool: false` and `tool_mode: null` in
each model entry. It does not edit normal Codex configuration, remove MCP tools,
change the selected model, or mutate requests. The model catalog intentionally
does not invent an `apply_patch_tool_type: "function"` capability.

## Exact 0.164.0 schema and wire evidence

The installed binary's offline local-catalog parser rejects patch values `bogus`
and `function` with **expected `freeform`**. Both `freeform` and JSON `null` parse;
`supports_search_tool: false` and `tool_mode: null` parse and roundtrip correctly.
This is version-specific evidence, not a promise about future clients.

An isolated Codex process, empty `CODEX_HOME`/`HOME`, synthetic environment key,
empty workspace and ephemeral loopback Responses fixture produced:

| Patch metadata | Multiagent | Tool count | Observed nonfunction types |
| --- | --- | --- | --- |
| `freeform` | Default enabled | 9 | `custom` named `apply_patch`; `namespace` named `multi_agent_v1` |
| `null` | Default enabled | 8 | `namespace` named `multi_agent_v1` |
| Omitted, as in the existing export | Default enabled | 8 | `namespace` named `multi_agent_v1` |
| `null` | Explicitly disabled in fixture | 7 | None; all seven tools have type `function` |

Each fixture received one POST and returned a synthetic terminal response. No
tool was executed. The existing omitted patch field already avoids custom patch
advertising in this clean fixture, so changing it to null is **not** evidence of a
Glyph fix. Setting `[features] multi_agent = false` is a concrete client-side
option for a provider that accepts only ordinary functions, but disables agent
delegation in that profile; the exporter does not apply that change silently.
File editing can still be performed through command tools when no native patch
tool is advertised. Neither fixture qualifies real Glyph behavior.

The nonsecret user configuration inventory has **two direct MCP server entries**
(`node_repl`, `sentry`) and **ten plugin entries**. No MCP server was initialized,
so the effective request tool count is unknown. The clean fixture's 7/8/9 tools
must not be represented as the user's full tool set.

## Control tool volume before requests

Official configuration supports `mcp_servers.<id>.enabled_tools` (allow list),
`disabled_tools` (deny list applied afterward), and `enabled = false` for a
specific server. Plugin MCP servers support the same controls under
`plugins.<plugin>.mcp_servers.<server>`. These can be placed in the existing
separate lain profile without changing the normal profile.

Choose the servers/tools needed for the task, inspect their nonsecret discovery
metadata, then keep the **total** exposed tool count below the route's limit,
including native Codex tools. A server count alone cannot establish that total.
Do not cut an arbitrary request down to 128 definitions, promise an untested
tool-search optimization, or disable every MCP server without user intent.

For a deliberate **single-run test**, the documented CLI `-c` overrides leave
all files unchanged. These options are independent; choose only those relevant
to the intended task:

```sh
# Avoid the observed built-in namespace tool for this run; agent delegation is unavailable.
codex --no-daemon --profile lain -c 'features.multi_agent=false'

# Exclude one known direct MCP server for this run; other servers remain configured.
codex --no-daemon --profile lain -c 'mcp_servers.sentry.enabled=false'

# Exclude one known installed plugin for this run, when its tools are unnecessary.
codex --no-daemon --profile lain -c 'plugins."gmail@openai-curated".enabled=false'
```

The corresponding direct-server allow-list key is
`mcp_servers.sentry.enabled_tools`; populate it only with exact tool names from
that server's discovered metadata. For a plugin server the key is
`plugins."<installed-plugin-id>".mcp_servers.<server-id>.enabled_tools`. An
empty or guessed allow list is not a reliable useful-tool selection. Applying
one override does not guarantee a total below 128; count the resulting tools
before claiming Beam compatibility. These commands are explicit options, not
automatic retries or a change to the generated profile defaults.

## Qualification

`tests/codex-gateway-profile/capture_tools.py` reproduces the isolated installed-
binary metadata capture. `Program.cs` checks the real Host export for profile
isolation, preserved model selection, explicit native-search disable and honest
catalog fields. The .NET fixture is prepared and awaits Main's build slot.
Installation and real provider inference remain unverified by this package.

Official OpenAI Docs checked 2026-10-24:

- [Profiles and CLI overrides](https://learn.chatgpt.com/docs/config-file/config-advanced#profiles): the selected `<name>.config.toml` overlays base config; project/CLI layers can still override it.
- [Configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference): `web_search = "disabled"` removes the native tool; MCP and plugin tool controls are distinct.
- [MCP configuration](https://learn.chatgpt.com/docs/extend/mcp?surface=cli): per-server enabled/allow/deny controls.
- [Implementing model catalogs](https://learn.chatgpt.com/docs/model-catalogs): use the exact client-version schema; metadata does not add provider capabilities.
- [Bedrock Gateway catalog guidance](https://learn.chatgpt.com/docs/enterprise/bedrock-through-litellm#prepare-the-client-catalog): explicit `tool_mode: null` and `supports_search_tool: false` are documented gateway adjustments; this does not prove unrelated provider support.
