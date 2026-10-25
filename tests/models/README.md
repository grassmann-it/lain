# Models and Codex verification

`dotnet run --project tests/models/Models.Tests.csproj` checks revision conflicts,
persisted updates, strict versus pool routes, provider/account validation,
profile references, placeholder exports, visible catalog selection, and launcher
binding JSON. During parallel development, pass
`-p:OperationsSource=/absolute/path/to/W3/src/Lain.Host/Operations`.

Collection cases additionally cover legacy JSON without collection fields,
ordered overlapping route references, shared revision conflicts, restart and
snapshot isolation, validation, atomic removal of deleted route references,
derived provider groups and synthetic loopback HTTP CRUD. They never call an
upstream provider or create executable autorouter targets.

Collections use GET `/api/v1/collections`, PUT `/api/v1/collections/{id}` with
`{revision,value}` and DELETE `/api/v1/collections/{id}?revision=...`.
GET `/api/v1/model-groups` returns a revisioned read-only view of provider and
custom groups. Collection saves preserve profiles and applied revisions;
membership alone does not enable or export a route. `RouterTargetId` must be
null until routing is separately qualified. A collection named Free makes no
cost guarantee.

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
The optional `provider` field carries `RouteDto.Provider` so usage can identify
the canonical provider even when its core transport uses a different provider
code. Legacy bindings leave this field unspecified.

Codex export presentation regression coverage also lives in
`tests/codex-gateway-profile`: human-readable catalog model/service labels retain
all inference slugs, manufacturer metadata stays distinct from the inference
operator, and presentation does not qualify transport capabilities. The exact
Codex `codex-auto-review` upstream identity keeps native `hide` visibility even
when its route alias differs. A profile with that model as explicit default, or
with no ordinary selectable model, fails export with a clear validation message;
an implicit default uses the first ordinary selectable model. Same-named aliases
or upstream models on other services do not inherit that internal role.
These cases are synthetic fixtures; they do not establish provider acceptance or
modify the normal Codex configuration.

The two-stage model picker is a separate, versioned **lain-patched Codex** client
extension. `CodexProfileAdapter.Generate(..., enableLainModelPicker: true)` and
`CodexExportRequest.EnableLainModelPicker` explicitly opt in; both default to
false while the normal native catalog remains `models` only. The extension is
`lain_model_picker: {schema_version: 1, groups: [{id, name, router_model, models}]}`.
It is generated only within the existing separate `lain.config.toml` export for
Codex `--profile lain`; Host profile IDs can differ. Normal Codex configuration
and launchers are untouched. An unmodified Codex client is not qualified for this
extension.

Picker export tests in `tests/codex-gateway-profile/PickerCases.cs` cover provider
and overlapping custom collections, profile/visibility intersection, stable
aliases and collection identities, empty groups and the internal review-model
filter. A derived `policy:free` group includes only `free_only` policy-allowed
routes; a user collection's name does not establish a cost guarantee. Every
`router_model` remains null: the patched client displays a disabled automatic
entry, and individual choices use their original inference aliases. This menu
extension does not implement automatic routing. Grouping changes no profile,
account, activation, default model, or normal native model entry. Fixture execution
and patched-client qualification are separate release checks.
