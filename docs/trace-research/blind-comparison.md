# Local conversation inspection and blind model comparison


> Scope update, 30 October: the user removed archive encryption/key management
> and prioritizes human inspection and skill-version comparisons across all
> recorded models. Earlier mandatory-encryption proposals below are superseded.
> [The current Sprint1 plan](../training-logging-plan.md) is authoritative for the planned scope;
> secret exclusion/private OS permissions and separate training/export rights remain.

30 October 2026 · Architecture proposal, no recording implementation.

## User direction

The immediate goal is to inspect how conversations and tool use actually look,
compare model behavior with less name/vendor bias, and develop better system
instructions from observed failures. Distillation remains a possible later use;
training eligibility is not a prerequisite for every permitted private review.
The user proposes hiding model names during assessment. This is blinding with
pseudonymous labels, not a guarantee of anonymous or unbiased content.

## Recommended usable first increment

A deliberately started local recording for one registered client and selected
routes, initially new Codex/Responses conversations through lain. Capture permitted
client-ingress request bodies and client-egress response/SSE bodies, plus typed
execution metadata, in a separate encrypted content store. This is an explicit
extension beyond the current output-only Inspector and metadata-only observer;
it needs its own capture policy, not just a switch which persists Live events.
Keep the global off/kill controls, secret exclusions, retention and independent
nonblocking archive failure policy from the brief. Training/export stays disabled.
Synthetic protocol/fault fixtures qualify the implementation; they do not replace
the requested eventual usable selected-session recording.

Needed content, when permitted: the instructions/messages actually submitted,
model-visible tool schemas, declared controls, model-requested tool names and
arguments, returned text, later client-reported tool results, ordering and IDs,
retry/cancellation/gap markers. Headers/keys/cookies and credential-bearing URLs
are excluded. Large or unsupported objects may be omitted with explicit coverage.
A generic sanitized API record is not necessarily byte-original raw content.

## API boundary and tool evidence

Two linked views: readable messages/tool timeline, and API records with known
protocol fields, event/item IDs, schemas and JSON source positions. Incremental
SSE assembly preserves ordering, parallel calls and incomplete arguments; it must
not merge by tool name alone. Requests resending history remain individually
traceable. previous_response_id/compaction or missing prefixes can leave a partial
session; start with a fresh conversation for controlled comparisons.

The client-facing request/response is useful for comparing the behavior the user
experiences. It does not establish the exact final upstream provider prompt:
lain and provider adapters can transform tools, instructions and reasoning
controls. Record the protocol/adapter/config version and capture boundary. A
separately qualified provider-boundary view is needed to investigate those
transformations; absent data stays unknown, not reconstructed.

Mark tool requested, result reported by client, and execution verified by a
harness as different facts. A full shell trace/real exit code requires explicit
client instrumentation. Proxy-visible tool calls/results are sufficient for the
first tool-format inspection; they do not establish that every command ran.

## Blind assessment proposal

- Preserve actual requested/resolved model, account/provider/route, version if
  disclosed and configuration privately in provenance. Do not overwrite originals.
- Generate a separate reviewer projection with labels such as Candidate A/B.
  Keep labels stable inside a comparison and randomize them between comparisons;
  keep the mapping separate from the blind view and its exports.
- Hide explicit identity in titles, routes, source panels, file names, error
  metadata and copy/export output while blind mode is active. A raw-source reveal
  is a deliberate unblinding action, recorded for that assessment.
- First judge behavior against a fixed rubric; optionally hide provider prices
  and timing until a second efficiency review. Preserve all actual measurements
  for later inspection. Do not conceal evidence of failure or differences in the
  tools/budgets supplied to the models.
- Responses, self-identification, distinctive tool names, error text, style and
  latency can reveal identity. Warn/mark possible unblinding. Never silently
  rewrite substantive response content merely to make it look anonymous.
- Lock/save the assessment before revealing the mapping. A single-user local
  hidden-name mode reduces cues but is not adversarial evaluator isolation or
  cryptographic concealment from the installation owner.

## What a useful comparison must control

Ordinary saved chats support exploratory observation; different tasks/context
cannot establish which model is better. For a controlled pair, freeze task,
repository/files or sandbox snapshot, harness/tool schema, permitted actions,
instruction version, route/adapter version and budgets. Requested controls and
actually observed effective controls are distinct; equal effort labels across
providers are not equal computation. Randomize order and use repeated trials
where feasible. Report incompleteness and uncertainty instead of a universal rank.

Rubric: task/test outcome where verified, correctness and relevance of tool
selection/arguments, avoidable actions, redundant rereads, unsupported claims,
recovery after a tool error, stopping behavior, latency and cost with observed
rather than invented usage. "Fewer tools" is not inherently better; necessary
verification can legitimately add steps.

## From observation to system-prompt experiments

Attach an annotation to a particular output/call/branch: observation, why it is
problematic, evidence, suggested instruction and review status. Example: after an
observed rejected argument format, propose a concrete tool-contract instruction.
Do not infer the model's private motivation or call every disliked action a bug.

Create a versioned prompt candidate; test old/new on the same selected model and
fixed task set. Then check unseen tasks for regressions. Comparing a changed prompt
on model B with an old prompt on model A changes two factors and cannot isolate
the prompt effect. Preserve model-specific prompts as hypotheses until tested;
avoid tailoring to one memorized conversation. Do not automatically modify the
user's live system prompt, routes or profiles from a review annotation.

## Suggested packages, not implementation approval

1. Explicit recording policy/identity, bounded encrypted local store and kill/
   retention/fault tests; first qualified Codex/Responses content path.
2. Loss-aware stream/message/tool projection plus API-record viewer; retain the
   existing bounded UI rendering behavior.
3. Separate blind review projection, randomized mapping, annotations and deliberate
   reveal; test identity leaks through details/copy/export.
4. Controlled task/prompt comparison manifests and side-by-side review. Automated
   replay/execution remains separately authorized; no implicit extra paid calls.

The storage/privacy/retention decisions remain necessary for real recording.
Dataset registries, SFT/DPO export, teacher synthesis and training are later
independent features. Provider/content-specific retention and evaluation rights
still apply; training restrictions alone do not define every inspection right.
