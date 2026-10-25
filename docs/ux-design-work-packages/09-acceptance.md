# UXD-09 – Integrierte Abnahme und Schließen der Designlücken

Ziel: Der neue Zuschnitt besteht Aufgaben, Zustandswechsel und echte native
Bedienung. Prüfkatalog früh ab UXD-00 vorbereiten, Ausführung nach Integration
der jeweiligen Seiten. **Alle folgenden Produktprüfungen sind geplant; in diesem
Planauftrag wurden sie nicht ausgeführt.** [Übersicht](README.md).

## Verantwortlichkeit und vorhandene Mittel

Testowner für `tests/desktop/Program.cs`, `Fixture.cs`, zusätzliche Fixtures,
README und `tests/presentation/*`. Seitenowner liefern Szenarien; Änderungen
an zentralen Tests werden hier integriert. Produktfixes gehen an den jeweiligen
Dateiowner. Bestehende AC-10-/ST-05-Abnahme nicht duplizieren.

Der [Desktop-Harness](../../tests/desktop/README.md) rendert Produktionsklassen
und Produktionstheme mit allowlisted InMemory-HTTP, isoliertem HOME/XDG/LAIN_HOME
und privatem Xvfb. Vorhanden sind fünf Accounts plus 35-Accountstress,
45 Routen, drei Profile, Polling-, Draft-, Quota- und Sourcefehlerfälle.
Bestehende Strukturprüfung „immer genau fünf Hauptziele“ gezielt durch die
gewählte Navigations-/Fähigkeitsmatrix ersetzen, nicht ersatzlos entfernen.

## Referenz- und Befundabdeckung

Alle finalen Frame-IDs und echte PNG-URLs stehen im
[Ergebnisprotokoll](../uxpilot-design-results-2026-10-24.md). Nur finale Gruppe
verwenden; verworfene Generatorstände sind keine Abnahmevorlage.

| Frames | Nachweis im Produkt | Paket / Review |
| --- | --- | --- |
| 1–3 Accounts | Alle Fenster; gleicher Inventaranker vor/nach Detail; sinnvolle Warnscopes | UXD-02, F01–03 |
| 4–8 Connect | Anbieter, Start, Warten, Keyziel soweit echt verfügbar, Abschluss | UXD-03, F06/F08 |
| 9 Models | 45er-Inventar, Status/Hidden/Disabled, sofortiges Edit, volle Policies | UXD-04, F05 |
| 10–11 Profiles | Viewing/Applied, globale aktuelle Vorschau und getrennte Export-/Setupaktionen | UXD-05, F09 |
| 12 Usage | Ergebnisbeginn sichtbar, korrektes Maß und Coverage, lokale Details | UXD-06, F07 |
| 13 Live | Liste/Output, belegte Gruppen, Retention-/Pause-/Clearsemantik | UXD-07, F10 |
| 14 Settings | Drei Gruppen, echter Scope/Trayzustand, lokale Speicherung | UXD-08, F11 |
| Alle | Getrennte Modi, keine Navigationduplikate, Scroll/Fokus, gemeinsame Typohierarchie | UXD-01, F04/F08/F11 |

## Noch fehlende Designqualifikation

Die ursprüngliche breite Gatewayserie deckt folgende Fälle nicht vollständig ab.
Die [Folgeprüfung und bestätigte Auswahl A/B/A](../uxpilot-selection-2026-10-24.md)
liefert inzwischen ergänzende Designframes. Die native Qualifikation bleibt
verbindliche Aufgabe dieses Pakets mit den jeweiligen Seitenownern:

1. **800×560:** bestätigtes Accounts-A mit Liste und Detail nebeneinander;
   Models mit langen Aliasnamen; Profiles mit fester Apply-Reviewfläche im Detail
   (Folgeauswahl B); Usagefilter/Ergebnis; Live mit dauerhafter linker Requestliste
   neben der Ausgabe (A); Settings. Native Screens prüfen und sichtbare
   Abweichungen mit Grund festhalten. Breakpoints an realem Inhalt messen.
2. **Monitor/Barebones:** eigenständiger Monitor ohne Core, mit/ohne echte lokale
   History; Gateway in Barebones mit direktem Profileszugriff. Advanced schaltet
   keine Fähigkeit frei. Fenster springt beim Moduswechsel nicht auf eine andere Größe.
   Gewählte Monitortabelle B mit langen Verbrauchsbalken unter den Zahlen (A)
   einschließlich oberer/unterer Scrollposition und vollständiger Quotas prüfen.
   Unter 800×560 liegende Mindestgrößen sind ein gesonderter Vorschlag.
3. **Offline/Fehler:** bewusst gewählter Monitor vs. Gateway ohne Setup vs.
   gestörtes eingerichtetes Gateway; nicht erreichbarer Host mit letztem Stand;
   fehlende/stale Quotaquelle, Methodenunsupported, Revisionkonflikt, Applyfehler,
   partielle Usage und Live-Sourcewechsel/Retentionlücke.
