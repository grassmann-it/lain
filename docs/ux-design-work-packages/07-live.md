# UXD-07 – Flüchtige Liveausgabe mit stabiler Auswahl

Ziel: Laufende bzw. kurzzeitig erhaltene Ausgabe gut lesen und ihren tatsächlichen
Kontext verstehen. Abhängig von UXD-01; Querverbindungen zu UXD-02/04/06 auf
deren Navigationsvertrag. F10. [Übersicht](README.md).

## Referenz, Befund und Dateien

Frame 13 `t41xl53MKsTLKcXbKOmb`,
[Bild und Prüfgrenzen](../uxpilot-design-results-2026-10-24.md).
`Inspector/InspectorView.cs` hat bereits Listen-/Outputlayout, Group-/Requestpicker,
Pause/Clear, lokale Filter und `ArrangeInspector`/`FitWorkspace`. Derzeit feste
Layoutteile (260-px-Liste, Höhe 480) responsiv prüfen. Vorhandene lokale
Grenzen 500 Events/262.144 Zeichen und Hostretention nicht verändern.

Dieses Paket besitzt ausschließlich die View. Collector-/Cursor-/Retentionverträge
beim Backendowner; Navigation/Presentationwechsel bei UXD-01, Client bei UXD-00.

## Aufgaben

1. Breite stabile Liste links und zentrale Ausgabe rechts. Quelle, Retention,
   Lücken und Zustand in kurzer Zeile; technische Ereignisdetails lokal.
   Toolbar-/Footerhöhe von der verfügbaren Ausgabehöhe abziehen, keine festen
   Rendererhöhen aus dem UX-Pilot-HTML übernehmen.
2. Gemäß bestätigter Folgeauswahl A auch bei 800×560 die Requestliste links
   dauerhaft neben der Ausgabe zeigen (Referenz 200 px). Der frühere schmale
   Listen-/Outputseitenwechsel ist ersetzt; [UXD-14](14-live-pause-source.md)
   konkretisiert die Abnahme. Requestauswahl, getrennte Scrollpositionen und
   lokaler Filter bleiben erhalten; Ausgabe nicht hinter Toolbar oder horizontal
   unzugänglichen Controls verstecken.
3. Nur belegte Conversationgruppen zeigen, darin lokaler Requestpicker. Sonst
   Requestliste; keine Gruppe aus ähnlichem Text, Modell oder Zeitnähe erfinden.
   Provenienz, Identitäten und Widersprüche im Expertbereich erhalten.
4. Pause, Refresh und Clear verständlich beschriften: Pause hält die Anzeige an,
   stoppt keine Inferenz; TTL läuft weiter. Clear view löscht keine persistente
   Usage. Requeststatus, Retentionlücke, lokale Kürzung und verlorene Quelle
   unterscheidbar halten.
5. Reasoning, Output, Toolereignisse, Copy/SelectableText und technische Details
   erhalten. Kein erfundener Tokenzähler, Toolname, exakter Countdown oder
   Lebensalter ohne Quelle. Globale Such-/Statusfilter ändern keine Inferenz.
6. Präsentationswechsel in place mit UXD-01 erhalten. Nur tatsächlicher Detach
   beendet Viewpolling/Buffer; bloßer Wechsel von Simple/Advanced tut dies nicht.

## Abnahme

- Designfixture: sechs beobachtete Streams, bis 120 Sekunden Retention,
  null verworfene Events. Nur die belegte Conversation ist gruppiert,
  andere Einträge sind Requests; lokaler Picker wählt den konkreten Request.
- Singleton, ungeklärte Identität, widersprüchliche Retryeltern, active/success/
  error, leere Quelle, kein Filtertreffer und Retentionlücke bleiben unterscheidbar.
- Pause verhindert keine fortlaufenden Ereignisse auf der Quelle; Resume
  behauptet bei abgelaufener Retention keine lückenlose Nachlieferung.
  Clear verändert keine Usage-/Konfigurationsmutation.
- SourceRestart/Detach, Offline/Recovery und lokales Bufferlimit erhalten
  ihre bisherige Semantik und kennzeichnen Lücken/Kürzung.
- Bei 1120×780 und 800×560 sind Ausgabe, Requestpicker, Pause/Clear und Footer
  bedienbar. Lange Output-/Toolzeilen bleiben lesbar/kopierbar.
- Neue Events und Modus-/Dichtewechsel behalten ausgewählte ID, lokalen Filter
  und Benutzerfokus. Scroll folgt nicht zwangsweise neuen Events, während der
  Nutzer an anderer Stelle liest.
- Contextlinks erscheinen nur bei belegter Account-/Routeidentität; Back
  erhält Requestauswahl und verfügbare Ausgabe innerhalb der echten Retention.

## Grenzen und Übergabe

Keine neue persistente Konversationshistorie, Promptaufzeichnung oder Änderung
von Hostbuffer-/TTLgarantien. Simple-Live-Direktzugriff bleibt die in UXD-00/01
benannte Navigationsentscheidung, kein lokaler zweiter Shortcut.

Liefern: beide Größen und Fehler-/Lückenzustände, vollständige Eventdarstellung,
Nachweis Presentation-in-place und präzise Aussage, welche Ausgaben flüchtig sind.
