# Linux component refresh packaging

Use `scripts/stage-component-refresh.py` when CLI, Host and Desktop change
together. The desktop-only helper deliberately refuses changed Host/CLI graphs.
This staging operation does not install software, execute application binaries,
or change services. The existing qualified proxy remains exactly
`8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0`.

First publish each application separately into a fresh directory. Record its
actual source commit and `InformationalVersion`; the already published quota Host
uses source `de872b7` and informational version `1.0.0+de872b7`. Do not rebuild
the proxy. Restore all three current locked application graphs before collecting
notices; if publishing from different source commits, first verify their NuGet
lock graphs agree with the restored graphs. The helper compares each source
commit's lockfile with the checkout and records its hash. It requires an executable
launcher, DLL, deps file and runtime configuration for each component, and checks
that the claimed informational version occurs in its DLL and ends in the supplied
source commit (at least seven characters).

From the integrated checkout, with new absolute output paths:

```sh
python3 scripts/stage-component-refresh.py \
  /absolute/baseline-package /absolute/new/lain-linux-x64-quota-ui \
  --host /tmp/lain-quota-host-publish de872b7 1.0.0+de872b7 \
  --cli /absolute/fresh-cli CLI_COMMIT 1.0.0+CLI_COMMIT \
  --desktop /absolute/fresh-desktop UI_COMMIT 1.0.0+UI_COMMIT
```

The baseline checksum manifest must pass before staging. Output, archive and
archive checksum must not exist or overlap any input or checkout. The helper
copies the baseline and replaces the three whole component directories. It
checks every copied application file and the entire unchanged proxy directory,
rebuilds notices, updates per-component commit/version/assembly hashes, and
preserves inherited backend evidence. Old application evidence remains historical;
`releaseQualified` is always `false` pending current UI and component acceptance.
The helper writes `SHA256SUMS`, a deterministic `.tar.gz` at gzip level 3 and its
SHA-256 sidecar. Failure may leave incomplete staging output; never install it.

Bundled Oxanium and IBM Plex Sans Condensed are embedded resources rather than
NuGet dependencies. Their original `OFL.txt` files must appear in the Desktop
publish under `Assets/Fonts/<family>/OFL.txt`, byte-identical to the checkout.
The notice collector also copies each family license into `notices/fonts/`,
records font and notice hashes in `inventory.json` under `bundledFonts`, and
retains the original copyright and reserved-font-name wording.

Before any subsequent installation, independently check staged `SHA256SUMS`, the
exact proxy hash and per-component provenance. A changed Host requires its own
controlled restart procedure; this staging helper performs no installation or
restart. See [the Linux installation guide](install.md) for the existing package
installation and rollback procedure. Keep the current UI acceptance gate open.
