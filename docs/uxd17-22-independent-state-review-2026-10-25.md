# Unabhängiger Zustandsreview UXD-17–22

Datum: 25.10.2026. Quellenbasis: Main `fee826a361ed3cc729f9e78bdc2b0d36b445e878`.
Gelesene Desktopdeltas: Shell `920036e`, Accounts `151876d`, Models `a10f15f`,
Connect `6c61b25` / `897cbe0`, Profiles `64b2351`.
Der nachfolgende Maincommit `eb4305f` verändert keine Desktopquelle.

Maßgeblich ist die ausdrückliche Aufnahme der sechs Pakete samt Nutzerwahl in
[UXD-17–22 Umsetzung](ux-state-implementation-2026-10-25.md): Draft B mit
bleibendem Hinweis/Rückweg/ausdrücklichem Verwerfen, Simple-Live A direkt.
Die [Paketgrundlage](ux-design-work-packages/16-state-continuity.md) und Pakete
[17](ux-design-work-packages/17-navigation-drafts.md),
[18](ux-design-work-packages/18-account-snapshots.md),
[19](ux-design-work-packages/19-model-command-identity.md),
[20](ux-design-work-packages/20-connect-client-setup.md),
[21](ux-design-work-packages/21-apply-status-consistency.md) und
[22](ux-design-work-packages/22-flow-acceptance.md) wurden gezielt gegengelesen.
Der aktuelle, teilweise unversionierte Auswahl-/UXD-00–15-Dokustand in Main
wurde ergänzend gelesen. Historische Vorschläge gelten dadurch nicht als gewählt.

Dies ist zunächst eine Quellprüfung. Kein eigener Build, Laufzeittest,
GUI-Zugriff, Login, Credentialzugriff, Dienstwechsel oder Produktedit.
Die folgenden Eingabefolgen sind aus den Kontrollflüssen abgeleitete
Reproduktionen, keine behaupteten bereits ausgeführten nativen Tests.
Die native Evidenz liefert separat UXD-22; ohne deren Sichtung folgt aus diesem
Bericht keine Installations-, Gesamt-UX- oder Accessibilityfreigabe.

## Befunde am Ausgangsstand

### F01 – Erhaltener Abschnittsentwurf wird durch zweiten Editor ersetzt

Priorität P1, Owner UXD-17 mit den Seitenownern.

Reproduktion: Models A bearbeiten; Profiles öffnen; im Profildetail eine andere
Route B öffnen; B bearbeiten; Accounts öffnen. Die kontextuelle Navigation zu B
umgeht die Wiederherstellung des Models-Drafts. Beim nächsten Abschnittswechsel
ersetzt B den Eintrag für A, ohne Save oder ausdrückliches Verwerfen.

Beleg: `MainWindow.Workspace.cs:290` stellt nur Navigation ohne Objektziel wieder
her. `RegisterEditor` in Zeilen 63–74 prüft den bereits erhaltenen
Abschnittsentwurf nicht. `SaveCurrentPage` in Zeilen 241–246 überschreibt
`_drafts[_section]`. Der Hinweis auf A kann während B noch sichtbar sein und
verschwindet erst nach dessen Überschreibung. Dies betrifft den vorhandenen
einzelnen Entwurf je Abschnitt; ein neuer Mehrfachentwurfsmanager ist keine
Voraussetzung für einen sicheren Eintritt in einen zweiten Editor.

### F02 – Alter Profile-Datensatz erhält unbemerkt die neue Revision

Priorität P1, Owner UXD-20/21 Profiles.

Reproduktion: Ein Profildetail bei Revision R fokussieren. Extern dasselbe
Profil ändern oder löschen; Polling liefert R+1, während das fokussierte Detail
montiert bleibt. Jetzt „Edit selection“ öffnen und speichern. Der Editor
verwendet noch die alten Werte des Detailobjekts, aber die neue Revision R+1.
Ein gelöschtes Profil kann damit wiederangelegt werden.

Beleg: `MainWindow.cs:258,348` aktualisiert den Snapshot, schiebt das erneute
Rendern bei Fokus aber auf. `MainWindow.Profiles.cs:234` bindet den alten
`selected`-Datensatz. `EditProfile` in Zeilen 640–650 übernimmt ihn unverändert,
während es die aktuelle Konfiguration liest. Save in Zeilen 755–759 sendet
diese Kombination; `ModelsService.cs:35–45` ist ein Upsert. Bei Models löst
`EditRoute` in `MainWindow.Routes.cs:413–424` den entsprechenden Eintritt bereits
über die aktuelle stabile ID auf.

### F03 – Bestätigtes runtime_busy bleibt dauerhaft unbekannt

Priorität P2, Owner UXD-21 / ST-02.

Reproduktion: Gültige Vorschau; Apply erhält HTTP 409 `runtime_busy`; danach
„Check current status“ mit frischem Beobachtungszeitpunkt und unveränderter
vorheriger completed Operation. Der Ausgang bleibt unbekannt, Refresh preview
und Apply bleiben gesperrt. Wiederholte Reads derselben richtigen alten
Operation können diesen Zustand nicht lösen.

