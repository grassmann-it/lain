# Compact CLI presentation delivery, 25 October 2026

The CLI from `b49096f1d131a12946cc434783ae2ca012ecf60c` is installed.
`lain usage` now highlights account names, aligns window labels and limits status
colours to individual metrics and warnings. Empty bar cells use dots, making
consumption legible in ordinary terminal fonts. Duplicate provider names and
repeated detail rows are removed. Accounts without readings form a secondary
group with short, neutral guidance. Complete setup instructions and diagnostics
remain in `lain usage --full`.

Future resets use the local timezone, named in the heading, with explicit
offsets to distinguish repeated daylight-saving hours. At widths below 48
columns the numeric percentage replaces the bar. Continuations are indented;
long names and words wrap safely, including Unicode. Shared quota selection,
unknown/zero/stale semantics, native remaining units and complete JSON stay
unchanged. Full output retains UTC timestamps.

Validation passed: 248 CLI checks and 59 private PTY/pipe/JSON captures at
20/40/60/80/120 columns, with and without colour. The exact published assemblies
separately passed the same 248 checks and 59 captures; loader traces and hashes
bind the tested assemblies to the package. The synthetic PTY captures were
rendered for visual review at 80/120 columns; the 20-column text was also
inspected. These renders are not screenshots of the user's terminal. An initial
thin-line empty track proved too similar to its filled track and was replaced
with dots before the final build and repeated checks.

The CLI-only updater passed 17 filesystem fixtures and independent source
review. Installation succeeded with unchanged Host/Core process identities and
protected Desktop/Host/Core files. Desktop remains on `67fb26c`; Host remains
on `5f41fba`. Earlier client provenance is retained, and new CLI provenance is
separate. No Desktop rebuild, service restart, provider request or configuration
mutation was required. This does not qualify a new backend or public release.

Installed follow-up: 15 accounts and three profiles; account, model and profile
response hashes unchanged; gateway ready at revision 35/35, profile lain.
Compact usage has 41 lines versus 209 full lines for that snapshot. All account
names are present; redirected output is plain; JSON and Full-JSON are byte equal.

Current package manifest:
`b0f93369ae9788b4a3ca0a3e3bab4d8812212308653d420edee9da9bd6a79d04`.
Rollback backup: `~/.local/share/lain/backups/cli-style-b49096f`.
Local logs, synthetic captures, source and package are retained under
`~/.cache/lain-qualification-20261025/cli-style/`.
Raw publish copies, the exact-test overlay and frozen-checkout build directories
were deleted, freeing 161.1 MiB. Build/runtime work was serial and bounded to
one CPU, 768 MiB memory high, 1 GiB maximum and 256 MiB swap; temporary build
files used the cache directory rather than `/tmp`.

[Sanitized evidence](../tests/acceptance/evidence/cli-style-20261025.json)
records hashes and checks without account payloads or credentials.
