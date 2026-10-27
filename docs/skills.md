# Bundled skills

Lain's current source bundles four skills for coding assistants. Check the
installed CLI first; developing source does not update an installed package.

```sh
lain --help
lain skills list --json
```

| Skill | Purpose |
| --- | --- |
| `lain-usage` | Inspect service status, accounts, models, usage, and quotas through the CLI |
| `lain-workers` | Work with bounded worker tasks using the documented worker commands |
| `lain-connect` | Guide account/client setup using existing public management operations |
| `lain-doctor` | Diagnose account, route, export, configuration, and client activation state |

For authorized workers, choose character/handle names autonomously; vary sources.
Favor Hackers, Kung Fury, The X-Files, Fringe, GitS and Lain;
also Blade Runner, Mr. Robot, John Dies at the End, Supernatural, Buffy;
Abridged: Hellsing Ultimate, Dragon Ball Z, Sister Leveling.
Hacker fiction, cyberpunk, mystery/horror, cult fiction and parodies welcome;
computer relevance optional.
Names stay unique/stable per run, including resumes. Set actual names where
supported; otherwise map aliases to native IDs. Record each name's source;
show tasks separately. Labels only: no roleplay or model/behavior/permission
changes; no delegation authority.

Install a bundled skill explicitly into a chosen project:

```sh
lain skills install lain-usage --project /absolute/project --json
```

The destination is `/absolute/project/.agents/skills/lain-usage`. Use the exact
name reported by `lain skills list` for other skills. Installation copies embedded
package resources, requires no Host connection or download, and refuses an
existing target so local edits remain intact. Review an existing skill before
choosing how to update it. Bundling does not automatically modify personal skill
libraries. The legacy worker skill installer remains supported.

After installation, let the assistant discover the project skill; start a new
session if necessary. File installation alone does not prove client discovery or
activation. Skill installation does not apply a profile, change accounts, log in,
run provider inference, or activate a running client.

For read-only diagnostics, the current command contract includes:

```sh
lain doctor --target codex-cli --project /absolute/project --json
lain doctor --target codex-vscode --project /absolute/project --json
```

`--profile NAME`, when used, denotes the native Codex profile. Doctor distinguishes
account setup, model routes, applied Host profile, export integrity, selected
configuration, reasoning effort, and runtime activation. Unknown activation is
not proof of failure or success. Doctor performs no automatic repair or inference.
A selected VS Code package and activation in an existing window/thread remain
separate observations.

Use [CLI usage](cli-usage.md) for quota/history semantics and the
[Codex CLI](../clients/codex/README.md) or [VS Code](../clients/vscode/README.md)
guides for optional client integrations. Skills use the same public operations
and authorization boundaries as the CLI; installing a skill grants no additional
permission for account, profile, or runtime changes.