Beleg: `MainWindow.Profiles.cs:426–446` setzt vor dem POST den unbekannten
Ausgang und behandelt nur `revision_conflict` ausdrücklich als Ablehnung.
`Program.cs:195` bildet `ProfileDrainTimedOutException` auf `runtime_busy` ab.
`ProfileApplicationService.cs:11–12,129–132,165–172` garantiert hier den
erhaltenen vorherigen Runtimezustand und restauriert dessen Operationsbeleg.
Die Statusklärung in `MainWindow.Profiles.cs:464–477` verlangt dagegen eine
andere completed Operation-ID oder einen Recoveryzustand. Dieser konkrete
bekannte Ablehnungsfall muss von Antwortverlust unterschieden werden;
allgemeines `operation_failed` ist damit nicht als sichere Ablehnung qualifiziert.

### F04 – Schmale Accounts verwenden das verworfene reine Detailmuster

Priorität P2, Owner UXD-17 Shell / UXD-18 Accounts.

Reproduktion: 800×560, Accountdetails öffnen. Die Inventarliste verschwindet,
die Detailseite erhält „Back“. Die ausdrücklich bestätigte schmale
Accountsvariante hält Liste und Detail nebeneinander.

Beleg: `MainWindow.Commands.cs:213–246` schaltet erst ab 1000 px auf zwei
Spalten; darunter setzt es `_page.Content = _accountInspectorDetails` und
versteckt den benachbarten Inspector. Dies ist eine Bestandslücke gegen das
bestätigte Layout, keine neue Designempfehlung. Die Quelle belegt den Wechsel;
Lesbarkeit und Geometrie einer Korrektur müssen nativ gesichtet werden.

### F05 – Späte Statusklärung nach Cancel aktualisiert nur die Shell

Priorität P2, Owner UXD-21 / UXD-17.

Reproduktion: Unbekannter Applyausgang; „Check current status“ verzögern;
Review abbrechen oder ein anderes Profil wählen; danach Status einer neuen
completed Operation liefern. Shell zeigt den nun bestätigten Zustand,
bereits gerenderte Profilliste und Summary behalten zunächst unknown bzw.
Last confirmed. Solange die Seite Fokus hält, verschiebt Polling das Rendern.

Beleg: Cancel ist in `MainWindow.Profiles.cs:335,351` nur während Commit
gesperrt, nicht während Statusread. Der Read in Zeilen 455–477 aktualisiert
Status und Unknownzustand global. `RenderProfileReview` in Zeilen 368–375
rendert aber nur bei Referenzgleichheit zum alten Review; ansonsten läuft nur
die Shellaktualisierung. `MainWindow.cs:348` verschiebt ein neues Rendern bei
Fokus. Statusklärung muss die sichtbaren drei Flächen auch dann konsistent
halten, wenn die ursprüngliche Reviewfläche nicht mehr existiert.

### F06 – Späte Connectantwort kann einen fremden Editor ersetzen

Priorität P1, Owner UXD-20 Connect / UXD-17.

Reproduktion: Accounts → „Reconnect account“, GET connection-methods/status
verzögern; Models öffnen und einen Editor bearbeiten; GETs freigeben. Die
Connectseite wird jetzt unter dem aktuellen Abschnitt Models montiert und
ersetzt dessen ungespeicherten Editor ohne vorherige Entwurfsicherung.

Beleg: `MainWindow.Commands.cs:137` startet `BeginProviderConnectionAsync`
ohne globalen RunAsync-Busyblock. `MainWindow.AccountChooser.cs:82–94` prüft
nach den Awaitpunkten weder Abschnitt noch Besitzer/Identität der Seitenanfrage.
`ShowConnectionMethods` und `ShowConnectionMethod` in Zeilen 120–194 schreiben
anschließend den aktuellen Editorzustand. Beim allgemeinen Chooser schützt
Zeile 40 nur den Erfolg durch einen Abschnittscheck; auch dessen Fehlerpfad
in Zeilen 73–76 schreibt ohne Besitzprüfung die aktuelle Seite um.
Ein lokaler Anfrage-/Seitenbezug ist erforderlich; ein echter Login ist zur
Prüfung dieses Rennens nicht nötig.

### F07 – Bestätigte Monitortabelle ist noch nicht angeschlossen

Priorität P2, Bestandslücke UXD-10 / Owner Accounts.

Der aktuelle Mainvertrag `docs/ux-design-work-packages/10-selected-layouts-monitor.md`
bestätigt für den kompakten Monitor eine Tabelle nach Account mit jedem
gemeldeten Quotafenster und langen Balken unter dessen Zahlen. Am geprüften
Quellstand verwendet `MainWindow.Accounts.cs:242–256` unabhängig von der
Monitorabsicht ausschließlich `AccountTiles`; `MainWindow.AccountTiles.cs:28–32`
baut dafür ein MasonryPanel. Die vorhandenen Quotazahlen/-balken sind damit kein
Nachweis der gewählten Monitortabelle. Reproduktion: Monitorabsicht, Accounts
bei 800×560 mit mehreren Accounts und Fenstern. Dies ist keine neue
Layoutwahl, sondern ein fehlendes Delta zu einer bestätigten früheren Auswahl.

