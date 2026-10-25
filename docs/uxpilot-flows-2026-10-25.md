# UX-Pilot-Prüfung von Bedienabläufen und Randzuständen

Stand 25.10.2026. Auftrag: die nach der Folgeauswahl benannten Lücken konkret
in UX Pilot darstellen, selbst prüfen und auswerten; offene Entscheidungen
als Screenshotvergleich vorlegen. Keine Produktcodeänderungen, Installation,
Dienstneustarts, echten Provideraktionen oder Commits in dieser Designrunde.

## Geltende Auswahl und Budget

Die [bestätigten Grundmuster und Folgeauswahl A/B/A](uxpilot-selection-2026-10-24.md)
bleiben gesetzt: Accounts breit eine Liste plus 400-px-Detail, Accounts schmal
340-px-Liste neben Detail, Models/Usage Tabelle plus Detail; kompakter Monitor
Tabelle B mit langen Verbrauchsbalken darunter (A), Applyreview im Profildetail
(B), Live schmal mit dauerhafter linker 200-px-Requestliste (A).

Die Runde ist abgeschlossen: **50 neue Credits**, damit **122/400 Zusatzcredits
verbraucht, 278 übrig**. Kumulativ über alle Designrunden: 260 Credits.
18 Generierungen und 23 kostenlose HTML-Korrekturfassungen; kein weiterer
kostenpflichtiger Reviewlauf. Die zwei neuen Auswahlfragen bleiben offen.

