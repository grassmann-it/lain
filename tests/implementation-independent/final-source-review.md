# Final integration source review, 24 October 2026

Reviewed Main `87c9b5b` and final wiring `1206cb7`, including the owner refresh
implementation introduced by `460dd09`. A targeted follow-up inspected A12 fixes
`41cd70a`, `7d7c1ce`, `f33f3bc` and renewal preflight fix `eaf9d6d` in Main.
These passes performed no build, process
launch, provider request, real credential access or installed-service change.
Unrelated provider research changes in Main were not modified. Root owns the
next frozen-artifact verification and delivery decision.

No additional release-critical source defect was found in the reviewed account
publication, restored Gateway bootstrap, Monitor gating and Apply/recovery paths.
This is a source conclusion, not complete AC-10/ST-01–05 acceptance.

## Reviewed boundaries

- `AccountService.CommitPrepared` publishes through configuration CAS, checks
  target account generation and verified binding, and retains separately selected
  purpose credentials. Removal or reconnect cannot silently resurrect the old
  operation's account.
- Login checkpoints immutable private results before local publication. Final
  publication alone takes the shared mutation gate; remote waits and cancellation
  remain outside it. An intervening configuration revision produces explicit
  credential recovery rather than applying stale state.
- Registered credential owners retire an old generation before remote rotation,
  persist the result before publication, and never undo consumption on failure.
  Their final commit shares the Apply gate and rechecks account generation,
  enabled state, selected generation and purposes. Rebasing an unrelated revision
  follows these target checks. Concurrent refreshes share a per-credential gate.
  The `eaf9d6d` follow-up also takes the publication gate around the initial
  snapshot and `ReserveConnect`, preventing an unrelated renewal publication
  from staling that preflight revision. It releases the gate before provider work
  and retains the final target-generation/selection checks.
- `1206cb7` wires bounded automatic renewal and exempts the explicit refresh
  endpoint from the outer mutation lock through `OwnsPublicationGate`; its inner
  publication lock therefore does not reacquire a semaphore already held by the
  request. Recovery adoption remains synchronous under the outer gate.
- Restored owned grants are quarantined before Core startup. Explicit Codex login
  can start an empty runtime under `proxy/login-bootstrap`, with separate config,
  bindings and journal. It shares the qualified owned-auth output and management
  token, while retaining the main active/runtime/application files. Login-only
  management readiness is not inference readiness. A live unready child is not
  replaced for login, and ordinary Start cannot mistake login-only for inference.
- Apply drains before invalidating runtime proof. A confirmed ordinary drain
  failure restores previous operation metadata; an unconfirmed admission resume
  remains recovery-required without killing streams. Failures after publication
  starts stop the new child and retain incomplete proof. Login bootstrap does not
  mark a profile applied; a subsequent Apply uses the normal transaction.
- Monitor avoids Core startup and optional model catalog loading, gates Core
  login/inference export/service actions, and reports passive Groq reads as
  awaiting observation when Core is absent. Optional malformed login registration
  is contained. The reference client port is gated by temporary state plus the
  explicit reference flag.

## Evidence actually inspected

Root's `/tmp/lain-integrated-20261024/followup-results.json` and logs report 12/12
independent checks, 117 foundation assertions, 63 transport assertions, 56 public
login checks, models checks and the restored Monitor process probe passed. The
independent suite and Monitor probe identify Host SHA256
`4d4df715d74ba413f633243be47ab4950aee8908221ed2eb0992ccfc73896bc1`.
The profile application log reports 92 synthetic lifecycle/recovery checks.
These results predate the final renewal wiring and do not qualify a later DLL.

Checked-in `tests/core-stabilization/evidence/adapter3/process.json` records six
passing actual-Core process scenarios against Core SHA256
`39c09202ca7c38c636cfb949abec3b71c6c9843dd2651cbc8bf44944c13c0629`,
source-manifest SHA256
`4441b3bfb5caf5be7c5ab954303c25eac7be2bbd4a3833e4a496cd755d257750`.
They cover SSE drain/reopen, exact tool dispatch limits, native protocol scope,
parent EOF/death and legacy journal recovered-copy replay in a loopback-only
network namespace. Their report expressly excludes full Host backup/restore,
old-binary rollback, installed service and sustained growth qualification.

Root reported an earlier restored Gateway fake-Core probe pass, but its separate
durable output was not located. The independently authored fixture remains
pending exact latest-Host rerun and recorded hash. It checks Host launch and
proof preservation; it does not qualify real Core OAuth.

The targeted A12 source recheck confirms independent cached metadata values and
observations. A failed Pass/Self read retains the previous value and original
observation, success and freshness timestamps; a successful wallet/usage read
keeps its own current timestamp. A successful explicit `subscription:null` clears
old Pass data, while malformed/unknown envelopes retain stale data. Repeated
failures do not refresh metadata timestamps, and account generation or credential
binding changes invalidate both caches. Shared CLI/Desktop facts display the
independent metadata state and last success. The 21 metadata assertions were
read, not executed by this reviewer. Root reports 73 passing reader checks
including those assertions; exact final integrated artifact evidence remains
pending inspection. The previously identified A12 source gap is closed.

## Residual acceptance gates

| Scope | Concrete remaining requirement or limit |
| --- | --- |
| AC-01/10 current integration | Execute the final Host renewal/publication integration and restored Gateway regression against recorded final artifacts. Earlier source/library results do not prove the final middleware wiring. |
| AC-10 / ST-05 delivery | No fresh exact Monitor/Gateway package, isolated fresh install/update and schema-compatible full-state rollback evidence was supplied for this implementation. Synthetic installer packages and older release reports do not close this gate. |
| AC-10 A12 metadata freshness | Prior source gap closed by independent metadata observations and stale-value retention. Retain the reported 73 reader/21 metadata checks and verify shared projections against the final integrated artifact; source review alone does not certify all A12 cases. |
| ST-01 compatibility | Actual Core proves the specifically qualified tool rules. Source-scoped context window facts are not request context enforcement; no tokenizer/context-size check is claimed. Do not advertise all known model facts as enforced dispatch limits. |
| ST-04 recovery/scale | Sparse index and bounded 1,000/10,000-event measurements preserve flat journal format. Complete Host/config/credentials/SQLite restore, actual filesystem exhaustion and sustained growth remain unqualified. Live segmentation/archive replay is explicitly deferred. |
| Product method scope | Unqualified official CLI, managed and console methods remain unavailable/setup-required. They cannot be counted as delivered methods merely because contracts/fakes or catalog entries exist. Live qualification remains separately untested. |

The deferred ST-01/ST-04 items require an explicit scope/limitation in a bounded
release report. They are not grounds to rewrite
passing tests or silently mark the complete work packages accepted. UI/native
acceptance is owned by the separately running acceptance work and is not claimed
by this source review.
