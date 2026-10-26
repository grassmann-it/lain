---
name: lain-workers
description: Set up lain project worker rules, resolve explicit tasks to configured routes, and run or inspect bounded native Codex worker batches through the lain CLI. Use for targeted delegation with lain, not automatic task decomposition or quota/cost optimization.
---

# lain workers

Use the installed `lain` CLI. Read `lain workers help` before
depending on the installed contract; repository source does not establish an
installed upgrade. If worker commands are unavailable, report that limitation.
This source skill is named `lain-workers` for standalone Codex discovery
(`$lain-workers` after installation). `lain:workers` is the desired product name;
the skill source is bundled in the CLI assembly. Project installation is an
explicit local action; neither bundling nor copying it registers that namespace.

## Project context and authorization

Read the target project's applicable `AGENTS.md` and existing worker rules before
preparing tasks. Use the current request and prior session authorization for
delegation, configuration and writing. Do not request approval again for work
already authorized or add a separate skill approval procedure. A role, route or
saved project preference does not grant additional tool, account or spending
permissions. Preserve user authentication, existing client configuration and
service state; resolve a missing setup requirement within the user's authorized
scope rather than silently changing profiles, credentials or cost controls.

Without a concrete task, inspect the project's settings/status and explain the
available next step. Do not invent a workload or treat a one-off instruction as
a change to permanent project rules. Keep dependent work together; the caller
supplies targeted tasks in v1. Automatic decomposition and quota/cost optimization
remain later work.

## Setup

For a requested project-local skill installation, use the bundled source:

```sh
lain workers install-skill --project /absolute/project
```

Optional `--json` returns structured output. The command copies the assembly's
bundled skill into `.agents/skills/lain-workers` under that project. It refuses
an existing target, preserving local changes; do not remove or overwrite those
changes to force an update. Discover `$lain-workers` in the client afterward,
starting a new session if necessary. Installation does not initialize rules,
apply a profile or grant execution permissions.

For an authorized project setup, choose an existing profile and inspect its
current routes/capability evidence through the CLI; keep those as the source of
truth instead of copying a provider/model/capability catalog into this skill.

```sh
lain workers init --project /absolute/project --profile PROFILE_ID
```

Init creates `.lain/workers.json` with `maxWorkers: 2`, empty `purposes` and
`costPolicy: "configured"`; it refuses an existing file. The empty template grants
no routes or permissions. Add the intended purpose/route references within the
authorized project setup, then review the file against the project rules. Its contract
is `schemaVersion: 1`, `profileId`, `maxWorkers` (integer 1–12), `purposes` and
`costPolicy` (`configured` or `free_only`). Each freely named purpose has `name`,
an ordered `routeIds` array of existing stable route IDs, and optionally
`reasoningEffort` and `contextMode`. Order expresses the user's preference; it does not establish
optimality, availability or a free route. Use only route-qualified reasoning
values; omission does not prove that reasoning is disabled. Validate the reviewed
rules before planning:

```sh
lain workers validate --project /absolute/project
```

Choose purpose-level `contextMode: "native"` for self-contained file/shell tasks
that need no MCP, apps, plugins or automatic skill guidance. This session-only
mode preserves project `AGENTS.md` and native tools. Include the necessary task
instructions in the prompt. Missing/null `contextMode` or `"inherit"` preserves
the existing context; use it when integrations or skills are needed. Do not
disable global integrations to fit one worker. The native runner discovers and
disables direct MCP servers individually and verifies the effective settings
before a turn; unresolved configuration conflicts stop the task. This mode
applies to the CLI worker runner, not built-in subagents or existing IDE threads.

The Host must match the active profile/revision, required capabilities and the
existing runtime cost policy. The separately installed Codex `lain.config.toml`
and `lain.models.json` must match that profile/revision too; selecting a default
client provider is insufficient. Do not refresh/apply/install them merely to
make a status or plan request succeed.

`free_only` needs actual runtime enforcement. The project flag, route name,
quota reading or plan is not a spending guard. Preserve unknown cost and quota
values as unknown; `configured` does not mean unlimited spending authority.

## Plan explicit tasks

Create a private tasks JSON file with `schemaVersion: 1` and a `tasks` array.
Each task has a unique `id`, a configured `purpose`, a concrete `prompt` and
optional `writePaths`. For example, these are read tasks:

```json
{
  "schemaVersion": 1,
  "tasks": [
    {
      "id": "review-api",
      "purpose": "review",
      "prompt": "Read the API contract and report concrete inconsistencies. Do not edit files."
    }
  ]
}
```

Replace `review` with an actual configured purpose. For an explicitly authorized
write task, add its intended relative `writePaths`, such as `["docs/guide.md"]`,
and state the expected result in the prompt. Those paths describe planning and
integration ownership; they are not per-path filesystem ACLs.

```sh
lain workers plan --project /absolute/project --tasks /private/tasks.json --revision N
```

