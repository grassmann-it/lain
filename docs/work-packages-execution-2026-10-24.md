# Umsetzung der Account-, UX- und Stabilisierungspakete

## Auftrag und Ausgangspunkt

Der Nutzer hat am 24.10.2026 das Ziel **„setze die arbeitspakete um“** aktiviert.
Die vorherigen Planungsgrenzen sind damit für die Umsetzung aufgehoben;
inhaltliche Garantien, explizite Nichtziele und anbieterbezogene Nachweise bleiben.
Basis: `5dc916fcea702f2a196520903937570745e26dac`.

Maßgeblich sind [AC-00–10](account-connect-work-packages/README.md),
[ST-01–05 und Übergabereife](product-state-and-gaps-2026-10-24.md),
[UX-Review](ux-review-2026-10-24.md) und [Modi/Aktionen](ui-modes-and-actions.md).
Die [parallele UX-Pilot-Runde](uxpilot-design-results-2026-10-24.md) bleibt eigene
Entwurfsarbeit; konkrete Ergebnisse gegen Produktverträge prüfen und dann integrieren.

Ziel ist die durchgängig geprüfte Lieferung. Ein enger erster Durchstich mit
Kilo/Monitor reduziert Integrationsrisiko und ersetzt nicht die übrigen Pakete.
Vorhandene Accounts, Namen, Routen, Profile, Secrets und History erhalten.
Legacyproxy und normale Codexkonfiguration bleiben unberührt. Kein Kauf,
Paid-Fallback oder versteckter Inferenztest. GUI ausschließlich privat auf Xvfb.

## Koordination

Höchstens zwölf gleichzeitig arbeitende Agenten einschließlich Integrator,
eigene Worktrees ab gemeinsamem Ausgangscommit. Alle Builds, Publishes,
Installationen und Runtimeabnahmen werden durch den Integrator koordiniert.
Fachworker dürfen isolierte nichtbauende Quell-/Fixturearbeiten parallel ausführen;
Build-/Runtimefenster ausdrücklich absprechen. Commitadresse `tom@grassmann-it.de`.

Ein Eigentümer für gemeinsame Core-/Client-DTOs, Hostverdrahtung, Registry,
Projektdateien und Migrationen: Integrator. Fachworker melden nötige Änderungen
als konkrete Vertragsvorschläge. Fachliche Interfaces mit ausführbaren Beispielen
vor Integration festschreiben; keine konkurrierenden Account- oder Loginservices.
Accountbasis besitzt Secrets/Generation/Connect-Lifecycle und Quotaauflösung.
UI-Owner koordiniert Desktop, CLI, Clientwrapper und Katalogdarstellung;
Codex-Artefaktinstaller gehört ausdrücklich dem Profilpaket.

Unverwandte laufende Änderungen in `docs/product-scope.md`, Providerrecherche
und `docs/provider-work-packages/` nicht mitstagen oder überschreiben. Die
UX-Pilot-Ergebnisdateien gehören der parallelen Entwurfsrunde.

## Technische Festlegungen für die erste Integration

- Monitor/Gateway ist eine ausdrückliche Betriebswahl, unabhängig vom UI-Modus.
  Alte Konfiguration bleibt Gateway. Monitor lädt/startet keinen optionalen Core
  und erzeugt keine künstlichen Collectorfehler. Gespeicherte History bleibt lesbar.
- Neue Zugangsmethoden dürfen Accounts ohne Modelltemplate, Tarifzwang oder
  Inferenzkey erzeugen. Inferenz und Monitoring besitzen getrennte Fähigkeiten
  und gebundene Credentials; derselbe geeignete Key wird nur einmal gespeichert.
- Stabile Account-ID bei Reconnect; neue private Credentialgeneration zuerst
  sichern, dann Referenz atomar veröffentlichen. Rotation/recoverbare Remoteergebnisse
  nach AC-00/01; alte Refresh-Tokens nicht durch Rollback wiederverwenden.
- Bestehende Routenschalter bleiben gespeicherte Änderungen mit explizitem Apply.
  ST-02 ergänzt Vorschau und verlässliche Zustands-/Recoverysemantik. Eine neue
  sofortige temporäre Sperre oder mehrere gleichzeitige Clientruntimes ist kein
  stiller Nebeneffekt. Laufende Streams bei Apply sichtbar berücksichtigen.
- ST-01 erfindet keine Fähigkeiten und entfernt keine Tools unbemerkt. Bekannte
  Einschränkungen brauchen einen qualifizierten Anschluss am echten Versandpfad.
