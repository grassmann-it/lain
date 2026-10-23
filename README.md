# lain

Eigener lokaler Proxy, Accountverwaltung und persistente Nutzungsstatistik für
Linux x64. Ein eigenständiger .NET-10-Host betreibt den gepinnten CLIProxyAPI-Core;
Avalonia und CLI verwenden dieselbe authentifizierte API und SQLite-Aggregation.
Das Fenster kann geschlossen werden, während der Dienst weiterarbeitet.

```sh
lain service start
lain ui
codex --no-daemon --profile lain
lain usage --today --json
```

**Stand 24.10.2026:** Der erweiterte Linux-Stand ist installiert. Quoten erscheinen
als aufklappbare Kacheln mit Prozenten, Restbudget und gemeldeten Resets; unabhängige
Spalten vermeiden Leerflächen. Semi/Ursa, Mint und dunkle Flächen sowie gebündelte
Plex-/Oxanium-Schriften bestimmen die Oberfläche.

Der Inspector zeigt eigene Streams und belegte Request-/Worker-Beziehungen.
Usage vergleicht Modelle, Accounts, Anbieter und Reasoning samt Messabdeckung und
P50/P95. Der Anbieterkatalog bietet offizielle Links, Einrichtung und deklarierte
Fähigkeiten für 15 zusätzliche Anbieter. Diese wurden mit lokalen Testanbietern
geprüft und bleiben ohne echte Zugangstests ausdrücklich `not_tested`.

380 UI-Prüfungen auf privatem Xvfb sowie die installierte Oberfläche mit echten
lokalen Daten sind geprüft. Ein echter historischer Request mit 81.986 Tokens
blieb beim Dienstupdate und Fensterneustart erhalten. Die neue Core-Qualifikation
umfasst synthetische Identitäts-, Dauerhaftigkeits- und Streamtests; frühere echte
Codex-/Command-Code-Proben bleiben ihrem damaligen Binary zugeordnet.
[Aktuelle Abnahme und Grenzen](docs/expansion-review.md) ·
[CLI und Einrichtung](docs/observability-cli.md) ·
[Quotenkacheln](docs/screenshots/quota-tiles.png) ·
[Inspector](docs/screenshots/inspector.png) ·
[Usage](docs/screenshots/usage-analysis.png)

- [Installation und Betrieb](docs/install.md)
- [Entwicklung und reproduzierbare Prüfung](docs/development.md)
- [Backendqualifikation](docs/backend-qualification.md)
- [Core, Journal und Authgrenzen](docs/backend-core.md)
- [API-Verträge](docs/service-contracts.md)
- [Architektur](docs/architecture.md)
- [Produktumfang](docs/product-scope.md)
- [Ausführungsplan](docs/execution.md)

Das kontrollierte Update erhält die bestehenden persönlichen Proxy-Dienste und
die normale Codex-Konfiguration. Lain besitzt eigene Dienste, Ports (18417/18418), Secrets,
Konfiguration und Journale. Der Codex-Export schreibt ausschließlich
`lain.config.toml` und `lain.models.json`; das Authkommando liefert nur einen
lokalen Inferenzschlüssel.

Automatische Kontingentoptimierung, Rollenrouting, Classifier und Approver
gehören zu späteren Ausbaustufen. Das Produkt ist unabhängig von der
bisherigen persönlichen Proxy-/TUI-Implementierung.
