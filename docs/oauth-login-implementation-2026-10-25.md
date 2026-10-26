# Weitere Browser- und Deviceanmeldungen

Stand 26.10.2026, Ausgangscommit `c9f8840`. Der Nutzer hat nach dem
Loginstatus ausdrücklich die direkte Implementierung der fehlenden Wege beauftragt.
Dies ist ein neuer Folgeauftrag; der abgeschlossene N01–N09-Bericht bleibt erhalten.

## Umfang und Grenzen

Kilo erhält den veröffentlichten Deviceflow einschließlich anschließend nutzbarer
Credential-/Quota-Anbindung. Kimi erhält den veröffentlichten Deviceflow mit
identifizierbarem lain-Host, gebundener Identität und eigenem Refreshowner.
Desktop und CLI zeigen den für Menschen bestimmten Eingabecode und die
Verifikationsadresse ausdrücklich an und behalten Cancel/Expire/Recovery bei.
Kimi hat einen separaten privaten Gerätecode. Kilo verwendet nach seinem
veröffentlichten Vertrag denselben Code auch als Polling-ID; eine zusätzliche
geheime Geräte-ID wird dafür nicht erfunden.
Bestehende Account-IDs, Namen, Routen, Profile und Historie werden erhalten.
Eine Anmeldung erzeugt keine Modellrouten und wendet kein Profil an.

Ein ausdrücklich für externe Hosts vorgesehener gemeinsamer öffentlicher
SDK-Client ist kein Secret und erfordert nicht allein wegen seiner Herkunft eine
zusätzliche Zulassung. Der dokumentierte Clientzweck und eine ehrliche eigene
Hostidentität sind nachzuweisen. Das ist keine Freigabe zur Übernahme fremder
Sitzungen oder zur Behauptung, lain sei die offizielle Hersteller-CLI.

Google-, Cloudflare- und Poe-Direktlogin haben bereits eigene Adapter. Ihre
veröffentlichten Registrierungsanforderungen werden konkret überprüft und als
versionierter Maintainerablauf vorbereitet. Falls eine Anmeldung beim Anbieter
oder dessen Zulassung erforderlich ist, kann sie nicht durch ein lokales
Availability-Flag ersetzt werden. Z.ai wird anhand seines tatsächlichen
öffentlichen Client-/Sitzungsvertrags geprüft; ein erfundener Refresh- oder
Delegatepfad wird nicht als fertiger Login geliefert.

Vorhandene Codex-/OpenRouter-/Google-CLI-/MiMo-Wege bleiben erhalten. Keine Käufe,
bezahlten Fallbacks, stillen Keycapänderungen oder Übernahme fremder Browserprofile.
Providerzustimmung und Benutzerlogin bleiben sichtbare Schritte. Synthetische
Prüfungen und tatsächlicher Login/Refresh/Reporting werden getrennt ausgewiesen.

## Parallele Verantwortung

| Bereich | Zuständigkeit |
| --- | --- |
| Kilo Deviceadapter und Providerfälle | account_model_groups |
| Kimi Deviceadapter, Credentialowner und Reader | collections_backend |
| Z.ai Herstellervertrag und konkreter Handoff | codex_picker_impl |
| Google/Cloudflare/Poe Maintainerregistrierung | codex_profile_launcher |
| Desktopcode und Loginzustände | n01_display |
| CLIcode und Loginzustände | cli_usage_table |
| Unabhängiger Integrationsreview | codex_scope_acceptance |
| Begrenzte serielle Build-/Testlane | codex_build_lane |
| Vollständiges Paket und Erhaltungsprüfung | impl_delivery |
| Gemeinsame Verträge, Hostregistrierung und Integration | Root |

Alle Worker in eigenen Cacheworktrees, insgesamt höchstens zwölf aktive Agenten.
Große Builds/Runtime seriell und ressourcenbegrenzt, GUI nur privates Xvfb/D-Bus,
Temporärdaten im Cache. Das externe Python-model-usage wird nicht verändert.

## Integrierter Umfang

- Gemeinsame additive Felder `UserCode` und `VerificationUri`; begrenzte
  ASCII-Codes und HTTPS-Links ohne Userinfo. Moderne und Legacy-Loginpfade räumen
  Anweisungen bei Abschluss, Fehler, Abbruch oder Ablauf auf.
