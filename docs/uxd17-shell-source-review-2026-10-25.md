# UXD-17 shell source handoff, 25 October 2026

Basis: `c3db87e`. User choices are **Draft B** (direct navigation with persistent
return/discard notice) and **Simple Live A** (direct Live destination). This is
source review and a synthetic input matrix, not a runtime or release approval.
No build, GUI, provider, credential, inference or service action was performed.

## Existing behavior retained

- `SaveCurrentPage` stores the mounted editor, its existing actions, login state,
  base revision, rebuild callback and focused control in the existing one-entry-
  per-section `PageDraft` dictionary. Section scroll anchors remain separate.
- `NavigateToAsync` restores that same mount and posts focus/scroll restoration.
  Navigating and changing information level/density do not call configuration
  writes or Apply. `ApplyPresentation` already leaves editors and Live mounted.
- `UpdateDraftStatus` and explicit `ReviewDraft` already detect changed revisions
  and rebuild through `EditorSnapshot`, naming unmatched fields. Save closures
  retain their captured original revision; no implicit revision rebasing is added.
- The original logo, mint theme, native controls, separate Monitor/Gateway intent,
  Settings/Back and single bottom-right information selector are retained.

## Implemented deltas

- A separate shell notice region is outside the workspace scroll viewer and
  ordinary dismissible messages. Each retained section has object identity,
  Continue editing and an explicit Discard draft. Discard only removes that
  section entry, performs no host request, and restores focus to current nav.
- Existing `RegisterEditor` accepts optional object identity. Page owners pass
  Models alias or Profiles name; legacy editors fall back to their mounted title.
  Stale retained notices name old/new revision. Polls update text/buttons in
  place so even a revision update preserves notice-button focus.
- Explicit revision review removes the obsolete mount from `_drafts` after
  successful input restoration. It remains the active editor and is captured
  normally when leaving; this prevents a duplicate notice for the old mount.
- Simple directly exposes Live; the existing menu builder automatically omits
  direct destinations from More. Barebones and capability gates are unchanged.

## Shared integration contracts

UXD-18 owns `_accountQuotaRefreshes`, `_accountQuotaRefreshGeneration`,
`RefreshAccountQuotaAsync`, `IsAccountQuotaRefreshing`,
`AccountQuotaRefreshStatus`, and mounted button/status observers. Shell now owns
`_configurationError`: pending first load is null; failed config/first load gets
a real error, successful config clears it. Full Quotas reads skip local refreshes
and only publish if their captured generation still matches, preventing an old
full snapshot from overwriting a later local response.

`AccountCommands` uses the local per-ID refresh instead of global `RunAsync`;
execution rechecks target-local busy state. Mutating commands retain existing
host/revision/global busy checks. Dynamic account menus repopulate availability
at opening while command identity and revision stay frozen. The inspector shows
an honest missing-quota window and keeps its local busy/error row outside its
scrolling detail. Accounts owner marks incremental changes in `_renderedPageState`
to avoid a generation-only poll rebuilding another account's mounted detail.

UXD-21 owns `_profileUnconfirmedApplyId`, `_profileUnconfirmedApplyRevision`,
`ProfileApplyOutcomeUnknown`, `ProfileConfirmedStatus`, and
`RefreshProfileReviewState`. Shell uses the captured confirmed status for
**Last confirmed** during unknown Apply outcomes and displays the requested
profile/revision separately, including when a subsequent host read fails.
Unknown outcomes show a warning instead of a Proxy-ready/Active success summary.
Every `UpdateServiceSummary` also invalidates a mounted profile review through
the owner callback, so a focused review cannot keep an enabled stale Apply.

## Verification handoff

`tests/desktop/fixtures/uxd17-navigation-drafts.json` defines the local input and
request assertions at both agreed sizes. UXD-22 owns native execution and central
Program/Fixture edits. Stable automation selectors are:

- `Retained local draft Models · gpt-lain-01` (notice border)
- `Return to Models · gpt-lain-01 draft`
- `Discard Models · gpt-lain-01 draft`
- Profiles equivalents use `Profiles · Daily`.

The banner disappears while its own draft is mounted. Draft B introduces no
modal dialog; Escape keeps the existing menu/inspector behavior. The fixture
must verify no save/apply request across navigation, Settings, presentation,
return and discard, plus original-revision conflict handling separately.

Static `git diff --check` and JSON parsing passed. Compilation/native verification
remain with Root's serial capped slot, after Accounts and Profiles contracts have
been integrated; this shell commit intentionally references those owner helpers.
