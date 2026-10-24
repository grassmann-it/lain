# CLIProxyAPI v8.0.41: Upgrade und externe Abhängigkeit

Der Nutzer hat am 24.10.2026 das direkte Upgrade beauftragt: möglichst unveränderte
CLIProxyAPI-Software verwenden und bestehende lain-Funktionen erhalten. Damit ist
die v7-Pinnung der ersten Lieferung für diesen Folgeauftrag aufgehoben.

## Implementierung

CLIProxyAPI wird aus dem offiziellen, gehashten v8.0.41-Modularchiv mit Commit
`8289ec4c71ad914d0612c2776352b76ba3d355a6` bezogen. Der vollständige vendorte
Quellbaum entfällt. `proxy/adapter` enthält den eigenen Launcher, Accountbindungen,
das dauerhafte Journal, den begrenzten Inspector und die Login-Eigentumsregeln.
Providerimplementierungen und Protokollübersetzung kommen weiter aus CLIProxyAPI.

Ein unverändertes offizielles Binary kann die vorhandenen Garantien nicht liefern:
Usage gelangt zunächst in eine RAM-Queue; Executorregistrierung, Feldanwesenheit
und einige bestehende Transportparameter benötigen zusätzliche Hooks. Diese
Erweiterungen werden offen als Patchdateien geführt. Das Buildmanifest trägt
`upstreamSourceModified: true`; der Upstream-Modulcache bleibt unverändert.
Die genaue Abgrenzung steht in [proxy/README.md](../proxy/README.md), Herkunft und
Dateihashes in [proxy/upstream.json](../proxy/upstream.json).

Die Host-Produktionsquellen brauchen für dieses Upgrade keine Änderung. Seine
Startflags, `/lain/*`-Schnittstellen und das Journalformat bleiben erhalten.
Die Auslieferung ersetzt ausschließlich Core, Go-Lizenznachweise und begrenzte
Core-Provenienz/Metadaten. Host, CLI, Desktop, Konfiguration, Secrets und offene
Profiländerungen behalten ihren Stand.

Eine bewusst geschlossene Nebenfunktion ist native serverseitige Bildgenerierung:
Ihr zusätzlicher modellbezogener Usage-Datensatz passte schon im v7-Stand nicht in
den qualifizierten Ein-Versuch-/Primärusage-Vertrag. Explizite native Bildtools
werden deshalb vor Versand klar abgewiesen, automatische Injektion deaktiviert
und Payload-Overrides am letzten HTTP-Sendepunkt geprüft. Normale Function-Tools
und Bildeingaben zur Analyse bleiben erhalten. Das ist eine transparente
Einschränkung des zuvor ungeprüften Pfads, keine behauptete neue Bildunterstützung.

## Abnahme

Der Core ist installiert und nach kontrolliertem lain-Neustart abgenommen.
Auslieferungsquelle: `734d7471703054496e7dc0361c4990bab38f6afd`.
Binary-SHA-256:
`e39e141daaca6c1946d93866e4d67e93651a07ff9664edf03078d4a9c49b6fe6`.
Source-Manifest-SHA-256:
`c5ff7548dcf72a0dd40b9c9a389799303bd7efebb7d1d30835aec13b1349d0cb`.

