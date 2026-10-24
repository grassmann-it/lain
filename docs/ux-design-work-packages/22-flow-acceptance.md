# UXD-22 – Native Abnahme der Zustandsfolgen

Ergebnis: Die neuen Randfälle funktionieren mit echten nativen Eingaben und
korrekten Requests. Bildvergleich, Funktionsnachweis und begrenzte Blocker sind
unterscheidbar. Ergänzt UXD-09/15; [gemeinsame Grundlage](16-state-continuity.md).

Owner: bestehender UXD-09-Testowner für `tests/desktop/Program.cs`, `Fixture.cs`,
Fixtures/README und `tests/presentation/*`. Native Harnessergänzungen nach deren
bestehendem Testvertrag. Produktfixes bleiben bei UXD-17–21 beziehungsweise
deren bestehenden Seiten-/Shell-/AC-/ST-Ownern.

Voraussetzungen: Szenarien/Fixtures können früh vorbereitet werden; tatsächliche
Ausführung nach dem jeweiligen Fachdelta. Für Draft-/Simple-Navigation nur die
jeweils vom Nutzer gewählte Variante abnehmen. Fehlende Wahl blockiert diesen
Gestaltungsnachweis, keine unabhängigen Account-/Model-/Setup-/Applyfälle.

## Prüfmatrix

| Strecke | Beobachtbarer Pflichtfall | Paket / Designreferenzen |
| --- | --- | --- |
| Navigation | Draft erhalten/verwerfen/zurück ohne Save/Apply; gewählte Simpleziele; Dichte/Stufe ohne Kontextverlust | UXD-17: `DRAFT-LEAVE-A` oder `DRAFT-LEAVE-B`, `SIMPLE-NAV-A` oder `SIMPLE-NAV-B`, `GATEWAY-BAREBONES`, `GATEWAY-ADVANCED-COMPACT` |
| Accounts | Erster Snapshot vs leer/Fehler; letzte Werte während Refresh; Antwort A überschreibt keine Auswahl B; alle gemeldeten Fenster geschlossen sichtbar | UXD-18: `ACCOUNTS-FIRST-LOAD`, `ACCOUNTS-REFRESH-BUSY` |
| Models | Command-ID bei Polling/Auswahlwechsel; Profil-/Fallbacksperre; Filterreset; verschwundene ID mit erhaltenem Draft | UXD-19: `MODEL-MENU`, `REMOVE-REFERENCED`, `MODELS-NO-MATCH`, `MODEL-SELECTION-GONE` |
| Connect/Setup | Fehlende Voraussetzung vor Login; Validierung vor Prepare; Prepared ohne Dateien; Install getrennt von Apply | UXD-20: `FIRST-START`, `CONNECT-PREREQUISITE`, `CODEX-SETUP-VALIDATION`, `CODEX-EXPORT-PREPARED` |
| Apply | Alter Diff gesperrt, Ziel-/Revisionswechsel, verlorene Commitantwort, Statusklärung in Detail/Liste/Shell | UXD-21: `APPLY-STALE-INPLACE`, `APPLY-RECOVERY-INPLACE` |

Alle 18 Designnamen sind damit zugeordnet. Drei bestätigte Referenzen für
Accounts schmal, Applyreview B und Live schmal A aus der Galerie bleiben
Regressionsgrundlage. Nicht gewählte Alternativen bleiben Designhistorie;
die Matrix fordert weder beide Implementierungen noch neue Start-/Modusseiten.

## Vorgehen und Fertigkriterien

1. Vorhandene [Desktop-/Präsentationsprüfung](09-acceptance.md) weiterverwenden,
   nicht dieselben Cases erneut aufbauen. Fehlende Fälle an realen Übergängen
   ergänzen: verzögerte/anders geordnete Antworten, Revision nach Preview,
   gelöschte ID und getrennte Statussicherheit. Fixtures vollständig synthetisch.
2. 1120×780 und 800×560 prüfen. Native Controls, unveränderte Katze, Mint,
   Profiles direkt im Gateway, ein Modusdropdown rechts, sichtbare Aktionen.
   Liste/Detail und ihre Scrollanker bleiben stabil; Fenstergröße ändert sich
   nicht automatisch. Galeriebreite 390 px ist keine Produktmindestgröße.
3. Tatsächliche Tastatur-/Pointereingaben für Menü, Escape, Dialog beziehungsweise
   nichtmodales Review, Browse/Inlinefehler und Fokusrückgabe nachweisen.
   Lange Labels und verfügbare DPI-/Schriftvergrößerung gezielt prüfen;
   tatsächliche Skalierung und Methode benennen. Farben allein tragen keinen Status.
4. Mutationen und ihre Abwesenheit über Fixture-/Requestprotokoll belegen.
   Für Installer ausschließlich bestehende isolierte Testumgebung verwenden;
   keine echten Accounts, Providerlogins, Benutzerkonfiguration oder Dienste
   als versteckte Voraussetzung. Keine pauschale Accessibilityqualifikation
   aus Bildern oder Strukturtests ableiten.
5. Pro Fall: Commit/Assembly und relevanter WIP, Fixture, Fenstergröße,
   Betrieb/Stufe/Dichte, Eingabemethode, erwartetes/tatsächliches Ergebnis,
   Request-/ID-/Revisionsbezug, Bildlink und offene Abweichung dokumentieren.
   PNGs selbst sichten. Fehlende native Eingabe nicht als bestanden markieren.

Lieferung: ergänzter UXD-09-Abnahmebericht und Evidenzmanifest, jeder Matrixfall
bestanden oder konkret blockiert/fehlgeschlagen, zugehöriger Seitenowner genannt.
Keine Abschlussmeldung für die Gesamtstrecke bei offenen Pflichtfällen.
Produktprüfungen sind **geplant und in diesem Planauftrag nicht ausgeführt**;
Installation, Dienstwechsel und reale Providerqualifikation bleiben getrennte Aufträge.

[Alle finalen IDs und PNGs](../uxpilot-flows-2026-10-25.json),
[Galerie-/HTML-Nachweise und fachliche Bewertung](../uxpilot-flows-2026-10-25.md).
