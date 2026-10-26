# Bundled skills CLI

Run in the shared serial verification lane:

```sh
flock /home/tomka/.cache/lain-verification-lane.lock dotnet run --project tests/skills-cli/Skills.Cli.Tests.csproj -m:1 -p:UseSharedCompilation=false
```

Fixtures use private projects under `~/.cache/lain-skills-tests/`, including spaces
and Unicode. They cover all four bundled names, offline installation without Host
discovery, preservation of local edits, invalid names/arguments, symlink refusal,
private permissions, staging cleanup and the legacy worker installer. No login,
profile apply, provider inference or installed runtime changes occur.

The publisher obtains its skill list from the compiled CLI and copies exactly those
skill trees into the package. Source fixtures alone do not qualify a published
package or client skill discovery; those require exact-package acceptance.
