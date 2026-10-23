# NVIDIA / Step synthetic checks

Run `tests/providers-nvidia-step/run.sh` after the integrator authorizes the next
serialized build. This creates a disposable .NET test project and performs only
in-memory HTTP fixture requests. No real keys, account creation, purchase,
inference or provider connectivity checks occur.

The tests cover PAYG balance normalization, zero/unknown values, secret-free
errors, fixed read-only endpoint authentication, rejection of Step Plan and custom
gateway endpoints, distinct paid/free classification, endpoint/protocol/plan
validation, and honest `not_tested` catalog state. They do not verify real access,
upstream streaming, tool behavior, reasoning deltas, performance or account limits.

The core test `TestOpenAICompatExecutorNvidiaStepDocumentedStreamFixture` also
uses only a local httptest server. It checks NVIDIA/PAYG/Plan path retention,
Bearer authentication, reasoning/tool continuation fields and streamed reasoning,
content and fragmented tool-call arguments through the existing compatibility
executor. Run it with the integrator's serialized Go test selection.
