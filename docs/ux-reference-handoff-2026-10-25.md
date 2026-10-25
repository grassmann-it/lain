# UX reference handoff, 25 October 2026

This is a read-only ST-05 handoff inventory, not a source freeze, product
acceptance or instruction to stage other owners' files. Root decides the exact
staging set. Provider research is outside this handoff. No source, owner document,
image, index entry, service or user window was changed by this audit.

The single inventory is the independent acceptance worker's
[handoff-closure.json](../tests/native-final-gates/handoff-closure.json), SHA-256
`7a8a287306f0dfbcfa5512a1357d3bee6cb78ccc02e2d8df0e6b3ba0c50b8317`. That manifest is delivered separately by its owner; integrate
it alongside this note. Do not replace it with a second media manifest. Its read
baseline is `e48bc118a5ecd2ada80648370ed2b8ef102fd01e` plus existing owner-authored
uncommitted design documents. The hashes below describe those working-copy
bytes, not their inclusion in a commit.

## Explicit document selection

These are exactly the 16 document seeds from the shared manifest. All were
untracked in Main when inspected. They provide UXD-10–15, the recorded user
selection, flow prompts/results, comparison history and embedded galleries.
This table is an explicit candidate staging list, not permission to stage an
entire dirty directory.

| Relative document | Bytes | SHA-256 |
| --- | ---: | --- |
| [ux-design-work-packages/10-selected-layouts-monitor.md](ux-design-work-packages/10-selected-layouts-monitor.md) | 4020 | `3cca50732e97d36e4ab4cf0de856970ec21badc0101c1a44eae0faf9fef89074` |
| [ux-design-work-packages/11-connect-interruptions.md](ux-design-work-packages/11-connect-interruptions.md) | 2423 | `0866ac9751680ed83ecf6489f640ac28b6af9de58b9c838d912b81cc4629a858` |
| [ux-design-work-packages/12-profiles-apply-recovery.md](ux-design-work-packages/12-profiles-apply-recovery.md) | 3755 | `8f637710a1802514cdea9606b7fe1caea90d2ac0552abc38f95a5b0e7ca15c6c` |
| [ux-design-work-packages/13-usage-partial-scope.md](ux-design-work-packages/13-usage-partial-scope.md) | 2465 | `3b826b5d3c9a239036823103b922addf6f280862c277f50a40b411ee85b64c91` |
| [ux-design-work-packages/14-live-pause-source.md](ux-design-work-packages/14-live-pause-source.md) | 2896 | `5216eab3108c9709cb079ccad80c07e004b731e4dde33675ec485196e08640c5` |
| [ux-design-work-packages/15-settings-and-native-gates.md](ux-design-work-packages/15-settings-and-native-gates.md) | 2904 | `4990f3f425362df58c3d7ef33f1fbb1aff2cecf70e8b38a0492f44195f7e6ce2` |
| [uxpilot-selection-2026-10-24.md](uxpilot-selection-2026-10-24.md) | 18168 | `0688f3a3002f494f09a5ab8f5acd4a8b64c1955dc10ab872eb2b767784ce505c` |
| [uxpilot-followup-2026-10-24.json](uxpilot-followup-2026-10-24.json) | 27102 | `91e18f696fdf27336d3025af71075a0f4fda5c593f23e79551cc928e919535a2` |
| [uxpilot-followup-2026-10-24.html](uxpilot-followup-2026-10-24.html) | 2674553 | `90664ebfb53a1d7f333f2d9a45bd1b3f51b3f169f25823d70fcfc164e8912fce` |
| [uxpilot-flows-2026-10-25.md](uxpilot-flows-2026-10-25.md) | 17649 | `34cab4524272cf37ab732bab43a436d94541d4954da15ec547a4769ab3a5a52e` |
| [uxpilot-flows-2026-10-25.json](uxpilot-flows-2026-10-25.json) | 37013 | `4e38863b1432b9b302c4fc39f532490dce2a54b4f6f8262695fa5e6e84f6bead` |
| [uxpilot-flows-2026-10-25.html](uxpilot-flows-2026-10-25.html) | 1449693 | `49da00931bfc9f8fbbdd131ecb4b74f585b24b13d269554b2940e64fa204de91` |
| [uxpilot-flow-prompts-2026-10-25.md](uxpilot-flow-prompts-2026-10-25.md) | 14196 | `4fa313a6928636de7b451253c5d4a4262d78d8d788b5fa9dc2c3729a0c2eff8f` |
| [uxpilot-comparison-2026-10-24.json](uxpilot-comparison-2026-10-24.json) | 22689 | `88f2e32ad6ad4eda929de49f4ef6b2ff7a59088ac6178ab07a815ac87c4408dc` |
| [uxpilot-comparison-2026-10-24.md](uxpilot-comparison-2026-10-24.md) | 9546 | `9fc3b09726d77613d938334ea6ba8fc33f60541ee6a9b5fd97d08dfac53b0917` |
| [uxpilot-comparison-2026-10-24.html](uxpilot-comparison-2026-10-24.html) | 3530765 | `d2c0affa3e212a28c4d0a5755502ed021b70852088772a505624fafd48dd610d` |

