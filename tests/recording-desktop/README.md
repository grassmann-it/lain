# Recording production view fixture

This executable opens the real `RecordingView` and real `LainApiClient` against an
in-memory synthetic HTTP handler. It cannot attach to an inherited user desktop:
the entry point requires a live private Xvfb PID and matching display marker.
The runner strips desktop variables before building and creates its own X server.
No provider request, installed host, user credentials or SQLite/archive files are
used by this fixture.

Run only in the integrator's serial build/UI lane:

```sh
tests/recording-desktop/run-private-xvfb.sh /absolute/private/evidence-directory
```

The fixture records `evidence.json` and 1120×780 / 800×560 screenshots. Its measured
latency and process working set describe only this fixture and invocation, not a
production capture or provider benchmark. It checks explicit default off, metadata
pagination, selected readable tool/message content, 8192/4096 text limits, lazy
source/worker details, model/coverage markers, narrow Back navigation, close/reopen,
source outage and source deletion invalidating content/copy surfaces.

Still requires separate qualification: real store retention and restart durability,
large selected-history paging/scroll stress, actual clipboard selection and X11
keyboard input, and stale-response timing. The shared v1 API currently pages
normalized records with embedded messages; it does not expose separate worker,
request, attempt and payload page endpoints. The UI labels that limitation instead
of implying raw-wire coverage. User labels use the dedicated runs API and are
always `configured`; no training-rights state prevents archive reading.

Integration: instantiate `new Lain.Desktop.Recording.RecordingView(client)` in a
bounded workspace viewport, preferably alongside Live. It implements
`IPresentationView`. No constructor call or view navigation enables capture. Keep
the Saved route accessible when live-source monitoring is unavailable.
