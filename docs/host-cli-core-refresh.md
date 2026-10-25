# Bounded Host + CLI + Core refresh (prepared, not delivered)

`scripts/host-cli-core-refresh.py` prepares one mixed-component package over an
explicit complete installed baseline. It preserves the exact Desktop source
`67fb26ce83f0f4aa2627fdde57d95ab8a1574355`, its payload hashes and component
provenance. It does not rebuild Desktop, build anything else, run payloads,
control services, read credentials/databases or edit external integration files.
Stage and install both default to a read-only plan; neither has an implicit
installation mode. No real artifact hashes or positive installed acceptance are
supplied by this source preparation.

## Existing paths and the narrow addition

The installed package metadata inspected for planning records Host `5f41fba`,
CLI `b49096f`, Desktop `67fb26c`, Gateway mode and Core
`e05d21027a9aa0c543430db8bee6fd8c34d5a9fff78ea81f80953e8f29cf485e`.
This is historical metadata, not a substitute for fresh exact manifest pins.
The requested current 35/35 revision state, accounts, 58 routes and History must
be captured by the live operator; preparation has not queried or verified them.

Existing `core-refresh.py stage` can assemble a new independently qualified
schema-2 Core over this exact application baseline. It does not change Host/CLI.
Its staged package is the new helper's Core input. The existing
`client-refresh.py --cli-only` instead requires historical Host/Core constants;
the full publishers and historical component/UI stagers require Desktop
publishes. None is a suitable existing combined Host/CLI/Core entry point.

The new helper accepts separately published Host and CLI directories, avoiding
the full `publish-linux.sh` Desktop build. It reuses `core-refresh.py`'s pinned
inventory/Core validation and follows its same-filesystem exchange/journal
transaction pattern. The exact Core helper SHA is an explicit input pin and
must match the Core package. Only its checked definitions/main guard are loaded;
no helper subprocess, build or runtime action is invoked by this wrapper.

Only these application paths can change:

- `cli/`, `host/`, `proxy/`, `notices/go/` and `core-source-provenance/`;
- `core-refresh.py`, `core-refresh.json` and bounded combined-refresh
  script/manifest/provenance paths;
- `build-info.json`, `gate-status.json` and `SHA256SUMS`.

All other package paths, including Desktop, non-Go notices, packaging assets,
client documents and older provenance files, remain byte-identical. Old source
provenance is historical for replaced components; the new bounded provenance
directory records their exact new publishes and frozen properties. The Core-only
input manifest remains provenance; use the new combined installer for the final
package, since the old Core-only installer rejects changed Host/CLI files.

The narrow dependency guard requires the same publish file set, source dependency
lock bytes, external `.deps.json` graph and every external runtime/dependency file
as the baseline. Only known Lain apphosts/project assemblies and their deps/PDB
files can differ. A dependency change is refused and needs separately reviewed
notice/dependency packaging. Current Desktop provenance is retained verbatim;
Host/CLI records get their actual versions, assembly/shared-assembly hashes and
unchanged source lock hash.

## Exact input manifest

Root fills these pins only after final integrated artifacts exist. All paths are
absolute, nontraversing, and contain no symlink ancestors. Complete inventories
are mappings of relative filename to lowercase full SHA256, without directory
entries. They must include every file, rather than just the main DLL.

```json
{
  "format": 1,
  "kind": "host-cli-core-refresh-inputs",
  "sourceCommit": "FULL_FINAL_40_CHARACTER_COMMIT",
  "helperSha256": "SHA256_OF_EXACT_CORE_REFRESH_HELPER",
  "base": {"path": "/absolute/exact-baseline", "manifestSha256": "SHA256SUMS_FILE_HASH"},
  "corePackage": {"path": "/absolute/qualified-core-only-stage", "manifestSha256": "SHA256SUMS_FILE_HASH"},
  "cli": {"path": "/absolute/fresh-cli", "informationalVersion": "1.0.0+FULL_FINAL_40_CHARACTER_COMMIT", "files": {}},
  "host": {"path": "/absolute/fresh-host", "informationalVersion": "1.0.0+FULL_FINAL_40_CHARACTER_COMMIT", "files": {}},
  "sourceEvidence": {"path": "/absolute/frozen-source-evidence", "files": {}},
  "qualification": {"path": "/absolute/publish-qualification.json", "sha256": "EXACT_REPORT_HASH"},
  "output": "/absolute/new-combined-package"
}
```

Each component's source evidence subdirectory (`cli/`, `host/`) contains frozen
`project.csproj`, `packages.lock.json` and `publish-properties.json`. The latter
retains the frozen source commit and actual explicit publisher properties:

```json
{"sourceCommit":"FULL_FINAL_COMMIT","publishProperties":{"InformationalVersion":"1.0.0+FULL_FINAL_COMMIT","RuntimeIdentifier":"linux-x64","SelfContained":"true"}}
```

