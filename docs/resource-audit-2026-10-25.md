# Ressourcen-Audit: model-usage und lain Observe

Stand: 25.10.2026. Lesender Audit nach dem Architektur-Sparring-Verfahren;
keine Implementierungsfreigabe für das externe Projekt. Geprüfter lain-Quellstand:
`5c6dfc7c1178a64b83ec4f852c1eb5b1662d9ec6`; die unten genannten Quelldateien
waren dort unverändert. Maßgebliche Grenzen stehen in
[Observability-Verträgen](observability-contracts.md) und
[Inspector-Entwurf](inspector-design.md).

## Zuordnung und Messgrenzen

Die gemeldete Belegung von mehreren GiB gehört zum **separaten Python-Dienst
`model-usage.service`**, nicht zu lains Host/Core. PID 1928 läuft mit
`/usr/bin/python3.14`; Einstieg und Arbeitsverzeichnis liegen unter
`/home/tomka/.local/share/model-usage`. Die Unit liegt unter
`~/.config/systemd/user/model-usage.service`. `lain.service` hat eine eigene
Cgroup und startet den nativen `~/.local/share/lain/app/host/Lain.Host` samt Core.
Der Python-Collector liest die bisherige Proxy-/Codex-Telemetrie; lain nutzt
seinen eigenen Core-Feed und seine eigene Usage-Datenbank.

Von Root übermittelte systemd-Stichprobe vom 25.10.2026, jeweils **gesamte
Dienst-Cgroup** einschließlich ihrer Prozesse und zugerechneten Dateiseiten:

| Dienst | MemoryCurrent | MemorySwapCurrent | MemoryPeak |
| --- | ---: | ---: | ---: |
| lain | 96.313.344 B / 91,9 MiB | 95.154.176 B / 90,7 MiB | hier nicht erhoben |
| model-usage | 3.318.218.752 B / 3.164,5 MiB | 4.321.628.160 B / 4.121,4 MiB | 8.290.652.160 B / 7.906,6 MiB |

Die frühere Stichprobe in
`~/.cache/lain-qualification-20261024/resource-compare-20261025.json` zeigte für
model-usage 5.711,6 MiB RAM und 2.315,2 MiB Swap. Der Verbrauch schwankt;
MemoryPeak ist ein bisheriger Höchststand, kein aktueller Verbrauch. Swap ist
ausgelagerter Speicher und wird hier nicht als zusätzlicher belegter RAM bezeichnet.
Eine separate `/proc/1928`-Stichprobe zeigte 1.134.124 KiB Prozess-RSS, davon
1.124.320 KiB anonym. Sie stammt von einem anderen Zeitpunkt und ist **kein
direkter Vergleichswert** zur gesamten lain-Cgroup.

## Externer Python-Dienst: belegte Wachstumsmechanismen

Die Prozesszuordnung ist belegt; die folgenden Quellbefunde erklären große
temporäre Objektmengen plausibel. Ihr jeweiliger Anteil am Peak wurde nicht mit
einem Heap-Profil gemessen. Die geprüften Python-Dateien waren älter als der
Prozessstart am 24.10.2026 um 09:26 UTC.

- [cli.py:140](/home/tomka/.local/share/model-usage/usagecore/cli.py:140) startet
  mindestens 30 Sekunden nach der letzten Zusammenfassung erneut `query(store)`,
  auch ohne geöffnete Oberfläche. Erst danach wird eine kleine Zusammenfassung
  geschrieben.
- [store.py:385](/home/tomka/.local/share/model-usage/usagecore/store.py:385)
  materialisiert vollständige Tabellen beziehungsweise Ereignisarten als Listen
  von JSON-Dictionaries. [query.py:208](/home/tomka/.local/share/model-usage/usagecore/query.py:208)
  lädt Requests und Codex-Usage vollständig, ergänzt Threads, Turns, Tools und
  Compactions und erzeugt weitere Kopien und Gruppierungen. Zeitfilter greifen
  anschließend; Pagination begrenzt nur die Ausgabe. Auch ohne Detailausgabe
  werden die Requests bei Zeile 325 vollständig sortiert.
- [cli.py:323](/home/tomka/.local/share/model-usage/usagecore/cli.py:323) führt
  dieselbe Auswertung pro v1-HTTP-Anfrage aus. Der `ThreadingHTTPServer` hat hier
  keine Begrenzung gleichzeitig laufender teurer Auswertungen. Überschneidungen
  können den Spitzenverbrauch vervielfachen.
- [codex.py:380](/home/tomka/.local/share/model-usage/usagecore/codex.py:380)
  lädt zusätzlich den gesamten Checkpoint, globale Deduplizierungs-IDs und
  Datei-/Threadinventare. Die Antwort-IDs wachsen mit der Historie und werden
  bei Zeile 558 vollständig sortiert und nach jeder bearbeiteten Datei geschrieben.
  Der Budgetwert von 5.000 Records pro Durchlauf begrenzt diese Inventare nicht.

Nur `stat`, keine Inhaltsabfrage: Die SQLite-Datei hatte etwa 4,02 GiB,
`codex-checkpoint.json` 38,2 MB und `history-checkpoint.json` 9,3 MB.
Die begrenzten Reads in [monitor.py:10](/home/tomka/.local/share/model-usage/usagecore/monitor.py:10)
beheben die Hintergrundauswertung nicht: v2/monitor liest höchstens 1.000 aktuelle
Events, 100 Requests, 500 Ownership-Zeilen und 256 Threads, während der beschriebene
Lifetime-Query weiterläuft. Eine übergroß konfigurierte SQLite-Cachegröße wurde
im geprüften Collector-Code nicht gefunden.

