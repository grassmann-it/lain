# Journalabschnitte und sichere Rückkehr zum Flatformat

ST-04 ergänzt ein ausdrücklich gewähltes Archivformat. Bestehende Journale bleiben
unverändert im bisherigen Flatformat; weder Start noch Update konvertieren sie.
Die vorhandene Qualifikation für Sparseindex, torn append, volles Dateisystem und
gemeinsames Host-/Core-Backup bleibt gültig in ihrem dokumentierten Umfang.

## Offline kopieren und prüfen

Host und Core müssen gestoppt sein. Vor einer Adoption gehört ein vollständiges,
zusammengehöriges Backup von Anwendung, Konfiguration, Credentials, Journal und
SQLite zum Wartungsablauf. Die bestehenden Whole-tree-Backupwerkzeuge kopieren
Unterverzeichnisse rekursiv; das Archiv ist als **gesamtes Verzeichnis** zu sichern.
Die folgenden Befehle arbeiten nur an ausdrücklich genannten Pfaden und erzeugen
neue Ziele. Sie installieren oder übernehmen das Ergebnis nicht automatisch.

```sh
lain-proxy journal-inspect --journal /absolute/old/events.jsonl
lain-proxy journal-segment-copy --journal /absolute/old/events.jsonl \
  --destination /absolute/new-archive --max-segment-bytes 67108864
lain-proxy journal-inspect --journal /absolute/new-archive/events.jsonl
```

`new-archive/events.jsonl` ist ein Verzeichnis. Die Kopie bewahrt alle Recordbytes,
auch unbekannte JSON-Felder und Whitespace, sowie Source-ID, Sequenzen und Event-IDs.
Der Bericht nennt SHA-256 des vollständigen logischen Ereignisstroms, Anzahl,
Bytes, Grenze und Ziel. Das Original und seine Source-Datei bleiben unverändert.
Ein aktiver Writer, unsicherer letzter Record, Innenkorruption, vorhandenes Ziel
oder zu kleiner Bytecap verhindert die Veröffentlichung. Auch ein gleichzeitig
angelegtes leeres Ziel wird nicht ersetzt (`RENAME_NOREPLACE`).

Nach Prüfung kann ein Operator den Core ausdrücklich mit dem **neuen** Journalpfad
starten. Bei Verwendung im vorhandenen Host muss die gesamte neue `events.jsonl`-
Verzeichnisstruktur offline an dessen Journalpfad übernommen werden, während das
Original an einem getrennten Backuport erhalten bleibt. Diese Adoption ist kein
Bestandteil der Kopierbefehle und wurde nicht an Nutzerdaten durchgeführt.
Source-ID und Cursor dürfen in SQLite nicht zurückgesetzt werden. Ein passender
SQLite-Stand darf unverändert ab seinem bisherigen Cursor weiterlesen; ein
Cursor oberhalb des Journal-High-Watermarks ist weiterhin ungültig. Quota-Snapshots
bleiben am stabilen `<journal-path>.quota-observations.json`-Sidecarpfad und wechseln
bei Rotation nicht zur jeweils aktiven Datei. Kopier-/Exportbefehle übertragen
nur das Journal; eine bestehende Sidecardatei gehört zum vollständigen Statebackup.

## Format und Crashvertrag

`manifest.json`, Version 1, enthält Source-ID, maximal erlaubte Segmentbytes,
eine geordnete Liste versiegelter Dateien mit erster/letzter globaler Sequenz,
Bytegröße und SHA-256 sowie Dateiname und erste Sequenz des aktiven Segments.
`owner.lock` ist die stabile, exklusive Laufzeit-/Wartungssperre. Alle Dateinamen
sind lokale Segmentnamen; Symlinks und andere Dateitypen werden verweigert.

Die Standardgrenze beträgt 64 MiB; ausdrücklich wählbar sind 4 KiB bis 1 GiB.
Ein Record wird niemals zerteilt. Ein einzelner größerer Record wird vor dem
Schreiben abgelehnt und der Writer bleibt fehlerhaft, bis der Operator die Ursache
geklärt hat. Eine passende größere Grenze lässt sich durch Flat-Export und eine
neue Segmentkopie auswählen. Bereits bestätigte Events bleiben erhalten.

