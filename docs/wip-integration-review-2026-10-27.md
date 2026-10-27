# WIP integration review — 27 October 2026

Read-only Git/source inventory, with this review document added afterward.
No merge, fetch, commit, staging, checkout, build, test, service or installation
was performed for this review. The cached `origin/main` ref was inspected;
its freshness against the live remote was not checked.

## The apparent WIP is larger than the unpublished delta

- Local `main`: `fef478f601f46b315939fa53f02caa9791086f1d`.
- Cached `origin/main`: `10ffd12fc2369c4db4203a012d018b0aea25d566`.
- Local HEAD is an ancestor of origin/main, 12 commits behind; no local commit
  divergence was found. There are no staged changes or unresolved merge entries.
- Status reports **353 paths: 124 modified and 229 untracked** before this report.
- **233 of those paths already match the publication commit byte for byte**.
  They include workers/doctor, routing/fallback, notices, platform installers and
  substantial test/source work already integrated through the isolated release
  source. They must not be counted as new unpublished implementations.
- The actual working tree differs from origin/main in **120 files: 89 modified
  and 31 new**. No origin file is missing. 1,479 files match.
- File contents were compared using Git blob hashes, including files untracked
  by the old local index but tracked in origin/main; executable/symlink modes
  were checked too. Ordinary `git diff HEAD` alone overstates this delta.

## Integration assessment

The remaining product changes are bounded and appear suitable for integration
as a small series on top of the publication commit. This is a scoped merge
recommendation, not a full semantic/security review or binary release approval.

| Slice | Scope | Assessment and dependencies |
| --- | --- | --- |
| A | 0.8.0 build identity, version-aware delivery/update and first-party lock references | Integrate together. Modified lockfiles change first-party version references only; no third-party versions/hashes or runtime sections change. Prior delivery checks cover both 0.8.0 and historical 1.0.0. |
| B | Voluntary PayPal link on About and Settings test harness | Bounded UI change; 45 private Settings checks passed earlier. Include the missing shared fixture compile reference. README support text is in F. |
| C | Four bundled skills and compact worker naming convention | Current wording has passed skill validation. Skill commands already exist in origin/main. Bundle the final wording; no schema or worker-runtime name API is introduced. |
| D | English Codex/VS Code menu sources, patch pin, assertions and client docs | Keep source and assertions/pin together. Source tests passed earlier. Historical native qualification records remain unchanged and do not qualify translated bytes; new native acceptance is still required. |
| E | Synthetic/xAI display catalog and validation translations | First-party text changes; identifiers/protocol values preserved. Prior mock checks passed. |
| F | Public English guides, provider/referral presentation and 0.8.0 notes | Integrate with their referenced product slices. Regenerated HTML and JSON research presentation belong with the generator. New referral entry guide is not a personal referral code. |
| G | Earlier CLI UX/provider research, discovery expansion and engineering updates | Documentation only; can be integrated separately after targeted source/link/publication review, or left out of the release source. These are historical findings, not proof of current implementation or fresh provider verification. |

## Recommended merge route

1. Start an isolated integration branch/worktree from the pinned publication
   commit. Keep this dirty local main intact; do not reset, stash, clean or try
   to pull through untracked copies of already-published files.
2. Transfer only the reviewed residual paths/hunks from the inventory below.
   Suggested commits: A; B; C; D+E with related English documentation; remaining
   public documentation/referral presentation; optional historical records G.
   README, product scope and release notes may span commits and need deliberate
   hunk ownership. This avoids duplicating the 12 already-integrated commits.
3. On that exact assembled tree, run the appropriate integrated checks and
   publication/secret review, inspect the final diff and ensure a clean index.
   Earlier check counts describe prior runs, not new tests executed here.
4. Merge the assembled source through the ordinary fast-forward/PR path after
   review. Tagging, binary publishing, own runtime update, native client
   requalification and real model acceptance remain separate release work.

