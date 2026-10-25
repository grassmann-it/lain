# UXD-14 – Live bei Pause, Retentionlücke und Sourcewechsel

Ergebnis: Flüssige Lesefläche mit stabiler Auswahl; Pausieren behauptet weder
gestoppte Inferenz noch garantierte Nachlieferung. Ergänzt UXD-07. Owner bleibt
`Inspector/InspectorView.cs`; Collector-/Cursor-/Retentionvertrag beim Backendowner.
Voraussetzungen UXD-01/07; keine neue History oder Änderung der Retention.

## Aufgaben und bestätigte Darstellung

1. Bestätigte Folgeauswahl A: `LIVE-NARROW-A`, auch bei 800×560 dauerhafte
   Requestliste links (Referenz 200 px) und Ausgabe rechts. Kein automatischer
   Wechsel auf Selector B oder getrennte Listen-/Outputseiten. Die frühere
   Empfehlung B wurde nicht übernommen; der Preis ist weniger Ausgabebreite.
   Ein lokaler Picker innerhalb einer belegten Conversation ersetzt die sichtbare
   linke Liste nicht. Die Wahl ändert keine Simple-/More-Navigationsentscheidung.
2. Display paused bedeutet nur Anzeige angehalten. Retention läuft weiter;
   Resume kann eine Lücke haben. Nur vorhandene Ausgabe zeigen, keine erfundene
   Komplettierung, Ereignisanzahl, Zeitmarke, Tokenzahl oder Toolargumente.
3. Sourcewechsel bindet eine alte Auswahl nicht allein wegen gleicher Request-ID
   an die neue Quelle. Alten lokalen Ausschnitt klar als vorherige Quelle zeigen,
   aktuelle Verfügbarkeit prüfen und sichere Neuauswahl ermöglichen.
4. Nur belegte Conversation gruppieren; zweiter Requestpicker nur bei tatsächlich
   mehreren Requests der Gruppe. Clear view verändert keine persistente Usage.
   Folgeausgabe springt nicht gegen die bewusste Leseposition.

## Abnahme

- `LIVE-NARROW-A/B`: gleiche sechs beobachtete Streams und Retention bis 120 s;
  Pause mit abgelaufenen Ereignissen. Keine Behauptung „sechs Events verloren“
  aus der Streamanzahl ableiten. Requeststatus bleibt letzter beobachteter Status.
- `LIVE-SOURCE-CHANGED`: altes `req-demo-02` nicht als aktuelles Ziel öffnen;
  Wiederaufnahme liefert keine unbelegte lückenlose Fortsetzung.
- Identische IDs aus verschiedenen Quellen, unbewiesene Gruppen, Requestwechsel,
  Clear, Detach, Bufferlimit, Offline/Recovery und Follow-Ausgabe native prüfen.
- Bei 800×560 bleiben Pause/Resume, Picker, Ausgabe, Clear und Shellfooter
  bedienbar. Linke Requestliste und rechte Ausgabe sind gleichzeitig sichtbar;
  eigene Scrollpositionen bleiben bei Request-/Moduswechsel erhalten. Lange
  Output-/Codezeilen sind lesbar und kopierbar; Controls werden nicht verdeckt.
  Tastaturwechsel zwischen Liste und Ausgabe, Fokus, Scroll-Erhalt und
  Presentation in place an UXD-09 liefern.

[Bilder](../uxpilot-followup-2026-10-24.html),
[Design-IDs](../uxpilot-followup-2026-10-24.json). Lieferung: bestätigte schmale
Liste/Ausgabe A, Source-/Identitätszuordnung, Gap-/Buffer-/Focusnachweise. Screens sind
keine Evidenz für echte Collector-/TTLgarantien oder implementierte neue Fähigkeiten.
