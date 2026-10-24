# Independent implementation checks

These adversarial checks were authored by the independent reviewer after source
review, rather than copied from implementation worker tests. Only temporary
synthetic state and in-memory HTTP handlers are used. No provider, installed
service, user credentials or screen is accessed.

Run only in the integrator's serialized build/runtime slot against the integrated
product checkout (or omit ProductRoot once these files are integrated):

```sh
dotnet run --project tests/implementation-independent/Independent.Tests.csproj -p:ProductRoot=/absolute/integrated/checkout
```

Coverage: bound Requesty identity failure; unverified reconnect of a verified
identity; independent monitoring preservation during inference replacement;
crash after durable remote credential before recovery pointer; restored rotating
grant quarantine; quota-only core credential projection; absent explicit handle
fail-closed behavior; failed superseding-generation persistence retry; replay
against a literal historical canonical journal digest; contradictory legacy
profile metadata versus an honestly unknown legacy identity.

`monitor_host_probe.py /absolute/Lain.Host.dll` separately launches the exact
already-built Host in an isolated temporary Monitor state and random loopback
port. It verifies absent Core, malformed optional Poe registration, restored
grant quarantine, unavailable-method rejection and history. Automatic quota
reads are explicitly disabled; no provider request occurs. Run only in Root's
serialized runtime slot. The C# suite additionally checks a new Codex snapshot
against an in-memory HTTP quota response, including account identity header.

Each scenario reports only its name and exception type. A passing source test
does not qualify exact packaged binaries, real providers, Core startup on restored
state, operation crash barriers, or complete AC-10 delivery.

On 24 October 2026 Root executed the suite in its serialized integration slot:
12/12 scenarios passed, and the separate restored Monitor process probe passed
with zero provider requests. The independent reviewer inspected both output logs
and `followup-results.json` under `/tmp/lain-integrated-20261024/`. Both checks
identify the tested Host assembly as SHA256
`4d4df715d74ba413f633243be47ab4950aee8908221ed2eb0992ccfc73896bc1`.
This records integrator execution of independently authored tests, not a separate
reviewer execution or qualification of subsequently changed artifacts.
