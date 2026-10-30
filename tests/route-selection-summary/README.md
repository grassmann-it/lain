# Observed route summary (PUB05)

Pure display helper: `Lain.Desktop.Presentation.RouteSelectionSummary`.

Embed at the top of the existing selected Usage attempt details:

```csharp
details.Children.Insert(0, RouteSelectionSummary.From(attempt).CreateControl());
```

For a selected saved-recording record:

```csharp
details.Children.Add(RouteSelectionSummary.From(record).CreateControl());
```

The helper receives only `AttemptDto` or `RecordingNormalizedRecord`. It does not
read current routing configuration, account names, quotas, balances, file paths,
request bodies or error bodies. RequestedModel is displayed as the requested
alias/router/model without guessing its kind. Actual provider/model/account/route
come directly from that selected record. Missing fields remain unknown. No merging
by model name or provider is performed.

The present DTOs contain no observed selection/skipped-reason field; both reasons
therefore remain `Unknown (not recorded)`. If explicit selection observations are
added to the shared contract later, add a separately provenance-checked mapping.
HTTP 429 remains HTTP 429; it is never interpreted as quota exhaustion, a skipped
account, fallback, or a reason to use a paid route. Recording completeness is
capture coverage; a finish reason remains a reported finish, never task success.

`Lines()` exposes fixed, read-only safe display lines. The small Control adds no
interactive inputs and performs no API calls or routing actions. Fields are capped
at 120 UTF-16 characters; newline/control/bidi formatting is removed. There are
five content lines, a title, at most two wrapped lines per field, and a 240-pixel
maximum height. It is an embeddable header, not a new page.

Synthetic checks (run only in the shared serial build lane):

```
dotnet run --project tests/route-selection-summary/RouteSelectionSummary.Tests.csproj
```

Cases cover requested != executed target, missing requested/actual identities,
same model on distinct accounts, HTTP 429 without quota/fallback claims, partial
recordings, reported finish semantics, hostile/long labels, and bounded read-only
controls. No MainWindow, UsageView, Host source or live services are changed.