### F08 – Live schmal verliert weiterhin die dauerhafte linke Requestliste

Priorität P2, Bestandslücke UXD-14 / Owner Live.

Reproduktion: Live bei 800×560; beobachteten Request auswählen. Statt der
gewählten dauerhaften Liste links und Ausgabe rechts verschwindet die Liste
und „Back to requests“ erscheint.

Beleg: `Inspector/InspectorView.cs:280–303` schaltet unter 900 px auf eine
Spalte; bei Detailauswahl wird `_sidebar` unsichtbar. Der aktuelle Mainvertrag
`docs/ux-design-work-packages/14-live-pause-source.md` bestätigt ausdrücklich
die linke Liste A bei 800×560 und verwirft getrennte Listen-/Outputseiten.
Die neue direkte Simple-Live-Navigation erfüllt diesen Inhaltsvertrag nicht
automatisch. Bestehende Daten-/Retentionsemantik ist dadurch nicht beanstandet.

## Positiv abgeglichene Kontrollflüsse und Grenzen

| Bereich | Quellbefund am Ausgangsstand | Noch erforderliche Evidenz |
| --- | --- | --- |
| Draft B / Simple-Live A | Hinweis liegt außerhalb des Workspace-Scrollers; Rückweg und gezieltes Discard vorhanden. Simple enthält Live direkt, nicht zusätzlich im More-Menü. Editor wird beim normalen Abschnittswechsel montiert erhalten. | Native Fokus-/Scrollfolge, Größen und Informations-/Dichtewechsel; F01 berücksichtigen. |
| Models | Menüöffnung bindet aktuelle Route-ID und Revision. Jede eingefrorene Aktion prüft beide erneut. Edit löst aktuelle ID auf; Save kann verschwundene Route nicht wiederanlegen. Profilmitgliedschaft und Profil-/Routenfallbacks sperren Entfernen. | Tatsächliche Pointer-/Rechtsklick-/Tastaturwege und Pollingrennen; strukturellen Test nicht als native Eingabe ausgeben. |
| Accounts | Refresh ist accountlokal. Werte bleiben während Busy montiert; Antworten mergen nur die angefragte Account-ID. Späte A-Antwort ersetzt nicht den Inspector für B. Alle vorhandenen Quotafenster werden iteriert; vorhandene Nenner-/Freshnesshelper bleiben maßgeblich. | Erster Snapshot vs. leer/Fehler, Erfolg/Fehler A→B, alle Fenster, Fokus/Scroll und schmale Geometrie. |
| Connect | StartConnectionLoginAsync prüft tatsächlich vor LoginAsync frische Status-/Methodeninformationen. RequiresCore, Monitorabsicht und Methodenverfügbarkeit sind getrennt. Fehlender Core erzeugt keinen Loginrequest. | Requestbeleg beider Größen, Rückwege und Methodenänderung während offenem Formular; F06 zusätzlich. |
| Clientsetup | Vor Prepare werden Executable und bestehender Elternordner erneut validiert. Prepare schreibt im Desktop keine Dateien. Install schließt über das tatsächlich gelieferte Artefakt mit Profil-ID/Revision/Generation und eigenes Ziel; kein Applyaufruf. | Native Browse-/Fokusrückgabe, Invalid/Cancel, Prepared ohne Dateien, private Installerdateien/gewöhnliche Configsentinel und Zielwechsel. |
| Applyreview | Feste Reviewfläche hält Profilliste auch schmal sichtbar. Vorschauantwort prüft Ziel-ID/Revision/Anfrage. Alter Diff bleibt lesbar und Apply gesperrt. Unknown nutzt Last confirmed in Shell, Liste und Review. | Laufende Commit-/Statusfolgen, alte/ungenügende Statusantworten, drei tatsächliche Flächen; F02/F03/F05 beheben bzw. begrenzen. |

Native Layout-, Input- und Requestnachweise werden unabhängig vom Quellenbefund
fortgeschrieben. Quellkorrekturen allein erfüllen UXD-22 nicht. Keine aus
Designs übernommenen Quotas, Generationen, Preise oder Statusgarantien sind
hier qualifiziert; tatsächliche Vertragsfelder bleiben die Grundlage.

## Folgeprüfung

Alle acht Befunde wurden dem Integrator mit konkreten Stellen und
Reproduktion übermittelt. Produktkorrekturen bleiben bei den bestehenden
Ownern. Dieser Reviewworktree enthält ausschließlich dieses Dokument.

Zusätzlich gelesene Ownerkorrekturen, noch ohne Laufzeitnachweis:

