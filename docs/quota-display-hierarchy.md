# Shared quota display hierarchy

The account inventory and `lain usage` use `Lain.Client.QuotaPresentation` for
human presentation. Provider response order is not presentation order. This
changes presentation only: provider readings, quota identities, API responses,
history and account configuration stay intact.

- Accounts with measured quotas precede accounts with no readings. Explicit pins
  retain their user-defined priority. Name/provider sorting orders within those
  groups. The explicit 5h ordering remains a held snapshot until updated.
- Short windows are ordered by their stated duration: for example 5 hours before
  7 hours or 7 days. Durations come from window labels, known source identities
  or explicit start/end bounds; time until reset is not a window duration.
- Show up to two primary windows with consumption, available balance and supplied
  reset information. A monthly-only or balance-only account still shows its main
  reading, including an exhausted balance. Missing values remain unknown.
- Additional windows, monthly limits and credit details are secondary. Desktop
  exposes them through an initially closed per-account expander; its open state
  survives quota refresh and page navigation. CLI compact output points to
  `--full` for secondary details.
- Empty *additional* purchased/free/bonus credit pots do not take space in the
  compact overview. This never suppresses the account's actual main wallet,
  a depleted rolling allowance or a monthly quota. Full account details and
  `lain usage --full` retain every original reading.
- Stale values and failed updates remain identified. A lack of quota support is
  guidance, not a provider failure. Independent budgets are never summed.

Examples: Command Code shows its 5-hour and weekly windows before monthly
credits. Kimi shows its 5-hour allowance before expandable monthly budgets.
MiMo without a connected quota source follows accounts with measured quotas.
The monitor table follows the same hierarchy. `lain usage --json` and
`lain usage --full --json` remain complete and identical for the same snapshot.
