# lain: vorbereitete UXpilot-Promptserie

Stand 24.10.2026. **Nur Vorbereitung; keine UXpilot-Generierung in dieser Sitzung.**
Alle Vorschläge sind Designentwürfe. Die zugehörigen konkreten Befunde stehen im
[UX-Review](ux-review-2026-10-24.md), die aktuellen synthetischen Ausgangsbilder in
der [Gesamtgalerie](../artifacts/ux-review-20261024/index.html).

Die englischen Blöcke sind kopierfertig. Zuerst Foundation als gemeinsame
Projektanweisung verwenden, danach jeweils einen gezielten Screenprompt. Eine
neue Sitzung soll nicht ungefragt alle Varianten erzeugen. Sinnvoller erster
Vergleich: Accountsübersicht und derselbe Account im geöffneten Detail; danach
die übrigen Arbeitsflächen. Der separate Codex-Handoff regelt den Sitzungsstart.

Für jeden Screen die verlinkten Original-PNGs als aktuellen Iststand betrachten.
Sie enthalten synthetische Fixtures, keine echten Konten oder Secrets. Die
Prompts benennen zusätzlich synthetische Designwerte; sie behaupten weder echte
Kontingentstände noch neue Providerfähigkeiten. Fehlende Quotaquellen werden
als fehlend gestaltet. Das gelieferte Katzenasset bleibt unverändert.

## 1. Global foundation

Gemeinsamer EN-Kontext; kein zusätzlicher Dashboard-Screen. Referenzen:
[Accounts](../artifacts/ux-review-20261024/pages/accounts-initial-1120x780.png),
[Profiles](../artifacts/ux-review-20261024/pages/matrix/matrix-profiles-simple-normal-1120x780.png),
[Settings](../artifacts/ux-review-20261024/pages/matrix/matrix-settings-simple-normal-1120x780.png).