- `183b4f6` ersetzt den schmalen Accounts-Detailwechsel durch benachbarte
  Flächen und passt den Inventarheader an. F04 ist damit an der Quelle adressiert;
  nutzbare schmale Geometrie bleibt nativ zu prüfen.
- `b6a4e87` plus Models `df5cf18` verhindern den zweiten Models-Editor beim
  zurückgehaltenen Abschnittsdraft und vermeiden dessen Löschung durch eine
  fremde Inventaraktion. Der konkrete Modelsweg aus F01 ist damit an der Quelle
  adressiert. Entsprechende Profile-/Setup-Einstiege und native Folge bleiben offen.
- `596535d` ersetzt beim Modellmenü die bisherige VisualRoot-Abfrage durch
  `TopLevel.GetTopLevel`; daraus wird hier kein Compiler-/Fokus-PASS abgeleitet.

Weitere Ownercommits und native Evidenz sind zum Zeitpunkt dieser Fassung
noch nicht abschließend gegengeprüft.

## Folgeprüfung auf Main `7a6b88c`

Die konkreten Kontrollflüsse aus F01–F06 wurden erneut gelesen. „Quelle
geschlossen“ bedeutet hier: Die benannte Reproduktion erreicht den falschen
Zustandsübergang nicht mehr. Es ersetzt keinen nativen Nachweis des Endstands.

| Befund | In Main gelesene Korrektur | Ergebnis der Quellprüfung |
| --- | --- | --- |
| F01 | Shell `84ae915`, Models `30f7b33`, Profiles `fc1462f` | Quelle geschlossen für die benannten Models-/Profiles-/Setup-/Removal-Einstiege. Zweiter Editor verweist auf Continue/Discard; fremde Inventaraktion entfernt den Draft nicht. |
| F02 | Profiles `fc1462f` | Quelle geschlossen: aktuelle stabile Profil-ID wird vor Edit/Setup aufgelöst; Save/Remove prüfen Existenz und ursprüngliche Revision. |
| F03 | Profiles `7372784` | Quelle geschlossen: ausschließlich bestätigtes 409/`runtime_busy` hebt Unknown auf, erhält den alten Status und verlangt frische Vorschau/neue Bestätigung. Recovery und Transportverlust bleiben getrennt. |
| F04 | Shell `cc6f092` | Quelle geschlossen. Bei beiden vereinbarten Größen bleiben die Accountflächen nebeneinander, Inventarheader wird schmal gestapelt. |
| F05 | Profiles `fc1462f` | Quelle geschlossen: globaler Statuscallback aktualisiert gemountete Summary und Listenmarker auch nach Cancel/Reviewwechsel, ohne fokussierte Seite oder Draft zu ersetzen. |
| F06 | Shell `2b0829d`, Connect `2330217` | Quelle geschlossen: Anfrage, akzeptierte Navigation, Abschnitt und Contentbesitz werden nach Await auf Erfolg und Fehler geprüft; veraltete Antworten schreiben keine fremde Seite. |
| F07 / F08 | Ownerdeltas separat beauftragt | Noch offen, noch keine Korrektur gelesen. |

Modellmenüfix `d14ec65` wurde zusätzlich gelesen. Er bindet denselben Commandsatz
auch vor programmatischem `ContextMenu.Open`, weil Avalonia dort das Openingevent
nicht zuverlässig auslöst. Die Identitäts-/Revisionsprüfung bleibt erhalten.

## Unabhängige Sichtung der ersten nativen Evidenz

Gelesenes Manifest:
`/home/tomka/Desktop/lain-implementation-worktrees/20261025/uxd22-evidence/native-r1/evidence.json`.
Der Testowner führte diesen Lauf aus, nicht der Reviewer.

- Manifestcommit `ce53ab56e843a7da6b2e20fa5d438c400637d2b8`;
  Desktopassembly SHA-256
  `EEF2B2AEA9EDC513927C19318F4F86683831B31A7C5F8C3CAB5107551150BC2E`.
- 18/24 Fälle PASS, sechs fehlgeschlagen; 374 Assertions, 64 Bilddatensätze.
  Der gesamte Lauf meldet korrekt FAIL, visuelle Prüfung nicht automatisch
  bestanden und Accessibility nicht qualifiziert. Private Xvfb, tatsächliche
  RenderScaling 1 in den gesichteten 800×560-Aufnahmen.
- Die beiden Draftfailures prüfen einen Disabled-Savezustand, den der Vertrag
  nicht zwingend fordert und das Produkt nicht implementiert. Der vorhandene
  Saveguard verhindert den Request. Im Folgelauf ist der tatsächliche native
  Saveversuch samt ausbleibendem Request und erhaltenen Eingaben zu prüfen.
- Die beiden Menüfailures decken einen echten Unterschied beim Öffnungsweg auf;
  der genannte Menüfix ist dafür quellengeprüft. Die beiden Connectfälle enden
  wegen nicht erreichbarem Add-account-Auslöser nach dem vorherigen gehaltenen
  Connectdraft; ein abgebrochener Fall zählt nicht als Gesamtnachweis.

Selbst geöffnete PNGs aus demselben Verzeichnis:

