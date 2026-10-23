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

**Stand 23.10.2026:** Backend A ist anhand des exakten Core-Builds qualifiziert:
15 synthetische Fälle, unabhängige Absturz-/Zugriffstests und echte Streaming-/
Toolfortsetzungen mit Codex und Command Code. Der Linux-Stand ist installiert: fünf Accounts, 45 Modellrouten, ein eigener
systemd-User-Dienst und das getrennte Codex-Profil `lain`. Echte Codex-Nutzung
bleibt nach Dienstneustart erhalten und wird in CLI und Oberfläche gleich
angezeigt. Die [Abnahme](docs/acceptance.md) dokumentiert Umfang und Grenzen. Unbekannter
Verbrauch bleibt unbekannt; fehlgeschlagene echte Proben werden nicht gelöscht.
Direkt verwaltete OAuth-Logins sind getrennt von externen Access-Token-Snapshots;
reale OAuth-Rotation wurde nicht als getestet ausgegeben.

Die Oberfläche verwendet jetzt Semi und Ursa im kompakten MeowChan-Stil.
[Usage-Vorschau](docs/screenshots/usage-semi.png) · [Accounts](docs/screenshots/accounts-semi.png) · [Designprüfung](docs/ui-redesign-review.md)

- [Installation und Betrieb](docs/install.md)
- [Entwicklung und reproduzierbare Prüfung](docs/development.md)
- [Backendqualifikation](docs/backend-qualification.md)
- [Core, Journal und Authgrenzen](docs/backend-core.md)
- [API-Verträge](docs/service-contracts.md)
- [Architektur](docs/architecture.md)
- [Produktumfang](docs/product-scope.md)
- [Ausführungsplan](docs/execution.md)

Die bestehenden persönlichen Proxy-Dienste und die normale Codex-Konfiguration
bleiben unverändert. Lain besitzt eigene Dienste, Ports (18417/18418), Secrets,
Konfiguration und Journale. Der Codex-Export schreibt ausschließlich
`lain.config.toml` und `lain.models.json`; das Authkommando liefert nur einen
lokalen Inferenzschlüssel.

Automatische Kontingentoptimierung, Rollenrouting, Classifier, Approver und
Inspektor gehören zu späteren Ausbaustufen. Das Produkt ist unabhängig von der
bisherigen persönlichen Proxy-/TUI-Implementierung.