```text
Design a coherent native desktop UI concept for “lain — Local AI Gateway”.
This is an evolution of the supplied current application screenshots, not a
new product identity. The application manages AI provider accounts and their
reported quotas, configured model access, saved profiles, persistent local
usage, and an ephemeral live request inspector. It also has a planned quota
monitor use case without a local inference proxy.

Use this as the shared foundation for all subsequent screens. Do not generate
an extra dashboard or a foundation screen. Treat the layouts as design proposals,
not as implemented functionality. Use English UI copy.

Preserve:
- The exact supplied cat logo. Do not redraw, restyle, replace, or generate it.
- The compact, quiet, dark native desktop personality: Avalonia with Ursa/Semi
  controls, credible desktop menus, dropdowns, tables, dialogs and focus states.
- Accounts as the start page and Profiles as a directly reachable frequent task.
- All existing expert capabilities, even when they move into a precise local
  menu or inspector. Progressive disclosure must not remove functionality.

Visual foundation:
- Workspace #050706; cards #08100D; inputs/raised surfaces #0C1511;
  hover/selected tint #10281F; subtle separators #30493E.
- Primary text #EDF2EF; secondary text #89938E; mint accent #73D7AE;
  semantic success #9BCF9C, warning #D9AF65, error #EA8087.
- Use Inter for body, values, forms and tables. Use a clean, correctly rendered
  IBM Plex Sans Condensed for restrained headings. Oxanium may be used for the
  lain wordmark and compact navigation. If a requested family is unavailable,
  use a compatible legible sans-serif rather than a serif fallback.
- The current captures show some serif-like title rendering. Do not preserve
  that accident as a brand requirement. Preserve mint, cat, dark surfaces and
  native character, while improving typography and hierarchy.
- Begin with 13 px body text, 11–12 px secondary metadata, 16–22 px headings.
  Improve density by removing repetition and arranging information, not by
  making operational text microscopic. Use aligned/tabular numeric values.
- Prefer 4–6 px corner radii, subtle 1 px borders, 4 px progress tracks,
  restrained 8/12/16 px spacing and desktop controls around 32 px high.
  Use visible mint keyboard focus. Keep status understandable without color.
- No gradients, glass effects, oversized rounded cards, marketing hero blocks,
  giant SaaS sidebar, decorative charts, mobile phone framing or browser tabs.

Design for the actual desktop viewports 1120×780 and 800×560. Start with the
requested viewport only. A narrow version is still the same desktop app.
Do not pretend a smaller proposed minimum is already supported. Do not resize
the window automatically when changing information level.

Shared shell:
- Small cat + lain wordmark in the top chrome, native window controls at right,
  a clear Settings entry and a quiet “Reload status” action. Reload status is
  not the same action as querying a provider’s quota.
- A thin global status area exposes host health, the actually applied profile
  and pending saved changes. The active profile can lead to Profiles.
- Exactly ONE information-level dropdown in the bottom-right footer:
  Barebones / Simple / Advanced. Do not repeat it in Settings or a toolbar.
- The proposed Simple gateway navigation is Accounts, Models, Profiles, Usage,
  More; Live is in More. Advanced shows Accounts, Models, Profiles, Usage, Live.
  Barebones keeps Accounts, Profiles and Usage direct, with Models/Live in More.
  Keep relative destination order and expose each destination once.
- Information level is independent from per-page Normal/Compact density.
  A local View menu can contain density, sorting and grouping, but not another
  information-level selector. Explicitly chosen filters/details/drafts survive.
- A deliberately configured monitor shows Accounts and only genuinely available
  history. It must not show fake proxy controls or claim request history without
  a history source. Advanced does not create missing capabilities. An offline
  configured gateway retains its destinations and cached structure with status.

Data and interaction rules:
- Every provider-reported quota window remains visible in the closed account
  summary: its name, reported used/remaining value and reset when supplied.
- Use consumed bars consistently. Balance-only money/points without a denominator
  have no percent bar. Unknown is not zero. Missing reset is not an invented date.
- Show stale/error state at the affected window. Past reset time means “reset
  due, refresh to check”, not a confirmed replenishment.
- Shared provider pools are not summed. A zero free-credit balance does not
  prove the entire account is unusable when other budgets exist.
- A credential saved locally is not proof of successful inference. Successful
  login does not prove quota support, a paid plan or all scopes.
- All figures and identities in these mockups are explicitly synthetic fixture
  data supplied for design. Do not add quotas, prices, costs, OAuth methods,
  usage charts or provider capabilities that are not specified.
- Right-click, the visible ⋯ button and keyboard context-menu access expose the
  same object commands. Important tasks cannot be right-click-only.
- Opening account details must not turn one masonry card into a full-width
  bridge that moves unrelated accounts. Use a focused side inspector or a
  dedicated narrow detail view with preserved list context.
- Selection, saving, applying to the global proxy, and exporting client files
  are different states/actions. A route’s menu visibility is not a kill switch.

Judge the design by tasks: quickly compare quotas, find the real next repair
action, edit one of 45 model entries, switch a profile with clear global impact,
read usage results in the first viewport, and follow live output without losing
orientation. Provide a usable application frame, not a presentation slide full
of design notes. Put any necessary design annotations outside the application.
```

## 2. Accounts: Hauptansicht

Erster empfohlener Entwurf. Iststand:
[Simple](../artifacts/ux-review-20261024/pages/accounts-initial-1120x780.png),
[Barebones](../artifacts/ux-review-20261024/pages/matrix/matrix-accounts-barebones-normal-1120x780.png),
[klein](../artifacts/ux-review-20261024/pages/accounts-initial-800x560.png).