| PNG | Tatsächlicher visueller Befund |
| --- | --- |
| `native-state-draft-800x560-retained.png` | Bleibender Hinweis benennt Models und gpt-lain-01; Continue editing/Discard sowie Footer sind sichtbar. Das Bild allein beweist keinen Fokus- oder Scrollübergang. |
| `native-state-account-refresh-800x560-late-success-b-selected.png` | Inventar und Command-Code-Detail stehen nebeneinander; vorhandene Daten/Last known und linker aktualisierter Wert sind sichtbar. Keine Behauptung über sämtliche Scrollpositionen. |
| `native-state-setup-800x560-prepared.png` | Daily, echte Fixture-ID/Revision/Exportgeneration, privates Ziel, No files installed yet sowie separate Install-/Cancelaktionen sind lesbar; Workspace 2 bleibt in der Shell berichtet. Dateiabwesenheit ist ein eigener Fixturecheck. |
| `native-state-connect-800x560-monitor-prerequisite.png` | Coreanforderung und unveränderte Monitorabsicht werden vor Login erklärt; Quellen-, Gateway-, Reload- und Rückwege sind erreichbar dargestellt. |
| `native-state-apply-800x560-confirmed.png` | Review bleibt neben der Liste; echter zusätzlicher Widerspruch: Daily Applied revision 3 und Shell Active Daily, aber Summary/Review noch saved 3 / proxy 1 mit Pending. |

Der letzte Bildbefund wurde vom Testowner ebenfalls erkannt. Profilesfix
`b87c9c3` wurde gelesen: Statusklärung übernimmt nur bestätigte AppliedRevision,
keine gespeicherten Objekte oder Editorrevisionen; Description und Pending
verwenden ausreichend aktuelle Hostmetadaten und benennen einen älteren
geladenen Snapshot. Damit ist dieser konkrete Widerspruch an der Quelle
adressiert; das korrigierte Bild ist noch nachzuweisen.

Die Requestdetails im r1-Manifest enthalten Methode, Pfad, Body und Revision,
aber keine Fallkennung; ihre Sequenz beginnt je Fixture wieder bei 1. Identische
Requests verschiedener Größen sind so nicht eindeutig einem Fall zuzuordnen.
Der Testowner wurde um die vorhandene `currentCase`-Zuordnung im Folgemanifest
gebeten. Native Inputtrace und Bilder haben diese Zuordnung bereits.

Kein Gesamt-PASS für UXD-22 aus diesem ersten Lauf oder den fünf Bildern.
F07/F08 und die gezielte native Nachprüfung der Korrekturen bleiben erforderlich.

## Quellabschluss der acht Ausgangsbefunde auf `a8b19ae`

Auch die beiden verbliebenen bestätigten Layoutdeltas sind jetzt gelesen:

- F07, Main `6bd348d`: `AccountInventoryRows` schaltet bei Monitorabsicht auf
  `MonitorAccountTable`. Jede Accountgruppe iteriert sämtliche gelieferten
  Fenster und verwendet die bestehenden `BudgetWindowRow`-/Quotahelper.
  Normierbare Werte behalten einen durchgehenden 0–100-Balken unter den Zahlen;
  unter 500 px werden Fensteridentität, Verbrauch und Reset gestapelt.
  Ohne Nenner bzw. bei unbekannten Werten wird kein Prozentbalken erfunden.
  Die Tabellenzeilen behalten accountlokalen Refresh, Scope, unbestätigten
  Reset und Hinweise zu gemeinsamem Budget. F07 ist an der Quelle geschlossen.
- F08, Main `a8b19ae`: Live behält dauerhaft zwei Spalten, links schmal 200 px
  beziehungsweise breit 280 px. Die Sidebar bleibt sichtbar; die bisherigen
  Listen- und Outputscrollcontrols bleiben montiert. Der getrennte Backweg
  wurde entfernt, Request-/Source-/Retentionmechanik bleibt vorhanden.
  F08 ist an der Quelle geschlossen.

Damit sind alle acht ursprünglichen Quellenbefunde konkret adressiert.
Das ist kein Gesamtabschluss der nativen Pflichtmatrix. Neue Tabellengeometrie,
schmale Liveausgabe, unabhängige Scrollpositionen, Fokus, lange Inhalte und die
geänderten Zustandsübergänge benötigen weiterhin die qualifizierte Endassembly.

Weitere tatsächlich gesichtete r1-PNGs, ohne neue Fehler gegenüber den bereits
genannten Befunden:

- `native-state-apply-800x560-unknown.png`: angefragtes Daily revision 3 und
  Last confirmed Workspace 2 revision 1 stehen sichtbar in Shell, Liste und
  Review; kein unqualifizierter aktueller Appliedmarker für Workspace 2.
- `native-state-apply-1120x780-stale.png`: alter Diff bleibt lesbar;
  Ungültigkeit ist erklärt, Apply gesperrt, Refresh preview/Cancel erreichbar.
