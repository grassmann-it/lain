# lain

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
