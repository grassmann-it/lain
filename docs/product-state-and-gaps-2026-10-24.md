# Produktstand und nächste Arbeitspakete

Stand: 24.10.2026, lesender Abgleich auf `5dc916fcea702f2a196520903937570745e26dac`.
Zustandsbewertung mit **vorgeschlagenen** Folgepaketen; keine neue
Implementierungsfreigabe oder Ersatzspezifikation. Abgeschlossene Lieferungen
bleiben im Umfang ihrer Abnahme abgeschlossen.

## Einschätzung

Lain hat eine belastbare Gatewaygrundlage und ist auf diesem Linuxrechner
benutzbar. Für einen leicht verständlichen Release für andere Nutzer fehlen
vor allem durchgängige Abläufe: verbinden, Kontingente richtig einordnen,
Modelle mit dem gewünschten Client verwenden, Änderungen wirksam machen und
Fehler selbst beheben. Die Zahl vorhandener Anbieterbeschreibungen bildet
diese Produktreife nicht ab.

Die jüngsten Account-Connect- und UX-Arbeiten sind Recherche, Entwürfe,
Arbeitspakete und Screenshots. Ihre Fertigstellung bedeutet nicht, dass die
beschriebenen Logins, der Monitor oder die neue Oberfläche installiert sind.
Während dieser Prüfung wurde die parallele UX-Pilot-Entwurfsrunde gestartet;
auch sie bleibt laut ihrer Übergabe Designarbeit ohne Produktumsetzung.

## Geliefert und belegt

- Eigenständiger .NET-Host, Linux-Userdienst, Avaloniaoberfläche, CLI und
  persistente Request-/Versuchshistorie mit gemeinsamem API-Vertrag.
- Account-/Routenbindung und dauerhaftes Corejournal; synthetisch qualifizierte
  Wiederholung, Abbrüche, Protokolle und Corewechsel. Der Core stammt inzwischen
  aus externer, gepinnter CLIProxyAPI-v8.0.41-Quelle. Der ausgelieferte Build
  enthält weiterhin den eigenen Adapter und zwei dokumentierte Hookpatches.
- Accounts, Models, Profiles, Usage, Live, Settings/About; Tray mit getrennten
  Optionen für Minimize und Close, Quota-/429-Meldungen und kompakter CLI-Usage.
- Separates Codexprofil und lokaler Authhelfer. Die normale Codexkonfiguration
  bleibt erhalten. Das belegt keine Kompatibilität jedes Modells mit jedem Toolset.
- Die letzte Anbieterlieferung enthält 20 Katalogmanifeste einschließlich
  SambaNova, Upstage, Cohere, Kilo und Fireworks. Private Einrichtung,
  zusätzliche Monitorzugänge und passive Groq-Erfassung sind vorhanden.

Belege: [Lieferbericht](provider-implementation-2026-10-24.md),
[Release-Manifest](../tests/acceptance/evidence/providers-release-final.json),
[Coreupgrade](core-v8-upgrade.md). Die Anbieterlieferung prüfte 1.038 Paketdateien
und 182 installierte UI-Checks. **Sie enthielt keine neuen echten
Provider-Inferenzproben.** Frühere reale Einzelproben und synthetische
Qualifikation sind keine allgemeine Livefreigabe aller Modelle.

## Frischer Installationsstand

Lesende CLI-Abfragen vom **24.10.2026, 21:04 Europe/Berlin**:

| Beobachtung | Bedeutung |
| --- | --- |
| Proxy bereit, kein gemeldeter Startfehler | Der bestehende Gateway läuft. |
| Gespeicherte Revision **11**, angewandte Revision **2** | Ausstehende Änderungen; gespeichert bedeutet nicht aktiv. |
| Aktive Profil-ID/-Revision `null` | Die API kann die aktive Profilidentität nicht belegen. Die Ursache wurde nicht untersucht. |
| **11 Accounts**, **3 Profile**, **47 konfigurierte Modelle** | Modelle sind nach Anbieter und Upstreammodell gruppiert; keine Routenzählung oder Zusage von 47 aktiven Clientmodellen. |
| Heute **13 Requests/Versuche, 10 Fehler**, Cursor **41**, `complete=true` | Der bekannte Journalstand ist übernommen; zehn Versuche haben unbekannte Usage. |
| Gesamte Tokenfelder `null` | Eine vollständige Summe ist unbekannt. Einzelne erfolgreiche Versuche enthalten dennoch gemeldete Tokens. |

