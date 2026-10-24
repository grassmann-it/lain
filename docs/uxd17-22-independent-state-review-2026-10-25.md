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
