# Arbeitspakete: Connect und Doctor für den ersten Einstieg

Stand 26.10.2026. Nutzerauftrag: nach der Priorisierung konkrete Arbeitspakete
für die zu implementierenden Teile ausarbeiten. Das ist eine Planübergabe;
Implementierung, Commit, neue Providerrequests und Installation sind durch
diesen Planauftrag nicht bereits ausgeführt. Bestehende Umsetzungsaufträge
bleiben erhalten. Die technischen Vorschläge unten konkretisieren den empfohlenen
Zuschnitt; die Planung ist keine öffentliche Releasefreigabe.

## Ausführung und aktueller Stand

Der anschließende Nutzerauftrag autorisiert die parallele Umsetzung mit
GPT-6.1-Sol-Workern. AP1–AP4 sind im Arbeitsbaum integriert: öffentlicher
`lain doctor`, `lain-connect`, `lain-doctor` sowie gemeinsame gebündelte
Installation von usage/workers/connect/doctor. Das CLI-Profilargument von Doctor
bezeichnet ausdrücklich den **nativen Codex-Profilnamen**; die aktive
Lain-Hostprofilidentität dient unabhängig zum Vergleich des installierten Exports.

AP4: 39 Offlineprüfungen bestanden, inklusive bytegleicher Ressourceninstallation
und Bestanderhaltung. AP1: deterministische Fehler-/Metadatenfixtures bestanden;
reale Clientaktivierung, vollständige VSIX-Integrität und Providerakzeptanz bleiben
unbeobachtet. Ein unabhängiger Skill-Forward-Durchlauf mit fünf synthetischen
Szenarien und 43 Fake-CLI-Aufrufen fand unnötige Abfragen und eine zu allgemeine
Preview-Anweisung; beide Formulierungen wurden gezielt korrigiert. Diese Spuren
qualifizieren keine echte CLI/Providerantwort.

AP0: Kernel verweigert normale Exe-Metadaten mehrerer Containerprozesse. Die vom
Nutzer autorisierte Prüfung im separaten sudo-Terminal identifizierte ssh-agent
und nginx. Ein optionaler privilegierter, ausschließlich lesender Prüfweg ist
gegen dieselben Klassifikationsregeln integriert und unabhängig geprüft.
20 synthetische Prüfungen und der echte AST-isolierte Scanner als UID1000
bestanden: 412 Prozesse, 78 privilegierte Reads, 1,319 Sekunden.
Der Quellenpin lautet `21142fb8ca76b1d58aabe76f4d7586856adfae6081dc9b6707043be3e3fb2bb7`.
Das ist kein gültiger Paket-/Installationsbeleg und ersetzt keinen frischen
exakten Update-Qualifikationslauf. Snapshotprüfung ist keine atomare Prozessruhe.
Keine pauschalen Prozessnamen-/Docker-Ausnahmen und keine Umdeutung des alten
NOT-VALID-Belegs. AP5 und die produktive Installation sind noch nicht abgeschlossen.
Neue Source ist weiterhin keine installierte Version.

Aktuelle private Pakete: `~/.cache/lain-connect-doctor-20261026/packages-r2/`,
Quellenpin `02b82e00d6dd037a9bf27cc6ce7c4f53c12c1c04` in separater Arbeitskopie;
ursprüngliches Main-WIP erhalten. Beide vollständigen Pakete gebaut, 22 echte
Offline-CLI-Aufrufe bestanden und fünf Skillressourcen pro Paket bytegleich
installiert. Gateway-Core reproduziert SHA256
`f737b8726b845796f38bad6f9a881bddb96e9598ea8908f9849559d91a2164cd`;
die 15 synthetischen Backendfälle bestanden in eigener Netznamespace. Das ersetzt
nicht die vollständige Core-/Paketqualifikation. Die Pakete bleiben gestuft und
uninstalliert. Der erste reale lesende Doctorlauf erkannte den vorhandenen
Synthetic-Account ohne konfigurierte Modellroute, zeigte aber noch zu grobe
Unbekannt-Befunde bei komplexen normalen TOML-Abschnitten; Der gezielte Parserfix besteht Fixtures und echten lesenden Quelllauf;
erneuter Paketbuild folgt. Rootprovider, Katalogvalidität und unbekannte
Inline-Authbeobachtung werden unabhängig bewertet. Fehlgeschlagener erster Monitorbuild bleibt
unter `packages/monitor.log` als Beleg erhalten; gemeinsame Desktop-Datei hatte
eine CLI-Abhängigkeit erhalten, die vor r2 behoben wurde.

