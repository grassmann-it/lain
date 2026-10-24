# AC-07 managed connection boundaries

Run only in the coordinated build slot:

```sh
dotnet run --project tests/managed-connections/ManagedConnections.Tests.csproj
```

This suite uses in-memory JSON and disposable lain state. It launches no CLI,
app-server, proxy, browser or login; it performs no HTTP requests and reads no
real credential cache or installed configuration. Public documentation/source
reads used to prepare the parsers are recorded separately.

The three new candidate methods are **unavailable**. Their adapters implement the
shared `IAccountLoginAdapter` lifecycle and return a sanitized failure without
issuing a credential. Including their method descriptors never enables a button.
Existing Kimi/Z.ai key adapters and Codex Core-owned/snapshot methods are untouched.
No new credential store, refresh owner, owner migration, revoke/logout or dummy
route is introduced.

The useful qualified local boundaries are:

- Kimi local-owner response envelope and `data.kind`, native windows, missing vs
  null booster state, integer cents/currency and monthly charge metadata.
- Z.ai international personal scope, explicit existing nonsecret key ID,
  noncreating discovery GET, short-lived PAT validation and earliest expiry.
  The projection parser is neither a token issuer nor a cache/refresh owner.
- Codex strict correlated RPC decoding, a read-only request allowlist,
  multibucket windows and credit display metadata. Account email/plan are never
  treated as verified account/workspace identity. Unknown credit units are not USD.

Synthetic parser success proves neither real login nor renewal/admission. See
[qualification](../../docs/providers/managed-logins-qualification-2026-10-24.md)
for the exact missing evidence and future qualification sequence.