Passt ein neuer Record nicht mehr ins aktive Segment, wird dieses synchronisiert,
vollständig verifiziert und gehasht. Anschließend wird eine neue leere Datei
exklusiv erzeugt und synchronisiert. Ein neues Manifest wird in einer separaten
Datei geschrieben und synchronisiert, atomar umbenannt und das Verzeichnis
synchronisiert. **Erst danach** wird der neue Record in das neue Active geschrieben;
sichtbar wird er nach dessen erfolgreichem `fsync`.

Ein Fehler vor der bestätigten Manifestveröffentlichung sperrt weitere Appends.
Nach einem Crash sind sowohl der alte als auch der neue Manifeststand vollständig
lesbar: das bisherige Active wurde weder umbenannt noch entfernt. Ein nicht
referenziertes neues Active ist leer und wird niemals still als History übernommen.
Nicht referenzierte Dateien bleiben erhalten; ihre Untersuchung/Entfernung ist
kein automatischer Bestandteil dieses Formats. Fehler nach dem eigentlichen
Append können einen vollständigen, aber nicht bestätigten letzten Record
hinterlassen; das ist dieselbe offen ausgewiesene Commitunsicherheit wie im
Flatformat.

Beim Start werden Manifestversion, lückenlose globale Bereiche, Source-/Event-IDs,
alle versiegelten Hashes und das vollständige Active geprüft. Cursorabfragen
finden das erste Segment per Bereichssuche und lesen über versiegelte Grenzen
bis ins Active. Ein Sparseindex je Segment begrenzt Indexspeicher; versiegelte
Dateien werden nur vorübergehend geöffnet. Ein versiegeltes Segment wird vor dem
Replay erneut gehasht, damit nachträgliche Korruption keine geänderten Summen
liefert. `journal/storage` meldet Format, Anzahl versiegelter Segmente, Bytecap,
logische Bytes und sämtliche regulären Dateien auf Platte einschließlich
zurückbehaltener Publikationsreste.

Das ist **keine** Behauptung konstanter Gesamtgröße oder beschleunigten Starts:
alle Archivbytes werden beim Start geprüft, der Index wächst weiter ungefähr
mit Ereignissen/256 plus Segmentanzahl. Hashprüfung vor archiviertem Replay kostet
bis zu eine Segmentgröße pro berührtem Segment. Es gibt keine Lösch-/Retentionregel;
alle bestätigten Ereignisse bleiben erhalten. Keine automatische Löschung ist
mit diesem Format autorisiert.

## Reparatur und Rollback

Ein unvollständiger letzter Record im Active stoppt den Start. Die Wartung kann
eine neue Flatkopie aller überprüften Archive plus des vollständigen Activepräfixes
veröffentlichen:

```sh
lain-proxy journal-recover-copy --journal /absolute/new-archive/events.jsonl \
  --destination /absolute/recovered-flat
```

`inspection.json` enthält den Hash des Originals einschließlich unsicherer Bytes,
Prefixbytes, Ereigniszahl und unsichere Tailbytes. Original und Tail bleiben erhalten.
Der Operator prüft die Unsicherheit vor Adoption; Recordpräsenz beweist keine frühere
Bestätigung. Fehlende oder veränderte versiegelte Dateien, falsche Source-/Sequenzen
und kaputte vollständige Records werden nicht repariert. Hier ist ein passendes
vollständiges Backup nötig, während das beschädigte Original erhalten bleibt.

Ein altes Core-Binary kann `events.jsonl` als Verzeichnis mit `O_RDWR` nicht öffnen.
Damit scheitert ein versehentlicher Binärrollback geschlossen, anstatt unbemerkt nur
den aktuellen Abschnitt zu lesen. Vor einem Legacy-Rollback ist der explizite
Export des **aktuellen** vollständigen Archivs erforderlich:

```sh
lain-proxy journal-export-flat --journal /absolute/new-archive/events.jsonl \
  --destination /absolute/legacy-flat
```

