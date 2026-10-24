# Local CLI connection qualification

Run `./tests/account-connect-cli/run.sh` only in the coordinator-approved slot.
It creates a temporary linux-x64 project referencing the real Host, uses locked
restore and disables MSBuild node reuse. There are no shared project/lock edits.
The fixture executable is copied into a new private directory and verified by
hash/version before execution. It contains no network or provider code and reads
only its explicitly owned fixture directory. No installed CLI, normal user config,
login, provider request, inference or service is used.

Latest result: **65 assertions passed**, final incremental build zero warnings or
errors. Initial complete Host compilation reports two existing nullable warnings
in PublicBrowserLoginAdapter.cs:63, outside this package. Source/static checks and
fixture artifact hash agree; see [qualification evidence](../../docs/account-connect-qualification/cli/README.md).

The checks exercise executable/hash/version rejection, forbidden environments and
symlinks, private stdout/stderr serialization, exit errors, bounded overflow,
process/descendant deadline termination, exact Requesty export parsing, safe
browser actions, journal modes/context/replay, wrong resource, malformed result,
active reconnect/cancellation, and crash before/after shared credential publication.
The real LoginOperationService publishes a fixture key without routes/profiles and
preserves account identity/name while rotating its generation on reconnect.

All five real method adapters return specific disabled/key-fallback results.
Alibaba fixtures preserve ordinary API scope, raw direct/DataV2 envelopes, status,
remaining/original grant amounts, Epoch-ms expiry, actual usage units/interval and
independent partial failure. An empty active-status qualification set cannot make
a grant available; expiry never becomes a reset. Console session and API key are
separate private fields, and cloud AK/SK/STS/unknown regions/hosts are rejected.