Neue [UX-Pilot-Seite](https://uxpilot.ai/a/ui-design?page=OwvfuNqsUpAtB1veoNrv).
Drei bestätigte Referenzen wurden unverändert kopiert. Nach der zwischenzeitlichen
Verbindungsstörung zuerst den bereits gestarteten Job weiter abgefragt; keinen
Doppeljob angelegt. Alle drei Jobs und die Rückspeicherung der Korrekturen sind
inzwischen abgeschlossen.

## Dargestellte und geprüfte Zustände

1. Apply B mit veralteter Vorschau und unbekanntem Commitausgang innerhalb
   derselben Reviewfläche; kein Wechsel zum nicht gewählten Bestätigungsdialog.
2. Zwei Präsentationen für Navigation mit ungespeichertem Models-Entwurf:
   A Nachfrage vor dem Verlassen, B Navigation mit bleibendem lokalem Drafthinweis.
   Beide bewahren Eingaben, ohne zu speichern oder Apply auszulösen.
3. Geöffnetes Modellmenü und Entfernen bei benannten Profilverwendungen.
4. Bewusster Ersteinstieg Monitor/Gateway und fehlende Corevoraussetzung.
5. Erster Ladevorgang, lokaler Refresh, kein Filtertreffer und verschwundene Auswahl.
6. Codexexport vorbereitet gegenüber Clientsetup mit fehlendem Zielordner.
7. Simple-Navigation A mit direktem Live gegenüber B mit offenem More-Menü;
   dazu Gateway Barebones und Advanced/Compact zur Konsistenzprüfung.

Nur Drafthinweise und Simple-Navigation bilden neue offene A/B-Fragen.
Die übrigen Bilder dienen als Zustandsfolgen und Gegenprüfungen. Neue Entwürfe
sind Vorschläge; eine Darstellung allein bestätigt keine neue Fähigkeit.

## Quellabgleich vor der Generierung

Gezielte lesende Prüfung auf HEAD `e8dcac16e4a5dcfc2b309e0702faa3748c324d70`,
mit parallelen fremden Dokumentationsänderungen. Keine allgemeine Codeabnahme.

- [Workspacezustand](../src/Lain.Desktop/MainWindow.Workspace.cs) bewahrt den
  montierten Editorentwurf je Abschnitt. Das ist kein allgemeiner Entwurfsmanager
  für beliebig viele Objekte und kein Beleg für Erhalt nach Appneustart. Die
  Varianten dürfen diese Fähigkeiten nicht erfinden. Neuprüfung eines veralteten
  Entwurfs erhält Eingaben und nennt nicht wiederherstellbare Felder.
- [Modellaktionen](../src/Lain.Desktop/MainWindow.Routes.cs) unterscheiden
  Enabled, Menüsichtbarkeit und Entfernen. Gespeicherte Profil-/Fallbackverweise
  sperren Entfernen; der Host validiert erneut. Verschwundene stabile ID wird
  ausdrücklich gezeigt und nicht durch eine andere Zeile ersetzt.
- [Profiles und Codexsetup](../src/Lain.Desktop/MainWindow.Profiles.cs) trennen
  Prepare, Install und Apply. Prepare erzeugt Artefaktbeschreibungen ohne Dateien
  zu installieren; Install erstellt eine separate private Konfiguration in einem
  neuen Zielordner. Ein generischer Überschreibdialog für die normale Codex-
  Konfiguration wäre hier falsch. Fehlender Zielordner ist vor Prepare ein
  Validierungsfehler; daraus folgt kein Rollback einer Installation.

## Ergebnis und zwei offene Entscheidungen

[Offlinegalerie mit 21 eingebetteten Bildern](uxpilot-flows-2026-10-25.html):
vier auswählbare A/B-Bilder, 14 weitere Zustandsbilder und drei bestätigte
Referenzen. Keine Vorauswahl; Auswahl und Notizen sind lokal speicherbar und
als Antworttext kopierbar. Die zwei Fragen bestätigen keine anderen Entwürfe.

| Frage | A | B | Empfehlung und Nachteil |
| --- | --- | --- | --- |
| Models-Draft beim Wechsel zu Profiles | Vorher nachfragen; Draft behalten und fortfahren oder weiterbearbeiten | Sofort wechseln, bleibender Hinweis mit Rückweg und ausdrücklichem Verwerfen | **B**, weil Abschnittsentwürfe bereits erhalten bleiben. Der Hinweis braucht Platz und darf nicht aus dem sichtbaren Bereich verschwinden. A bleibt für bewusst unterbrochene Wechsel sinnvoll. |
| Live-Einstieg in Simple | Live direkt als fünftes Ziel | Live unter More | **A bei regelmäßiger Live-Nutzung**: gleicher Footerplatz wie More, ein Schritt weniger. B gewichtet die übrigen Bereiche bei seltener Live-Nutzung stärker. |

Beide Entscheidungen sind **offen**. Die gewählten Accounts-/Monitor-/Models-/
Usage-/Apply-/Live-Layouts bleiben unverändert. Die übrigen Bilder sind geprüfte
Zustandsstudien und Empfehlungen, keine automatische Freigabe einer neuen
Startseite, Modusmatrix oder Produktfähigkeit.

Starten:

```sh
firefox /home/tomka/Desktop/lain/docs/uxpilot-flows-2026-10-25.html
```

## Learnings aus der eigenen Prüfung

- **Applystatus muss überall dieselbe Sicherheit ausdrücken.** Bei unbekanntem
  Ausgang wurde auch der Appliedmarker der Profilliste auf „Last confirmed“
  korrigiert. Angefragtes Daily und zuletzt bestätigtes Workspace 2 bleiben
  getrennt. Status abfragen; kein erfundener Erfolg, Rollback oder automatischer
  Retry. Die veraltete Vorschau bleibt lesbar, Apply ist bis zur Erneuerung gesperrt.
- **Draftschutz und Applyreview sind verschiedene Abläufe.** Beide Draftvarianten
  bewahren denselben Models-Entwurf je Abschnitt während der geöffneten App.
  Sie speichern und applizieren nichts; Mehrfachentwürfe je Objekt oder
  Neustartpersistenz sind nicht belegt. Die gewählte Applyreviewfläche B bleibt bestehen.
- **Modellidentität trägt jede Aktion.** Alias `gpt-lain-01`, Upstream
  `gpt-model-01` und Account Personal Codex sind getrennt. Enabled, Visible und
  Applied sind verschiedene Zustände; Hidden ist kein Zugriffsverbot. Bei
  verschwundener stabiler ID bleibt der Draft zur Prüfung erhalten und wird
  keiner anderen Route nach Alias oder Zeilenposition zugeordnet.
- **Gespeicherte Verwendungen sperren Entfernen.** Die drei bekannten Profile
  werden benannt. Defaultwechsel allein entfernt keine Mitgliedschaft.
  Automatische UX-Pilot-Ideen wie Quick detach oder Force remove wurden verworfen;
  historische Usage bleibt erhalten. Keine zusätzlichen Fallbackverweise erfunden.
- **Fehlende Daten brauchen unterschiedliche Zustände.** Erster Snapshot,
  wirklich leeres Inventar, kein Filtertreffer und lokaler Refresh sind verschieden.
  Während Refresh bleiben letzte bekannte Werte statt 0 oder vermeintlichem Refill.
  Andere Accounts bleiben erreichbar. Im letzten Abgleich wurden die bekannten
  Quotafenster zusätzlich in fünf geschlossenen Accountzusammenfassungen ergänzt.
- **Monitor und Gateway brauchen ehrliche Voraussetzungen.** Fehlender Core
  wird vor Codexsign-in erklärt; kein gestarteter Login oder pauschales
  API-Key-Monitorversprechen. Der Monitor erhält keine unbelegte Gatewayhistorie.
  Betriebsabsicht, Informationsumfang und lokale Dichte bleiben getrennt.
- **Prepare, Install und Apply bleiben getrennt.** Prepare liefert eine
  Artefaktbeschreibung ohne Dateien zu installieren. Install erstellt einen
  neuen privaten `lain-codex-…`-Ordner mit eigener Konfiguration und unveränderlichem
  Katalog. Fehlender Zielordner ist ein Validierungsfehler vor Prepare, kein
  Installationsrollback. Die normale Codexkonfiguration wird nicht überschrieben.
- **Generatorbilder brauchen fachliche und geometrische Korrektur.** Erfundenen
  alten Default, technische Identitäten und zusätzliche Fähigkeiten entfernt.
  Ausfälle der Utility-CDN-Darstellung durch eingefrorene Styles umgangen;
  Appwurzel bei x0/y0 und explizite Workspacehöhe sichern den Footer. Automatische
  Vorschläge für neue Health-/Profilmetriken oder „Apply all“ nicht übernommen.

Die [Übergabe UXD-16 mit sechs Paketen UXD-17–22](ux-design-work-packages/16-state-continuity.md)
ordnet die Ergebnisse bestehenden Ownern zu. Sie startet keine Implementierung;
die beiden offenen Präsentationsentscheidungen blockieren nur ihre eigene Gestaltung.

## Läufe, Canvas und Nachweise

| Lauf | Abgeschlossener Job | Screens | Credits |
| --- | --- | --- | --- |
| 1 | `l2ZlKik4RjWMMzpa7MqE` | 6 | 17 |
| 2 | `lzWfLPtxvc3KKDfBuroH` | 6 | 15 |
| 3 | `4nTa1DzpdcwFZdMMuNqq` | 6 | 18 |
| Gesamt dieser Runde | | 18 | **50** |

Finale Gruppe **`7SQ2YIrtujoaVtkm4lLM`**, „Geprüfte Entwürfe · 25.10.2026“.
18 finale Entwürfe nebeneinander mit 1560 px Schritten bei y0; drei Referenzen
darüber bei y−1600. 18 unveränderte Generatororiginale bei y1600, fünf ersetzte
Korrekturfassungen bei y3200. Original-/Archivgruppe `JkJnIoxihmNBCEXxckjJ`,
Referenzgruppe `iW9N0yg7TR7pccayyTKB`. Alle **44 tatsächlichen Seitenpositionen**
erneut gelesen: mit konservativen 1440×1024-Rechtecken **keine Überlappung**.

Alle 18 Generatororiginale, lokalen Korrekturfassungen und echten finalen
UX-Pilot-PNGs selbst gesichtet. 23 kostenlose HTML-Versionen über MCP gespeichert
und finale PNGs zurückgelesen. Die Galerie bettet diese echten PNGs ein; ihre
800×560-Appausschnitte werden für den Vergleich zugeschnitten. Die vollständigen
UX-Pilot-Exporte können zusätzlich freien Canvasraum enthalten.

Galerieprüfung bestanden: 1600/1120/800/390 px ohne Kartenüberlappung oder
horizontalen Überlauf; 21 Bilder, vier Optionen ohne Vorauswahl, Auswahl/Notizen,
Persistenz, Reset, Zoom und Clipboard-/Storagefallback. Offline keine externen
Requests oder JavaScriptfehler. 18 Design-HTML-Geometrien mit 800×560-Shell,
472-px-Workspace und 48-px-Footer geprüft; ein Modusdropdown rechts und Profiles
im Gateway direkt. Bewusster Ersteinstieg und Monitor haben eigene Navigation.

Dies qualifiziert **Designartefakte und Galerie**, keine native Produktbedienung.
Tastatur, Fokus, Menüzustände, DPI/Schriftvergrößerung, Polling, Racebedingungen
und reale Host-/Provideranschlüsse sind in UXD-09/16 noch geplante Nachweise.
Keine Produktcodeänderungen, Builds, Installation, Dienstneustarts, echten
Provideraktionen, Commits oder zusätzlichen Agenten in dieser Runde.
Dokumentationsprüfung: 48 lokale Links auflösbar, alle 18 finalen IDs und
PNG-Quellen im Bericht enthalten, Kostenabgleich und Whitespace ohne Befund.

- [Drei tatsächlich verwendete Prompts](uxpilot-flow-prompts-2026-10-25.md).
- [Manifest mit Original-/Final-IDs, Bildquellen und Einzelbewertungen](uxpilot-flows-2026-10-25.json).
- Lokale Rohantworten, HTML, PNGs und `verification.json`:
  `artifacts/uxpilot-flows-20261025/` (ignoriert). Die Offlinegalerie und die
  folgenden echten Bildlinks sind unabhängig von diesem Ordner nutzbar.

## Finale Designs und direkte PNG-Links

<!-- final-design-table -->
| Design | Finale ID | Bild | Eigene Bewertung |
| --- | --- | --- | --- |
| `DRAFT-LEAVE-A` | `WQMwYilOeK3hDfw8tIe3` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/6b00d9e24b-b30886db5a02da022e53.png) | Der Dialog betrifft lokale Entwürfe. Deine Entscheidung für Applyreview im Profildetail bleibt davon unabhängig. |
| `DRAFT-LEAVE-B` | `AGIvXXFPn4QPdG1UX4cs` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/e6b0b4d5cc-b10ea54dc564a43f6e48.png) | Grundlage: vorhandener Editorerhalt je Abschnitt. Kein Mehrfachentwurfsmanager und keine Neustartpersistenz. Verwerfen bleibt ausdrücklich. |
| `SIMPLE-NAV-A` | `gXaPK2gTzMukCmgc0Xqa` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/dac8e355af-daf8ac1a4eb33a628ecb.png) | More und Live belegen in diesem Vergleich denselben Platz. B gewinnt daher kaum Breite; A spart den zusätzlichen Klick. |
| `SIMPLE-NAV-B` | `ce9Wu9l0uiJW1ULbWv2L` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/c0c2c570d2-31114e36725ab0b38c86.png) | Das offene Menü macht den Unterschied sichtbar. Dies ist weiterhin eine eigene offene Entscheidung; die feste linke Live-Requestliste bleibt gewählt. |
| `APPLY-STALE-INPLACE` | `Rnmsye7JvdMJ5f8slhBp` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/627abce702-e982ac36039f4883defb.png) | Die Profilliste bleibt sichtbar. Der alte Diff ist lesbar, aber Apply gesperrt. Neuer Default nur als gpt-lain-01; kein erfundener vorheriger Alias. Profil- oder Revisionswechsel darf die Vorschau nicht umhängen. |
| `APPLY-RECOVERY-INPLACE` | `OPjufr5jEIoSPlVt1EXV` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/15aaa42022-ce28e7aa6916533058c2.png) | Daily ist das angefragte Ziel, Workspace 2 nur der letzte bestätigte Stand. Auch die Liste sagt Last confirmed. Kein Erfolg, Rollback, automatischer Retry oder laufende Recovery wird behauptet. |
| `FIRST-START` | `al3xiGJOt1h6x6itOEGy` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/cc4d4bb632-be986aaf4e24c3e4b826.png) | Betriebsabsicht steht getrennt vom View-Informationsumfang. Die beiden Einstiege sind eine Zustandsstudie, keine neue Anbieterqualifikation oder automatisch akzeptierte neue Startseite. |
| `CONNECT-PREREQUISITE` | `Q0fEfYUgjwZgxL0Xs262` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/b18f8ac2f0-59b1f6f64aec1bb1586d.png) | Die fehlende Voraussetzung wird vor dem Start erklärt. Gateway einrichten oder zurück zu Quellen; kein gestarteter Login, kein pauschales API-Key-Monitorversprechen und keine Gatewayhistorie im Monitor. |
| `ACCOUNTS-FIRST-LOAD` | `pYIcSyAvW4GD7rDTvi7V` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/750cd8c60d-89f891db641da94edd82.png) | 340 px Liste und benachbartes Detail bleiben reserviert. Skeletons zeigen Struktur, keine erfundene Accountanzahl. Keine Werte, Prozente oder Nullzustände werden vorgespiegelt. |
| `ACCOUNTS-REFRESH-BUSY` | `SuzbA3vlgytXM2tQ29Xv` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/e037b6f345-20d168e1241a0421cc7b.png) | 37 % verbraucht, 63 % verbleibend und Reset bleiben als letzter bekannter Stand sichtbar. Nur die Refreshaktion ist beschäftigt. Andere Accounts bleiben erreichbar; keine erfundene Dauer oder automatische Auffüllung. |
| `MODEL-MENU` | `suvtnICRwmkPRjhWVP06` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/465c3ace1a-72ac927e17bf4274cf7d.png) | Sichtbarer ⋯-Trigger und Menü am ausgewählten Modellzugang. Account Personal Codex, Alias gpt-lain-01 und Upstream gpt-model-01 sind getrennt. Enabled, Visible und Apply sind verschiedene Zustände. |
| `REMOVE-REFERENCED` | `CCJVEY1Ax8KfSETSD3zV` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/9688b6717d-a3d2ac33ecd5a893953b.png) | Daily, Workspace 2 und Workspace 3 werden benannt. Ein Defaultwechsel allein entfernt keine Mitgliedschaft. Kein Quick detach oder Force remove; historische Usage bleibt. Unbekannte Fallbackverweise werden nicht erfunden. |
| `MODELS-NO-MATCH` | `o9biHFaAUq3uclE4Tbux` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/0c09e51ec7-744190ac5771023554dd.png) | Die aktive Suche bleibt sichtbar, Clear filters bietet den nächsten Schritt. Kein Accountfehler oder neuer Verbindungsauftrag; Tabellenkopf bleibt über dem Ergebnisraum. |
| `MODEL-SELECTION-GONE` | `3BV9UP9TAX6x9lbp3aym` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/eea4ece6e5-8b5a3d4f1ed2d4dc6aa6.png) | gpt-lain-01 wurde entfernt. Der Draft bleibt zur Prüfung erhalten, wird keiner anderen Route zugeordnet. Disabled pending Apply und Enabled hidden bleiben im Inventar lesbar. Keine behauptete Profilsperre oder automatische Ersatzwahl. |
| `CODEX-SETUP-VALIDATION` | `o8C2r4Br0fcTHlrThlJz` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/b3658f0642-d755459cd7ea6cf9f521.png) | Inlinefehler, Prepare gesperrt. Weder Vorbereitung noch Installation hat stattgefunden. Kein erfundener lokaler Executablepfad und kein vermeintliches Installationsrollback. |
| `CODEX-EXPORT-PREPARED` | `j5F42WDDfTo5q6DgRjcj` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/cd9cf3aa1d-3a94c5f2ba2f3fa14ff5.png) | Install client files ist ein gesonderter nächster Schritt. Neuer privater lain-codex-…-Ordner, separate Config und Katalog; normale Codexconfig bleibt erhalten. Daily ist ausgewählt, Workspace 2 angewendet. |
| `GATEWAY-BAREBONES` | `3IQ5ZRjNJwT3dUeK7Xsx` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/bb05cb6ecc-541a3d603bac6e304da8.png) | Accounts, Profiles und Usage direkt; Models/Live im geöffneten More-Menü. Gleiche Daten und feste 340/460-Aufteilung. Dies prüft die bisher vorgeschlagene Modusmatrix, ohne sie pauschal freizugeben. |
| `GATEWAY-ADVANCED-COMPACT` | `u2HrSV5vYJ8xFzpYUxOj` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/10b65e14a1-f21675fb4e298e99816e.png) | Gleiche feste Spalten; Dichte wird separat benannt. Sourcebereich ist offen, fehlende Quelldaten werden erklärt. Keine neuen Quotas, Preise oder Quellenidentitäten durch Advanced. Native Lesbarkeit ist noch zu prüfen. |