Das Ergebnis enthält die exakten zusammenhängenden Recordbytes als `events.jsonl`,
die unveränderte `events.jsonl.source` und `archive.json` mit SHA-256 und Cursorzahl.
Es wird vor Veröffentlichung unabhängig als Flatjournal geparst und gehasht.
Alle seit der ersten Konvertierung hinzugekommenen und rotierten Events sind
enthalten. Die ursprüngliche alte Flatdatei allein wäre inzwischen veraltet und
ist **kein** zulässiger Rollbackstand. Export bestätigt nur Journalformatkompatibilität;
weitere Host-, Schema-, Credential- und Eventfeldgrenzen des Zielreleases gelten
unverändert. Insbesondere wird `prepare-legacy-rollback.py` nicht umgangen. Es verweigert ein
Segmentverzeichnis vor dem Lesen der Flat-Source-Datei mit der konkreten
Exportanweisung. Das bestehende `upgrade_probe.py`-Verfahren ist weiterhin nur für
Flatjournale qualifiziert und verweigert Segmentverzeichnisse bereits vor jeder
Serviceaktion; seine weiteren Prüfungen werden nicht aufgeweicht.

## Nachweise und Grenzen

Die neuen Go-Tests decken echte getrennte Dateien, alle Cursorpositionen einer
mehrteiligen Historie, Bytegleichheit einschließlich unbekannter Felder, Live-
Rotation, paralleles Append/Read, Quota-Sidecars, Restart, explizite Flatfortsetzung,
Schreib-/Publikationsfehler, einen konkurrierenden Zielpfad, Manifest-/Hash-/Source-
Korruption und torn-Active-Recovery ab. Abrupte Testprozesse enden an den tatsächlichen
Active-/Manifest-Synchronisationsgrenzen; diese Tests sind keine Hardware-Powerloss-
Simulation. Vorbereitete Checks gelten erst nach einem protokollierten Testlauf
als ausgeführt.

`tests/core-stabilization/archive_gate.py` prüft zusätzlich die echte Core-CLI und
HTTP-Replay-/Append-/Restartpfade in einem privaten Netzwerknamespace mit ausschließlich
Loopback. Der Bericht bindet Core-Binary, Sourcemanifest und Harness per SHA-256.
Die Probe verwendet eigene synthetische Stateverzeichnisse und mehr als 600
Records, einschließlich 600 Records in einem tatsächlich getrennten Segment.
Ein erweitertes `tests/usage/CrashProcess.cs --collect-journal` ruft den echten
`JournalUsageCollector` mit `SqliteUsageStore` auf: bestehender Flatcursor,
Sparsearchiv, viele kleine Archive, weitere Events, abrupter Restart, frischer
Collector und Flat-Export müssen dieselben Source-/Cursor-/Summen liefern.
Die bestehenden Whole-tree-Backup-/Restorefunktionen werden für das gestoppte
synthetische Verzeichnis einschließlich Archiv und echter Collector-SQLite
wiederverwendet; die wiederhergestellte Kombination wird erneut eingelesen.
Core-, Sourcemanifest-, Helper- und sämtliche Sealed-Hashes werden gebunden.
Die Probe behauptet weder die Ausführung eines alten Binaries noch eine neue
vollständige Host-Lifecycle-/Credential-Restoreabnahme. Diese bestehenden
Qualifikationen bleiben getrennte Belege.


Am 25.10.2026 wurden die integrierten Go-Tests auf Source `6adbf89` mit
Sourcemanifest `9c6bed3c8be2af1c3f98942b8402e7b49d6cbaae2ea96cde8132e1b3046739b1`
ausgeführt: **95 Top-level-Tests, 208 Passereignisse, drei Pakete, keine Fehler**;
der bestehende opt-in-Messlauf wurde erwartungsgemäß übersprungen. Alle elf
Segmentjournaltests und der neue CLI-Test einschließlich Crashsubprozessen und
Sparsegrenzen bestanden. Zwölf Upgrade- und zehn Legacy-Pythonregressionen
bestanden ebenfalls. [Beleg und Rohlogs](../tests/core-stabilization/evidence/journal-segments/unit.json).
Go 1.27.2, `-mod=readonly -p=1 -count=1 -timeout=90s`, private SSD-Vorbereitung,
Loopback-Namespace und Ressourcenlimits wurden verwendet; die vorbereiteten
Quellen wurden danach unverändert verifiziert. Die tatsächliche Core-/Collector-
Prozessprobe bleibt ein eigener, erst nach frischem Binarybuild ausführbarer Gate.
