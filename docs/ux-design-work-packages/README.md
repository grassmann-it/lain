# Arbeitspakete für die geprüften UX-Pilot-Entwürfe

Stand: 24.10.2026. Auftrag: aus den bewerteten Entwürfen konkrete Arbeitspakete
machen. **Planung abgeschlossen bedeutet hier weder Designabnahme noch Beginn
der Produktimplementierung.** In diesem Auftrag keine Produktcodeänderungen,
Builds, Installation, Dienstneustarts oder echten Provideraktionen.

## Ziel und Grundlage

Accounts werden zum stabilen Einstieg: alle gemeldeten Kontingente vergleichen,
einen Account öffnen und direkt daneben handeln, ohne andere Kacheln zu
verschieben. Anschließend werden Connect, Models/Profiles, Usage/Live und
Settings mit derselben Informationshierarchie und Bedienung überarbeitet.

Maßgebliche Referenzen:

- [Geprüfte Entwürfe, alle 14 Frame-IDs und direkt erreichbare PNGs](../uxpilot-design-results-2026-10-24.md).
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

Die positive Rückmeldung „sieht eigentlich gut aus“ und der Paketauftrag geben
die Ausarbeitung frei. Folgende **Vorschläge** bleiben zur konkreten Abnahme
sichtbar; sie werden durch diesen Plan nicht zu bestätigten Produktentscheidungen:

| Punkt | Planungsannahme und Nutzen | Preis / Alternative | Zuständig |
| --- | --- | --- | --- |
| Breite Accounts | Dauerhaft reservierter Inspector rechts mit 400 px bei 1120×780; links eine Inventarspalte | Weniger Accounts gleichzeitig, freie rechte Spalte vor Auswahl. Overlay ist nur bei neuem Auftrag zur Revision zu prüfen, da es Inhalte verdecken kann. | UXD-00/02 |
| Schmale Details | Bei 800×560 eigene Detailfläche mit Back statt zwei zu enger Spalten; Ursprung samt Scroll/Fokus erhalten | Kein gleichzeitiger Überblick. Noch kein geprüfter neuer Designframe; Grenzbreite nach Messung festlegen. | UXD-01/02, Prüfung UXD-09 |
| Navigation | Simple: Accounts, Models, Profiles, Usage, More mit Live; Advanced: Live direkt; Barebones: Accounts, Profiles, Usage direkt | Live benötigt in Simple einen zusätzlichen Schritt. Bei täglicher Live-Nutzung fünf direkte Ziele als Alternative vorhalten. | UXD-00/01 |
| Models | Tabelle mit lokaler Auswahl/Detail statt hoher Karten | Lange Namen und vollständige Editoren brauchen schmale Abnahme. | UXD-04 |
| Übrige Seiten | Verdichtetes Profiles-Master/Detail, sofort sichtbare Usagezeilen, Live-Liste neben Ausgabe, drei Settingsgruppen | Schmal, Monitor, Offline und vollständige Expertbedienung sind visuell noch unqualifiziert. | UXD-05–09 |

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

Schon vorhanden sind Präferenzspeicherung, Draft-/Navigationszustand,
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

UXD konkretisiert die bestehenden UX-01–03 aus dem
[Ausführungsplan](../work-packages-execution-2026-10-24.md). AC-00–10 und ST-01–05
behalten ihre fachlichen Zuständigkeiten; dieser Plan startet sie nicht neu.

Übergabereife: UXD-00 ist als lesender Vorlauf konkret beschrieben. Die
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
Gegenprüfung: zehn eindeutige Paket-IDs, alle 14 finalen Frame-IDs und F01–F11
zugeordnet, keine Kreise in den harten Paketabhängigkeiten; 98 lokale Links über
13 Dokumente auflösbar. Whitespaceprüfung einschließlich unversionierter
Dokumente ohne Befund. Keine weiteren UX-Pilot-Credits verbraucht.
