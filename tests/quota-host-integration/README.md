# Host quota integration fixtures

Run `dotnet run --project tests/quota-host-integration` in the coordinated build slot.
All state, browser operations, HTTP responses and credentials are synthetic; no browser process,
provider service, installed state or real access material is used.

Coverage: local MiMo method availability, explicit quota-only login, reader reuse without browser
startup, late publication conflict and recovery, monitor/account removal, detached session retries,
same-profile generation changes, generic cleanup callbacks, Groq project-bound Enterprise reads
without Core, passive behavior without opt-in, and dated NVIDIA guidance without measured values.

Source prepared on 25 October 2026; runtime validation remains with the coordinated integration run.
