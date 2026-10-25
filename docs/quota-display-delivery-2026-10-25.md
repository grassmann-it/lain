# Quota hierarchy delivery, 25 October 2026

The shared [display hierarchy](quota-display-hierarchy.md) is installed in the
Desktop and CLI clients from `67fb26ce83f0f4aa2627fdde57d95ab8a1574355`.
The owned Host and proxy binaries and processes were retained; Host source remains
`5f41fba5324e1cf1e58e2229d6c35c90ba800846`. This is a client refresh with mixed
component provenance, not a new backend qualification or public release.

Accounts with actual quota readings precede unavailable accounts. Explicit pins
retain their priority; the manual 5h order remains frozen until updated. Short
windows come before monthly allowances and credits. Desktop and Monitor show up
to two primary windows, with additional budgets initially collapsed. Expansion
survives refresh and navigation. Main zero balances remain visible; empty extra
credit pots remain accessible in account details and `lain usage --full`.

`lain usage` follows the same shared selection rules. Its compact view omits
source/check details and secondary monthly windows, preserves state warnings,
and retains concise positive extra balances. The installed 15-account snapshot
uses 47 compact lines versus 209 full lines. JSON is complete, remains in Host
order and was byte-identical with and without `--full` in the installed check.
Account, model and profile response hashes match the pre-update snapshot.
Readiness and configuration stayed at 35/35, profile `lain`.

Validation: 38 shared policy checks, 234 CLI checks, 94 final native hierarchy
checks and 29 final Monitor checks. Earlier frozen-5h and account-snapshot flows
passed 48 and 35 checks respectively before the final padding-only fix. The
published binaries separately passed 94 hierarchy and 234 CLI checks; loader
traces and hashes identify the exact tested assemblies. Native tests used a
private Xvfb, D-Bus and synthetic handlers. Saved final images were visually
reviewed at 1120 and 800 pixels. Product publishes had no errors; the native
harness retains its 11 existing unused-fixture warnings.

The first client update stopped before mutation because the process scanner
could not identify the nondumpable user manager. A bounded private updater fix
uses stable process metadata and an independently identified D-Bus owner to prove
four specific platform roles: user manager, its PAM child, the exact polkit unit
and the direct KWin-wrapper child. Unclassified processes still stop the update.
The corrected native D-Bus JSON parser, 12 proof fixtures, 14 filesystem fixtures
including full rollback, actual read-only process proofs and independent source
review passed. The second update completed with unchanged Host/Core process
identities. Initial failure reports remain intact; atomicity across a machine
crash or exclusion of every concurrent client launch is not claimed.

The qualified installed package manifest is
`9bd1dafcfcb6d681d52c6ab29aa21f09dc454ff1882dcd44c260323ed08c15ce`.
Only qualification and updater-binding metadata changed after exact binary
acceptance; assembly hashes did not change. Original package and rollback backup
are retained. Raw publish copies, test overlays and generated build directories
were deleted, freeing about 1.01 GiB; logs, screenshots and evidence remain.

[Sanitized evidence index](../tests/desktop/evidence/quota-hierarchy-20261025.json)
contains component hashes, counts and report hashes. Full local evidence is in
`~/.cache/lain-qualification-20261025/quota-hierarchy/` and
`~/.cache/lain-qualification-20261025/quota-clients-1f10e8a/` (the latter name is
historical; its frozen checkout and delivered clients are the full 67fb26c commit).
The original private updater failure and all earlier failures remain separate.
No provider refresh, inference request or change to ordinary Codex configuration
was needed for this delivery.
