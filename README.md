# lain — Account Monitor and Local AI Gateway

lain manages local provider accounts, quotas, and stored usage data for Linux
x64. Desktop and CLI share the same private .NET 10 Host. The Host can keep
running independently of the window.

First-party code: [Artistic License 2.0](LICENSE), with separate
[third-party and image notices](NOTICE). Cat photo:
[Alexander Huxel · Webentwicklung Huxel](https://webentwicklung-huxel.de/)
[(Flickr)](https://flickr.com/people/204852984@N05/).

The current **source** implements two explicit operating modes:
**Monitor** reads qualified account quotas and existing local history without
an installed or running proxy Core. **Gateway** adds inference, models, profiles,
and Lain's pinned CLIProxyAPI Core. A proxy failure does not automatically switch
a Gateway to Monitor. Information level (Barebones / Simple / Advanced), local
density, and operating mode are separate settings.

**Source state, verified packages, and the installed service are separate evidence.**
The integrated account, stabilization, and UXD changes are authorized by the
implementation task. This does not make them a newly installed or approved
release. Historical installation reports and screenshots apply only to their
recorded binary hashes; old check counts do not prove the current state.
Private Monitor/Gateway package acceptance and a later service switch follow
the integrated checks.

```sh
lain --help
lain status --json
lain mode --json
lain usage
lain history --today --json
lain accounts methods --json
```

These commands belong to the current source contract. First use `lain --help`
to check the capabilities of the actually installed CLI; development does not
update an installation. `usage` shows stored quota snapshots; `history` reports
requests and tokens recorded by the Gateway. Unknown values remain unknown.
A login or stored key does not prove inference or quota permissions. Public
research, synthetic fixtures, and real provider qualification are reported
separately. Console, OAuth, and CLI methods require their own qualified
client/session contract; reader fixtures alone do not enable login.

Accounts are the starting point. The native UI preserves reported quota windows,
account identities, drafts, context navigation, and independent local window
preferences. A single information-mode dropdown sits at the bottom right.
Settings contains density, window/notification preferences, and Help/About;
switching Monitor/Gateway is a separate Host action bound to a revision.
About reads the actual Desktop assembly version and shows the reported Host
version separately. The original cat, mint/green/black colors, and bundled fonts
are retained.

Gateway integrates CLIProxyAPI **v8.0.41** as a verified external dependency.
Lain's adapter and explicit hook patches preserve account bindings, journaling,
and transport guarantees; this is not an unchanged official release binary.
The package records the exact patch/source/build identity, which the upstream
version number alone cannot describe. Monitor packages omit this Core.

```sh
scripts/publish-linux.sh --mode monitor /absolute/new/lain-monitor
scripts/publish-linux.sh --mode gateway /absolute/new/lain-gateway
```

Publishing creates private staging artifacts with checksums, component provenance,
and corresponding third-party notices. `gate-status.json` separates backend
verification from complete release acceptance. No publisher chooses an application
license, publishes externally, or activates a service.

- [Provider referrals: actionable guide](docs/provider-referrals.md) · [full referral overview and sources](docs/provider-referrals-overview-2026-10-26.md)
- [Free and subscription comparison across connectors](docs/provider-recommendations-2026-10-26.md)
- [OAuth/device logins: current state and next integrations](docs/oauth-provider-overview-2026-10-26.md)
- [0.8.0 candidate: distribution and installation](docs/release-0.8.0.md)
- [Linux installation](docs/install.md) and [operating modes, releases, and safe recovery](docs/linux-delivery-stabilization.md)
- [Current work packages and evidence boundaries](docs/work-packages-execution-2026-10-24.md)
- [UXD packages](docs/ux-design-work-packages/README.md) and [review references](docs/uxpilot-design-results-2026-10-24.md)
- [Bundled skills and project setup](docs/skills.md)
- [CLI commands](docs/cli-usage.md), [tray](docs/desktop-tray.md), [API contracts](docs/service-contracts.md)
- [Development](docs/development.md), [backend qualification](docs/backend-qualification.md), [architecture](docs/architecture.md)
- [Historically installed v8 Core and limitations](docs/core-v8-upgrade.md)
- [Isolated verification of actual packages](tests/delivery-stabilization/README.md)

Updates preserve accounts, custom names, separate credentials, history, profiles,
routes, and desired/active revisions. A synthetic state with desired revision 11
and applied revision 2 must retain exactly that pair; an update is not an implicit
Apply. `app.previous` alone is not a data/OAuth rollback. Consistent stopped
snapshots are explicitly restored into new private state. Consumed OAuth refresh
grants remain blocked until targeted recovery or reconnection; usable static
accounts and history remain accessible. Do not blindly start old Hosts without
these safeguards against restored credentials.

The legacy proxy, other CLI profiles, and normal Codex configuration remain
outside this ownership boundary. Lain uses its own services, data, and secrets.
Codex integration writes separate `lain.config.toml`/`lain.models.json` files;
the auth helper supplies only the local inference key. Purchases, paid fallback,
public publication, and application license selection are not side effects of
this work. The application license is Artistic-2.0; making the repository public
is authorized. A public release with a tag and binary downloads remains a
separate step.

If you find lain useful, you can [support its development with a voluntary donation](https://www.paypal.com/donate/?hosted_button_id=5BR4MK748PDSQ).
