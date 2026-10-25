# UXD-02 – Stabile Accounts und benachbarter Inspector

Ziel: Kontingente vergleichen und einen Account bearbeiten, ohne Nachbarn zu
verschieben. Erster vollständiger UI-Durchstich nach UXD-00/01; F01–F03.
[Gemeinsame Grenzen](README.md).

## Referenz und heutiger Befund

Finale Frames 1–3: `Rf8aun5CqK1jJDDEsy1R`, `e15bU4GJVZiLJqr1AD7h`,
`ZFjD7XKb3sOKSBNNikNQ`; [Bilder und Bewertung](../uxpilot-design-results-2026-10-24.md).
Das Z.ai-Paar zeigt denselben Scrollstand 515 und unveränderte Kartenpositionen.
Dieser HTMLwert ist eine Vergleichsreferenz, kein fester Avalonia-Scrollwert.

Heute: Expander wachsen im `MasonryPanel`; Expansion löst neue Anordnung aus.
Alle Quotafenster sind bereits im Header vorhanden. Sortierung/Pins/Grouping und
eingefrorene 5h-Sortierung weiterverwenden. Ausgaben eines Einzelbudgets werden
teils zur accountweiten Warnung verdichtet; genau diese Aussage korrigieren.

Owner: `MainWindow.Accounts.cs`, `AccountTiles.cs`, `Quota.cs`, `Budgets.cs`,
`Overview.cs`; `MasonryPanel.cs` nur nach Prüfung aller Verwendungen. Shell und
Monitoringzugang an UXD-01/03 übergeben, nicht konkurrierend ändern.

## Aufgaben

1. Bestätigten breiten Entwurf A nachbilden: eine scrollbare Accountspalte und
   dauerhaft reservierter 400-px-Inspector rechts bei 1120×780. Vor Auswahl kurze
   ruhige Hilfe rechts; öffnen/schließen ändert die verfügbare Inventarbreite nicht.
2. Titel-/Detailtrigger wählt genau eine stabile Account-ID. Keine Expansion des
   Inventareintrags. Rechts eigener Scrollbereich, fester kompakter Kopf mit
   Name, Close und gemeinsamen Commands. Schmal bei 800×560 gemäß gewähltem A
   Liste und Detail nebeneinander (Referenz 340 px Liste, Rest Detail); kein
   Überdecken und kein automatischer Wechsel auf eine separate Detailseite.
3. Geschlossene Zusammenfassung verdichten: Name, gegebenenfalls Pin/Status,
   jedes gemeldete Fenster mit Rest/Reset und gültigem Verbrauchsmaß. Gemeinsame
   Aktualität einmal; lokale Abweichungen direkt an der betroffenen Zeile.
   Quellenprotokoll, IDs und Credentialtechnik in lokale Details verschieben.
4. Warnungen nach Scope bilden. „Free credits empty“ bedeutet keine erwiesene
   Accountsperre. Fälliger Reset bleibt unbestätigt, bis Quelle neue Daten liefert.
   Unbekannte Werte nicht mit Null ersetzen. Balance ohne Nenner ohne Balken;
   gemeinsam genutzte Budgets kennzeichnen und nicht addieren.
5. Inspector erhält alle bestehenden Funktionen: Refresh, Check, Pins, Enable/
   Disable, Monitoringzugang, Models, Usage, Quellen und Remove mit benannten
   Verweisen/Bestätigung. Reparaturaktion nur passend zum tatsächlichen Fehler.
   Kein Schlüsselwert in Darstellung, Kopie oder Evidence.
6. Filter/Sortierung/Grouping erhalten. Polling ändert nicht ungefragt die
   eingefrorene 5h-Reihenfolge. Explizites Aktualisieren der Reihenfolge bleibt
   erkennbar. Gefilterte/entfernte Auswahl bekommt Erklärung und sicheren Fokus;
   niemals den Inspector still an die nächste Kachel binden.

## Abnahme mit synthetischen Fällen

Fixturequelle: [Accounts-Datensatz](../uxpilot-design-prompts-2026-10-24.md#2-accounts-hauptansicht).
Designzeit 24.10.2026, 14:10 Europe/Berlin; diese Werte sind keine echten Ansprüche.

- Personal Codex: 7 Tage, 37 % verbraucht, 63 % Rest, Reset 30.10. 13:00;
  gemeinsames Budget als geteilt erkennbar.
- Command Code: alle sechs gemeldeten Fenster vorhanden. 5h: 16/20 USD-Credits,
  Reset vom Vortag unbestätigt; Weekly 18/40 mit Fehler/Stale; Monthly 60/80;
  Purchased 12 und Free 0 ohne Nenner/Balken; Model allowance unbekannt.
  Daraus entsteht weder „Account exhausted“ noch ein automatischer Paid-Fallback.
- Kimi: letztes 5h-Fenster 100 % verbraucht mit unbestätigtem vergangenen Reset;
  Monthly total 38 % und Monthly coding 82 % Rest beide geschlossen vorhanden.
- Z.ai: 5h 750/1000 Credits Rest, 7 Tage 28 % Rest ohne erfundene absolute Menge;
  beide Resets erhalten. MiMo ohne qualifizierte Quelle neutral mit Consolelink.
- Im unteren Inventar Z.ai öffnen, wechseln und schließen: Geometrie aller
  anderen Karten und Inventaroffset bleiben bei unveränderten Daten identisch.
  Breites Before/After-Paar mit denselben Daten, Größe und sichtbaren Zeilen liefern.
- 35-Accountstress, lange Namen und viele Fenster bleiben scrollbar; keine
  Fenster als „+ weitere“ verstecken. Normal/Compact reduziert keine Pflichtdaten.
- Polling, Settings/Back, Moduswechsel und Fensterverkleinerung erhalten Auswahl
  und Rückweg. Independent scroll rechts bewegt die Liste links nicht.
- Sichtbares ⋯, Rechtsklick und Tastatur liefern dieselben IDgebundenen Aktionen;
  Entfernen und Revisionskonflikt handeln nicht auf einem veralteten Ziel.

## Grenzen, offene Annahme und Übergabe

400 px und das schmale Nebeneinander sind ausdrücklich gewählte Layouts mit dem
dokumentierten Platznachteil. [Auswahl und Folgeprüfung](../uxpilot-selection-2026-10-24.md).
Volle Expertenfelder, lange Namen und native Bedienung qualifizieren UXD-01/09
vor einer vollständigen Produktabnahme. Keine Änderung an tatsächlichen Quota-
oder Routingfähigkeiten, Credentialowner, Readern oder Kauf-/Fallbackverhalten.
Fehlende Quellen auf Grundlage aktueller Fähigkeiten formulieren, nicht den
synthetischen MiMo-Isttext pauschal in eine neuere Version übernehmen.

Liefern: native breite und schmale Zustände, Vorher-/Nachher-Geometrienachweis,
Quota-/Fehlerfälle, vollständige Commandliste und dokumentierte Abweichungen.
