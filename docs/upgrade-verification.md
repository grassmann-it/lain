# Controlled own Host/Core upgrade and rollback

This procedure replaces the whole owned application, including a new Core and
the Host's SQLite migration. The earlier component-only masonry updater is not
sufficient: leaving the old Core in place does not test the new journal, and
restoring only an old executable over a migrated database is not a safe rollback.

[`tests/acceptance/upgrade_probe.py`](../tests/acceptance/upgrade_probe.py) prepares
and verifies this transition. Without `--execute` it validates the package and
prints a plan; it does not stop services or install files. Execution belongs to
the integrator after exact-artifact, staged-package and applicable native UI
checks. This document and the synthetic tests do not claim an installed upgrade
has occurred.

## Before the controlled interval

Freeze the reviewed package and its complete `SHA256SUMS` file. Verify the
independent backend, Inspector and acceptance reports against the **full** new
Core SHA-256, its source/toolchain manifest, and packaged qualification file.
The helper checks the package's pinned manifest, every listed file, absence of
unlisted payloads, executable components, exact Core hash and matching
`passed=true` qualification. A staged `releaseQualified=false` package requires
explicit `--allow-unreleased`; using that option does not change the gate.

The default owned installation is `~/.local/share/lain/app`, supervised only by
`lain.service`. Launcher and unit paths are `~/.local/bin/lain` and
`~/.config/systemd/user/lain.service`. The helper validates the service's unit
and executable path. Custom paths must be explicit; symlinks and overlapping
package/state paths are refused. It does not run the general installer, alter
unit contents, reload systemd, enable services, rewrite Codex configuration, or
operate the legacy service.

Plan the reviewed package without mutation:

```sh
python3 -B tests/acceptance/upgrade_probe.py upgrade \
  --package /absolute/reviewed-package \
  --package-manifest-sha256 FULL_SHA256_OF_SHA256SUMS \
  --core-sha256 FULL_REVIEWED_CORE_SHA256 \
  --allow-unreleased
```

Use a quiet interval for **own lain inference and configuration mutations**.
Leave the user's open Desktop untouched; ordinary read-only UI polling is
allowed. The legacy proxy remains running. No concurrent account, profile,
credential or configuration edits should occur. `--clients-quiesced` records
this execution condition under the already authorized controlled upgrade; it is
not a new interactive permission prompt or a claim that a window was closed.
The helper samples the own API, rejects pending attempts and requires stable
bounded usage snapshots. It cannot prevent another client from subsequently
issuing a request. Default comparison is a fixed 30-day UTC
window, configurable with `--days 1..366`, at most 50,000 paged attempts. It does
not claim to have compared history outside that window.

## One stop, snapshot, replacement and restart

After root review and execution authorization, add `--execute
--clients-quiesced` to the same reviewed command. The helper then:

1. Reads only the authenticated own GET API for readiness, configuration and a
   fixed-window usage/attempt baseline. No inference or quota-refresh endpoint
   is called.
2. Fingerprints normal Codex config, the own launcher/unit, stable legacy binary,
   config and OAuth files, and the legacy service's identity/start state. These
   are private hashes, never credential contents in the public report. Volatile
   legacy `*.tmp` authentication files and `oauth/logs/**` are excluded explicitly; legacy logs,
   queues and databases are not read.
3. Copies and verifies a private new application stage. It stops **only
   `lain.service`**, verifies inactive/MainPID zero and an empty prior service
   cgroup, then snapshots owned state. `usage.db`, any WAL/SHM files, full proxy
   journal/source sidecars, runtime configuration, routes/profiles, credentials,
   owned auth and imports are copied together. There is no live SQLite read or
   backup operation.
4. Retains the entire old application as `app.before` beside the stopped state
   snapshot in a unique private `upgrade-backups/<timestamp-id>` directory.
   It swaps the verified whole stage into `app`, leaving launcher/unit and
   ordinary Codex/legacy files untouched.
5. Starts **only `lain.service`** once and waits for own API/Core readiness and
   the same applied revision. It compares the original fixed-window summary,
   cursor and legacy attempt fields; requires the new additive attempt fields;
   exercises `/usage/analysis` and `/inspector`; and rechecks configuration,
   credentials, runtime files, package contents and protected external hashes.

Migration is verified through actual successful queries of the new analysis and
attempt schema. No second stop or direct read of the live database is used just
to inspect `PRAGMA user_version`, and the report does not invent a numeric
observed schema version. A new API listener alone is not enough: the own Core
must be ready and old usage must still agree.

Restarting the existing own service resumes its normal quota/owned-auth
background behavior. The helper issues zero inference requests and no explicit
quota refresh; this is not a claim that normal background provider requests
cannot occur. A changed owned credential or protected legacy file is reported
as a changed observation during this interval, not automatically attributed to
the installer and never silently restored.

Private `upgrade.json` records phase, stopped snapshot fingerprints, old
application identity, API baseline, package identity and verification outcome.
Snapshots contain private credentials and must remain local with private modes;
do not commit or upload them. Public stdout contains only outcome, counts and
the private backup location. On failure, the helper leaves evidence and performs
**no automatic rollback**. Depending on the failing phase, the own service may
be stopped or running the new generation; inspect its actual state and the
recorded phase before taking the next root-controlled action.
If failure happened before application replacement and the complete old `app`
is still installed, root can verify that state and start the unchanged own
service. An incomplete snapshot is not a rollback candidate. Do not treat a
missing `app` between the two rename operations as permission to start either
Host against partially restored state.

## Rollback is a guarded paired restore

First inspect the rollback plan:

```sh
python3 -B tests/acceptance/upgrade_probe.py rollback \
  --backup /absolute/lain/upgrade-backups/REVIEWED_SNAPSHOT
```

After root review, `--execute --clients-quiesced` stops the own service and
archives the entire current state before considering any restoration. It then
requires the current journal **and source identity** to match the pre-upgrade
snapshot byte-for-byte. Credentials, owned auth, configuration, routes, runtime
files and protected external observations must also match.

If new durable events appeared, restoring an old database/journal would hide
potentially consumed requests or reuse sequence identities. The helper refuses
that rewind, preserves the new generation and leaves the own service stopped.
Root must reconcile/export the new history or use a compatible forward repair;
an old API's inability to parse the new event kind is not permission to discard
it. If auth rotated, restoring an older snapshot could resurrect invalid refresh
state: that case is also refused. No automatic legacy modification or token
copy is a fallback.

Only when those guards pass does rollback restore the matching **old application
and stopped old state together**, including DB/WAL/SHM and journal/source files.
The migrated generation remains archived. The helper restarts the own service,
checks readiness, the same configuration and original usage window, and records
`rollback.json`. Never manually start the old Host against the migrated database
or truncate a journal to make its parser accept it.
Restored DB/WAL/SHM, credentials, configuration and directory entries are flushed
before that start. Recovery phases distinguish restoring/restored application,
restoring/restored state and completed flush. A flush failure keeps the service
stopped; root must inspect the retained snapshots and phase before resuming.

## Verification of the helper

```sh
python3 -B -m unittest discover -s tests/acceptance -p 'test_upgrade_probe.py' -v
```

These tests use temporary fake packages, fake database bytes, mocked service
transitions and a fake GET API. They check plan-only behavior, exact-package
rejection, paired application/data restoration, interrupted cutover, and refusal
to erase new events or rotated auth; an injected flush failure prevents restart.
They run no real service, compiler, SQLite
query or installation. An actual installed upgrade/rollback result must be
recorded separately with the exact package/Core hashes and interval-scoped
preservation evidence; it does not follow from these fixtures alone.