## Ziel und Ausgangsstand

Ein Nutzer kann einen bestehenden Lainzugang mit Codex CLI oder der lokal
qualifizierten Codex-Erweiterung in VS Code verbinden und bei Problemen eine
verständliche Diagnose erhalten. Linux x64 und die bereits qualifizierten
Clientversionen bilden v1. Andere Harnesse werden als nicht qualifiziert
angezeigt; ihre Adapter gehören in spätere Pakete.

Arbeitsbasis: `main`, HEAD `fef478f601f46b315939fa53f02caa9791086f1d` mit
umfangreichem bestehendem WIP. Kein sauberer Releasecheckout. Vorhandene Quellen
und Nachweise erhalten. Gemeinsamer Lieferplan:
[Worker-/Releasebericht](release-workers-2026-10-26.md#vorschlag-für-das-nächste-lokale-update).
Produktgrundlage: [Produktziele](product-scope.md),
[Workervertrag](workers-v1.md), [Clientlieferung](vscode-model-groups-delivery-2026-10-26.md).

Vorhanden sind Account-/Loginaktionen, Profilvorschau, revisionsgebundenes Apply,
Codexexport/-installation und private Clientwartung. `lain-usage` ist installiert,
`lain-workers` liegt als gebündelte Source vor. `lain-connect`, `lain-doctor`,
`lain doctor` und `lain skills …` existieren noch nicht; unten sind vorgeschlagene
neue Verträge. UI und CLI bleiben die ausführenden Verwaltungsschnittstellen.

## Gemeinsame technische Festlegung für die Übergabe

- Die Skills führen mehrschrittige Abläufe mit der installierten öffentlichen
  CLI. Sie pflegen keine eigene Account-/Modell-/Preisdatenbank und editieren
  keine Providerstores. Gültige bestehende Sessionautorisierung wird genutzt.
- Ein gemeinsamer lesender Diagnosebefehl lautet vorgeschlagen
  `lain doctor --target codex-cli|codex-vscode [--project DIR] [--profile ID] [--json]`.
  Ohne Ziel liefert er Lain-Dienst-/Versions-/Betriebsstatus; ohne Projekt keine
  erfundene Workspaceprüfung. Er führt weder Reparatur noch Inferenz aus.
- JSON-v1 enthält `schemaVersion`, Beobachtungszeit, Ziel/Scope, beobachtete
  Versionen und eine `findings`-Liste. Pro Befund: stabiler `code`, Zustand
  `ok|warning|blocked|unknown`, knappe Erklärung, konkrete Herkunft und geeigneter
  nächster Schritt. Vorschläge sind Daten, keine automatisch ausgeführten Befehle.
  Ungeprüfte Providerakzeptanz bleibt unbekannt.
- Diagnose unterscheidet Erreichbarkeit, Accountsetup, gewünschte/angewandte
  Revision, installierten Export und tatsächlich gewählte Clientkonfiguration.
  Ein gültiger Export allein beweist keine effektive Bindung des laufenden Threads.
- Client-/Katalogprüfung verwendet vorhandene Parser, Artifactinspektion und
  Wartungsbelege, soweit geeignet. Lokale Configs nur begrenzt und über eine
  Feldallowlist lesen; keine vollständigen Configs, Secrets, Prompts oder rohen
  Inspectorpayloads ausgeben. Keine neuen Hostendpunkte, solange öffentliche
  Hostdaten plus eigene lokale Metadaten den Befund tragen.
- Read-only-Diagnose löst keine Providerrefreshes, Auth-Tokenausgabe, Logins,
  Profilanwendung, Clientstarts mit Turns, Dienststarts oder IDEreloads aus.
  Vorhandener Host darf seine unabhängigen Hintergrundaufgaben weiter ausführen.

## Pakete und exklusive Verantwortung

### AP0 — Vorhandene Reparaturen lieferfähig machen

**Ergebnis:** gemeinsamer Kandidat aus Worker-/Kontext-/Reasoningexport und
Cohere-Corefix statt weiterer punktueller Katalogreparaturen.

Die Arbeit A–C des [Lieferplans](release-workers-2026-10-26.md#vorschlag-für-das-nächste-lokale-update)
übernehmen: WIPumfang abgrenzen, Updateidentitätsprüfung klären und das genaue
Gatewaypaket qualifizieren. Vorhandenen Reasoning-Schnittmengenexport nutzen;
die noch fehlende tatsächliche Router-/Effortweitergabe prüfen. Offene
Modellqualität bleibt im [Providerbericht](provider-tool-context-repair-2026-10-26.md)
sichtbar. Der alte Updatebeleg `NOT-VALID` wird nicht umetikettiert.

**Abnahme:** Quell-/Manifest-/Paketbindung stimmt; Installation erhält Account-,
Profil-, History- und Credentialbestand; Auto und Worker funktionieren am
Kandidaten. Keine zusätzliche vollständige Skillbibliothek als Voraussetzung.
**Owner:** Integrator/Delivery; große Builds und Runtime in einer seriellen Lane.
AP0 kann neben der Sourcearbeit AP1–AP3 vorbereitet werden; die gemeinsame
Installation folgt erst nach Integration und AP5-Vorprüfung.

### AP1 — Öffentliche Diagnose für den konkreten Clientanschluss

**Ergebnis:** der neue `lain doctor` liefert verständliche Textausgabe und
sanitisierte strukturierte Befunde aus demselben Diagnosemodell.

**Änderungen:** neue Dateien unter `src/Lain.Cli/Diagnostics/` und eigenständige
Fixtures unter `tests/client-diagnostics/`. Bestehende öffentliche Clientmethoden
und `CodexArtifactInstaller.Inspect` verwenden. Fehlende Diagnosedaten gezielt
ergänzen; keine Credential-/SQLite-/Usagequeue-Zugriffe.

Prüffälle: Host nicht erreichbar, Monitor statt Gateway, fehlender Accountsetup,
abweichende Revision, fehlender/veralteter Export, falscher Provider/Katalog im
Client oder Workspace, unzulässiger Effort, Auto-Katalog ohne qualifizierte
Effortliste, fehlender/falscher Helper, veraltete Clientversion und VSIX
ausgewählt aber Aktivierung unbekannt. Fehlendes Setup, unbekannte Unterstützung
und tatsächlicher Fehler sind unterschiedliche Befunde. Bei Tool-/Kontextfehlern
zwischen Anzahl und Tokenkontext unterscheiden; ohne tatsächlichen Payload keine
erfundene Messzahl. HTTP 200 ist kein Beleg einer fachlich korrekten Antwort.

**Abnahme:** deterministische Fixtures für die relevanten Fehler und den guten
Pfad; begrenzte Zeit-/Größenbudgets; Ausgabe enthält keine eingelegten Testsecrets.
Nicht erreichbarer Host erlaubt verbleibende lokale Teilbefunde. Konfiguration
und Betriebszustand bleiben erhalten. Vorhandene CLI-Exitcodes nutzen; Befundstatus
im JSON ist die fachliche Diagnose und wird nicht mit Transportfehlern vermischt.
**Abhängigkeit:** oben festgelegter Diagnosevertrag; CLI-Verdrahtung durch AP4.

### AP2 — Skill `lain-connect`

**Ergebnis:** ein geführter, wiederaufnehmbarer Anschluss über vorhandene
Account-/Profil-/Clientoperationen und die Diagnose aus AP1.

**Exklusive Dateien:** `skills/lain-connect/`; die Beschreibung begrenzt auf
Clientanschluss mit Lain. Vor Erstellung `skill-creator` verwenden.

Ablauf: installierte Fähigkeiten ermitteln, vorhandene passende Accounts und
Profile bevorzugen, Client/Zielprojekt eindeutig zuordnen, Vorschau und
Revisionsbezug herstellen, beauftragte Schritte ausführen, danach effektive
Bindung prüfen. Fehlenden Accountzugang in den vorhandenen privaten UI-/TTY-
oder sichtbaren Loginweg übergeben. Keine Secrets im Chat. Login, Export,
Clientinstallation und globales Profilapply bleiben getrennte Operationen;
ein vorhandener Account oder Statusauftrag löst kein Apply aus.

Explizite gültige Modell-/Effortwahl erhalten. Konflikte verständlich ausgeben;
keine heimliche Effortsenkung, Profilersetzung oder Umschaltung der normalen
Codexkonfiguration. VS Code nutzt seinen qualifizierten bestehenden Adapter;
der Skill erfindet keine neue Alpha-Kompatibilität und lädt das Fenster nicht
nebenbei neu. Abschluss nennt aktiven oder noch nicht beobachteten Zustand.

**Abnahme:** frischer Anschluss und bestehender bereits korrekter Anschluss,
veralteter Export, Workspaceoverride, nicht unterstützter Client, fehlende
Anmeldung und nötiger IDEreload in realistischen trockenen Skilldurchläufen.
Bestehende Autorisierung wird genutzt, nur fehlende wesentliche Auswahl abgefragt.
**Abhängigkeit:** AP1-Ausgabe; Sourcearbeit nach Vertragsfestlegung parallel möglich.

### AP3 — Skill `lain-doctor`

**Ergebnis:** konkrete Fehlermeldung eingrenzen, passenden Befund erklären und
innerhalb des Reparaturauftrags eine gezielte Korrektur samt Nachprüfung führen.

**Exklusive Dateien:** `skills/lain-doctor/`; Erstellung mit `skill-creator`.
Primär Diagnose aus AP1, bei Bedarf begrenzte öffentliche Attempts/History mit
passendem Zeit-/Gesprächsbezug. Kein pauschales Vollprotokoll einsammeln.

Korrekturen verwenden vorhandene öffentliche Verwaltungs-/Wartungswege.
Revisionskonflikt bedeutet neu lesen und bewerten. Unveränderter Retry,
Modell-/Accountwechsel oder längeres Timeout gelten nicht als Diagnose.
Bei fehlender Reparaturfunktion tatsächliche Grenze nennen und einen konkreten
Entwicklungsbefund liefern; kein Erraten und Patchen von Produktivdateien.

**Abnahme:** Effort/none bei Astra bzw. Auto, fehlender Helper, falsche Bindung,
128-Toolgrenze versus Kontextüberschreitung, abgelaufener Zugang versus 429 und
unbrauchbare Antwort trotz HTTP 200. Diagnoseauftrag allein verändert nichts;
ein bestehender konkreter Reparaturauftrag braucht keine zweite pauschale
Freigabe. Ein erforderlicher sichtbarer Anbietergrant bleibt beim Nutzer.
**Abhängigkeit:** AP1; getrennt von AP2 parallel bearbeitbar.

### AP4 — Integration und projektlokale Skillinstallation

**Ergebnis:** das Paket enthält die vorgesehenen Skills und kann sie ohne
Hostverbindung projektlokal installieren.

**Vorgeschlagener Vertrag:** `lain skills install NAME --project DIR [--json]`,
zunächst feste Namen `lain-usage`, `lain-workers`, `lain-connect`, `lain-doctor`.
Kein Marketplace, Download oder Benutzer-globaler Migrationslauf.
`lain workers install-skill` bleibt als kompatibler Weg erhalten.

**Exklusive Dateien des Integrators:** `CliApplication.cs`, `Lain.Cli.csproj`,
gemeinsamer Skillinstaller samt Anpassung des vorhandenen
`WorkerSkillInstaller.cs`, CLIhelp und Integrationsdoku. AP1 liefert die neue
Handler-/Diagnoseimplementierung, AP4 verdrahtet sie. Paketressourcen und
Installationsallowlist haben eine gemeinsame Quelle.

**Abnahme:** neue Projektinstallation ohne Dienst, unbekannter Skillname,
vorhandene lokal bearbeitete Skills und unzulässige/symlinkende Zielpfade.
Kein Überschreiben bestehender Installation; vorhandene Workerinstallation bleibt
kompatibel. Die Skillinstallation startet keine Worker, legt keine Routen fest
und aktiviert kein Profil. Skillvalidator plus gezielte Paket-/CLIchecks.
**Abhängigkeit:** AP1–AP3-Inhalte; Paketintegration gemeinsam mit AP0.

### AP5 — Gesamtweg abnehmen und regulär ausliefern

**Ergebnis:** exakte installierbare Paketbytes und ein durchgängiger belegter
Einrichtungs-/Diagnoseablauf. Quelldateien allein sind nicht die Lieferung.

Vorprüfungen auf privaten Fixtures: Paketinstallation der Skills, CLIhilfe und
JSON, Connect in frischem und vorhandenem Projekt, absichtlich falscher
Katalog/Effort, Doctorbefund, gezielte Reparatur und erneuter guter Befund.
Echte Client-/Helper-/VS-Codeabnahmen isoliert; GUI nur privates Xvfb.
Default- und separate Lainbindung sowie Workspacekontext unterscheiden.
Während Diagnose keine Inferenz und keine unbeauftragten Betriebsänderungen.

Danach reguläre private Lieferung gemäß [Lieferplan](release-workers-2026-10-26.md):
frische Bestandsguards, gültiger Updatebeleg, konsistente Sicherung, Installation,
Clientexport und Aktivierungsprüfung. Echte Providerproben nur im bestehenden
konkreten autorisierten Budget oder als separat benannter Live-Prüfumfang;
`connect`/`doctor` dürfen nicht automatisch Inferenz freischalten. Ergebnisse
erhalten den tatsächlichen Quell-/Paketpin und offene Modellfehler.
**Owner:** Abnahme/Integrator; keine parallelen großen Builds oder Runtimewechsel.

## Reihenfolge und verbleibende Grenzen

AP1-Vertrag zuerst festhalten; dann AP1, AP2 und AP3 in getrennten Dateibereichen.
AP0-Analyse kann daneben laufen. AP4 integriert die gemeinsamen CLI-/Paketdateien;
AP5 prüft den finalen Stand und vollzieht erst danach die lokale Lieferung.

Kein zusätzlicher Account-/Modellskill in dieser Runde. Neue Provider,
beliebige Harnessadapter, KI-Recherche, Classifier/Approver, automatische
Zerlegung, Budgetoptimierung und allgemeine Skillupdates bleiben außerhalb.
Bestehende Kontingent-/History-/UI-/Betriebsfunktionen werden erhalten und am
neuen Paket passend gegengeprüft. Öffentliche Lizenz-/VSIX-Verteilung und die
vollständige reale Modellabnahme bleiben eigene Releasegrenzen. Der Plan
verkleinert diese bestätigten Anforderungen nicht.
