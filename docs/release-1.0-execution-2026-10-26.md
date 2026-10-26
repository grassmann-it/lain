# Lain 1.0.0: laufende Releasearbeiten

## Aktueller Folgestand: r3-Kandidat und laufende Recherche

Der Nutzer lässt die aktive VS-Code-Recherche „Priorisiere umsatzstarke
Unternehmen“ samt ihren Workern ausdrücklich zu Ende laufen. Kein Dienststopp,
Clientreload oder Eingriff in diese Recherche. Das eigene Update wartet weiter;
isolierter Paketbau und Abnahme bleiben davon unabhängig.

Der neue private Quellstand ist
`023c87f24d73cadce187bc8a013cd9b1c5b7877d`, aufbauend auf
`5fab859b8f603b7f7ce430d0d2fff748b05bd150`. Ergänzt sind konkrete
[Zusatznotices](../packaging/licenses/review-20261026.md), Collectorprüfungen,
der getrennte [Plattforminstallervertrag](platform-installers-delivery.md)
und Dokumentation. Produkt- und Coreinputs sind gegenüber 5fab unverändert.
Die Main-Arbeitskopie bleibt uncommittet; eingefroren wurde ausschließlich im
privaten Cachecheckout. Neue Artefakte und Bindungen liegen unter
`~/.cache/lain-release-candidate-r3-20261026/`.

Die strikte Corequalifikation für den r3-Anwendungssourcecommit ist
bestanden (`core-qualification.json`). Sie verwendet ausdrücklich die
unveränderten Corebytes und die ursprüngliche 5fab-Go-Testidentität;
historische Berichte werden nicht umetikettiert. Beide r3-Pakete sind gebaut,
vollständige Archiv-/Manifest-/Sourceprüfungen bestanden. Die tatsächliche
Paketprüfung fand danach eine Vertragslücke: `check_core` akzeptierte nur die
alte Rootnotice-Kennzeichnung, der neue Collector liefert rekursive Notices.
Vor einem Runtime-/Installationsschritt abgewiesen; Fehlerartefakte bleiben
unter `full-packages/plan.json` erhalten.

Die gezielte Korrektur akzeptiert beide verifizierten Kennzeichnungen und
erhält alle übrigen Coreprüfungen. Echte Prüfaufrufe gegen das neue r3-Paket
und den historischen 5fab-Bestand bestehen. Privater Folgekandidat
`882bdadd09235b35ca74c0ebdcff59e00061b2fe` verändert ausschließlich
`scripts/core-refresh.py`; Artefakte liegen getrennt unter
`~/.cache/lain-release-candidate-r4-20261026/`. Neue strikte Corequalifikation,
beide Paketbauten, vollständige Archiv-/Source-/Manifestprüfung und elf echte
isolierte Paket-Runtimefälle sind hierfür bestanden. Report:
`full-packages/runtime/report.json`. R3 bleibt unverändert erhalten.
Manifest-SHA-256 Monitor:
`477c04f19c745014636d36122eb8d32d40f35e681353fb700c9aa82e2a15c683`;
Gateway: `e648a89ec1d545feed2d099773bda33a657dfcd760f67787afedaf45e9b573b5`.
Native Workerabnahme und Exact-Core-Drain mit den r4-Paketbytes sind ebenfalls
bestanden (`native-workers/summary.json`, `drain.json`,
`worker-checks/closure-proof.json`). Die acht gepflegten UI-Läufe (vier Fälle
bei Skala1/1,25) sind am exakten r4-Paket bestanden,54 PNGs visuell geprüft.
Bundle: `~/.cache/lain-native-r4-20261026-evidence/native-bundle-r1.json`.
Die private Sealerfunktion `native()` hat diesen Bundle gegen153 tatsächlich
gepinnte Belegdateien erfolgreich validiert; kein Gesamtseal daraus abgeleitet.
Beide vollständigen Defaultläufe sind inzwischen bestanden: jeweils698 Checks
und114 Screenshots bei Skala1/1,25. Alle228 Bilder wurden geprüft, ohne
blockierenden Befund; bekannter P3 bleibt dokumentiert. Finaler Harnesscommit
`f15ecef6d4240686ece70d761e537dff5dda74f5`, nur
`tests/desktop/{Program.cs,Fixture.cs}` weichen vom Produktstand ab.
Belege: `default-proof-r11.json`, `default-all-visual-r11/review.json` im
gleichen nativen Evidenceverzeichnis. Die zusätzliche Fixtureantwort ist exakt
`GET /api/v1/routes => Config.Routes`; kein Aufweichen der Requestallowlist.