- ST-03 ergänzt bekannte Teilsummen pro Tokenkomponente samt Messabdeckung;
  vollständige Summen bleiben bei fehlender Messung null. HTTP 429 alleine wird
  nicht zu „Abokontingent erschöpft“. Nur feste bereinigte Fehlerkategorien speichern.
- ST-04 bewahrt bestehende Daten. Größenanalyse, segmentiertes Journal bzw.
  Archivierungsansatz müssen Source-/Cursor-/Replaygarantien erhalten. Keine
  automatische Löschung ohne festgelegte Aufbewahrung; Recovery zuerst qualifizieren.
- ST-05 liefert geprüfte private Linuxpakete. Öffentliche Veröffentlichung und
  Wahl einer Anwendungslizenz bleiben eigene Produktentscheidungen.

## Abnahmeverzeichnis

| Paket | Zu beweisendes Ergebnis | Stand |
| --- | --- | --- |
| AC-00/01 | Account-/Methoden-/Ownerverträge, verlustfreie Migration, Host ohne Core, Reconnect/Cancel/Rotation-Recovery | Integriert; 139 Foundationprüfungen, unabhängige Szenarien und echte Hostprozesschecks bestanden, einschließlich korrigierter paralleler Erneuerung. |
| AC-02/09 | Einheitlicher privater Desktop-/CLI-Connect, optionale Modelle, echte Fähigkeiten aller 25 Anbieter | Integriert, CLI geprüft; Connect-/Modelbildabnahme mit gezielten Nachkorrekturen läuft. |
| AC-03 | Kilo Wallet/Pass, Requesty Self, Synthetic-Erweiterung, Step-Erhalt, xAI-Metadaten | Reader und unabhängige Metadatenaktualität integriert; erweiterte synthetische Readerprüfung bestanden. |
| AC-04 | OpenRouter/Poe-Flow und Callback mit Lifecycle; Produktionsvoraussetzungen ehrlich qualifiziert | 56 echte-Host-Assembly-Fixturechecks bestanden; Poe benötigt eigene qualifizierte Registrierung, reale Logins nicht geprüft. |
| AC-05 | Isolierte offizielle CLI-Anschlüsse je Anbieter, gebundene Ergebnisse und passende Reader | Qualifikationsframework und Alibaba-Normalisierung lokal geprüft (65 Assertions); fünf reale Methoden bleiben wegen konkreter Binary-/Identitäts-/Isolationsvoraussetzungen deaktiviert. |
| AC-06 | Google/Cloudflare-Monitorquelle, Scope/Rechte und OAuthtransport separat qualifiziert | Adapter und Cloudfixtures bestanden; eigene Clientzulassung, tatsächliche Rechte und Cloudflare-Datasetqualifikation bleiben methodenspezifisch offen. |
| AC-07 | Kimi/Z.ai/Codex-Owner und Loginwege mit erhaltener Legacy-Snapshotgrenze | Lokale Parser/Verträge geprüft; unqualifizierte zusätzliche Loginwege explizit deaktiviert. Codex-Corelogin und externer Snapshot getrennt erhalten. |
| AC-08 | Begrenzte Console-Sessionintegration, MiMo zuerst, jeder Folgeanbieter separat qualifiziert | Begrenzte Reader-/Sessionverträge und lokale Fixtures integriert; kein unbewiesener produktiver Browsertransport aktiviert. |
| AC-10 | Unabhängige Offlineabnahme, Paketierung, erhaltene Installation, reale Prüfung gesondert | Unabhängige Backend- und kombinierte UI-Prüfungen bestanden; exakte Paket-/Update-/Restoreabnahme und Lieferung folgen. |
| ST-01 | Versionierte Client-/Modellfähigkeiten, tatsächliche Tool-/Streaming-/Kontextgrenzen, keine stillen Kürzungen | Transportmatrix und qualifizierte Toolgrenzen in Host/Core geprüft; kein universeller Kontext-Tokenizer oder unbelegter Providerbeweis. |
| ST-02 | Vorschau, gespeicherter/aktiver/exportierter Stand, Wiederherstellung und konsistente Clientartefakte | 92 Profil-/Recoverychecks und echte Host/Core-Drainintegration bestanden. Laufende Requests verweigern Apply mit erklärendem HTTP 409; alte Runtime, Stream und Beweisdateien bleiben erhalten. |
| ST-03 | Teilmessungen und Fehlerkategorien serverseitig, gleiche CLI/UI-Zahlen und erhaltene Replaysemantik | Integrierte Teilmessungs- und Clientchecks bestanden; schmale Usagebildabnahme wird nach Layoutkorrektur wiederholt. |
| ST-04 | Wachstum/Start/RAM gemessen; Datenpflege, konsistentes Backup/Restore, Fehler-/Recoveryfälle | Sparse Index, begrenzte Messung, nichtdestruktive Journalrecovery und echte ENOSPC auf isoliertem 1-MiB-tmpfs bestanden; vollständige Paket-/Hostzustandswiederherstellung folgt. |
| ST-05 | Aktuelle Build-/Supportidentität, wiederholbarer Release, Fresh install/Update/Rollback | Getrennte Publisher und isolierte Paketprobe vorbereitet; frische Artefakte und tatsächliche Lieferung fehlen noch. |
| UX-01 | Stabile Accountkacheln/Details, alle Fenster und richtige Budgetzustände | UXD-01/02 integriert; Geometrie, Rückweg und native Commands geprüft; abschließende Bildmatrix läuft. |
| UX-02 | Unterschiedliche Modi, kompakte Monitoransicht und gemeinsame Objektcommands | Modus-/Capabilitymatrix integriert; native Tastatur und Kontextmenü bestanden; vollständige Barrierefreiheit nicht behauptet. |
| UX-03 | Übersichtliche Models/Profiles/Usage/Live, Settings und Navigation ohne Duplizierung | UXD-03–08 integriert und sichtbare Befunde korrigiert; zusätzlich 50 Connect-/Modelchecks, 15 Profilchecks und 29 Settingschecks bestanden. |

