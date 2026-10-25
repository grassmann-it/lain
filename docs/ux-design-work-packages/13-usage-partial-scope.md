# UXD-13 – Usage bei Teilmessung, Filterdraft und Sourcefehler

Ergebnis: Gewählte Ergebnistabelle plus Seitendetail zeigt bekannte Werte,
unbekannte Komponenten und ihren Scope verständlich. Ergänzt UXD-06/ST-03.
Owner: `UsageView.cs`/`UsageView.Analysis.cs`; Messverträge ST-03, Client UXD-00.
Voraussetzungen UXD-01/06 und tatsächliche Komponenten-/Coveragefelder.

## Aufgaben

1. Bekannte Tokenteilsumme ausdrücklich als solche zeigen; fehlende Komponente
   bleibt Unknown, niemals 0. Requests/Attempts/Fehler können dabei vollständig
   sein. Keine frei erfundene Prozent-Coverage oder hochgerechnete Gesamtsumme.
2. Fixture `USAGE-PARTIAL`: 43.200 + 21.000 = 64.200 bekannte Tokens, Kimi Unknown.
   128 Requests, 132 Attempts, vier historische Fehler (3,0 % der Attempts).
   Breakdown nennt ausdrücklich Completion errors, keinen gleichgesetzten Fehlertyp.
3. First-output P50 240 ms/P95 510 ms ist in dieser Fixture global über alle
   Accounts, 120/132 geeignete Attempts gemessen. Gruppenauswahl macht diese
   Werte nicht gruppenlokal. Keine aktuellen Providerquotas in Usage einmischen.
4. Filterdraft Kimi ist nicht die angewendete All-accounts-Query. Explizites
   Apply filters ändert die Query; Polling bewahrt den Draft. Zeitraum inklusive
   Europe/Berlin und exklusivem Ende bleibt im Editor nachvollziehbar.
5. Collector nicht erreichbar, Sourcewechsel, fehlende Messung, leeres Ergebnis
   und kein Filtertreffer bleiben unterscheidbar. Letzte Werte als letzte Werte
   markieren; Source-/Cursorwechsel verspricht keine lückenlose Fortsetzung.

## Abnahme und Lieferung

- Vollständige und partielle Fixtures separat: keine 81.986 Total bei unbekannter
  Kimi-Komponente. Null/Unknown dürfen durch Formatierung nicht zusammenfallen.
- `USAGE-NARROW` behält bei 800×560 Tabelle und Seitendetail; alle Kernspalten,
  Quellenkontext, Kontextaktion und More filters bleiben erreichbar.
- Querywechsel, Polling bei fokussierter Eingabe, Sourcefehler/-wechsel und
  Account/Models → Back erhalten angewendeten Kontext, Draft, Auswahl und Scroll.
- Feld-/Scopezuordnung sowie native breite/schmale Fehlerbilder und tatsächliche
  ST-03-Nachweise liefern. Keine neue Datenbank, Schätzung oder Fehlerklassifikation.

[Galerie](../uxpilot-followup-2026-10-24.html),
[Manifest](../uxpilot-followup-2026-10-24.json). Das Layout A ist bestätigt;
neue Fehlerdarstellung ist ein Vorschlag. Produktlaufzeit wurde hier nicht getestet.