The current tracked planning context remains in
[the package index](ux-design-work-packages/README.md),
[UXD-00](ux-design-work-packages/00-integration.md),
[UXD-09](ux-design-work-packages/09-acceptance.md), and
[the original design results](uxpilot-design-results-2026-10-24.md).
Several tracked documents have unrelated or historical owner changes in Main.
They are not silently included in the 16-file selection above. Review any
additional tracked delta explicitly. The shared inventory is bounded to these
seeds and their local images; it is not a recursive inventory of all product,
provider or source documentation.

## Image and external-reference boundary

The shared inventory includes 70 existing local PNG references, 4,577,609 bytes:

| Ignored directory | PNG count |
| --- | ---: |
| `artifacts/uxpilot-20261024/` | 14 |
| `artifacts/uxpilot-alternatives-20261024/` | 14 |
| `artifacts/uxpilot-deepening-20261024/` | 24 |
| `artifacts/uxpilot-flows-20261025/` | 18 |

All 70 paths are ignored by Git. Existence in the current checkout does not put
them into a new checkout. The shared JSON inventory contains their exact relative
paths, byte sizes and hashes. Root must explicitly choose whether to transfer
that ignored media set separately or include selected media in version control.
No force-add, copying or media cleanup occurred in this audit.

The three selected HTML galleries are self-contained for their displayed images:
comparison embeds 31, followup embeds 29 and flows embeds 21 `data:image/` elements.
These counts include repeated references and alternatives; they are not 70
additional unique PNGs. The selected 16 documents total 7,819,837 bytes; including
the manifest's 70 PNGs totals 12,397,446 bytes. Embedded galleries permit viewing
without the ignored PNG directory. JSON local-image paths and older artifact
links still describe the separately retained artifacts and must not be claimed
to resolve from the selected documents alone in a clean checkout.

References to `uxpilot.ai` and `storage.googleapis.com` are external provenance
and navigation links. Their availability was not requested or tested; no remote
fetch occurred. A remote URL is not a durable local artifact. The galleries have
inline browser scripts for comparison/selection; opening a gallery and its local
notes is separate from product implementation. Historical recommendations and
alternatives do not override the explicitly documented user choices. Design
screenshots are synthetic references, not installed-runtime or native-input
acceptance evidence.

## Read-only checks and remaining gates

- Rehashed all 86 manifest entries: every file exists and matches its recorded
  SHA-256. No competing inventory was created.
- Resolved local Markdown/HTML file links in the 16 document seeds against Main:
  no missing file target and no untracked target outside the shared inventory.
  This is a file-path check, not a Markdown fragment/anchor validator or a claim
  that every currently ignored artifact will exist in a clean checkout.
- Checked document text for recognizable API/GitHub key prefixes, JWT patterns,
  private-key delimiters and quoted long credential assignments: no matches.
  This limited scan does not certify every encoded image, arbitrary secret
  format, remote resource or historic artifact. No credentials were read.
- No build, application, provider call, service action, user-display operation,
  automatic staging or source freeze was performed.

ST-05 handoff is concrete and reviewable after Root selects/stages the intended
owner documents and shared manifest, records the commit, and explicitly records
how ignored media is delivered. Installed and exact-package/native acceptance
remain separate gates owned by their existing runners and reviewers.
