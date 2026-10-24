# UXD-08 Settings implementation handoff

Source base: `ec68062`, 24 October 2026. Product ownership is restricted to
`src/Lain.Desktop/MainWindow.Settings.cs`. Reference: final frame 14
`O63jRSAvsTNvo54maqIS`, reviewed from its actual PNG. Original branding, native
Avalonia/Semi/Ursa controls, fonts and assets are retained.

Settings now has three compact native groups: View density, Window and
notifications, Help and application. Density labels track the selected origin
page and continue through `SavePreferences` and `SynchronizeSettings`. Checkbox
labels/descriptions are part of their activation area; all three independent
flags still use the same preference storage and rollback path. A missing tray
explains normal minimize/close behavior and retained preferences; host lifetime
is explicitly separate. Existing threshold behavior is unchanged; the baseline
has no persisted threshold setting to expose.

Help keeps Getting started/About and adds a deliberate Host operating mode
entry. Monitor/Gateway is a distinct host configuration review, with current or
last-known mode text, no ambiguous second dropdown, captured configuration
revision, explicit interruption warning and confirmation. It refuses unavailable
or changed configuration; conflicts never retry using a newer revision. Local
preferences make no Host API mutation. The only information-level dropdown
remains in the shell footer.

About shows actual Desktop assembly informational version independently from
version reported by the currently connected Host. Offline host identity remains
unavailable. Existing repository action, undeclared application-license status,
third-party notices and original cat source are retained. Long values wrap and
page widths stretch within a maximum; narrow Settings uses the shared shell's
normal scroll contract. There is no new fixed-height page or nested scroll area.

New scoped fixture: `tests/desktop-settings`. It uses private Xvfb and synthetic
state/in-memory HTTP, checks wide/narrow layouts and all independent flags,
persisted density, tray descriptions, About, cancelled host review and actual
storage-error rollback. Its source is ready; build, runtime screenshots and
visual acceptance remain **not_run** pending root's serialized slot. The test
fake tray is not a real X11/Wayland/tray result. Existing shell Back/history,
selection/draft/scroll behavior is reused unchanged and remains subject to
integrated UXD-09 checks.

Integration with shell owner was confirmed: existing `SavePreferences` hooks,
three tray flags and natural Settings outer scroll remain supported. No shared
shell, preference, tray or API-client file is modified by this package.
