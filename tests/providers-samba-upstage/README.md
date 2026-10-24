# SambaNova and Upstage fixtures

Requires Main's serialized build/runtime slot. No real credentials or external
provider requests. `Program.cs` checks actual embedded manifests, private
AccountService setup, model/endpoint/protocol validation, free-only rejection,
and actual Host writer output for two isolated synthetic accounts.

```sh
mkdir -p artifacts/tmp
TMPDIR="$PWD/artifacts/tmp" dotnet restore tests/providers-samba-upstage/SambaUpstage.Tests.csproj --disable-parallel -p:BuildInParallel=false -nr:false
TMPDIR="$PWD/artifacts/tmp" dotnet build tests/providers-samba-upstage/SambaUpstage.Tests.csproj --no-restore -m:1 -p:BuildInParallel=false -nr:false
TMPDIR="$PWD/artifacts/tmp" dotnet run --project tests/providers-samba-upstage/SambaUpstage.Tests.csproj --no-build --no-restore
TMPDIR="$PWD/artifacts/tmp" dotnet run --project tests/providers-samba-upstage/SambaUpstage.Tests.csproj --no-build --no-restore -- --provider upstage
python3 tests/providers-samba-upstage/probe.py --core /absolute/path/lain-proxy \
  --exporter tests/providers-samba-upstage/bin/Debug/net10.0/SambaUpstage.Tests.dll \
  --report /tmp/lain-samba-upstage-report.json
```

The Core probe calls the same fixture executable to compile each official
catalog-bound model first. The fixture then replaces only generated YAML's
upstream URL and the corresponding endpoint-derived synthetic credential hashes
with its private loopback server. Production validation is unchanged. Reported
SHA-256 values identify the supplied Core and exporter. The Core is copied into a
private temporary directory; only owned processes and files are removed.

The shared backend fake provider supplies Chat, stream, tool roundtrip and
cancellation. Provider-specific cases add 401/403/429/404, unknown public models,
a truncated stream without usage, bounded max_tokens=64, strict account identity,
and durable replay after restart. Truncation checks unknown usage; it does not
claim that every EOF is classified as an upstream error. These are synthetic
transport results, never live-provider or quota qualification.

2026-10-24 local Host qualification: restore/build succeeded, zero warnings/errors;
both fixture invocations passed 42 assertions each. Core probe was not executed
in the worker build slot and remains an integrated qualification step.

`ConnectionChecks.cs` runs automatically at the start of both .NET fixture
invocations above. It qualifies connection derivation and explicit manifest
validation, monitor guidance, settings/billing/model rejection before private
writes, empty versus explicit model selection in one revision, stable account
and route identities, name preservation, request redaction and legacy setup.
This connection-first extension awaits the integrated build after the original
42-assertion provider runs; no separate runner or live credentials are needed.
