# UXD-01 – Shell, Navigation und gemeinsame Bedienung

Ziel: Eine native Grundlage, auf der die Seiten ohne konkurrierende Scroll-
oder Zustandsmodelle aufgebaut werden. Abhängig von UXD-00; verantwortlich für
F04, F08 und den gemeinsamen Teil von F11. [Übersicht und Owner](README.md).

## Befund und Dateien

`MainWindow.cs` enthält Header, globales Busy-/Refreshhandling, einen äußeren
Workspace-Scrollviewer, fünf Navbuttons und den bereits einzigen Modusdropdown.
`MainWindow.Workspace.cs` bewahrt Preferences, Drafts, Navigation und Scrollstand.
Usage und Live werden beim Präsentationswechsel bereits in place angepasst;
Live-Detach verwirft den Buffer. Diese Eigenschaften erhalten.

Exklusiv: diese beiden Dateien, `Ui.cs`, `Design/Workspace.axaml`,
`Presentation/*`. Originalkatze/Branding und Fontassets weiterverwenden.

## Aufgaben

1. Header, dünne Statuszeile und Footer konsistent verdichten. Accounts starten
   zuerst; Settings, Back, Servicezugang und Hostzustand bleiben verständlich.
   Dynamische Inhalte dürfen den Footer nicht aus dem Fenster schieben.
2. Navigation gemäß ausdrücklich noch vorläufiger Matrix aufbauen:

   | Betrieb / Stufe | Direkt | Zusätzlich erreichbar |
   | --- | --- | --- |
   | Gateway Barebones | Accounts, Profiles, Usage | Models und Live über More |
   | Gateway Simple | Accounts, Models, Profiles, Usage | Live über More |
   | Gateway Advanced | Accounts, Models, Profiles, Usage, Live | Service im zugehörigen Menü |
   | Bewusster Monitor | Accounts; Usage bei echter Historienquelle | Settings, Hilfe, bewusster Gatewayeinrichtungseinstieg |

   Profiles im Gateway immer direkt. Jedes Ziel erscheint genau einmal.
   Beim Stufenwechsel ein offenes Ziel erhalten, selbst wenn sein Navigationseintrag
   nach More wandert. Alternative fünf direkte Simpleziele aus UXD-00 berücksichtigen.
3. Genau einen Barebones/Simple/Advanced-Dropdown unten rechts behalten.
   Monitor/Gateway nicht dort hineinmischen; Betriebswechsel ist eine bewusste
   fachliche Aktion, getrennt von Density und Darstellungsstufe.
   Advanced erzeugt keine fehlende Fähigkeit. Offline/gestörtes Gateway behält
   bekannte Ziele und letzten Stand mit Kennzeichnung.
4. Seitenvertrag aus UXD-00 umsetzen: Listen und Inspector erhalten eigene
   begrenzte Scrollflächen. Äußeren Scrollviewer je Seitenart anpassen; keine
   zwei konkurrierenden vertikalen Scrollflächen für denselben Hauptinhalt.
   Neue Editoren starten oben; Back stellt den Ausgangsanker wieder her.
5. Wiederverwendbares natives Auswahl-/Detailmuster und Commands bereitstellen:
   sichtbares ⋯, Rechtsklick und Tastatur öffnen denselben Satz samt Begründung
   nicht verfügbarer Aktionen. Objekt-ID bei Öffnung binden, vor Ausführung
   gegen aktuelle Daten/Revision prüfen. Escape schließt die oberste lokale
   Ebene, danach Fokus zum Trigger; gelöschte Ziele erhalten einen sicheren Ersatz.
6. Auswahl, Draft, lokale Aufklappungen und Scroll/Fokus über Polling,
   Modus-/Dichtewechsel und Settings/Back erhalten. Live/Usage dafür nicht neu
   instanziieren. Models-/Profiles-Capture bleibt hier koordiniert.
7. Typohierarchie vereinheitlichen: Inter für Werte/Controls, vorhandene Heading-
   und Brandfamilien in ihrer Rolle. Keine kleineren unlesbaren Werte als
   Ersatz für Hierarchie. Fokus und Status nicht ausschließlich farblich zeigen.
8. Layoutverhalten für 1120×780 und 800×560 bereitstellen. Schmaler Detailwechsel
   erhält den Hauptkontext und sichtbares Back; keine automatische Fensteränderung.
   Eine kleinere Monitor-Mindestgröße bleibt ein gesonderter Vorschlag.

## Abnahme

- In beiden Größen bleibt der gesamte Footer sichtbar, genau ein Modusdropdown
  unten rechts; Katze unverändert, keine überlappenden Controls.
- Navigation entspricht der gewählten Matrix, Accounts starten zuerst, Profiles
  sind im Gateway in jeder Stufe direkt. Wechsel der Stufe schreibt keine
  Account-/Route-/Profilkonfiguration und startet keinen Core.
- Offline ist kein Monitorwechsel. Monitor zeigt keine ausführbaren Proxy-/
  Live-/Profilaktionen ohne entsprechende Fähigkeit; vorhandene Historie bleibt lesbar.
- Folge „lange Seite unten → neuer Editor“ beginnt am Editoranfang. Folge
  „Inventar unten → Detail → Settings → Back → Close/Back“ erhält Objekt,
  Filter, Draft, Scroll und sinnvollen Fokus.
- Bei Polling während offenem Menü bleibt die Aktion an derselben ID. Ein
  inzwischen entferntes Objekt wird nicht durch die nächste Zeile ersetzt.
- Tastatur erreicht Navigation, Liste, Detail, Menü und Rückweg; Fokus ist
  sichtbar, Escape arbeitet lokal, ein Dialog hält Fokus und gibt ihn zurück.
- Ein Präsentationswechsel in Live leert keine Streams; Usagefilterdraft und
  angewendete Query bleiben unterscheidbar.

## Nichtziele, Spielraum und Übergabe

Keine neue Themebibliothek, WebView, HTMLübernahme oder Fontinstallation.
Breakpoints und konkrete Controlklassen sind nach Messung delegierbar. Ob die
reservierte Accountsfläche und Simple-Live-Navigation übernommen werden, bleibt
die in UXD-00 benannte Layoutentscheidung.

Liefern: gemeinsamen Seiten-/Commandvertrag, Ownerhinweise für neue Dateien,
Screens beider Größen und Szenarien für UXD-09. Die zentrale Testrevision von
„immer genau fünf Hauptziele“ erfolgt gezielt mit dem Testowner; die gewünschte
Navigations-/Fähigkeitsprüfung bleibt bestehen.
