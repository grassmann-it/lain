# AC-07 managed-login qualification

Observed 2026-10-24, implementation baseline `de4ed85`. Public documentation and
source GETs only. No real provider/API request, login, credential/cache read,
CLI/app-server process, installed configuration change or inference was used.
Tests are separate synthetic execution; their result is recorded below after the
coordinated build window.

## Owner decisions

No new refresh owner is selected or enabled. Root accepted this bounded delivery:
qualify useful local parser/contracts and expose precise disabled method gates
until admission, isolation, identity and renewal evidence exists. This is a
provider-specific evidence gap, not a claim that own integrations are impossible.

| Method | Production state | Missing prerequisite | Existing usable path |
| --- | --- | --- | --- |
| `kimi-device` | unavailable | Supported lain toolkit client admission; bound region/subject and single refresh owner, or independently qualified isolated official CLI/local-bearer handoff | Existing Coding key on its exact qualified `.com`/`.ai` base |
| `zai-managed` | unavailable | Supported own client/version; renewable OAuth/business context; personal international project and existing key ID | Existing international Coding key, raw monitor Authorization |
| `codex-standalone` | unavailable | Pinned isolated app-server file backend, verified stable account/workspace identity, restart/renewal and targeted detach proof | Existing Core-owned browser login; explicit expiring external snapshot |

`ManagedLoginQualifications.Methods()` returns these unavailable descriptors.
Root may include them with guidance; it must not register them as available.
Defensive adapters follow `IAccountLoginAdapter` and cannot return `prepared`,
issue secrets, call a provider or change models/routes. Shared
`LoginOperationService` remains the publication authority.

## Kimi contract

The public TypeScript OAuth toolkit exports device request/poll/refresh functions,
and its constants contain an official client ID. Exported code and an official
identity do not establish lain admission; no official client ID is copied.
The installed `kimi` command was located, but no command or normal Kimi state
was read and no version/isolation claim is made. Python `kimi-cli` and TypeScript
`kimi-code` are not assumed interchangeable.

The public local-server specification says every JSON response has a numeric
`code` (0 success), while usage additionally has `data.kind=ok|error`. The parser
requires both success conditions and never surfaces raw error text. Present
`limit5h`, `limit7d`, `monthTotal` and `monthCode` become separate percent windows.
Missing windows remain missing; invalid ratios stay unknown. `extraUsage:null`,
missing and present wallet are distinct. Booster cents/currency and monthly
charge settings remain independent metadata; no synthetic budget/zero/USD value
is produced. No local bearer or server is acquired by this parser.

Sources fetched:

- [Device toolkit](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/device.ts), SHA256 `774482abc2a71f712547266421c8959d5459f5fcd90436bfd63da6f4a1071c01`.
- [Toolkit constants](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/constants.ts), SHA256 `030d2b874ba94b77a9b068ba502b6706addb7b90279122c8f91f5796d6205444`.
- [Managed usage schema](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/managed-usage.ts), SHA256 `130afd97e8c8e9d3b53f8ccaaa7bb1181af1f878a2e5d5c72f53bfab4e46ece9`.
- [Local server reference](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/docs/en/reference/server-api.md). Its API is explicitly experimental; a future pinned owner must check its authenticated live schema, not silently trust main-branch documentation.

## Z.ai contract

Public ZCode source uses `clientType:zcode` and its version when issuing a PAT;
no supported own-client or renewable session evidence has yet been established.
The shared source's general resolver performs GET discovery and, when missing,
a POST key creation. The new boundary contains only a GET request constructor
and explicit existing-ID parser. It has no network client, creation function,
implicit first/default project or fallback key selection.

The `apiKey` list field is a nonsecret identifier. It can never be accepted as
the PAT. PAT parsing separately requires a successful business envelope,
Bearer token type, nonempty access token, positive `expiresIn`/`expiresAt`, and
an effective expiry equal to the earlier of absolute expiry and
request-start-plus-lifetime. `enable:false` produces no projection; omitted
`enable` follows the public source default, while null/invalid values fail.
Scope includes account, subject, family, exact international origin,
organization, project, key ID and login generation. Changing one invalidates
reuse. The parser performs no caching, OAuth renewal, token issuing or storage.
Team key type 2 and CN origins remain separately unqualified.

Sources fetched:

- [PAT schema/cache contract](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/shared/src/project-access-token.ts), SHA256 `c8b2e868b008e275e7b1dc121d78caab24ffd7c6bf39e9ee2fb4eb78331f5e83`.
- [Existing resolver and its create branch](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/shared/src/project-access-token-client.ts).
- [Success envelope codes](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/shared/src/project-access-token-response.ts).
- [Business/PAT adapter](https://raw.githubusercontent.com/zai-org/ZCode/main/apps/zcode-cli/packages/adapters/src/auth/coding-plan-api-key.ts), SHA256 `0b5bcf1d755a4415f3ffee4d1ffec8e842072a5d5d0ccba77dd271aa79505425`.

## Codex contract

Official [app-server documentation](https://developers.openai.com/codex/app-server/)
was fetched through OpenAI Docs. It documents managed browser/device login,
`account/read`, `account/login/cancel` and `account/rateLimits/read` separately
from experimental externally managed tokens. `account/read` returns
`type:chatgpt`, nullable email and plan type. It does not document the stable
ChatGPT account/workspace ID required for adopting a verified grant. Email,
plan, local process ID and random owner ID must not substitute for that binding.
No normal auth cache is inspected to guess the missing identity.

The candidate protocol helper currently allows only `account/read` with
`refreshToken:false` and `account/rateLimits/read`. It refuses login, logout,
turns, credit reset, arbitrary request parameters and external token refresh.
It validates response IDs, rejects duplicate controls and sanitizes RPC errors.
It never launches a process or reads a file.

The quota parser preserves independent bucket/window identities, nullable
windows, percentages, actual durations and Unix-second resets. A present
multi-bucket view replaces the backward-compatible single view; it is never
double-counted. Credit metadata remains raw provider balance/hasCredits/unlimited
without inventing currency, total or finite quota semantics. This is source-level
parser evidence, not installed app-server schema or real renewal qualification.

A future enablement must prove isolated HOME/CODEX_HOME, file-only auth backend
(no shared OS keyring), pinned protocol version, stable verified workspace
identity, new independent grant, restart and one owner-controlled renewal,
late-result/cancel handling and targeted local detach. It must use the shared
credential generation store for an opaque owner reference; no refresh material
may be copied from Core, CLI/extension or external snapshots. Existing Owned and
Snapshot semantics remain untouched. No SIWC-OSS token is repurposed for WHAM.

## Local validation handoff

Fixture JSON and `git diff --check` passed static checks. The synthetic .NET
suite is source-prepared and **awaits Root's integrated build/runtime slot**:
`dotnet run --project tests/managed-connections/ManagedConnections.Tests.csproj`.
No build/test result is claimed by this source commit. Parent owns descriptor
inclusion and will run the suite after integration; candidate adapters must
remain unregistered as available methods.