- `native-state-first-snapshot-error-800x560-pending.png` und
  `native-state-first-snapshot-error-800x560-error.png`: Laden und Fehler
  unterscheiden sich sichtbar; beide Flächen bleiben reserviert.

Der unabhängige Bildumfang beträgt damit neun r1-PNGs. Ein korrigierter
Folgelauf wird eigenständig zugeordnet, nicht in den ersten Lauf eingerechnet.

## Gezielter nativer Folgelauf r2

Manifest `native-r2/evidence.json` im oben genannten Evidenzverzeichnis:
Commit `87e82135ad9d9648897b1c00628da91fd031b5fb`, Desktopassembly SHA-256
`8C2713EDFF27D06DA9445A735FEB6DA11BE405F9FCFAECEF88155678369F0301`.
Die gezielte Teilmenge hat 7/8 Fälle PASS, 199 Assertions und 24 Bilddatensätze;
der gesamte Teilmengenlauf bleibt korrekt FAIL. Das ist noch kein Nachweis
für die nachfolgenden Monitor-/Live-/Applystatuskorrekturen.

Vier weitere PNGs wurden unabhängig geöffnet:

- `native-state-model-menu-800x560-pointer-menu-x11.png` und
  `native-state-model-menu-800x560-keyboard-menu-x11.png`: vollständige separate
  native Popupfläche, dieselben neun Commands einschließlich tatsächlichem
  Accountbezug. Das Popup darf über das Appfenster hinausragen und ist deshalb
  im X11-Bild, nicht allein im Avalonia-Seitenrender, zu beurteilen.
- `native-state-retained-model-command-800x560-blocked-edit-b.png`: der
  erhaltene Models-A-Draft ist benannt; Continue/Discard bleiben sichtbar,
  während der geschützte Eintritt in B erklärt wird.
- `native-state-draft-800x560-failure.png`: gültiges Detail für gpt-lain-01 mit
  sichtbarem Editbutton. Der fehlgeschlagene Test sucht den Inventarbutton
  `Edit model access …`, obwohl im schmalen Detail
  `Edit selected model access …` angezeigt wird. Die Quelle und das Bild
  stützen einen Locatorfehler; der native Folgetest muss den real sichtbaren
  Einstieg verwenden. Der fehlgeschlagene Fall wird nicht als PASS umgedeutet.

Zusammen sind jetzt 13 konkrete PNGs aus zwei getrennten Läufen gesichtet.
Die native Endqualifikation bleibt noch offen.

## Gezielte Endassembly-Sichtung `native-final`

Manifest `native-final/evidence.json`, Commit
`5af70cbcc3108f30fc7c844c6074535d400a387e`, Desktopassembly SHA-256
`689A91279225725BCBA4EE9BDE43434731408DBFB3C1920BA1D8F44A05A62D3E`.
Diese gezielte Matrix meldet 17/18 Fälle PASS, 352 Assertions und 46
Bilddatensätze, insgesamt korrekt FAIL. Getestet wurden Draft,
Apply/late-preview/runtime_busy, verspätetes Connect/Status, Browse und die
Monitor-/Live-Layouts in 1120×780 und 800×560. Sie ist keine Wiederholung einer
historischen 45-Fälle-Matrix. Tatsächliche RenderScaling dieser Aufnahmen: 1.

Elf weitere PNGs wurden selbst geöffnet, unabhängig vom Testowner:

| Aufnahmen aus `native-final` | Beobachtung und begrenzter Abschluss |
| --- | --- |
| `native-layout-monitor-table-800x560-selected.png`, `native-layout-monitor-table-1120x780-selected.png` | Inventar und Detail gleichzeitig sichtbar. Der lange 0–100-Balken liegt unter den Verbrauchs-/Restzahlen; schmal werden Fenstertitel und Reset gestapelt. Breite Ansicht zeigt weitere Command-Code-Fenster einschließlich Last known, unbestätigtem Reset und Guthaben ohne Prozentbalken. F07 ist für diese native Fixture in beiden Größen belegt. |
| `native-layout-live-left-800x560-selected.png`, `native-layout-live-left-1120x780-selected.png` | Linke Liste bleibt mit ausgewählter Ausgabe sichtbar, schmal 200 px; Pause, Follow, Refresh, Clear und Footer sind erreichbar dargestellt. Der Lauf prüft gemountete Scrollcontrols, Polling und Informationswechsel. F08 ist für diese native Fixture in beiden Größen belegt. |
| `native-state-apply-800x560-confirmed.png`, `native-state-apply-1120x780-confirmed.png` | Daily revision 3, saved 3 / proxy 3 und Shell Active Daily stimmen überein. Keine falsche Pendingzeile. Der im ersten Lauf sichtbare Metadatenwiderspruch ist in diesen Aufnahmen geschlossen. |
| `native-state-apply-rejected-800x560-rejected.png`, `native-state-apply-rejected-800x560-manual-new-review.png` | Bestätigtes runtime_busy ist als Ablehnung mit erhaltenem Workspace 2 erklärt, kein Unknown. Apply bleibt bis zur bewusst erneuerten Vorschau gesperrt; danach ist die neue Bestätigung erreichbar. Manifest belegt denselben Fall auch breit und keinen automatischen zweiten Apply. F03 für diese Fälle nativ geschlossen. |
| `native-state-status-late-1120x780-confirmed-after-navigation.png` | Viewing Workspace 3 bleibt erhalten, während Summary, Liste und Shell das tatsächlich bestätigte Daily revision 2 zeigen. F05 ist breit nativ belegt. |
| `native-state-status-late-800x560-failure.png` | Der Test erreicht nach Cancel die schmale Daily-Detailseite mit sichtbarem Back to profiles. Er sucht dort vor dem Backschritt den nicht sichtbaren Workspace-3-Listenauslöser. Der Fall bleibt fehlgeschlagen und benötigt den korrigierten nativen Nachlauf; kein PASS aus dem Bild allein. |
| `native-state-browse-800x560-picker-open-x11.png` | Ein tatsächlicher nativer Ordnerdialog mit Open/Cancel ist sichtbar. Das Manifest führt Escape/Fokusrückgabe getrennt als bestandenen Ablauf; das Bild qualifiziert keinen plattformübergreifenden Dateidialog. |

