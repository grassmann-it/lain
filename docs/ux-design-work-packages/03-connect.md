# UXD-03 – Anbieter zuerst verbinden

Ziel: Anbieter wählen, passenden belegten Zugang verbinden, Kontingente sehen,
danach optional Modelle einrichten. Abhängig von UXD-01/02 und den tatsächlichen
Anschlüssen aus UXD-00; neue Fähigkeiten bei AC-01/02/09. F06/F08.
[Übersicht](README.md).

## Referenz, Befund und Dateien

Finale Frames 4–8: `gzASp44CCY795jSZqF7i`, `VqOo9QWKoHTuPl2QFBA3`,
`k0kGywZOHwQqiJP6M9dc`, `wQVdJZc3s1aOOHiSyLRP`, `XRRl0bWu3u4yabFGoOVB`;
[PNG-Links](../uxpilot-design-results-2026-10-24.md).

Der heutige Chooser beginnt mit Methodenkategorien. Login-Polling/Cancel,
connection-first Keyform, optionale Modelle und Providerkatalog existieren.
Owner: `MainWindow.AccountChooser.cs`, `Login.cs`, `Providers.cs`, `Monitor.cs`
und `Providers/ProviderCatalogView*.cs`. `Monitor.cs` bearbeitet den
Monitoringzugang eines Accounts; die Betriebsmodus-Shell gehört UXD-01.

## Aufgaben

1. Einen Anbieterbestand für Discover und Add account verwenden, mit
   unterschiedlicher Absicht. Nach Anbieterwahl die einfachste aktuell
   verfügbare Methode zeigen; Alternativen lokal. Keine erneute Pflichtwahl
   zwischen internen Zugangskategorien vor der Anbieterwahl.
2. Methodenkatalog/Fähigkeiten aus UXD-00 anbinden. Unqualifizierte Methode
   erklärt ihre Voraussetzung und vorhandenen Fallback. Sichtbare Anbieterlogos
   oder Logintexte sind kein Implementierungsbeleg. Coreabhängigen Codexweg
   im Monitor nicht als funktionsfähig darstellen.
3. Codexfolge trennen: Start mit „Continue in browser“, dann Warten mit
   „Reopen browser“, Cancel, gegebenenfalls Ablauf/Fehler, danach konkreter
   Accountabschluss. Erneutes Browseröffnen erzeugt keine zweite Operation.
   Vorhandene Operation und Drafts über UI-Präsentationswechsel erhalten.
4. Keyeingabe maskieren. Nur echte Pflichtfelder der Methode verlangen; Kilo-
   Ziel ist Name plus Key ohne Pflichtplan, Route oder Modell. Das wird erst
   produktiv angeboten, wenn Accountbasis und Reader tatsächlich angeschlossen
   sind; bis dahin ehrlicher bestehender Weg/benannter Blocker.
5. Abschluss benennt nur nachgewiesene Ergebnisse: Credential verbunden,
   Monitoring verfügbar, falls geliefert. Done primär, Choose models optional.
   Weder Verbindung noch Quota-Read ist eine erfolgreiche Inferenzprobe.
6. Reconnect/Monitoringergänzung an vorhandene Account-ID binden; keine
   unbemerkte Accountduplikation. Berechtigungen, Loginowner, Callback und
   Secretpersistenz aus AC übernehmen. UI erzeugt keine eigene Refreshverwaltung.
7. Fehler lokal und konkret: falsche Eingabe, fehlende Voraussetzung, Cancel,
   Ablauf, Methodenunsupported, Host offline, Revisionskonflikt. Keine pauschalen
   Reconnectwarnungen für fehlende Quotaquellen. Neue Form beginnt oben,
   Back erhält Anbieterkontext und ungefährliche Draftwerte.

## Abnahme

- Alle fünf finalen Referenzzustände sind mit synthetischen Antworten
  nachvollziehbar; bei 1120×780 und 800×560 sind Pflichtfelder, Primäraktion,
  Cancel/Back und eventuelle Fehler ohne Überlappung erreichbar.
- Anbieter → Methode → Account funktioniert ohne vorgelagerte Modellpflicht.
  Skip/Done erzeugt keine Route; optionale Modellwahl erfolgt ausdrücklich.
- Start, erneutes Browseröffnen, Polling, Cancel, expiry, completed und verspätete
  Antwort nach Cancel erzeugen keinen falschen Erfolg oder doppelten Account.
- Monitor ohne Core bietet nur tatsächlich geeignete Wege an. Ein bekannter
  Gatewayfehler wird nicht als neue Betriebswahl behandelt.
- Key/Token erscheinen weder in UI-Screenshot, Log noch Clipboardevidence.
  Credentialprüfung und Quotaergebnis werden nicht als „Full read access“ oder
  globale Anbieterfähigkeit umgedeutet.
- Gespeicherter Account erscheint mit derselben ID im Accounts-Inventar;
  kontextueller Übergang zu Models und Back bewahrt den ursprünglichen Kontext.
- Außer den ausdrücklich angeforderten Verbindungsaktionen entsteht keine
  Discovery, Inferenz, Dateiübernahme oder kostenpflichtige Nebenwirkung.

## Grenzen und Übergabe

Keine neuen OAuth-/Browserbrücken, Anbieterreader, `.env`-Imports oder pauschalen
CLI-Caches implementieren. Qualifikation je Anbieter bleibt bei
[AC-Paketen](../account-connect-work-packages/README.md).

Liefern: Zustandsdiagramm oder Zustandsliste mit aktuellen APIs, native Screens,
Lifecycle-/Fehlernachweise und Tabelle „lokal ausführbar / Voraussetzung fehlt“
je berührter Methode. Reale Loginabnahmen bleiben separat ausgewiesen.
