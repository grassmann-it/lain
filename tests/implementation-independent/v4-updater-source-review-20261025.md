# v4 update source review, 2026-10-25

Reviewed source only:

- Main `tests/delivery-stabilization/v4_update.py`, integrated `e647c19` plus
  `4233d66` (worker delta `930c901`).
- `/home/tomka/.cache/lain-qualification-20261024/update-installed-lain-v4.py`,
  independently hashed as
  `cc8a45cd9b9fca3c870e25c0c759af2fbeb07baff75c87eb03d69af55dcc2de5`.
- Relevant `actual_packages.py`, `upgrade_probe.py`, installer and maintenance
  source contracts. No helper import/execution, build, tests, services, processes,
  real installation state, credentials, or private evidence were accessed.

## Pre-execution guard findings

1. **No exclusive updater lease.** The private helper's execute path starts at
   line 183 without a fixed installation-wide lock. Two helpers with different
   fresh backup destinations can both pass baseline and stopped-writer checks,
   then interleave prior-app archive, install and service start. Guards detect
   some outcomes after mutation, but do not serialize it. Acquire an owned,
   no-symlink, nonblocking fixed lock outside app/app.previous, hold it through
   final verification/failure stop, and refuse a second updater before mutation.

2. **Loaded service identity is assumed.** The helper pins the installed app and
   uses the exact intended `systemctl --user ... lain.service`, but does not
   establish the loaded service's FragmentPath/ExecStart/environment before
   stopping it. A modified or stale-loaded unit could control another install.
   Validate that the loaded unit is the expected owned unit and executable/state
   path, and reject unexpected overrides before stop. This is a source guard gap,
   not a claim that the user's actual service is misconfigured.

3. **External backup can become stale before overwrite.** Lines 201-210 back up
   launcher/unit/desktop/icon before stopping. Their fingerprints/absence are not
   checked again immediately before the installer at line 241. A concurrent edit
   could be overwritten while the backup contains only an older version. Recheck
   all four external records before archive/install; the updater lease alone
   does not serialize an independent editor. Frozen configuration/runtime/journal
   files do already receive repeated checks.

Findings were sent to Root and the delivery worker. No updater edits were made
by the reviewer. Execution readiness depends on Root resolving or explicitly
bounding these guard gaps and completing the exact package qualification.

## Confirmed source properties

The private updater defaults to plan-only and requires a hashed passed,
non-plan-only report matching all four package identities. Source commits,
complete cli/host/desktop identities, package inventory and Core qualification
are checked. It requires the installed previous Gateway source `793822c`, captures
dynamic CLI identities twice, and preserves desired/applied revision, profiles,
models and historical token components rather than hardcoding current counts.

UI detection defers/refuses an update; it does not kill UI or discard drafts.
Service control targets the fixed lain.service name. After stopping, the helper
requires no own installed writer, uses the maintenance backup's before/after/
copied inventory verification, includes app.previous in the full stopped state,
and backs up the four external integration files. app.previous is archived only
after the fresh stopped snapshot is checked. Installation uses the pinned real
installer. Current selected generation/material/auth files are compared by
opaque hash, without credential JSON or SQLite parsing by this helper.

The failure path attempts to stop the named service and optionally snapshots
the failed current state. It never promotes old credentials, old application,
or an older database schema. This intentionally leaves a failed upgrade stopped
for review; it is not an automatic recovery promise. A UI that opens during a
child command is detected afterward; the helper does not forcibly terminate it.

The v4 harness uses synthetic state in a distinct loopback-only network namespace,
with the trusted parent namespace anchor reused from the existing harness. It
tests desired19/applied2, schema4 integrity and history, full stopped backup,
prior-app archive, real new installer, app-only return preserving a simulated
newer grant, and forward update. Monitor requires no Core package/listener. Same
Core binary reuse requires exact source identity and identical qualification
record. The explicit scope remains this fixture, not arbitrary downgrade or
rotating-grant recovery. No execution result is inferred from source inspection.

