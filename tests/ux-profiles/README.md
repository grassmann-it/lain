# UXD05 profile workspace qualification

Source prepared against `ec68062` and reviewed against final UX-Pilot frames10/11.
Only `MainWindow.Profiles.cs` is owned by this package. Shared dependencies are
`Ui.Viewport(Control)` from the shell owner and the root-owned Desktop compile link
to the existing ST02 `CodexArtifactInstaller.cs`. There is no second installer.

Implemented: compact independent inventory/detail scrolling, search, Viewing versus
reported Applied identity/revision, direct model rows, local narrow Back, native
owned Apply dialog, complete policy/fallback selection and ordering, separate saved
profile export, read-only Codex preparation and explicit client file installation.
Legacy unknown identity stays unknown. Disabled/hidden defaults are explained.

Apply uses GET `/profiles/{id}/preview?revision=…`, re-reads `/configuration` before
POST `/profiles/{id}/apply`, refreshes conflicts and requires a fresh confirmation.
Success requires `/status` to confirm profile/revision. Failed/unknown responses do
not become active success. The dialog shows the host's exact stream behavior and
recovery action; it does not invent a local runtime diff.

Focused fixture: build `UxProfiles.Tests.csproj` for linux-x64 in an authorized build
slot, then run `run-private-xvfb.sh /tmp/private-evidence-dir`. It reuses the existing
synthetic desktop HTTP fixture, adds actual preview contracts, and records wide,
narrow, modal and installed-client screenshots. Routed native control events test
view/apply separation, cancel focus, stale revision refresh, double click, narrow
scroll/back, draft preservation, full policy save, no-write export preparation and
explicit generation-safe installation into the private output directory. No real
host, providers or ordinary Codex config is used.

Status: **not_run**. Consolidated build and private Xvfb runtime await the root slot.
The UXD09 owner separately owns integrated keyboard/pointer and cross-page visual
acceptance; these sources do not claim screenshot or runtime success yet.
