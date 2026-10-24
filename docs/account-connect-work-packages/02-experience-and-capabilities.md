# AC-02 / AC-09 — Account Connect in Desktop, CLI und Katalog

Planstand: 2026-10-24, Codebasis `5dc916fcea702f2a196520903937570745e26dac`.
Dies ist eine Übergabe zur späteren Umsetzung, kein Implementierungs- oder Loginauftrag.
Fachliche Grundlage: [Account-Connect-Entwurf](../account-connect-redesign-2026-10-24.md) und dessen acht Anbieterberichte; der Integrator übernimmt diese gemeinsamen Quellen zusammen mit diesem Paket.
Noch offene Hersteller-/Clientqualifikation blockiert jeweils die betreffende Methode, nicht den vorhandenen Keyzugang oder den gesamten Anbieter.

## Ziel und Grenzen

- AC-02 liefert denselben verständlichen Anschluss in Desktop und CLI: **Anbieter → empfohlene Methode → Ressource nur falls nötig → Ergebnis-/Quotakachel → optional Modelle**.
- Ein Monitoraccount braucht weder Core noch Dummykey, Tariftemplate oder Modellroute. Derselbe Account kann später Inferenz erhalten; Reconnect erhält ID, History und Ressourcenbindung.
- Eine erfolgreiche lokale Speicherung, Anmeldung, Modellliste und echte Quotaablesung sind unterschiedliche Ergebnisse. Inferenz wird dadurch weder bewiesen noch kostenpflichtig getestet.
- AC-09 beschreibt alle 25 Anbieter und 35 Produktvarianten; pro Methode werden implementierte Fähigkeiten, dokumentiertes Ziel und offene Qualifikation getrennt ausgewiesen.
- Außerhalb: Provideradapter, Credentialowner, OAuthregistrierung, Remote-Keyerzeugung, Vertragsänderungen und Service-/Datenmigrationen. Kein Connect aktiviert bezahlte Modelle, wendet Profile an oder verändert laufende Requests.

## Ausgangslage und exklusive Dateigrenzen

- `MainWindow.AccountChooser.cs` und `MainWindow.Login.cs` bieten bislang speziell Codex; die vorhandene Loginoperation wird verallgemeinert, kein zweiter Wizard eingeführt.
- `MainWindow.Providers.cs` und `AccountWizard.cs` verlangen heute Connection, Tarif und Key vor optionalen Modellen. Leere `SelectedModelRouteIds` verhindert zwar Routenanlage, löst aber die Account-/Template-/Tarifkopplung im Host nicht.
- `MainWindow.Monitor.cs` / `MonitorWizard.cs` verlangen noch manuelle Ressourcen-/Tokenangaben. Bestehende externe Snapshots bleiben ohne Refresh; eigene Sessions brauchen den von AC-01 definierten Owner.
- AC-02 besitzt Desktop-Anschlussdateien, `MainWindow.Accounts.cs`, `MainWindow.Settings.cs`, `MainWindow.Overview.cs`, `MainWindow.AccountTiles.cs`, `MainWindow.Quota.cs`, `MainWindow.Budgets.cs` und `Providers/ProviderCatalogView*.cs`, soweit dieses Paket sie berührt.
- AC-02 besitzt `src/Lain.Cli/Program.cs` samt Commandregistrierung, CLI `AccountWizard*.cs`, `MonitorWizard.cs`, zugehörige Darstellung sowie die Transportwrapper in `src/Lain.Client/LainApiClient.cs` nach Contract-Freeze. Eventuelle neue Desktop-/CLI-Präsentationshelfer gehören ebenfalls ausschließlich AC-02.
- AC-09 ist **einziger Integrationsowner** von `src/Lain.Host/Providers/Catalog/*.json`, einschließlich neuer MiMo-/Referenzdeskriptoren, soweit AC-00 diese Darstellung festlegt. AC-03…08 liefern Datenvorschläge und Belege, keine konkurrierenden Katalogedits.
- AC-00 besitzt gemeinsame DTOs/Contracts, `ProviderCatalogRegistry.cs`, `src/Lain.Host/Program.cs`, formale Validierung, Migrationen und Buildintegration. AC-01 besitzt Accountbasis, Credentialresolver, Loginoperationen und Monitor-Lifecycle.
- AC-02 besitzt passende CLI-/Desktop-Präsentationstests; AC-09 besitzt Katalogabdeckungstests. Bestehende fremde Testdateien nur nach klarer Ownerübergabe ändern; AC-10 führt die integrierte Abnahme.

