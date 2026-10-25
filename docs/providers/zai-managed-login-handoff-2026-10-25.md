# Z.ai managed login: source qualification and concrete handoff

Reviewed 2026-10-25 against lain `c9f8840` and the public manufacturer repository
[`zai-org/ZCode@3c5b227649371507ddc4eb46ac5133afe6db1cab`](https://github.com/zai-org/ZCode/tree/3c5b227649371507ddc4eb46ac5133afe6db1cab).
Only public source was fetched. No ZCode command, provider login, authenticated
request, real credential store, key creation or account change was performed.

## Result

The reviewed source establishes a working **ZCode** login, but does not establish
a supported, non-creating **lain** login path. Keep `zai-managed` unavailable and
retain the existing international Coding-key connection. No production adapter,
speculative OAuth refresh endpoint or abstract login/refresh delegate suite is
added by this handoff.

This is a specific missing integration contract, not evidence that Z.ai forbids
third-party clients or that external registration is necessarily required. A
published shared public-client contract could resolve the identity question.
An own-client registration, if offered, would need a documented exchange and
PAT contract as well; a configurable client-ID string alone resolves neither.

Automatic renewal of the **primary OAuth session is not a necessary condition
for offering login**. The official behavior supports a finite session followed
by reconnect. The earlier qualification's requirement for a renewable OAuth /
business session must not become an additional, unsupported product gate.
Independent identity and non-creating operation are the concrete unresolved
requirements here.

## Public library and CLI paths examined

| Path | What the manufacturer exposes | Concrete gap for this task |
| --- | --- | --- |
| Desktop OAuth | [Provider configuration](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/services/src/oauth/providers/zaiProviderConfig.ts#L15) uses ZCode's app ID and callback, authorizes at `chat.z.ai/api/oauth/authorize`, exchanges through `zcode.z.ai/api/v1/oauth/token`. | No published common-client agreement or independently usable lain app/exchange is established by this configuration. Do not copy the official app identity or claim that changing its label grants admission. |
| OAuth configuration overrides | [`resolveZaiOAuthClientId`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/src/zcodeEndpoint.ts#L313) accepts environment overrides; the [provider comment](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/services/src/oauth/providers/zaiProviderConfig.ts#L43) describes separating test/production OAuth apps. | An override is not a public client-registration or shared-client contract. It does not describe a third-party exchange, approved callback or PAT client identity. |
| Exported CLI OAuth helper | [`createCliOAuthClient`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/apps/zcode-cli/packages/adapters/src/auth/cli-oauth.ts#L9) supports `providerId`, `baseUrl`, `httpClient`, `trace`; init/poll uses a random local poll bearer. | No caller/product identity parameter or documented external host contract comparable to an explicitly supported public-client toolkit was found. The default remote path remains ZCode's CLI service. Exported source alone does not establish lain admission. |
| Exported project-token helper | [`ProjectAccessTokenInput`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/src/project-access-token-client.ts#L39) accepts login/account/family and selection policy; token issuance [sends](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/src/project-access-token-client.ts#L207) `clientType: "zcode"`, `clientVersion: ZCODE_VERSION`. | No own caller client type/version or explicit personal existing-key ID input. Importing the helper does not make its hardcoded ZCode identity lain's identity. |
| Actual CLI | [`zcode login [zai\|bigmodel] [--no-browser]`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/apps/zcode-cli/packages/cli/src/login-command.ts#L10) delegates to the [bootstrap login](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/apps/zcode-cli/packages/bootstrap/src/auth-login.ts#L101). | Before credentials are persisted or success returns, bootstrap resolves project-token material. Its resolver can create a dedicated key; the exposed CLI arguments cannot prohibit that branch or select the required existing personal key ID. |

The auth helpers are exported by `@zcode/adapters/auth`; both
[`@zcode/adapters`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/apps/zcode-cli/packages/adapters/package.json)
and [`@zcode/shared`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/package.json)
are marked `private: true`. That packaging detail is supporting context, **not**
a prohibition or proof of server admission rules.
[`@zcode/client`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/client/src/index.ts)
exports remote-service/RPC connections, not an independent OAuth login toolkit.

### Why delegating to the genuine CLI is insufficient here

Delegating to an unmodified official CLI would preserve its truthful ZCode
identity. Its side effects still fail this task's non-creating requirement:

1. [`#resolveLocation`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/src/project-access-token-client.ts#L249)
   reads customer organizations/projects and selects a default/first project.
   The standalone path keeps `personalProjectSelection: "default"`; unlike the
   desktop's `non-team` selection it does not filter out `projectType == 2`.
2. The [dedicated-name lookup](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/src/project-access-token-client.ts#L321)
   searches for `zcode-api-key`. If absent, `candidate ?? ...` performs
   **POST `.../api_keys`** with `usageScene: 1`. An unrelated existing key does
   not prevent creation. The bootstrap cannot return a successful OAuth-only
   login before this resolver completes.
3. `--no-browser` only changes browser opening. It does not suppress project
   selection, key creation or token issuance. Private HOME isolation can protect
   local stores but cannot prevent a remote key-creation request.

Do not seed an undocumented key-location cache, intercept undocumented traffic,
or patch the CLI to pretend it has an official existing-ID/no-create contract.
A revised manufacturer CLI with an explicit non-creating bound-key path could
be qualified independently; this reviewed version does not provide that path.

## Three distinct credential lifetimes

The [manufacturer's credential audit](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/docs/analysis/credentials-json-code-audit.md#L316)
and current implementation distinguish these layers:

- **Primary OAuth session:** `ZaiProviderAdapter` does not implement
  `refreshToken`. [`OAuthService.refreshToken`](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/services/src/oauth/oauthService.ts#L999)
  explicitly rejects adapters without that method. Optional generic refresh
  fields are not evidence that Z.ai grants or consumes a refresh token.
  Preserve reconnect semantics when the primary grant stops working; do not
  invent an OAuth refresh endpoint or promise indefinite login.
- **Business JWT:** the standalone adapter exchanges the still-valid original
  OAuth token at `/api/auth/z/login`. The [business-token cache](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/apps/zcode-cli/packages/adapters/src/auth/zai-business-token-cache.ts)
  bounds reuse by expiry and exchanges again while that source grant works.
  This is not primary OAuth renewal. Desktop login already persists the
  exchanged business JWT; the two stored values are not interchangeable.
- **Project Access Token (PAT):** the [PAT cache](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/packages/shared/src/project-access-token.ts)
  issues a short-lived token for an existing key ID. The effective deadline is
  the earlier of absolute expiry and request-start plus lifetime. Reissuing a
  PAT cannot extend or resurrect the primary grant.

The [manufacturer PAT contract](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/docs/model-provider-project-access-token.md#L14)
also says PAT use remains subject to the official version's request-security
admission rules. It does not establish that an arbitrary clientType/version is
accepted. Its [U01 evidence](https://github.com/zai-org/ZCode/blob/3c5b227649371507ddc4eb46ac5133afe6db1cab/docs/model-provider-project-access-token.md#L131)
describes PAT-backed monitor quota, but labels the check as service regression,
not a real-backend test. Do not promote that to successful live qualification.

## Exact implementation handoff when a usable path is published

1. Establish either a public-client SDK contract that expressly supports an
   independent caller, or an own-client OAuth exchange plus PAT clientType /
   actual version, or a genuine official CLI mode with non-creating semantics.
   Do not require primary OAuth refresh if reconnect is the official behavior.
2. Bind verified subject and login generation to the international
   `https://api.z.ai`, personal account, explicitly selected organization,
   project and existing nonsecret key ID. Verify the project belongs to the
   returned customer and is personal; reject team/CN inheritance and ambiguous
   defaults. A context string by itself is not proof of personal membership.
3. Discover by GET only. Customer information uses the raw business JWT;
   international personal key discovery and PAT issuance use Bearer business
   JWT. A missing key ID, 404, disabled PAT or permission failure must never
   invoke POST `.../api_keys`, copy-secret or select a different key/project.
4. Use `ZaiKeyDiscovery` / `ZaiProjectTokenProjection` as the existing parsing
   boundaries, not as evidence of a complete owner. Their scope object validates
   shape and binding; it does not verify account membership or acquire a grant.
5. Route login publication through `LoginOperationService` and the shared
   generation store. Keep one owner for bounded business/PAT reissuance;
   primary-grant expiry becomes reconnect. Cancel/removal/generation changes
   invalidate late results. No foreign auth-cache adoption or parallel store.
6. Add credential-kind-specific quota handling only after the access projection
   is qualified. The existing `ZaiQuotaAdapter` accepts `api-key`; do not disguise
   an expiring PAT as a static API key. The monitor endpoint uses the raw PAT,
   without an added Bearer prefix. Retain independent stale/unknown quota facts.
7. Before availability changes, verify synthetic wrong-scope/missing-ID/team/CN,
   expiry/reconnect, canceled/removed target and no-create request sequences.
   Pin an actual CLI if delegated. Real login/renewal checks require their own
   authorized execution; none are claimed here.

Root owns the shared catalog/qualification descriptors and registration. A
precise current gate is: **"No supported independent client contract or bound,
non-creating official CLI path has been established from the pinned source;
use an existing international Coding key."** A renewable primary OAuth session
must not be listed as independently mandatory.

## Reproducible source evidence

SHA-256 of decisive fetched files at the commit above; hashes describe public
source, not credentials or runtime data:

| File | SHA-256 |
| --- | --- |
| `packages/services/src/oauth/providers/zaiProviderConfig.ts` | `d0f490f62c64da334cec0e1b890f241d496ede8420597b774f819fb7483e8cda` |
| `packages/services/src/oauth/providers/zaiProviderAdapter.ts` | `e8b994c6f3e39b4fcb41af06505e948b8e7a41dff6e9a5fc007c2af3ecd22629` |
| `packages/services/src/oauth/oauthService.ts` | `4dc7ee0c222836b83d5f25a483d04eee7dbba02a5ebde6bcebe31fbc7e0ec87b` |
| `packages/shared/src/project-access-token-client.ts` | `a4dad9ecb2902ccdf8ff6d4f821b101e3a860b0e2472ae4a0e69b700eb4f5850` |
| `apps/zcode-cli/packages/adapters/src/auth/cli-oauth.ts` | `c3f09aabf53bc24dcf6923f9e7318676bd3ca39a53632f294394bfb44700cb08` |
| `apps/zcode-cli/packages/bootstrap/src/auth-login.ts` | `060f5e992f9d3d77dcf81fef1bb6938bb82c78fb9640b67214069fe2a3181b02` |
| `docs/model-provider-project-access-token.md` | `6fdee85684d75c30f197c7ae0378121c6b92c41503b04b62a9bc1da1e4607c16` |
| `docs/analysis/credentials-json-code-audit.md` | `62e5da4bd132470db10750581504da73ee1220aa8f9ea735a40b24557fd98247` |

No product code changed, so no build or runtime test is claimed. Validation is
the pinned public-source review, local hash comparison and `git diff --check`.
