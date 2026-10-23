# Development and verification

The pinned SDK is .NET 10.0.111. Restore uses committed NuGet lockfiles. The
Go proxy has its own verified source snapshot and build script; it never uses
the installed personal proxy binary.

```sh
dotnet restore Lain.sln --locked-mode -r linux-x64 -p:SelfContained=true -m:1
scripts/verify.sh
```

Serial builds avoid concurrent compiler memory pressure. Synthetic process
probes additionally require loopback sockets and explicit own binary paths:

```sh
scripts/verify.sh --core /absolute/lain-proxy --host /absolute/Lain.Host
```

No verification command above calls real providers. The separately documented
`tests/live` harness performs the bounded authorized reference probes only
with an explicit execution flag and matching synthetic qualification SHA.
A provisional `syntheticPassed` marker is accepted by Host only with
`LAIN_REFERENCE_QUALIFICATION=1` and private state below the temporary directory.
Normal installations require a final exact-SHA `passed` marker.

Host, inference and Core management credentials are three different secrets.
Do not print private state, config snapshots, upstream error bodies or tokens
in test reports. Synthetic credentials must stay synthetic. The baseline
isolation guard compares the existing service's stable files privately;
transient `.tmp` churn is reported separately.

For the integrated Inspector, analysis and provider expansion, use
[`scripts/verify-expansion.sh`](../scripts/verify-expansion.sh). The
[expansion verification guide](verify-expansion.md) lists its serial suites,
optional explicit artifact probes, Linux namespace isolation for fixed Host
ports, retained evidence and separate mandatory private-Xvfb GUI runner. It does
not rerun the original packaging/account/model/quota suites automatically or
treat synthetic success as live-provider or release qualification.