## lain: begrenzte Haltung, vermeidbare Allokationen

| Ebene | Quellbelegte Grenzen |
| --- | --- |
| Core-Inspector | 4.096 Events, 4 MiB serialisierte Eventgröße, 15 Minuten Alter; höchstens 256 Content-Captures und 2 KiB pro Displayzeile |
| Host-Inspector | 8.192 Events, 32 MiB serialisierte Eventgröße, 256 Streams; 1 MiB pro Stream, 256 KiB pro Event; Content 120 Sekunden, terminale Metadaten 60 Sekunden |
| Host-Reader | 500 Events pro Seite, 8 MiB Antwortgrenze, höchstens 16 Seiten pro Pass; keine überlappenden Passes |
| Desktop | 500 Events und 262.144 Textzeichen; Polling 250 ms, Snapshot höchstens einmal pro Sekunde |

Quellen: [inspector.go:66](../proxy/adapter/internal/lain/inspector.go#L66),
[integration.go:179](../proxy/adapter/internal/lain/integration.go#L179),
[InspectorStore.cs:31](../src/Lain.Host/Inspector/InspectorStore.cs#L31),
[InspectorCollector.cs:12](../src/Lain.Host/Inspector/InspectorCollector.cs#L12),
[InspectorView.cs:23](../src/Lain.Desktop/Inspector/InspectorView.cs#L23).
Diese Zahlen sind **keine Obergrenze des Prozess-RSS**: Objekte, UTF-16-Strings,
Serialisierung, Frameworks und temporäre Kopien kommen hinzu. Ablauf wird bei
Reads/Beobachtungen geprüft; die TTL ist kein Hintergrundtimer für sicheres Löschen.

Der konkrete Allokationshotspot liegt in
[inspector.go:70](../proxy/adapter/internal/lain/inspector.go#L70): Jede Verdrängung
kopiert die verbleibenden Event-, Größen- und Zeit-Slices in neue Arrays.
Bei gesättigtem Ring kann jedes neue Event fast den gesamten Ring kopieren.
[Observe:16](../proxy/adapter/internal/lain/observation.go#L16) hält während
Parsing und Append den gemeinsamen Integration-Lock. Das erzeugt vermeidbare
Allokations-/GC-Arbeit und möglichen Stream-Wettbewerb, ist aber kein Nachweis
unbegrenzt gehaltener Historie oder der externen Python-GiB-Belegung.
Zusätzliche begrenzte Kopien entstehen durch JSON-Größenberechnung und
`MemoryStream.ToArray()` im Host-Reader. Dessen 250-ms-Polling hängt im
[Program.cs:43](../src/Lain.Host/Program.cs#L43) an Gateway-Bereitschaft, nicht an
einem geöffneten Live-Fenster.

## Braucht diese GiB-Ursache einen Observe-Schalter?

**Nein.** Lains Observe an/aus ist zur Behebung der belegten externen Zuordnung
und des Python-Full-History-Pfads nicht nötig: Der andere Dienst läuft unabhängig
und erzeugt seine Zusammenfassungen auch ohne lain-Viewer. Ein lain-Schalter würde
diese Tabellenmaterialisierung und Deduplizierungsinventare nicht abstellen.
Ein späterer Capture-Schalter kann Datenschutz oder lains eigene CPU-/RAM-Kosten
adressieren, ist aber eine getrennte Produktentscheidung. Nur das Live-Fenster
zu schließen beendet die Core-/Host-Pufferung ebenfalls nicht.

Ein pauschales Abschalten von `Integration.Observe` wäre außerdem fachlich
unvollständig: Der Pfad erfasst auch erste Ausgabezeit und belegte Response-IDs
für Messung und Zuordnung. Ein etwaiger Schalter müsste flüchtige Inhaltserfassung
von diesen Metadaten trennen und dürfte Usage-/Journalgarantien nicht schwächen.

## Vorschläge und offene Beweise

Vorschläge, noch nicht implementiert oder qualifiziert:

1. Extern: Teure Accounting-Abfragen zusammenführen/serialisieren; langfristig
   inkrementelle SQLite-Projektionen und persistente Deduplizierung statt aller
   historischen Python-Objekte. Exakte ID-Zuordnung, Replay und unbekannte Werte
   erhalten; keine Historie oder Deduplizierungs-IDs zum Sparen löschen.
2. lain: Echten Ringpuffer mit konstantem Aufwand pro Append prüfen. Cursor,
   Lückenanzeige, TTL, Bytegrenzen und sofortige Referenzfreigabe müssen gleich bleiben.
3. Später im koordinierten Testslot: synthetische Allokationsmessung am gesättigten
   Ring sowie externer Accounting-Test mit wachsender künstlicher Historie und
   Parallelabfragen. Erst das quantifiziert Einsparung und Peak-Ursache; aktuelle
   Produktionsdaten müssen dafür weder vollständig geladen noch verändert werden.

Dieser Audit startete keine Builds, Lastgeneratoren oder Poller, fragte keine
Datenbanken, Provider oder Queues ab und änderte keinen Dienst. Große DB-/Journal-
Dateien wurden weder gelesen noch gehasht. Ausschließlich dieser Bericht wird
als Dokumentationsänderung geliefert.