## Helper guard follow-up, SHA-256 `8d25c3fc229537a23e667a104eb311e294474ad7f9009d8ef3aa80dfe5f82c87`

Independently read and hashed the revised helper source. Findings 1 and 3 are
closed at source level: a fixed private nonblocking flock lease surrounds all
execution and failure cleanup, and all four external file fingerprints/absence
are rechecked immediately before the installer. The lock descriptor refuses
links, foreign ownership, non-regular files, extra hardlinks and non-private
permissions; it is not inherited across exec.

Finding 2 is substantially addressed: loaded FragmentPath, single ExecStart
path/argv, and own MainPID/executable are checked before baseline and every
service action, including failure stop. One remaining boundary was sent to the
delivery worker and Root: loaded Environment/EnvironmentFiles are not checked,
so the same Host executable could operate on a different LAIN_HOME. Require the
exact loaded own state path and fail closed on unresolved environment files.
No claim about the real unit's configuration is made. No helper execution or
private state inspection was performed.

## Own-state guard closure, SHA-256 `692691c6ab7924d602bd641dedbd0b747ce28852f9505a89aef12eeab054b312`

Independently read and hashed the final helper revision. The remaining specific
service-boundary finding is closed at source level: own_unit requires all loaded
environment metadata, rejects EnvironmentFiles, parses Environment privately,
requires exactly one LAIN_HOME equal to the fixed STATE, and rejects any
UnsetEnvironment rule for that key. Environment fields are removed from the
returned/persisted unit record. The existing exact unit/executable/PID checks and
recheck before each stop/start remain present; stopped MainPID zero is accepted
for the intended subsequent start.

All three concrete findings in this review are now addressed in source. This is
not an execution result or authorization to update: exact-package qualification,
runtime guards and the orchestrator's execution decision remain separate. The
reviewer did not invoke the helper or read the installed unit/state.

## Process-scope adjustment, final SHA-256 `a88a088c81f9ee7a7548b0ed8a704899c12a8b42ccc8bb46a4910760e3ce889d`

Source-only review of the adjustment requested after Root encountered nondumpable
external user processes. The reviewer did not inspect live processes. Root supplied
the external role/cgroup evidence separately. Exemptions require the exact listed
external comm and cgroup; systemd additionally requires the system manager's
reported user-service MainPID, and sd-pam its matching PPid. Unknown unreadable
processes still refuse the audit. Accessible own executables remain inspected.

Two narrow source findings in intermediate `761996bf...` were fixed: an already
recognized executable candidate now remains classified as a candidate if argv
later raises PermissionError, and Desktop DLL detection accepts both relative
and absolute arguments. The final monotonic candidate predicate uses both comm
and executable basename for Lain.Desktop/Lain.Host/Lain.Cli/lain-proxy/dotnet;
these candidates cannot take the external-role EACCES exemption. No argv or
environment values are logged by this classification.

The loaded-unit show call now includes `--all`, allowing deliberately empty
EnvironmentFiles/UnsetEnvironment fields to remain visible while preserving the
required-field and own-LAIN_HOME checks. Source-level closure confirmed for this
specific adjustment; no runtime outcome or whole-machine security guarantee is
claimed. Prior update lease, backup, failure-off and no-rollback guards remain.

## EnvironmentFiles compatibility follow-up, SHA-256 `d16b41752d09d42b1456d19842f25879042a605e49598ce8edae0dc40e6419fd`

Root's read-only preflight found that EnvironmentFiles may be omitted even with
`systemctl show --all`; the preceding source observation did not establish actual
visibility on this installation. The revised helper invokes a fixed user-systemd
D-Bus EnvironmentFiles property read only when the show key is absent. The
existing run wrapper requires command success, and the fallback accepts only
`a(sb) 0` after surrounding whitespace removal. Missing output, another type,
nonempty arrays and failed queries reject the update. A present nonempty show
property remains rejected. Property contents are not returned or persisted.

Narrow source-level closure confirmed after independent source read/hash; no
runtime, D-Bus, systemd or private-state query was performed by the reviewer.
