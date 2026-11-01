# STAB-10 · Brand interaction and discoverable About

Planning status: ready to implement against current Desktop after coordination;
left-click About and right-click menu are recommended interaction choices within
the user's accepted request for an About/menu action on the cat. No code change
or native qualification performed by this package preparation.
Owner: Desktop chrome/settings owner.
Dependencies: STAB-00 baseline; share MainWindow edits with STAB-02/03 owners.
Final version identity from STAB-08, not a new hardcoded release value.

## User outcome

Clicking the header cat should open About/information or an appropriate context
menu. Version and application information should be easy to find, with MeowChan
as a concrete design reference. Preserve the original cat, current brand style
and meaningful distinction between UI, Host and optional Gateway components.

## Existing evidence

Source9d5fbdc plus WIP: MainWindow.cs constructs the header Image and makes its
parent titleArea a drag/maximize surface. MainWindow.Settings.cs already has
AboutPage, assembly informational version, independently reported Host version,
repository/support and license/photo attribution. Reuse it; do not build a second
conflicting About data source.

Read-only MeowChan reference in the local sibling project: Desktop/Shell/
MainWindow.BrandNavigation.cs uses a named accessible BrandAboutButton;
Desktop/Views/About/AboutWindow.axaml shows selectable version/copy and About,
Credits and Licenses tabs. Reuse interaction/layout principles, not an unreviewed
copy of its update subsystem, branding or product-specific links.

## Proposed behavior and ownership

1. Cat/brand is an actual focusable button with About tooltip/accessibility name.
   Left click or Enter/Space opens the existing About destination; right-click or
   context-menu key offers About, Settings and Copy version info. Final tiny menu
   may be simplified; no essential operation is right-click-only.
2. Isolate the button from title drag/double-click handling. Remaining titlebar
   still moves/maximizes the window. Double-clicking the cat does not invoke an
   unrelated maximize action or multiple About windows.
3. Opening About preserves current editor drafts and supports returning to the
   prior workspace. Reuse existing navigation/draft semantics; a lightweight
   owned About window is an alternative only if it simplifies this behavior.
4. Present a compact product/actual Desktop version header, selectable/copyable.
   Group About, components/technical info, and credits/licenses using the existing
   theme. Tabs are a recommendation, not a requirement to add many screens.
5. Show actual Host version/status separately. Core version/build/provenance only
   when reported by a verified existing source; Monitor has no installed-Core
   requirement. Missing/offline information remains unavailable or clearly dated.
6. Copy a bounded nonsecret version summary (app/OS/known component versions);
   never include credentials, account emails, private paths or raw diagnostics.
   App version works offline. Copy-version is not an exported support archive.
7. Reuse project/support/license attribution links already maintained. Link actions
   open only on deliberate click. Do not introduce update checks/downloads or
   remote telemetry simply by opening About.

Code boundary: Desktop header/Settings/About and focused tests. No Host version
schema rewrite or production installer/version changes needed for click behavior.
English product labels, existing Barebones dropdown stays in its agreed location.

## Acceptance — not executed

- Pointer and keyboard open About from cat; context menu is accessible.
- Clicking brand does not drag/maximize; free titlebar still does.
- Existing draft is retained on open/return; repeated click has predictable focus.
- App/version visible with Host offline and in Monitor without Core.
- App and Host version mismatch is shown honestly rather than overwritten.
- Copy action contains the advertised known versions and no sensitive identifiers.
- About/credits/licenses usable at800×560 and1120×780 under private Xvfb;
  contrast, focus, scroll and links checked. No existing screenshot implies this pass.

Return changed paths, actual focused checks and native evidence as in
[the package index](README.md). No launch/reload of the user's production UI.
