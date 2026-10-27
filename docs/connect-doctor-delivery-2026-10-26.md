# Connect-/Doctor-Implementierung und privater Lieferkandidat

Stand 26.10.2026. Ausdrücklicher paralleler Umsetzungsauftrag mit GPT-6.1 Sol;
Grundlage: [AP0–AP5](connect-doctor-work-packages-2026-10-26.md).

## Implementierung

- `lain doctor [--target codex-cli|codex-vscode] [--project DIR] [--profile NAME] [--json]`:
  rein lesende öffentliche Hostdaten und begrenzte lokale Metadaten. `--profile`
  bezeichnet das native Codexprofil. Accountsetup, Modellrouten, angewandtes
  Hostprofil, Exportintegrität, ausgewählte Konfiguration, Effort und laufende
  Aktivierung werden getrennt bewertet. Keine Inferenz oder automatische Reparatur.
- `lain skills list [--json]` und `lain skills install NAME --project DIR [--json]`:
  usage, workers, connect und doctor stammen aus eingebetteten Paketressourcen.
  Ziel ist `.agents/skills/NAME`; vorhandene Ziele werden erhalten. Der bisherige
  Workerinstaller bleibt kompatibel. Der Publisher bezieht seine Liste vom
  tatsächlich gebauten CLI und liefert dieselben Skillbäume mit.
- `lain-connect` und `lain-doctor` führen Einrichtung beziehungsweise Fehlersuche
  mit bestehenden öffentlichen Verwaltungsoperationen. Bereits korrekte Bindung
  ist ein No-op. Gültige Modell-/Effortwahl bleibt erhalten; ausgewählte VSIX und
  tatsächliche Fenster-/Threadaktivierung bleiben unterschiedliche Beobachtungen.

## Unlesbare Containerprozesse

Der Nutzer autorisierte sudo in einem separaten Terminal. Die erste gezielte
Prüfung identifizierte `/usr/bin/ssh-agent` und `/usr/sbin/nginx`.
`scripts/service-update.py --privileged-process-inspection` aktiviert nun einen
optionalen Metadatenweg: fester nichtinteraktiver `sudo readlink`-Aufruf,
begrenzte Ausgabe und Wartezeiten, wiederholte UID-/Startzeit-/comm-/argv-/Exe-
Snapshots. Der eigentliche Updater bleibt normaler Benutzer. Keine pauschalen
Prozessnamen-/Docker-Ausnahmen, dauerhaft erweiterten Rechte oder Passwortannahme.
Vor einem späteren autorisierten Update ist `sudo -v` im selben Terminal nötig.
Der Planmodus führt den Scanner weiterhin nicht aus.

20 synthetische Prüfungen bestanden; unabhängiger Review fand nach Korrekturen
keine weiteren konkreten P1/P2. Echte AST-isolierte Abnahme des unveränderten
Scanners als UID1000: **412 Prozesse, 78 privilegierte Reads, 1,319 Sekunden**.
Quellen-SHA256 `21142fb8ca76b1d58aabe76f4d7586856adfae6081dc9b6707043be3e3fb2bb7`.
Beleg: `~/.cache/lain-privileged-scanner-jst4cv3s/report.json`.
Snapshots sind keine atomare Prozessruhe; Popen selbst und der Gesamtscan haben
keine harte globale Deadline. Dieser Nachweis ersetzt keine exakte Updatequalifikation.

## Prüfungen

39 Skill-CLI-, 476 Client-/CLI-, 224 Worker-CLI-, 299 Worker-Host-,
98 Katalogexport- und 150 Profil-/Artifactprüfungen bestanden. Dazu Doctor-
Fixtures für Fehler, gemeinsame TOML-Abschnitte, quoted Profil-/Schlüsselnamen,
Inline-Authabgrenzung, Secrets, Sonderdateien und unabhängige Katalogvalidität.
15 Updater-, 23 Component-Delivery- und 92 Codex-Dispatcher-Tests bestanden.
Fünf unabhängige trockene Skillabläufe mit43 Fake-CLI-Aufrufen erkannten unnötige
Abfragen und eine zu allgemeine Preview-Anweisung; beide wurden korrigiert.
Das sind keine realen Provider-/Login-/Clientaktivierungsbelege.

