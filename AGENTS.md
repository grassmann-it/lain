# lain

Aktueller Recording-Folgestand 01.11.2026: Default-on und Settings-Archivzugang
implementiert/geprüft und vomNutzer installiert. Receiptverified, Host
0.8.0+recording-settings-20261101,926/926ready, Archivliste lesbar. Historische
Aufnahmelücken bleiben; Startstatus source_unavailable bestätigt keine durchgehende
Erfassung. Beleg: `docs/recording-settings-delivery-2026-11-01.md`.
VSCodeSnapAutoUpdate273→274 war beimShutdown06:13unterbrochen; snapdStart11:36
vollendete es, VSCode1.144.0 wieder geöffnet. KeinLainUninstall.

Aktueller lokaler Abgleich 31.10.2026 22:50: Vollpaket80dcf13 installiert und gegen
1155 Paketdateien geprüft, Gateway926/926ready, Login-AutostartAN. Codexdispatcher
repariert; normale Lainstarts patched0.164, Subcommands Original0.166. VSCode
Produktionsstart/Resume wieder erfolgreich; sporadische Rendererursache offen.
Aufnahme content AN, aber collection_failed/unknownLoss: nicht vollständig.
Aktueller Beleg und Grenzen: `docs/codex-client-recovery-2026-10-31.md`. Ältere
Nichtinstalliert-/Nichtaktiviertangaben unten sind historisch.

Aktueller Implementierungsstand 31.10.2026: lokaler opt-in Recorder und priorisierte
Releasekorrekturen sind integriert. Sourcefreeze10734a4, Liefer-/Prüfgrenzen in
`docs/recording-execution-2026-10-30.md`, Pins in
`docs/recording-verification-2026-10-31.json`, Bedienung in
`docs/recording-operations.md`. Linux-Prüfpaket gebaut und isoliert geprüft;
kein produktiver Install/Apply und keine Aufnahme aktiviert. Public-/Backend-
Liefergates nicht automatisch aus einzelnen Tests ableiten. Fremde CLI-Routen-WIP
liegt weiter außerhalb des eingefrorenen Kandidaten. Ältere Plan-only- und
Installiert-Angaben unten gelten für ihren jeweiligen historischen Bezug.

Aktive Arbeitskopie 26.10.2026: Projektregeln und begrenzte Worker sind ausdrücklich
zur parallelen Umsetzung beauftragt. Vertrag: `docs/workers-v1.md`, Liefer- und
Prüfstand: `docs/release-workers-2026-10-26.md`. Automatische Zerlegung und
Quota-/Kostenoptimierung bleiben nachgelagert. Neue Sourceänderungen nicht als
bereits installierte oder veröffentlichte Version behandeln.
Ausnahme: der separate Codexlauncher wurde lokal repariert und qualifiziert;
normales `codex` und `--profile lain` verwenden jetzt beide das Gruppenmenü.
Der zusätzliche native OpenAI-`/usage`-Fix ist am26.10.2026 qualifiziert und
lokal installiert; echte Anmeldung und Kontingentabfrage geprüft. Laufende
Daemons erhalten, sofort neue Bytes mit `codex --no-daemon`. Bericht:
`docs/native-openai-usage-delivery-2026-10-26.md`.
Die Worker-Host-/CLI-Source ist weiterhin nicht produktiv installiert.
Die VS-Code-Modellgruppen sind separat nativ qualifiziert und als lokale
Erweiterung 26.1021.22028 installiert; Produktionsfenster nicht neu geladen.
Liefer-/Wartungsbelege: `docs/vscode-model-groups-delivery-2026-10-26.md`.
Astras eingebundener Katalog ist auf low/fünf unterstützte Werte repariert;
spätere Modellwahl und bestehende Threadsettings bleiben erhalten.

Aktuelle Orientierung (26.10.2026): installierter OAuth-/Device-Folgestand
`ff5771c` in `docs/oauth-login-implementation-2026-10-25.md`: Kilo und Kimi,
frische vollständige Pakete, native Abnahme und einmaliges eigenes Update
mit Bestanderhaltung bestanden. Echte neue Providerlogins bleiben separat;
Der allgemeine Requesty-CLI-Loginadapter ist weiterhin nicht aktiviert; ein
konkreter Nutzerzugang wurde separat über die offizielle isolierte CLI
angemeldet und privat übernommen (16 Accounts, Revision 55, angewandt 54).
Vorherige private Linux-Lieferung N01–N09
in `docs/next-work-packages-delivery-2026-10-25.md`; konsolidierter Ausgangsstand und
nächste Arbeitspakete in `docs/next-work-packages-2026-10-25.md`.
Die Umsetzung ist inzwischen als Goal beauftragt; aktueller Ablauf in
`docs/next-work-packages-execution-2026-10-25.md`. `docs/execution.md` und
`docs/acceptance.md` enthalten frühere Aufträge und deren damalige Nachweise;
alte Offen-/Laufend-Aussagen nicht ohne Abgleich als heutigen Stand übernehmen.
Bestätigte Ziele stehen in `docs/product-scope.md`, der geprüfte technische
Ausgangsbestand in `docs/technical-inventory.md`. Bestehende persönliche
Proxy-/TUI-Projekte sind Quellen und mögliche Bausteine; ihre Entscheidungen
gelten nicht automatisch für lain. Vorschläge bleiben als solche gekennzeichnet.
Der aktuelle Paketentwurf steht in `docs/implementation-plan.md`, die vorgeschlagene
Struktur in `docs/architecture.md` und die fachlichen Verträge in
`docs/service-contracts.md`. A qualifizierte den ursprünglichen Backendanschluss;
spätere Upgrades und Lieferungen stehen in den aktuellen Berichten oben.
Ursprüngliche Paketgrenzen nicht als Beleg noch fehlender Implementierung lesen.

<!-- architecture-sparring:start -->
Architekturkontext für Codex: Dieser Abschnitt gilt nur für Codex.
Bei architekturrelevanter Planung, Umsetzung oder Gegenprüfung: Falls
`.architecture-sparring/config.json` vorhanden ist, lies sie zuerst.
Nur wenn `auto_context.codex` true ist, lies `index.md` unter `notes_path`
und die für den Auftrag relevanten verlinkten Entscheidungen/Projektunterlagen.
Fehlende Konfiguration oder Notizen sind kein Fehler und kein Einrichtungsauftrag.
Nur ausdrücklich akzeptierte Anforderungen und Entscheidungen sind verbindlich;
Vorschläge und Hypothesen bleiben vorläufig. Prüfe den Bezugsstand zur Arbeitskopie.
Kläre tragende Architekturkonflikte vor der betroffenen Umsetzung. Bestehende
Projektregeln und der konkrete Auftrag gelten weiterhin; dieser Hinweis startet
kein vollständiges Sparring und autorisiert keine zusätzlichen Dateiänderungen.
<!-- architecture-sparring:end -->

## Explicit worker route selection (30 October 2026)

The user rejects automatically introducing Lain's `free_only` mode when requesting
a free endpoint such as `openrouter/free`. Ordinary worker delegation uses the
configured route with `costPolicy: "configured"`. `providers check-free` is optional
strict-policy qualification, not a mandatory route-availability check. Keep the
requested model, existing provider spending caps and no-paid-substitution request;
do not add a fallback or silently change an applied route-level guard. Do not infer
extra cost-policy requirements merely from the model's free label.
