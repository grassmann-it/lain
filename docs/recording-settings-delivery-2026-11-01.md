# Recording Settings: local candidate, 1 November 2026

Status: implemented, tested and **locally installed by the user** on 1 November 2026. The manual operator receipt reports verified, Host version `0.8.0+recording-settings-20261101`, ready Gateway and preserved desired/applied revision 926/926. The archive list is readable. Recording remains enabled in content mode. Its immediate post-restart status reported `source_unavailable`; this receipt does not establish uninterrupted collection or recover historical gaps.

## User command

Close lain UI and finish active Codex/proxy calls, then run:

```sh
bash /home/tomka/.cache/lain-recording-settings-20261101/install.sh
```

The operator verifies the exact installed manifest, five candidate assembly hashes, the private update lease, process identity, closed Desktop and applied revision. It waits at most 60 seconds for idle, closes admission, reconfirms zero active requests, stops the owned service, atomically replaces the five assemblies and metadata, then starts and checks Host readiness. Existing credentials, routes, recordings, policy and CLI/Core binaries stay in place. Original assemblies and metadata are preserved under `original-assemblies/`. Failure before replacement preserves running requests; replacement failures attempt assembly-only rollback without restoring data. Inspect `installation.json` before recovery-required retries. A completed repeated command reports already installed.

The baseline is the installed 80dcf135 package, manifest SHA256 `2529d5f51bf0839716fd8239ca1e0b8aac92570b26c24223efbe02e4f4885371`. Candidate identifier is `0.8.0+recording-settings-20261101`; this is a local component patch, not a qualified public release. Build metadata records the base source plus local patch provenance and updated component hashes. The old full-upgrade operator must not be rerun for this patch.

## Behavior

- First startup with no persisted recording policy initializes local sanitized message/tool capture enabled. Existing saved off policies and invalid policies are not silently enabled.
- Settings displays actual recording status, completeness and a Saved conversations navigation button. Recording settings no longer require a second consent checkbox.
- The archive list refreshes while no conversation is selected; opening Settings does not mutate recording policy.
- Capture remains local and unencrypted, bounded, sanitized and independently stoppable. Training permission remains separate.

## Archive fault

The existing enabled policy was not the cause of the empty archive. Its projection reached the old 128 MiB SQLite page ceiling and raised SQLite error 13 during reads/synchronization. Reproduction used a private copy through the production store/projection API, not direct SQL inspection. With the patched Host, the same 35,301-record source lists conversations successfully. The finite ceiling is now 512 MiB; disk reservation includes the index and rollback journal within the overall recording budget. Queue/memory limits are unchanged.

Historical capture gaps remain real: before installation the service reports collection_failed, 113,671 dropped records and unknownLoss. Do not reset counters or claim backfill or lossless capture. Future ceiling exhaustion remains possible; the change removes the diagnosed current ceiling, not every potential recording failure.

## Validation

- Recording policy: 58 assertions passed, including initial persistence, explicit stop preservation and invalid-policy handling.
- Recording projection: 41 checks passed.
- Native Recording UI: passed at 1120×780 and 800×560 (`desktop-r2/evidence.json`).
- Native Settings UI: 47 checks passed (`settings-r3/`); screenshot visually inspected. Tests wait for actual layout/scroll settlement before existing viewport assertions.
- Original archive copy: SQLite 13 reproduced with installed code; patched code returns one bounded conversation-list item with no error. No chat contents printed. Temporary copy removed after reproduction.
- Installer preflight passed against all 1,154 manifest entries and five assembly candidates, without installation. Active requests remained running. The user subsequently executed installation successfully; automated rollback has not been independently fixture-tested or exercised in production.
- git diff --check passed.

## Rollback and completion

For subsequent checks, inspect installation.json status, `lain status --json`, `lain recording status --json` and bounded conversation list readability. Start `lain ui`, then Settings → Saved conversations. Keep historical loss visible. If manual recovery is needed, close UI and drain requests before stopping the service; restore only the five assemblies and three metadata files from original-assemblies, then restart. Never replace the recording directory, credentials, Core state, client config or historical upgrade receipts.
