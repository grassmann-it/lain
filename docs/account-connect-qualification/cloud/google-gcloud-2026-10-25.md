# Official Google Cloud CLI browser monitoring

This path executes Google's installed CLI; it does not implement OAuth using
another application's client ID. No live login, user project request, shared
gcloud configuration or ADC read was performed while implementing it.

## Host integration contract

Construct one adapter and register it as both `IAccountLoginAdapter` and
`ICredentialOwner`:

```csharp
await using var googleCli = new GoogleGcloudLoginAdapter(
    Path.Combine(paths.Root, "accounts", "google-gcloud"),
    accountId => {
        var account = repository.Snapshot.Accounts.SingleOrDefault(a => a.Id == accountId);
        var selectedId = account?.Capabilities?.SingleOrDefault(c => c.Purpose == "quota")?.CredentialId;
        var handle = account?.Credentials?.SingleOrDefault(h => h.Id == selectedId && h.Owner == "google-gcloud");
        return handle is null ? null : secrets.Generations.Read(accountId, handle).PrivateJson;
    });
var googleCliRetention = new GoogleGcloudProfileRetention(repository, secrets, paths, googleCli);
```

The selected-credential callback is required so reconnect pins the previously
verified Google user subject as well as the immutable project number. The shared
account binding continues to use the **project number** as its Subject. The human
Google subject and account email stay private inside the owner credential.

- Provider `gemini-api`, method `official-cli`, owner `google-gcloud`, purpose
  `quota`, no Core required. Enable only when `googleCli.IsAvailable`.
- Private kind `gcloud-owned`: local account ID, owned profile reference, verified
  login account/subject, access token, real expiry, projectId/projectNumber and
  quotaProjectId. No CLI refresh token or ID token is exported into this object.
- `MonitorCredentialStore` must project `gcloud-owned` identically to
  `cloud-oauth` as access-token-snapshot. `MonitorAccessService` should identify
  `google-gcloud` as managed Google Cloud CLI access. Register the same owner in
  `CredentialOwnerService` and `OwnedCredentialRefreshService`.
- After successful replacement/removal, under the foundation publication gate,
  invoke `googleCliRetention.DisconnectAsync(oldAccount, ct)`. Never invoke cleanup
  as rollback before a successful commit. The helper retains all currently
  capability-selected profiles and non-published prepared recovery profiles.
  Remote-pending, malformed, missing, restore-quarantined, ambiguous or oversized
  metadata defers deletion. Recovery scanning is bounded to 1,024 metadata files;
  credential-generation trees are not searched. Busy/active profiles remain.
- The existing Desktop/browser action consumes the returned URL. No protected
  authorization-code input endpoint is needed: the official CLI owns its native
  loopback callback and token exchange.

## Scope wording for the catalog/UI

**Google Cloud CLI signs in with its standard Google Cloud permissions: Cloud
Platform, Compute, App Engine and Cloud SQL, plus account identity and reauthentication.
Lain uses this connection to read account, project and Monitoring data.**

This is not a read-only OAuth grant. Installed SDK 584.0.0
`surface/auth/login.py:GetScopes` selects `CLOUDSDK_SCOPES + REAUTH_SCOPE`; its
login argument parser has no `--scopes` option. The exact seven scope strings are
validated in browser actions. A smaller initial grant would require a different,
separately qualified official flow. Neither Owner/Editor IAM nor new project/API/
billing creation is performed by lain. Project IAM and API availability remain
independent of consent. X-Goog-User-Project is pinned to the selected project;
Google may require `serviceusage.services.use` for that quota-project header.

## Isolation and lifecycle

The currently installed Linux x64 SDK at `/opt/google-cloud-cli`, version
584.0.0, is admitted only when its reviewed command/flow/helper/browser/config/
metrics source hashes match. Changed or missing installations become unavailable
before a login process starts. Other versions/platforms are not silently admitted.
The SDK client ID is read only from its hash-checked installed configuration for
**comparison** with browser actions; it is neither bundled nor used by lain to
construct a provider authorization request.

Every connection gets a fresh private profile. The process receives a replacement
environment with its own CLOUDSDK_CONFIG; inherited tokens, ADC paths, custom
Python startup paths, proxies and alternate gcloud configuration variables are
excluded. The default user's gcloud directory is never opened. No --update-adc,
application-default login, resource creation, API enablement or inference occurs.

The CLI's BROWSER command is a private executable that emits only a randomly
marked browser action through its inherited stdout. The host incrementally drains
both pipes, bounds output and validates the exact Google authorization host/path,
installed SDK client ID, scope set, code response, state, S256 PKCE and localhost
callback. The SDK's native server remains responsible for callback/state/code
handling. A DISPLAY marker selects that SDK path; the bridge never contacts the
user's display server. Fallback to a copy-code or remote-bootstrap flow is not
accepted. CLI logs/usage reporting are disabled. An explicit metrics environment
also prevents the SDK's otherwise independent GCE environment probe.

Login/renewal results come from the official `config config-helper --format=json
--force-auth-refresh` command, whose installed source expressly supports external
tools and reports `credential.token_expiry`. No guessed one-hour expiry is used.
Userinfo verifies the provider subject and verified email against the CLI account;
reconnect and renewal reject identity changes. Private access is checkpointed
before discovery. New connections offer verified projects; reconnect reads the
exact pinned project. Existing inference keys, local IDs and history remain intact.

Cancellation/expiry kills the owned process tree and suppresses later publication.
A private PID/start-time marker supports cleanup after host restart only when the
process also carries the exact owned CLOUDSDK_CONFIG path. Per-profile locks
serialize processes and protect cleanup. CLI refresh grants remain in the profile
through partial/recovery results; post-publication retirement deletes only profiles
no selected or recovery generation still needs.

## Verification status

The existing Google/Cloudflare runner had 77 passing assertions before this
addition. Added `GoogleGcloudChecks` covers real installed SDK URL-builder/browser
bridge behavior entirely offline, streamed URL delivery, environment isolation,
bounded cancellation, path-link rejection, actual expiry, wrong client/scopes,
replay, project picker, private checkpoint, user-subject pinning, renewal and
selected/recovery/profile-retirement behavior.

Initial capped builds succeeded. Early fixture runs revealed (and fixed) a test
exception filter and the SDK's unexpected GCE metrics-environment probe. That probe
stalled in DNS and did not authenticate or read user/provider account state. The
fixture now rejects all non-loopback DNS/connections and production explicitly
sets the SDK metrics environment. The final revised fixture run is pending the
integration owner's serialized slot; this file does not claim it passed yet.