```text
Using the shared lain foundation, design the Accounts start page at 1120×780,
Simple information level, Normal density, configured gateway. This is the main
product screen, not a marketing hero. Preserve the supplied cat and dark/mint
desktop identity while making the account inventory significantly easier to scan.

The first glance must answer: which account is this, what remains in every
reported quota window, when does it reset, and is this value current?

Layout:
- Compact page heading “Accounts”. One primary “Add account” action and a quiet
  secondary “Discover providers” entry. Do not create competing big setup cards.
- Search and a compact “Filter / View” control. Keep active filters understandable
  without a permanent second row of configuration fields when none are active.
- A stable two-column inventory with clear reading order, pinned accounts first.
  Cards remain in position when another account is inspected. They may have
  different heights because different providers report different windows.
- Account name, provider and visible ⋯ at each card head. Provide an accessible
  quick “Refresh quota” command without opening technical details. A clear
  account/title detail trigger should not look like a chevron for a single quota.
- Each quota uses a compact aligned row: window name, used percentage or real
  remaining amount, consumed bar only when valid, remaining value, reset.
- Normal shared freshness appears once per card. If windows have different
  freshness, show the exception locally. Source URLs belong in the inspector.
- Do not repeat the same balance on adjacent lines, “Enabled” on every healthy
  row, or a full “checked / local time / source” sentence for every window.
- Keep each reported window visible. Do not use “+3 more quotas” to save space.

Use ONLY this synthetic dataset. Treat all times as Europe/Berlin; the design
clock is 24 Oct 2026, 14:10. These are sample values, not real entitlements.

1. “Personal Codex” — provider Codex; pinned; shared provider budget.
   7 days: 37% used, 63% left; resets 30 Oct, 13:00.
   Last successful quota update: 24 Oct, 14:10.

2. “Command Code Pro” — provider Command Code.
   5 hours: 20% used, 16 of 20 USD-Credits left. Last success 23 Oct, 14:00;
   reset was due 23 Oct, 19:00, so label it as unconfirmed after its reset time.
   Weekly: 55% used, 18 of 40 USD-Credits left; resets 30 Oct, 13:00;
   last success 23 Oct, 14:00; refresh temporarily failed.
   Monthly credits: 25% used, 60 of 80 USD-Credits left; resets 1 Nov, 01:00;
   last success 24 Oct, 14:10.
   Purchased credits: 12 USD-Credits left; no denominator or reset supplied.
   Free credits: 0 USD-Credits left; no denominator or reset supplied.
   Model allowance: not reported; no value or reset supplied.
   If summarizing the zero balance, say “Free credits empty”, never the broad
   “Account exhausted”. Do not imply paid fallback will happen automatically.

3. “Kimi Coding” — provider Kimi.
   5 hours: last known 100% used, 0% left; last success 23 Oct, 14:00;
   reset was due 23 Oct, 19:00. It needs refresh, not a fabricated replenishment.
   Monthly total: 62% used, 38% left; resets 1 Nov, 01:00.
   Monthly coding: 18% used, 82% left; resets 1 Nov, 01:00.
   The two monthly windows were last checked 24 Oct, 14:10.

4. “Z.ai Coding” — provider Z.ai.
   Credits · 5 hours: 25% used; 750 of 1,000 credits left;
   resets 24 Oct, 19:00.
   Credits · 7 days: 72% used; 28% left; absolute amounts not supplied;
   resets 30 Oct, 13:00. Both last checked 24 Oct, 14:10.

5. “MiMo API” — provider Xiaomi MiMo.
   No supported quota source in this application state. Show a concise neutral
   explanation and “Open provider console”; do not draw a zero bar, fake balance,
   fake reset or universal reconnect warning.

The thin status area says “Proxy ready · Applied: Workspace 2”. The footer
shows the Simple navigation from the foundation and ONE “Simple” dropdown.

Keep the useful content near the top; fit more meaningful account information
than the current screen without hiding quotas or shrinking text below the
foundation. A separate later 800×560 variant should stack the same cards and
preserve their values rather than invent a mobile interface.
```

## 3. Ein gemeinsames Accountdetail und Aktionsmenü