Die beiden qualifizierten Testdateien wurden per geprüftem Dreifachabgleich
in Main übernommen. Sie sind byteidentisch zum qualifizierten Harness; der
vorherige Mainbestand ist gesichert, andere Mainänderungen bleiben erhalten.
Kein Maincommit. Importnachweis: `test-import/import-proof.json` im r4-Root.
Lokales Update und reale Modellmatrix sind noch offen. Keine Veröffentlichung.

Der Nutzer hat inzwischen das Ende der letzten externen Codexaufgabe bestätigt.
Das produktive Desktopfenster ist geschlossen. Frische öffentliche
Bestandsaufnahme und SHA-Guards für r4 sind vorbereitet, weiterhin16/59/3 und
Revision58/57. Separate sichtbare sudo-Terminalprüfung hat den lesenden
Originalprozessscanner (420 klassifizierte Prozesse) und die Bestands-/
Backendaufnahme bestanden. Belege unter `own-update-prep/scanner-v1/` und
`capture-v1/`; kein Dienststopp oder Update aus diesem Schritt.

Historisch: Der vollständige Defaultlauf bei Skala1 erreichte407 bestandene Checks und
scheiterte an einer alten Sichtbarkeits-/Textabfrage des Analysefehlers.
Screenshot belegt die kompakte „last known analysis“-Notice und die erhaltene
Gruppe. Der gesuchte ausführliche Snapshottext steht im geschlossenen Bereich
„Source & freshness“. Die gezielte Testkorrektur und erneuten vollständigen
Läufe wurden im privaten Testcheckout abgeschlossen; kein Produktpatch oder
rückwirkendes PASS für den fehlgeschlagenen Lauf.

Frischer eigener Qualifikationsseal: `own-update-prep/sealed-r1/seal.json`,
Qualification-SHA-256
`42c6777c1284c3b2da5034a7e2da244bbe9e30d21c3c718b5486a7496a7b63dd`.
Der reguläre v1-Updateplan wurde geprüft und die Ausführung gestartet. Sie
scheiterte sicher **vor dem Dienststopp** am Requestdrain; Backupjournal meldet
`failed-before-stop`, Admission ist wieder freigegeben. Installierter Bestand
bleibt unverändert. Öffentliche Codex-Metadaten belegen weiterhin aktive
Rechercheworker unter `/home/tomka/Desktop/ai`, die neue Kinder starten. Die
vermeintlich beendete Recherche ist deshalb noch kein ruhiges Updatefenster.
Der Nutzer möchte die Recherche ausdrücklich weiterlaufen lassen. Das lokale
Update wartet. V2-Scannerprüfung mit425 Prozessen bestanden; anschließender
Capture wegen fehlgeschlagener privilegierter Prozessinspektion verweigert.
Kein neuer Seal oder Dienststopp aus v2. V1-Backup-/Logpfade bleiben erhalten.

