# Codex onboarding catalog packaging repair

The v8 packaging change in `42f2d75` omitted
`proxy/codex-model-catalog.json` from both the canonical fresh publisher and the
Core-only refresh. The retained Host reads this file once at startup. Without it,
fresh imports without explicit Codex aliases and successful owned logins can save
an account without creating its default model routes. Existing routes survive;
imports with explicit aliases do not depend on the catalog.

Both packaging paths now ship the exact tracked catalog and validate it before
delivery: schema 1, a nonempty model list, ASCII model IDs of at most 200 characters
using letters, digits, dots, underscores and hyphens, and no duplicate IDs including
case-only collisions. The recorded source, source hash, snapshot version and
`static-catalog-not-provider-entitlement` qualification are retained unchanged.
This repairs packaging; it does not refresh the earlier v7 static model snapshot
or establish current provider availability or account entitlement.

The catalog is a Host runtime asset, not a new Go compilation input. Its exact
bytes appear in the payload and frozen source provenance. `build-info.json`
records their hash, model count and original static provenance. Core refresh also
binds that hash in its delta manifest. Normal package checksums cover the catalog
and provenance copies. Source-manifest build inputs and qualified Core binary
hashes are unaffected by this repair.

Baseline verification deliberately remains an exact inventory check. A sealed
historical v8 package that genuinely lacks the catalog can therefore be upgraded.
The new target must have a valid catalog and matching provenance; deleting it and
regenerating `SHA256SUMS` does not bypass validation. Rollback restores the exact
baseline, including the historical absence when applicable. No account, route,
profile, credential or history migration is performed by this packaging repair.

Focused synthetic checks (no Go/.NET build, app/service launch or provider call):

```sh
python3 scripts/test-core-refresh.py
python3 scripts/test-fresh-build-info.py
```

The Core fixture covers catalog validation, byte/provenance retention, missing,
empty, duplicate, unsafe and changed target catalogs with regenerated checksums,
installation and injected rollback from both present and catalog-less baselines.
The fresh-publish fixture executes the publisher's actual static copy block and
source/build-info blocks with fake component bytes, proving the catalog is copied
and an absent or changed copy cannot receive fresh provenance.

Both fixtures passed on 24 October 2026; `bash -n scripts/publish-linux.sh` and
`git diff --check` also passed. These are packaging checks, not an executed Host
onboarding or live-provider acceptance.

Before release, Main must also run the integrated packaged Host onboarding check
in an isolated synthetic environment: import without aliases, exclusions and
explicit aliases, plus successful fake owned login. Loading the file into a
running Host requires a restart; restoring the file alone does not create missing
routes for previously added accounts. Actual OAuth, provider availability and
entitlement remain separate live checks.
