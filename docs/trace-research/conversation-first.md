# Conversation-first recording and browsing


> Scope update, 30 October: the user removed archive encryption/key management
> and prioritizes human inspection and skill-version comparisons across all
> recorded models. Earlier mandatory-encryption proposals below are superseded.
> [The current Sprint1 plan](../training-logging-plan.md) is authoritative for the planned scope;
> secret exclusion/private OS permissions and separate training/export rights remain.

30 October 2026 · Next-increment architecture and work-package proposal.
No archive/capture has been enabled by this document.

## Confirmed direction and scope revision

The user agrees that assessment is a separate step. Record the conversation
content observable on explicitly enabled lain connections first; anonymized
exports and assessment come later. Next prioritize coherent conversations with
messages, tool calls/results and evidenced subworkers, efficient storage and
viewing in lain UI. This supersedes the recommendation that blind assessment
must be in the initial deliverable, and the earlier one-short-session scope is
not a confirmed limit. It does not remove the original exclusions for credentials,
invisible interception, unapproved cloud upload or hidden reasoning.

An opt-in first implementation should support ongoing capture on the enabled
scope, not require the user to press Record for every turn. New installations
still default off; global stop and explicit client/route controls remain.
"Everything" means the supported, permitted conversation payload and metadata
crossing the named boundaries, not arbitrary HTTP headers, repositories or
traffic which bypasses lain. Existing chat logs are not consent to silently
import all historical material or a reason to bypass the capture security design.

## A conversation is a graph, not a request file

Keep these separately identified and causally linked:

- Conversation and branch: model switches need not split the conversation; an
  actual fork remains a distinct branch even if much of its prefix is shared.
- Turns and API requests: one turn may contain several model calls and tool
  rounds. A request that resends history is a new observation, not proof that the
  user wrote all messages again.
- Dispatch attempts: retries/failover remain attributable to their real model,
  route and outcome; don't make a failed attempt's output the successful answer.
- Ordered messages/content items and tool calls: arguments may arrive in fragments;
  explicit scoped call/item IDs connect a later result. Same-name tools, time
  proximity and identical text do not establish identity or execution.
- Workers and parent edges: root conversation, worker conversation and branch
  identities remain distinct. Explicit harness provenance can connect them.
  Missing/conflicting relationships stay unassigned/ambiguous rather than being
  guessed into one chat or inferred from a familiar tool name.

Reuse verified existing dimension/evidence semantics from
`Lain.Core/ObservabilityContracts.cs` and the adapter dimension parser, not the
short-lived Inspector buffer as an authoritative archive. Scope native IDs by
client/harness/source generation; don't merge unrelated clients sharing a token.
A known root conversation must not swallow all simultaneous requests with no
known relationship. A UI navigation tree is a projection of this graph, not the
only persisted representation.

Store available original IDs/provenance privately so later anonymization does
not destroy joins. Full history resends, compaction, missing prefixes and backend
state references require explicit completeness markers. API views identify their
capture boundary and whether retained content was redacted or normalized; they
must not claim packet-level or exact upstream prompt fidelity.

## Efficient persistence without losing distinctions

Recommended storage design, still to be qualified:

1. A small independent index for conversation/branch/request/attempt/tool/worker
   relationships plus an append-only source-event sequence with gaps/drops.
2. Encrypted payload objects with scoped content-addressable references. Repeated
   messages, instructions and tool schemas can reuse physical objects; every
   request still keeps its ordered occurrence references and own outcome.
   Identical content never proves that two logical messages are the same event.
3. Compression before encryption and bounded record/chunk sizes. Separate privacy
   namespaces, rights and retention ownership in deduplication; reference counting
   and deletion must not erase another retained reference or leave orphaned copies.
4. No second full JSON request dump for every history resend by default. A known-
   protocol semantic envelope can preserve all supported fields with object refs;
   its API rendering is normalized JSON, not byte-identical wire data. Exact raw
   body retention, if needed, is a separately named representation with its own
   space/retention budget. Measure savings on repeated long-history fixtures.
