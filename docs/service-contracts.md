# Gemeinsame Verträge für Host, UI und CLI

Stand 23.10.2026. Fachliche Invarianten zur [Architektur](architecture.md).
Die implementierten DTOs und der ausführbare Wirevertrag v1 sind unten sowie
in `src/Lain.Core/Contracts.cs` und dem gemeinsamen API-Client festgehalten.

## Identität und Konfiguration

| Begriff | Gemeinsame Bedeutung |
| --- | --- |
| Account | Stabile lain-ID, Anzeigename, Anbieter, Zustand und Credentialreferenzen; zwei Accounts desselben Anbieters bleiben getrennt |
| Credentialzuordnung | Versionierte Verbindung von lain-Account zu beobachteter Core-Credentialidentität; historische Zuordnung bleibt nachvollziehbar |
| Budgetpool | Belegt gemeinsam genutztes Kontingent; mehrere Keys erzeugen nicht automatisch mehrere Budgets |
| Modellroute | Stabile Route-ID zu Upstreammodell, Anbieter-/Endpointkonfiguration und Account oder ausdrücklich beschriebenem Accountpool; unterstützte Fähigkeiten |
| Modellprofil | Name, gewählte Routen/Aliase, Sichtbarkeit und Zielclient; Export verwendet Accountplatzhalter ohne Secrets |
| Clientbindung | Unterstützter Harness mit Version und Profil; Herkunft wird nur bei belastbarer Zuordnung ausgewertet |

Anzeigenamen dürfen wechseln. Entfernte Accounts/Routen verschwinden aus der
aktiven Auswahl; historische Verbrauchsbelege behalten ihre Identität. Ein
Account wird nicht durch dieselbe Credential als angeblich zweiter unabhängiger
Account nachgewiesen. Ein strikt gewählter Account darf nicht unbemerkt durch
Core-Retry zu einem anderen wechseln; Poolauswahl wird ausdrücklich kenntlich.

Konfigurationsänderungen haben eine Revision. Der Host validiert und wendet sie
an; API-Antworten unterscheiden gewünschten und tatsächlich wirksamen Stand.
Fehlgeschlagene Core-Anwendung ist kein erfolgreiches Speichern einer aktiven Route.
Login/OAuth sind eigene abbrechbare Vorgänge mit Zustand; das Geheimnis gehört
nicht in Statusantworten. Entfernung/Aktualisierung müssen laufende Requests und
gespeicherte Historie berücksichtigen.

## Request, Ausführungsversuch und Ereignis

Ein logischer Clientrequest kann mehrere Upstreamversuche verursachen. Die
Summen zählen Nutzerrequests und tatsächlich verbrauchte Versuche getrennt.
Pro Versuch werden reale Modell-/Provider-/Accountidentität und bekannte Route
erfasst. Gewünschtes Modell und tatsächlich verwendetes Modell sind unterscheidbar.

Ereignisse benötigen eine dokumentierte Herkunft, eindeutige Deduplizierung und
eine belastbare Verbindung zwischen Request und Versuch. Paket A entscheidet,
welche Quell-IDs und normalisierten IDs dafür verwendet werden können. Eine
zeitliche Näherung oder ein Modellpräfix ersetzt diese Verbindung nicht.

Nur bereinigte Metadaten werden gespeichert; keine rohen Credentials, Headers
oder vollständigen Request-/Responseinhalte für die Basisstatistik. Wiederholtes
Einlesen desselben Ereignisses zählt nicht erneut. Bereits gespeicherte Daten
überleben Neustarts. Bei nicht rekonstruierbaren Ereignisverlusten bleibt der
Zeitraum als möglicherweise unvollständig markiert; keine Genauigkeitsgarantie
für eine flüchtige Queue erfinden.

## Mengen, Zeiten, Kontingente und Kosten

- Input-/Output-/Cache-/Reasoningwerte behalten die belegte Anbietersemantik;
  Teilmengen werden nicht erneut zum Total addiert. Unbekannte Werte werden als JSON `null`, niemals als gemessene `0` ausgegeben.
- Tagesgrenzen und Zeitraumfilter verwenden eine ausgewiesene Zeitzone. Späte
  Ereignisse korrigieren betroffene Aggregate nachvollziehbar.
- Latenz und Zeit bis zur ersten Ausgabe benötigen passende Zeitstempel. Tokens
  pro Sekunde werden nur aus belegter Tokenzahl und benanntem Zeitintervall
  berechnet; andernfalls bleibt die Kennzahl unbekannt.
- Quota-Snapshots führen Einheit, Account-/Poolscope, Quelle, Beobachtungszeit,
  Reset und Zustand. Veralteter letzter Erfolg bleibt von einer frischen Antwort
  unterscheidbar. Ein Fehler eines Accounts verdeckt keine anderen Accounts.
