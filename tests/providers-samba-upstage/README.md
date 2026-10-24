# SambaNova and Upstage fixtures

Requires Main's serialized build/runtime slot. No real credentials or external
provider requests. `Program.cs` checks actual embedded manifests, private
AccountService setup, model/endpoint/protocol validation, free-only rejection,
and actual Host writer output for two isolated synthetic accounts.

```sh
dotnet run --project tests/providers-samba-upstage/SambaUpstage.Tests.csproj
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