## AC-02 — Schritte und sichtbares Verhalten

1. Mit AC-00/01 den geheimnisfreien Contract einfrieren: stabile Provider-/Produkt-/Methoden-ID, Zweck, Fähigkeiten, Reife, Ressourcenbedarf, Zielaccount, Revision, Operation/Phase, Nutzeraktion und getrenntes Quotaresultat.
2. Anbieterwahl zeigt eine tatsächlich verfügbare empfohlene Methode und deren Fähigkeiten. Weitere Methoden sind erreichbar; ein bloß dokumentierter Browserweg wird nicht als fertiger Login angeboten. Headless/Automation behält privaten Keyzugang.
3. Methodenstart bindet Zweck und vorhandenen Account. Neue Ressourcen erst entdecken, bei genau einer zulässigen Ressource automatisch binden, sonst einmal auswählen. Reconnect darf nicht unbemerkt Org/Projekt/Workspace/Region wechseln.
4. Browser-/Deviceablauf verwendet dieselbe Hostoperation mit Status, Fortsetzen und Abbrechen. Navigation zurück lässt laufende Anmeldung erkennbar; expliziter Abbruch beendet sie. Keine Login-Endlosschleife und keine automatische Ausweitung der Rechte nach Verweigerung.
5. Normale Nutzer sehen keine Client-ID-/Client-Secret-Registrierungsformulare. Fehlende Maintainerregistrierung/Callback-/Deploymentqualifikation erscheint als Methodenblocker mit passendem vorhandenen Keyfallback. Geheimwerte werden verdeckt eingegeben, nie geloggt und nach Übergabe geleert.
6. Bereits vorhandene passende Credentials werden wiederverwendet; derselbe Key für Inferenz und Quota wird einmal erfragt. Zusätzliche Leserechte ergänzen den bestehenden Account nur bei tatsächlichem Bedarf und mit erklärtem Zweck.
7. Delegierte Hersteller-CLI arbeitet in privatem, isoliertem Profil/Configpfad. UI benennt fehlende CLI/ungeeignete Version und sicheren Fallback; kein Lesen/Ändern beliebiger Fremdprofile, kein pauschaler Authcacheimport und keine Tokenkopie als behaupteter Refreshowner.
8. Ergebnis zeigt Accountkachel zuerst: bestätigte Ressource, verbundene Fähigkeiten, konkreter Quotastatus und nächste sinnvolle Aktion. Ein zulässiger Read nach Connect ist von bloßer lokaler Speicherung getrennt; keine versteckte Inferenz zur „Prüfung“.
9. Modelle/Tarife in den Routingbereich verschieben und dort vollständig erhalten: bisherige Auswahl, Plan-/Modellbeschreibungen, Free-only-Regeln, explizite Nutzerentscheidung. Gemeldeter Tarif, Nutzerangabe und Routenvoraussetzung behalten ihre Herkunft; Login beweist keinen Paid-/Trialstatus.
10. Bestehenden Monitorzugang auf „Fähigkeit ergänzen/erneuern/entfernen“ am selben Account umstellen. Lokales Entfernen behauptet keine Anbieterrevokation. Disconnect-/Reconnectstatus folgt dem Host; verspätete Operationen dürfen entfernte Credentials nicht wieder sichtbar aktivieren.
11. Settings bietet Monitor/Gateway als Betriebswahlvorschlag mit erklärter Wirkung. Vorhandene Installation bleibt Gateway; Informationswechsel und Connect schalten weder aktiven Dienst noch Proxy um. Ein gewünschter Betriebswechsel ist eine eigene explizite Aktion; laufende Requests werden nicht still gestoppt.
12. CLI erhält denselben Ablauf über dieselben APIwrapper. TTY bleibt privat mit Esc/Ctrl-C/Ctrl-D; Umleitung wird vor Hostdiscovery abgelehnt. `add-api --key-stdin/--key-env` und stabile JSON-/Exitcode-Verträge bleiben für Automation verfügbar; Commandregistrierung in `src/Lain.Cli/Program.cs` bleibt bei AC-02.

