# Arbeitspakete für die geprüften UX-Pilot-Entwürfe

Aktueller Implementierungsstand: [kumulative Qualifikation vom 25.10.](../cumulative-implementation-2026-10-25.md). Der folgende Text hält den ursprünglichen Planauftrag und seine Folgerunden fest. Die beiden später beantworteten Nutzerfragen sind geklärt: Draftnavigation B (direkter Wechsel, bleibender Hinweis/Rückweg/Verwerfen) und Simple-Live A (direkt in der Hauptnavigation); Profiles bleibt direkt erreichbar. Frühere offene Fragen sind damit keine aktuellen Sperren.

Stand: 25.10.2026; Grundpakete vom 24.10. Auftrag: aus den bewerteten Entwürfen
konkrete Arbeitspakete machen. **Planung abgeschlossen bedeutet hier weder Designabnahme noch Beginn
der Produktimplementierung.** In diesem Auftrag keine Produktcodeänderungen,
Builds, Installation, Dienstneustarts oder echten Provideraktionen.

## Ziel und Grundlage

Accounts werden zum stabilen Einstieg: alle gemeldeten Kontingente vergleichen,
einen Account öffnen und direkt daneben handeln, ohne andere Kacheln zu
verschieben. Anschließend werden Connect, Models/Profiles, Usage/Live und
Settings mit derselben Informationshierarchie und Bedienung überarbeitet.

Maßgebliche Referenzen:

- [Bedienabläufe und Randzustände vom 25.10.](../uxpilot-flows-2026-10-25.md),
  [neue Auswahlgalerie](../uxpilot-flows-2026-10-25.html) und
  [Übergabe UXD-16 mit sechs Paketen UXD-17–22](16-state-continuity.md).
  Die zunächst offenen Fragen zu Draftnavigation und Simple-Live sind wie oben
  bestätigt beantwortet. Die übrigen bestätigten Layouts bleiben gesetzt.
- [Geprüfte Entwürfe, alle 14 Frame-IDs und direkt erreichbare PNGs](../uxpilot-design-results-2026-10-24.md).
- [Bestätigte Auswahl und abgeschlossene Folgeprüfung](../uxpilot-selection-2026-10-24.md),
  [neue Offlinegalerie](../uxpilot-followup-2026-10-24.html),
  [29 Bilder mit IDs, Quellen und Bewertungen](../uxpilot-followup-2026-10-24.json).
