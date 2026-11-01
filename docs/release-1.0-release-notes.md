# Lain 1.0.0 — historical Linux x64 candidate release notes

Candidate dated 26 October 2026, source `882bdadd09235b35ca74c0ebdcff59e00061b2fe`.
These prepared notes record an earlier candidate, not a public release or full
approval. Its archives remain `releaseQualified: false`. The prospective
**0.8.0** release has [separate notes](release-0.8.0.md) and requires new artifacts
and evidence; the historical hashes and qualification below do not apply to it.

## Scope

Lain combines local account, model, and route management with CLI and Avalonia
Desktop. **Monitor** provides this interface without an inference Core;
**Gateway** adds Lain's pinned CLIProxyAPI 8.0.41 build. Both packages contain
self-contained .NET 10.0.11, installers, skills, maintenance helpers, checksums,
component provenance, LICENSE/NOTICE, and the exact tracked source. First-party
components use Artistic-2.0; third-party software and the approved Huxel photo
retain their own rights.

The candidate adds quota-dependent router availability, bounded fallback, and
safe handling of existing thread pins. Subsequent pin migration supports complete
message/paired function history; opaque reasoning/custom-tool histories require
a new thread. CLI/Host functions for bounded workers and the bundled lain-usage,
lain-workers, lain-connect, and lain-doctor skills are part of the source and
package delivery. Personal skill directories are not changed automatically.

## Recorded verification

The new Core is qualified. Delivery evidence reports 106 Host checks, 298 Go test
passes, and eleven passing isolated full-package checks. The exact Monitor/Gateway
archives were also checked for internal checksums, archive consistency, and their
source-archive binding; see the [notice review](release-1.0-notice-review.md).

These results are not a new production installation or complete live acceptance
of every provider/model. At this historical handoff, local production remained
`ff5771c`, revision 58 stored / 57 applied. The new own-update/preservation
acceptance awaited the user-requested end of ongoing research. Native Desktop
verification passed: eight maintained runs and two full default runs with 698
checks each; all 228 default images were reviewed.

## Limitations

- Target: Linux x86_64; no finished macOS/Windows delivery or verified general
  distribution matrix. Desktop requires a graphical session and native Avalonia
  dependencies; user-service operation requires systemd.
- Provider access, quotas, and model permissions depend on the account. A static
  catalog does not confer provider access.
- No modified Codex binaries or modified VSIX in the public Lain package. Optional
  UI adaptations are supplied as separate source/patches with pins; users obtain
  original clients themselves and qualify local builds. There is no blanket
  authorization to redistribute those binaries.
- Historical notice gaps are closed in source882 and the r4 archives;
  [packaging review](../packaging/licenses/review-20261026.md). Earlier archives
  and failed acceptance runs remain historical evidence.
- Source is included; not all external dependencies and upstream archives needed
  for a complete offline rebuild are included. New accompanying documents from
  that task are not part of the frozen source revision.

## Installation, upgrade, and removal

The [historical download verification and Linux operating guide](release-1.0-distribution-readiness.md)
records exact archive hashes, prerequisites, and commands. Installation does not
start/enable a service. Before updates, finish jobs, close Desktop and clients,
stop the service cleanly, and make a private backup. The installer preserves data
and keeps the previous application as `app.previous`; intentionally archive that
backup before another update. Installing Monitor does not implicitly downgrade
an existing Gateway state.

Rolling back old binaries alone is not safe data/OAuth recovery. Restore snapshots
only into a new directory; observe quarantine and required account reconnection.
For removal, first stop the service and clients and remove managed integration
files. Private data remains until explicitly removed separately. Optional client
adaptations and provider grants have their own rollback/revocation procedures.

At this handoff, the GitHub repository was intended to become public after the
publication review. A public release with tag/downloads and full acceptance
remained separate steps. The corrected r4 archives were built and checked;
ongoing research was not interrupted for a local update.
