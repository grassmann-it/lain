# Technischer Ausgangsbestand

Lesender Quellstand vom 23.10.2026. Quellen sind das persönliche Projekt
`codex-proxy-tui` sowie dessen installierte `modelcoach`-/CLIProxyAPI-Ergänzungen.
Die folgenden Beobachtungen sind keine Abnahme einer lain-Version. Es wurde bei
dieser Bestandsaufnahme keine Inferenz gestartet und kein laufender Dienst geändert.

| Baustein | Bereits vorhanden | Bedeutung für lain |
| --- | --- | --- |
| Proxy | CLIProxyAPI in Go; HTTP/SSE/WebSocket, Providerübersetzung, Accountauswahl, Prioritäten, Round Robin/Weighted/Fill First, Session-Affinität, Cooldowns | Wiederverwendbarer Kandidat für den Datenpfad; Produktpolicy und portable Integration gesondert prüfen |
| Lokale Core-Erweiterungen | Parent-Request-Beziehung, Modellkompatibilität, Startup-Verhalten; Quellsnapshot und Hashmanifeste | Nicht durch einen ungeprüften Upstream-Build ersetzen; aktuelle Artefaktprovenienz vor Übernahme abgleichen |
| Usage-Erfassung | Go-Observer, privater JSONL-Spool, Python-Collector, SQLite | Fachliche Grundlage für Tokens, Requests, Ausführungsversuche, Timing und Fehler |
| Usage-API | Loopback-HTTP mit Bearer, `/v1/*`-Abfragen und `/v2/monitor`-/`quotas`-Snapshots | Oberfläche kann unabhängig konsumieren; bisher lokaler Einzelplatz |
| TUI | Python ≥3.11, Textual; Tabellen, Filter, Usage, Abo-Karten, Workerbaum | Darstellungsregeln und Erfahrungen nutzen; kein bindender UI-Stack für lain |
| Quotaadapter | Python `provider_status.py`, mehrere Anbieter, Cache/Quellzeit/Fehler/`last_good`, Hintergrundabruf alle 300s | Gute Fehler-/Frischesemantik; Adapter brauchen Accountinstanzen |
| Empfehlungen | Read-only Kandidaten-/Signalregeln und Fixtures; Modellfit/Quotaunsicherheit | Hinweise vorhanden; keine fertige kontingentoptimierte Ausführung |
| Inspektor | Flüchtige RAM-Ringe, Unix-Socket, NDJSON, Cursor/Replay, SSE-/WS-Deduplizierung | Separater späterer Kandidat; Nutzer meldet Fehler, Vollabnahme/aktueller Laufstand nicht aus älteren Tests ableiten |
| Administration | Lokale Python-Skripte für Modellkataloge, Registry, Backup, überprüften Reload | Konkrete Betriebskenntnis; persönliche Pfade/Installer ersetzen keine portable Produktverwaltung |

## Wichtigste Grenze: mehrere Accounts desselben Anbieters

Heute sind Quota-Cache und Hinweise nach **Provider** adressiert. Mehrere Adapter
erwarten genau einen passenden Zugang; Command Code lehnt mehrere Schlüssel ab.
Die TUI erzeugt sechs feste Karten. Persistierte Usage enthält Provider/Modell,
aber keine belastbare Account-/Budgetpoolidentität. `pool_for()` klassifiziert
anhand fester Modellpräfixe.

Für lain reicht daher eine größere Accountliste in der UI nicht. Account,
Credentialreferenz, tatsächlich verwendete Route und gemeinsam geteiltes
Kontingent müssen unterscheidbar werden. Die Zuordnung eines Ausführungsversuchs
muss beim Dispatch bekannt sein; ein Modellname allein beweist keinen Account.

Quellstellen im Projekt `codex-proxy-tui`:

- `integrations/model-usage/usagecore/store.py`: Event-Allowlist, `pool_for()`.
- `integrations/model-usage/usagecore/cli.py`: `sanitize_quotas()`, `QuotaCache`.
- `src/codex_proxy_tui/app.py`: feste Karten in Aufbau und `render_quotas()`.
- Installierter Bestand `modelcoach/provider_status.py`: Credentialauflösung,
  Cache und Quota-Normalisierung; `capacity_advice.py`: Hinweise pro Provider.

## Usage, Quota und Geld

