# Desktop signal monitor tests

`dotnet run --project tests/desktop-signals/Lain.Desktop.Signals.Tests.csproj`

Pure in-memory HTTP fixtures and an explicitly advanced clock exercise the production
monitor. No Avalonia, desktop/session bus, local Host, provider, credentials or live
service is used. All requests must be GETs to the four documented Host endpoints.

Checks cover silent startup, real 429 attempt/retry identities and route attribution,
dedup/cooldown, bounded paging and honest coverage labels, independent quota windows,
freshness/unknown rejection, threshold/recovery/reset cycles, known shared-pool
dedup, bounded hover text, sanitized errors and cancellation.