Iststand:
[vorher](../artifacts/ux-review-20261024/interactions/states/01-account-before-closed-1120x780.png),
[nach Öffnen](../artifacts/ux-review-20261024/interactions/states/01-account-after-open-1120x780.png),
[heutige Aktionen](../artifacts/ux-review-20261024/interactions/states/01b-account-after-actions-1120x780.png),
[Technik klein](../artifacts/ux-review-20261024/interactions/states/02-account-after-technical-800x560.png).

```text
Using the shared lain foundation and the exact Accounts dataset above, design
the state after opening “Z.ai Coding” at 1120×780, Simple/Normal.

Solve the current loss of spatial context: the account must not become a
full-width expanded card below unrelated columns. Keep the overview’s card
positions and scroll anchor unchanged. Open a focused approximately 400–440 px
side inspector as an overlay within the workspace; it must not reflow the grid.
The selected card has a restrained selected outline. The shell remains stable.

Inspector hierarchy:
1. Fixed compact header: “Z.ai Coding”, provider Z.ai, explicit Close button,
   and visible ⋯. Account identity remains visible while detail content scrolls.
2. Its two exact quota windows with the same values and resets as the overview.
   One common “Updated 14:10” line; no duplicated quota report underneath.
3. A direct “Refresh quota” action. Context links “Models (8)” and “Recorded
   usage” lead to the existing pages filtered to this account. The count 8 is
   synthetic configured model access, not a claim about the provider catalog.
4. A compact “Connection” summary. Use “API key stored” and “Local credential
   check available”; do not imply an inference test occurred.
5. A deliberately expandable “Technical details” section with Account ID
   “account-demo-zai”, credential type, individual source timestamps, units,
   and source information. The 7-day window’s absolute amount remains “Not
   supplied”. Never show a secret value.

Shared account command menu, available from ⋯, right-click and keyboard:
Refresh quota; Open recorded usage; Open models; Pin account; Disable account;
Manage connection / monitoring access; Check local credential; separator;
Remove account… . Keep rare/destructive commands secondary, not an eight-button
wall. Menu labels must make the selected target obvious. A removal preview
would show model references and retained history; do not draw a one-click delete.

Show a concise local repair banner only when the source state requires one.
For an optional stale variant, say “Last successful update 23 Oct, 14:00” with
“Refresh quota”; for expired access use “Sign in again” on the same account.
These states are alternatives, not simultaneous global errors.

Keyboard behavior to communicate outside the canvas if needed: Escape closes
the inspector, focus returns to the same account, reopening preserves explicit
detail preferences. Do not put implementation notes inside the product UI.

Create only this requested wide state now. If a narrow variant is requested
later, at 800×560 use a dedicated account detail view with “Back to Accounts”,
the same identity/actions, and a restored inventory anchor on return. Do not
squeeze a tiny list and tiny inspector side by side.
```

## 4. Account verbinden: Anbieter zuerst, Modelle danach

Iststand:
[Chooser](../artifacts/ux-review-20261024/interactions/supplement/28-connect-chooser-1120x780.png),
[Formular](../artifacts/ux-review-20261024/interactions/provider-flow/provider-multi-form-before-1120x780.png),
[kleines Formular mit Modellen](../artifacts/ux-review-20261024/interactions/provider-flow/provider-connection-credentials-models-800x560.png).
Der neue Account-/Fähigkeitsvertrag ist ein vorgeschlagenes Backendvorhaben.
Der Entwurf darf ihn darstellen, aber nicht als bereits ausgeliefert bezeichnen.