Die Requestzuordnung ist in diesem Manifest korrigiert und selbst geprüft:
174 Requestdetails aus 18 Fixture-Sitzungen, kein fehlendes `fixtureSession`/
`currentCase`, keine doppelte Kombination aus Session und Sequence. Jede Session
ist genau einem Matrixfall zuordenbar; die anfänglichen `startup`-Reads haben
denselben eindeutigen Sessionbezug wie die nachfolgenden Bilder. Die sechs
Apply-POSTs gehören den Apply-, Rejected- und Late-status-Fällen, jeweils genau
einer pro Fall und Ziel p1 mit Revision 2 beziehungsweise 3. Die übrigen
untersuchten Fälle erzeugen keine Mutation. Die frühere Zuordnungslücke ist
damit für diesen Lauf geschlossen.

Der unabhängige Bildumfang beträgt nun 24 konkrete PNGs aus drei getrennten
Läufen. Kein neuer visueller Befund in den elf gezielt geprüften Endassembly-
Aufnahmen. Der 800er Late-status-Nachlauf und eine gesonderte Vergrößerungsprüfung
sind noch nicht gelesen. F02 bleibt hinsichtlich der stale-ProfileDto-Reproduktion
quellengeprüft; aus den anderen nativen Fällen wird kein ungetesteter
Profileditor-Nachweis abgeleitet. Ebenso keine pauschale Abnahme aller langen
Namen, Scrollpositionen, Providerfälle oder Accessibilitymerkmale.

## Getrennter Late-status-Nachlauf `native-status-final`

Das Manifest `native-status-final/evidence.json` nennt denselben Commit
`5af70cbcc3108f30fc7c844c6074535d400a387e` und exakt dieselbe Desktopassembly-SHA
`689A91279225725BCBA4EE9BDE43434731408DBFB3C1920BA1D8F44A05A62D3E`
wie `native-final`. Der korrigierte Fixtureweg nimmt im schmalen Detail zuerst
Back to profiles. Beide Größen wurden erneut ausgeführt: 2/2 PASS,
29 Assertions, zwei Bilddatensätze, tatsächliche RenderScaling 1.
Der ursprüngliche 17/18-Fehllauf bleibt als solcher erhalten.

Beide PNGs wurden unabhängig geöffnet:

- `native-state-status-late-800x560-confirmed-after-navigation.png`
- `native-state-status-late-1120x780-confirmed-after-navigation.png`

In beiden bleibt Viewing Workspace 3 nach der verspäteten Statusklärung
erhalten. Die Summary zeigt Daily revision 2 sowie saved 2 / proxy 2, die Shell
Active Daily. Breit ist außerdem der Appliedmarker für Daily in der weiterhin
sichtbaren Liste erkennbar. Schmal ist die erwartete Workspace-3-Detailseite mit
Back to profiles sichtbar. Die Requestdetails belegen genau einen Apply-POST
je Fall auf p1 revision 2; die verspätete Klärung wiederholt Apply nicht.

F05 ist damit für die getestete native Fixture in 1120×780 und 800×560
geschlossen. Der unabhängige Bildumfang beträgt nun 26 konkrete PNGs aus vier
getrennten Läufen. Kein neuer visueller Befund in den beiden Nachlaufbildern.
Die gesonderte Vergrößerungsprüfung bleibt bis zu ihrer eigenen Sichtung offen;
die oben genannten Grenzen für ungetestete Profileditor-, Inhalts-, Provider-
und Accessibilityfälle gelten fort.

## Unabhängige Sichtung der tatsächlichen Paket-DLLs

Die folgenden Nachweise liegen getrennt unter
`/home/tomka/.cache/lain-qualification-20261024/packaged-quota-state-ui/`:

