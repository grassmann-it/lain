# Host cached router availability

Offline executable tests; all HTTP uses in-process fixture handlers, and private
state is created only in a disposable synthetic directory. No live provider,
installed Host/Core, user configuration or service is accessed.

The separate Host loop publishes a complete replacement every 15 seconds through
the existing authenticated loopback client. Frozen contract:

```json
GET /lain/router-availability
{"schemaVersion":1,"generation":"<Core process UUID>","sequence":0}

POST /lain/router-availability
{"schemaVersion":1,"generation":"<same UUID>","sequence":1,
 "observedAt":"<UTC>","expiresAt":"<UTC, at most observedAt + 5 minutes>",
 "entries":[{"routerId":"...","profileId":"...","routeId":"...",
 "memberFingerprint":"...","accountId":"...","provider":"...","model":"...",
 "blockedUntil":"<UTC>","reason":"quota_exhausted"}]}
```

POST sequence is exactly GET sequence + 1. A successful POST returns 204; 409
invalidates the entire batch, which is rebuilt from a new GET on the next tick.
The other contract responses are 400 (invalid) and 413 (over 1 MiB). Maximum 4096
entries. Empty entries clear all previous Host observations. No HTTP response
body, credential or quota reading is logged.

Initial supported evidence is deliberately narrow: legacy API-key accounts with
one proven inference transport and the same quota key on official Kimi Code or
Z.ai coding endpoints. The proof is captured from private JSON already loaded by
the existing quota credential read, contains no key, and travels with its cached
quota observation, account generation and credential binding version. A sole
selected transport variant can qualify; multiple variants, separate inference
and quota handles, or optional separate monitoring credentials remain unknown.
There is no additional credential resolution for this proof.
The proof store retains at most 4096 account IDs; existing accounts can replace
their proofs at capacity, and new accounts remain unknown until capacity frees.

Kimi's verified percent subscription windows and Z.ai's five-hour/weekly
TOKENS_LIMIT/CREDIT_LIMIT windows can suppress only their bound router members.
PAYG endpoints, account balances, MCP TIME_LIMIT, rates, other providers, generic
or unmatched scopes/models, zero allocations, missing resets, stale/error/future
observations and missing proof remain unknown. This is not generic account-wide
quota inference. Kimi device monitoring is not joined to unrelated inference.

Expiry is bounded by original observation/success + 5 minutes, explicit
freshness, and the actual reset/window end. Republishing does not renew it.
Pending application sends an empty replacement; stable applied revision,
account generations and generated binding bytes are rechecked before POST.
Failures stay within the availability loop and existing quota refresh/usage
lifetimes remain independent. Monitor mode and a stopped/not-ready Core skip
publication without HTTP, binding reads or snapshot reads; the loop remains
alive for later gateway readiness.

Latest run:106 offline Host checks passed. The existing Fireworks connector
emits two Windows portability warnings; these are not a native Windows pass.

Run from the repository root using the shared bounded verification lane:

```sh
flock /home/tomka/.cache/lain-verification-lane.lock systemd-run --user --scope --quiet \
  -p MemoryHigh=1536M -p MemoryMax=2G -p MemorySwapMax=256M -p CPUQuota=150% \
  nice -n10 dotnet run --project tests/router-availability-host/RouterAvailability.Host.Tests.csproj -r linux-x64 \
  -p:UseSharedCompilation=false -m:1
```