Ein Quellenbeleg, ein implementierter Adapter, lokale Fixtureprüfung, installiertes
Paket und reales Providerergebnis bleiben verschiedene Nachweise. Fehlt eine
externe Voraussetzung, den konkreten betroffenen Anschluss und die nächste Aktion
benennen; keine funktionierende Methode vortäuschen oder Gesamtpakete als fertig
markieren. Unabhängige Arbeit weiterführen.

## Erweiterung um UXD-00–09

Der Nutzer hat die neu hinzugekommenen Arbeitspakete ausdrücklich in den laufenden
Implementierungsauftrag aufgenommen. [UXD-00–09](ux-design-work-packages/README.md)
konkretisieren daher UX-01–03; vorhandene Umsetzung wird erhalten und anhand der
14 Referenzen sowie der schmalen/Monitor-/Fehlerfälle vervollständigt. Layoutannahmen
werden als überprüfbare Umsetzungsentscheidungen geführt, nicht als bereits
bestandene Designabnahme. Die Zwölfergrenze und private Xvfb-Prüfung gelten weiter.

UXD-00 Anschlussstand: `/connection-methods`, `/accounts/connect`, Login inklusive
Resourcewahl, `/runtime-mode`, Profil-Preview/Apply und ST-03-Teilmessungen sind
im gemeinsamen Host/Client angeschlossen. Erste Hostkompilation ist bestanden;
synthetische HTTP-Gesamtabnahme steht noch aus. Modelle werden über
`POST /accounts/{id}/models` mit Revision, Accountgeneration, exakter ConnectionId
und mehreren Template-IDs angehängt; der private Key bleibt beim Host.
Die [aktuelle Anschlussmatrix](uxd-integration-2026-10-24.md) enthält konkrete
Methoden, synthetische Eingaben, Fehlersemantik und den gemeinsamen Seitenvertrag.

Dateiaufteilung der Erweiterung: Shell+Accounts ein Owner; Connect+Models beim
bestehenden Desktopowner; Profiles, Usage, Live und Settings jeweils eigene
Seitenowner; zentrale visuelle Abnahme separat. Root besitzt gemeinsame DTOs,
Hostverdrahtung und Integration. Gemeinsame Shell-/Policyhelpers nur über Owner.

## Laufende Nachweise

- Ausgangsstand erneut gelesen; keine Produktänderungen seit `5dc916f` vor diesem
  Auftrag. Vorhandene Plan-/Recherchedateien werden gezielt für Worktrees übernommen.
- Integriert sind Accountgenerationen/Recovery, Reader, Browseradapter,
  Teilmessungen, Kompatibilität, Profiltransaktionen, Corejournal und die erste
  Desktopüberarbeitung. Isolierte Workerprüfungen sind bestanden, soweit ihre
  Lieferberichte dies ausweisen; neue AC-05–08-Prüfungen teilweise noch ausstehend.
- Erster integrierter Hostbuild: 0 Warnungen/Fehler. Weitere Integration seitdem
  wird erneut geprüft. Bisher keine Installation, Änderung des vorhandenen
  Dienstes oder echte Anbieteraktion in diesem Auftrag.