Zwei vollständige Pakete r3 wurden gebaut;22 tatsächliche Offlinekommandos gegen
beide selbstständigen Paket-CLIs bestanden. Jeweils fünf Ressourcen bytegleich
installiert; lokale Änderungen erhalten. Der erste Monitorbuild scheiterte an
einer CLI-Abhängigkeit in der mit Desktop geteilten Datei; vor r2 korrigiert.
Der TOML-Folgefix ist in r3 enthalten und geprüft. Paketstatus und
endgültige Pins stehen in `~/.cache/lain-connect-doctor-20261026/verification.json`.

Der neue Gateway-Core reproduziert SHA256
`f737b8726b845796f38bad6f9a881bddb96e9598ea8908f9849559d91a2164cd`.
Die ursprünglichen 15 Binaryfälle, alle acht Upgrade-Gates, sechs Prozessfälle,
14 Kontextfälle, zwölf Archivfälle sowie 236 Go-Testpasses und die vorgeschriebenen
Python-Suiten bestanden am bisherigen Core. Im Provider-Gate blieb Kilo/cancel
wegen fehlendem terminalem Usage-Fakt fehlgeschlagen. Zwei gezielte Socketabbruch-
Diagnosen bestanden; sie ersetzen den fehlgeschlagenen Originalnachweis nicht.

Ein separater deterministischer Test belegt eine Corelücke: Ein nach Callerabbruch
offen bleibender innerer Streamkanal verhindert den Abschluss aktiver Attempts.
Alle acht neuen Testvarianten schlagen am bisherigen Executor fehl. Der gezielte
Fix wählt beim Empfang auch Caller-Cancellation und erreicht dadurch `After`.
Der gesamte Lainadapter besteht mit Race-Detector (13,764s); unabhängiger Review
fand keinen neuen konkreten Fehler. Bekannte Usage bleibt erhalten; erst nach
Abschluss eintreffende Usage wird abgewiesen. Provider-only-Abbruch bei
Bufferoverflow drainiert wie bisher. Das beweist noch nicht die Ursache des
ursprünglichen Kilo-Gatefehlers.

**Frische vollständige Corequalifikation r2 bestanden**, Sourcepin
`bec078ea89623087e977768b44035a0a60fb294c`, Adapterpatch5. Core-SHA256
`6b95ee24e9969b7f13d024eaea451818a1cb4f99bcaa1954601e87bb4a0ff634`,
Manifest-SHA256 `a024ac6e495e4717b6b199742dbcfb6077d467251da2356c1d234fb92b3a2d61`.
Alle acht Upgrade-Gates, sechs Prozessfälle, 27 Providercases (einschließlich
Kilo/cancel), 14 Kontextfälle, zwölf Archivfälle, 245 Go-Testpasses und beide
vorgeschriebenen Python-Suiten neu bestanden. Strikter Wrapper hat alle Rohbelege
an diese Bytes gebunden. Beleg:
`~/.cache/lain-connect-doctor-20261026/core-qualification-r2/qualification.json`.
Sämtliche Providerfälle sind synthetisch; keine echte Provider-/Loginqualifikation.
Die alten Fehlbelege bleiben erhalten. Vollpakete r4 mit diesem Nachweis werden
neu gebaut; bisherige r3-Pakete bleiben staged und unqualifiziert.

## Konkreter Synthetic-Befund und Liefergrenzen

Öffentliche installierte CLI: Synthetic-Account aktiviert, aber keine einzige
Synthetic-Modellroute konfiguriert. Deshalb fehlt der Anbieter in Codex.
Der statische Anbieter-Katalog enthält Abo-Routen für DeepSeek V4.1 Flash1017,
Kimi K3 und GLM4.7 Flash. Diese sind keine tatsächlich eingerichteten oder live
qualifizierten Routen. Keine PAYG-Umschaltung, Account-/Profiländerung oder neue
Providerinferenz wurde aus dieser Beobachtung ausgelöst.

Regulärer vorhandener Einrichtungsweg: Models → vorhandenen Synthetic-Account
wählen → „Choose provider models…“ → Synthetic/openai-chat und Tarif Go → gewünschte
Subscription-Templates hinzufügen; anschließend dem Profil zuordnen, Preview,
reguläres Apply und Codexexport. Ein neuer Login ist dafür nicht nötig. Accounts
ohne ausgewählte Modelle sind zulässig; fehlende automatische Routen sind hier
kein bewiesener allgemeiner Bug. Ein öffentlicher CLI-Attachbefehl fehlt; UI und
`POST /api/v1/accounts/{id}/models` existieren. Go-Templateauswahl ist kein belegter
serverseitiger Schutz gegen PAYG: gleicher Endpoint und Modellname beweisen keine
Abrechnungsgrenze. Kein Wechsel auf PAYG wurde vorgenommen.