```text
Using the shared lain foundation, design the new “Add account” flow as a small
set of coherent desktop states. The goal is provider → simplest supported
connection → account with verified capabilities → optional model selection.
The current flow has an extra connection-category page and asks for routing-
derived billing plans too early. Remove that conceptual detour.

Default requested output is State A at 1120×780. States B–D below define the
same flow for later explicitly requested frames; do not automatically generate
every state or add an elaborate wizard with artificial required steps.

STATE A — Provider chooser:
- Open directly from “Add account”. Use a compact dialog or focused content
  page with “Add account”, provider search, readable provider rows and Cancel.
- Show Codex, Command Code, Kimi Code, Z.ai Coding, Kilo, OpenRouter and Poe,
  plus “Custom API / Import setup” as quiet alternatives.
- For this design’s current-availability examples, Codex offers its existing
  browser sign-in; the other listed providers can offer their existing API-key
  route. Do not label their planned browser flows as already available.
- Each row shows provider name, a short connection-method label and existing
  account count only where supplied. Avoid pricing/plan marketing cards.
- Choosing a provider reveals its recommended supported connection. Provider
  documentation remains accessible without credentials through a secondary link.
- The provider catalog and this chooser use the same provider identity; do not
  create another separate catalogue/category screen before this one.

STATE B — Supported browser connection, Codex in configured gateway mode:
- Heading “Connect Codex”. One primary “Continue in browser” button and a short
  description of the actual connection purpose. API-key or import alternatives
  appear only if supported, under “Other connection methods”.
- Waiting state: “Complete sign-in in your browser”, “Reopen browser”, Cancel.
  No request for a user-managed client ID, callback URL or pasted OAuth token.
- A resource selector appears only if the provider actually returns multiple
  eligible accounts/projects. Never insert an obligatory resource page when
  only one exists. Reconnect preserves the original account/resource binding.
- This existing Codex example must not be presented as proof of a working
  core-free monitor authentication lifecycle.

STATE C — API-key alternative, proposed simplified Kilo account foundation:
- Heading “Connect Kilo”; prefilled editable display name “Kilo”; one masked
  “API key” input; a quiet official key-page link; Connect and Cancel.
- No obligatory “Free models” billing plan and no required model choice.
  Ask for a region/project only when it genuinely changes the selected access
  and cannot be discovered. Kilo has no such extra field in this example.
- No paid model is preselected. Do not create new provider keys or purchases
  silently. Do not claim the Kilo wallet reader is already implemented.
- For a later target design that includes a qualified wallet reader, a wallet
  balance is a real currency amount without an invented percent denominator;
  do not generate that success state until its data is explicitly supplied.

STATE D — Successful account result, using supplied Codex quota fixture:
- Show “Personal Codex” and a compact, normal account summary: 7 days 37% used,
  63% left, resets 30 Oct 13:00; updated 24 Oct 14:10, Europe/Berlin.
- State specifically that quota data is available and the account credential
  is connected. Do not say that inference was tested or a proxy was activated.
- Main action “Done”; secondary “Choose models”. Model configuration is the
  next optional task, not a required final wizard step. Preserve custom names.
- Partial access is a usable result: keep the working capability, show the
  missing one with a concrete “Add read access” or “Sign in again” action only
  when that selected connector actually needs it. No universal second token.

Across all states: no inferred paid entitlement, no duplicate account created
by reconnect, no automatic Apply, no paid fallback, no model inference request
for checking quota. Display a friendly local progress/error state without
repeated generic warnings about the whole application. Keep actions reachable
at 800×560 with a stable form header and action area.
```

## 5. Models und Profiles: häufige Arbeit verdichten

Iststand:
[Models](../artifacts/ux-review-20261024/pages/matrix/matrix-models-simple-normal-1120x780.png),
[Modelldetail](../artifacts/ux-review-20261024/interactions/states/08-model-after-details-1120x780.png),
[Profiles](../artifacts/ux-review-20261024/pages/matrix/matrix-profiles-simple-normal-1120x780.png),
[Apply](../artifacts/ux-review-20261024/interactions/states/13-profile-apply-confirmation-1120x780.png).

