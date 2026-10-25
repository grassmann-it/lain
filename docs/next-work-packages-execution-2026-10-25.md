# Umsetzung N01–N09

**Abschlussstand:** Die private Lieferung ist installiert und läuft mit
Revision 54/54. Maßgeblich für aktuelle Ergebnisse und offene Grenzen ist der
[Lieferbericht N01–N09](next-work-packages-delivery-2026-10-25.md).
Die Statusspalte und Zwischenstände unten sind das historische Ablaufprotokoll;
sie überschreiben den Abschlussbericht nicht. Insbesondere sind native Matrix,
CLI-Aufnahmen, MiMo-Browserstart und reale Zweiturn-Routerprobe abgeschlossen.
Der erste automatische Own-Update-Start blieb fehlgeschlagen; explizite Reparatur,
Dienstneustart und Erhaltungsprüfung bestanden danach separat.

Am 25.10.2026 hat der Nutzer die Umsetzung des
[konsolidierten Plans](next-work-packages-2026-10-25.md) als Goal mit parallelen
Workern beauftragt. Die frühere Beschränkung auf Planung ist damit für diesen
Auftrag aufgehoben. Kein Auftrag zur öffentlichen Veröffentlichung, zu Käufen,
bezahlten Fallbacks oder zum Wechsel der normalen Codexkonfiguration.

Die empfohlenen begrenzten Lösungen werden umgesetzt: bekannte kurze Anzeigenamen
ohne Bestandsmigration, zwei ausdrücklich bezeichnete Codexinstallationsziele und
bewusst qualifizierte Clientversionen mit überprüfbarem Rebind. Bestehende
Corepatches und Free-Kostenschutz bleiben erhalten. Neue providerseitige Zustimmung
und interaktive Anmeldung bleiben echte, separat ausgewiesene Schritte.

## Aufteilung

| Paket | Exklusive Zuständigkeit | Anfangsstatus |
| --- | --- | --- |
| N01 | Account-/Quota-Darstellung und gemeinsamer kurzer Anzeigename | Integriert; CLI-/Desktopqualifikation läuft |
| N02 | Synthetic- und OpenRouter-Readerkorrekturen | Integriert; 44 Reader- und 37 Gatewayprüfungen bestanden; gemeinsamer Service-/Clientanschluss separat |
| N03 | MiMo-Browser und zugehörige Fixtures | Integriert; 19 Startfixtures und Consolefälle bestanden; Browser-/Loginmethoden getrennt offen |
| N04 | Cloudflare-Readerdiagnostik und zugehörige Fixtures | Integriert; 143 Checks bestanden, optionaler IOException-Pfad korrigiert; echter Read offen |
| N05/N06 | Profiles-UI, Codexsetup und danach Gruppenverwaltung | Integriert; bestätigte Draft-/Cancel-/Referenz-/Receiptbefunde nachgeprüft behoben; native Abnahme läuft |
| N06-Vertrag | Reorder-/Gruppen-API und Clientanschluss | Integriert; Models-Suite bestanden, sicherer Applycatch angeschlossen |
| N07 | Portable Komponentenlieferung und begrenzte Diagnostik | Quellenfixes integriert und unabhängig nachgeprüft; 54 Fixtures bestanden; exakte Paketqualifikation offen |
| N08 | Codexdispatcher, Wartung und Rücknahme | Integriert; 39 Pythonfixtures bestanden, Ausführbarkeit geprüft; reale Migration offen |
| N09 | Unabhängige Integration, native Abnahme und genaue Evidenzgrenzen | Drei fokussierte Nativefälle, 30 Größen-/Modus-/Skalierungskombinationen und privater Runner integriert; native Runtime noch offen |

Root koordiniert gemeinsame Verträge, Build-/Projektdateien, Integration und
Auslieferung. Worker arbeiten in eigenen Gitworktrees unter Benutzer-Cache,
nicht auf dem Desktop. Maximal zwölf aktive Agenten einschließlich Root.
Fremde schmutzige Dokumente und gesicherte Worktrees bleiben erhalten.

## Ausführung und Evidenz