The pinned independently reviewed publish report requires `kind` equal to
`host-cli-core-publish-qualification`, `passed: true`, `sourceCommit`,
`baseManifestSha256`, `coreManifestSha256`, `sourceEvidenceFiles`, `cliFiles` and
`hostFiles`. Those fields must match the complete exact inputs above. Additional
fields should describe actual checks and limits. The helper checks the main DLL's
full version attribute and bindings, but cannot reconstruct a compiler's source
history from binary bytes; frozen build/provenance review remains necessary.
A true report over fake or old artifacts is not a qualification of new artifacts.

Run the following only in Root's later preparation window:

```sh
python3 scripts/host-cli-core-refresh.py stage \
  --inputs /absolute/reviewed-inputs.json --inputs-sha256 FULL_INPUT_JSON_SHA256
```

Adding `--apply` writes the new staging directory only. It verifies source inputs
again after copying, preserves original inputs, seals exact `SHA256SUMS`, and
prints its manifest-file hash. It creates no archive and performs no installation.
On staging failure, inspect/discard only the new incomplete output, never install
it. The result retains `releaseQualified: false` pending exact combined-package
and installed acceptance.

## Later qualification and live operation

1. Serialize .NET Host/CLI publishes and the separate Core build/qualification
   after the occupied Rust lane is released. Use locked self-contained linux-x64
   inputs and the final source pin. Create the exact Core-only package with
   `core-refresh.py stage`; then prepare the combined package as above. Recheck
   all stage/input hashes and Desktop equality independently.
2. Test the exact target Host/CLI/Core in a new isolated home and distinct
   loopback-only namespace, with synthetic accounts/routes/current schema/History.
   Verify the new models/group path, Core compatibility and restart retention.
   No live credentials or provider requests are required. Existing
   `v4_update.py` expects every application component at one source commit and
   is not directly applicable to this intentionally retained Desktop package.
   Tailor a bounded mixed-component fixture rather than weakening its old guard.
3. Adapt and independently review the private own-service operator pattern in
   `~/.cache/lain-qualification-20261024/update-installed-lain-cumulative.py`.
   The 20261025 reconciliation operator demonstrates its exact loaded-unit,
   read-only CLI and shared-lease guards. Do not execute either historical
   operator unchanged against the new mixed package or relabel its old results.
4. The Root operator must hold the existing shared exclusive updater lease,
   validate the exact loaded own `lain.service` identity/environment/hooks,
   and require a quiet writer/UI/request state before stopping anything.
   If the user's window is open, defer without closing it. Capture two stable
   CLI baselines (`status`, `accounts`, `models`, `profiles`, fixed-range
   `history`) and retain actual account/profile/route identities, expected 58
   routes, 35/35 revisions, active identity and nullable History token fields.
   Do not infer an empty admitted-request set from stable History alone.
5. Stop only own `lain.service`, confirm its Host/Core/writers are gone, create
   a fresh full stopped-home snapshot with the existing maintenance helper,
   and back up external integration/fingerprints. Keep latest credentials and
   generations; never restore an older grant. Existing `app.previous` remains
   untouched by this bounded installer. Preserve normal Codex files and the
   legacy proxy by independent before/after fingerprints, without rewriting them.
6. Immediately recheck the complete stopped installed baseline and target pins.
   Run the staged installer's read-only plan with a unique backup inside
   LAIN_HOME, outside `app/`:

```sh
python3 /absolute/new-combined-package/host-cli-core-refresh.py install \
  --package /absolute/new-combined-package --manifest-sha256 FULL_STAGED_MANIFEST_SHA256 \
  --home /home/tomka/.local/share/lain \
  --backup /home/tomka/.local/share/lain/backups/UNIQUE_HOST_CLI_CORE_TRANSACTION
```

Only the separately authorized Root operator adds `--apply --stopped`. The
helper rechecks exact inventories and refuses running installed Host/Core/CLI/
Desktop payloads. It stages replacements on the same filesystem, fsyncs them,
journals each exchange and verifies the complete installed target. Its private
file-operation lock does not replace the operator's shared lease. Concurrent
foreign updates are unsupported. Private data paths and external Codex/legacy/
integration files are outside its write set.

7. Recheck the installed manifest, unchanged Desktop and all private/external
   preservation invariants before starting only own `lain.service`. No daemon
   reload is needed because this helper never changes its unit. Check exact
   running Host/Core identities and readiness; capture the same fixed-range
   CLI views and compare account/route/profile/status/History identities. Do not
   Apply or rewrite profiles/config to make the check pass. Seal only actual
   successful evidence; file replacement alone establishes no acceptance.

A synchronous failed file exchange reverses completed application-file exchanges
and verifies the old complete inventory while no runtime has started. This is
not a data/schema/credential rollback. Process/power loss needs journal-based
recovery with own service kept stopped. After starting the new runtime, any
failure belongs to Root's stop-and-investigate procedure; no automatic runtime,
schema or credential rollback is performed by this script.

## Offline source fixtures

`scripts/test-host-cli-core-refresh.py` uses temporary text payloads, synthetic
data sentinels and real filesystem exchanges. Core qualification and process
inspection are explicitly mocked; no binary, service, DB, credential or provider
operation occurs. The fixtures cover no-write plans, strict input pins, publish/
dependency tampering, protected Desktop changes, exact target installation,
private sentinels and reversal after an injected interrupted journal write.
They are not real Core, P2, combined-package or live-state acceptance.
