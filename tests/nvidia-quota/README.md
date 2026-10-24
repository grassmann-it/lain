From repository root: `dotnet run --project tests/nvidia-quota/NvidiaQuota.Tests.csproj`.
Offline fixture handler only; no Build login, inference or account reads. Checks
fixed anonymous GET, native reference wording, absent numeric quota, changed/error/
oversized payloads, cancellation and precise setup prerequisites. The fixture is
the official anonymous runtime document retrieved during the 2026-10-24 research.