Schon erarbeitet sind getrennte Request-/Execution-Ebenen, Deduplizierung,
Quell-/Abdeckungsangaben und exakte Response-ID-Joins. Diese Regeln sind wertvoll
für die gewünschte Tages-/Modellstatistik. Der neue Harness-/Accountbezug ist
zusätzliche Arbeit; bisherige persönliche Codex-Joins sind kein universelles
Harnessprotokoll.

- **Usage:** beobachtete Tokens, Requests und Versuche. Ein Fallbackversuch kann
  echten zusätzlichen Verbrauch verursachen, bleibt aber Teil desselben Requests.
- **Quota:** Anbieterbeobachtung mit Einheit, Scope, Fenster, Reset und Quellzeit.
- **Billing:** Tarif, Zusatzguthaben, dokumentierte Obergrenze, Kostenschätzung und
  tatsächliche Abrechnung unterscheiden sich. Fehlende Kosten sind nicht null.
- Command-Code-Modellobergrenzen sind dokumentiert, ihre Restwerte bleiben bei
  den bisher geprüften API-Quellen unbekannt. Nicht zu freien Budgets addieren.

Quelle: `integrations/model-usage/usagecore/query.py`, `store.py`,
`docs/monitor-api.md` sowie `modelcoach/provider_status.py`.

Die historische Query unterstützt bereits `daily`, `weekly`, `cumulative`,
Zeitzone, Start/Ende und Filter nach Modell/Provider/Thread/Effort/Tier. Tageswerte
und Gruppen nach exaktem Modellalias × Providerpool × Effort × Tier werden
getrennt berechnet. Eine fertige Matrix **Tag × Modell × Account × Harness** und
eine kanonische Modellidentität über Provider fehlen. Die heutige TUI zeigt nur
einen Teil der vorhandenen Abfragemöglichkeiten als Summarysnapshot.

Der Store ist idempotent; zusätzliche echte Ausführungen können mit ihrer Usage
berücksichtigt werden. Ein vollständiger Retry-/Fallbackzähler ist dagegen nicht
belegt (`retry_attempts` unbekannt). Lange Historien werden weitgehend in Python
aggregiert: für viele Accounts sind begrenzte SQL-Abfragen oder reproduzierbare
Aggregatprojektionen als Weiterentwicklung zu prüfen.

## Fallback und Kontingentoptimierung

Der Core kann schon Credentials auswählen und auf Fehler/Cooldowns reagieren.
Es fehlt dagegen der im persönlichen Entwurf geplante, verbindliche Vertrag für
anbieterübergreifende Quotaentscheidung, Ablehnung, Reservierung und Bindung.
`ModelRouteResponse` führt Ziel/Modell/Grund; Routerfehler werden als `unhandled`
behandelt. Das SDK kennt andernorts durchaus `AuthID`: die Lücke betrifft den
konkreten Routervertrag, nicht jede Möglichkeit einer Accountauswahl.

Quellen im Core-Snapshot:
`sdk/cliproxy/auth/selector.go`, `sdk/pluginapi/types.go`,
`internal/pluginhost/model_router.go`; ergänzend persönliche `docs/routing.md`.
Der dortige Entwurf ist eine Quelle für lain, keine bereits akzeptierte Architektur.

## Übernahme und Distribution

Der Bestand kombiniert Go, Python, SQLite und Textual mit Linux-User-Systemd,
Unix-Sockets, lokalen `.so`-Plugins, Home-Pfaden und einer `.venv`. Diese
Betriebsannahmen brauchen für Windows/macOS eine gesonderte Lösung. Eine
Avalonia-Oberfläche allein macht den Backendbestand noch nicht portabel.

Der Core-Snapshot nennt CLIProxyAPI `v7.3.17` und Upstream-Commit `9bdde54…` mit
lokaler Patchkette. Core und mitgelieferte Upstreamlizenz sind MIT; zugehörige
Hinweise bleiben bei Übernahme erhalten. Lizenz des eigenen Produkts ist offen.
Das bestehende TUI-Repo besitzt weiterhin keine Commits/Remote, aber Quell- und
Buildmanifeste. Vor Quellübernahme reproduzierbaren Basissnapshot festhalten.

Quellen: `integrations/proxy-core/baseline.json`, mitgelieferte `LICENSE`,
`integrations/model-usage/baseline.json`, `scripts/deploy.py`,
`scripts/install-launcher.sh`, `docs/implementation-status.md`.
