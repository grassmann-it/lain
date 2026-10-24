# UXD-18 – Erste Accountdaten und lokaler Refresh

Ergebnis: Noch keine Daten, wirklich leeres Inventar und Aktualisierung letzter
bekannter Werte sind unterscheidbar. Accountwechsel bleibt während lokalem
Refresh möglich. Ergänzt UXD-02/10; [gemeinsame Grundlage](16-state-continuity.md).

Owner: UXD-02 für `src/Lain.Desktop/MainWindow.Accounts.cs`,
`MainWindow.AccountTiles.cs`, `MainWindow.Quota.cs`, `MainWindow.Budgets.cs`.
Refresh-/Busyänderungen in `MainWindow.cs` integriert ausschließlich UXD-01;
Client-/Vertragsänderungen über UXD-00 und bestehende Quotaowner.
Voraussetzung: bestehende Shell, gewähltes Accounts-Master/Detail und echte
Account-/Quotasnapshotverträge. Keine Abhängigkeit von den offenen UXD-17-Varianten.

## Bestandsabgleich und Aufgaben

1. Neuere Account-/Quotahelpers und vorhandene stabile Sortierung zuerst lesen.
   Alle gemeldeten Quotafenster verwenden; keine neue Prozent-/Freshnesslogik
   neben den vorhandenen Helpers. Bereits erfüllte Fälle mit Evidenz übernehmen.
2. Ausstehender erster Konfigurationsabruf, erfolgreich leeres Inventar,
   fehlgeschlagener Abruf und fehlende Quota eines bekannten Accounts erhalten
   eigene Aussagen. Vor der Antwort weder „keine Accounts“ noch Hostfehler
   behaupten. Liste und Detail behalten ihre reservierte Geometrie.
3. Accountgebundenen Refresh als lokale Beschäftigung zeigen. Vorhandene Werte
   und ihr Alter/letzter bekannter Status bleiben sichtbar; kein Nullsetzen,
   Refill oder geschätzte Dauer. Nur betroffene Refreshaktion sperren;
   Navigation ermöglichen, ohne globalen Mutationsschutz pauschal abzuschalten.
4. Ergebnis und Fehler an die angefragte Account-ID binden. Späte Antwort von A
   darf nach Wechsel zu B dessen Details nicht überschreiben. Auswahl, lokale
   Aufklappungen und getrennte Scrollanker erhalten. Entfernten Account ehrlich zeigen.
5. Alle bekannten Fenster auch in geschlossenen Accountzusammenfassungen zeigen.
   Prozentbalken nur mit belastbarem Nenner; unbekannt, Betrag ohne Nenner,
   vergangener unbestätigter Reset und gemeinsames Budget behalten ihre Bedeutung.

## Geplante Abnahme und Lieferung

- Ersten Snapshot im synthetischen Client verzögern; danach jeweils leer,
  Daten oder Fehler liefern. Zustände und nächster Schritt passen zur Antwort;
  kein erfundener Accountcount oder 0-%-Balken während des Wartens.
- Personal Codex mit bekannter 7-Tage-Fixture 37 % verbraucht / 63 % verbleibend
  und Reset 30.10. 13:00 am Fixturetag 24.10. aktualisieren. Werte und Balken
  bleiben in Liste und Detail während Busy und bei Fehler erhalten.
- Während Refresh A → B wechseln, Antworten/Fehler verspätet liefern; keine
  falsche Zuordnung oder globale Navigationssperre. Mehrere gemeldete Fenster
  sowie unbekannte Werte und fehlende Nenner als zusätzliche Fixtures prüfen.
- Beide gewählten Accountgrößen, langer Name und Inventarscroll: keine
  Nachbarverschiebung, keine verdeckten Aktionen. Lieferung mit IDs, Zustandsfolge,
  nativem Vorher/Während/Nachher-Bild und Requestzuordnung an UXD-22.

Designs: `ACCOUNTS-FIRST-LOAD`, `ACCOUNTS-REFRESH-BUSY`; die vier
Navigations-/Dichtestudien in UXD-17 zeigen zusätzlich korrigierte geschlossene
Quotazusammenfassungen. [Bilder, finale IDs und Bewertung](../uxpilot-flows-2026-10-25.md).
