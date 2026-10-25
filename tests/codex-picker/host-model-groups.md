# Actual mixed-package group integration

This source-only harness extends the package staging pattern from
`tests/acceptance/package_probe.py` without changing that broad acceptance gate.
It qualifies the exact supplied Host/CLI/Core combination. Desktop is never
started; its bytes and the complete package checksum manifest are recorded and
verified after staging.

Preparation only (starts no process or provider):

```sh
python3 tests/codex-picker/host_model_groups.py \
  --package /absolute/path/to/mixed-package \
  --output /absolute/path/to/new/prepared-evidence
```

To run the complete isolated lane, use a **new** evidence directory and add
`--run`. To independently check the retained Desktop executable against a known
baseline, also pass `--expected-desktop-sha256 HASH`. The script does not build
or install anything outside its private filesystem mounts.

The outer process records package metadata, the exact `SHA256SUMS`, and SHA256
for Host, CLI, Core and Desktop. Bubblewrap creates separate PID/network
namespaces and mounts a synthetic home at the **original, unchanged HOME path**.
No HOME/CODEX_HOME assignment or override occurs. The child environment is an
allowlist, with task-specific XDG, LAIN_HOME, desktop and unit directories.
The package itself is mounted read-only. Its own installer writes only to the
synthetic home and retained evidence mount; no systemd command is run. Network
access is limited to the isolated namespace's loopback fixture.

The real staged Host API creates two fake-key loopback accounts, two strict
routes, one profile, two explicitly ordered collections and collection/account
routers using revision CAS. Exactly two readable `account:ID` groups must come
first, each containing only that strict account's alias. Custom collections
follow in saved order even when their names sort differently; IT Sec keeps its
name, member order and router. The test checks the staged typed CLI's router view, real
Host grouped export, and `profiles install-codex --model-groups` output. It also
verifies that ordinary export omits the extension and virtual router model.

Profile application must start the **exact staged Core**: the harness checks
the Host child's `/proc/PID/exe` path and executable hash. It then sends one
Responses request, stops/restarts the staged Host, verifies the unchanged
durable pin and sends a second full-history turn with the same Thread-Id. Both
must hit the collection's first real account. A third request through the explicit
account router must hit the other selected account and create its separate pin.
Host usage and staged CLI history
must agree; RequestedModel keeps the router alias while account/route/model
fields identify the real upstream. Finally a stale router CAS must return 409
without mutation, and the main Codex config sentinel must remain byte-identical.

`result.json`, `summary.json`, Host/installer logs, synthetic installed state,
export artifacts and package manifest remain in the private output directory
on failure. Synthetic state includes fixture credentials and generated local
tokens; no real account state is copied. The summary only passes after outer
verification of the inner exit status, exact staged hashes and sentinel hash.

This does not qualify native picker rendering, Free routing, live providers,
or Desktop behavior. Those gates remain separate. A prepared artifact is not a
successful run; only a passing summary from the exact candidate package is
runtime evidence.