Die begrenzte Versuchsliste enthält heute acht HTTP-400- und zwei HTTP-429-
Fehler. Sie liefert keine persistierten Fehlerursachen. Die zuvor vom Nutzer
gezeigten Toolfehler sind zusätzlicher Kontext, keine aus den HTTP-Codes
bewiesene Erklärung jedes Versuchs. Keine Anbieteranfrage wurde wiederholt
und keine Token-Teilsumme aus einer Seite der Versuchsliste berechnet.

| Provider im aktuellen Cache | Zustand |
| --- | --- |
| Codex, Z.ai, Kimi, OpenRouter | Gemeldete Quota-Fenster `ok` |
| Command Code | Fünf Fenster `ok`, eines `unknown` |
| Groq | `awaiting-observation`: passive Quelle wartet auf geeignete reguläre Inferenz |
| Gemini | `setup-required`: zusätzlicher Monitorzugang fehlt |
| Cloudflare | Reader `unimplemented` |
| Kilo | Reader fehlt in lain; geeigneter Wallet-/Pass-Leseweg ist recherchiert |
| MiMo, NVIDIA Build | Konsolenhinweis; aktuell keine qualifizierte Quota-Anbindung |

Alle elf Accountobjekte melden daneben `unverified`. Das bezeichnet keine
gescheiterte Quotaabfrage und beweist keinen ungültigen Key. Auth-, Inferenz-
und Monitorzustände müssen verständlicher getrennt werden. Die Cacheabfrage
löste keinen Refresh aus und belegt keine neue Livequalifikation.

## Bereits geplante offene Arbeit

| Bereich | Tatsächliche Lücke | Vorhandener Arbeitsstand |
| --- | --- | --- |
| Account Connect/Reconnect | Verbindungs-/Tarif-/Modellstruktur prägt noch die Accountanlage; vereinfachte Browserwege sind weitgehend nicht implementiert. | [AC-00–10](account-connect-work-packages/README.md), 25 Anbieter zugeordnet. Keine zweite Loginarchitektur anlegen. |
| Eigenständiger Monitor | Installer verlangt Core; Boot, Collector und eigener Codexlogin sind gekoppelt. | [Monitorentwurf](monitor-mode.md); AC-00/01/10. Kilo als erster vollständiger Durchstich vorgeschlagen. |
| Übersicht/Interaktion | Aufklappen verschiebt Kacheln, Metadaten dominieren, ein leeres Fenster kann den gesamten Account als erschöpft darstellen. | [Elf belegte UX-Befunde](ux-review-2026-10-24.md). |
| Barebones/Simple/Advanced und Aktionen | Zu wenig unterschiedlicher Arbeitsumfang; gemeinsame Kontext-/Menüaktionen und kleine Monitoransicht fehlen. | [Modus-/Aktionsentwurf](ui-modes-and-actions.md). UI-Modus, Betriebswahl und Dichte getrennt behandeln. |
| Neue Gestaltung | 318 synthetische Xvfb-Aufnahmen und Prompts liegen vor; parallele Generierung laut Ergebnisprotokoll angelaufen. Noch keine Produktumsetzung. | [UX-Pilot-Übergabe](uxpilot-codex-handoff-2026-10-24.md), [laufende Entwurfsrunde](uxpilot-design-results-2026-10-24.md). |
| Anbieter modular ergänzen | JSON-Katalog vorhanden, Reader-/Auth-/Transportregistrierung und Providerregeln noch verteilt. | [M0](provider-work-packages/modularity.md) mit AC-00 abgleichen; keine zweite Pluginengine. |

Die AC-Basis enthält noch zu konkretisierende Betriebs-/Persistenz- und
Credentialowner-Verträge. Consoleconnectoren sind zunächst Versuche; OAuth
allein ist kein Quotabeleg. Nicht alle Pakete sind sofort unabhängig ausführbar.