5. Bound queue bytes, event count, work concurrency and decrypted working set.
   Disk/fsync, encryption, compression and indexing happen outside provider stream
   delivery. Record dropping/omission on pressure; never promise lossless recording
   under arbitrary pressure while also promising fail-open bounded resources.
6. Page metadata and payloads independently. Loading a conversation list must not
   decrypt every response or deserialize whole sessions; opening one conversation
   must not load all its subworkers. Preserve the recent bounded text-layout fix.

Maintain the original accounting journal and its fail-closed behavior unchanged.
Archive failures stop/degrade only archive capture, with visible completeness.
No reuse of auth/inference keys as encryption keys, no plaintext fallback when
content protection is unavailable. Key storage, retention and disk budget need a
concrete implementation design before real content is accepted.

## UI outcome

Recommend extending Live with separate **Now** and **Saved conversations** modes,
rather than adding another duplicate top-level page. This navigation is a proposal.
Closing UI does not stop an explicitly enabled service-owned recorder; stopping
recording, pausing display, deleting an archive and stopping inference are
separate actions.

A saved conversation opens a readable ordered message/tool timeline, model changes
and retry markers, plus an expandable worker tree. Each block links to the
corresponding retained API item/event. Tool payloads and technical metadata are
collapsed on demand, ordinary emitted text is readable, and gaps/incomplete
streams remain visible. Subworkers show a brief status/summary derived only from
available evidence, and load lazily. Reopen after UI/service restart without
misrepresenting archived messages as a new live stream.

## Next implementation packages

| Package | Ownership and work | Observable acceptance |
| --- | --- | --- |
| CONV-1: causal identity and fixture contract | Core DTOs/projector + synthetic protocol fixtures; coordinate adapter/Host/client contracts | One root + two workers, parallel tools, retry, fork, model switch, missing identity and compaction remain correctly linked or explicitly unknown. Export need not exist |
| CONV-2: persistent protected archive | Separate Host archive store/migrations, payload refs, key handling, retention, source index and bounded worker | Restart/recovery, duplicate ingestion, deletion/reference rules, disk/queue pressure and repeated-history size/RAM measurements; no credential payloads or plaintext fallback |
| CONV-3: actual capture integration | Existing auth/admission and attempt/stream boundaries for first Codex/Responses path; client/route scope and global stop | Fresh multi-turn conversation + tools, exact outcome/coverage, UI closed, stop/restart; output equivalence and unchanged accounting gates; no invisible interception |
| CONV-4: saved conversation UI | Shared typed client + Desktop paged browser, timeline, worker graph and API item detail | Native Xvfb narrow/wide, long session/parallel workers, copy/selection, collapse, reopen, follow-off and resource checks against exact deliverable |
| Later: export/review | Read-only anonymized derivations preserving joins; optional blind review and prompt annotations | No rewrite of originals, scoped permissions and no automatic training approval |

CONV-1 can proceed independently with entirely synthetic data. CONV-2/3 require
concrete key/retention/client-identity integration; the implementation must make
those choices explicit rather than silently weakening the original brief.
CONV-4 can use the same fixture contract in parallel; native acceptance uses the
installed deliverable, not copied mock UI. Supporting more client/protocol paths
is incremental and separately qualified, not inferred from a provider's name.

The packages are prepared for the requested next implementation, not claims of
completed code. Actual real-content enablement must be explicit and observable.

## Optional model explanations

A possible skill can request short task-relevant assumptions, evidence and
uncertainties. Keep these as **model self-report annotations** linked to the
observed turn/action. A later generated explanation is not the model's actual
internal reasoning and must not be labeled as recovered chain of thought.
Annotations never rewrite the captured messages/tool events or establish success.
See [decision-note proposal](decision-notes.md).
