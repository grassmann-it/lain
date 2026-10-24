# Local official-CLI connector qualification

This package implements local process/ownership boundaries. **No actual provider
CLI is enabled or qualified.** Each provider's disabled JSON manifest and report
names the missing evidence independently. No installed CLI or normal configuration
was discovered, invoked, read or modified. Public manufacturer source alone does
not certify a release binary or the account-specific resource/rights.

The executable fixture is a tiny local Python script with no network code. Its
SHA256 is recorded under tests/account-connect-cli/fixture-artifact.json; tests
copy and hash it before execution. Fixture qualification cannot enable the
production runner or Alibaba adapter. It does not establish an installable
manufacturer manifest or a license for bundling one.

Source implementation includes shell-free argument lists, executable/hash/version
qualification, an environment replaced with approved nonsecret isolation fields,
separate private bounded stdout/stderr, deadline cancellation and own process-tree
termination, redacted process result serialization, and no HOME rewriting.
Profiles/directories are 0700; metadata journals and fresh result files are 0600.
Symlink/path escapes are rejected. Ordinary provider config/keyring/refresh files
are never searched. Browser actions require a separately qualified field and
HTTPS host/query allowlists; callback codes and secrets cannot become status URLs.

The journal records operation/account/revision/generation digest, own profile
reference, phases, process identity and an optional known remote key ID, never
stdout, credentials or callback codes. A same-operation replay cannot reissue a
key. An unresolved earlier issuance for that account also blocks a new operation
until the authoritative shared-publication predicate confirms adoption or recovery
is explicitly completed. Account leases and the shared lifecycle serialize connection attempts.
Restart reconciliation terminates only an exact PID/start-time/executable match;
otherwise it never kills another process. Root supplies the authoritative shared
publication predicate: committed operations become adopted, uncertain ones require
review of the saved own profile/provider keys. No automatic create/revoke retry.

Alibaba raw reads use IAlibabaConsoleReadOwner with exactly trial, trial-protection
and Online usage methods. The implementation has no generic public Console API
caller. Only Beijing/Singapore owned profiles are accepted, with exact site,
workspace and API host. No cloud AK/SK/STS or default-region fallback is imported.
The Console session stays in the connector's own profile and is not copied into
the ordinary inference key. No refresh is attempted without an explicit owner.
Session failure preserves the static inference key; reauthorization targets the
same own Console profile after the full ownership integration is qualified.

Trial raw details preserve provider status, original amount, remaining amount and
Epoch-ms expiry. Unqualified active status vocabulary defaults to no available
grants. Trial scope is the account in International/Singapore and a specific qualified model/unit;
Coding Plan is excluded. Expiry is never a periodic reset. Protection is a boolean
observation, never a claimed transport enforcement. Workspace usage preserves
actual units and explicit interval, with DataAsOf null when provider time is absent.
One failed read retains valid independent observations; no zero balance is inferred.

Integration owner/root still must:

- Keep real methods unregistered/disabled until each manifest and identity/isolation
  proof is approved. Source-only Command Code/Fireworks/Mistral cannot run.
- Supply authoritative current binding checks for delegated read scope and
  generation; a profile reference is opaque, never a filesystem path from the UI.
- Publish independent Alibaba ordinary key and delegated Console capability/owner.
  The present lifecycle accepts one credential, so mixed/quota-only CLI bootstrap
  fails before process start. No monitor request creates an avoidable API key.
- Add typed grant expiry/status UI semantics only after active grant semantics are
  qualified. AlibabaTrialGrant preserves these facts locally; no generic DTO was
  extended for an unqualified reader. WindowEnd marks one-off grant end, not reset.
- Feed shared publication state into journal restart reconciliation and use shared
  credential recovery; never treat an old own profile as a newly authorized login.

Verification command: `tests/account-connect-cli/run.sh` in a centrally granted
build/runtime slot. The temporary project references the real Host with locked
linux-x64 restore and node reuse disabled, leaving shared project/lockfiles alone.
Local verification on 2026-10-24: **65 assertions passed**, including real local
fixture processes, private pipes/environment, timeout/descendants, shared lifecycle
publication/reconnect, journal replay/crash cases, disabled manufacturer methods,
and Alibaba raw schema/partial-result checks. Final incremental build: zero
warnings/errors. The first full Host compile also exposed two existing nullable
warnings in PublicBrowserLoginAdapter.cs:63; no new CLI/source warnings remain.
The test harness was corrected to catch InvalidDataException explicitly, allow a
bounded child-signal scheduling interval, and reach the journaled login process
before testing an in-flight cancellation (pre-issuance setup failure is distinct).
All fake process output stayed private. No product/project/lockfile changed.
Shell syntax, Python AST, five manifest JSON files and git diff checks pass.
Real login/read rights and every manufacturer artifact hash remain unqualified.
