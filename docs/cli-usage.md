# Account usage and proxy history

Run `lain --help` to check the installed version's command contract.
`lain usage` shows useful cached quota readings. `--full` includes every configured account:

```sh
lain usage
lain usage --full
lain usage --json
```

The default human overview shows accounts with actual readings in a compact
aligned table, preserving Host account order. Accounts without readings are
hidden; a neutral footer gives their count and points to `--full`. The heading's
account count still describes the complete configured account set.

Each account has at most two primary readings, with shorter explicit windows
first (for example, 5 hours before 7 hours or 7 days). Monthly allowances are
secondary; additional credits retain their native amounts, and zero-only extra
credits are hidden. A sole monthly allowance or wallet remains visible, as does
a real exhausted main balance. Missing readings stay unknown and never become
zero. Unknown secondary windows remain in `--full`.

The table leads with window and native remaining amount or explicitly labelled
reading. Usage, usage estimates, observed rates and configured limits retain
their own labels; they never become remaining funds. Percentage consumption and
reset columns appear when there is room. From 110 columns, accounts have a
shared table column; narrower terminals use account headings. From 140 columns,
warnings have their own state column. At smaller widths, reset/state details
continue directly below the relevant reading. Below 48 columns, a single-column
layout preserves native units. Long names and labels wrap locally without
forcing other accounts out of the table or dropping text.

Known generated single-account names use short provider labels such as `Gemini`,
`NVIDIA` or `Cloudflare`; a single account with an imported email name also uses
the provider label. Custom names are preserved; multiple accounts remain
distinguishable. These display labels never rename accounts or alter JSON.
Headings use bold text and colour is reserved for warnings. Consumption bars and
raw provider IDs no longer occupy the default overview. Plain output and
`NO_COLOR` retain the same content and alignment.

Resets use the system's local timezone, named in the heading, with an explicit
UTC offset on each future reset (including repeated hours at daylight-saving
transitions). `--full` and JSON retain the original UTC timestamps. Disabled
accounts retain their reported readings. Credential, expiry and shared-budget
warnings stay visible for measured accounts; accounts without readings and their
setup/error diagnostics remain available in `--full`. Refresh errors and
last-known warnings remain visible even when their measured quota window is
deferred. Stale/overdue displayed values are marked as last known. Successful
snapshots older than five minutes, or numerical snapshots without a successful
check time, are also last known.

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
