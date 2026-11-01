# Recording policy qualification

Synthetic, offline policy checks. No installed runtime is touched. Run in the
integrator's serial build lane:

```sh
dotnet run --project tests/recording-policy/RecordingPolicy.Tests.csproj
```

The executable links the policy source and references Core only; it adds no
package dependency and does not build the Host. Checks cover inert ephemeral policies, production first-install content defaults,
persisted stops and invalid-policy fail-closed behavior,
explicit all-model gateway scope, independent revisions, stale queue/stop gates,
route filters, secret fields and patterns, nested tool arguments, malformed JSON,
UTF-8, unsupported/split SSE, size limits, atomic policy persistence and symlinks.

`RecordingPolicyService(string? directory = null)` implements `IRecordingPolicy`.
Pass the dedicated recording directory in production. `null` is an ephemeral test
policy. Production passes `initializeContentCapture: true`: only an absent policy
gets a persisted content-enabled revision 1. Existing off/invalid policies are
never overwritten by that installation default. `Snapshot` returns a copy. `Update` has optimistic recording-only revision
checks. Any successful change increments revision and stop generation. A disable
closes admission under the policy lock before control-file I/O; a failed file save
leaves this process off and reports `policy_save_failed`. A failed persistent stop
cannot promise restart remains off: the integrator should surface that error and
keep the source stopped. No live policy file watching occurs.

The collector must stamp observations with the snapshot revision/generation that
admitted them, pass that same snapshot to `Sanitize`, and discard queued older
records on policy change. The durable store must call
`IRecordingPolicy.IsCurrent(observation.PolicyRevision, observation.StopGeneration)`
at its final admission point before starting a bounded commit. Writes that already
passed that gate may finish after stop; stop is distinct from physical deletion.
Do not re-stamp old queued bytes with a newly enabled revision.

The source handoff must accept only the typed source fields, reject unknown JSON
members (especially headers/auth objects), and authenticate the owned loopback
source. A claimed client name, native conversation ID or User-Agent is not client
isolation. Route filters use actual selected route IDs.

Content is retained only from complete full JSON with encoding `utf8_json`, `json`
or `application/json`. Raw SSE/deltas/fragments are omitted with a gap. A normalizer
may assemble streams transiently and submit a complete full JSON representation
for sanitization before any durable write. Never persist independently redacted
fragments: a secret can cross chunk boundaries. Basic request JSON and completed
response JSON are supported; unsupported and partial representations stay visible
as metadata/gaps. Argument strings must contain valid JSON or are omitted.

Defaults are tunable engineering defaults: 16 MiB record, 64 MiB queue, 5 GiB disk,
7 days content and 30 days metadata. They are not claimed as measured acceptance
budgets. Queue/disk/retention enforcement belongs to collector/storage.

The sanitizer excludes known secret fields, bearer/basic/JWT/provider-token forms,
secret assignments, credential URLs, sensitive URL parameters, PEM private keys,
and contents mentioning common credential files. It cannot recognize every
possible secret encoded or embedded in ordinary prose. Ordinary code and messages
are retained where supported. No absolute secrecy claim, training gate, encryption,
cloud upload or provider change is introduced.

At initial handoff the suite is prepared but deliberately not executed, respecting
the shared serial build/test lane. Record actual source pins and results separately.