Realer lesender Doctorlauf erkennt Lain als Rootprovider und die qualifizierten
Codex-/Helperbytes. Er meldet separat inkonsistenten bisherigen Export, fehlende
Account-Modellrouten und unbekannte Effort-/Threadaktivierung. Produktionsconfigs
wurden nicht automatisch repariert.

**AP1–AP4 implementiert; AP0-Prozessproblem gezielt repariert und praktisch geprüft.
AP5 ist teilweise abgenommen, nicht vollständig geliefert oder installiert.**
Arbeitskopie Main mit vorhandenem WIP erhalten; der Kandidat liegt als eigener
Commit in einer separaten Cache-Arbeitskopie. Aktiver Dienst weiterhin `ff5771c`,
Gateway bereit, Revision57/angewandt57. Finale exakte Core-/Paket-/native
Gesamtabnahme, frische Updatequalifikation und reguläre Installation bleiben offen.
Der alte NOT-VALID-Beleg bleibt unverändert. Vollständige Modellqualifikation,
Cohere-Antwortqualität und öffentliche Lizenz-/VSIX-Lieferentscheidung bleiben
weitere Releasegrenzen. Automatische Workerzerlegung und Quotaoptimierung bleiben später.

## Fortschreibung: abgeschlossene private AP5-Abnahme, noch kein eigenes Update

Die oben noch offenen r4-Paketprüfungen sind inzwischen ausgeführt: beide
Vollpakete gebaut,22 echte Offlinekommandos bestanden, alle11 synthetischen
Paket-/Update-/Fehlerfälle bestanden. Acht gepflegte native Szenarioläufe
(vier Szenarien bei100/125%) bestehen538 Prüfungen.54 Screenshots wurden
tatsächlich angesehen; eine P3-Beobachtung bleibt: Bei800px berührt rechter
Detailtext die Scrollbar, Werte und Aktionen bleiben lesbar.
Der alte generische Default-Smoke bleibt FAILED wegen eines veralteten
Expander-Oracles gegenüber den bestehenden Accountkarten. Er wurde nicht
umetikettiert; eine vollständige Migration dieser alten Probe bleibt offen.

Der echte native Workerablauf benutzt die exakten r4-CLIDLLs und qualifizierte
Codex-/Helperbytes: Readonly-Schreibverbot, isoliertes Worktree-Schreiben und
Native-/Inherited-Kontextvergleich bestanden. Neues Core-Drain-Szenario bestanden.
Echte lesende Scanner-/Dienst-/Backendidentität und Bestanderhaltung bestanden.
Ein privater Capturehelfer berücksichtigt ausschließlich den nicht fallenden
globalen Journalcursor bei identischem vollständigem geschlossenem Zeitfenster;
die strengere Prüfung des eigentlichen Updaters bleibt unverändert.

Frischer vollständiger Seal:
`~/.cache/lain-connect-doctor-20261026/own-update-prep-r4/sealed-r4-r2/qualification.json`,
SHA256 `ba567fecfc18da4983102033958852a6e4d4aa6e4300d0744095df63e2fecd12`.
Status ausdrücklich `sealed-not-executed`; kein tatsächlicher eigener
Update-/Backup-/Installationsnachweis und keine öffentliche Releasefreigabe.
GatewaymanifestSHA `f6c93b86380fe24dbca6ec1079e8f47a95b05a1630ce23f79d27bbe11e8ad205`,
MonitormanifestSHA `15c140d3739aa35d78ec7919fdaf3bbc7318f1461dbafb7f3f791d437419d5fd`.

Der Nutzer hat danach absichtlich einen nativen ChatGPT-Login ausgeführt.
Damit ist die bisherige Auth-Guardbaseline für eine Installation veraltet.
Der anschließend qualifizierte und lokal installierte native OpenAI-Usage-Client
ändert außerdem die bisherige Clientbaseline. Laufende Daemons wurden erhalten.
Die historischen Belege bleiben unverändert; vor einem tatsächlichen eigenen
Update sind die autorisierten Änderungen frisch aufzunehmen.
Der zusätzliche Auftrag für natives OpenAI-`/usage` wird im
[separaten Bericht](native-openai-usage-delivery-2026-10-26.md) fortgeführt.
