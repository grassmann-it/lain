# Native Codex `/model` acceptance — 25 October 2026

**Passed: seven native PTY cases against the exact newly built patched Codex CLI.** The final
run, `native-r3`, finished in 13.03 seconds. It retained 22 named screen captures, raw terminal
transcripts, final screens, per-case results, the synthetic home files, and resource evidence.
This verifies the real `/model` path; no AppEvent injection or external selection wrapper was used.

The product/runtime lane was explicitly released to Root immediately after the run. No compiler
ran in parallel, no production Codex or daemon was started, and no desktop window was opened.

## Exact inputs

- Binary: `/home/tomka/.cache/lain-codex-build/target/dev-small/codex`.
- Binary SHA-256: `5b3a058525925731f12eec3366fd6dff74f716fb2967791e4531e88e7a06d484`.
- Prepared source: `/home/tomka/.cache/lain-codex-sources/prepared-picker-scoped-unicode-locked-0.164.0`;
  the root/build lane had verified its alias fix and all source files before this acceptance.
- Integrated harness: `/home/tomka/Desktop/lain/tests/codex-picker/native_model_picker.py`.
- Harness SHA-256: `079ee9409e2e316dded0b417ba878ea936f11d80f807bb835f1784865946e67d`.
- Test-only harness corrections: `433f608` and `6c1dec0` on the isolated client worktree branch.
  Neither changes product source or the binary.

## Final results

| Invocation / fixture | Native result |
| --- | --- |
| `--profile lain`, provider `lain`, valid metadata | Free / IT Sec / Provider / Alle; actual second model menus |
| No profile, provider `lain`, valid metadata | Original upstream quick picker |
| `--profile other`, provider `lain`, valid metadata | Original upstream quick picker |
| `--profile lain`, other provider, valid metadata | Original upstream quick picker |
| No profile, provider `lain`, invalid extension | Extension ignored; original upstream picker |
| `--profile other`, provider `lain`, invalid extension | Extension ignored; original upstream picker |
| `--profile lain`, other provider, invalid extension | Extension ignored; original upstream picker |

The grouped case verified disabled **Automatisch · Noch nicht eingerichtet** first, exact Free
membership, a shared model in Free and IT Sec, Provider membership, hidden-model exclusion,
Escape returning to the group list, and unfiltered Alle. Alle included the visible synthetic
`codex-auto-review` marker, demonstrating that the fallback does not inherit upstream's auto-slug
exclusion. Selecting Shared Model through the native menu persisted the exact wire slug
`synthetic/shared` in the synthetic `lain.config.toml`.

Every case passed both its inner result and independent outer validation. All seven synthetic
ordinary `config.toml` sentinels stayed byte-identical. Every provider request list was empty.
The audit also confirmed different actual host/child PID and network namespace identifiers,
unchanged original HOME values, and the exact mounted synthetic catalog hash.

## Isolation and limits

The run used a private bwrap home bind-mounted at the original HOME path; HOME and CODEX_HOME
were not repurposed. Each case used its own PID/network/IPC/UTS namespaces, trusted synthetic
workspace, explicit newly built binary, `--no-daemon`, and a synthetic local HTTP deny server.
Only `/model` text and menu keys were entered. There was no inference prompt or provider call.

The systemd scope enforced `MemoryMax=1073741824`, `MemorySwapMax=0`, and `CPUQuota=100%`.
Recorded memory peak: **864,268,288 bytes** (824.23 MiB). Memory limit hits and OOM events: zero.

## Preserved earlier failures

`native-r1` failed its first case because sending `/model` and Enter in one physical PTY packet
triggered the existing paste-burst Enter suppression. The transcript showed the slash command
remaining in the composer. The case recorded no provider requests and an unchanged main sentinel.
The run was deliberately stopped after that failure; its artifacts and aborted-resource record
remain. The harness now waits for the configured footer and physically separates slash text and
Enter by a 350 ms pumped pause.

`native-r2` exercised all seven menus correctly, but its two ordinary-invocation sentinel checks
failed because upstream first-start screen-reader detection appended only
`[tui] screen_reader_detection_done = true`. This was confirmed against the retained files and
upstream `tui/src/screen_reader.rs`. The synthetic fixture now seeds that existing startup marker.
All `native-r2` failures and evidence remain; `native-r3` is a fresh complete seven-case run.

These were harness/first-start-fixture corrections. No product binary change was made between
r1, r2, and r3.

## Evidence and limits of the conclusion

Evidence root: `/home/tomka/.cache/lain-qualification-20261025/model-picker/`.
The final machine-readable results are `native-r3/summary.json`, `native-r3/acceptance-audit.json`,
`native-r3/prepared.json`, seven per-case `result.json` files, and `native-r3-resource.json`.

This is native UI acceptance with synthetic state. It does not qualify real autorouting,
inference capabilities, actual provider access, live account quotas, installation, or existing
production conversation migration. The router row was intentionally unavailable in this fixture.
The 14 targeted Rust library tests are separate work owned by the build lane; this report does
not claim they ran as part of these native cases.
