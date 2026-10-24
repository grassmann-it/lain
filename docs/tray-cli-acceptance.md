# Compact CLI and retained Desktop acceptance

Installed on 24 October 2026. `lain usage` is the compact current quota view;
`lain usage --full` retains diagnostics, IDs, native used/limit amounts, sources
and observation times. `lain history` remains recorded proxy activity. JSON and
explicit historical `usage` aliases retain their contracts.

The installed five-account snapshot has thirteen quota windows: 20 lines in the
default view versus 99 in the full view. Narrow terminals wrap complete quota
fields together. All accounts, windows, missing data and warning states remain
visible; unknown values are not rendered as zero.

Validation:

- 173 client/CLI contract checks, including unchanged full/compact JSON.
- 55 private terminal, pipe and JSON captures at 60, 80 and 120 columns.
- CLI-only installation tested in a copied installation, including active-CLI
  refusal and a synthetic active-Desktop process argument.
- Installed CLI checked against the real Host's cached snapshots without a
  provider refresh. Account, model and profile configuration, revision 4/2,
  historical 81,986 tokens/cursor 2, UI preferences and Host/Core processes were
  retained.
- Exact retained Desktop: 124 installed checks and 60 private tray checks.
  Native launcher and icon were separately verified. GUI testing used private
  Xvfb/Openbox/D-Bus sessions.

CLI source at build time: `984cf7139bc01a98551c839a6c195b7ccbb14550`.
Desktop remains byte-identical to `e5dbaa3142549d6d0d03799ef90ffa96a459907d`.
Host and Go Core were neither rebuilt nor restarted. The installed usage skill
was updated, with its previous copy retained in the CLI installation backup.
These are historical build identities; an author-email rewrite does not relabel
already built binaries. The repository rewrite map resolves their equivalent
source trees.

The installed and staged metadata now records the completed client acceptance;
earlier false/pending gates remain historical records. The final archive was
checked file-for-file against the installed manifest. This client delivery adds
no new live-provider qualification and does not implement Monitor mode.

Evidence: [installed CLI](../tests/acceptance/evidence/cli-compact-installed.json),
[installer](../tests/acceptance/evidence/cli-compact-installer.json),
[terminal captures](../tests/acceptance/evidence/cli-compact-terminal-fixture.json),
[final package](../tests/acceptance/evidence/cli-compact-release-final.json).
