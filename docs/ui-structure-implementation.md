# UI structure implementation · 24 October 2026

The user authorized the preceding architecture-sparring design with “baue bis
fertig” and requested eight Sol workers. This is the implementation scope for
the current goal. Existing mint/dark styling and fonts remain; the user-supplied
cat image becomes the application/window/launcher logo. No new provider calls
or legacy proxy operations are needed.

## Required outcome

1. Exactly five primary destinations: Accounts (initial page), Models, Profiles,
   Usage, Live. Profiles stays directly accessible for frequent switching.
2. One Account inventory, expandable tiles with all named quota windows and
   percentages/remaining/reset values. Correct unknown/stale/shared states.
3. Search, explicit filters, persistent pins/reorder, stable provider/name order,
   optional grouping. Polling must not reorder accounts based on available data.
4. Discover providers inside Accounts, reachable without a credential. One
   Add account entry chooses provider/auth method; manual API and import remain.
5. Barebones/Simple/Advanced presentation presets, Simple default, plus independent
   Normal/Compact density per main area. Preferences survive restart and are
   separate from configuration revisions. Same functions and five destinations
   remain reachable; local details reveal technical fields in every preset.
6. Models is one configured model-access inventory with clear labels; the derived
   model list is assistance when adding, not a second pseudo-discovery inventory.
7. Profiles distinguishes selected/saved/applied state, global proxy application
   and Codex client export. Expose reliable applied profile metadata from Host.
8. Usage has one main configurable breakdown instead of repeated account/model
   tables. Preserve whole-window totals, history, filters and performance coverage.
9. Live is the Inspector: readable output first, single-request picker hidden,
   coherent empty/filter states and unchanged bounded retention/provenance.
10. Context links Account/Model → Usage and Usage/Live → Account/Model preserve
    navigation/filter context. No fabricated Live history or relationships.
11. Global service/pending status and reachable proxy actions replace Overview's
    service section. Setup becomes contextual, reachable help.
12. Refresh and presentation changes preserve unsaved inputs and focused controls.
13. New user cat image used unchanged as source, scaled by UI; app/window/Linux
    launcher icon included in package. Preserve origin and third-party notices.
14. Meaningful tests and visual verification at 1120×780 and 800×560 on private
    Xvfb only; installed payload checked against package; owned data retained.

## Parallel boundaries

Integrator owns MainWindow.cs, MainWindow.Overview.cs, Ui.cs, shared contracts,
Host/client applied-profile metadata, integration, serialized builds and install.
Workers own: preferences; Accounts/tiles/quota; providers/login; Models/Profiles;
Usage; Live; desktop test harness; branding/distribution/review. All edits use
isolated worktrees. No worker starts builds/tests/services without the integrator's
build slot. No subagents; eight requested workers plus integrator are sufficient.

## Shared UI hooks

Types in `Lain.Desktop.Presentation/ViewOptions.cs` are the shared presentation
contract. View owners add `SetPresentation(PresentationOptions)` and optional
`Action<WorkspaceNavigationRequest>? NavigateRequested` callbacks.

Integrator provides these MainWindow partial members:

- `WorkspacePreferences _preferences` reads current local preferences.
- `PresentationOptions PresentationFor(string section)` uses primary labels.
- `void SavePreferences(Func<WorkspacePreferences,WorkspacePreferences> change,
  bool render = true)` saves UI preferences only.
- `void NavigateTo(WorkspaceNavigationRequest target)` routes with history.
- `void ShowProviderCatalog()` and provider worker's `ShowAddAccountChooser()`.
- `void ShowUsageForAccount(string id)` / `ShowModelsForAccount(string id)`.

Usage owner provides `Task FilterToAccountAsync(string id)` and
`Task FilterToRouteAsync(string id)` for contextual filters; Models owner provides
`void ApplyModelsNavigation(WorkspaceNavigationRequest target)` to set local
route/account filters before render. Existing constructor signatures stay valid.

Preferences worker implements `WorkspacePreferencesStore(string? path = null)`
with `Current`, `Error`, and `bool Save(WorkspacePreferences preferences)`;
atomic writes, validation, invalid-file fallback and separate storage are required.

Do not alter Core transport, durable Usage semantics, provider credentials,
source import files, normal Codex configuration or pending applied revisions.