Aus Review und UX-Pilot-Entwürfen sollten kleine Umsetzungspakete entstehen:
**Accountkacheln/Details**, dann **Modi/gemeinsame Aktionen**, danach
**Models/Profiles/Usage/Live**. AC-02 übernimmt den neuen Connectablauf,
kein zweiter Wizard. Konkrete Layoutentscheidungen bleiben bis zur Auswahl
der Entwürfe offen; eine laufende Generierung ist keine Designabnahme.

## Zusätzlich vorgeschlagene Arbeitspakete

P1: wesentlich für einen verlässlich nutzbaren Gatewayrelease.
P2: vor breiterem oder längerem Betrieb gezielt qualifizieren.
Priorität und Zuschnitt sind Empfehlungen, keine neue Releasezusage.

### ST-01 · Client- und Modellkompatibilität · P1

**Befund:** [CodexProfileAdapter](../src/Lain.Host/Models/CodexProfileAdapter.cs)
exportiert konservative Standardmetadaten; eine qualifizierte Modellfähigkeits-
quelle fehlt. Seine Versionskonstante nennt 0.162.1, während
[Toolaufnahmen](providers/codex-gateway-tools.md) bereits 0.164.0 untersuchen.
Websuche ist im separaten Profil deaktiviert. Glyph-Tooltypen, Beams
128-Tool-Grenze und Lagunas 429 sind getrennte, nicht pauschal gelöste Fälle.

**Ergebnis:** Kompatibilitätsmatrix je Clientversion, Route, Protokoll und
benötigter Fähigkeit; Export nur belegter Modelldaten. Bekannte Inkompatibilität
vor Versand verständlich erklären, unbekannte Fähigkeiten nicht erfinden.
Eine optionale eingeschränkte Clientkonfiguration benennt ihren Funktionsverlust.

**Abnahme:** Repräsentative Streaming-/Toolfortsetzungen, Namespace-/Custom-
Tools, Toolanzahl und Kontextgrenzen mit synthetischen Anbietern; reale Proben
gesondert ausweisen. Vollständiges verwendetes Toolset zählen, nicht MCP-Server.
Keine willkürliche Toolkürzung oder stiller kostenpflichtiger Fallback.

**Grenze:** Zunächst Codex; Claude Code/OpenCode brauchen später eigene
Adapterqualifikation. Bestehende Transporttests bleiben erhalten.

### ST-02 · Gespeichert, aktiv und im Client sichtbar · P1

**Befund:** [Apply im Host](../src/Lain.Host/Program.cs) stoppt den gemeinsamen
Core, schreibt die Runtime und startet neu. HTTP-Mutationen sind bereits
serialisiert und revisionsgeschützt. `Enabled`, `Visible`, globale
Profilanwendung und Clientexport haben unterschiedliche Wirkungen.
Der [Codexinstaller](../src/Lain.Cli/CodexArtifactInstaller.cs) ersetzt Katalog
und Konfiguration einzeln; die Paarung ist keine gemeinsame Transaktion.

**Ergebnis:** Vor Anwendung zeigen, welche Routen/Accounts und welches Profil
sich ändern, was bereits aktiv ist und welche Clients betroffen sind.
Laufzeit, aktives Profil und exportierter Katalog erhalten eine nachprüfbare
Zuordnung. Fehlgeschlagene Anwendung oder unterbrochener Export bekommen
einen dokumentierten Wiederherstellungsweg.

**Abnahme:** Speichern, separat aktivieren, Client öffnen, veralteten Export
erkennen; Absturz/Schreibfehler zwischen Veröffentlichungsschritten;
Aktivierung während eines Streams; deaktivierter Profildefault und weitere
ausstehende Änderungen. Kein erfolgreiches Aktivsignal für Teilzustände.