- AC-05–08 liefern lokale Qualifikation und explizite methodische Gates; fehlende
  eigene Clientregistrierung, stabile Identität oder Browser-/CLI-Isolation bleibt
  an der konkreten Methode sichtbar. Ein Parser ist kein funktionierender Login.
- Core `8.0.41-lain-adapter.3`: 66 Top-Level-Go-Tests und 40 tatsächliche
  Coreprozesschecks mit Loopback als einziger Netzwerkschnittstelle bestanden.
  [Artefaktgebundene Evidence](../tests/core-stabilization/README.md) unterscheidet
  diese Prüfung von der noch folgenden gesamten Hostintegration.
- Integrierter Backendlauf unter `/tmp/lain-integrated-final-20261024/`:
  Produktionsbuild, Cloud, zwölf unabhängige Szenarien, Profilanwendung,
  Transport, Teilmessungen, CLI/Client, Reader und echte Monitor-/Restore-Hostproben
  bestanden. Neue Foundation-Erneuerungsprüfung fand einen lokalen Revisionsrace;
  Legacy-Quotaharness benötigt einen aktualisierten echten Hostprojektbezug.
  Beide Befunde werden gezielt behoben, nicht als bestandene Gesamtprüfung gezählt.
- UXD-09 prüft echte native Eingaben und PNGs getrennt. Der erste Lauf fand einen
  Usage-Logical-Parent-Fehler, vorzeitige Connect-Erfolgsmeldungen und zu wenig
  sichtbare Ergebnisfläche bei 800×560. Die Korrekturen werden anhand stärkerer
  Lesbarkeits- und Layoutassertions erneut geprüft. Frühere Capturezahlen sind
  kein Nachweis für die nachfolgend geänderte Assembly.

## Abschluss der integrierten Quellenprüfung

Die genannten Zwischenbefunde sind inzwischen gezielt behoben: 139 Foundation-,
98 Quota-, 73 Reader- und 55 Cloudprüfungen bestanden; 218 CLI-/Clientverträge
bleiben grün. Der ältere Quotatest verlangt jetzt wie AC-01 den ursprünglichen
Beobachtungszeitpunkt. Passmetadaten werden mit eigener alter Aktualität erhalten,
während das Wallet eine neue erfolgreiche Messung bekommen kann.

UXD-09 bewahrt den fehlgeschlagenen breiten Zwischenlauf und ergänzt den
gezielten Abschluss mit 8/8 Fällen und 141 Assertions. Das
[geprüfte Bild-/Eingabeprotokoll](../tests/uxd-acceptance/review.md) enthält alle
14 Referenzzuordnungen, Quellen-/Assemblyhashes und tatsächlich gesichtete PNGs.
Die anschließend ausgeführten speziellen Connect-, Profil- und Settingsfixtures
decken Modellattachment, vollständige Policybearbeitung, Clientartefakte,
Dateifehler und Neustart zusätzlich ab. Die Laufprotokolle enthalten auch die
anfänglichen Harnessfehler und deren Korrekturen.

Am 24.10. meldete der Nutzer Speicher-/I/O-Druck. Der Integrator beendete die
laufende Testserie und löschte auf ausdrücklichen Wunsch rund 6,3 GiB nicht mehr
benötigter, inaktiver temporärer Build-/Installationsdateien. `/tmp` sank von
7,3 GiB auf etwa 1 GiB. Bestehende Dienste und Proxy wurden dabei nicht verändert.
Nach ausdrücklicher Fortsetzungsfreigabe laufen weitere Builds/Prüfungen seriell
in eigener systemd-Scope mit 1,5 CPU, 1,5 GiB MemoryHigh, 2 GiB MemoryMax und
256 MiB Swaplimit, niedriger CPU-/I/O-Priorität. Temporäre Buildarbeit verwendet
einen eigenen SSD-Ordner; unbenötigte Zwischenstände werden gelöscht.

Evidenzverzeichnisse: `/tmp/lain-integrated-final-20261024/` für die ersten
Integrationsläufe und `/home/tomka/.cache/lain-qualification-20261024/` für
folgende Kandidatenprüfungen. Ein durch die Ressourcenunterbrechung abgebrochener
Lauf bleibt abgebrochen. Der erneute echte Host/Core-Draincheck bestand mit
HTTP 409 und unverändertem altem Betrieb. Paketierung/Installation sowie reale
Anbieterprüfungen bleiben getrennte noch folgende Nachweise.
