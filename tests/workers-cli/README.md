# Worker CLI and bounded native-client fixtures

Run from the repository's serial build lane:

```sh
dotnet run --project tests/workers-cli/Workers.Cli.Tests.csproj -m:1 -p:UseSharedCompilation=false
```

Linux fixtures exercise strict local JSON, setup without a Host, packaged skill
installation and preservation, typed plan requests, actual concurrent child
processes, native launch arguments, completed-turn evidence, retained partial
results, timeout, cancellation of descendant processes, per-project locking and
interrupted/unknown status. The subprocess client is a synthetic Python fixture;
it never calls a model provider or opens personal Codex state. Private evidence
is retained below `~/.cache/lain-workers-tests/` and its path printed on success.

Context fixtures exercise explicit `contextMode:native`: public config discovery,
individual direct-MCP disabling, managed conflicts, unchanged inherit defaults
and identical verified overrides at actual exec. Successful execution retains
`ResultReviewState:not_reviewed` rather than certifying semantic quality.

These checks do not qualify actual native worktree creation, sandbox isolation,
model quality, provider quota, or lifetime routing guarantees. Native client and
provider qualification remain separately identified in the release report.
The genuine isolated client comparison is documented in
[`workers-acceptance`](../workers-acceptance/README.md).