- Kilo: persönlicher Device-Login für Inferenz, Modelle und Quota. Das ausgegebene
  opaque Token wird als `static-key` behandelt, ohne erfundene Ablaufzeit oder
  Refresh. Zunächst dauerhaftes privates Checkpoint ohne nutzbare Zwecke, dann
  Personal-/E-Mail-Prüfung. Bestehende qualifizierte Transportdaten werden beim
  Reconnect erhalten; alte Tokens in Root, Varianten und YAML werden ersetzt.
- Kimi: explizite globale oder Mainland-CN-Region vor dem ersten Aufruf,
  ehrliche lain-Hostidentität, `/me`-Bindung und `/usages`. Der neue eigene Owner
  erneuert ausschließlich seine eigenen Grants über die gemeinsame
  Generationverwaltung. OAuth ist hier **quota-only**; vorhandene Inferenzkeys
  bleiben unabhängig. Der Compositeadapter erhält den bisherigen Keyreader.
- CLI und Desktop: sichtbarer kopierbarer Code, manueller Linkfallback und
  fortgesetztes Polling auch in `authorizing`. Terminale Anzeigen verschwinden
  bereits vor einem möglicherweise fehlgeschlagenen Konfigurationsread.
- Google/Cloudflare/Poe: [Offline-Maintaineroperator](oauth-maintainer-registration-2026-10-25.md)
  mit Registrierungsplan, eigener öffentlicher Clientidentität und deaktiviertem
  Staging. Ein Plan erstellt keine Providerregistrierung und aktiviert keinen
  ungeprüften Adapter.
- Z.ai: [konkreter Handoff](providers/zai-managed-login-handoff-2026-10-25.md).
  Kein unabhängiger Publicclientvertrag belegt; offizieller CLIpfad kann bereits
  vor Rückgabe einen Key anlegen. Kein spekulativer Delegate-/Refreshadapter.

Details und Quellen: [Kilo](account-connect-research/kilo-device-implementation.md),
[Kimi](providers/kimi-device-login-2026-10-25.md).

## Abnahme und Lieferung

Die gezielten synthetischen .NET-Suiten und die native Gerätecodeanzeige sind
mit dokumentierten Sourcepins geprüft. Die Desktopprüfung lief ausschließlich
in privatem Xvfb/D-Bus bei 100/125 Prozent, mit tatsächlichem RenderScaling und
Hash der geladenen Desktopassembly. Die vollständigen gemeinsamen Host-/CLI-/
Desktoppakete wurden separat gebaut; die eigene Installation und deren
Erhaltungsprüfung folgen anschließend.

Der erste Foundation-Zwischenbuild hatte einen erfolgreichen Restore/Build,
startete wegen einer währenddessen geänderten Quelle aber kein Fixture. Er ist
kein Testpass. Ein erfolgreicher synthetischer Test ersetzt außerdem keinen
tatsächlichen Nutzerlogin, Refresh oder numerischen Providerread.

Fortsetzung am 26.10.: R1 am integrierten Pin `582605e` baute Kilo fehlerfrei,
startete das Fixture wegen eines fehlenden privaten `/mnt`-Mounts aber nicht.
R2 korrigierte ausschließlich diesen Harnessfehler und fand dann den echten
Kilo-Fehlerfilterbefund: `InvalidDataException` bei `[]` wurde nicht abgefangen.
`b379382` behandelt diese Antworten ausdrücklich, erhält auch beim
Checkpointfehler ausgegebenes Material für Recovery und räumt einen unerwartet
fehlgeschlagenen Initialisierungstask beim Dispose auf. Beide Fehlversuche
bleiben als solche dokumentiert. Die übrigen Suiten starteten darin noch nicht.
R3 erreichte danach die Erhaltungsprüfung und fand einen Fixturefehler: Die
synthetischen Journal-IDs entsprachen nicht dem Storevertrag `source:sequence`.
`3a18f00` korrigiert ausschließlich diese beiden Testwerte. Für R4 dürfen
unabhängige, jeweils vollständig isolierte Suiten nach einem gewöhnlichen
Fixturefehler seriell weiterlaufen; das Gesamtergebnis bleibt dann fehlgeschlagen.
Build-, Source-, Isolations-, Timeout- und OOM-Fehler stoppen weiterhin den Lauf.
R4 hat Kimi (54), Foundation (226) und CLI (411 Prüfungen einschließlich der neuen
Devicefälle) erfolgreich ausgeführt. Kilo scheiterte am zweiten künstlichen
Ausgangszustand: Account- und Routeprovider waren vor dem Reconnect verschieden.
`d03ce6c` ersetzt ihn durch eine konsistente gespeicherte Legacyroute, während
der volle kanonische Katalogfall unverändert erhalten bleibt. Daraus folgt kein
Freibrief, Produktguards zu lockern; R4 bleibt insgesamt fehlgeschlagen.

