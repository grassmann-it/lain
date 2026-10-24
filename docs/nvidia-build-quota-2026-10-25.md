# NVIDIA Build reporting boundary

Implementation basis: main `9b1cd4b`; public evidence from 2026-10-24, reviewed
again locally on 2026-10-25. Fresh public HTTP verification failed because DNS was
unavailable; web search and browser access were unavailable as well. No account,
credential store, existing browser session, login or inference call was read/run.
This document does not claim fresh live endpoint validation.

`NvidiaBuildQuotaAdapter` makes one fixed anonymous GET to the official public
[`content/copy.yaml`](https://build.nvidia.com/api/runtime/content/copy.yaml)
runtime document. Despite its suffix it returns JSON. The owned HTTP client has
no cookies, no default authentication, and no redirects; response size is bounded
to 32 KiB and time to 20 seconds. Its current source strings are `Up to 40 rpm`
and `10,000 requests per day`. The first keeps the provider's qualification.
The official detail text says limits vary by model and other-user traffic can
throttle requests. These are product reference values, never a hard account
limit, observed usage, remaining credits or a reset. No numeric `QuotaReading`
is emitted. Failure retains the concrete setup prerequisite with sanitized text.

The reader deliberately does not interpret an arbitrary `remainingCredits`
property as an account balance. No official Build remaining-credit or usage
endpoint, response schema, account scope or reset semantics has been established.
The cached official full Build chunks inspected here expose static rate-limit
copy, API-key inventory/provisioning, global artifact invocation/download counts,
and individual inference-response usage. The latter two are not account quota.
See the full [MiMo/NVIDIA research](quota-auth-research/mimo-nvidia.md).

`NvidiaBuildConnection.Method()` describes the precise `setup-required` reporting
connection on the existing account. It does not ask for a second API key or claim
a working OAuth/device flow. The official NGC CLI requires an existing key via
`ngc config set`; its use of `$oauthtoken` is API-key authentication. Build's own
website login uses the `api-catalog` application and provisions a key with
`invoke_function`; it does not establish a public lain application identity or
monitor grant. No borrowed app identity or key-creation mutation is implemented.

The unavoidable next evidence is specific: NVIDIA must supply a supported
third-party owned-session/callback handoff and a read-only Build reporting route,
or an explicitly authorized isolated console run must establish those contracts.
That read must bind the console subject/org to the existing inference account,
identify the Build product, and report units/window/reset semantics. An inference
key, generic NGC org membership, enterprise NVCF metrics or local request history
cannot establish this binding. Until that evidence exists, saving a monitor
credential or showing a successful quota login would make a false promise.

Host integration: register `NvidiaBuildQuotaAdapter` as `IQuotaAdapter` (container
owns disposal), and include `NvidiaBuildConnection.Method()` in the connection
catalog. It needs no secret-store or Core DTO change. Fixture checks are in
`tests/nvidia-quota`; no builds or live requests were run by this worker.
