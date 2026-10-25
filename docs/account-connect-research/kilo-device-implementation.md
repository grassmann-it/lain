# Own personal Kilo device connection

The `kilo/device` adapter implements the authentication contract exported by the
official, publicly installable MIT `@kilocode/kilo-gateway` package. Availability
means this installed method exists; it does not establish a successful real login,
account entitlement, model availability, credit, or inference result.

## Pinned manufacturer evidence

All source links below pin `Kilo-Org/kilocode` commit
`9723be621429771ea47a2614b0ac433448023cec`:

- [Package README](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/README.md): public package and authentication support.
- [Public exports](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/index.ts): initiation and polling functions exported for clients.
- [Device HTTP contract](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/auth/device.ts): bodyless `POST https://api.kilo.ai/api/device-auth/codes`, then `GET /api/device-auth/codes/{code}`; 202 pending, 403 denied, 410 expired. No client ID or client secret.
- [Response types](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/types.ts): `code`, `verificationUrl`, `expiresIn`; approved response `token`, `userEmail`.
- [Browser instructions](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/auth/device-auth-tui.ts): display the code, open verification URL, and enter code. This contract uses the same code for the browser instruction and poll; it does not define a separate OAuth `device_code` field.
- [Constants](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/constants.ts): 3000 ms polling interval and official API/Gateway endpoints.
- [Profile](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/profile.ts): personal profile, `user.email ?? email`, optional `hasPersonalAccount`; organization access uses a separate optional organization header.
- [Personal key setup](https://kilo.ai/docs/getting-started/setup-authentication): Gateway clients use the personal profile key.

An independently checked nine-file SHA-256 source manifest is retained in the
qualification evidence cache at
`~/.cache/lain-qualification-20261025/next/kilo-device-source-9723be6/`.
The package's TUI locally duplicates the token into a refresh field and sets a
synthetic one-year expiry. Those implementation conveniences are not evidence of
a refresh grant or guaranteed token lifetime; lain implements neither.

## Local ownership and publication

Production has fixed HTTPS endpoints, no redirect/cookie/environment-proxy
following, bounded response size/depth/time and 3-second minimum polling. Codes
are accepted only as bounded ASCII alphanumeric/hyphen values; browser URLs must
use HTTPS on `kilo.ai` or `app.kilo.ai`, without userinfo or fragments. An uncertain
approval result terminates polling; it does not automatically issue another code.
Local Cancel never claims to revoke a remote token.

The issued token is checkpointed before profile metadata is read. That checkpoint
has **no purposes**, so local recovery cannot adopt an unconfirmed personal token.
Only a matching approved email and confirmed personal profile, with any requested
existing binding matched, produce an adoptable `static-key` credential for exactly
the requested inference/models/quota purposes. The adapter publishes no accounts
itself. `LoginOperationService` owns immutable generations, durable recovery,
revision/generation/removal fences and publication. No refresh owner is needed.

Personal binding is `provider=kilo`, `product=gateway`,
`resourceType=personal`, subject the normalized confirmed email, with no region or
organization ID. Explicit `hasPersonalAccount=false` fails the personal gate;
missing/null remains compatible with the manufacturer's optional legacy field.
The adapter never chooses an organization, default model, purchase or billing cap.

Reconnect reads only the selected account's current inference-generation lease
before the first authorization POST. A Kilo-owned handoff preserves qualified
connection/route/transport IDs, billing choice and safe imported model mappings.
It removes old root/variant/YAML keys, then inserts the newly issued token in all
transport descriptors. Foreign endpoints, custom/organization headers and unknown
private additions require local review before authorization begins. This avoids
publishing a new key that silently breaks existing selected routes or forwards it
to an unqualified transport. Other purpose generations remain centrally managed.

Quota uses the existing `KiloQuotaAdapter` and current purpose-bound resolver,
including fixed personal wallet/Pass reads. A true zero balance remains zero.
No quota read occurs as part of device authorization itself.

## Integration

Shared device display DTOs require `UserCode` and `VerificationUri`; terminal login
states clear both. `LoginOperationService` must accept provider `expired`.

```csharp
await using var kiloLogin = new Lain.Host.Accounts.Kilo.KiloDeviceLoginAdapter(
    configuration: repository, secrets: secrets);
// Add kiloLogin to accountLogins.
// ConnectionMethodCatalog.Snapshot:
result.Add(Lain.Host.Accounts.Kilo.KiloDeviceLoginAdapter.ConnectionMethod());
```

Use the actual existing repository/secret variable names. Do not register a
credential refresh owner. Reconnect that replaces inference requires the local
handoff dependencies; missing wiring refuses before issuing a remote code.

## Offline verification

`tests/kilo-device-login/Kilo.DeviceLogin.Tests.csproj` is an executable fixture
project with a fake HTTP handler and isolated temporary own state. It covers the
manufacturer wire contract, code/URL validation, cadence/backoff, terminal states,
idempotence/context fences, checkpoint ordering, cancellation during approval,
profile fallbacks and identity gates, central recovery adoption rejection, actual
immutable-generation reconnect and `ProxyConfigurationWriter.Write`, descriptor
key replacement, real SQLite History preservation, and the current-generation
handoff into the real quota adapter with a zero balance. No real provider
account/login API, inference, purchase, cap change or external credential store
is used by these tests.

Run the fixture only in the assigned serial build slot:

```sh
dotnet restore tests/kilo-device-login/Kilo.DeviceLogin.Tests.csproj --locked-mode
dotnet run --no-restore --project tests/kilo-device-login/Kilo.DeviceLogin.Tests.csproj
```