Source integration does not need to wait for optional referral codes or native
macOS/Windows distribution. It must not claim that unresolved model/provider
cases or newly translated native client bytes have passed acceptance.

## Exact residual inventory

This inventory predates this report itself. Local architecture-sparring notes
are ignored project memory and are not included in the public source inventory.

### A — 0.8.0 identity and delivery — 48 files

- `Directory.Build.props` (modified)
- `scripts/prepare-component-delivery.py` (modified)
- `scripts/service-update.py` (modified)
- `scripts/test-component-delivery.py` (modified)
- `scripts/test-host-cli-core-refresh.py` (modified)
- `src/Lain.Cli/packages.lock.json` (modified)
- `src/Lain.Desktop/packages.lock.json` (modified)
- `tests/account-connect-fireworks/packages.lock.json` (modified)
- `tests/account-connect-foundation/packages.lock.json` (modified)
- `tests/client-cli/packages.lock.json` (modified)
- `tests/client-connect-stabilization/packages.lock.json` (modified)
- `tests/client-diagnostics/packages.lock.json` (modified)
- `tests/cloud-connections/packages.lock.json` (modified)
- `tests/cloudflare-quota/packages.lock.json` (modified)
- `tests/codex-gateway-profile/packages.lock.json` (modified)
- `tests/console-connections/packages.lock.json` (modified)
- `tests/deepseek/packages.lock.json` (modified)
- `tests/desktop-connect-stabilization/packages.lock.json` (modified)
- `tests/desktop-installed/packages.lock.json` (modified)
- `tests/desktop-signals/packages.lock.json` (modified)
- `tests/desktop-usage/packages.lock.json` (modified)
- `tests/desktop/packages.lock.json` (modified)
- `tests/groq-enterprise/packages.lock.json` (modified)
- `tests/host-core-contract/packages.lock.json` (modified)
- `tests/implementation-independent/packages.lock.json` (modified)
- `tests/kilo-device-login/packages.lock.json` (modified)
- `tests/kimi-device/packages.lock.json` (modified)
- `tests/managed-connections/packages.lock.json` (modified)
- `tests/mimo-browser-startup/packages.lock.json` (modified)
- `tests/model-compatibility/packages.lock.json` (modified)
- `tests/models/packages.lock.json` (modified)
- `tests/native-final-gates/packages.lock.json` (modified)
- `tests/profile-application/packages.lock.json` (modified)
- `tests/provider-policy/packages.lock.json` (modified)
- `tests/provider-transport/packages.lock.json` (modified)
- `tests/providers-cohere-kilo/packages.lock.json` (modified)
- `tests/providers-independent/packages.lock.json` (modified)
- `tests/providers-samba-upstage/packages.lock.json` (modified)
- `tests/quota-host-integration/packages.lock.json` (modified)
- `tests/quota-sources/packages.lock.json` (modified)
- `tests/router-availability-host/packages.lock.json` (modified)
- `tests/skills-cli/packages.lock.json` (modified)
- `tests/transport-compatibility-integration/packages.lock.json` (modified)
- `tests/ux-profiles/packages.lock.json` (modified)
- `tests/uxd-acceptance/packages.lock.json` (modified)
- `tests/workers-cli/packages.lock.json` (modified)
- `tests/workers-host/packages.lock.json` (modified)
- `tests/xai-metadata/packages.lock.json` (modified)

### B — About support and UI harness — 3 files

- `src/Lain.Desktop/MainWindow.Settings.cs` (modified)
- `tests/desktop-settings/Program.cs` (modified)
- `tests/desktop-settings/Settings.Tests.csproj` (modified)

### C — Bundled worker naming convention — 4 files

- `skills/lain-connect/SKILL.md` (modified)
- `skills/lain-doctor/SKILL.md` (modified)
- `skills/lain-usage/SKILL.md` (modified)
- `skills/lain-workers/SKILL.md` (modified)

