# Presentation preferences checks

Run `dotnet run --project tests/presentation/Presentation.Tests.csproj` from the
repository root. The console harness source-includes only the presentation
contract and store: it needs no Avalonia runtime, display, host, credentials or
provider access. All test files and `LAIN_HOME` values are temporary and removed.

Checks cover defaults, reload and ordered pins, strict validation, invalid/corrupt
file fallback and repair, private file modes, defensive snapshots, atomic write
failure and cleanup, an unavailable save lock, stale-window field merging,
symbolic link refusal, and configuration/revision isolation.

Default storage is `$LAIN_HOME/ui/workspace-preferences.json`, or
`Environment.SpecialFolder.LocalApplicationData/lain/ui/workspace-preferences.json`
when `LAIN_HOME` is unset. Saves use a private sibling lock and an atomic sibling
temporary-file replacement. Separate windows merge fields changed since their
own last snapshot; edits to the same scalar setting or pin list use the last save.
Density changes merge per primary destination. Missing density entries mean
Normal. Pins retain their explicit order and are bounded to 512 unique account
IDs (1–100 ASCII letters, digits, hyphen or underscore each).
