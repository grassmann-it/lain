# English-only source and 0.8.0 preparation

The user selected **0.8.0**, without a beta suffix, on 27 October 2026. This
supersedes the earlier version proposal. This change prepares source; it does
not publish a tag/download or update the running service.

- Shared first-party build identity is 0.8.0. Delivery preparation and readiness
  checks use recorded component versions rather than assuming 1.0.0. Historical
  packages and retained components keep their actual identities.
- Desktop About and README offer voluntary PayPal support using the public
  MeowChan donation URL. About retains separate application/host versions and
  the Huxel website and Flickr credits.
- Remaining first-party German catalog/validation strings and optional client
  menu source are translated. CLI, skills and the main Desktop copy were
  already English. Public entry, installation and provider guides are English;
  internal/historical engineering notes may remain German. User content,
  protocol identifiers and upstream diagnostics retain their original meaning.
- The [referral guide](provider-referrals.md) provides current public entry
  points. It contains no personal codes and performs no referral attribution.
- All four bundled skills include the requested worker naming convention;
  `lain-workers` explains unique/stable per-run aliases, supported native naming,
  separate task descriptions, and the CLI v1 naming limitation. Names change no
  models or behavior. Skill frontmatter validation passes for all four skills.

## Verification

The integrated solution builds successfully. Host, CLI and Desktop generated
assembly metadata all report 0.8.0 with the same source suffix. This local WIP
build is not a frozen release source. The build reports two existing Windows
platform warnings in the Fireworks process adapter.

479 client/CLI contract checks and 90 version/delivery Python checks pass.
Synthetic/xAI catalog and validation checks pass (41 assertions, mock HTTP).
All four skill files pass the skill validator; 39 bundled-skill CLI assertions
pass. First-party project references in lockfiles are aligned to 0.8.0 without
changing third-party versions, hashes or retained runtime-specific sections.
The Settings/About harness compiles after adding its missing shared
`CodexArtifactFixture` compile reference. Private Xvfb evidence is stored under
`/home/tomka/.cache/lain-english-0.8.0-20261027/`; the initial run passed 44 checks,
and the follow-up passed 45 checks including support reachability at 800×560.
The About/version and support screenshots were visually reviewed.
No support button was clicked and no external payment page was opened.

## Remaining release qualification

The [0.8.0 release entry](release-0.8.0.md) distinguishes these new bytes from
the earlier qualified 1.0.0 candidate. Fresh frozen packages, installed runtime
acceptance and final real provider/model acceptance remain necessary before
download publication. Optional Codex/VS Code client changes require new native
qualification; previously installed clients and recorded qualifications remain
unchanged. No running research, service, production client, credentials or
account configuration was modified by this work.
