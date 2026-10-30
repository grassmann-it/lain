# REC06 integration and verification

Contract: `RecordingContract.Version == 1`. This package provides
`AddRecordingCollection(directory, coreAddress, managementToken, shouldCollect)`
and `MapRecordingEndpoints()`. Root must register IRecordingPolicy,
IRecordingStore, and IRecordingProjection before constructing the collector.
`shouldCollect` must be the existing Core/Gateway availability predicate. Core
must independently read the **same** private recording `policy.json`; this
collector verifies its acknowledged revision via `/lain/recording/status` and
reads only `/lain/recording/events`. Stop also uses authenticated
`POST /lain/recording/disable`. No profile Apply/configuration endpoint is used.

REC03's `RecordingStreamAssembler.Push` runs before policy sanitization and
persistence. Each stream fragment produces a metadata-only durable source record;
a terminal assembled JSON document is sanitized before its commit. No raw SSE
fragment is retained. Core's default `captureRawSse=false` means only available
assembled terminal representations reach Host; unsupported streaming content is
explicitly unavailable. Enabling raw transient handoff requires explicit source
policy agreement and is not silently enabled by this package.

The collector is one sequential consumer with one bounded HTTP page in flight.
A journal commit advances its source sequence; a projection failure marks rebuild
required. Source cursor restoration scans bounded committed pages. Source epochs,
monotonic per-page order, schema, policy generation, body/page bytes, high watermark,
explicit gap ranges and continuation cursor are checked. Explicit evictions are
journaled as local `source_gap` records. Crash completeness remains unknown.

API routes (all inherit existing Host token middleware):

- GET status/policy, PUT policy
- GET conversations (summary only), GET conversations/{id} (explicit content)
- GET events (explicit sanitized committed bodies)
- GET/PUT conversations/{id}/runs (explicit configured provenance only)
- DELETE archive?expectedRevision=N (requires recording off)

All routes are under `/api/v1/recording`. Policy limits cap pages; explicit body
reads omit bodies beyond a 4 MiB response budget with `api_body_limit`. No paths,
payload-file references, arbitrary URLs, credential arguments, filesystem scanning,
training-rights gate or inference/provider operation is exposed.

Errors: stale_revision/recording_must_be_off/cursor_stale/cursor_expired -> 409;
unsupported policy/invalid query -> 400; absent conversation -> 404; auth -> existing
401 middleware. Collector errors are fixed codes, never source response text:
source_unauthorized, source_unsupported, source_policy_pending, source_reset,
source_invalid, source_cursor_invalid, source_gap, source_oversize,
source_redirect_rejected, store_commit_failed, storage_unavailable, collection_failed.

Integrator CLI wiring in existing CliApplication.cs:

- validation: `if (command == "recording") { ValidateRecording(options); return; }`
- execution: `case "recording": result = await Recording(client, options, ct); break;`
- help: recording status/config/enable/off/list/show/events/runs/run-label/delete archive.

`enable` requires explicit `--mode content|metadata_only --revision N`; mutations
require recording revision. `show` and `events` explicitly request bounded content.
Run label syntax: recording run-label CONVERSATION_ID RUN_ID --revision N
[--variant TEXT] [--configured-revision TEXT] [--prompt-hash SHA256]
[--skill-hash SHA256]. Both JSON and normal existing CLI formatting work.

Run only in the shared serial verification lane after REC03 is integrated:

```
dotnet run --project tests/recording-api/RecordingApi.Tests.csproj
dotnet run --project tests/recording-cli/RecordingCli.Tests.csproj
```

The API harness includes synthetic store/projection faults, durable cursor replay,
fragmented-secret redaction, default-off gating, Core unavailability, redirect/auth
rejection, address validation and body caps. CLI fixtures use the common client
and require root's dispatch wiring. No production configuration or provider calls.
