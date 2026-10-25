# Google, Cloudflare and Poe maintainer registration

Prepared against `c9f8840`, 25 October 2026. This closes the offline setup work;
it does not claim a registered client, real login, callback acceptance or refresh
qualification. No provider account/client, DNS record, consent configuration,
local installation or availability flag was changed. Existing key/snapshot access
remains independent. Host/CLI integration and release activation belong to Root.

## Supported registration paths

| Provider | Supported maintainer path | External work still required |
| --- | --- | --- |
| Google | Own Google Auth Platform **Desktop app** client; system browser, S256 and IPv4 loopback. | Maintainer project/client and branding/consent, production/scope review, actual callback/exchange/refresh and existing-project IAM qualification. |
| Cloudflare | Account-owned OAuth client through its dashboard or authenticated `POST /client/v4/accounts/{account_id}/oauth_clients`; public clients use token authentication `none` and S256. | Own account with OAuth Client Write, current scope selection, registration acceptance, publisher domain verification/public release and real callback/account/refresh qualification. |
| Poe | Maintainer creates the application at `https://poe.com/api/clients`; S256 authorization code produces a user API key. | Own client ID and qualification of the actual fixed release callback and key persistence/reconnect behavior. |

Sources reread on 25 October 2026:
[Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app),
[Cloudflare registration](https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/),
[Cloudflare create API](https://developers.cloudflare.com/api/resources/iam/subresources/oauth_clients/methods/create/),
[Poe OAuth](https://creator.poe.com/docs/external-applications/sign-in-with-poe).

No anonymous, general-purpose dynamic registration endpoint for the Google
Desktop or Poe flow is documented in these sources. Do not substitute Google
IAP/Workload Identity client APIs or Cloudflare Access/MCP dynamic registration:
they serve different authorization systems. Cloudflare's account API is usable
automation after a separate authorization/review; it requires a maintainer's
administrative credential and is not anonymous public-client registration.
No foreign application's ID, Wrangler ID or end-user client-ID prompt is needed.

Google documents client-secret omission for its public Desktop token exchange;
the existing adapter sends no secret. Its current `cloud-platform` grant is broad,
not a read-only permission. An external Testing application may have seven-day
refresh expiry; production and scope approval remain release gates. No project,
API activation, billing link, key or service account is automatically provisioned.
See [Google token expiry](https://developers.google.com/identity/protocols/oauth2#expiration).

Cloudflare defaults a new client to private account-member use. Public release
requires client name/logo/URL/scopes and domain TXT verification; promotion to
public is permanent. This tool does not prepare an automatic promotion operation.
Scope IDs must come from the
[actual OAuth scope catalog](https://developers.cloudflare.com/api/resources/iam/subresources/oauth_scopes/methods/list/),
not API-token permission labels. The create API manages `offline_access` from
`refresh_token` registration; the Host still requests `offline_access` at login.
These facts do not prove Workers AI Analytics entitlement or endpoint scope mapping.

Poe's documentation exempts `localhost` redirects for **local development**.
It does not establish that an arbitrary release callback, `127.0.0.1`, IPv4
resolution or a fixed port is already qualified. Register/review the exact release
URI. The current Host supports HTTP `localhost`/`127.0.0.1`, explicit ports
1024–65535 and a non-root callback path. It uses `apikey:create`, not a refresh grant.

## Offline setup tool

`scripts/prepare-oauth-maintainer.py` uses only local files. It accepts no provider
token/secret, performs no HTTP or browser operation, never installs a file and
cannot set qualification true. All commands validate a versioned setup spec:

```json
{
  "schemaVersion": 1,
  "google": { "clientId": null },
  "poe": {
    "clientId": null,
    "callbackUri": "http://127.0.0.1:18419/lain/poe/callback"
  }
}
```

Null IDs are allowed for a registration plan, so the maintainer can review the
external steps before creating anything. Staging requires the provider-issued
IDs after that separate work. The tool checks ID shape, not ownership: the
maintainer must establish ownership in the provider console/evidence.

```sh
python3 scripts/prepare-oauth-maintainer.py plan --spec /private/setup.json
python3 scripts/prepare-oauth-maintainer.py validate --spec /private/setup.json
```

To add Cloudflare, the spec's optional `cloudflare` object has these exact fields:

| Field | Value |
| --- | --- |
| `clientId` | Null before registration; own provider-issued ID before staging. |
| `accountId` | Own 32-hex maintainer account ID. |
| `clientName`, `clientUri`, `logoUri` | Release name and owned public HTTPS metadata URLs. |
| `callbackUri` | Exact loopback URL accepted by the current Host contract. |
| `scopes` | Unique actual resource scope IDs plus `offline_access`; 2–32 entries. |
| `scopeCatalogSha256` | Full lowercase SHA256 of the complete local catalog snapshot. |
| `policyUri`, `tosUri` | Optional public HTTPS metadata URLs. |

An authenticated maintainer can separately capture every page of the official
scope catalog. Do not save the request Authorization header with it. Wrap the
unaltered response pages in this local v1 envelope:

```json
{
  "schemaVersion": 1,
  "sourceUrl": "https://api.cloudflare.com/client/v4/oauth/scopes",
  "capturedAt": "2026-10-25T20:00:00Z",
  "pages": ["REPLACE_WITH_COMPLETE_OFFICIAL_RESPONSE_OBJECTS"]
}
```

The placeholder above is deliberately invalid. Each page must have `success:
true`, no errors, its `result` array and complete `result_info` containing `page`,
`per_page`, `count`, `total_count`. Pages start at 1, share totals and size, have
unique IDs and cover the whole declared catalog. This is completeness/consistency
checking, not proof that a local snapshot is authentic or fresh. Review its source
and capture date before a real registration. No snapshot ships as a qualified
Cloudflare scope selection.

```sh
python3 scripts/prepare-oauth-maintainer.py plan --spec /private/setup.json \
  --cloudflare-scope-catalog /private/cloudflare-scopes.json
```

The plan contains a reviewable Cloudflare POST URL/body with `none`,
`authorization_code`/`refresh_token`, `code`, exact callback and selected resource
IDs. It excludes the automatically managed protocol scope from the create body.
It contains no authorization header and is never sent. Provider registration and
DNS/public promotion still need Root review; the tool has no apply/network mode.

## Staging and release handoff

After the own IDs exist, stage into a **new private directory** under an owned
parent protected from other writers:

```sh
python3 scripts/prepare-oauth-maintainer.py stage --spec /private/setup.json \
  --cloudflare-scope-catalog /private/cloudflare-scopes.json \
  --output /private/new-oauth-stage
```

Omit `--cloudflare-scope-catalog` when the spec has no Cloudflare entry. Input
JSON is bounded, regular and opened with no-follow/nonblocking flags; duplicate
properties, unknown fields, secret/token fields and qualification switches are
rejected. Symlink inputs, FIFOs and oversized files fail. Existing output paths,
symlink staging paths and unprotected output parents fail; no file is replaced.
Stage directories are 0700 and files 0600. An interrupted stage is evidence to
inspect, not something installed or silently resumed.

The optional staged Host files preserve the existing loader contracts:

- `maintainer/cloud-oauth.json`: Google/Cloudflare clients, every `qualified:false`
  and `cloudflareNeuronCandidateQualified:false`.
- `maintainer/poe-login.json`: direct `clientId`/`callbackUri` fields with
  `registrationQualified:false`.
- `registration-plan.json` and `setup-receipt.json`: separate v1 preparation
  records with input/catalog/tool and output hashes. A preparation receipt is
  neither a signature nor a login qualification result.

The Host formats deliberately gain no version field: Cloud's strict existing
deserializer would reject it. Versioning belongs to the setup spec/receipt.
No staged file replaces an existing provider registration or changes availability.
Root must review/merge any later installation and separately qualify the checklists
in the plan before its release configuration enables a method. A candidate neuron
reader needs its independent data-source qualification even after browser login.
Google/Poe console steps, actual Cloudflare registration, DNS, consent, callbacks,
refresh/key issuance and platform behavior cannot be completed by offline fixtures.

## Local verification

`PYTHONDONTWRITEBYTECODE=1 python3 tests/oauth-maintainer/test_setup.py -v`:
**17 serial tests pass** (0.446 seconds measured). They use temporary synthetic
files and one Python CLI subprocess; scope names/IDs in fixtures qualify no
provider. Checks cover public-client plan parameters, all false release gates,
Host output fields, callback restrictions, secret rejection, scope snapshot pins
and pagination, symlink/FIFO/size/duplicate JSON refusal, private staging and
refusal to replace an existing directory. `git diff --check` passes.
No .NET build, Host/runtime, browser, provider registration/login or real token
request was run. The existing Host loaders were inspected; integrated .NET
loading and real provider qualification remain separate Root checks.