Neu beauftragt: öffentliches Repo nach Prüfung vorbereiten und umschalten,
Provider-Referralübersicht mit Bezugsstellen eigener Links sowie OAuth-Inventar
und umsetzbare Ergänzungen. Referral-/OAuthübersichten sind fertig, ebenso Plattform-Contractworkflow.
Der zusätzliche Vergleich aller27 Connectoren liegt als vorläufiger Bericht vor;
frische Preisprüfung scheiterte am Netzausfall. Sauberer separater Veröffentlichungscommit:
`5fcc7f0f52abff00b1a5c9981c34ae788bffb46b` unter
`~/.cache/lain-publication-source-20261026/`.26 gezielt übernommene Dateien,
Link-/Whitespaceprüfung und erneuter Secret-Scan ohne ungeklärte Treffer.
Repo weiterhin PRIVATE; Upload/Visibilityprüfung durch GitHub-DNSausfall
blockiert. Kein Tag/Release. Main nicht committiert.

Nachtrag zur Netzstörung: Erneute öffentliche Angebotsrecherche für alle27
Connectoren erfolgreich, verbleibende Auslese-/Tariflücken ausdrücklich benannt.
Aktuelle Zusammenfassung: [Providerempfehlungen](provider-recommendations-2026-10-26.md).
GitHubmetadaten wieder erreichbar, Remote-main weiterhin5dc916f, keine weiteren
Heads oder Tags gemeldet. Der Netzfehler oben ist historisch; noch kein lokales
Update oder öffentlicher Binärrelease aus dieser Recherche.

Die folgenden Abschnitte dokumentieren frühere Zwischenstände; dortige
Revision57/57 und Kandidat bec078e sind keine aktuellen Updateinputs.

Am 26.10.2026 hat der Nutzer die fünf zuvor beschriebenen Releasearbeiten mit
„führes aus“ zur Ausführung beauftragt. Dies umfasst die lokale Lieferung und
Abnahme sowie die Vorbereitung einer öffentlichen Auslieferung. Ungeklärte
Lizenzentscheidungen werden dadurch nicht stillschweigend getroffen.

| Arbeit | Aktueller Nachweis / nächster Schritt |
| --- | --- |
| Gesamtstand installieren | Vollpakete r4 und frischer Seal bestanden. Frische öffentliche Bestandsaufnahme:16 Accounts,59 Routen,3 Profile, Revision57/57. Neuer Prozess-/Backend-/Erhaltungsnachweis und regulärer Updateplan bestanden. Tatsächliche Ausführung scheiterte vor dem Dienststopp: `Own request drain timed out; preserve existing streams`. Admission nachweislich wieder freigegeben, Produktivinstallation unverändert. Ruhiges Runtimefenster/aktive Requests klären; keinen laufenden Stream abbrechen oder Updatefehler als Erfolg deklarieren. |
| Clients und Skills aktivieren | Neuer nativer OpenAI-Usage-Client bereits lokal installiert. Host-/CLI-/Coreupdate, reguläre Katalogexporte und projektlokale Skillinstallation stehen noch aus. Laufende IDEfenster werden erhalten. |
| Reale Modelle abnehmen | Historische59-Alias-Matrix und Folgeläufe bleiben erhalten:23 offene,7 stärker qualifizierte,29 nur historisch bestandene Aliase. Folgeplan und feste K1/K2-Kontexte implementiert,37 Offlinechecks bestanden. Neue Requests erst mit geprüftem Requestumfang und passendem installierten Stand. |
| Releaseprüfungen | Gepflegte native Szenarien bestanden; alter Default-Smoke bleibt FAILED. Testmigration auf tatsächliche Accountkarten in Arbeit, ohne Produkt-/Oraclefehler als bestanden umzuwerten. |
| Öffentliche Auslieferung | Nutzer wählte Artistic-2.0; LICENSE/NOTICE angelegt, Publisher übernimmt beide plus exakte Source. Portable Archivprüfsummen und synthetischer Sourcearchivcheck bestanden. Fotofreigabe bestätigt, Attribution Alexander Huxel · Webentwicklung Huxel mit Website und `(Flickr)` ergänzt. About-/Publisheränderungen noch Source; finaler Paketbuild und Noticeabnahme ausstehend. Keine öffentliche Veröffentlichung ausgeführt. |

