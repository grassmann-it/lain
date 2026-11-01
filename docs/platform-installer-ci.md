# Platform installers: synthetic CI on native runners

The `.github/workflows/platform-installer-contracts.yml` workflow runs the
unchanged `tests/platform-installers/test_contract.py` on `ubuntu-latest`,
`macos-latest`, and `windows-latest` with Python 3.12. The matrix runs serially;
a failure does not prevent diagnostics on the other operating systems.
PR and push triggers are limited to changes in the workflow, this documentation,
`packaging/platform-installers/**`, `tests/platform-installers/**`, and the two
Mac/Windows entry points. Manual runs are also available. There is no publish,
installation, or release job.

## Checks and isolation

- The Python engine and contract test are compiled in memory with
  `compile(..., 'exec')`. `PYTHONDONTWRITEBYTECODE=1` prevents import pycache in
  the source tree.
- Ubuntu and macOS check `scripts/install-macos.sh` with `bash -n`.
- Windows parses `scripts/install-windows.ps1` using
  `System.Management.Automation.Language.Parser.ParseFile`; parser errors fail
  the job. The entry point is not executed. The runner uses PowerShell 7; this
  is not separate evidence for Windows PowerShell 5.1.
- The complete existing suite runs without test changes, skips, or expected
  failures. Empty suites and skipped tests fail the job.
- Temporary fixtures reside under canonically resolved `RUNNER_TEMP`. This
  avoids macOS symlink ancestors such as `/var` without bypassing `safe()`.
  Windows must support file symlink creation; a preflight probe otherwise fails
  explicitly, without requesting administrator rights or disabling the symlink test.
- Lifecycle cases replace `layout`, `offline`, `architecture`, and `integration`;
  their actual file operations stay in the temporary test area. Rendering cases
  replace `Path.home` and `APPDATA`. No installer entry point, real package,
  process launch, Registry/PATH change, or write to the runner home is part of
  this check. The checkout must be clean afterward.

The suite constructs synthetic Mach-O/PE headers and temporary
`native-qualification.json` fixtures with passing and failing contract values.
This unchanged test mechanism is not generated native acceptance evidence: the
workflow issues no `native-qualification` report, adopts no fixture as release
evidence, and uploads no artifacts.

## Permissions and pins

Only `contents: read`; no secrets or persistent checkout credentials,
`pull_request_target`, package installation, caches, or artifact transfers.
The only Actions are checkout and Python setup; their normal source/Python
retrieval is the only intended download behavior. Commit pins were checked on
26 October 2026 against official GitHub repositories through `releases/latest`
and the corresponding Git tag refs:

| Action | Release | Commit |
| --- | --- | --- |
| [actions/checkout](https://github.com/actions/checkout/releases/tag/v7.0.1) | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| [actions/setup-python](https://github.com/actions/setup-python/releases/tag/v7.1.0) | v7.1.0 | `6d4fd22b20465ccc0f7354476588e8df7c9ca0e0` |

Runner images and the Python 3.12 patch version remain floating. The matrix does
not claim actual binary execution on all four target architectures; the RID/mode
matrix is synthetic.

## Delivery status and remaining native boundaries

Locally on Linux, 26 October 2026: 13 unchanged contract tests passed in the
shared `flock`/systemd verification lane with memory/CPU limits. Python compilation,
Bash syntax, and YAML structure were checked separately. No test/installer source
file was changed for that delivery.

Source inspection shows no inherent OS blocker with a canonical temporary path
and available symlink support. In particular, lifecycle cases with target `macos`
also use replaced integration on Windows; they do not test a real macOS or Windows
system lifecycle there. Missing Windows symlink rights and other native filesystem
differences remain hard CI failures to investigate from the first actual run.

Update: the [first GitHub run 40416133877](https://github.com/grassmann-it/lain/actions/runs/40416133877)
on source8708650 passed on all three runners, including their shell syntax checks.
The repository is now public; the source push triggered the run. macOS/Windows
were not executed locally. The green run confirms only contract tests and syntax
on those runners. Real native packages, Host/CLI/GUI execution, autostart/LaunchAgents,
user PATH, updates/uninstall in a user account, crash recovery, signing/notarization,
and Windows Core portability remain separate open acceptance work. This workflow
does not approve native packages, GUI, or Core. Earlier statements of “no workflow”
in the original installer delivery report describe its state at that time.
