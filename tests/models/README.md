# Models and Codex verification

`dotnet run --project tests/models/Models.Tests.csproj` checks revision conflicts,
persisted updates, strict versus pool routes, provider/account validation,
profile references, placeholder exports, visible catalog selection, and launcher
binding JSON. During parallel development, pass
`-p:OperationsSource=/absolute/path/to/W3/src/Lain.Host/Operations`.

`python3 tests/models/codex_probe.py` additionally starts a synthetic loopback
Responses server and runs installed Codex in a temporary `CODEX_HOME`. It accepts
the same development override as `--operations-source`. No provider credentials
or existing Codex settings are used. Local sockets must be permitted.

Verified locally on **Codex CLI 0.162.1**, 2026-10-23:

- `--profile lain` loads `lain.config.toml` beside the base config; the fixture's
  base model sentinel remains unchanged and is overridden by the profile.
- `--strict-config --no-daemon --profile lain exec` accepts generated config.
- The model catalog selects the generated route alias and excludes hidden routes.
- Provider command auth executes the absolute helper with exactly
  `auth token --audience codex`; its stdout becomes the bearer header on
  `/v1/responses`.
- A synthetic SSE answer completes successfully.

Official OpenAI documentation fetched and read on the same day:
[configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference).
It documents external profile files, per-profile `model_catalog_json`, and
`model_providers.<id>.auth.command/args/timeout_ms/refresh_interval_ms`.
Command auth must not be combined with `env_key`, `experimental_bearer_token`, or
`requires_openai_auth`; the generator omits those settings.

The adapter only generates artifacts; it does not install anything or change
`~/.codex/config.toml`. Install `lain.config.toml` and the generated catalog at the
absolute catalog path, then launch `codex --no-daemon --profile lain`. Model
capabilities not established by the route DTO are not advertised. The initial
catalog explicitly lists configured models (`source: configured`); this is not
an upstream availability or compatibility assertion.

For a standalone export folder, place both generated files there and launch
`CODEX_HOME=/absolute/export/folder codex --no-daemon --profile lain`.
Codex resolves `lain.config.toml` beneath `CODEX_HOME`; the launch arguments alone
use its default existing home. The profile uses top-level keys, not a nested
`[profiles.lain]` table in this verified Codex version.

Host integration: register `ModelsService` and `CodexProfileAdapter` as singletons
and call `MapModelEndpoints` behind the host authorization boundary. The Codex
export endpoint is `POST /api/v1/profiles/{id}/codex/export` with
`{revision, executablePath, catalogPath}` and returns `CodexProfileArtifacts`.
Use `ResolveForApply` before runtime apply. `RouteBindings.Compile` builds the
launcher startup contract using credential IDs from W3's config writer; the host
must restart/apply successfully before calling `ConfigurationRepository.MarkApplied`.

Multi-transport bindings accept an optional third argument keyed by route ID,
then logical account ID, to the route-specific core credential ID. The runtime
writer supplies this as `ProxyConfigurationWriter.RouteCredentials`. JSON emits
`routes.<alias>.credentials` for those routes while retaining stable logical
IDs in `accounts`. An explicit route map must cover exactly its members; missing,
foreign, blank, or conflicting credential ownership fails compilation. Without a
route map, the original global account mapping remains supported. Two transports
of one logical account do not create two account or budget identities.

Every enabled route remains in the launcher bindings, including hidden routes.
The `visible` field preserves model-menu visibility separately from callability;
legacy bindings without this field default to visible.
