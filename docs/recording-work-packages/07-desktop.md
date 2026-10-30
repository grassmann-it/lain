# REC-07 · Saved conversations and skill/run variant inspection

Owner: Desktop worker. Depends on [REC-00](00-contracts.md) + REC-06 client
contract; start with shared fixtures, integrate real REC-05 queries later.
Status: plan only, no window/service/recording started.

## Outcome and ownership

A person opens a saved conversation in lain, reads messages/tool rounds, expands
known workers and API details, and sees available model/skill/prompt/run variants.
Closing/reopening UI does not erase the service-owned archive or stop an explicitly
enabled recorder. No review/grading, blind export, training or prompt changes.

Own future `src/Lain.Desktop/Recording/**` and
`tests/recording-desktop/**`. Integrator owns existing MainWindow/navigation/shared
resources and registration. Reuse theme/resources; no redesign of Accounts/Models/
Profiles, no duplicate state store or direct SQLite/filesystem access. API worker
owns the typed client; shared DTO changes go to00.

## Proposed presentation

- Current Live offers a route to **Saved conversations** using one reusable archive
  view. Final navigation allocation is coordinated with integrator; no duplicated
  main workspace. An unavailable live source must not make retained data disappear.
- Conversation/run list loads metadata pages only, with model/variant/time/coverage
  summary. No eager decryption requirement or key workflow; files are unencrypted
  under the accepted private-OS storage model. No full conversation payload per list.
- Selected conversation: ordered readable roles/parts, requested tool name/arguments,
  client-supplied result, attempt/model changes, incomplete and conflict markers.
- Expand worker/branch tree lazily; identity unknown/ambiguous stays explicit.
  Same tool name, similar text or close timestamps aren't an inferred parent edge.
- Each item links to retained API observation and boundary/representation. Display
  actual plain text once parsed, no JSON-escaped prose; unsupported content labeled.
- Run/variant panel shows available skill/prompt hashes/version/source with evidence
  status. User label isn't proof of actual loading or compliance. Manual variant
  metadata edits are separate from immutable captured messages.
- Recording status/control is distinct from display pause, Clear, archive deletion
  and inference stop. All used models included by default; optional filters visibly
  narrow scope. Unknown training rights do not grey out the reading view.

## Performance and interaction requirements

Keep retained controls and the current bounded text-layout behavior: at most8192
visible text characters per existing long-output page, sections<=4096 and technical
source details in25-entry pages unless a separately measured improvement replaces
it. These are current implementation facts, not a guarantee for the new archive.

Backend pagination is required in addition: list, selected timeline, workers,
requests, attempts and payloads fetch separately. No whole-archive traversal,
parse/pretty-print on every delta or full rebuild after one appended event. Preserve
selection, scroll anchors, expanded branches and focused text during polling.
Cancellation/generation checks prevent a late query painting another conversation.
Copy actions state whether they copy selected/visible/full retained data and obey
retention/deletion; never copy an expired hidden payload as if still retained.

## Acceptance

Private Xvfb/D-Bus using production view/client with synthetic in-memory source:

- narrow800x560 and wide1120x780; list -> selected conversation -> worker -> API
  record -> Back, with layout/scroll allocation checked;
- >=3 turns, full-history resend, model change, fork, root+two workers, two
  interleaved same-name tools, supplied result, failed retry, compaction and gaps;
- long message and many small tool/worker blocks; lazily paged queries and measured
  UI responsiveness/working set, not reuse of the previous363ms/61ms claim;
- focus/select/copy, user scroll disabling follow, collapse state, filter/selection
  transitions, stale response, source unavailable and stop/read-only history;
- close/reopen loads persisted fixture archive; no fake new live stream or lost
  conversation; source deletion invalidates visible content safely;
- unknown skill version/model revision/worker parent remains unknown, manual run
  label doesn't become an observed manifest; all-model capture status truthful.

Return source/artifact/fixture pins, actual checks/screenshots/metrics, no evidence
from user desktop/provider traffic, and small navigation integration requests.
