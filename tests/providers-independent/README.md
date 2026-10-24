# Independent provider acceptance

Owned by the independent review worker. Synthetic credentials and temporary state
only; no installed service, personal data, sockets, or provider requests. This is
behavioral acceptance of the shared contract, not a live qualification claim.

Run only in Main's serialized build/test slot:

```sh
dotnet run --project tests/providers-independent/Independent.Tests.csproj -p:BuildInParallel=false
```

`host_probe.py` independently exercises the actual packaged Host over authenticated
loopback HTTP. Run it only in Main's serialized slot inside a private user/network
namespace with loopback enabled. It refuses any non-loopback network interface.

```sh
python3 tests/providers-independent/host_probe.py --host /absolute/Lain.Host.dll --proxy /absolute/proxy/lain-proxy --report /absolute/host-report.json
```

The probe uses temporary synthetic state, disables background quota polling with
the explicit reference flags, and never starts/applies Core, refreshes quotas or
starts OAuth. It checks 20 providers, connection-only and selected-model setup,
invalid/revision atomicity, monitor GET/PUT/DELETE authorization and separation,
manual Google token snapshots, and 8 default routes from a no-alias Codex snapshot
import using the catalog actually adjacent to the packaged Core. JSON evidence
contains artifact hashes and fixed check labels, never tokens or response bodies.

The executable checks distinct gateway/monitor projections, passive observation
age without gateway credential access, model/window identity during partial
refreshes, metadata/unknown preservation, neutral missing capability, sanitized
real failures, connection-only setup and reuse for two model routes, conflict
atomicity, invalid model selection, stable account identity, and no auto-apply.

Pending integrated review supplements (not claimed by these fixtures):

- Actual Groq Core observation ingress through Host adapter; account/request/model
  mismatch, 429, duplicate headers, stale event, and zero inference during refresh.
- Fireworks pagination/wrong-account/unit/updateTime; Gemini project/model scope;
  Cloudflare coverage/usage-only; NVIDIA official-source boundary.
- Monitor API storage/expiry/removal, wrong-resource rejection and no inference-key
  fallback for a configured separate monitor. Imported snapshots never refresh or
  revoke. Manual tokens and external access snapshots are supported setup paths;
  unqualified owned OAuth is a separate capability, not an unsupported-provider label.
- UI and CLI: an unverified stored credential is neutral, distinct from an actual
  authentication failure, missing credential, or expired credential. Unsupported
  quota capabilities produce guidance without warning cascades. Check custom names,
  original data freshness, spend-cap labels and absence of fabricated percentages.
- Fresh installed Codex onboarding catalog artifact and Main's private live evidence.

Contract pin for initial authorship: `7e9837a`. Source review at `aefcd78` added
old-key re-add after credential replacement and headless presentation predicates
(unverified versus actual failure; successful empty Monitoring response versus
error and last-good data). These checks load presentation types but create no UI,
window, service or network client. No build or run has been performed at this stage.
