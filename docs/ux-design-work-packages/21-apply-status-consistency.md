# UXD-21 – Veraltete Applyvorschau und unbekannter Ausgang

Ergebnis: Die gewählte Reviewfläche im Profildetail bleibt auch bei Konflikten
nutzbar. Shell, Profilliste und Detail widersprechen sich nicht über den
bestätigten aktiven Stand. Ergänzt UXD-05/12; [gemeinsame Grundlage](16-state-continuity.md).

Owner: UXD-05 für `src/Lain.Desktop/MainWindow.Profiles.cs`; Shellstatus in
`MainWindow.Workspace.cs`/`MainWindow.cs` ausschließlich über UXD-01.
ST-02 besitzt Vorschau-/Apply-/Recoverysemantik, UXD-00 den Clientanschluss,
UXD-09 zentrale Tests. Voraussetzung: gewählte Reviewfläche B aus UXD-05/12 und
vorhandene reale Vorschau-, Commit- und Statusverträge. Keine Abhängigkeit von
der offenen Draft- oder Simple-Live-Wahl. Mit UXD-20 Schreibzugriff koordinieren.

## Bestandsabgleich und Aufgaben

1. Vorhandene Vorschau-/Revisionsprüfung, Doppelklickschutz und Statusabfrage
   erhalten. Im gelesenen Stand öffnet `ConfirmProfileApplication` noch ein eigenes Fenster;
   prüfen, ob UXD-05/12 inzwischen die bestätigte feste Reviewfläche geliefert hat.
   Fehlendes Layout dort ergänzen, kein zweites Reviewmodell parallel bauen.
2. Vorschau an Profil-ID und gespeicherte Revision binden. Bei veraltetem Stand
   lesbaren alten Diff eindeutig ungültig zeigen, Apply sperren, Refresh preview
   anbieten. Alte Defaults nur aus echten Daten; unbekannte Baseline benennen.
   Profil-/Draft-/Revisionswechsel kann eine alte Antwort nicht neuem Ziel zuordnen.
3. Vor dem Sendefall, bestätigt fehlgeschlagen und nach Sendung unbestätigt
   unterscheiden, soweit der reale Vertrag dies trägt. Bei unbekanntem Ausgang
   angefragtes Profil/Revision und letzten bestätigten Stand getrennt zeigen.
   Auch Liste und Shell verwenden dann „Last confirmed“/unbekannt statt eines
   unqualifizierten aktuellen Applied-Erfolgsmarkers.
4. Check current status liest den realen Zustand. Keine Erfolgsannahme aus
   Viewing, gespeichertem Stand oder altem Status; keine erfundene Recovery,
   Rollbackgarantie oder automatische Wiederholung. Erneutes Apply nur nach
   geklärtem Zustand, aktueller Vorschau und bewusster neuer Bestätigung.
5. Cancel verlässt die Reviewfläche ohne Mutation, Liste und Fokus bleiben
   nachvollziehbar. Während eines bereits gesendeten Commits bedeutet Abbruch
   oder Navigation kein bestätigtes Rollback; bestehende ST-02-Regeln beachten.
   Export-/Setupmeldungen verändern die Sicherheit des Appliedstatus nicht.

## Geplante Abnahme und Lieferung

- Viewing Daily, zuletzt bestätigt Workspace 2; zwei gespeicherte Änderungen
  wie in der Fixture. Revision nach Preview ändern: Apply gesperrt, alter Diff
  lesbar, keine Mutation mit alter Revision/neuem Ziel. Neue Vorschau braucht
  neue Bestätigung. Verspätete Previewantwort nach Profilwechsel verwerfen.
- Doppelklick sendet höchstens eine laufende Commitaktion. Ausfall vor Sendung,
  Antwortverlust nach Sendung und fehlgeschlagene Statusabfrage separat simulieren.
  Keine unbestätigte Erfolgsanzeige in Detail, Liste oder Shell.
- Status später bestätigt Daily mit passender Revision bzw. einen anderen
  tatsächlichen Stand: drei Anzeigen werden konsistent aktualisiert. Alter oder
  unzureichender Status darf angefragten Erfolg nicht allein bestätigen.
- Reviewfläche mit sichtbarer Profilliste bei 1120×780 und 800×560; Diff scrollt
  lokal, Aktionen bleiben erreichbar, Cancel/Fokus nativ. Kein Wechsel zum
  verworfenen normalen Applydialog und kein verstecktes Force apply.
- Lieferung: Übergangstabelle mit realem APIbezug, IDs/Revisionen, Requestfolge,
  native Drei-Flächen-Evidenz und gegebenenfalls fehlendem Statusvertrag.
  Ein fehlender Vertrag wird begrenzt als Blocker gemeldet, nicht lokal erfunden.

Designs: `APPLY-STALE-INPLACE`, `APPLY-RECOVERY-INPLACE` im
[Manifest](../uxpilot-flows-2026-10-25.json). Die genaue neue Fehlerpräsentation
bleibt ein geprüfter Vorschlag innerhalb des bestätigten Applylayouts B.
