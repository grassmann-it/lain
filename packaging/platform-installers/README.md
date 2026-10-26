# Native package contract v1 (separate from Linux)

This directory and the two platform entry points must travel together. Python
3.10+ is an explicit installer dependency (on Windows: `python` on PATH, not just
an App Execution Alias). No bootstrap download is performed. Windows PowerShell
5.1+ is sufficient for the entry point. Native signing/notarization is external.

An extracted package has exactly these top-level namespaces:

- `platform-package.json`, `native-qualification.json`, `LICENSE`, `NOTICE`, `SHA256SUMS`
- `cli/`, `host/`, `desktop/`, `licenses/`; `proxy/` only for Gateway

`platform-package.json`:

```json
{"schemaVersion":1,"runtime":"osx-arm64","runtimeMode":"monitor"}
```

Runtimes: `osx-x64`, `osx-arm64`, `win-x64`, `win-arm64`. Executables are
`cli/Lain.Cli`, `host/Lain.Host`, `desktop/Lain.Desktop`, plus
`proxy/lain-proxy` for Gateway; Windows adds `.exe`. Dependencies and third-party
licenses must be complete. macOS requires thin Mach-O files with execute bits;
Windows requires PE files matching the target architecture. Universal Mach-O
packages are intentionally unsupported. Package paths use ASCII letters, digits,
underscore, dash, slash and dot; canonical relative paths only. No symlinks,
junctions, reparse points, special files, case aliases or omitted files.

`SHA256SUMS` uses `lowercase-sha256`, two spaces, canonical relative filename.
It covers **every** regular file except the root `SHA256SUMS` itself, including
the qualification report. The report contains:

```json
{
  "schemaVersion": 1,
  "runtime": "osx-arm64",
  "passed": true,
  "secretScanPassed": true,
  "corePortable": false,
  "checks": ["fresh-install", "cli-path", "desktop", "host-start-stop", "status-json", "empty-usage", "update-preservation", "uninstall-preservation"],
  "files": {"platform-package.json": "<sha256>", "...": "<sha256>"}
}
```

`files` must exactly equal the checksum inventory excluding the report itself.
Gateway additionally requires `corePortable: true`. This report is an offline
release-engineering attestation, **not** cryptographic authentication or an
installer-run test. Only trusted, independently authenticated artifacts should
be installed. Hashes detect corruption; a malicious distributor can replace
hashes and the report. Do not manufacture reports to bypass pending native tests.

A clean package must be built from public source inputs without copying any user
home, credentials, configuration or runtime database. Secret-like filenames are
rejected as defense in depth; this does not inspect arbitrary file contents or
replace the producer's secret scan (`secretScanPassed`). No real credentials
belong in fixtures, reports, or packages.

The native qualification harness must stage the final payload first, run the
checks against that exact payload in disposable native user accounts, and bind
its inventory into the final report. The generic installer `check` action checks
the resulting contract only; runtime qualification needs a separate native
harness. No currently supplied binary package satisfies this contract.