| Manifest | Tatsächlicher Umfang |
| --- | --- |
| `scale-1/evidence.json` | 18/18 PASS, 377 Assertiondatensätze, 46 Bilddatensätze; Draft, gehaltene Modelcommands, Apply/late-preview/runtime_busy, verspätetes Connect/Status und Monitor-/Live-Layouts in beiden Größen. |
| `scale-1.25/evidence.json` | 4/4 PASS, 70 Assertiondatensätze, 12 Bilddatensätze; ausschließlich Monitor-/Live-Layouts in beiden Größen. Tatsächliche RenderScaling aller Aufnahmen: 1,25. |

Beide nennen Paketquellstand `d7200d7d700bd2d23abf8bf028d40526eb235bf7`
und Desktop-SHA-256
`89D8D86BA1519F5154841DE6F72C345643FD5A2D2E9F97F9668BE812C48937D6`.
Der gelesene `SaveEvidence`-Code dieses Stands ermittelt Pfad und SHA direkt
aus `typeof(MainWindow).Assembly.Location`. Der Paketprüfer kopiert Desktop,
Client und Core aus dem Paket in seinen privaten Harness und zeichnet deren
Hashes in `packaged-identities.json` auf. Das temporäre Harnessverzeichnis
wurde nach dem Lauf erwartungsgemäß entfernt und deshalb nicht erneut gehasht.

Stattdessen wurden die vorhandenen Dateien der beiden Pakete
`artifacts/lain-linux-x64-gateway-quota-state-20261025` und
`artifacts/lain-linux-x64-monitor-quota-state-20261025` selbst gelesen und
gehasht: Desktop, Client und Core stimmen jeweils mit den aufgezeichneten
Produktidentitäten überein. Beide `build-info.json` nennen denselben Quellstand.
Der SHA des Gateway-`SHA256SUMS` stimmt zusätzlich exakt mit dem aufgezeichneten
Paketmanifest-SHA `ced56fffeecc3e29dcdeb02ae503ec71e0b31ee5234adce0e93afff2db42435c`
überein. Das belegt die Zuordnung der geprüften Desktopassembly zum Paket;
Installation und reale Provider sind kein Bestandteil dieses Reviews.

Sechs weitere X11-PNGs wurden selbst geöffnet:

| Dateien, jeweils für `800x560` und `1120x780` | Visueller Befund |
| --- | --- |
| `scale-1/native-state-apply-{Größe}-confirmed-x11.png` | Profilliste und Review bleiben nebeneinander. Summary, Listenmarker und Shell bestätigen Daily revision 3 mit saved 3 / proxy 3. Kein falscher Pendingstatus; vor erneutem Apply ist eine frische Vorschau erforderlich. |
| `scale-1.25/native-layout-monitor-table-{Größe}-selected-x11.png` | Beide Accountflächen bleiben sichtbar; lange Balken unter Verbrauchs-/Restwerten, schmal gestapelte Angaben. Breit sind weitere Fenster mit Last known, fälligem unbestätigtem Reset und Guthaben ohne erfundenen Prozentbalken lesbar. Aktionen und Footer bleiben sichtbar. |
| `scale-1.25/native-layout-live-left-{Größe}-selected-x11.png` | Linke Requestliste bleibt neben der Ausgabe sichtbar. Pause, Follow, Refresh, Clear, Informationswahl und Retentionhinweis sind erreichbar dargestellt; die Ausgabe nutzt ihren eigenen Scrollbereich. |

Die Größen bezeichnen logische Clientmaße; die 1,25-Aufnahmen zeigen entsprechend
vergrößerte native Fenster. Kein neuer visueller Defekt in diesen sechs Bildern.
Damit ist die Vergrößerung für diese Monitor-/Live-Fixtures unabhängig gesichtet.

Die Requestdetails wurden ebenfalls vollständig auf Zuordnung geprüft:
184 Records aus 18 Sessions bei Scale 1, 36 Records aus vier Sessions bei
Scale 1,25. Keine fehlende Session-/Fallkennung, keine doppelten
Session-/Sequence-Schlüssel und keine falsche Fallzuordnung der Requests oder
Bilder. Startupreads sind über ihre Session eindeutig zuordenbar. Scale 1
enthält genau sechs Apply-POSTs, einen je Apply-, Rejected- und Late-status-Fall,
sowie zwei ausdrücklich gewählte PUTs auf Route r2 revision 2 in den beiden
Retained-model-command-Fällen. Diese Fälle prüfen den Erhalt von Draft A nach
der fremden Aktion B. Sonst keine Mutation; der Scale-1,25-Lauf enthält nur Reads.

Insgesamt wurden nun 32 konkrete PNGs aus sechs getrennten Läufen unabhängig
geöffnet. Die Paketläufe ersetzen keine historische 45-Fälle-Matrix; aus ihrer
begrenzten Auswahl folgt keine pauschale Accessibility-, Provider- oder
Releasefreigabe. Quellen und Laufzeit wurden durch diesen Reviewer nicht geändert
und keine Builds oder Tests ausgeführt.