4. **Expertbedienung:** vollständige Accountzugänge, Quellen/Owner, Modell-/
   Profilpolicies, Fallbacks, Attribution/Tokenkomponenten und Livedetails.
   Jedes bestehende Feld einem neuen erreichbaren Ort zuordnen; nichts still verlieren.
5. **Physische Eingabe:** kompletter Tastaturweg und nativer Pointer-/Menüpfad,
   lesbarer Fokus, Dialogfokus, Escape/Back sowie Rückgabe zum Auslöser. Bestehende
   routed Clicks allein belegen weder Hit-Testing noch vollständige Tastaturbedienung.

Wenn eine dieser Prüfungen die reservierte Breite oder Navigationannahme
widerlegt, betroffene Entscheidung in UXD-00 neu bewerten. Unabhängige Seiten
weiterführen; keine Bildabweichung als technische Notwendigkeit verstecken.

## Ausführung und Nachweise bei späterem Umsetzungsauftrag

Zuerst vereinbarten Referenzcommit und synthetische Fixtures sichern. Laufende
Build-/Runtimearbeit zentral koordinieren. Geeignete vorhandene Einstiegspunkte:

```sh
./tests/desktop/run-private-xvfb.sh /tmp/lain-uxd-evidence
dotnet run --project tests/presentation/Presentation.Tests.csproj
```

Bei nachgewiesen vorhandenem passendem Build unterstützt der Desktoprunner
`LAIN_DESKTOP_SKIP_BUILD=1`. Er ersetzt keine Aktualitätsprüfung des Assemblies.
Nur bei tatsächlichen Änderungen am Tray-/Fensterverhalten zusätzlich den
[Tray-Harness](../../tests/desktop-tray/README.md) einsetzen. Für native
Eingabe-/Signalnachweise vorhandene Infrastruktur unter
[desktop-native](../../tests/desktop-native/README.md) und
[desktop-signals](../../tests/desktop-signals/README.md) prüfen und gezielt ergänzen.
Kein Benutzerdisplay/-bus, keine echte Credentialdiscovery und keine echten
Provideraufrufe als versteckte Testvoraussetzung. Fehlende Werkzeuge in der
Ausführungsübergabe benennen; dieser Plan installiert sie nicht.

Breite Seiten in Simple/Normal gegen die 14 Referenzen prüfen. Für alle sechs
Hauptseiten beide Größen mit Barebones/Simple/Advanced und Normal/Compact
prüfen, soweit die jeweilige Fähigkeit verfügbar ist. Dazu gezielte Monitor-,
Offline-, Draft-/Fokus- und Konfliktfälle aus den Einzelpaketen. Das ist eine
Abdeckungsmatrix, keine Forderung nach redundanten Tests jeder internen Methode.

Pro Fall Produktionsassembly/Commit, Viewport, Betriebsabsicht, Informationsstufe,
Dichte, Fixture, Aktion, erwartetes und tatsächliches Ergebnis protokollieren.
PNGs unabhängig sichten; automatisches Bestehen beweist keine gute Hierarchie.
Accounts liefern Geometrien vor/nach Auswahl bei identischen Daten und gleichem
Listenoffset; Änderungen nur an Auswahl und Inspector, keine Nachbarverschiebung.

## Abschließende Abnahme

- F01–F11 und alle 14 finalen Frames haben belegte Zuordnung zu Test-/Bildnachweis
  oder eine konkret benannte offene Abweichung. Neue schmale/Monitor-/Fehlerbilder
  sind gesondert als Produktnachweise erkennbar.
- Katze, Theme, Mint, native Controls, ein Dropdown unten rechts, direkter
  Profileszugriff im Gateway und Accountsstart sind durchgehend erhalten.
- Kein Quota-/Fähigkeits-/Kostenwert erfunden. Unbekannt, gemessener Nullwert, Teilsumme,
  Coverage, unbestätigter Reset und gemeinsame Budgets bleiben korrekt.
- Auswahl/Speichern/Apply/Export/Setup, Enabled/Hidden sowie Monitor/Darstellung/
  Dichte sind in Bedienung und HTTPnachweisen getrennt.
- Polling und Navigation verlieren keine zugesagten Drafts, IDs, Anker oder
  Eingabefokus. Keine ausschließlich per Rechtsklick erreichbare Funktion.
- Tastatur-/Pointerprüfung ist mit tatsächlicher Methode dokumentiert. Falls
  vollständige Accessibility-/Screenreaderprüfung fehlt, bleibt sie ausdrücklich
  unqualifiziert; kein pauschales Accessibility-Label vergeben.
- Ausstehende Anbieter-/APIabhängigkeit bleibt als begrenzter Blocker sichtbar.
  Ein Fixtureerfolg wird nicht als echte Login-, Inferenz- oder Installationsabnahme
  ausgegeben. Kein Gesamtpaket als abgeschlossen markieren, solange Pflichtfälle fehlen.

Liefern: lesbarer Abnahmebericht mit Ergebnisübersicht, Evidenzmanifest, sicheren
Bildlinks und offenen Abweichungen. Installation/Paketierung erst im gesondert
beauftragten AC-10-/ST-05-Lieferumfang; dieses Paket autorisiert keinen Dienstwechsel.