```text
Using the shared lain foundation, design the configured Models inventory and
its related Profiles workspace. Default requested output is the Models frame
at 1120×780, Simple/Normal. Create the Profiles frame only when explicitly
requested as the second frame. Both must use the same objects and commands.

MODELS FRAME:
- Title “Models”, one primary “Add model access”, compact search and filters.
  This is the user’s configured access inventory, not a provider discovery page.
- The synthetic inventory has 45 entries. Replace the current 45 tall cards
  with a legible list/table that shows several meaningful entries at once.
- Simple columns: Name in client; Provider model / Account; Access state;
  Show in model menu; Spending policy; visible Edit or ⋯.
- Use synthetic sample aliases gpt-lain-01 through gpt-lain-08 and provider
  model identifiers gpt-model-01 through gpt-model-08. These are fixture model
  names, not claims of real model availability. Accounts include Personal
  Codex, Command Code Pro, Z.ai Coding and Kimi Coding from the Accounts design.
- Most entries are Enabled and Visible. gpt-lain-02 is Disabled in the saved
  configuration with “Pending apply”; gpt-lain-04 is Enabled but Hidden from
  the model menu. These states must look different. Hidden is not disabled.
- Selected gpt-lain-01 is bound strictly to Personal Codex, used by 3 saved
  profiles, and has a configured spending policy. Do not invent a first-party
  classification from its alias or promise automatic fallback.
- A shared context menu exposes Edit, Enable/Disable, Show/Hide in model menu,
  Used by profiles, Usage, Open account, Copy client name, and separated Remove… .
  Frequent actions cannot require expanding an ID/provenance section first.
- A focused detail inspector retains exact binding, pool, provenance, IDs,
  policy and fallback references. Advanced adds useful technical columns and
  full controls; it must not automatically expand 45 long forms.
- A concise pending-change indicator explains that the saved Disabled state
  takes effect on the next global profile Apply. Do not offer a fake immediate
  single-route runtime kill switch. Do not infer permission for a paid fallback.

PROFILES FRAME:
- Keep Profiles as its own direct navigation destination. Retain the useful
  master/detail pattern rather than burying it below Models.
- Left list: Daily (5 model entries), Workspace 2 (10, actually applied),
  Workspace 3 (15). Daily is selected for viewing. Selection is not activation.
- Detail header clearly distinguishes “Viewing Daily” from “Applied: Workspace 2”.
  Show default client name gpt-lain-01 and five compact linked model rows.
  Each row can open its model access without a second centered button block.
- Primary action “Apply to proxy…”; secondary Edit. Client setup and Export
  remain separately labeled menu actions. They do not apply the profile.
- Use one clear scrolling detail region. Keep identity and main actions visible.
- A requested Apply preview is a compact review dialog with old/new profile,
  the two synthetic pending saved changes (“gpt-lain-02 disabled” and
  “Daily default changed to gpt-lain-01”), and explicit global scope.
  Explain that the shared proxy restarts and active requests may be interrupted.
  Buttons “Apply to proxy” and Cancel. Do not require confirmation for ordinary
  profile selection, only for this consequential action as in the current flow.
- Editors start at their own header; returning restores the profile-list anchor.
  Preserve all existing default/policy/fallback/export controls in expert detail.

Keep the native dark/mint compact character, exact cat, English copy and one
footer information dropdown. The improvement is scanability and task access,
not an oversized admin dashboard or a reduced expert feature set.
```

## 6. Usage und Live Inspector: Ergebnisse in den Vordergrund

Iststand:
[Usage](../artifacts/ux-review-20261024/pages/matrix/matrix-usage-simple-normal-1120x780.png),
[unvollständige Coverage](../artifacts/ux-review-20261024/interactions/states/17-usage-partial-coverage-top-1120x780.png),
[Live](../artifacts/ux-review-20261024/pages/matrix/matrix-live-simple-normal-1120x780.png),
[Live klein](../artifacts/ux-review-20261024/pages/matrix/matrix-live-barebones-normal-800x560.png).