| Getrennte Prüfung | Ergebnis |
| --- | --- |
| Adapter-/Hookregressionen und unabhängiges Quellenreview | 69 Adaptertests; fünf gezielte Upstream-Testfälle einschließlich Subtests; keine offenen Befunde im geprüften Hookumfang. Zwei unabhängig vorbereitete Builds samt Sidecars bytegleich. |
| Unabhängige Runtime | Alle acht Gruppen bestanden: Protokolle/Dauerhaftigkeit, Auth/Crash, Inspector, Upgradegrenzen, native Toolgrenze, Hostwriter, Versionswechsel, SQLite-Replay. Nur privater Loopback-Namespace und synthetische Anbieter. |
| Vollständiges Journal zehnmal durch echten SQLite-Consumer | 200 Requests, 200 Versuche, 3.600 Tokens, zwei Accounts, Cursor 600; unbekannte Cache-/Reasoningmengen bleiben null. |
| v7→v8→v7 | Alte Bytes, Ereignisse und Source-ID unverändert; neue Sequenzen schließen an. Gemischtes Journal zehnmal: vier Requests/Versuche und 72 Tokens. Kein echter Datenstand für den Test kopiert. |
| Frischer Linux-Paketbau | Echter `publish-linux.sh`-Lauf, 59 gelinkte Go-Module ohne fehlende Root-Lizenznachweise, 40 NuGet-/Runtimepakete. Eigenständige frische App-Builds wurden nicht installiert oder pauschal releasequalifiziert. |
| Core-only-Installer | Exaktes Paket auf vollständiger privater Appkopie; absichtlicher Fehler nach zwei Swaps stellt alle ursprünglichen Bytes wieder her. Anschließende Installation entspricht dem vollständigen Paketmanifest. |
| Echte Installation | Zusammengehöriges App-/Statebackup im gestoppten Zustand; ausschließlich lain gestoppt/gestartet. Fünf Accounts, 45 aktive Modellaliase, Revision 4/2, bestehende Credentials und Runtime unverändert. Journalbytes/Source-ID und Historie mit 81.986 Tokens/Cursor 2 erhalten. |
| Installierte UI/CLI | 166 Prüfungen der unveränderten Desktopassemblies gegen den aktualisierten Dienst auf privatem Xvfb. CLI aus `/tmp`: 20 kompakte gegenüber 99 vollständigen Zeilen, identisches JSON, unveränderte historische Zahlen. |
| Isolation | Persönlicher Proxydienst samt geschützten Dateien, normale Codex-Konfiguration, separate lain-Clientintegration, Icon und UI-Präferenzen unverändert. Keine echte Inferenz ausgelöst. |

Der vollständige vendorte Baum mit 1.476 Dateien entfällt. Die zwei expliziten
Patches betreffen 16 Upstreampfade; 576 Patchzeilen enthalten auch die zugehörigen
Regressionstests. Die Paketprovenienz bewahrt ursprüngliche App-Pins und frühere
Gatebelege; nur Core/Go-Notices und ausdrücklich begrenzte Metadaten wurden ersetzt.

Private zusammengehörige Sicherung:
`~/.local/share/lain/upgrade-backups/core-v8-20261024T142030Z`.
Das abschließend geprüfte Paket liegt unter
`artifacts/lain-linux-x64-0.1.0-core-v8-20261024.tar.gz`; sämtliche 1.205 Dateien
entsprechen der versiegelten Installation. `releaseQualified` und
`coreRefreshQualified` sind für genau diesen Stand gesetzt.
Keine Datenbankmigration war nötig. Der Versionsrollback wurde synthetisch gelesen;
eine tatsächliche Rückkehr der Benutzerinstallation wurde nicht ausgeführt.

Belege: [Quell-/Unitgates](../tests/acceptance/evidence/core-v8-source.json),
[unabhängige Runtime](../tests/acceptance/evidence/core-v8/acceptance.json),
[frischer Paketbau](../tests/acceptance/evidence/core-v8-fresh-package.json),
[Installer](../tests/acceptance/evidence/core-v8-installer.json),
[Installation](../tests/acceptance/evidence/core-v8-installed.json),
[UI](../tests/acceptance/evidence/core-v8-installed-ui.json),
[CLI](../tests/acceptance/evidence/core-v8-cli.json),
[finaler Lieferbeleg](../tests/acceptance/evidence/core-v8-release-final.json).

Ausführbare Matrix: [Backendqualifikation](backend-upgrade-qualification.md).
Staging-/Installationsvertrag: [Core-only-Auslieferung](core-upgrade-delivery.md).

Neue Upstreamfeatures sind durch einen Versionswechsel nicht automatisch in lain
freigeschaltet oder live qualifiziert. Der bestehende Endpointumfang bleibt
bestehen. Reale OAuth-Rotation, neue Anbieterzugänge und neue Upstreamfähigkeiten
brauchen gesonderte Nachweise. Der persönliche Bestandsproxy und seine Queue
gehören nicht zu den Upgradeprüfungen.
