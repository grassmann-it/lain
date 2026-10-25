# UXD-12 – Profiles, aktuelle Applyvorschau und unklarer Ausgang

Ergebnis: Nutzer erkennt, welches Profil er betrachtet, was gespeichert ist und
was tatsächlich aktiv bestätigt wurde. Ein Konflikt oder Verbindungsverlust
führt zu Statusklärung statt unbewusstem erneutem Apply. Ergänzt UXD-05/ST-02.
Owner: Profilesview; Vorschau-/Commit-/Recoveryvertrag ST-02, Client UXD-00,
gemeinsame Policyhelpers UXD-04. Voraussetzungen UXD-01/04/05 und realer ST-02-Anschluss.

## Aufgaben und bestätigte Darstellung

1. Daily betrachten, Workspace 2 angewendet; Auswahl aktiviert nichts. Speichern,
   globales Apply, Export und Clientsetup sind eigene Aktionen mit echten Ergebnissen.
2. Review zeigt alte/neue Identität, aktuelle revisionsgebundene Änderungen und
   globalen Proxyumfang mit möglicher Unterbrechung aktiver Requests. Keine erfundene
   Diffpräzision oder pauschale Aussage über atomare/sequenzielle Backendupdates.
3. Bestätigte Folgeauswahl B: `PROFILE-REVIEW-B`, feste Reviewfläche im
   Profildetail; Profilliste bleibt sichtbar. Die frühere Empfehlung Dialog A
   wurde nicht übernommen. Explizite Bestätigung, globaler Umfang und Schutz vor
   veralteter Vorschau gelten unverändert. Vorschau an Profilziel und Revision
   binden. Bei Profil-/Kontextwechsel die alte Vorschau verwerfen oder als
   ungültig sperren; neues Ziel erst nach aktueller Vorschau bewusst bestätigen.
   Cancel verlässt die Reviewfläche ohne Mutation und gibt Fokus zum Auslöser zurück.
4. Konflikt: Apply sperren, Vorschau ausdrücklich erneuern und erneut bestätigen.
   Kein Force apply und kein automatischer Wiederholungsversuch mit neuer Revision.
5. Verbindungsverlust während Commit: „Apply outcome unknown“, letzter bestätigter
   Stand und angefragtes Ziel getrennt. Aktuellen Status abfragen; weder Erfolg
   noch Rollback noch erneuten Apply als bereits erfolgt darstellen.
6. Vollständiger schmaler Profileditor: Default, Mitgliedschaft einschließlich
   disabled/hidden Routen, Policycheck, Fallbackauswahl/-reihenfolge und Permission.
   Alle bestehenden Controls zuordnen. Aktueller Code speichert Fallbackkonfiguration,
   qualifiziert aber keine ausführbare Fallbackruntime; deren Grenzen sichtbar halten.

## Abnahme

- Zwei gespeicherte Änderungen der Fixture bleiben gleich: `gpt-lain-02 disabled`
  und Dailydefault `gpt-lain-01`. Auswahl ist kein Apply.
- Revision ändert sich nach Review; Doppelklick/Busy, Cancel, Hostverlust vor/nach
  Mutation, unbekannte Baseline und Status-Recovery mit synthetischen APIs prüfen.
  Erfolgsanzeige erst aus tatsächlichem bestätigten Zustand, nicht aus Viewing.
- 1120×780 und 800×560: Reviewfläche im Profildetail, sichtbare Profilliste,
  vollständig erreichbare Bestätigungs-/Abbruchaktionen. Keine modale Fokusfalle
  für die normale Applyvorschau; Tastaturweg und Fokusrückgabe nativ prüfen.
- Offenes Review bei Profilwechsel, Draftänderung und Revisionswechsel verliert
  seine Gültigkeit. Kein Commit mit altem Diff/neuem Ziel; neue Bestätigung erst
  nach aktueller Vorschau. Cancel verändert weder Konfiguration noch Appliedstatus.
- Konflikt-/Recoveryhinweise sowie Profileditor mit festen Aktionen und scrollbar
  vollständigen Feldern bleiben schmal erreichbar. Die Auswahl B bestätigt nicht
  pauschal alle vorgeschlagenen Fehlerdarstellungen.
- Export-/Setupfehler dürfen den aktiven Profilstatus nicht verändern.

[Bilder und Bewertung](../uxpilot-selection-2026-10-24.md),
[Design-IDs](../uxpilot-followup-2026-10-24.json). Lieferung: bestätigte Reviewfläche B,
reale API-/Revisionszuordnung und native Fehler-/Recoverybelege. Fehlender
ST-02-Anschluss blockiert nur die davon abhängige Strecke; kein erfundener Ersatzvertrag.
