# Optional decision notes for prompt improvement

30 October 2026 · Skill idea only; no skill installed or prompt changed.

The user proposes brief explanations alongside chat to reveal useful assumptions
and improve system instructions. Recommend a concise **decision note** rather
than simulated/reconstructed internal reasoning. Models can provide useful
rationales, but a self-report can be mistaken or a plausible post-hoc story.
External evidence and controlled prompt experiments remain necessary.

## Proposed note contract

At a significant task decision, optionally provide:

- Task-relevant assumption, stated as an assumption.
- Observable source or tool result supporting the decision; identify what has
  not been checked. Do not invent evidence or claim a tool ran without a result.
- Material uncertainty and a practical next verification step.

Keep it brief; no exhaustive internal deliberation or narration before every
trivial command. Example, illustrative only: "Assumption: the CLI and UI use the
same configuration. Evidence: both point to the same file in the inspected code.
Still unknown: whether the running process loaded this revision. Next check:
read its reported revision."

Potential instruction, not yet an installed skill:

> For consequential decisions, state the relevant assumption, the observable
> evidence you used, and any material uncertainty or next check. Keep this to a
> few short lines when useful. Provide a concise explanation, not a purported
> transcript of private internal reasoning. If explaining afterwards, label it
> retrospective and don't assert unobserved causes as facts.

## Provenance and comparison

Record note kind separately: native provider-returned summary (when allowed),
contemporaneous model self-report, retrospective model explanation, or external
reviewer hypothesis. Keep author/model, time, applicable prompt/skill version,
turn/action reference and evidence references; missing context stays explicit.
Do not classify any of these as human verification automatically.

The note instruction changes the model's behavior, token use and potentially tool
choices. Mark runs with it enabled. First preserve a baseline without it; compare
notes-on/notes-off or prompt-v1/v2 on the same model/task before attributing a
behavior difference to a model. A skill requesting justifications is itself an
intervention, not passive observation of standard behavior.

User instructions can change many defaults, such as response style, when to
verify an assumption or how to report uncertainty. They don't override higher-
priority instructions, safety constraints, tool availability or actual API schema.
A skill text also cannot guarantee a separate machine-readable event channel in
every harness. A structured note tool/client event would be a separate integration;
ordinary commentary can be retained as such in the first supported harness.

No automatic live system-prompt updates, extra teacher/judge calls, training
labels or export permission arise from collecting notes. Prioritize the requested
conversation/tool/worker archive before this optional experimental skill.
