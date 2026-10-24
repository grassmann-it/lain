# Account usage and proxy history

Run `lain --help` to check the installed version's command contract.
`lain usage` shows current quota snapshots for every configured account:

```sh
lain usage
lain usage --json
```

The human overview shows actual account names and providers, enabled/disabled
state, quota windows, consumed-percentage bars, remaining balances and reset
times. Disabled accounts retain their reported values. Missing credentials,
unsupported quota APIs, missing snapshots and unknown measurements stay visible.
Only known consumed percentages produce bars; a known zero is a valid reading.
Stale values are marked as last known, balances retain their native units and
shared budgets remain identified as shared.

The overview makes `GET /api/v1/accounts`, then `GET /api/v1/quotas` through the
existing authenticated client. These reads use Host snapshots and do not trigger
a provider refresh. If either GET fails, the command keeps the existing exit code
and stderr diagnostic behavior, with empty stdout.

`usage --json` has a versioned CLI composition envelope. Each entry contains the
complete `AccountDto` and every matching `QuotaDto`, in Host account order. For
example, an account without a quota snapshot has an empty array:

```json
{
  "version": 1,
  "kind": "account-usage",
  "accounts": [
    {
      "account": {
        "id": "account-example",
        "name": "Personal account",
        "provider": "codex",
        "enabled": true,
        "credentialKind": "codex-owned",
        "state": "ready",
        "budgetPoolId": null,
        "expiresAt": null,
        "sharedWithLegacy": false
      },
      "quotas": []
    }
  ]
}
```

`lain quotas --json` continues to return the original raw `QuotaDto[]` from a
single `GET /api/v1/quotas`. It does not fetch account names or add an envelope.

Use `history` for proxy requests, tokens and performance:

```sh
lain history --json
lain history --today --timezone Europe/Berlin --json
lain history --from 2026-10-23T00:00:00Z --to 2026-10-24T00:00:00Z --account ACCOUNT_ID --json
lain history analysis --today --group-by modelFamily,accountId --json
lain history aggregate --today --json
lain history performance --today --json
```

`history` accepts the existing historical usage arguments and preserves their
validation, API paths and JSON payloads. Summary reads use `/api/v1/usage`;
`analysis`, `aggregate` and `performance` use `/api/v1/usage/analysis`.
See [observability CLI](observability-cli.md) for exact filters, measured
performance and coverage semantics.

Explicit historical invocations under `usage` remain aliases, including
`lain usage --today --json`, `lain usage --from ... --to ...`, account/model
filters and `lain usage analysis|aggregate|performance`. Only `usage` with no
arguments or with `--json` alone selects the all-account quota overview.