Die parallelen Nebenarbeiten verwenden GPT-6.1 Sol. Große Builds und native
Runtimeprüfungen bleiben seriell über die gemeinsame Verification-Lane.
Produktivdaten werden nur über öffentliche Lain-Kommandos gelesen; bestehende
Credentials-/Konfigurationsguards werden ausschließlich als SHA geprüft.

Zusätzlicher Nutzerauftrag: [Accountnamen und Plattforminstaller](account-names-and-platform-installers.md).
Einzelaccounts werden nach Provider angezeigt; Umbenennen per Account-Menü und
CLI ist im Quellstand ergänzt. macOS-/Windows-Installer und native GitHub-Prüfung
nach öffentlicher Repositorybereitstellung sind als weitere Lieferarbeit erfasst.

Weitere ausdrückliche Erweiterung: [Quotaabhängiges Routing](collection-router-v1.md).
Echter IT-Sec-Aufruf am alten installierten Stand wählt Kimi k3 und erhält429;
öffentliche Attempts bestätigen Route/Account und ausgeschöpftes 5h-Fenster.
Luna ist per regulärer CLI in IT Sec gespeichert: Revision58, angewandt57.
Die bisherigen Erhaltungs-/Updateinputs mit57/57 sind dadurch historisch und
müssen für einen tatsächlichen nächsten Updateversuch neu erfasst werden.
Quota-Cache-Brücke und begrenzter Fallback sind im Quellstand umgesetzt und
geprüft:106 Hostchecks, vollständige Go-Adaptersuite und gezielter Router-Racelauf
bestanden. Spätere Pinmigration unterstützt nur vollständige Message-/gepaarte
Functionhistorie; opaque Reasoning-/Customtoolsätze verlangen einen neuen Thread.
Die Änderungen sind noch nicht installiert. Sie ändern
den Core: r4-Binary-/Sourcequalifikation kann dafür nicht wiederverwendet werden.
Neue Corequalifikation vollständig bestanden; Monitor-/Gatewaypakete gebaut,
Prüfsummen und elf isolierte Paketprüfungen bestanden.
[Lieferstand und Grenzen](quota-aware-routing-delivery-2026-10-26.md).
Eigener frischer Updateseal, native Paketabnahme und echter Gruppenlauf bleiben
ausstehend. Beobachtung zeigte offenes Lain-UI und7–9 aktive HTTP-Anfragen;
keine Zwangsbeendigung und keine neue erfolgreiche Installation.

Belege zur aktuellen Updatevorbereitung liegen unter
`~/.cache/lain-connect-doctor-20261026/own-update-prep-r4/` in neuen
`*-release-r1`-Pfaden. Frühere Inputs, Fehler und Seals bleiben erhalten.
Frischer Seal: `sealed-release-r1/seal.json`, Qualification-SHA
`bfb8e8926f42f93db99aaa767f033bb2615898ad066e54152f12085e2205fe51`.
Tatsächlicher Fehlversuch:
`~/.cache/lain-connect-doctor-20261026/own-service-update-backup-r4/update.json`,
Phase `failed-before-stop`, `admissionRestored=true`; keine gestoppte Sicherung
oder Paketinstallation aus diesem Versuch. Öffentlicher Status danach bestätigt
weiterhin `ff5771c`, Gateway bereit, Revision57/57. Der Inspector zeigte laufende
Codex/Sol-Attempts, aber unvollständige Ringabdeckung; daraus allein folgt keine
vollständige Liste aller aktiven HTTP-Handler oder ein bewiesener Leak.
Der vorbereitete Kandidat ist `bec078ea89623087e977768b44035a0a60fb294c`;
die Main-Arbeitskopie mit vorhandenem WIP wird nicht pauschal eingecheckt.
