# UXD-05 – Profile, globales Apply, Export und Clientsetup

Ziel: Häufige Profilauswahl kompakt bedienen und ihre tatsächliche Wirkung
verstehen. Abhängig von UXD-01/04; revisionsgebundene Vorschau/Recovery aus
ST-02 und UXD-00. F09. [Übersicht](README.md).

## Referenz, Befund und Dateien

Frames 10/11: `tUQuAL5HzEst2g0t69B1`, `qCI0uheft2XiFWKSBJmQ`;
[Bilder](../uxpilot-design-results-2026-10-24.md).
`MainWindow.Profiles.cs` hat bereits Master/Detail, Auswahl getrennt vom aktiven
Profil, Applyconfirmation, Export und Codexsetup. Vor allem Textlänge und
Aktionshierarchie ändern. Owner ausschließlich diese Datei; Route-/Policyhelpers
UXD-04, Shellzustand UXD-01, neue Clientverträge UXD-00, Artefaktinstallation ST-02.

## Aufgaben

1. Masterliste und Detail verdichten: ausgewähltes Profil (Viewing), tatsächlich
   angewendetes Profil samt Revision (Applied), Defaultmodell und Modellzeilen
   schnell erfassbar. Unbekannte aktive Identität als unbekannt zeigen.
2. Modellzeilen selbst direkt zu Models verlinken statt zusätzliche lange
   „Open model“-Buttonzeilen. Kontext/Back und ungespeicherten Profildraft bewahren.
   Alle vorhandenen Profil-, Default-, Policy- und Fallbackfelder erhalten.
3. Auswahl, Speichern, Apply, Export und Clientsetup klar trennen. Auswahl
   verursacht keinen Request zur Runtimeänderung. Export ist keine Installation;
   gespeicherter Stand ist nicht automatisch aktiver oder exportierter Stand.
4. Applyconfirmation als kompakten nativen Dialog über demselben Profilkontext
   zeigen. Echte aktuelle Vorschau aus ST-02 verwenden: von/zu, Revision,
   betroffene Routen/Accounts/Clients und tatsächliches Streamverhalten.
   Globalen Proxyumfang und mögliche Unterbrechung laufender Requests nennen.
5. Zwischen Vorschau und Commit Revision erneut prüfen. Veraltete Vorschau
   erneuern und bewusst neu bestätigen lassen; kein automatisches Reapply.
   Busy/Doppelklick, Teilausfall, Recovery, unbekannte Baseline und Host offline
   an den realen ST-02-Status anschließen. Ohne solchen Anschluss keine erfundene
   präzise Diff-/Recoveryanzeige aus lokalen Annahmen bauen.
6. Export-/Codexsetupfunktionen erhalten und korrekt beschriften. Bestehenden
   separaten Artefaktinstaller aus ST-02 verwenden; normale Codexkonfiguration
   und andere Clients nicht durch einen UI-Layoutwechsel verändern.

## Abnahme

- Designfixture: Viewing Daily, Applied Workspace 2; Dailydefault `gpt-lain-01`,
  fünf verlinkte Modellzeilen. Auswahl von Daily wendet es nicht an.
- Applyfixture: Workspace 2 → Daily; zwei gespeicherte Änderungen
  (`gpt-lain-02 disabled`, Dailydefault geändert), globaler Umfang sichtbar.
  Keine erfundenen Policy-/Synchronisationswerte oder isolierte Profilruntime.
- Cancel schließt den Dialog ohne Mutation und stellt Fokus wieder her.
  Confirm sendet genau eine aktuelle, revisionsgebundene Aktion.
- Revisionkonflikt nach Vorschau, unbekannter aktiver Stand, fehlgeschlagenes
  Apply und Recovery bleiben verständlich; Viewing wird nicht als Erfolg Applied.
- Disable/Hidden aus UXD-04 behalten ihre Bedeutung. Deaktivierte Defaults und
  Fallback-/Profilverweise werden vor Apply konkret sichtbar.
- Export und Clientsetup sind einzeln prüfbar und werden nicht als Applyerfolg
  behandelt. Angezeigte Dateistände/Revisionen entsprechen den echten Ergebnissen.
- In 800×560 bleiben Master/Detail-Rückweg, Default, Modellzugriff und Aktionen
  erreichbar; Dialogbuttons werden nicht abgeschnitten. Tastaturfokus bleibt
  im Dialog und kehrt danach zum Trigger zurück.

## Grenzen und Übergabe

ST-02 besitzt fachliche Vorschau-/Aktivierungs-/Recoverysemantik; UXD-05 deren
Darstellung. Fehlender ST-02-Anschluss blockiert nur diese vollständige Strecke,
nicht die kompakte Master-/Detailvorbereitung. Keine neue Clientruntime,
Installationslogik oder stillschweigende Anwendung gespeicherter Änderungen.

Liefern: Profil-/Dialogzustände mit echter APIzuordnung, Konflikt-/Recoveryfälle,
Nachweis erhaltener Expertenfelder und getrennte Export-/Setupnachweise.
