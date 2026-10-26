# Lain 1.0.0: laufende Releasearbeiten

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
Nach der gemeinsamen Integration sind neue Core-/Hostpakete und echte Abnahme
des ausgewählten Modells erforderlich. Keine neue erfolgreiche Installation behauptet.

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