**Offene Produktentscheidung:** Schnelle Routensperre beim nächsten Apply
oder eigene sofortige Betriebsaktion. Für den ersten Ausbau empfehle ich klare
gespeicherte Schalter mit Änderungsübersicht. Eine spätere Sofortsperre braucht
einen eigenen Vertrag und darf nicht unbemerkt alle ausstehenden Änderungen
anwenden. Mehrere Clients teilen derzeit einen globalen Laufzeitstand;
unabhängige Profile je Client sind keine vorhandene Funktion.

### ST-03 · Usage bei Teilmessungen und Fehlern · P1

**Befund:** Die [Serveraggregation](../src/Lain.Host/Usage/SqliteUsageStore.cs)
lässt eine Tokenkomponente unbekannt, sobald einem ausgewählten Versuch der
Wert fehlt. Das wahrt den Vertrag, lässt aber bereits gemessene Nutzung in
der Gesamtsicht verschwinden. `complete` beschreibt den Collectorstand,
nicht vollständige Tokenmessung. Der Attemptvertrag hat ein Fehlerfeld,
der gelesene Persistenz-/Ausgabepfad liefert dafür keine Ursache.

**Ergebnis:** Zusätzlich zur weiterhin nullable Gesamtsumme einen ausdrücklich
bekannten gemeldeten Teilbetrag und Messabdeckung serverseitig liefern.
GUI, CLI und Skill unterscheiden Journal- von Messabdeckung und zeigen
dieselben Werte. Kleine strukturierte Fehlerkategorien mit Handlung anbieten,
soweit belegbar; keine rohen Credential-/Prompt-/Antwortdaten persistieren.

**Abnahme:** Bekannte/unbekannte Tokens, fehlende Cache-/Reasoningwerte,
Abbruch, 400/429, doppelte Events und identische Filter. Unbekannt wird nicht
zu null Tokens; der Teilbetrag gilt nicht als vollständiger Verbrauch.
HTTP 429 allein beweist keine ausgeschöpfte Abogrenze.

**Abhängigkeit:** Fehlerkategorien mit ST-01 abstimmen; additive DTO-Erweiterung
und gemeinsame Aggregation, keine lokale Summierung paginierter Versuche.

### ST-04 · Dauerbetrieb, Datenwachstum und Wiederherstellung · P2

**Befund:** Das [Journal](../proxy/adapter/internal/lain/journal.go) wächst
append-only, wird beim Start vollständig gelesen und hält einen Offset je
Ereignis im RAM. Ein unvollständiger letzter Eintrag stoppt das Öffnen und
verlangt Operator-Recovery. Das ist ein Integritätsschutz; ein nutzergeeigneter
Recovery-/Archivierungsablauf fehlt in diesem Pfad. Crash-/Replayprüfungen
belegen keine langfristige Skalierung.

**Ergebnis:** Größen-/Plattenzustand sichtbar machen und Startzeit, RAM und
Abfragen mit synthetischer Langzeithistorie messen. Darauf begrenzte
Journalabschnitte/Archivierung sowie konsistentes Backup/Restore von
Konfiguration, Credentials, Journal und SQLite entwerfen und qualifizieren.
Ein Wartungsablauf erklärt Integritätsfehler und die konkrete Reparatur.

**Abnahme:** Volles Dateisystem, torn append, Neustart, Restore aus passendem
Backup, Replay über Archivgrenzen. Keine bestätigten Ereignisse verlieren,
keine geänderten Summen oder zurückgesetzten Source-/Cursoridentitäten.
Bestehende Daten nicht automatisch löschen; Aufbewahrungs-/Löschregeln vorher
ausdrücklich festlegen.

**Grenze:** Keine Requesthistorie aus Quotadifferenzen. Ein persistenter letzter
Quotastand ist ein kleiner separater Zusatz nach AC-01: Quellenzeit,
Credentialgeneration, Scope und Stalezustand erhalten. Quotakurven und
Verbrauchsprognosen bleiben spätere Produktarbeit.

### ST-05 · Nachvollziehbarer Release · P1 für Veröffentlichung

