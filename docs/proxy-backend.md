# Vorhandener Proxy als Grundlage

Geprüft am 23.10.2026. Bestätigte Produktrichtung: bestehende Proxysoftware als
Abhängigkeit verwenden und möglichst mitliefern. CLIProxyAPI ist der bevorzugte
Prüfkandidat. Version, Lieferform und Usage-Anschluss sind noch nicht entschieden.

## Lokaler Bestand

**CLIProxyAPI** ist der eigentliche Go-Proxy. **EasyCLIProxyAPI** ist eine separate
Verwaltungsoberfläche dafür. Lokal startet `cliproxyapi.service` den Core direkt;
lain braucht für diesen Datenpfad keine zweite Verwaltungsoberfläche.

Der laufende Core entspricht dem vorhandenen Basissnapshot
`7.3.17-parent-request.3-model-compat.3-startup-readiness.1`:

- Prozessdatei und installierte Binärdatei haben denselben SHA-256:
  `8b2e25d6546e2d84ab42d2f2a843fa1efc106adef27e2b9477d1cc2171bb0cb9`.
- Lokale Ergänzungen betreffen Request-/Versuchszuordnung, bestimmte
  Modell-/Toolschema-Anpassungen und die Bereitschaft beim Start.
- Die Managementabfrage meldet Observer `model-usage 1.1.0-live.1` als aktiv.
  Vorhandener `live.2`-Quellcode belegt keine aktive Installation.

Das ist kein unveränderter Upstream-Build. Die lokale Ausführung verwendet
Linux amd64, CGO und dynamische Bibliotheken; ihre Portabilität ist nicht bewiesen.

Quellen: `codex-proxy-tui/integrations/proxy-core/baseline.json`, installierte
`model-usage/proxy_core/manifest.json` und `README.md`, Prozess-/Dateihash sowie
Managementstatus. Weitere Übernahmegrenzen stehen im
[technischen Bestand](technical-inventory.md).

## Upstream und Distribution

Am Prüftag ist **v8.0.40** der aktuelle veröffentlichte Kandidat. Die Release-
Pipeline liefert native Artefakte für Linux, macOS und Windows mit Checksummen.
Docker ist keine Voraussetzung. Die MIT-Lizenz erlaubt Weitergabe unter Erhalt
der Lizenz-/Copyright-Hinweise. Pluginfähige Linux-Builds und `no-plugin`-Builds
haben unterschiedliche Laufzeitvoraussetzungen.

V8 bietet einen neuen Managementvertrag unter `/v8/management`; die alte
`/v0/management`-API steht vor der Ablösung. Der erste erfolgreiche V8-
Konfigurationsschreibvorgang migriert eine ältere Datei auf `config-version: 8`.
Ein Rückwechsel muss deshalb auch zur Konfiguration passen.

Die aktuelle Version wurde hier weder installiert noch gegen die lokalen
Erweiterungen qualifiziert. Ein vorhandenes Release allein ersetzt diese Prüfung
nicht. Für lain sind fest zugeordnete Core-Version, Plattformartefakt, Prüfsumme,
Lizenz und Konfigurationsstand erforderlich, sobald die Lieferform feststeht.

Quellen: [Release v8.0.40](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.40),
[Release-Pipeline](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.40/.github/workflows/release.yaml),
[MIT-Lizenz](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.40/LICENSE),
[Management V8](https://help.router-for.me/management/apiv8),
[EasyCLIProxyAPI](https://github.com/router-for-me/EasyCLIProxyAPI).

## Usage bleibt eine eigene Integrationsaufgabe

Upstream hat die früheren Usage-Statistiken seit v6.10.0 entfernt. Der aktuelle
Managementvertrag bietet mit `/observability/usage/queue` eine entnehmende Queue,
keine dauerhafte Tages-/Modellhistorie. lain braucht einen Collector und eigene
Persistenz. Mehrere UI-Fenster dürfen nicht unabhängig dieselbe Queue leeren.
Quellen: [README](https://github.com/router-for-me/CLIProxyAPI/blob/v8.0.40/README.md)
und Managementdokumentation oben.

Zusätzlich im **lokalen Quellsnapshot** belegt; für einen anderen Build erneut prüfen:

- Die alte `/v0/management/usage-queue` verwendet `PopOldest` und flüchtige
  Aufbewahrung. Abholung und lokales Speichern sind keine atomare Zustellung.
- Native Usage-Records haben `AuthID`/`AuthIndex`; die Queue exportiert
  `auth_index`, den der heutige Collector verwirft. Das ist eine Core-
  Credentialidentität, keine dauerhafte lain-Account-ID.
- `/api-key-usage` enthält begrenzte RAM-Erfolgs-/Fehlerzähler und ersetzt keine
  Tokenhistorie. Rohdaten können Zugangsdaten enthalten; nur ausgewählte,
  bereinigte Metadaten gehören in die Statistik.
- Das Go-SDK bietet `Service.RegisterUsagePlugin` und `HandleUsage(ctx, record)`.
  Ein kleiner Launcher mit Usage-Sink wäre ohne natives `.so`-Plugin möglich;
  Pufferung, Schreibausfälle und Shutdown sind trotzdem zu klären.

Quellstellen: `sdk/cliproxy/service_plugins.go`, `sdk/cliproxy/usage/manager.go`,
`sdk/cliproxy/builder.go` im lokalen Core-Snapshot sowie der bestehende Collector.

Vor einer Implementierungsübergabe bleiben drei konkrete Punkte zu qualifizieren:
Streaming/Tools der ersten Harnesse, persistente Usage samt Accountzuordnung und
Start/Update/Rückwechsel auf den ersten unterstützten Plattformen. Die Wahl
zwischen Queue-Collector, kleinem SDK-Launcher und bestehendem Observer bleibt
offen. Der spätere Inspektor ist keine Voraussetzung für die Basisstatistik.
