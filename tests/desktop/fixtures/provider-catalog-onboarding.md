# Provider onboarding UI fixture

`provider-catalog-onboarding.json` is a secret-free `ProviderCatalogDto` snapshot
for synthetic desktop tests. It never contacts the URLs it contains.

The three entries copy the researched manifests from the parallel provider
workers at this checkpoint:

- NVIDIA Build: ongoing developer access, documented route features, unknown
  live budgets and account limits, no qualified spend-prevention claim.
- Inception Mercury: a one-time new-account trial and separate PAYG access;
  trial expiry remains unknown. It must not match an ongoing-free filter.
- Poe: subscription points and add-on points, shared account scope, distinct
  model/manufacturer/publisher evidence and no guessed recurring price.

All entries retain `liveVerification: not_tested`. The snapshot is not evidence
of a successful account connection. No key values or other secrets appear here.

Use empty `AccountDto[]` to model the user's current state without new keys.
For state-label tests, clone a descriptor in memory with `AdapterState` set to
`documented`, `prepared`, or `mock_tested`. A separate clone with
`LiveVerification = "verified"` can check the live-verification label; that is a
synthetic rendering scenario and must not be saved into production manifests.

The view constructor accepts callbacks in this order:

```csharp
new ProviderCatalogView(openLink: uri => opened.Add(uri),
    beginSetup: providerId => selected.Add(providerId));
```

Creating the view and calling `SetCatalog` must invoke neither callback.
Buttons are named `<Provider name>: Official account / keys`,
`<Provider name>: Official documentation`, and `Set up <Provider name> in lain`.
The native toggle is named `<Provider name> setup and capabilities`.
The page heading and root automation name are `Supported providers`;
root shell navigation chooses its own label. The setup callback only passes the
provider ID; the shell owns the account form, concrete route choice, settings,
secret entry and subsequent explicit verification.

Useful checks include access-type filtering, provider search, unknown balances
and capabilities, no trial/free conflation, no provider calls while rendering,
retained expanded state and controls on identical refreshes, and explicit
link/setup callback invocation after a user action.
