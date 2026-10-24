# Desktop/CLI presentation stabilization

Scope: AC-02/09 presentation and UX-01–03 against shared account/quota/usage
contracts. Provider owners and credential/login lifecycle remain on the host.
No installed service, real provider, real account, or normal desktop is used by
this package's fixtures.

## Implemented presentation

- An empty individual budget window is labeled as that window's state. It no
  longer paints an account-wide exhausted border or makes an unproved claim
  that the entire account is exhausted (F03).
- All reported quota windows remain in the inventory. Small amounts retain
  significant digits; exact provider amounts remain accessible. Kilo Pass
  amounts/billing/renewal and partial regeneration are distinct typed facts.
- Accounts use a reserved adjacent 400 px detail pane at widths >=1000. Opening
  detail changes neither inventory width nor card coordinates. This intentionally
  trades simultaneous card columns for stable reading space. At narrower widths,
  detail is a separate task page; closing restores the mounted inventory, scroll
  and triggering control. No pane covers another card.
- One frozen command list supplies visible menus and native context menus for
  accounts, models and profiles. Detail/readout context menus use the same pattern.
  Destructive account removal names route bindings and retains history. Escape
  closes account detail and returns keyboard focus.
- Configured RuntimeMode is independent of readiness and information level.
  Barebones Gateway directly exposes Accounts/Profiles/Usage; Simple adds Models;
  Advanced adds Live. Other available destinations appear once in More. Monitor
  exposes Accounts and only an available local-history source; an offline Gateway
  retains its configured navigation. Monitor permits user-selected 640×420 windows
  without forcing a geometry change.
- Newly opened editors reset inherited page scroll to show object identity (F08).
  Mounted drafts and their own navigation context remain preserved.
- Models render compact inventory rows with direct edit/More commands. Advanced
  adds technical identity without automatically opening every detail. Profiles
  retain master/detail, primary Apply, secondary Edit and a named action menu.
- Usage places closed optional filters and compact totals before the breakdown.
  Known reported component sums carry measured/eligible counts and do not replace
  unknown full totals. Typed fixed error category/evidence/action are shown in
  desktop and CLI attempt details. Live keeps pause/follow visible and moves
  refresh/clear into a local tools menu.

## Connect and runtime actions

The provider chooser consumes the installed host method catalog, including
unavailable methods and their concrete guidance. It does not infer method
availability from a browser URL. Available methods are gated by purpose, explicit
host mode and CoreLogin. A key flow asks only the actual resource fields, name,
private credential and (for external snapshots) expiry. Tariffs and models are
absent. The resulting account precedes optional model configuration. Reconnect
uses the existing account ID, generation and binding. Browser/CLI methods use the
same existing login operation, resource selection, status and cancellation API.

The CLI uses these same typed endpoints. `accounts methods`, generic
`accounts login PROVIDER --method ID --purpose ... --account ID`, and `mode`
expose the installed methods and explicit host intent. Existing `add-api`
stdin/environment automation remains intact. Settings mode changes use a separate
review/confirm action; the footer remains the only information-level selector.
Global profile Apply requires a host preview; an unavailable/unknown baseline is
visible and never replaced by a fabricated diff. Application status/recovery text
and exported profile/generation identity remain distinct.

## Qualification

Serialized linux-x64 builds passed for production Desktop/CLI and both new
harnesses. The Desktop production build has no warnings; its harness uses the
same obsolete Bitmap.Save overload as the older harness and reports that one
warning. Existing client/CLI regression checks: **218 passed**. The intentional
wizard change updates its old tariff/model prompt assertions, preserving
cancellation, naming, private input and revision-conflict checks. The private PTY
save/hidden-secret/cancel/redirect suite passed. Six additional CLI checks passed
for deliberate Monitor, exact small money, Pass dates/regeneration and partial
token measurement coverage.

Private Xvfb stabilization: **26 checks passed**, 17 in-memory API calls. The sole
POST is the explicitly clicked synthetic Connect; there are no provider,
inference, service, browser or installation actions. Evidence and synthetic
screenshots: [artifact directory](../artifacts/desktop-connect-stabilization-20261024/evidence.json).
1120×780 and 800×560 captures cover account details/modes,45 configured model
entries, Profiles and Apply preview; Monitor is also captured at640×420.
Screenshots were visually inspected for adjacent/narrow details and Models /
Connect result. This is UI fixture evidence, not OS pointer, screenreader or live
provider qualification.

Host admission/wiring and the authoritative25-provider /35-product method
catalog remain integrator work. Alias-compatible reconnect (command-code versus
commandcode; mimo versus xiaomi-mimo) needs host integration coverage. The generic
resource/reconnect lifecycle is connected to frozen endpoints but requires the
integrated host fixtures; no real credential flow is claimed complete here. Existing broad desktop assertions
which require exactly five primary entries and expanding account tiles represent
the superseded navigation/layout; the new focused harness qualifies the new
contract independently.