R4 schloss danach auch Public-Login im tatsächlichen Hostmodus (56) und
Cloud-Connections (149) erfolgreich ab: **896 bestandene Einzelprüfungen** am
Harnesspin `3a18f00`, bei insgesamt weiterhin fehlgeschlagenem R4. Der
Desktop-Smokeharness kompilierte ohne Fehler; neun CS0649 betreffen bestehende
Fixturefelder, noch keine native Anzeige daraus behauptet.

Der zusätzliche Sourceabgleich fand einen echten Importvertragsbedarf:
`LegacyImportService` schreibt die booleschen Felder `displayNameIsExplicit` und
`sourceEnabled`. `ff5771c` erhält genau diese Felder streng typisiert auch in
Transportvarianten; andere unbekannte Felder und die bisherigen Header-/Endpoint-
Grenzen bleiben abgewiesen. Eine neue Fixture ruft den tatsächlichen Importer
mit synthetischer Quelle auf und prüft auch den deaktivierten Transport.

**R5: 176 Kilo-Prüfungen bestanden** am finalen Produktpin
`ff5771c47fd61ead38d82d294b053c0dee21c67d`, Restore/Build ohne Warnungen oder
Fehler. Die 896 anderen Checks wurden nicht wiederholt und behalten ihren
ursprünglichen Pin. Ein Dateiabgleich belegt zwischen R4 und R5 ausschließlich
Änderungen an Kilo-Helper, Kilo-Fixture und zugehöriger Dokumentation. Die
zusammen 1.072 bestandenen Prüfungen stammen aus diesen beiden getrennten Läufen.

Private Nachweise: `~/.cache/lain-qualification-20261025/next/`
`oauth-integrated-3a18f00-r4` und `oauth-kilo-ff5771c-r5`, jeweils mit
Sourceinventar, Runner, Logs, tatsächlichen Ressourcenlimits und privaten
Netz-/PID-/HOME-Nachweisen. R4 erreichte 673,3 MiB RAM, R5 396,1 MiB; beide
ohne Swap oder Speicherereignisse. Große Temporärdaten liegen im Cache.

Vollständige frische Gateway- und Monitorpakete am Produktpin `ff5771c` gebaut:
PE-/Komponentenprovenienz, elf tatsächliche Paketfälle, exakter Core-Drain und
neun Host-Modellgruppenchecks bestanden. Der laufende Vorgänger blieb während
dieser isolierten Prüfung bytegleich. Core unverändert
`191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`.
Gatewaymanifest `34c22a77949e1945c23f0a7a307d49c9bef8ef4c365ec69491857530fec7c2a8`,
Monitormanifest `2b6fa79a8bd2334bd9392991db5f63b3445c64f4f725341dbd8f43da8246a852`.
Belege: `next/oauth-full-ff5771c47fd6` unter demselben Cachepfad.
Der volle Build erreichte 1.536,7 MiB und die MemoryHigh-Drosselung, keinen
MemoryMax-/OOM-Abbruch. Native Anzeige und eigene Installation haben separate
Nachweise.

Der erste native Anlauf stoppte vor jeder GUI am Dependencyvergleich: Der
R4-Harness war framework-dependent, das Releasepaket self-contained mit
Runtimepack 10.0.11. Dieser Unterschied wurde nicht still aus dem Vergleich
entfernt. Stattdessen wurde ein eigener frischer Release-/SelfContained-
Smokeharness am Produktpin gebaut: Runtimepack 10.0.11, neun bekannte
Fixturewarnungen, null Fehler. Der unabhängige Review bestätigt identische
172 Runtime- und 15 Nativedateien; nur die belegte Root-/Referenzkonvention
des Runtimepackeintrags wird im privaten Closureoperator normalisiert.
Source-, Manifest-, Input- und DLL-Guards bleiben erhalten.

