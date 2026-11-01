# Lain 0.8.0 — prospective release and installation

The selected version for the next public release is **0.8.0**. This page describes
the prospective release from the current work in progress. It is not a published
download, an installation receipt, or acceptance evidence for new binaries.

The new 0.8.0 bytes require their own frozen source revision, package manifests,
checksums, and qualification. No source hash, archive hash, or passing check from
an earlier candidate is transferred to 0.8.0. Final contents and supported
platforms must be confirmed against those exact new artifacts before publication.

## Intended distribution

Lain manages provider accounts, quotas, local usage history, models, and routes
through a shared Host, CLI, and Avalonia Desktop. Monitor operates without an
inference Core; Gateway adds Lain's pinned CLIProxyAPI integration. Existing
configuration takes precedence over a package's initial mode.

The public Linux packaging path provides separate Monitor and Gateway archives,
checksums, provenance, source, installers, maintenance helpers, and notices.
First-party code uses Artistic-2.0; third-party software and the credited photo
retain their own terms. Optional Codex and VS Code adaptations are documented as
source/patch workflows; local qualification does not authorize general binary
redistribution.

The source also includes bounded worker commands and the bundled `lain-usage`,
`lain-workers`, `lain-connect`, and `lain-doctor` skills. Bundling does not install
skills into personal directories or prove that an existing client has loaded
them. Provider access, quotas, model permissions, and live client activation
remain specific to the account and exact runtime.

## Installation entry point

There is no confirmed public 0.8.0 download or checksum on this page. Obtain a
final artifact only from the confirmed release channel once it is published;
verify its matching checksum and package manifest before installation.

- [Linux installation and package layout](install.md) describes the installer,
  default paths, explicit service startup, updates, and removal.
- [Operating modes and safe recovery](linux-delivery-stabilization.md) describes
  stopped snapshots, update preservation, and rotating-credential safeguards.
- [CLI usage](cli-usage.md) and [desktop tray behavior](desktop-tray.md) describe
  everyday operation. Check the installed CLI with `lain --help`.
- [Codex CLI integration](../clients/codex/README.md) and
  [VS Code integration](../clients/vscode/README.md) describe optional local clients.
- [Provider referral guide](provider-referrals.md) provides actionable signup links.

The installer does not start or enable a service. Before a full update, complete
requests and workers, close clients and Desktop/tray, stop the service, and make
a private stopped backup. Preserve the existing `app.previous` before another
update. An application rollback alone does not restore compatible data or
rotating OAuth grants. Restore snapshots into a new directory and follow the
credential recovery contract.

macOS/Windows installer contract fixtures and CI checks are not native package,
GUI, Core, signing, or installation acceptance for 0.8.0.

## Remaining work — review on 27 October 2026

Recommended order for the selected 0.8.0 release; this review does not start
implementation, provider requests, installation or publication:

New startup requirement: opening Lain should ensure its background service is
available. The [startup/lifecycle review](startup-and-autostart-work-packages.md)
proposes prioritizing Linux startup, UI integration and Core cold-start diagnosis
before the Linux download. The user confirmed background startup at sign-in
enabled by default during setup; startup on opening and login startup remain
independently configurable in Settings.
macOS/Windows native lifecycle work gates their own downloads independently.

1. Assemble a clean release source from reviewed WIP, including the final skill
   wording. Run relevant checks and a fresh publication/secret review on that
   exact tree. Freeze its revision, dependency locks and provenance.
   The [WIP integration review](wip-integration-review-2026-10-27.md) separates
   already-published changes from the actual residual delta.
2. Build fresh Monitor/Gateway packages. Verify 0.8.0 identities, checksums,
   bundled skills, notices and source correspondence; repeat isolated fresh
   install, update, preservation and recovery acceptance on those artifacts.
3. Requalify changed optional Codex/VS Code English menu sources on native
   clients. Existing qualification records describe older bytes and are not
   updated merely by changing source pins. Keep original OpenAI usage working.
4. In an actually quiet window, update the local runtime through the guarded
   path, verify saved/applied revisions, export/activate the selected clients
   and confirm discovery of the new skills. Observe current activity first;
   previous research activity is historical evidence, not a current process check.
5. Execute the already requested realistic per-model acceptance against the
   installed candidate. The earlier 59-alias matrix has 23 unresolved cases,
   seven auto/quality passes and 29 weaker historical tool passes; none of those
   results accepts new 0.8.0 bytes. Record reasoning/tool/context behavior and
   semantic quality per route. Verify quota-aware group selection and bounded
   fallback, including exhausted/all-unavailable cases, without exhausting real
   quotas to manufacture a failure. Offline fixtures and actual provider
   observations must remain distinguishable.
6. Verify the English installation-to-first-request flow and its failure paths,
   publish the final support/model matrix and known limitations, then prepare
   the v0.8.0 tag, notes and exact binary assets for the authorized release step.

Recommendation: ship the qualified Linux x64 scope first. macOS/Windows
installer contract CI already passes, but actual native packages, lifecycle and
GUI acceptance remain separate work before offering those downloads. Additional
OAuth integrations, automatic worker allocation and referral monetization need
not delay this release. Personal referral links are still optional inputs;
their absence does not prevent a usable provider connection.

## Historical evidence

The [historical 1.0.0 candidate distribution record](release-1.0-distribution-readiness.md)
and its [release notes](release-1.0-release-notes.md) describe the earlier
`882bdadd09235b35ca74c0ebdcff59e00061b2fe` source and r4 archives. Their versions,
paths, hashes, test counts, and qualification boundaries remain historical.
Those archives are not the new 0.8.0 artifacts and must not be renamed or
presented as qualified 0.8.0 downloads.

Publication, a release tag, binary uploads, production installation, and service
changes are separate actions; this documentation does not perform them.
