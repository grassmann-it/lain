# Cloud connection local fixtures

Run under the integrator's serialized build slot:

```sh
dotnet run --project tests/cloud-connections/Cloud.Connections.Tests.csproj -r linux-x64 --disable-build-servers -p:RestoreLockedMode=true
```

At initial source commit: not run. All remote provider replies are synthetic.
Only the OAuth callback test opens a temporary IPv4 loopback listener and sends
local fixture requests; it disposes listeners/clients on completion. No browser,
real provider, credentials, installed state or live account is used.

The package depends on the shared rotating-owner/projection and recoverable
empty-purpose generation changes for integrated host publication. The direct
adapter fixtures use a checkpoint fake; they do not claim to retest that lifecycle.