```text
Using the shared lain foundation, design two related but distinct workspaces:
persistent Usage and ephemeral Live. Default output is Usage at 1120×780,
Simple/Normal. Generate Live only when explicitly requested as the second frame.
Do not merge these into an ambiguous “Activity” dashboard.

USAGE FRAME:
- Compact title and one applied filter context: “18–24 Oct 2026”, Europe/Berlin,
  All accounts, All models. The underlying end boundary is 25 Oct exclusive;
  show this precision in the date editor, not as a paragraph above every table.
- A slim filter toolbar supports date presets, account/model and More filters.
  Advanced filters open locally. Distinguish pending edits from applied filters.
- Immediately show four compact totals from the synthetic source:
  128 client requests; 132 upstream attempts; 4 historical errors (3.0% of
  attempts); 81,986 reported total tokens. Costs are not supplied.
- The first actual breakdown rows must be visible in the initial viewport.
  “Group by: Account” and a compact sort control sit with this one table.
  Use these consistent synthetic rows:
  Personal Codex: 70 requests, 72 attempts, 2 completion errors, 43,200 tokens.
  Command Code Pro: 30 requests, 32 attempts, 1 completion error, 21,000 tokens.
  Kimi Coding: 28 requests, 28 attempts, 1 completion error, 17,786 tokens.
- The historical-error total and completion-error breakdown use different
  definitions in the product. They happen to match in this sample; do not
  silently relabel them as identical measurements.
- Expanding or selecting one group exposes token types, measured performance
  and clear “Filter to account” / “Open account” context actions. Do not repeat
  all top-level totals as another large card stack.
- If displaying whole-range performance, the supplied synthetic measurement is
  First output P50 240 ms, P95 510 ms, 120 of 132 eligible attempts measured.
  Keep sample coverage beside the number. Do not invent costs or latency data.
- If a daily chart is useful, the ONLY supplied series for 18–24 Oct is:
  attempts [14,19,20,23,18,22,16] and total tokens
  [8400,11200,12600,15000,10800,13986,10000]. A compact chart is optional;
  it must not push the breakdown out of the viewport.
- Normal source/freshness is a compact line with local details. For a requested
  incomplete-source variant, show “Coverage incomplete — totals may omit
  activity” above the affected results. Keep values; do not invent a correction.
- Preserve token types, daily totals, attempts/errors, attribution, all filters,
  measurement sources and technical identities through local details/Advanced.
  Provider remaining quota belongs on Accounts, not in these local usage totals.

LIVE FRAME:
- Use Advanced navigation with Live directly reachable and ONE Advanced footer
  dropdown. The output should dominate the workspace rather than the controls.
- Compact top strip: source connection state, search/filter, “Pause display”,
  and Follow output. Refresh and Clear view are secondary menu actions.
- A stable request/group list at left and one readable event/output area at
  right. At 800×560 use a compact selection mechanism rather than two cramped
  permanent panes. Show a second request picker only for a multi-request group.
- Synthetic source: 6 observed streams, retention up to 120 seconds, 0 dropped
  events. Show that this content is ephemeral. Pausing display does not stop
  inference. Clearing the view does not delete persistent Usage.
- Groups: proven Conversation conv-demo-01 (2 requests, 2 streams, Active);
  Request req-demo-02 (1 stream, Active); Request req-demo-03 (1 stream,
  Finished with error); Request req-demo-04 (2 streams, Active).
  Do not infer conversation grouping when only a request ID is known.
- Selected request uses synthetic model gpt-model-01. Output examples:
  “Reviewing the selected module…” then “The configuration is now consistent.”
  A tool event can say “Tool metadata available” without fabricated arguments.
- Make request names/status readable, full IDs available in focused details,
  and technical Event details quiet. Preserve source-loss, dropped-event and
  expired-buffer states when requested. No invented link to a request that
  is no longer in the live buffer and no persistent-chat presentation.

Use the same dark native materials and type hierarchy as Accounts/Models.
No decorative analytics widgets, giant KPI tiles or generic web dashboard shell.
```

## 7. Settings: den bereits ruhigen Bereich gezielt verbessern

