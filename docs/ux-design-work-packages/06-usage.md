# UXD-06 – Usage mit sichtbaren Ergebnissen und ehrlicher Messung

Ziel: Auswertung sofort erfassen, danach gezielt filtern und Messgrundlagen
öffnen. Abhängig von UXD-01; neue Teilmessungswerte aus ST-03/UXD-00. F07.
[Übersicht](README.md).

## Referenz, Befund und Dateien

Frame 12 `VFZv56jHJJ2zSdBAQhNG`,
[PNG und Zahlenprüfung](../uxpilot-design-results-2026-10-24.md).
`Usage/UsageView.cs` und `UsageView.Analysis.cs` besitzen Filterdrafts, angewendete
Query, Quellen-/Freshness-/Coveragezustand, mehrere Aufklappbereiche, Token-/
Performanceanalyse und Kontextnavigation. Owner dieses Pakets: beide Dateien.
Geteilte Observabilityclientänderungen gehen an UXD-00, Shellcapture an UXD-01.

## Aufgaben

1. Kernfilter und angewendeten Kontext kompakt oben, danach Summen und Beginn
   der Aufschlüsselung im ersten großen Viewport. Lange Datums-/Dimensionswahl,
   Messgrundlagen und Fehlerdetails lokal aufklappen. Keine zweite konkurrierende
   Breakdownansicht erzeugen.
2. Offene Filtereingabe und angewendete Query ausdrücklich getrennt halten.
   Polling arbeitet auf dem angewendeten Stand und überschreibt keinen Draft.
   Zeitbereich inklusive Zeitzone und exklusivem Enddatum nachvollziehbar machen.
3. Requests, Attempts, historische Fehler und Completion errors richtig benennen.
   Fehlerquote nur mit ihrem tatsächlichen Nenner. Fehlende Messung bleibt null;
   bekannte Teilsummen mit Coverage statt scheinbar vollständiger Gesamtsumme.
   Nicht gemeldete Tokenkomponenten oder Preise nicht ergänzen.
4. Gruppenzeilen und Drilldown direkt erreichbar machen. Vorhandene Dimensionen,
   Quellen/Attribution, Reasoning-/Cachewerte, Performance und Trunkierung
   vollständig erhalten; keine Felder nur wegen der ruhigeren Simpleansicht verlieren.
5. Diagramm und Performance mit ihrem tatsächlichen Scope beschriften.
   Gruppenfilterung darf unverändert globale Tages-/Performancewerte nicht als
   gruppenlokal präsentieren. P50/P95 und gemessene/eligible Attempts zusammen zeigen.
6. Empty, no match, partielle Messung, Pollingfehler, Sourcewechsel und Offline
   unterscheiden. Letzten Wert nur als letzten bekannten Stand zeigen. Quelle/
   Cursorwechsel nicht als nahtlose Vollständigkeit ausgeben.

## Abnahme

- Vollständige synthetische Designfixture: 128 Requests, 132 Attempts,
  4 historische Fehler (3,0 %), 81.986 Tokens. Sofort sichtbare Breakdownzeilen:
  Personal Codex 70/72/2/43.200, Command 30/32/1/21.000,
  Kimi 28/28/1/17.786. Zeilenfehler sind ausdrücklich Completion errors;
  nicht stillschweigend mit historischen Fehlern gleichsetzen.
- Zeitraum Europe/Berlin, Ende 25.10. exklusiv. Globale First-output-Werte:
  P50 240 ms, P95 510 ms, Coverage 120/132. Tagesreihe und Performance bleiben
  als „alle Accounts“ erkennbar, solange die Quelle globale Werte liefert.
- Separate Teilmessungsfixture enthält vollständige und unbekannte Komponenten;
  Coverage/Teilsumme bleibt von vollständigem Total unterscheidbar.
  429 alleine wird nicht als erschöpftes Abokontingent erklärt.
- In 1120×780 ist der Beginn der Auswertung ohne Öffnen eines Expanders sichtbar.
  In 800×560 bleiben Filter, Kontext und Ergebnisrückweg ohne Überlappung bedienbar.
- Polling bei fokussierter Eingabe, Modus-/Dichtewechsel und Settings/Back
  erhalten Draft, angewendeten Filter, lokalen Drilldown und Fokus.
- Gruppe/Attempt → Account/Model → Back erhält Zeit-/Filterkontext und Scroll;
  historische Daten werden nicht aus aktuellen Quotaänderungen rekonstruiert.
- Sourcefehler, Sourcewechsel und partielle Analyse liefern keine erfundene
  Vollständigkeit; Trunkierung bleibt am betroffenen Ergebnis erkennbar.

## Grenzen und Übergabe

Keine neue Usage-Datenbank, Fehlerklassifikation oder Messschätzung. ST-03
liefert fachliche Teilsummen-/Fehlerverträge; bestehende Funktionen erhalten,
neue Felder erst mit echtem Anschluss darstellen.

Liefern: breite/schmale Ergebniszustände, vollständige/partielle/fehlerhafte
Fixtures, Feld-/Scopezuordnung und Nachweis unveränderter Filter-/Drilldownsemantik.
