# Delivering external Core v8.0.41

Core source-manifest schema 2 records the verified official dependency separately
from lain's adapter and explicit upstream hook patches. `buildInputs` binds those
inputs plus the build/prepare scripts. The build sidecar binds the actual binary,
source-manifest, compiler, upstream version/commit/archive, modification flag and
patch-set hash. The old schema-1 snapshot is historical evidence, not the source
of the new binary.

`publish-linux.sh` remains the fresh-package path. It runs the normal locked .NET
publishes and `build-core.sh`, carries the exact `.manifest.json` as
`proxy/build-manifest.json`, ships `LICENSE.upstream` as `proxy/LICENSE`, and
collects notices from a separate verified prepared source tree. It never copies
licenses from the removed `proxy/core` directory. `LAIN_QUALIFICATION_FILE` is
optional for an unqualified fresh artifact; if provided, its independent report
must pass for both the exact binary and source-manifest SHA-256. Package/installed
acceptance remains false. The historical patch-2 `stage-expansion-release.py`
explicitly refuses schema 2.

A Core-only update uses `scripts/core-refresh.py`; it does not republish or relabel
the installed Host, CLI or Desktop. The integrator must first freeze and qualify
the new Core, complete the old-data/new-Core compatibility check independently,
and finish sealing the current application baseline. No migration is performed
by this helper.

```sh
# Use the final integrated commit and independently frozen qualification report.
# LAIN_GO must be the matching pinned compiler executable.
python3 scripts/core-refresh.py stage \
  --base /home/tomka/.local/share/lain/app \
  --source-commit FULL_INTEGRATED_COMMIT \
  --core /absolute/frozen/lain-proxy \
  --core-build-manifest /absolute/frozen/lain-proxy.manifest.json \
  --qualification /absolute/frozen/qualification.json \
  --output /absolute/nonexistent/lain-core-v8.0.41
```

Staging accepts only an exact `SHA256SUMS` baseline with component assembly hashes
matching its retained provenance. It assembles upstream privately using
`prepare-core.py` and the verified archive cache; `--archive` can supply the
already downloaded official zip. No binary is built or executed. The Go collector
uses the matching compiler only for `version -m`, `env` and readonly/offline
`list -m -json all`. It checks the prepared-source inventory before/after collection,
resolves the pinned local upstream replacement explicitly, preserves its original
MIT text, and reports every missing root notice. Core staging refuses incomplete
root-notice evidence. Nested/generated third-party obligations remain subject to
review; collected evidence is not legal approval.

Only these payload paths may change:

- `proxy/` and `notices/go/`;
- bounded `core-source-provenance/`, `core-refresh.py` and `core-refresh.json`;
- `build-info.json`, `gate-status.json` and `SHA256SUMS`.

Every other package file stays byte-identical, including all three application
payloads, their source pins, NuGet/font/official notices, client provenance,
launcher/icon assets and delivery metadata. Original application notice files
keep their existing component attribution. Prior build/gate/Core evidence is
copied verbatim into the fresh bounded Core provenance directory; old provenance
is not recursively copied. Old acceptance describes its original scope. Current
package gates are false until independent acceptance, even when the retained
application has a positive historical seal. New `backendQualified: true` describes
only the exact newly supplied independent Core report.

Installation is a separate integrator action after review and compatibility tests.
Stop only the managed **lain** service outside this helper. The installer refuses
running installed Host/Core processes and performs no service actions itself.
The legacy proxy/services are outside this procedure.

```sh
python3 /absolute/nonexistent/lain-core-v8.0.41/core-refresh.py install \
  --package /absolute/nonexistent/lain-core-v8.0.41 \
  --home /home/tomka/.local/share/lain \
  --backup /home/tomka/.local/share/lain/backups/core-v8.0.41-UNIQUE
```

The exact staged installer rechecks the full current baseline, target inventory,
protected files, component provenance and qualification. Replacements are copied
into a new same-filesystem backup area, checked again, then exchanged with
`renameat2`. A journal records each completed item. Ordinary exceptions reverse
all completed exchanges and verify the original inventory. A process/power loss
between exchanges needs integrator recovery from `transaction.json` and
`previous/`; keep lain stopped until the complete target or baseline inventory is
restored. Do not discard the backup before independent installed acceptance.
Restart/qualification/sealing is controlled by the integrator and is never inferred
from staging or a successful file replacement. Private config, databases, journals,
credentials, desktop integration and service definitions are never modified.

The synthetic regression test exercises only disposable files and fake Go/source
metadata, with no app/runtime/service/network activity:

```sh
python3 scripts/test-core-refresh.py
```
