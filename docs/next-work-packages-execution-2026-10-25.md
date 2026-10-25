# Umsetzung N01–N09

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
| N03 | MiMo-Browser und zugehörige Fixtures | Integriert; unabhängiger enger Quellenreview ohne Zusatzbefund; Fixture-/Browsergate offen |
| N04 | Cloudflare-Readerdiagnostik und zugehörige Fixtures | Integriert; optionaler IOException-Fehlerpfad nach Review korrigiert; Runtime-/Realread offen |
| N05/N06 | Profiles-UI, Codexsetup und danach Gruppenverwaltung | Integriert; bestätigte Draft-/Cancel-/Referenz-/Receiptbefunde werden vor Abnahme korrigiert |
| N06-Vertrag | Reorder-/Gruppen-API und Clientanschluss | Integriert; Models-Suite bestanden, sicherer Applycatch angeschlossen |
| N07 | Portable Komponentenlieferung und begrenzte Diagnostik | Erste Quelle integriert; unabhängiger Review blockiert echte Ausführung bis Transaktions-/Poststartfixes abgeschlossen sind |
| N08 | Codexdispatcher, Wartung und Rücknahme | Integriert; 38 Pythonfixtures bestanden; Ausführbarkeitsprüfung und reale Migration noch offen |
| N09 | Unabhängige Integration, native Abnahme und genaue Evidenzgrenzen | Drei fokussierte Nativefälle und privater Runner integriert; native Runtime noch offen |

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

## Laufender Zwischenstand

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
