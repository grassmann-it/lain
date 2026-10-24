# Account Connect key readers

Run `./tests/account-connect-readers/run.sh` in a coordinator-approved .NET build
slot. All HTTP uses fake handlers and synthetic keys. No service, installation,
provider request or inference is performed. The project is generated under TMPDIR
and removed after the run; shared project files remain untouched.

Qualified offline semantics:

- Kilo direct USD wallet zero, separate Pass credits/status/dates, independent
  wallet/Pass failure, no Pass-to-wallet arithmetic or inferred tier.
- Requesty Self permissions and group metadata; zero monthly limit is uncapped;
  monthly spend and explicitly bounded period usage are separate observations.
- Synthetic current weekly percentage preferred over legacy weekly credits;
  reported rolling constraint stays separate; decimal strings are exact and
  regeneration never becomes a full reset. Legacy subscription remains fallback.
- Step prepaid/postpaid balance preserves zero; cumulative recharge fields are
  never limits; declared organization scope and unqualified negatives are unknown.
- xAI fixed-host key metadata discovers the actual returned team, preserves
  nullable block/disabled states and rejects a mismatched existing team binding.
- Fake 401/403/302, timeout, oversized body, transport error, wrong host and
  response drift cannot expose credentials in result diagnostics.

Primary schemas read during implementation on 2026-10-24:

- Kilo-Org/kilocode@9723be621429771ea47a2614b0ac433448023cec,
  packages/kilo-gateway/src/api/{profile,kilo-pass,constants}.ts.
- requestyai/cli@21809398e99d888565178d718186b637f3de4566,
  internal/client/{api_keys,usage}.go.
- synthetic-lab/octofriend@87d7fa9ced654e61cc2b22b61e5211c958376165,
  source/utils/quota.ts.
- https://docs.x.ai/developers/rest-api-reference/inference/other.
- Step existing documented /v1/accounts reader preserved; research in
  docs/account-connect-research/xai-stepfun-poe-synthetic.md.

Host integration must use its no-redirect HttpClient, enforce current credential
revision/generation and explicit product/resource binding, and retain stale values
only for that same binding. These lifecycle checks belong to Foundation/QuotaService,
not to stateless readers. Provider-specific qualifiers are Kilo Product=gateway /
ResourceType=personal and Step Product=payg / ResourceType=personal / Region=com.
Null legacy bindings permit the existing fixed endpoint reads without confirming
identity or inventing a ScopeId. Explicit incompatible bindings reject before HTTP.
Requesty key/group and xAI team bindings are checked against returned metadata.
Live qualification remains a separately authorized step.

Verified locally on 2026-10-24 against shared contracts 643f626:

| Suite | Assertions |
| --- | ---: |
| account-connect-readers | 52 |
| providers-synthetic-xai | 41 |
| providers-nvidia-step | 40 |
| quota | 98 |
| providers-cohere-kilo | 43 |

All five final runs passed; successful builds had zero warnings/errors. Builds
were serial, node reuse disabled, and runtime identifier linux-x64. Existing
Synthetic/Step/quota scripts received `RuntimeIdentifier=linux-x64` through the
environment. The Cohere/Kilo test project's baseline lock omits its RID; a
locked build of that project reported NU1004. The exact existing Program.cs was
therefore compiled in a disposable linux-x64 project referencing the real Host;
its locked restore/build and 43 checks passed. No repository lock files changed.
Disabling lockfile generation while existing locks are present was rejected by
NuGet (NU1005) and was not used for successful verification.

Independent review identified a Requesty identity bypass after Self HTTP/schema
failure on a resource-bound account. Commit 1ac7bbc requires matching Self metadata
before reading usage for that binding. Six fake key/group HTTP/malformed/unknown
schema cases now prove no usage request or observation escapes this check; one
unbound legacy case proves independent partial usage remains available. The narrow
52-case suite passed after this fix with zero warnings/errors under the same
linux-x64/no-node-reuse setup. Other provider suites above are unchanged by this
Requesty-only fix and were not repeated.
