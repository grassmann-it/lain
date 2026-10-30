# Live Inspector: readable agent activity

Requested by the user on27 October2026. Outcome accepted; layout details below
are a proposed implementation contract. No implementation or native acceptance
is claimed by creating this document.

## User outcome

Quickly inspect what an agent is saying and doing to notice obvious mistakes.
Readable output and identifiable tool actions take priority over transport/event
metadata. This is observation, not automatic evaluation of task correctness.

## Default presentation

- Keep the existing request list and adjacent output area. Identify an agent,
  conversation, model and account only where supported by source evidence.
- Present assistant text as selectable plain text with actual line breaks and
  whitespace; do not expose JSON envelopes or JSON-escaped strings as the message.
  Concatenate contiguous deltas belonging to the same verified output block.
- Put a compact type label in each block header: Message, Tool call with tool name,
  reported reasoning, error, or status where actually observed. Preserve attempt,
  turn and tool-call boundaries; do not join text across a retry or missing data.
- Technical lifecycle/status events should update a compact request status or
  collapsed technical section instead of each consuming a full message card.
- Cursor/source IDs, full identities, timestamps, raw sanitized event attributes,
  measurement provenance and routine counters live in collapsed details. Preserve
  usable copy actions and user-chosen expansion state through live updates.
- Keep errors, capture gaps, truncation and expiration visibly distinguishable.
  A collapsed detail section must not hide that the displayed stream is partial.
- Follow output only while requested; user scrolling, selecting/copying text,
  pausing display and switching requests must not disrupt reading or inference.

The current InspectorView already merges some text deltas and provides collapsed
Event details and identities. The user's complaint therefore requires checking
the complete screen and incoming event mix, not claiming that adding another
Expander alone solves it. Source reviewed: RenderEvents in
src/Lain.Desktop/Inspector/InspectorView.cs; no new GUI test performed here.

## Content boundary

The existing [ephemeral inspector contract](../observability-contracts.md#inspector-api-and-ephemeral-dataflow)
remains applicable. Only captured allowed output can be displayed; missing prompt,
message role, tool result or reasoning must not be reconstructed or invented.
The current policy excludes request prompts and tool arguments/results. The user
has requested clearer tool classification, not blanket capture of these payloads.
If later requested, capturing them needs an explicit policy/retention extension.
No hidden model reasoning is requested. Plain text is decoded according to the
known protocol; do not recursively unescape arbitrary user/model text.

Keep bounded content, expiry, source/cursor gaps and redaction. No persisted raw
conversation feed as a side effect of making the screen useful.

## Proposed acceptance cases

1. Streaming assistant text reads as one growing text block within its known
   boundaries, including line breaks; transport lifecycle events do not bury it.
2. Interleaved tool calls stay separately identified by call ID/name; no invented
   result or success state. Text after a call remains in the correct turn/attempt.
3. Retry/failover and simultaneous agents cannot merge their content incorrectly.
4. All routine technical metadata is accessible on demand; gaps/errors remain
   visible without opening it. Copying text copies displayed text, not envelopes.
5. Private-Xvfb native review at supported narrow/wide sizes confirms readable
   output, useful screen allocation, stable scroll/follow and bounded expiry.

Evidence status: criteria not evaluated in this documentation change.