Vor parallelen Änderungen gemeinsamer Ausgangscommit. Keine konkurrierenden
Builds/Restore-/Publishes: Root vergibt einen begrenzten seriellen Slot.
GUI ausschließlich eigenes Xvfb und D-Bus; TMPDIR unter
`~/.cache/lain-build-tmp`. Die aktuelle Maschine hat Speicherdruck, deshalb erst
Quellarbeit und kleine Python-/statische Checks, danach passende begrenzte Tests.
Keine Veränderung des externen `model-usage`-Diensts.

Pro Paket werden Source, lokale Prüfungen, Paket-/Installationsergebnis und reale
Providerqualifikation getrennt nachgetragen. Offen benötigte Nutzeranmeldung oder
Accountrechte verhindern keine unabhängige Quellimplementierung und dürfen nicht
als erfolgreicher Monitoringabruf ausgegeben werden.

## Historisches Ablaufprotokoll

Gemeinsame Ausgangsbasis `7661886`. Neun Fachworker arbeiten in getrennten
Cacheworktrees, Root integriert einzelne Commits. Die installierte Anwendung
wurde bisher nicht ersetzt und kein realer Providerlogin oder Inferenzrequest
aus dieser Goalrunde gestartet. Lesender Skill-CLI-Bestand: 15 Accounts, drei
Profile, angewandte Revision 54, separates lain-Profil; feste History-Baseline
privat unter `~/.cache/lain-qualification-20261025/next/baseline`.

Die zwei ersten Readerläufe wurden mit tatsächlich geprüften Memory-/CPUlimits
ausgeführt: 192,1/193,7 MiB Peak, keine OOMereignisse, keine Buildwarnungen.
Der Benutzer-cgroup fehlt der IOcontroller; Prozesspriorität `nice 10` und
`ionice -c3` ergänzen die CPU-/Memorygrenzen. Keine fremden Dienste verändert.

Integrierte Backendprüfung läuft seriell: Models auf `e6d428b` bestanden. Der
erste ClientCLI-Lauf scheiterte an einer alten Fixture mit drei identischen
Account-IDs; ausschließlich die IDs und passende QuotaAccountId wurden in
`d590b10` berichtigt. Der ursprüngliche Fehlerlog bleibt erhalten. Fortsetzung
auf `b46b05e` mit derselben Quelle im Backend und den neuen Nativefällen.
Weitere Ergebnisse sind noch nicht vorweggenommen.

Fortsetzung abgeschlossen: ClientCLI 380 Checks, Cloudflare 143, MiMo-Start
19 Fixtures, drei Console-Gruppen, Quota 104 Assertions und AccountConnectReaders
157 Checks bestanden. Alle Builds ohne Warnungen/Fehler, maximal 461,3 MiB;
keine High-/Max-/OOMereignisse. Models lief auf `e6d428b`, die übrigen sechs
Suiten auf `b46b05e`; der ursprüngliche CLI-Fehler bleibt erhalten.
Evidence mit Einzelpins, Kommandos und Artefakthashes:
`~/.cache/lain-qualification-20261025/next/backend-e6d428b/summary.json`.

Nach Freigabe der Backendlane integriert: N08-Ausführbarkeitsprüfung;
N07-Postflight-/Drain-/Transaktionsfixes (54 lokale Fixtures, 23 davon unabhängig
wiederholt); N05/N06-Draft-/Cancel-/Referenzkorrekturen und begrenzte sichere
Receiptreads. N07-Quellenreview hat keine offenen Befunde in seinem Scope;
exakte Paket-/Dienstqualifikation bleibt offen. Die UI-Fixes werden unabhängig
nachgeprüft. Kein Ergebnis dieser Quellenreviews wird als nativer UI-Pass gezählt.

Der erste private N05-GUIversuch endete im Xvfb-Start, bevor der Harness lief;
NVIDIA/EGL und ein fehlendes privates X11-Socketverzeichnis wurden protokolliert.
Der Runnerfix schaltet in seiner Namespace auf Softwaredarstellung und erzeugt
sein eigenes Socketverzeichnis. Noch kein erfolgreicher Produkt-/Bildtest daraus.

Unabhängige Reviews haben echte Anschlussfehler aufgedeckt: Cancel nach einer
späten Dispatcherprüfung, nicht erfasste Listen-/Prepared-Drafts und ein nicht
zulässiges Retarget referenzierter Router. Die Owner korrigieren diese mit
gezielten Fixtures. Beim neuen Updateoperator wurden Postflight-Baseline,
Poststartdrain und geerbtes `LAIN_HOME` korrigiert; im neuen vollständigen
Paketpfad verlangt der Review zusätzlich dauerhafte vorbereitete Payloads und
atomaren Directorywechsel. Vor geschlossenem Review keine echte Installation.

