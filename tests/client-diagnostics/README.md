# AP1 offline fixtures

Root alone runs `dotnet run --project tests/client-diagnostics/ClientDiagnostics.Tests.csproj`
in the shared serial build lane. No worker build or runtime was performed.

Integration entry point:
`Lain.Cli.Diagnostics.DoctorCommand.RunAsync(string[] args, TextWriter output, TextWriter error, Func<LainApiClient>? clientFactory = null, CancellationToken ct = default)`.
Pass command-tail arguments, before generic Host discovery. `help` and `--help`
need no Host. Exit 0 means a completed report (including blocked findings), 2
means invalid usage, and 3 means unavailable/cancelled diagnosis. No target gives
service metadata only. Project/profile require a supported target.

`DoctorEngine(IDoctorEnvironment).DiagnoseAsync(DoctorRequest, CancellationToken)`
permits deterministic filesystem/API/clock fixtures. JSON uses web camelCase,
schemaVersion 1, observedAt, target, scope, versions, findings. Finding fields:
code, state, explanation, source, nextStep. Values and exceptions from configs or
API payloads are never interpolated in findings. Scope is classified rather than
echoing caller paths/profile IDs. Only syntax-checked Host semantic versions are
reported. No tokens, login, refresh, inference, client execution or mutations.

Production API adapter uses only status/configuration GETs within one four-second
budget. Local reader rejects symlink ancestors and caps individual reads at 2 MiB;
the existing artifact inspector has its own 16 MiB per-file cap. The config parser
accepts generated JSON-quoted TOML string fields and simple table headers only;
other relevant syntax is unknown. No general native configuration parser is
available publicly without starting a native process. Root/profile static
selection is checked independently of installed artifact integrity. Ancestor
workspace conflicts are warnings; trust/precedence remain unknown.

Implemented follow-up observations: launcher schema 2/3 manifests expose numeric
version metadata; versions below 0.164.0 are warned about. Managed binary bytes
are compared with manifest and the two source-pinned qualified CLI hashes.
The adjacent code-mode helper is checked for existence and the source-pinned
helper hash. This helper differs from the Lain auth command: that declaration
is separately checked for an absolute existing command and exact auth/token
arguments. Root/profile string comments are supported; unsupported inline auth
or provider declarations yield unknown rather than a partial provider success.
`--profile` selects the native Codex profile name; export identity is compared
with the active Host profile independently.
Only the active Host profile's own revision is used for stale comparison.
Workspace presence no longer suppresses independent root/profile checks; their
findings describe static root/profile configuration, not effective workspace state.
Ancestor traversal is capped at 64 directories.

Default VS Code extensions.json selection is checked against a single matching
openai.chatgpt registry entry and its package publisher/name/version/location.
Versions below local baseline 26.1021.22028 are warned about. Registry/package
agreement does not qualify extension bytes or establish running activation.

Remaining boundaries: no launcher entry/symlink or PATH selection proof; no full
runtime package integrity, executable permission or helper execution check; no
Lain auth executable identity check. Manifest versions are metadata only. CLI
version pins come from clients/codex/qualified-releases.json; the helper also
matches clients/codex/code-mode-host-0.164.0.qualification.json. These are reviewed
source constants, not trust in arbitrary local manifests. CLI pin updates require
source review. VS Code profile overrides, remote/Insiders/custom extension roots,
complete VSIX content and running activation remain unknown. Common quoted table headers and repeated unrelated array tables are supported.
Multiline values remain conservatively unknown. Inline provider auth remains
unknown independently of root model/provider/catalog observations.
Binary hashing permits 512 MiB (including the observed 418673216-byte CLI),
checks cancellation and an eight-second per-file deadline between 1 MiB reads,
and rejects files growing beyond the cap. Regression source includes an
over-cap sparse fixture. No total filesystem deadline is enforced; individual read/hash caps and ancestor
count caps bound ordinary inputs, ordinary local files are required.
API enabled-account setup is not profile-specific authentication proof. Actual
payload tool/token counts and response quality are never inferred. AP1 is not
fully accepted until Root has run fixtures and closed or explicitly retained
these limitations in integration/delivery acceptance.

Tests use in-memory environments, an HTTP handler fixture and one temporary
over-cap sparse file; even the
offline command smoke cases omit targets to avoid reading the user's Codex home.
