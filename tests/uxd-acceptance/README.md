# UXD-09 independent product acceptance

Run only in the integrator's serialized build/runtime slot:

```sh
./tests/uxd-acceptance/run-private-xvfb.sh /tmp/lain-uxd-acceptance
# Only after the matching integrated harness has been built:
LAIN_DESKTOP_SKIP_BUILD=1 ./tests/uxd-acceptance/run-private-xvfb.sh /tmp/lain-uxd-acceptance
```

The project references the actual desktop assembly and production theme.
`Fixture.cs` is linked from the existing desktop harness: five accounts with
every quota-window kind, 45 model entries, three profiles with 5/10/15 routes,
independent applied Workspace 2, known/unknown Usage and evidenced/unproven Live
groups. The UXD variation disables model 02, hides enabled model 04, gives model
45 a long alias and introduces global pending configuration. It does not infer
a historical per-model delta from the global revision.

The runner unsets inherited DISPLAY/WAYLAND_DISPLAY/session D-Bus, creates an
owned Xvfb with Xauthority and isolated HOME/XDG/LAIN_HOME, and refuses native
startup without its PID/display marker. Only allowlisted in-memory HTTP is
used. No real host, provider, credential discovery, browser or inference occurs.

Requirements: .NET 10, Xvfb, xauth, timeout, `/usr/bin/import`, libX11.so.6 and
libXtst.so.6. XTEST provides native pointer and keyboard events in private Xvfb.
Native root captures include popup menus and modal windows. Page captures use
Avalonia's renderer. There is no user-display/session-bus fallback.

The matrix covers Accounts, Models, Profiles, Usage, Live and Settings at
1120×780 and 800×560 with Barebones/Simple/Advanced and Normal/Compact. Settings
has no independent density; its matrix variations retain the chosen page
densities. Targeted cases cover account geometry and independent scrolls,
Settings/Back, narrow detail, native search/Tab/Enter/Escape, context-menu parity,
long aliases, profile selection/Apply review, partial Usage, transient Live,
local preference scope and Monitor capabilities versus offline Gateway.

`evidence.json` separates `functionalResult`, `visualResult` and
`accessibilityResult`. It records actual viewport, information level, density,
runtime intent, fixture, case/action, assertions, assembly SHA-256, checkout
commit, HTTP history, focus trace and images. Every generated image starts at
`visualReview: REQUIRED`. Assertions never mark PNGs visually accepted.
[review.md](review.md) maps the 14 final references and explicit remaining cases.

The older broad desktop suite retains historical Expander-specific account
expectations. Its navigation contract now uses the capability/mode matrix, but
it must not be reported as rerun because this new suite passes. AC/ST/backend
qualifications remain separate.

Not established: screenreader/complete accessibility, clipboard, real OAuth or
provider entitlements, installed tray/window-manager behavior, release readiness.