**Native R2 bestanden:** 31 Assertions je tatsächlichem RenderScaling 1 und
1,25, zusammen 62. Vier Produktionsfensteraufnahmen bei 1120×780 und 800×560
sind visuell geprüft; Gerätecode, Aktionen und manueller Link überlappen nicht
und sind horizontal vollständig sichtbar. Das kleine Fenster scrollt vertikal.
Der geladene Desktophash entspricht exakt dem Releasepaket; Core und Client
liegen paketidentisch in der unveränderten privaten Closure mit normalen
Projektbindungen. Für diese beiden Assemblies protokolliert die Fixture keinen
separaten geladenen Pfad/Hash; eine solche Messung wird nicht behauptet.
Root hat zusätzlich die 800×560-Aufnahme bei 125 Prozent selbst geöffnet.
Keine tatsächliche Provideranmeldung, physische Clipboard- oder vollständige
Tastatur-/Accessibilityabnahme. Belege: `next/oauth-device-native-ff5771c-r2/`
mit `acceptance.json` und Bildern. R1 bleibt als Fehler vor jeder GUI erhalten.
**Eigene Installation erfolgreich:** Ein einziger freigegebener Operatorlauf
aktualisierte Host, CLI und Desktop gemeinsam auf `ff5771c`. Der originale
Updatebericht endet in `verified`, Admission ist wieder offen; keine Fehler,
Reparatur, Wiederholung oder Rollback. Der neue Host und sein direkter
Coreprozess sind identifiziert, Proxy bereit, Core weiter bytegleich `191f4f17…`.
15 Accounts, 58 Routen, drei Profile und Revision 54/54 erhalten. Accounts-,
Models- und Profiles-JSON sind vor/nach dem Update bytegleich; die feste
Historienperiode 22.10.00Z–25.10.19Z ist inhaltlich gleich, Cursor 809→809.
Aktuelle Credentials, normale Codexdateien, separate Profile/Kataloge, Legacy-
und externe Integrationen sind hashgleich erhalten. Eine vollständige gestoppte
Sicherung bleibt privat im geplanten `own-service-update-backup` erhalten.

Der frisch vorbereitete Ausgangszustand hatte gegenüber dem früheren Bericht
nur normale Laufzeitdeltas: append-only Journal mit identischem altem Präfix,
zehn alte und zehn neue Diagnoselogpfade, neue Beobachtungszeit und globaler
Historiencursor 398→809. Das ist keine unveränderte gesamte Laufzeitablage.
Der eigene Updateaufruf dauerte 61,482 Sekunden, Peak laut systemd 1,5 GiB,
Swap 76,2 MiB, mit den vereinbarten Ressourcenlimits. Der letzte noch laufende
Cgroup-Snapshot zeigte keine Max-/OOM-Ereignisse; ein finaler Cgroup-Read nach
deren Entfernung wird nicht behauptet.

Die installierte CLI bestätigt lesend Kilo `device` für Inferenz/Modelle/Quota
und Kimi `kimi-device` als quota-only verfügbar. Echte Nutzeranmeldung, Refresh
und vollständige Livequalifikation sind dadurch weiterhin nicht erfolgt.
Belege im Fullpaketverzeichnis: `own-service-update-backup/update.json` und
`post-install-readonly-r1/report.json`. Der frühere fehlgeschlagene f3d-Erststart
bleibt als unveränderter eigener Fehlerbericht erhalten; dieser neue Lauf
benötigte keine Reparatur.

Nach erfolgreicher Lieferung ausschließlich elf benannte reproduzierbare
`artifacts/bin`-/`artifacts/obj`-Unterverzeichnisse alter OAuth-Zwischenläufe
gelöscht: 172.254.196 Byte logische Größe (164,27 MiB). 8.644 verbleibende
Dateien in den betroffenen Läufen sind vor/nachher SHA-identisch. Quellen,
Fehler-/Test-/Runtimeberichte, Logs und aktueller SelfContained-Harness bleiben
erhalten; vollständige Pakete, Native R2 und Sicherungen liegen außerhalb der
Löschliste. Physisch freigewordener Speicher wurde nicht gemessen. Privater
Nachweis: `next/oauth-reproducible-build-cleanup-20261026.json`.

## Separate Tarifrecherche

Der Nutzer nennt **200 EUR Gesamtbudget**, günstigster Einstieg und
Kosten-Nutzen-Stufen. [Recherche und Quellen](evaluation-minimum-plans-2026-10-25.md)
empfehlen zuerst vorhandene Rechte und nur gezielte kleine Zusatzkäufe. Das
Budget autorisiert keinen Kauf; es wurde nichts bestellt. Ein höherer Tarif
behebt keine fehlende Clientregistrierung oder Readerimplementierung.
Aktualisierte Nutzerangabe vom 26.10.: Kimi und MiMo vorhanden, aber gekündigt;
MiniMax uninteressant; Kilo vorhanden und erschöpft. Kein erneuter Kauf allein
für Quote-/Reset-/Erschöpfungsprüfungen. **Synthetic Go ist inzwischen laut
Nutzer gekauft und aktiv**; tatsächlicher EUR-Endbetrag und numerisches
Restbudget bleiben unbekannt. Der Tarifbericht enthält vier weitere bedingte
Kandidaten mit direkten Links; Requesty zunächst kostenlos beginnen. Die
Tarifbezeichnung stammt vom Nutzer. Ein nach dem Update ausschließlich lesend
abgefragter automatischer Hostcache meldet für Synthetic `plan-weekly` mit
0 Prozent Verbrauch und für OpenRouter 994/1000 verbleibende Freeanfragen;
Quellenzeit 25.10.23:52Z. Kein manueller Providerrefresh oder Inferenzrequest.
Der OpenRouter-Keycap von 20 USD ist weiterhin kein Walletsaldo.