- `windowLabel` benennt das belegte Budgetfenster, `usedPercent` hält einen
  ausdrücklich gemeldeten Verbrauchsprozentsatz unabhängig von Mengenwerten.
  Fehlende Mengen werden nicht aus Prozentwerten als Tokens/Credits erfunden;
  Prozentpunkte dürfen mit Einheit `percent` auf 100 normalisiert werden.
- `unsupported` bedeutet, dass kein geprüfter Abruf für den Anbieter vorhanden
  ist; es ist kein Verbindungsfehler. `error` und `stale` bleiben tatsächlichen
  Abruffehlern beziehungsweise überalterten Beobachtungen vorbehalten.
- Fenster werden nicht addiert. Eine Übersicht darf das knappste bekannte
  Fenster hervorheben, muss dabei das Fenster und verbleibende Budgets nennen.
- Aboentgelt, enthaltene Nutzung, gekaufte API-Credits, geschätzte Tokenkosten
  und tatsächliche Abrechnung sind verschiedene Größen. Gemeinsame Budgets
  werden in Gesamtansichten nur einmal berücksichtigt.

## Gemeinsame Anwendungsfälle

| Bereich | Benötigte Operationen im ersten Stand |
| --- | --- |
| Dienst/Proxy | Status, Version, Bereitschaft, gewünschter/wirksamer Konfigurationsstand; kontrollierter Start/Stop/Neustart des eigenen Proxys |
| Accounts | Auflisten, hinzufügen, Login beginnen/Status/Abbruch, Verbindung prüfen, aktivieren/deaktivieren, entfernen |
| Modelle/Profile | Verfügbare Modelle lesen, konkrete Routen auswählen, Sichtbarkeit/Aliase setzen, benanntes Profil speichern und für den Referenzclient anwenden/exportieren |
| Usage | Zeitraumübersicht, Tages-/Modell-/Accountaufteilung, Request-/Fehleransicht mit Filtern; nur belegte Harness-/Profildimensionen |
| Quota | Snapshots lesen, Aktualisierung anfordern und laufenden/fehlgeschlagenen Abruf erkennen |

Antworten enthalten Vertragsversion, Snapshot-/Revisionsbezug, Erfassungsstand und
Abdeckungsangaben. GUI und CLI liefern für denselben Datenstand dieselben Zahlen.
Die UI darf formatieren, aber keine abweichende Fachaggregation implementieren.
Listen und Zeiträume sind begrenzt bzw. paginiert; eine Anfrage lädt nicht
unbeschränkt die gesamte Historie in einen Client.

CLI-Befehle bieten JSON auf stdout, Diagnose auf stderr und unterscheidbare
Fehlerzustände. Ein Skill nutzt dokumentierte Befehle, kein TUI-Screenscraping.
Hostausfall liefert eine verständliche Meldung, keinen stillen Ersatzcollector
und keine direkt geöffnete Datenbank. Änderungen gehen denselben Prüf-/Anwendungsweg
wie in Avalonia; Zugriff auf die CLI erweitert nicht die Autorisierung eines Skills.

## Vertragsabnahme vor paralleler Umsetzung

Paket A liefert mindestens bereinigte Beispiele für Erfolg, 429/Retry, Abbruch,
zwei Accounts, Replay und eine bekannte Abdeckungslücke. Danach werden API-/JSON-
Beispiele und Felddefinitionen so festgehalten, dass Avalonia und CLI unabhängig
gegen denselben Vertrag arbeiten können. Das Schema des Proxy-Cores bleibt
hinter dessen Adapter; seine Version ist nicht die öffentliche lain-API-Version.

## Implementierter Wirevertrag v1

`src/Lain.Core/Contracts.cs` enthält die gemeinsamen DTOs;
`src/Lain.Client/LainApiClient.cs` ist der typisierte Referenzclient.
JSON verwendet camelCase und explizites null für unbekannte Messwerte.
Die API bindet ausschließlich 127.0.0.1:18418 und verlangt ein eigenes
Host-Bearertoken aus der privaten lain-Ablage. Inferenz- und Managementtoken
sind getrennt. Alle Mutationen werden im Host serialisiert; überholte
Revisionen liefern 409. Konfiguration beschreibt gewünschten und angewendeten
Stand getrennt. Ein Profil wird erst nach erfolgreichem Start als angewendet
markiert. Ungültige Runtimegenerationen dürfen nicht neu gestartet werden.

Usage: GET `/api/v1/usage` und `/api/v1/attempts`, Parameter `from`, `to`,
`timeZone`, `accountId`, `model`, `statusCode`, `offset`, `limit`. Intervalle
sind höchstens 366 Tage, Requestseiten höchstens 500 Einträge. Die Endpunkte
liefern dieselben Aggregate für GUI, CLI und Skill.

Lokale Credentials und Anbieterfehler werden nicht als öffentliche API-Daten
ausgegeben. `/api/v1/auth/token?audience=codex` liefert nach Hostauthentifizierung
ausschließlich den lokalen Inferenzschlüssel.
