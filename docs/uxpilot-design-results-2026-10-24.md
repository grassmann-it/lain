# lain: geprüfte UX-Pilot-Entwürfe vom 24.10.2026

Status: Entwurfsauftrag abgeschlossen. **14 selbst gesichtete Ergebnisframes,
70 von 500 freigegebenen Credits verbraucht.** Alle Ergebnisse bleiben Vorschläge;
keine Freigabe für Produktarchitektur oder Implementierung.

## Öffnen und vergleichen

- [UX-Pilot-Seite: lain · UX design review · 2026-10-24](https://uxpilot.ai/a/ui-design?page=5a6Hhb1aH085Fs6REXW4)
- Page-ID: `5a6Hhb1aH085Fs6REXW4`
- Geprüfte Gruppe: `g1Jnn1lQgyMCgUK2X7tx`, „Reviewed concepts …“.
- [Lokale Galerie aller geprüften UX-Pilot-PNGs](../artifacts/uxpilot-20261024/index.html)
- [Manifest mit IDs, Bildlinks, HTML, Metadaten und Positionen](../artifacts/uxpilot-20261024/final-manifest.json)

Auf Nutzerwunsch liegen die 14 Ergebnisse **nebeneinander** in einer Zeile bei
`y=0`, in der folgenden Reihenfolge. Der Abstand beträgt 1560 Canvaspixel pro
Frame, also mindestens 120 Pixel zwischen den vollständigen 1440-Pixel-Bildern.
Referenzbilder stehen darüber (`y=-1700`); frühere Iterationen darunter
(`y=1600/3000/4400`). Alle Originale und Zwischenstände bleiben erhalten.
Sie sind ausdrücklich als frühere, teilweise fehlerhafte Fassungen markiert.
Drei Hinweise stehen außerhalb der Appframes.

Die gespeicherten seitenbezogenen Positionen wurden über MCP erneut gelesen:
**36 Designobjekte plus 15 Canvasobjekte, keine Überschneidung**, auch bei
konservativ mitgerechneten 1440×1024-PNG-Flächen.
[Koordinatenprüfung](../artifacts/uxpilot-20261024/canvas-check.json).

| Frame, von links nach rechts | Design-ID | UX-Pilot-Bild |
| --- | --- | --- |
| 1. Accounts · overview · verified | `Rf8aun5CqK1jJDDEsy1R` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/b1eadb27fd-ca6b46d1cc733d82bce6.png) |
| 2. Accounts · lower inventory · before detail | `e15bU4GJVZiLJqr1AD7h` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/d601e78de7-ab94876e73999bb1eb80.png) |
| 3. Accounts · lower inventory · adjacent Z.ai | `ZFjD7XKb3sOKSBNNikNQ` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/608961a4cc-e81f217127a57025db45.png) |
| 4. Connect · provider chooser · final review | `gzASp44CCY795jSZqF7i` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/9a7182b71e-cee26407888921cba413.png) |
| 5. Connect · Codex · start | `VqOo9QWKoHTuPl2QFBA3` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/5e22731cb1-09b34665710fb42220ea.png) |
| 6. Connect · Codex · waiting | `k0kGywZOHwQqiJP6M9dc` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/78b45ee8e0-176e6ed43c0b5167c437.png) |
| 7. Connect · Kilo key target · final review | `wQVdJZc3s1aOOHiSyLRP` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/c62665a2bd-842c927fa4e4b5e4a954.png) |
| 8. Connect · Codex · connected | `XRRl0bWu3u4yabFGoOVB` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/214f80c426-83e06454b66bd5e30aef.png) |
| 9. Models · reviewed inventory | `XjprbmkRcPW8vOyOPOEr` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/7728cc3fab-765948b632df61864f57.png) |
| 10. Profiles · reviewed Daily | `tUQuAL5HzEst2g0t69B1` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/67c8981c4f-bd5b693112b2eae72baf.png) |
| 11. Profiles · global Apply review | `qCI0uheft2XiFWKSBJmQ` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/efb39161af-bab38da2d7ee53d6eeeb.png) |
| 12. Usage · reviewed results | `VFZv56jHJJ2zSdBAQhNG` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/e2eeeda7c7-d1e2a6bb15d2390d263a.png) |
| 13. Live · reviewed ephemeral output | `t41xl53MKsTLKcXbKOmb` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/1d28026e5f-0997900628577d6ce3a9.png) |
| 14. Settings · native checkboxes · reviewed | `O63jRSAvsTNvo54maqIS` | [PNG](https://storage.googleapis.com/uxpilot-auth.appspot.com/screenshots/09c5d8771c-96a53bdbe1b8213eeae7.png) |

## Grundlage und übertragene Referenzen

Ausgangscode: `5dc916fcea702f2a196520903937570745e26dac`.
Gelesen und verwendet: [Startauftrag](uxpilot-codex-handoff-2026-10-24.md),
[UX-Review F01–F11](ux-review-2026-10-24.md),
[Foundation und Detailprompts](uxpilot-design-prompts-2026-10-24.md),
[Brandreferenz](brand-reference.md),
[Connectentwurf](account-connect-redesign-2026-10-24.md) und
[AC-Arbeitspakete](account-connect-work-packages/README.md).
Architecture-sparring wird fortgeführt; der Entwurf trifft keine stillschweigende
Produktentscheidung.

Die passenden vorhandenen Original-PNGs wurden lokal angesehen; keine erneute
Aufnahme der Produktionsoberfläche. Elf ausgewählte synthetische Referenzen
wurden übertragen: Accounts breit, vor/nach Öffnen und schmal, Connectchooser,
bisheriges Providerformular, Models, Profiles, Usage, Live und Settings.
Die beiden Accounts-Startreferenzen sind byteidentisch aus getrennten Capturepfaden.
Zusätzlich wurde die originale Katze unverändert hochgeladen
(1.665.508 Bytes, 1254×1254). Keine Kontodaten oder Credentials.
[Assetprotokoll](../artifacts/uxpilot-20261024/session.json),
[weitere Referenzen](../artifacts/uxpilot-20261024/other-references.json).

## Aufträge und tatsächliche Credits

Die Freigabe „ich geb ich so 500 credits frei“ gilt für die gesamte Runde.
UX Pilot meldet Kosten erst nach Abschluss; es gibt keinen harten Budgetparameter.
Deshalb fünf aufeinanderfolgende Generierungen. Kein kostenpflichtiger Parallelstart.

| Auftrag mit tatsächlich übermitteltem Prompt | Job-ID | Credits |
| --- | --- | ---: |
| [Accounts](../artifacts/uxpilot-20261024/01-accounts-submitted-prompt.txt) | `n3N91re1xaEXKKdWGILk` | 13 |
| [Connect](../artifacts/uxpilot-20261024/02-connect-submitted-prompt.txt) | `rem1UkWGgSP2kWRakHZU` | 18 |
| [Models/Profiles](../artifacts/uxpilot-20261024/03-models-profiles-submitted-prompt.txt) | `unefAeBymh1t7V7wwSBN` | 18 |
| [Usage/Live](../artifacts/uxpilot-20261024/04-usage-live-submitted-prompt.txt) | `UN3fbniTIaNhvvhRiKNG` | 14 |
| [Settings](../artifacts/uxpilot-20261024/05-settings-submitted-prompt.txt) | `lMwPduyDk0mCJ7Z8igvl` | 7 |
| **Gesamt** | **12 ursprüngliche Generierungen, 14 geprüfte Ergebnisversionen** | **70 / 500** |

Die vollständigen Jobantworten heißen im selben Artefaktverzeichnis
`01-accounts-job.json` bis `05-settings-job.json`.
Deterministische HTML-Korrekturen über UX-Pilot-MCP erzeugten neue Versionen,
laut jeder Toolrückgabe mit **0 Credits**. Originale wurden nicht überschrieben.
Der erste zu lange Accountsauftrag wurde vor Jobstart abgewiesen; die API hat
eine Grenze von 8000 Promptzeichen. Keine weiteren Generierungsläufe notwendig.

## Eigene Prüfung und Korrekturen

Alle 14 endgültigen **tatsächlichen UX-Pilot-PNGs** wurden selbst angesehen.
Korrekturen wurden über `get_design` und anschließend versionierte HTML-Edits
mit Ausgangshash in UX Pilot gespeichert, danach erneut als UX-Pilot-Bild geprüft.
Das ist eine bearbeitete UX-Pilot-Entwurfsserie, kein lokal erfundener Ersatz.
Dunkles Grün/Schwarz, Mint, unveränderte Katze, native Desktopchrome, direkte
Profiles-Navigation und genau ein Modusdropdown rechts unten bleiben durchgehend.

### Accounts und stabiles Öffnen

Der erste Generatorentwurf entfernte beim Öffnen fremde Quotazeilen und verschob
Kacheln. Außerdem fehlten Kimi-Monatsrest/Reset, Zeit- und Fehlerzustände und
korrekte Guthabeneinheiten. Diese Abweichungen wurden korrigiert.

Der Vorschlag reserviert rechts dauerhaft 400 Pixel; links bleibt eine scrollbare
Inventarspalte mit identischem vollständigem Inhalt. Alle gemeldeten Fenster
stehen in den geschlossenen Accountzusammenfassungen. Guthaben ohne Nenner haben
keinen Prozentbalken. Vergangene Resets bleiben unbestätigt, MiMo ohne Quelle
neutral. Gemeinsames Anbieterbudget wird nicht summiert. Kein erfundener Key
oder Sekundenzeitpunkt; „Gateway Mode“ wurde zum Informationsmodus korrigiert.

Frames 2 und 3 zeigen denselben unteren Scrollstand: Kimi, Z.ai und MiMo bleiben
an genau derselben Position; nur Auswahlfarbe und rechter Inspector ändern sich.
Damit ist die ausgewählte Z.ai-Kachel zugleich mit ihrem Detail sichtbar.
Frame 1 erhält den ursprünglichen oberen Startausschnitt. Der feste Inspector
verringert die Zahl gleichzeitig sichtbarer Accounts und lässt vor Auswahl Platz
frei. Das ist ein bewusster Entwurfsvorschlag mit diesem Nachteil.

### Connect

Anbieterchooser ohne wiederholte Einstiegskategorien oder erfundene Accountzahlen.
Codex startet über den vorhandenen Browserweg; Initialzustand und Warten sind
separate Frames. Im Warten steht nur „Reopen browser“. Kilo benötigt im Zielentwurf
Name und Key, keinen Pflichtplan und keine vorgelagerte Modellwahl.
**Das Kiloformular hängt von AC-00/01 ab; es belegt keinen ausgelieferten Walletreader.**

Entfernt wurden automatische Discovery, erfundener `.env`-Import, unbelegtes
„Full read access“, „Credential verified“ und ein erfundenes Keypräfix.
Der Codex-Abschluss sagt nur Credential verbunden und Quota verfügbar, keine
Inferenzprobe. Done ist primär, Choose models optional; doppelte Modellaktion
und irreführender Active-Badge sind bereinigt. Anbieterlinks sind Designziele,
keine qualifizierten echten Loginabläufe.

### Models und Profiles

45er-Inventar als Tabelle mit acht vollständig benannten synthetischen
Beispielzeilen, ohne weitere erfundene Aliase. Preise, Free-/Flat-fee-Aussagen,
Monatscap, Config-ID und automatischer Provider-Cascade-Fallback wurden entfernt.
Nicht gelieferte Fachwerte heißen „Not supplied“. gpt-lain-01 ist strikt an
Personal Codex gebunden und wird von drei Profilen verwendet.
Disabled/Pending apply bei gpt-lain-02 bleibt getrennt von dessen Menü-Sichtbarkeit;
gpt-lain-04 ist Enabled und Hidden. Gespeichertes Disable wirkt erst beim nächsten
globalen Apply. Edit ist sofort erreichbar; die sichtbaren Modellmenüs enthalten
denselben vollständigen Commandsatz.

Profiles zeigt Viewing Daily und Applied Workspace 2 getrennt. Modellzeilen haben
sichtbaren Direktzugriff; Export und Client setup sind getrennt beschriftet.
Erfundene Policy-, Export-, Routing- und Synchronisationswerte wurden entfernt.
Der Apply-Dialog liegt kompakt im Appbereich über genau diesem Profilframe:
Workspace 2 → Daily, zwei gespeicherte Änderungen, globaler Proxyneustart und
mögliche Unterbrechung aktiver Requests. Reine Profilwahl löst kein Apply aus.

### Usage und Live

Usage zeigt sofort die drei Breakdownzeilen. Synthetische Zahlen geprüft:
128 Requests, 132 Attempts, vier historische Fehler (3,0 %), 81.986 Tokens.
Zeilen: Codex 70/72/2/43.200; Command 30/32/1/21.000; Kimi 28/28/1/17.786.
Die Zeilen bezeichnen ausdrücklich Completion errors; historische Fehler bleiben
ein anderes Maß. Keine Kosten oder erfundene Input-/Outputaufteilung.
Datumspräzision steht lokal aufklappbar mit Europe/Berlin und 25.10. exklusiv.
Tagesreihe und First-output-Performance sind als **alle Accounts** beschriftet;
P50 240 ms, P95 510 ms und Coverage 120/132 stehen zusammen.

Live bleibt flüchtig: sechs Streams, bis 120 Sekunden Retention, null verworfene
Events. Nur die belegte Conversation wird gruppiert. Andere Einträge bleiben
Requests. Für die Conversation gibt es einen lokalen Requestpicker.
Erfundene Altersangaben, Countdown, Tokenzahlen und Toolnamen wurden entfernt.
Pause stoppt keine Inferenz; Clear view löscht keine persistente Usage.
Die zusätzliche Live-Toolbar wird von der Arbeitsflächenhöhe abgezogen, damit
Navigation und einziger Advanced-Modusdropdown im 780er-Apprahmen bleiben.

### Settings

Drei ruhige Gruppen: seitenbezogene Dichte, Fenster/Benachrichtigungen und Hilfe.
Normal/Compact ist unabhängig vom Informationsmodus. Tray-unavailable-Zustand
und weiterlaufender Host stehen bei den betreffenden Controls. Keine zweite
Modusauswahl, erfundene Version oder permanente Erfolgsmeldung. Unnötige große
Quotavorschau entfernt; alle drei Gruppen passen in den ersten Viewport.
Native Checkboxen ersetzen ein im Renderer unlesbares Iconfont-Häkchen.

## Bewertung und konkrete Abnahmepunkte

Nutzerreaktion: „sieht eigentlich gut aus“, mit Nachfrage nach Bewertung und
abzunehmenden Unterschieden. Das ist positive Rückmeldung zum Gesamteindruck;
eine pauschale Freigabe aller Layoutentscheidungen oder Produktumsetzung wird
daraus nicht abgeleitet.

Bewertung: Die breite Gateway-Richtung verbessert Orientierung und Zugriff.
Die folgenden Punkte beschreiben konkrete Übernahmen; sie bleiben bis zur
Entscheidung Empfehlungen.

| Bereich / Frames | Unterschied zur bisherigen Oberfläche | Empfehlung / relevante Abwägung |
| --- | --- | --- |
| Accounts 1–3 | Dauerhaft reservierter 400-Pixel-Inspector neben einer scrollbaren Accountspalte | Breit übernehmen, wenn weniger gleichzeitig sichtbare Accounts und freie Spalte vor Auswahl akzeptiert werden. Das ist die größte Platzentscheidung. Schmale Variante noch ausarbeiten. |
| Models 9 | Tabelle mit lokalen Commands und benachbartem Inspector statt vieler hoher Karten | Als Standard übernehmen. Acht Beispielzeilen zeigen die Richtung; der echte 45er-Bestand und lange Namen müssen später daran bestehen. |
| Connect 4–8 | Anbieterwahl, fokussierter Zugang, separates Browserwarten, konkreter Abschluss und optionale Modellwahl | Ablauf übernehmen; tatsächliche Login-/Quotafähigkeiten bleiben connectorabhängig und werden durch das Bild nicht freigegeben. |
| Profiles 10–11 | Bestehendes Master/Detail verdichtet; Viewing/Applied und Apply/Export/Clientsetup klar getrennt | Gestaltung übernehmen. Der globale Apply-Umfang ist eine fachliche Vorgabe, keine neue Designfreiheit. |
| Usage / Live 12–13 | Erste Ergebniszeilen sofort sichtbar; lokale Drilldowns; stabile Liste neben zentraler Liveausgabe | Breite Anordnung übernehmen. Filter-, Fehler- und Messsemantik aus dem Auftrag bleiben verbindlich. |
| Navigation | Simple führt Live in More; Advanced zeigt Live direkt | Empfehlung für einen ruhigen Simple-Modus. Bei täglicher Live-Nutzung ist ein direkter Live-Eintrag in Simple eine begründete Alternative. Profiles direkt und der einzelne Modusdropdown sind bereits vorgegeben. |

Settings braucht vor allem die im Entwurf gezeigte Beschriftungs- und
Gruppenbereinigung; daraus folgt keine neue große Settingsstruktur.
Katze, dunkler Look, Mint, keine erfundenen Quotas und keine springenden Kacheln
sind bereits beauftragte Anforderungen und benötigen keine erneute Bestätigung.

Empfohlener Abnahmeumfang jetzt: die breite visuelle Richtung und die konkret
gewählten Listen-/Detailmuster. Vollständige UX-Abnahme erst mit 800×560,
Monitor/Barebones, Fehler-/Offlinezuständen und qualifizierter Tastatur-/
Fokusbedienung. Designzustimmung autorisiert weiterhin keine Produktcodeänderung.

## Prüfgrenzen und nächster Entscheidungspunkt

Die Appfläche ist 1120×780. UX Pilot exportiert 1440×1024 mit ungenutztem Rand;
der Rand ist keine neue Produktgröße. Ein Rendererproblem kollabierte anfänglich
Flexinhalte auf Höhe null. Explizite 690-Pixel-Arbeitsfläche (Live 650 plus Toolbar)
behebt das in den tatsächlichen UX-Pilot-PNGs. Lokale Geometrieprüfung ist nur
ein Zusatz zur Sichtprüfung, kein Ersatz für die UX-Pilot-Bilder.

Diese Runde liefert breite Gateway-Entwürfe. Eine neue 800×560-Variante,
Barebones-/Monitorvarianten, Fehler-/Offlinevarianten und der vollständige
Advanced-Editor sind noch nicht separat visuell ausgearbeitet. Die vorhandenen
schmalen Originalreferenzen bleiben verfügbar. Expertenbereiche sind als lokale
Details verortet; ihre vollständige Bedienung ist nicht als fertig qualifiziert.
Die acht Inventarbeispielzeilen erfinden keine Daten für die übrigen 37 Einträge.

Zusätzliche lokale HTML-Geometrieprüfung: alle 14 Appflächen 1120×780,
Footer vollständig erreichbar, genau ein Modusdropdown im Footer und direkter
Profiles-Zugang. Das Accountpaar hat identische Kachelrechtecke und denselben
Scrollwert 515. Im tatsächlichen UX-Pilot-PNG ändern sich links außerhalb der
Auswahlfläche **null Pixel**. [Geometrieprotokoll](../artifacts/uxpilot-20261024/layout-check.json),
[Gesamtvalidierung](../artifacts/uxpilot-20261024/validation.json).
53 lokale Dokumentationslinks und alle Bild-/HTML-/Metadatenverweise sind auflösbar;
`git diff --check` meldet keine Whitespacefehler.

Der gemeinsame Repositorystand wurde während dieser Entwurfsrunde durch andere
Commits fortgeschrieben; beim Abschluss steht HEAD auf `60466da44b87f1171cbc1ddcb962f9e1a1a8443f`.
Der Vergleich mit der Start-Hashliste findet 17 geänderte vorher versionierte
Produktdateien, deren Bytes sämtlich dem inzwischen neuen HEAD entsprechen.
Diese Änderungen wurden hier weder bearbeitet noch zurückgesetzt. Eine globale
Bytegleichheit zum Sitzungsstart wird deshalb nicht behauptet. Die Designs beziehen
sich weiterhin auf die vorbereiteten synthetischen Referenzen; neuere Lieferung
ist durch diese Entwurfsprüfung nicht erneut qualifiziert.

Echte Betriebssysteminteraktion, Rechtsklick-/Tastaturparität, Fokusrestauration,
Screenreader und echte Providerflows wurden nicht am Avalonia-Produkt abgenommen.
Die HTML-Bilder belegen Anordnung und Hierarchie, keine Backendfähigkeiten.
Nächster Entscheidungspunkt ist die Auswahl bzw. Revision dieser Entwurfsrichtung;
daraus folgt noch kein Implementierungsauftrag.

## Folgeauftrag: konkrete Arbeitspakete

Auf „mach arbeitspakete daraus“ sind
[UXD-00–09 mit Reihenfolge, Dateieigentum und Abnahme](ux-design-work-packages/README.md)
ausgearbeitet. Sie konkretisieren die bestehenden UX-01–03 und schließen an die
AC-/ST-Pakete an. Start: Integrationsabgleich und gemeinsame Shell, danach
Accounts/Inspector, Connect, Models/Profiles, Usage/Live und Settings.

Reservierte 400-Pixel-Accountsfläche, Simple-Live-Navigation und schmaler
Detailwechsel sind als Vorschläge mit Abwägung benannt. Fehlende schmale,
Monitor-/Barebones-, Offline-/Fehler- und native Tastaturnachweise haben eigene
Aufgaben in UXD-09. Der Paketauftrag erzeugt weder eine pauschale Designabnahme
noch eine neue Produktimplementierung; keine weitere UX-Pilot-Generierung nötig.

In diesem Entwurfsauftrag keine eigenen Produktcodeänderungen, Builds, Installation,
Dienstneustarts, Providerlogins, Inferenzproben, Käufe, Commits oder Veröffentlichung
öffentlicher Previews.
Nur UX-Pilot-Artefakte und die lokale Ergebnisdokumentation wurden bearbeitet.
Das Artefaktverzeichnis ist ignoriert und muss bei Rechnerwechsel mitgegeben werden.
