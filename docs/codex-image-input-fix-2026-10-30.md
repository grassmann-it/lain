# Codex image input catalog repair · 30 October 2026

Status: **local client catalog repaired; persistent exporter source fixed/tested but not installed into the running Host**. No recording implementation or activation. No Host/Core restart, profile Apply, provider credential changes or real image-provider call.

## Cause and evidence

`CodexProfileAdapter.CatalogEntry` hardcoded `input_modalities: ["text"]` for every route and router. The active generation held472 entries, all text-only, including native Codex Sol/Astra/Luna. The pinned native Codex0.164.0 model source advertises text+image for these concrete models. Its `context_manager/normalize.rs::strip_images_when_unsupported` replaces image content when the metadata lacks image. Thus a model can receive a textual unsupported-media marker even though the user attached an image.

This is a proven metadata gate, not proof of a broken OS clipboard. Official model-catalog guidance requires modalities to describe the actual route: https://learn.chatgpt.com/docs/model-catalogs. Image attachment guidance: https://learn.chatgpt.com/docs/image-inputs?surface=cli.

## Source change

- `ModelCompatibilityModalities.cs`: known pinned native image-capable models require an exact native Codex Responses transport (owned login or external access snapshot). Alias/name resemblance cannot grant capability. Other providers require qualified, exact, documented image-capable routes and compatible transport; unknown remains conservative.
- `CodexProfileAdapter`: per-route modality export; router image support is the intersection over actual compiled members. A mixed/unknown router does not advertise images merely because one target can accept them.
- `InputModalityCases.cs`: native, external snapshot, changed model/stale transport, third-party same name, unqualified client/route, documented route, protocol mismatch and homogeneous/mixed router cases.

113 Codex gateway-profile export assertions passed with the existing suite. Test-only fixture selection initially used the internal protocol name rather than the descriptor's `openai-chat` name; the fixture was corrected and the final suite passed. No provider inference was used for these tests.

## Native client qualification

Each installed backend ran against an isolated temporary HOME/workspace and a loopback fake Responses endpoint, with synthetic credentials and a generated2x2 PNG. Two otherwise equal catalog cases were exercised:

| Client | Text-only catalog | Text+image catalog |
| --- | --- | --- |
| Installed patched CLI0.164.0 | Request contains zero images | One input_image; decoded PNG equals original bytes |
| Installed VS Code backend0.164.0-alpha.11.1 | Request contains zero images | One input_image; decoded PNG equals original bytes |

This tests native `--image` handling/model metadata and outgoing API data. It does not physically exercise terminal clipboard or VS Code paste UI, nor real provider vision recognition or the production Core transport. Both backends completed the positive fixture with exit0.

CLI binary SHA256: `dce2d889f7a0924894a8e2321f6c7101445cbc66a5baad821f725ce430899541`.
VS Code backend SHA256: `e6d386e8822288f6d5f327e966a5a5e1de928de8795bb6149716005a410f38f2`.
Evidence: `/home/tomka/.cache/lain-vision-fix-20261030/client-image-evidence.json` and `/home/tomka/.cache/lain-vision-fix-20261030/vscode-image-evidence.json`.

## Local catalog delivery

The current applied Core bindings and pinned native model metadata were used to qualify eight concrete native aliases. A copy of the current catalog was changed only in those entries' input modalities. Other464 entries, all group definitions/order, defaults, reasoning choices and other root TOML settings remain unchanged.

Used the existing `CodexArtifactInstaller` transaction/validation logic through a small private helper, first in a disposable dry-run, then for the separate lain profile. It created an immutable generation, compatibility copy and correct current/manifest hashes. Root config's sole change was its catalog pointer to the same generation; parsed TOML equality was checked. Installed profile integrity reports installed/not stale against applied revision917. Desired919 was not exported or applied, and pending runtime changes were preserved.

New catalog: `/home/tomka/.codex/.lain-artifacts/db09fba9a5944e9081d61b4e06c0089c/lain.models.json`.
Private backups/receipt: `/home/tomka/.cache/lain-vision-fix-20261030/backup`, `/home/tomka/.cache/lain-vision-fix-20261030/receipt.json`.

## Client refresh and remaining delivery

Restart the terminal client, or use a fresh process/session, to load the catalog. Reload the VS Code window/extension backend to invalidate its startup-bound model metadata; current conversations/processes may retain the previous text-only model info. No login or provider credential replacement is part of this repair.

**The Host still runs its previous exporter.** A fresh profile/catalog export from that old Host can reintroduce text-only entries until the source fix is delivered in a qualified Host update. No production Host payload was overwritten while it runs. The current repaired catalog is active now; source correctness and local artifact repair must not be described as an installed Host upgrade.

## Subsequent context metadata repair

The [auto-compaction follow-up](codex-auto-compaction-fix-2026-10-30.md) created a newer active generation which preserves all eight repaired image modalities while adding pinned native context defaults. The generation above is the image-repair receipt, no longer the final current pointer after that follow-up. Both source fixes still await installation into the running Host exporter.
