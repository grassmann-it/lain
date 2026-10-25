# UXD-15 – Settingsfehler und gemeinsame native Abnahme

Ergebnis: Settings zeigt den tatsächlich gespeicherten Zustand; die neuen
Entwürfe bestehen die Abnahme im nativen Desktop. Ergänzt UXD-08/09, ersetzt
deren bestehende Tests und Seiteneigentümer nicht. Owner Settingsdatei UXD-08;
zentrale Tests und Evidenz UXD-09, jeweilige Seitenfälle beim Seitenowner.
Voraussetzungen UXD-01/08; integrierte Ausführung nach betroffenen Folgepaketen.

## Settings

1. Drei Gruppen beibehalten: Density for Ausgangsseite, Fenster/Benachrichtigungen,
   Help/About. Genau ein Informationsmodusdropdown unten rechts in der Shell.
   Kein zweites Modus-/Theme-/Proxy-/Anbieterformular zur Füllung freien Platzes.
2. Simulierter Schreibfehler beim Einschalten Close to tray stellt den vorherigen
   gespeicherten Wert OFF wieder her; Fehler am Control, Retry ausdrücklich.
   Keine Saved-Erfolgsmeldung und kein manueller globaler Apply.
3. Traypräferenz und tatsächliche Trayverfügbarkeit unterscheiden. Bei nicht
   verfügbarem Tray normales Minimieren/Schließen erklären; Host läuft unabhängig.
   Echte Version/Lizenzen nur in About, keine erfundene Designversion übernehmen.
4. Back kehrt mit Auswahl, Draft und Scroll zur Ausgangsseite zurück. Help/About
   bleibt schmal durch Scroll erreichbar; native Checkboxen eindeutig beschriften.

## Gemeinsame Produktabnahme

- Alle gewählten Muster bei 1120×780 und 800×560 prüfen. Keine automatische
  Fensteränderung, abgeschnittenen Aktionen oder verdeckten Footer. Lange Namen,
  volle Expertenfelder und hoher Account-/Routenbestand als echte Fixtures.
- Physische Tastatur-/Pointerpfade, Fokus, Escape, lokale Menüs, Dialogfokus und
  Rückgabe zum Trigger; ⋯, Rechtsklick und Tastatur teilen IDgebundene Commands.
- Polling/Revisionswechsel bewahren Auswahl, Draft, lokale Aufklappungen und
  getrennte Scrollanker. Dialoge schreiben nie auf inzwischen andere Ziele.
- Sekundärfarbe #89938E auf #08100D liegt rechnerisch bei ca. 6,08:1, auf #0C1511
  bei ca. 5,86:1. Der pauschale UX-Pilot-Befund „unter 3:1“ wurde widerlegt.
  Tatsächliche Kombinationen einschließlich Opazität und Disabledzuständen messen;
  daraus keine pauschale Accessibilityzertifizierung ableiten.
- Kleine Labels und Trefferflächen im echten Desktop prüfen. 12 px Metadaten in
  dieser Runde sind eine Designempfehlung, keine erfundene WCAG-Mindestschriftgröße.
  Farbe allein trägt keinen Zustand. Unbestätigter Reset ist nicht automatisch Fehler.

Lieferung: `SETTINGS-NARROW-ERROR` und `-LOWER` nativ, Fehler-/Rollbacknachweis,
Feldinventar pro Editor und UXD-09-Matrix mit ausgeführten Ergebnissen und offenen
Blockern. [Galerie](../uxpilot-followup-2026-10-24.html) und
[Bewertung](../uxpilot-selection-2026-10-24.md) belegen ausschließlich Designarbeit;
keine Installation, echten Provideraktionen oder Laufzeittests in dieser Runde.