Barebones, Simple und Advanced ändern ausschließlich Informationsumfang, niemals Scopes, Rechte, Methodenverfügbarkeit oder Kostenregeln. In jeder Stufe sichtbar: Anbieter/Account, benötigte Nutzeraktion, Ergebnis, Quota mit Einheit, Fehler-/Stalezustand und Quelle/Prüfzeit. Advanced ergänzt technische Phase, Scope-/Ressourcenbelege und Diagnose ohne Secrets.
Alle echten Quota-/Balancefenster bleiben auf der geschlossenen Kachel zugänglich; keine Addition verschiedener Pools, keine Prozentleiste ohne Nenner, kein unbekannt = 0. Last-known-Werte und Resetunsicherheit bleiben erkennbar.

### Zustandsvertrag für Darstellung und Aktionen

| Zustand | Darstellung und erlaubter nächster Schritt |
| --- | --- |
| `pending` | Laufende Anmeldung/Discovery/Read mit Phase; fortsetzen oder abbrechen, kein zweiter Start durch Polling. |
| `unsupported` | Diese Fähigkeit/Methode hat keinen unterstützten Weg; Anbieterportal oder vorhandener geeigneter Keyweg, kein Loginzwang. |
| `denied` | Fehlendes/verweigertes Readrecht; gezielt erklären und optional ergänzen, keine automatische breitere Berechtigung. |
| `expired` | Credential abgelaufen; passenden Owner erneuern/anmelden lassen, keine Snapshotrefreshbehauptung. |
| `unobserved` | Noch keine gültige Messung; bei passiven Headern normale Nutzung über lain erforderlich, kein erfundener Kontostand. |
| `partial` | Nur tatsächlich gelesene Fähigkeiten/Fenster anzeigen; fehlende Daten separat benennen, Erfolg nicht auf gesamten Account übertragen. |
| `stale` | Letzten bekannten Wert mit Quelle und Zeitpunkt erhalten; nicht als aktuelles Restbudget ausgeben. |
| lokal gespeichert / frisch gelesen | Konfiguration unbestätigt beziehungsweise konkrete erfolgreiche Beobachtung ausdrücklich unterscheiden. |

Operationszustand und Beobachtungszustand bleiben getrennte Achsen: eine Anmeldung kann fertig und die Quota trotzdem `unobserved`, `denied` oder `partial` sein. Revisionskonflikt/Transportfehler bietet kontrolliertes Neuladen/Wiederholen; kein doppelter Account und keine blinde Wiederholung von Remote-Create.

## AC-09 — Vollständiger Fähigkeitskatalog

Pro Produkt/Methode erfassen: Zweck, Inferenz-/Readfähigkeiten und Quotaart, Ressourcenscope, Regions-/Produktgrenzen, Credentialart/Owner, zusätzliche Rechte, Discoverybedarf, offizieller Einstieg, vorhandener Keyfallback, Qualifikationsblocker, Quellen-/Prüfdatum sowie Implementierungs-/Mock-/Livezustand.
Fähigkeiten gelten pro Methode und Produkt, nicht pauschal pro Anbieter. „Dokumentiert“, „implementiert/mockgeprüft“ und „live geprüft“ bleiben getrennt; ein erfolgreicher Login oder GET `/models` ist keine Inferenzqualifikation. Passive Header sind **kein eigenständiger Monitor**.

Die vollständige [Anbietermatrix mit Paketzuordnung und ehrlichem Fallback](provider-map.md) ist die zentrale Quelle für alle 25 Anbieter. Sie beschreibt das zu integrierende Ziel, keine schon ausgelieferten Connectors. Jeder Kandidat bleibt gesperrt, bis das dort zuständige Paket seinen durchgängigen Zugang qualifiziert hat.

AC-09 integriert Datenvorschläge erst nach AC-00-Schemafreigabe und jeweiligem Providerreview. Die 35 Varianten aus `account-connect-research/coverage.json` erhalten vollständige, eindeutig zugeordnete Produkt-/Methodenabdeckung; keine Zusammenlegung von Consumer/API, Regionen, Trial/Production, Coding/PAYG oder Org-/Keybudget.
Vorhandene Modell-/Tarifdeskriptoren und Quellen bleiben im Routingkatalog erhalten. Falls Referenzanschlüsse nicht als JSON geführt werden, legt AC-00 ihre formale Quelle fest; AC-09 prüft sie trotzdem in derselben 25-Anbieter-Abdeckung.

