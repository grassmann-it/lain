# Editor field preservation inventory

Read on base `e48bc11`. Paths/line numbers refer to that source. Runtime native
results remain **not_run** until separately recorded. This inventory distinguishes
editable data, selectors/actions and read-only provenance; it is not a screenshot
claim. The new native harness targets Models and Profiles; existing owner suites
remain responsible for other editor flows.

| Editor / source | Fields and actions | Saved/retained meaning |
| --- | --- | --- |
| Models, `src/Lain.Desktop/MainWindow.Routes.cs:418` | Client name; provider model ID; provider identifier; known-model suggestion; provenance ID; documented provider-route selector/load | `Alias`, `UpstreamModel`, `Provider`, `ProviderRouteId`. Suggestions/load are explicit helpers, never inferred capabilities; unchanged provenance remains exact. |
| Models binding, `MainWindow.Routes.cs:469` | Explicit pool; every account checkbox; Enabled; Visible | Stable account IDs preserved in original order when unchanged; pooled/strict semantics, enabled and menu visibility remain distinct. |
| Models policy, `MainWindow.Routes.cs:552` and `:601` | Spending policy; every currently revealed fallback slot; paid-fallback permission; Check spending policy | `RoutingPolicyDto` retained when unchanged. Fallback ID order retained; selecting free-only disables/removes paid permission. Existing unrecognized cost policy has explicit Keep existing policy. Check uses saved route and does not save/apply. |
| Models footer, `MainWindow.Routes.cs:520` | Save model access; Cancel | Save is exact r1/configuration revision, not Apply. Cancel discards draft. Native gate checks current trigger focus after both. |
| Profiles, `src/Lain.Desktop/MainWindow.Profiles.cs:735` | Profile name; default client name; search; each Include model access checkbox; Select shown; Clear shown | Stable profile ID, original route-ID order if unchanged, `DefaultRouteId`; search is presentation only. Hidden/disabled default is explained. |
| Profiles policy, `MainWindow.Profiles.cs:762` | Shared spending policy; fallback expander; every fallback checkbox; up/down order actions; paid-fallback permission; Check spending policy | Explicit IDs/order/permission preserved. Runtime fallback limitation is shown; saving does not imply executable fallback or Apply. |
| Profiles footer, `MainWindow.Profiles.cs:855` | Save profile; Cancel | Exact p1/revision mutation, preserved selections/default/policy; no Apply/export. Native gate checks current trigger focus after both. |
| Client setup, `MainWindow.Profiles.cs:556` | lain executable; existing destination parent; Browse; Prepare; Prepared identity; Install; Cancel | Preparation is read-only; installed artifacts preserve exact profile/revision/generation. Native setup already has separate UXD-20 evidence; final editor gate does not repeat it. |
| Account Connect, `src/Lain.Desktop/MainWindow.AccountChooser.cs:222` | Account name; private credential; optional expiry; method-declared settings including enum/resource/region fields; explicit Connect/Back | Exact method contract determines visible fields. Reconnect binds existing ID; credential controls remain secret, absent from this inventory's runtime screenshots. Dynamic setting fields must not be replaced by a static provider-wide list. |
| Login/resource choice, `src/Lain.Desktop/MainWindow.Login.cs:44` | Start/reopen/cancel; safe browser URL/manual copy; resource choices when returned | Same operation on reopen, exact selected provider resource. No manufactured account/rights or credential output. Existing lifecycle suite owns callback/race evidence. |
| Monitor source editor, `src/Lain.Desktop/MainWindow.Monitor.cs:52` | Available source selector; resource ID; private monitoring token; optional expiry; source-specific guidance | Separate monitoring purpose/owner; existing inference key retained. Source fields/claims may change in concurrent AC-03 work and require that owner's final inventory delta. |
| Legacy custom account/import, `src/Lain.Desktop/MainWindow.Accounts.cs:444` and `:469` | Custom name/provider/endpoint/private API key; import config path/optional auth directory | Explicit legacy account/import workflow, not general Connect success or implicit login. No live credential discovery in this harness. |
| Settings, `src/Lain.Desktop/MainWindow.Settings.cs` | Section density; close-to-tray; start hidden; notifications; Help/About | Local preferences with actual tray availability distinct. Concurrent Settings owner supplies inline Retry and final native rollback/focus evidence. Shell information-level dropdown remains separate. |

The runtime `controls` entries in `evidence.json` enumerate every mounted Models/
Profiles TextBox, ComboBox and CheckBox and whether actual Tab reached it. Expanded
policy/suggestion fields are included; disabled controls are measured separately
and are not falsely required to receive Tab focus. Save/Cancel outcomes retain
request body, target ID and revision through the existing synthetic fixture.

Source concerns to verify rather than hide: Cancel handlers currently just clear
`_editing` and call `Render()`; no explicit return-to-Edit focus assignment appears
there. The harness does not call `.Focus()` to manufacture a passing result.