**Befund:** Paketmanifest, Prüfsummen, Komponentenprovenienz, Drittanbieter-
Lizenzsammlung und Installationsprüfungen existieren. Die API meldet pauschal
`0.1.0`; README und ältere Doku beschreiben teils frühere Kontostände/Prüfungen.
Neue Recherche-/Planungsdateien sind unversioniert, Screenshots liegen in
ignorierten Artefakten. Eine `.github`-CI-Konfiguration fehlt im geprüften
Repository. Die eigene Anwendungslizenz ist noch nicht deklariert; auch
[About](../src/Lain.Desktop/MainWindow.Settings.cs) sagt das. Drittanbieterlizenzen
entscheiden diese Produktfrage nicht.

**Ergebnis:** Reproduzierbarer Releaseablauf verbindet Source-/Build-/Corestand,
aktuelle Fähigkeiten, passende Testnachweise und Paket. README/About/Status
erklären denselben Lieferstand. Fresh install, Update, Rollback und Wiederanlauf
haben einen durchgängigen Nutzerweg. Pläne und ausgewählte Reviewreferenzen
erreichen einen frischen Checkout bzw. eine explizite Übergabe tatsächlich.

**Abnahme:** Isolierte Linuxinstallation ohne Entwicklerverzeichnisse, Upgrade
eines Bestands, erhaltene Accounts/History/Clientconfig, kontrollierter
Installationsfehler und Wiederherstellung. Vor breiterem Desktopversprechen
konkrete X11-/Wayland-/Trayumgebung und reale Eingabe prüfen; Screenshots
ersetzen dies nicht. Bestehende native Einzelprüfungen weiterverwenden.

**Offen:** Erster öffentlicher Releaseumfang, Produktlizenz und Verteilung.
Die Lizenzwahl trifft der Nutzer. Automatischer Updater, Signierinfrastruktur
und öffentliches Pluginformat sind dafür nicht bereits beschlossen.
Der notwendige Corepatchumfang bleibt in jeder Releaseprovenienz sichtbar.

## Übergabereife vor der Umsetzung

Nachfrage des Nutzers: Müssen vor der Umsetzung weitere Arbeitspakete definiert
werden? **Gezielt ja.** Die vorhandenen AC-Pakete enthalten bereits Aufgaben,
Dateizuständigkeiten und Abnahme. ST-01–05 sind dagegen zunächst Zuschnitte;
sie sind noch keine vollständigen parallelen Workeraufträge. Die folgende
Priorisierung bleibt eine Empfehlung, keine Implementierungsfreigabe.

| Bereich | Vor Delegation noch nötig | Unabhängiger früherer Einstieg |
| --- | --- | --- |
| AC-00–10 | Bestehende Übergabe verwenden; AC-00 konkretisiert gemeinsamen Betriebs-/Account-/Credentialvertrag und ausführbare Beispiele vor Fachintegration. Methodenspezifische Qualifikationsgrenzen erhalten. | Vertragsarbeit, Readerparser und synthetische Fixtures nach bestehender Aufteilung. |
| ST-01 | Quellen-/Reifevertrag für Fähigkeiten; prüfen, wo bekannte Einschränkungen im tatsächlichen Inferenzpfad greifen; konkrete Client-/Tool-Testmatrix und Dateigrenzen. | Vorhandene Codex-Transportfixtures und reproduzierbare Toolcaptures zusammenführen. |
| ST-02 | Gespeichert/aktiv/exportiert genau abbilden; Apply-/Abbruch-/Recoveryvertrag und atomare Veröffentlichung zuschneiden. Sofortsperre als eigene offene Funktion abgrenzen. | Lesende Zustands-/Änderungsübersicht mit bestehender Applysemantik planen. |
| ST-03 | Bedeutung bekannter Teilsummen und Messabdeckung je Tokenkomponente; additive DTOs, gemeinsame Aggregation, belegte Fehlerkategorien und Persistenzmigration. | Aggregationsfälle lokal spezifizieren; Fehlerklassifikation nur vom entsprechenden ST-01-Vertrag abhängig machen. |
| UX | Ausgewählte Entwürfe in Komponenten, Interaktionen und prüfbare Zustände für breite/schmale Fenster übersetzen; Modus-/Fähigkeitsmatrix festhalten. | Kleine bestehende Fehler wie Scrollposition oder falscher Accountgesamtstatus können separat zugeschnitten werden. |
| ST-04 | Zuerst begrenzte Größen-/Startzeit-/Recoveryanalyse. Archivierung und Retention erst mit Daten-/Replaygarantien und festgelegtem Aufbewahrungsumfang ausarbeiten. | Synthetische Messung und dokumentierter Ist-Recoverypfad, ohne bestehende Daten zu ändern. |
| ST-05 | Ersten beworbenen Releaseumfang und benötigte Abnahme festlegen; Lizenz/Verteilung vor Veröffentlichung entscheiden. | Vorhandene Paketprüfungen und Buildidentitäten vereinheitlichen. |

