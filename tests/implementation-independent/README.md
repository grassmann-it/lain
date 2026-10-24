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
grant quarantine; quota-only core credential projection; replay against a literal
historical canonical journal digest.

Each scenario reports only its name and exception type. A passing source test
does not qualify exact packaged binaries, real providers, Core startup on restored
state, operation crash barriers, or complete AC-10 delivery. Tests are `not_run`
until an integrator slot is explicitly assigned.