Substitute the current numeric revision for `N`; `--revision` is optional for
plan/run. Review the resolved profile, revision, route/model, reasoning, context mode and scopes.
The batch count must not exceed `maxWorkers`. Plan resolves the supplied tasks;
it launches no worker and reserves neither quota nor capacity. Use CLI/Host
diagnostics for an unavailable or unqualified route; do not substitute a model
from memory. Route order is evaluated before launch; the selected route stays
pinned during execution. On a revision conflict, re-read and review
the changed state instead of blindly resubmitting at a newer revision.

## Run and inspect

Use the bounded CLI runner when the batch needs a checked count, timeout and
managed child lifecycle. Continue within existing authorization once its tasks
and effective configuration match the request.

```sh
lain workers run --project /absolute/project --tasks /private/tasks.json --revision N --client /absolute/codex --timeout-seconds 900
lain workers status --project /absolute/project
```

Use an explicit absolute `--client` path to the complete native Codex CLI
**0.164.0** executable, the only version supported by this runner.
`--timeout-seconds` is optional, defaults to 900 per child run, and accepts
integers 1–3600.

The agreed native launch contract uses **no native `--profile`** for either real
`exec` or the configuration probe: the 0.164 app-server rejects that flag. The
Host's Lain profile/revision and separate installed artifacts must still pass
their checks. Both native invocations must receive the same complete CLI
overrides derived from that validated binding, with a fresh provider ID unique
to the run, its provider/auth configuration and catalog, plus the selected model
and effort. This is a per-run binding, not a change to the user's saved config.

Before a turn, native app-server `config/read` and `configRequirements/read`
must gate the effective selected provider, catalog, auth, model/effort, sandbox
and all three delegation flags below. Probes run serially before parallel worker
turns, avoiding native first-start initialization collisions in shared state and
system skills. They perform no turn; native Codex may initialize its local state.
Reject conflicting managed requirements rather than weakening them or proceeding
with inherited defaults. Require the runner's actual probe result; this contract
does not establish completed implementation or qualification.

Write sources must already be `trusted` in effective native Codex configuration.
Missing or untrusted entries fail with `native_config_source_trust_required`.
Do not set trust flags to bypass this check: native `--worktree` can otherwise
persist trust for a previously unset source. Preserve the user's trust choice.

Read tasks use native `--sandbox read-only`. Explicit write tasks use native
`--worktree --sandbox workspace-write` and start from the source repository's
clean **HEAD**. Before a batch containing write tasks, the whole repository must
have no staged, unstaged or untracked changes, including outside the project
subdirectory and declared `writePaths`. The runner captures the source HEAD and
rechecks cleanliness and identity immediately before each write-task launch.
These checks do not lock Git against unrelated concurrent changes. Ignored files
are not copied into native worktrees, and no WIP snapshot is created. Do not
commit, stash, clean or discard user changes merely to satisfy this precondition;
report the blocking state within the existing task scope.

Inspect the isolated worktree changes and report them for integration. No
automatic merge occurs. Do not equate a workspace-write sandbox with confinement
to the declared `writePaths`.

Child native delegation is disabled with `agents.enabled=false`, legacy
`features.multi_agent=false` and `features.multi_agent_v2=false`; do not start
nested workers through another route.
The run lock is per project, not a global Host scheduler. The runner's count and
child timeout controls apply to its own batch, not all agents or provider traffic.
The runner never relaunches a failed task, keeps its route pinned and sets the
client's request/stream retry settings to zero. This does not establish that all
Codex internals or providers never retry. Approval/sandbox controls must not be
weakened to obtain a result.
An existing harness's native subagent route is optional only where its effective
model routing and required capabilities are qualified; do not imply it inherits
the CLI runner's lifecycle guarantees.

Runtime state uses `LocalDiscovery.StateDirectory/workers/<project hash>`,
honoring `LAIN_HOME` when set and otherwise using
`LocalApplicationData/lain/workers/<project hash>`. Keep this runtime root private
and outside the checkout. Project rules in `.lain/workers.json` are versionable
by design; task prompts/files stay private and ignored according to project
conventions. Report the CLI's actual per-task outcome and timeout/failure;
keep incomplete output incomplete. Inspect status before deciding what happened
after interruption. Retain worktree results for deliberate integration rather
than deleting or merging them automatically. `SourceHead` records the source
commit captured from Git for a write batch. A task's `VerifiedWorktree` identity
comes only from a unique registered Git worktree with matching native thread
ownership metadata. Treat worktree paths or branches reported by the model as
unverified; process success does not verify them. Missing or ambiguous Git/native
metadata leaves the worktree identity unknown/null. An uncaptured source HEAD
also stays unknown.
Result fields `RequestedModel` and
`ExpectedAccountIds` describe intended routing, not observed upstream execution.
A successful process or turn does not establish which upstream account ran.
Report the actual account only from a matching history/inspector observation;
without that join it remains unknown. Report cost/usage only where observed:
neither skill prose nor successful planning enforces a worker, quota or spending
limit.

`succeeded` records execution completion. `ResultReviewState: "not_reviewed"`
keeps semantic quality separate: inspect the answer/diff and validate the task's
acceptance criteria before treating it as a correct result.
