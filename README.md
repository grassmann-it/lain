# lain — Local AI Gateway

Eigener lokaler Proxy, Accountverwaltung und persistente Nutzungsstatistik für
Linux x64. Ein eigenständiger .NET-10-Host betreibt den gepinnten CLIProxyAPI-Core;
Avalonia und CLI verwenden dieselbe authentifizierte API und SQLite-Aggregation.
Das Fenster kann geschlossen werden, während der Dienst weiterarbeitet.

```sh
lain service start
lain ui
codex --no-daemon --profile lain
lain usage
lain history --today --json
```

**Stand 24.10.2026:** Die neue UI-Struktur ist installiert: **Accounts** als
Startseite, **Models**, **Profiles**, **Usage** und **Live**. Anbieter entdecken
gehört zu Accounts. **Barebones / Simple / Advanced**, unabhängige Dichte,
Suche, angeheftete Accounts und stabile Sortierung machen die Darstellung
anpassbar. Simple ist der Standard. Das Katzenlogo erscheint im Fenster und
Linux-Launcher unter **lain — Local AI Gateway**.

Barebones / Simple / Advanced wechseln direkt im Dropdown unten rechts.
Unter **View** lassen sich Minimieren ins Tray, Schließen ins Tray und
Kontingent-/429-Meldungen unabhängig einstellen. Das neue Katzenicon wird in
Fenster, Tray und Launcher verwendet. Der Host läuft beim Beenden der UI weiter.

`lain usage` zeigt kompakt die Kontingente aller Accounts; `lain usage --full`
ergänzt Quellen, IDs und weitere Details. `lain history` zeigt die aufgezeichnete
Proxy-Nutzung. Explizite alte Aufrufe wie `lain usage --today --json` bleiben
Verlaufsaliase. [CLI-Befehle](docs/cli-usage.md) · [Tray-Verhalten](docs/desktop-tray.md).

Aufklappbare Accountkacheln zeigen alle gemeldeten Kontingentfenster mit Prozent,
Restbudget und Reset. Semi/Ursa, Mint, dunkle Flächen und gebündelte
Plex-/Oxanium-Schriften bestimmen den Look. Die Navigation erhält Entwürfe und
Filter; Usage zeigt ihre Kennzahlen zuerst und besitzt eine Hauptaufschlüsselung.

Der Inspector zeigt eigene Streams und belegte Request-/Worker-Beziehungen.
Usage vergleicht Modelle, Accounts, Anbieter und Reasoning samt Messabdeckung und
P50/P95. Der Anbieterkatalog bietet offizielle Links, Einrichtung und deklarierte
Fähigkeiten für 15 zusätzliche Anbieter. Diese wurden mit lokalen Testanbietern
geprüft und bleiben ohne echte Zugangstests ausdrücklich `not_tested`.

Die exakte installierte Oberfläche besteht 124 Prüfungen auf privatem Xvfb;
Tray und Benachrichtigungen weitere 60 in einer privaten Sitzung. Die kompakte
CLI besteht 173 Vertragsprüfungen und 55 Terminal-/Pipe-/JSON-Captures.
Ein echter historischer
Request mit 81.986 Tokens, Cursor 2 und der gewünschte/aktive Stand 4/2 blieben
beim Dienstupdate und Fensterneustart erhalten. Der native Launcher samt
Fenstericon ist ebenfalls geprüft. Der qualifizierte Core blieb bytegleich;
dieser Umbau fügt keine neue Live-Anbieterqualifikation hinzu.
[Aktuelle Abnahme und Grenzen](docs/tray-cli-acceptance.md) ·
[CLI und Einrichtung](docs/observability-cli.md) ·
[Accounts](docs/screenshots/ui-structure-accounts.png) ·
[Profiles](docs/screenshots/ui-structure-profiles.png) ·
[Usage](docs/screenshots/ui-structure-usage.png)

- [Installation und Betrieb](docs/install.md)
- [Standalone-Referralübersicht](docs/provider-referrals.html)
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

Als nächster eigenständiger Ausbau ist ein [Monitor-Modus](docs/monitor-mode.md)
ohne lokalen CLIProxyAPI-Core vorgesehen: zunächst Kontingente und Resets,
Request-/Tokenhistorie später. Dieser Modus ist noch nicht implementiert.