## Abhängigkeiten, Reihenfolge und Blocker

- AC-00 friert Contracts/Registryvalidierung und Betriebswahlsemantik ein; AC-01 liefert templateunabhängigen Account, stabile Bindung, Operationen, getrennte Credentialowner und Monitor ohne Core. Ohne diese Ergebnisse keine echte Monitor-only-Abnahme.
- AC-02 kann Präsentation/Mockabläufe gegen vereinbarte Fixtures vorbereiten; echte Methodenfreigabe erfolgt erst nach AC-03…08. Keine UIseitige Nachbildung von Hostautorisierung, Refresh oder Ressourcenentscheidung.
- AC-09 integriert nach den Providerlieferungen in einem Ownerdurchgang; Schema-/Datenfehler gehen an AC-00 beziehungsweise Providerowner zurück. Benennungen und Details dürfen UX-intern gewählt werden, Fähigkeiten und Sicherheitsgrenzen nicht.
- Fehlende öffentliche Clientregistrierung, unklarer CLI-/Sessiontransport, unbestätigte Readquelle oder OAuth-Inferenztransport bleiben explizite Methodenblocker. Vorhandene qualifizierte Keywege bleiben unabhängig nutzbar; kein globales „Anbieter unterstützt“ als Freigabe.

## Geplante Prüfung und Abnahme

- `tests/client-cli`: Fake-HTTP-/Terminalfixtures für identische Desktop-/CLI-Operationen, alle Zustände, Ressourcen einmal/mehrdeutig, Reconnect, Abbruch in jeder Phase, Revisionkonflikt und kontrolliertes Wiederholen ohne Duplikat.
- Private PTY-Prüfung über vorhandenes `wizard-pty.py`: Secrets erscheinen weder Echo/ANSI/JSON/Fehlertext noch Prozessargumenten; Esc/Ctrl-C/Ctrl-D und Umleitung bleiben sicher. Automation und vorhandene JSON-/Exitcodes regressionsprüfen.
- Desktopfixtures in privatem **Xvfb ausschließlich**: Providerwahl, Browser-/Devicefortsetzung, Keyboard/Fokus, Ressourcenauswahl, Kachel vor Modellen, alle Informationsstufen und schmale Ansicht. Keine GUIprüfung auf Nutzerscreen; keine echte Anmeldung für Fixtureabnahme.
- Alle Stufen zeigen identische erlaubte Aktionen/Scopes/Kostenregeln. Balance ohne Nenner, mehrere nichtadditive Pools, unknown/zero, partial/stale und fehlender Reset bleiben unterscheidbar; letzte Werte verschwinden nicht bei Lesefehlern.
- Katalogtest prüft exakt 25 Anbieter/35 Varianten, eindeutige IDs, vollständige Pflichtfelder, Quellen und Produktbindungen; keine startbare unqualifizierte Methode, kein passiver Standalonemonitor, keine Client-ID-Pflicht im Normalflow.
- Negative Vertragsfixtures: Groq ohne Enterprisequelle nur unobserved Header; NVIDIA ohne Quotaquelle Portal; Nebius ohne Balance kein erfundener Geldwert; Inception ohne Accountread keine Trial-/PAYG-Auswahl; OpenCode-MCP ohne Readtool kein Quotaerfolg.
- Monitor-only ohne Core/Route und anschließendes Ergänzen von Gatewayfähigkeit am selben Account prüfen; Key-/Sessionrotation, Disconnect und verspätete Resultate gegen AC-01-Fixtures. Kein UIcallback darf selbst Credentials wiederherstellen.
- Vorher/nachher vergleichen: angewandtes Profil, vorhandene Modell-/Tarifwahl, laufender Gateway und Requests bleiben durch Connect/Informationsstufe unverändert. Settings-Betriebswahl löst keinen versteckten Stop/Start aus; expliziten Wechsel gesondert prüfen.
- AC-10 übernimmt integrierte Abnahme; AC-00 allein serialisiert notwendige Builds. Mockbeleg, lokale Prüfung, Installation und echte Accountprüfung getrennt protokollieren. Dieser Plan hat keine dieser Prüfungen ausgeführt.