Empfohlen für die nächste Planrunde: **ST-01, ST-02 und ST-03 ausformulieren**,
die vorhandene AC-Übergabe um gemeinsam benötigte Verträge ergänzen und die
UX-Pakete nach Auswahl der Entwürfe konkretisieren. ST-04/05 müssen dafür
nicht vorab vollständig durchgeplant sein. Offene Fragen blockieren nur die
jeweils abhängigen Teile.

Jeder ausführbare Auftrag braucht Ergebnis/Abgrenzung, aktuelle Quellenbasis,
Ein-/Ausgabeverträge, Abhängigkeiten, zuständige Dateien und beobachtbare
Abnahme einschließlich relevanter Fehlerfälle. Klassenbenennung und andere
gewöhnliche Implementierungsdetails bleiben beim Implementierer.

Für die Zusammenführung einen gemeinsamen Eigentümer für DTOs, Hostverdrahtung
und Migrationen beibehalten. AC-02 und UX teilen einen abgestimmten UI-Owner;
ST-Aufträge dürfen bestehende Dateizuständigkeiten nicht still überschreiben.
Neue Worktrees erhalten die bislang unversionierten Planungsdateien ausdrücklich
mit. Diese Reifeprüfung führt keine Commits oder Workerstarts aus.

## Empfohlene Reihenfolge und spätere Ziele

1. **AC-Basis + Kilo + Accountübersicht:** Ohne Core einen vorhandenen Zugang
   verbinden und sinnvolle Quotas sehen; Gatewaybestand vollständig erhalten.
2. **ST-01 parallel, ST-02 auf gemeinsamen Verträgen:** Codex-Testweg
   vorhersagbar machen, Änderungen verständlich übernehmen. Neue Provider
   anschließend auf dieselbe Qualifikation setzen.
3. **ST-03 und restliche UX-Pakete:** Gemessene Nutzung nützlich darstellen,
   Modi/Aktionen unterscheiden; weitere Logins einzeln nach Reife liefern.
4. **ST-04/05 vor breiter Auslieferung:** Dauerbetrieb, Wiederherstellung und
   ausdrücklich beworbenen Releaseumfang abnehmen.

Automatische Kontingentoptimierung, providerübergreifende Fallbackausführung,
Rollenrouting, Classifier, Approver, eigene Researchfunktion, externe
Clienthistorie und Skills-/Agentbibliothek sind spätere Vorhaben. Inspektor
und lokale Proxyhistorie sind dagegen bereits vorhanden. Fallbackgraphen
können gespeichert/geprüft werden; ihre Ausführung wird im
[Kosten-/Routingvertrag](../src/Lain.Host/Providers/ProviderCostPolicy.cs)
beim Apply ausdrücklich abgewiesen. Die Oberfläche darf dies nicht als
aktive Fallbackfunktion verkaufen.

## Prüfgrenzen

Aktueller Code, bestehende Liefernachweise, Entwürfe und lokale CLI-Reads wurden
abgeglichen. Zwischen App-Quellpin `413588c` und HEAD gibt es in `src`, `proxy`
und `scripts` keinen Diff. Frühere PASS-Nachweise wurden gelesen, nicht in dieser
Bewertung erneut ausgeführt. Kein Vollreview aller Methoden, keine neuen
Builds/Tests, Providerreads, Logins, Applys, Servicewechsel, Installationen
oder Commits. Fremde laufende Recherche- und UX-Pilot-Dateien bleiben erhalten.
