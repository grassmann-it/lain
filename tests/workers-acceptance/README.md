# Serial worker acceptance

Run after restoring the seven listed .NET projects using their existing lockfiles
and runtime identifiers. Build and runtime checks share the repository's one
serial lane; do not start a native helper qualification concurrently.

```sh
python3 tests/workers-acceptance/run.py --output "$HOME/.cache/lain-qualification-20261026/workers-v1/NEW_RUN"
```

The output directory must not exist. Logs and the report are private; the report
records source hashes, exact commands, exit codes and timings. Commands execute
in separate process groups and are bounded to 300 seconds with descendant cleanup.
The source manifest covers implementation and fixture sources, excluding build
outputs. All provider responses, credentials and child clients in these suites
are synthetic. Actual Codex/helper shell execution has its own isolated harness
at `tests/codex-picker/native_code_mode_host.py`; preparation/unit tests alone
do not qualify it. Production installation and provider inference are separate
from this offline acceptance.

## Genuine native worker context

`native_runner.py` runs the actual C# runner and a pinned Codex CLI/helper pair
inside private bubblewrap network/PID namespaces. Private homes are bind-mounted
at unchanged HOME/CODEX_HOME paths. All provider replies and auth are synthetic;
the real client performs file/shell execution, read-only denial and an isolated
detached worktree write. The user's configuration and network are inaccessible.

```sh
flock /home/tomka/.cache/lain-verification-lane.lock \
  python3 -B tests/workers-acceptance/native_runner.py \
  --binary /absolute/codex --binary-sha256 EXACT_SHA \
  --helper /absolute/codex-code-mode-host --helper-sha256 EXACT_SHA \
  --assembly /absolute/Workers.Cli.Tests.dll --assembly-sha256 EXACT_SHA \
  --output /absolute/private/new-run --context-fixture --run
```

Without `--run` it only prepares. `--context-fixture` adds an owned discovery-only
MCP with 160 synthetic tools plus skill/project sentinels. It compares inherited
context with explicit `native` workers, checks MCP/automatic skill omission and
retained `AGENTS.md`, and records serialized tool bytes. Top-level tool entries
and callable namespace members are counted separately: Codex groups this server
in one namespace. This does not qualify Beam's 128-entry boundary or measure
actual tokens. The separate live boundary check and exact native evidence are
in the [follow-up report](../../docs/provider-tool-context-repair-2026-10-26.md).