Weitere Nutzerergänzung: Auch Requesty-Account und Guthaben sind vorhanden,
Betrag/Währung unbekannt; noch nicht mit lain verbunden. Keine weiteren Käufe
empfohlen. Nutzer möchte dafür OAuth. Der offizielle Requesty-CLIweg ist
dokumentiert, in lain aber weiterhin nicht qualifiziert/aktiviert; der
aktuelle Katalog bietet API-Key-Eingabe. Ein nutzbarer OAuthlink wird nicht
aus der bekannten Herstellerclient-ID konstruiert.

Für den konkreten Nutzerlogin danach die offizielle Requesty-CLI v0.1.16
separat vorbereitet: Release/Source `21809398e99d888565178d718186b637f3de4566`,
Archiv-SHA `d5eb9b56a4beeb298f59b7a784fcdf614548f38cb9226a6cf48d5ae1c5efee2e`,
ELF-SHA `b50ab070cdb1c1f0670613d790bbec11a8a0a2c927d29e90cab015f9bc0b6974`.
Ein privater Namespace mountet einen frischen Store am ursprünglichen
HOME-Pfad, ohne dessen Umgebungswert zu ändern; kein Zugriff auf echte
Requesty-/Codexprofile, kein Harnessaufruf. Offline Help/Loginhelp bestanden,
`--version` wird vom Hersteller nicht unterstützt; Identität stattdessen mit
Releasehash und eingebetteter VCS-Revision belegt. Vorbereitungsmountfehler
bleiben als Fehler vor jeder echten Anmeldung dokumentiert.

Nach unabhängigem Source-/Isolationsreview genau eine echte offizielle
CLI-Challenge gestartet, dynamischen PKCE-Link aus deren Browseraufruf privat
erfasst und dem Nutzer gegeben. Kein eigener OAuthclient nachgebaut. Die CLI
verwaltet State/Verifier und den zeitlich begrenzten Loopbackcallback selbst;
sie erstellt nach Nutzerzustimmung einen API-Key. **Nutzerbestätigung und
Übernahme in lain sind noch offen.** Kein Tokenexport, kein automatischer
Retry oder Profilwechsel. Auch eine erfolgreiche CLI-Anmeldung aktiviert
noch keinen allgemeinen Requesty-Loginadapter in lain.

Privater Operator/Nachweise:
`~/.cache/lain-qualification-20261026/requesty-oauth-v0.1.16-235713Z/`,
Operator-SHA `44d7bd887b043b486b596776ee29bd5c999c9d35b277a7b9d0460f087b8dcb17`,
Loginrun `login-b23413d2069c40a7`. Der Prozess wird begrenzt überwacht, ohne
Secrets auszugeben. Link, Code und Tokens gehören nicht in Projektdokumente.

## Angeforderte OpenRouter-Free-Probe

Am 26.10. zusätzlich beauftragt. Lesende Prüfung des installierten `f3d4851`
und seines aktiven Profils `lain` auf Revision 54: `openrouter-llama-3.3-free`
und `openrouter/free` sowie der OpenRouter-Anbieterrouter verwenden
`costPolicy: configured`; die Profilpolicy ist leer. Die hypothetische
`check-free`-Eignung der Llama-Route aktiviert keinen Dispatchschutz.
Der gemeldete 20-USD-Wert ist ein Keyausgabenlimit, kein Guthaben.

Der vorhandene `free_only`-Guard verlangt bei jedem Dispatch eine frische
Same-Key-Antwort mit numerischem 0-USD-Limit einschließlich BYOK. Deshalb
keine Anfrage über die ungesicherten Bestandsrouten, keine Änderung des
vorhandenen Schlüssels und keine bezahlte Ausweichroute. Für diese Liveabnahme
fehlen ein separater entsprechend begrenzter Schlüssel und eine aktivierte
geschützte Route. Es wurde kein erfolgreicher Free-Inferenztest behauptet.
