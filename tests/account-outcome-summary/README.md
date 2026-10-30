# Account outcome summary (PUB-04)

Pure account-details rows, using only supplied `AccountDto`, `RouteDto`, `QuotaDto`,
`StatusDto` and an explicit clock. No network, storage, login, repair, inference,
price policy or model selection is performed. `QuotaPresentation.HasReading` is
reused so zero measurements and exact/native amount handling stay consistent.

Root integration in `MainWindow.Accounts.cs`, inside `AccountDetail`
(the existing method building the details panel), after the existing action strip:

```csharp
var summary = AccountOutcomeSummary.Build(account,
    _configuration?.Routes ?? [], quotas, _status, DateTimeOffset.UtcNow);
foreach (var row in summary.Rows)
{
    content.Children.Add(Ui.Text(row.Label + ": " + row.Value, 12));
    content.Children.Add(Ui.Text(row.Detail, 11, Ui.Muted));
}
```

Use existing text rows/detail layout; this does not require new account tiles or
cards. Keep the saved custom account name and existing actions. Expose `Evidence`
and `ObservedAt` in technical detail/tooltips when useful. Supplied global service
status and desired-route configuration are explicitly distinct from an account's
provider observation. Inference remains unverified in this component because none
of these inputs proves a successful provider inference attempt. A future attempt
view can show that separate evidence without redefining quota success.

Null legacy capabilities remain unknown. Explicit credential-purpose metadata may
show configured intent. `available` capability state is still a configured
capability, not proof of provider authentication or response quality. Model route
counts distinguish configured from enabled-and-visible, without inventing profile
inclusion or applied selection. Monitoring freshness honors snapshot `ok`/`fresh`
state and explicit `FreshUntil`; it does not invent a client TTL. Unsupported
monitoring is neutral and does not disable other account purposes. Raw provider
errors, credential handles and source URLs are not rendered.

Prepared serial-lane check (not run at handoff):

```sh
dotnet run --project tests/account-outcome-summary/AccountOutcomeSummary.Tests.csproj
```

It links the pure file, references the existing Client/Core projects and adds no
packages. Checks include legacy null/empty capability semantics, preserved names,
configured versus observed outcomes, zero/non-finite readings, stale/mixed/expired
freshness, account isolation, unsupported monitoring, disabled intent, credential
expiry, explicit purpose metadata and excluded raw error text. Desktop root build
will compile the component after cherry-pick.
