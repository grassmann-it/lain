# Poe / OpenCode Go preparation checks

Run `bash tests/providers-poe-opencode/run.sh` in the integrator's coordinated
build slot. The isolated executable uses fake HTTP and no credentials.

Covers official Poe point balance and cursor history shapes (microsecond
timestamps), unknown/malformed values, credential-origin qualification, publisher
provenance, OAuth PKCE/state/expiry parsing, and the four distinct OpenCode Go
protocol families. It never calls a paid inference endpoint or creates an account.

Host registration, callback ownership/key persistence, backend route wiring and
real connections are not qualified by these tests.
