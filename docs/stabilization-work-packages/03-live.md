# STAB-03 · Readable Live Inspector

Planning status: Semantic UI projection ready; capture-policy changes require a separate reviewed decision.
Owner role: Desktop/Inspector owner; coordinate optional feed work.
Dependencies: STAB-00; existing LIVE-1–4 review is the detailed source.

## Outcome and boundaries

Implement the user goal: read emitted agent text and identify tool activity to
spot obvious mistakes. Remove routine metadata-card noise, retain collapsible
technical facts. Do not expand output-only capture to prompts/tool payloads.

## Ownership

Desktop/Inspector and pure projection tests. Changes to Core observation fields,
Host redaction or DTOs are coordinated with integrator and isolated from initial UI work.

## Work

1. Use docs/live-inspector-review-2026-10-27.md and contracts/live-inspector.md.
2. Project semantic text/tool/status blocks, preserving attempt/turn/source/gap boundaries.
3. Make technical JSON lazy; preserve selection, scroll, follow and bounded expiry.
4. Qualify tool identity mapping before merging fragmented parallel tool calls.
5. Measure newline/redaction delay separately; never remove secret protections merely for speed.

## Acceptance — proposed, not executed

- Lifecycle/null-text fragments do not become fake message or successful tool cards.
- Contiguous answer content reads naturally; interleaved metadata does not bury it.
- Two fragmented concurrent tool calls stay distinct where identity is proven.
- Buffering/redaction/expiry/gaps are truthful; no invented results or hidden reasoning.
- Private-Xvfb narrow/wide cases cover copy, pause, resize, source restart and follow-off.
- Exact changed capture policy, if any, passes split-secret/Unicode/long-line tests first.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.

## Partial implementation · 29 October 2026

The [responsiveness fix and native evidence](../live-inspector-performance-2026-10-29.md) implement lazy technical details, retained event controls, indexed relationships and bounded long-output layout. Synthetic native Inspector acceptance passes; this does not complete the remaining semantic/capture acceptance above and is not installed delivery.