### D — English optional client integrations — 10 files

- `clients/codex/README.md` (modified)
- `clients/codex/patches/0001-lain-model-picker.patch` (modified)
- `clients/codex/upstream.json` (modified)
- `clients/vscode/README.md` (modified)
- `clients/vscode/lain-groups-ui.js` (modified)
- `scripts/clients/test_codex_dispatch.py` (modified)
- `tests/codex-picker/native_model_picker.py` (modified)
- `tests/codex-picker/test_prepare.py` (modified)
- `tests/vscode-groups/native_ui_cases.cjs` (modified)
- `tests/vscode-groups/test_ui.mjs` (modified)

### E — English provider catalog and validation — 3 files

- `src/Lain.Host/Providers/Catalog/synthetic.json` (modified)
- `src/Lain.Host/Providers/Catalog/xai.json` (modified)
- `src/Lain.Host/Providers/SyntheticXaiConfiguration.cs` (modified)

### F — English public documentation and referrals — 21 files

- `README.md` (modified)
- `docs/architecture.md` (modified)
- `docs/backend-qualification.md` (modified)
- `docs/core-v8-upgrade.md` (modified)
- `docs/english-0.8.0-delivery-2026-10-27.md` (new)
- `docs/install.md` (modified)
- `docs/oauth-provider-overview-2026-10-26.md` (modified)
- `docs/platform-installer-ci.md` (modified)
- `docs/product-scope.md` (modified)
- `docs/provider-recommendations-2026-10-26.md` (modified)
- `docs/provider-referrals-overview-2026-10-26.md` (modified)
- `docs/provider-referrals.html` (modified)
- `docs/provider-referrals.md` (new)
- `docs/referral-research-a.json` (modified)
- `docs/referral-research-b.json` (modified)
- `docs/release-0.8.0.md` (new)
- `docs/release-1.0-distribution-readiness.md` (modified)
- `docs/release-1.0-release-notes.md` (modified)
- `docs/service-contracts.md` (modified)
- `docs/skills.md` (new)
- `scripts/build-referral-overview.py` (modified)

### G — Historical research and engineering records — 31 files

- `docs/cli-ux-review-2026-10-25/README.md` (new)
- `docs/cli-ux-review-2026-10-25/adaptive-layout.md` (new)
- `docs/cli-ux-review-2026-10-25/layout-a-120-stress.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-a-120.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-a-160-stress.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-a-160.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-a-80-stress.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-a-80.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-b-120-stress.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-b-120.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-b-160-stress.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-b-160.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-b-80-stress.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-b-80.txt` (new)
- `docs/cli-ux-review-2026-10-25/layout-preview.html` (new)
- `docs/cli-ux-review-2026-10-25/model-collections.md` (new)
- `docs/cli-ux-review-2026-10-25/names-and-structure.md` (new)
- `docs/cli-ux-review-2026-10-25/quota-explanations.md` (new)
- `docs/cli-ux-review-2026-10-25/work-packages.md` (new)
- `docs/provider-discovery-2026-10-24.md` (modified)
- `docs/provider-monitoring-review-2026-10-25/README.md` (new)
- `docs/provider-monitoring-review-2026-10-25/api-providers.md` (new)
- `docs/provider-monitoring-review-2026-10-25/cloud-regional.md` (new)
- `docs/provider-monitoring-review-2026-10-25/coverage.json` (new)
- `docs/provider-monitoring-review-2026-10-25/google-cloudflare.md` (new)
- `docs/provider-monitoring-review-2026-10-25/kilo-openrouter.md` (new)
- `docs/provider-monitoring-review-2026-10-25/other-gateways.md` (new)
- `docs/provider-monitoring-review-2026-10-25/subscriptions.md` (new)
- `docs/providers/monitor-auth.md` (modified)
- `docs/providers/quota-sources.md` (modified)
- `docs/release-1.0-execution-2026-10-26.md` (modified)