Iststand:
[Settings](../artifacts/ux-review-20261024/pages/matrix/matrix-settings-simple-normal-1120x780.png).

```text
Using the shared lain foundation, refine Settings at 1120×780, Simple/Normal.
The current Settings page is already relatively calm. Preserve that restraint;
do not invent a large settings taxonomy, account-management duplicate or fake
preferences solely to fill the window.

Design three concise groups in a readable approximately 720–800 px content
column. Whitespace is acceptable; this is a native utility, not a web dashboard.

1. View density
   Clearly state the scope “Density for Accounts”. A page selector offers
   Accounts, Models, Profiles, Usage, Live. The selected page is Accounts;
   density is Normal with Compact as the alternative. These are independent
   per-page local preferences. A small optional preview may reuse the supplied
   Codex quota row, but must not invent data or become a second account page.
   Explain scope in one short sentence. The same preference can be reached
   through the relevant page’s View menu.

2. Window and notifications
   “Minimize to tray” on; “Close to tray” off;
   “Usage and rate-limit alerts” on.
   Use a concise local tray state. For this fixture, tray is unavailable:
   “Tray unavailable — normal minimize and close behavior is used.”
   Closing or hiding the UI does not stop the independently running host.
   Keep that consequence near the relevant controls, not as repeated global prose.

3. Help and application
   Quiet “Getting started” and “About lain” entries. About holds version,
   repository, licenses and the unchanged cat; do not create fake settings there.

Changes are saved locally; show unobtrusive feedback when relevant, not a
large permanent success banner. Use native checkboxes/dropdowns, consistent
labels and aligned controls. The Settings icon/entry can indicate the current
location; do not falsely highlight Accounts as the current content page.

Exactly ONE Barebones / Simple / Advanced dropdown remains at the bottom-right
of the shared footer. Do not add an “Information level” selector in Settings.
Do not duplicate theme selection, proxy setup, credentials or provider plans.

At a later requested 800×560 size, retain the same groups and reachable controls
with one clear scroll area. Preserve the restrained mint/near-black palette,
clean sans typography, cat branding and standard native window chrome.
```

## Prüfung der späteren Entwürfe

Ein hübscher Screen reicht nicht. Beim späteren UXpilot-Ergebnis insbesondere
folgende Punkte gegen die Ausgangsbilder und Foundation abgleichen:

- Katze, Mint, dunkle native Anmutung erhalten; Typografie konsistent und lesbar.
- Ein Modusdropdown unten rechts, keine doppelte Overview oder Modussteuerung.
- Alle gemeldeten Quota-Fenster geschlossen sichtbar; Unknown, stale, fehlende
  Quelle, reines Guthaben und fälliger Reset fachlich korrekt.
- Detailöffnung erhält den Accountkontext; Commands sind sichtbar auffindbar
  und vollständig auch ohne Rechtsklick erreichbar.
- Profiles bleibt im Gateway direkt zugänglich. Ausgewählt, gespeichert und
  angewendet sind verschieden; Apply bleibt global, Export separat.
- Modelle sind bei 45 Einträgen scanbar; Deaktivieren und Menüsichtbarkeit
  bleiben verschieden; kein erfundener Sofortstopp oder Paid-Fallback.
- Usage zeigt echte Ergebnisse im ersten Viewport; Live bleibt flüchtige
  Ausgabe. Messabdeckung und relevante Quellfehler werden nicht weggestaltet.
- Connect spart Schritte, ohne nicht implementierte Logins oder Quotaquellen
  als verfügbare Produktfunktion auszugeben. Keine erforderlichen Routing-
  Modellentscheidungen vor dem bloßen Verbinden eines Accounts.

Navigation im Simple-Modus, genaue Inspectorbreite und spätere kleinere
Monitorfenster sind vorgeschlagene Varianten. Keine dieser detaillierten
Anweisungen ersetzt eine Nutzerentscheidung für die tatsächliche Umsetzung.
