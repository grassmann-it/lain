# Account usage and proxy history

Run `lain --help` to check the installed version's command contract.
`lain usage` shows current quota snapshots for every configured account:

```sh
lain usage
lain usage --full
lain usage --json
```

The default human overview puts accounts with quota readings first, preserving
Host order within each group. Each short account header has at most two primary
readings, with shorter quota windows first (for example, 5 hours before 7 hours
or 7 days). Monthly allowances are secondary; additional credits use concise
amounts, and zero-only extra credits are hidden. A sole monthly allowance or
wallet remains visible, as does a real exhausted main balance. Missing readings
stay unknown and never become zero. When a main reading exists, unknown
secondary windows are deferred to `--full`.

Account names are highlighted, repeated provider names are omitted, and status
colours belong to consumption bars and warnings rather than whole rows. Window
labels align within each account. Accounts without readings form a quiet second
group, with short setup/status guidance instead of full connection instructions.
Detailed instructions and unabridged error messages remain in `--full`.

Narrow terminals wrap at field boundaries, with indented continuations; below
48 columns the bar gives way to its explicit percentage. Long names and words
wrap without dropping text. Known consumed percentages, native remaining
balances, reset times and concise shared-budget tags remain visible. Resets use
the system's local timezone, named in the heading, with an explicit UTC offset
on each future reset (including repeated hours at daylight-saving transitions).
`--full` and JSON retain the original UTC timestamps. Disabled
accounts retain their reported values. Missing credentials, expiry and unavailable
quota diagnostics remain visible for accounts without readings. Refresh errors
and last-known warnings remain visible even when their quota window is deferred. Stale/overdue displayed
values are marked as last known. Successful snapshots older than five minutes,
or numerical snapshots without a successful check time, are also last known.

`lain usage --full` shows every window, including zero-only extra credits and
unknown readings, in the same human presentation order. It includes full account
IDs, credential diagnostics, used/limit amounts, source/check times and
shared-budget explanations. `lain usage --full --json` returns exactly the same
overview JSON as `lain usage --json`, preserving all data and the original Host
account/window order; the flag only changes human presentation.

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
filters and `lain usage analysis|aggregate|performance`. `usage` with only the
optional `--full` and `--json` flags selects the all-account quota overview.
Combining `--full` with historical arguments or `history` is rejected before
Host discovery.