- [UX-Pilot-Canvas](https://uxpilot.ai/a/ui-design?page=5a6Hhb1aH085Fs6REXW4),
  finale Gruppe `g1Jnn1lQgyMCgUK2X7tx`. Referenzen oben, Ergebnisse
  nebeneinander, verworfene Iterationen darunter. Nur die finale Gruppe verwenden.
- [Review mit F01–F11](../ux-review-2026-10-24.md),
  [Detailprompts und synthetische Werte](../uxpilot-design-prompts-2026-10-24.md),
  [ursprünglicher Startauftrag](../uxpilot-codex-handoff-2026-10-24.md).
- [Brandreferenz](../brand-reference.md),
  [Modi und Aktionen mit ihrem jeweiligen Entscheidungsstatus](../ui-modes-and-actions.md),
  [Produktumfang](../product-scope.md), [Serviceverträge](../service-contracts.md).

Die Artefakte unter `artifacts/uxpilot-20261024/` und die Originalaufnahmen unter
`artifacts/ux-review-20261024/` sind lokale, ignorierte Ergänzungen. Für eine
Übergabe diese Dokumente einschließlich Ergebnisprotokoll mitgeben; die dortigen
echten Bild-URLs funktionieren unabhängig vom lokalen Artefaktverzeichnis.
Die acht Models-Beispielzeilen sind keine Datenquelle für weitere 37 Modelle.

## Feste Vorgaben und noch offene Übernahmen

**Aktualisierung nach ausdrücklicher A/B/C-Auswahl:**
[Bestätigte Entscheidungen und Folgeprüfung](../uxpilot-selection-2026-10-24.md)
haben für die dort genannten Layouts Vorrang. Accounts breit A und schmal A
(nebeneinander), Models A, Usage A sowie Monitor B mit ergänzten Verbrauchsbalken
sind gewählt. Die Folgeauswahl bestätigt zusätzlich Monitorbalken A (lang unter
den Zahlen), Apply B (Reviewfläche im Profildetail) und Live schmal A (dauerhafte
linke Requestliste). Dies bestätigt keine anderen Navigations- oder Fehlerentwürfe.

Fest sind Originalkatze, dunkler Grün-/Schwarzlook, Mintakzent und native
Avalonia/Semi/Ursa-Anmutung. Accounts starten zuerst. Profiles bleiben im Gateway
direkt erreichbar, auch in Barebones. Genau ein Informationsmodusdropdown steht
rechts unten. Darstellungsmodus, Betriebsabsicht Monitor/Gateway und lokale
Dichte bleiben getrennt. Öffnen eines Details darf das Inventar nicht neu packen.

Alle gemeldeten Quotafenster bleiben in geschlossenen Accountzusammenfassungen
vorhanden. Unbekannt darf weder als 0 noch als 0 % erscheinen. Guthaben ohne Nenner
erhalten keinen Prozentbalken; vergangene Resets bleiben bis zur Bestätigung
unbestätigt. Gemeinsame Budgets nicht addieren, leere Einzelbudgets nicht als
accountweite Sperre ausgeben. Keine erfundenen Quotas, Preise, Fähigkeiten,
Logins oder automatischen kostenpflichtigen Fallbacks.

Die frühere positive Rückmeldung gab zunächst die Ausarbeitung frei. Inzwischen
hat der Nutzer die unten entsprechend markierten Layouts ausdrücklich gewählt.
Andere Punkte bleiben Vorschläge; eine pauschale Designfreigabe folgt daraus nicht:

| Punkt | Planungsannahme und Nutzen | Preis / Alternative | Zuständig |
| --- | --- | --- | --- |
| Breite Accounts, bestätigt A | Dauerhaft reservierter Inspector rechts mit 400 px bei 1120×780; links eine Inventarspalte | Weniger Accounts gleichzeitig, freie rechte Spalte vor Auswahl; bewusst gewählt. | UXD-00/02 |
| Schmale Accounts, bestätigt A | Bei 800×560 Liste und Detail nebeneinander; Referenz 340 px Liste und verbleibende Detailbreite | Engere Controls; lange Namen, volle Quotas und Expertenaktionen qualifizieren. Frühere Backseite ersetzt. | UXD-01/02, Prüfung UXD-09 |
| Navigation, Nutzerwahl bestätigt | Simple A: Live direkt. Profiles bleibt direkt. Draftnavigation B erhält den Entwurf beim unmittelbaren Wechsel mit Hinweis/Rückweg/Verwerfen. Die übrige Modusmatrix bleibt gemäß UXD-01 getrennt zu bewerten. | Kein neuer A/B-Umschalter; direkte Navigation und erhaltene Entwürfe nativ prüfen. | UXD-00/01/17 |
| Models, bestätigt A | Tabelle mit benachbartem Detail statt hoher Karten | Lange Namen und vollständige Editoren brauchen native schmale Abnahme. | UXD-04 |
| Usage, bestätigt A | Ergebnistabelle mit Seitendetail | Weniger Tabellenbreite; Scope und partielle Werte müssen trotzdem lesbar bleiben. | UXD-06 |
| Monitor, bestätigt B mit Balken A | Tabelle aller Quotafenster mit langen Verbrauchsbalken unter den Zahlen | Mehr Zeilenhöhe als Inline-B ist akzeptiert; ohne Nenner oder bei unbekannten Werten kein Prozentbalken. | UXD-01/02/10, Prüfung UXD-09 |
| Apply, bestätigt B | Feste Reviewfläche im Profildetail, Profilliste bleibt sichtbar | Explizite Bestätigung und Revisionsschutz; Profilwechsel invalidiert die Vorschau statt sie auf ein neues Ziel umzuhängen. Frühere Dialogempfehlung ersetzt. | UXD-05/12 |
| Live schmal, bestätigt A | Auch bei 800×560 dauerhafte Requestliste links und Ausgabe rechts, Referenz 200 px Liste | Weniger Ausgabebreite ist akzeptiert; kein automatischer Selector-/Seitenwechsel. Simple zeigt Live gemäß späterer Nutzerwahl direkt. | UXD-07/14 |
| Übrige Seiten, Vorschläge | Verdichtetes Profiles-Master/Detail und drei Settingsgruppen, weitere Fehlerdarstellungen | Keine pauschale Freigabe; native Fehler-/Expertenabnahme bleibt erforderlich. | UXD-05/07–09 |

Offene Übernahmen hindern den lesenden Integrationsabgleich und die Ausarbeitung
unabhängiger Fälle nicht. Von ihnen abhängige Gestaltung bleibt ein prüfbarer
Vorschlag, bis die jeweilige Entscheidung vorliegt; keine pauschale neue
Bestätigungsrunde und keine stillschweigende Freigabe beim Implementierer.

## Technischer Bezugsstand

Gezielter Quellabgleich auf Main; letzter gelesener HEAD
`a4b7374cd59f77b06f791f8602e7a5987e29d1a8`. Die Entwurfsbilder stammen vom
vorbereiteten Stand `5dc916f`; sie belegen keine Qualifikation neuerer Änderungen.
Parallel sind insbesondere Providerdokumentation und Hostdateien in Bearbeitung.
Vor Umsetzung aktuellen HEAD und betroffene Dateien erneut abgleichen. Bereits
gelieferte Arbeit erhalten; weder fremde Änderungen überschreiben noch mitstagen.

Der folgende Befund gehört zum ursprünglichen Paketzuschnitt vom 24.10.; er ist
kein aktueller Mangelkatalog. Den fortgeschrittenen Implementierungs-/Abnahmestand
im [Ausführungsbericht](../work-packages-execution-2026-10-24.md) und den gezielten
Quellabgleich auf `5c6dfc7c1178a64b83ec4f852c1eb5b1662d9ec6` in
[UXD-16](16-state-continuity.md) vor neuen Änderungen berücksichtigen.

Schon vorhanden waren Präferenzspeicherung, Draft-/Navigationszustand,
kontextueller Rückweg, Quotahelpers, Login-Lifecycle und eigenständige Usage-/
Liveviews. Diese Mechanismen weiterverwenden. Kritische Unterschiede zum Ziel:

- Accountdetails wachsen im `MasonryPanel` als Expander und verschieben Nachbarn.
- Der äußere Workspace-Scrollviewer besitzt bisher den Hauptscrollanker.
  Lokale Listen und Inspector benötigen einen abgestimmten Ersatz.
- Navigation ist noch auf fünf gleichrangige Ziele gebaut; vorhandene Tests
  verlangen das ebenfalls. Gezielte Revision statt bloßes Löschen der Prüfungen.
- Core-DTOs für Methoden, Betriebsmodus und Applyvorschau existieren; ihre
  tatsächliche Host-/Clientverdrahtung ist unterschiedlich weit. UXD-00 erfasst
  reale Endpunkte, Revisionen und Ausfallverhalten vor dem Anschluss.

## Pakete und Reihenfolge

Nach ausdrücklicher [Auswahl und Vertiefungsrunde](../uxpilot-selection-2026-10-24.md)
ergänzen UXD-10–15 die ursprünglichen Pakete. Als Deltas gegen den aktuellen
Umsetzungsstand anwenden; bereits erledigte Arbeit nicht erneut aufbauen.
Dieser Design-/Planauftrag startet keine Produktimplementierung oder Agenten.

Folgeprüfung und Folgeauswahl abgeschlossen: 24 neue finale Bilder;
Monitorbalken A, Applypräsentation B und schmale Liveansicht A sind ausdrücklich
bestätigt. Frühere Empfehlungen A/A/B bleiben Bewertungshistorie; die fünf zuvor
gewählten Grundmuster bleiben verbindlich. Verbrauch
dieser Zusatzrunde 72/400 Credits, kumulativ 210. Native Nachweise in den Paketen
sind geplante Abnahme, keine ausgeführten Produktprüfungen.

| Folgepaket | Ergebnis | Grundlage / bestehender Owner |
| --- | --- | --- |
| [UXD-10](10-selected-layouts-monitor.md) | Gewählte Panels beider Größen; Monitortabelle mit Verbrauchsbalken | UXD-00/01/02/04/06 |
| [UXD-11](11-connect-interruptions.md) | Ablauf, Cancel und Reconnect derselben Accountidentität | UXD-03, AC-02/09 |
| [UXD-12](12-profiles-apply-recovery.md) | Revisionskonflikt, unbekannter Applyausgang, volle Profileditoren | UXD-05, ST-02 |
| [UXD-13](13-usage-partial-scope.md) | Teilmessung, globaler Scope, Filterdraft und Quellenfehler | UXD-06, ST-03 |
| [UXD-14](14-live-pause-source.md) | Schmale Liveausgabe, Pause/Retention und Sourceidentität | UXD-07 |
| [UXD-15](15-settings-and-native-gates.md) | Settingsrollback und native integrierte Nachweise | UXD-08/09, Seitenowner |
| [UXD-16](16-state-continuity.md) | Gemeinsame Übergabe und Reihenfolge für UXD-17–22; kein zusätzliches Implementierungspaket | Bestehende Owner und AC-/ST-Verträge |
| [UXD-17](17-navigation-drafts.md) | Abschnittsentwurf erhalten, gewählte Draft-/Simple-Live-Präsentation und Rückweg | UXD-01; Draft B und Simple-Live A bestätigt |
| [UXD-18](18-account-snapshots.md) | Erster Snapshot, leeres Inventar, lokale Aktualisierung und vollständige Zusammenfassungen | UXD-02, Shell UXD-01 |
| [UXD-19](19-model-command-identity.md) | IDgebundene Commands, Referenzsperre, Filterleere und verschwundene Auswahl | UXD-04, Policy-/Referenzowner |
| [UXD-20](20-connect-client-setup.md) | Loginvoraussetzungen und getrenntes Prepare/Install/Apply | UXD-03/05, AC-/ST-Anschlüsse |
| [UXD-21](21-apply-status-consistency.md) | Veraltetes Review und unbekannter Applyausgang in Detail, Liste und Shell | UXD-05/12, Shell UXD-01, ST-02 |
| [UXD-22](22-flow-acceptance.md) | Native Eingabe-, Request- und Bildnachweise für die Zustandsfolgen | UXD-09/15 und Seitenowner |

Weitere Zustandsrunde vom 25.10. abgeschlossen: 18 finale Entwürfe, 50 neue
Credits, damit 122/400 Zusatzcredits und 260 kumulativ. Die Galerie enthält
21 Bilder. Auf weiteren Nutzerauftrag ist UXD-16 jetzt in sechs eigenständige
Pakete UXD-17–22 aufgeteilt. Jedes enthält Ergebnis, Dateien/Owner, vorhandene
Mechanismen, konkrete Deltas, Abhängigkeiten und geplante Abnahme. Zuerst
bereits erfüllte Fälle prüfen, dann Accounts als ersten Fachdurchstich;
Connect/Models/Profiles folgen mit den bestehenden Ownern. UXD-20 und UXD-21
berühren dieselbe Profilesdatei und werden vom selben Owner nacheinander bearbeitet.
UXD-22 bereitet früh vor und schließt nach den jeweiligen Fachfällen ab.
Keine neuen Produktprüfungen oder Implementierung in diesem Planauftrag.

UXD-10 zuerst auf die bestehenden Owner verteilen; danach berührte Fachstrecken
UXD-11–14 anschließen. UXD-15-Settings kann nach der gemeinsamen Shell unabhängig
vorbereitet werden; die integrierte UXD-09-Abnahme folgt nach den betroffenen
Seiten. Kein harter Rückverweis von einem Fachpaket auf die fertige Abnahme.

UXD konkretisiert die bestehenden UX-01–03 aus dem
[Ausführungsplan](../work-packages-execution-2026-10-24.md). AC-00–10 und ST-01–05
behalten ihre fachlichen Zuständigkeiten; dieser Plan startet sie nicht neu.

Übergabereife der ursprünglichen Planung: UXD-00 ist als lesender Vorlauf konkret beschrieben. Die
Seitenpakete sind unter den benannten Layoutannahmen ausgearbeitet; gemeinsame
Verträge aus UXD-00/01 und tatsächliche AC-/ST-Anschlüsse sind vor der jeweils
abhängigen Umsetzung nachzuweisen. UXD-09 hat einen fertigen Prüfkatalog, aber
noch keine ausgeführten Produktnachweise. Dies ist keine Umsetzungsfreigabe.

| Paket | Prüffähiges Ergebnis | Abhängigkeiten | Zuordnung |
| --- | --- | --- | --- |
| [UXD-00](00-integration.md) | Aktueller Vertragsabgleich, eindeutige Owner und festgehaltene Layoutentscheidungen | Aktuellen Stand lesen | AC-00, ST-02/03, alle UX-Ziele |
| [UXD-01](01-shell-navigation.md) | Native gemeinsame Shell, getrennte Modi, gleiche Commands, Scroll-/Fokusvertrag | UXD-00; Entwurfslücken als Vorschlag | UX-02, F04/F08/F11 |
| [UXD-02](02-accounts.md) | Accounts mit allen Quotas und stabilem benachbartem Inspector | UXD-00/01 | UX-01, F01–03 |
| [UXD-03](03-connect.md) | Anbieter zuerst; ehrliche Verbindung, Warten und Abschluss | UXD-01/02; echte neue Methoden zusätzlich AC-01/02/09 | AC-02/09, F06/F08 |
| [UXD-04](04-models.md) | Dichtes Modellinventar samt vollständiger Bearbeitung | UXD-01/02; Policyvertrag aus UXD-00 | UX-03, F05 |
| [UXD-05](05-profiles.md) | Kompakte Profile und getrenntes globales Apply/Export/Clientsetup | UXD-01/04; Vorschau/Recovery aus ST-02 | UX-03, F09 |
| [UXD-06](06-usage.md) | Ergebnisse vor Konfiguration, korrekte Teilmessungen/Drilldowns | UXD-01; neue Messwerte aus ST-03 | UX-03, F07 |
| [UXD-07](07-live.md) | Stabile Requestliste und flüchtige Ausgabe mit lokaler Technik | UXD-01; Querverbindungen zu UXD-02/04/06 | UX-03, F10 |
| [UXD-08](08-settings.md) | Drei ruhige Gruppen mit bestehender lokaler Speicherung | UXD-01 | UX-03, F11 |
| [UXD-09](09-acceptance.md) | Integrierte visuelle und funktionale Abnahme samt dokumentierten Lücken | Prüfkatalog ab UXD-00, Ausführung nach betroffenen Seiten | Alle F01–F11, Anschluss an AC-10/ST-05 |

1. UXD-00 klärt den aktuellen Integrationsstand. UXD-09 definiert dazu früh die
   Nachweise, ohne Seitenimplementierung vorwegzunehmen.
2. UXD-01 liefert zuerst die gemeinsame Grundlage; UXD-02 liefert den ersten
   vollständigen Anwendungsfall: unten Z.ai wählen, daneben prüfen, schließen,
   ursprünglichen Scroll-/Fokusanker behalten.
3. Danach Connect; danach Models und Profiles als zusammenhängenden Durchstich.
   Gemeinsame Route-/Policyhelpers zuerst in UXD-04 stabilisieren.
4. Usage und Live, anschließend Settings. Unabhängige Seitendateien können nach
   stabiler Shell getrennt bearbeitet werden; gemeinsame Dateien haben einen Owner.
5. UXD-09 integriert die fehlenden schmalen/Monitor-/Fehler-/Tastaturfälle und
   meldet tatsächliche Ergebnisse. Paketierung/Installation bleiben AC-10/ST-05
   im dort jeweils beauftragten Umfang.

Dies sind logische Verantwortlichkeiten, kein Auftrag zum Start von Agenten oder
eine neue Modellbesetzung. Builds und Runtimeprüfungen bei späterer Umsetzung
mit dem bestehenden Integrator koordinieren.

## Dateieigentum

| Owner | Exklusive Dateien / Grenze |
| --- | --- |
| UXD-00, Integrator | `src/Lain.Client/LainApiClient.cs`, `ObservabilityClient.cs`, gemeinsame Clientverträge und Anschlussänderungen. Core-/Host-/Katalogverträge nur mit bestehenden AC-/ST-Ownern; kein zweites Backend. |
| UXD-01, Shell | `MainWindow.cs`, `MainWindow.Workspace.cs`, `Ui.cs`, `Design/Workspace.axaml`, `Presentation/*`. Gemeinsames Page-Mounting, Navigation, Command-/Fokusgrundlage. |
| UXD-02, Accounts | `MainWindow.Accounts.cs`, `MainWindow.AccountTiles.cs`, `MainWindow.Quota.cs`, `MainWindow.Budgets.cs`, `MainWindow.Overview.cs`, bei Bedarf `MasonryPanel.cs` nach Nutzungsprüfung. |
| UXD-03, Connect | `MainWindow.AccountChooser.cs`, `MainWindow.Login.cs`, `MainWindow.Providers.cs`, `MainWindow.Monitor.cs`, `Providers/ProviderCatalogView*.cs`. `Monitor.cs` ist Account-Monitorzugang, keine Betriebsmodus-Shell. |
| UXD-04, Models | `MainWindow.Routes.cs`, einschließlich bestehender von Profiles benutzter Route-/Policyhelpers. Gemeinsame Extraktion nur über diesen Owner und Shellowner abstimmen. |
| UXD-05, Profiles | `MainWindow.Profiles.cs`; vorhandene Export-/Installationsfunktionen mit ST-02 abstimmen, keine parallele Implementierung. |
| UXD-06, Usage | `Usage/UsageView.cs`, `Usage/UsageView.Analysis.cs`. |
| UXD-07, Live | `Inspector/InspectorView.cs`. Hostcollector/-grenzen bleiben beim Backendowner. |
| UXD-08, Settings | `MainWindow.Settings.cs`; Speicher-/Traygrundlagen über deren bestehenden Owner. |
| UXD-09, Abnahme | `tests/desktop/Program.cs`, `Fixture.cs`, zugehörige Fixtures/README und `tests/presentation/*`; zusätzliche native Prüfungen nach deren bestehendem Testvertrag. Seitenowner liefern Fälle und abgestimmte Teständerungen. |

Desktoppfade in der Tabelle sind relativ zu `src/Lain.Desktop/`. Neue gemeinsame
Dateien werden vor ihrer Anlage einem Owner zugeordnet. Seitenpakete liefern
Änderungswünsche für Shell, Client und zentrale Tests an den jeweiligen Owner.
Das gilt auch für die derzeit in `MainWindow.Workspace.cs` gemeinsam erfassten
Models-/Profileszustände. Vorhandene parallele AC-/ST-Owner haben Vorrang;
UXD-00 vereinbart einen Übergang statt konkurrierender Schreibzuständigkeit.
Planordner und Ergebnisprotokoll sind bei dieser Übergabe unversioniert. Vor
neuen Worktrees/Checkouts gezielt mitgeben; dieser Auftrag erstellt keinen Commit.

## Gemeinsame Fertigkriterien

Ein Seitenpaket liefert native Controls, keine UX-Pilot-HTML-/Tailwindübernahme
und keine neuen UI-Abhängigkeiten. Renderhilfen mit festen 690-/650-Pixelhöhen
sind keine responsiven Produktvorgaben. Bestehende Schriftfamilien und Katze
weiterverwenden. Status wird zusätzlich zu Farbe durch Text/Form erkennbar.

Jede Übergabe benennt betroffenen Commit, Dateien, tatsächlichen APIanschluss,
getestete Zustände, Bildnachweise, Abweichungen und offene Blocker. Auswahl,
Speichern, globales Apply, Export und Clientsetup bleiben eigenständige Aktionen.
Polling erhält ID, Auswahl, Draft, Fokus und benutzten Scrollanker. Sichtbares
⋯, Rechtsklick und Tastatur verwenden denselben aktuellen Commandsatz.

Die Prüfungen in den Einzelpaketen sind **geplant, in diesem Planauftrag nicht
ausgeführt**. Für diese Übergabe wurden Quellen/Abhängigkeiten, Referenzabdeckung,
lokale Dokumentlinks und Whitespace geprüft; keine Produktabnahme behauptet.
Gegenprüfung der ursprünglichen Übergabe: zehn eindeutige Paket-IDs, alle 14 finalen Frame-IDs und F01–F11
zugeordnet, keine Kreise in den harten Paketabhängigkeiten; 98 lokale Links über
13 Dokumente auflösbar. Whitespaceprüfung einschließlich unversionierter
Dokumente ohne Befund. Keine weiteren UX-Pilot-Credits verbraucht.