Die integrierten Nativeharnesses bauen inzwischen erfolgreich (bekannte
Fixturewarnungen bleiben dokumentiert). N01: je 15 Assertions bei 100/125 %
bestanden. N05: neun Descriptor-/FIFO-/Receiptchecks und die dateifreie gruppierte
Vorbereitung bestanden. Weitere Fixtureläufe stoppten an fehlender
Layout-/Validierungsbereitschaft nach direkten Editoraufrufen; die ursprünglichen
Fehler bleiben unter `next/native-{5f95574,0c7393f,e1a2182}` erhalten. Synchronisation
wurde gezielt ergänzt; vollständiger Profiles-/Matrixpass steht weiterhin aus.
Der erfolgreiche Xvfb-Start ist kein visueller Pass; das bisherige schwarze
Render-Fehlerbild belegt kein geprüftes Layout.

N08 wurde real auf Wartungsschema 3 migriert, mit unverändertem qualifiziertem
Original/Patch 0.164.0 und bestandener Dateierhaltung. Ein alter eigener leerer
Lock mit Modus 0644 wurde nach Eigentümer-/Identitätsprüfung unter exklusivem
Lock auf 0600 eingeengt; der ursprüngliche vor Mutation verweigerte Versuch bleibt
erhalten. Normale Version und Unterbefehlhilfe sind bytegleich zum Original.
Der erste echte Menüversuch ist nicht nachgewiesen, ohne Inferenzprompt;
normale Konfiguration/Auth unverändert, getrennte lain-Profilmetadaten änderten
sich beim Start. Keine Rücknahme dieser aktuellen Profildaten.

N07: tatsächliches installiertes Appverzeichnis stimmt vollständig mit der
expliziten Vorgängerbaseline überein (1205 Dateien, Manifest `3ea1444b…`). Die
portablen exakten Paket-/PE-/Drainharnesses sind integriert und im engen
Quellenreview freigegeben. Frische Gateway-/Monitorpakete, deren Laufzeit und
eine eigene Dienstaktualisierung sind noch nicht als bestanden ausgewiesen.

Fortschreibung nach der exakten Paketlane: Gateway und Monitor aus `09cb8a5`
wurden frisch gebaut; Core blieb bytegleich `191f4f17…`. Elf exakte
Paket-/Transaktionsfälle, der Core-Streaming-/Draintest und neun
Host-Modellgruppenchecks bestanden. Der erste isolierte Lauf scheiterte an der
fremden Prozesssicht; der erfolgreiche zweite Lauf verwendet zusätzlich eine
eigene PID-Namespace und frisches `/proc`. Beide Ergebnisse bleiben erhalten.
Keine eigene Dienstaktualisierung daraus ausgeführt.

Die exakten Desktopbytes dieses Pakets bestanden Profiles mit jeweils 111 und
N01 mit jeweils 15 Checks bei 100/125 %. Die fokussierten nativen Zustandsabläufe
bestanden ebenfalls bei beiden Skalierungen; zwei Modellfälle bei 125 % wurden
nach Korrektur der Popup-Positionierung im Harness gezielt wiederholt. Frühere
Fixturefehler werden nicht als Produktpass umbenannt. Unabhängige Bildreviews
haben die erfolgreichen Zustände geprüft; eine nicht blockierende Abweichung
zwischen Popup-Capture und Renderbild bleibt dokumentiert.

Die Quota-Matrix bei 100 % meldet **12 PASS und 3 FAIL**: Bei Monitor 640×420
bleibt die zweispaltige Darstellung zu eng, der lange Accountbutton wird
abgeschnitten und Quota ist im ersten Blick nicht sichtbar. Dies ist ein echter
Produktbefund in allen drei Informationsstufen. Eine begrenzte responsive
Korrektur läuft; die Assertions werden dafür nicht abgeschwächt. Quota bei
125 %, tatsächliche CLI-PTY-Aufnahmen und die reale MiMo-Startprobe bleiben
offen. Erst nach Korrektur werden neue vollständige Pakete erstellt und deren
betroffene Gates erneut geprüft. Das `09cb8a5`-Paket wird nicht als fertige
Lieferung installiert.
