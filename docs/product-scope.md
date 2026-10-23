# Produktziele

Stand: 23.10.2026. Grundlage sind die ausdrücklichen Nutzerwünsche und Antworten
im Architektur-Sparring. Dieser Stand beschreibt Ziele; eine fertige Architektur
oder Implementierung wird damit nicht behauptet.

## Bestätigt

- Das Produkt heißt **lain**. Der zunächst genannte Arbeitstitel/Ordner
  `multitool` wurde entsprechend umbenannt.
- Menschen installieren lain **lokal und verbinden ihre eigenen Accounts**.
  Dies wurde auf die Frage nach dem Betriebsmodell ausdrücklich gewählt.
- Mehrwert für verschiedene **Harnesse** ist ein Schwerpunkt: Codex/ChatGPT
  Desktop im eigenen Alltag, Claude Code/Claude Desktop bei Kollegen und
  OpenCode als weiterer Terminal-Harness. Anbieterzugänge wie Z.ai sollen sich
  möglichst gut mit geeigneten Harnessen einrichten lassen. Die konkrete
  Anschlussfähigkeit jeder Oberfläche wird geprüft, nicht aus dem Namen abgeleitet.
- Accounts und Modelle sollen über einen gemeinsamen lokalen Proxy für geeignete
  Clients nutzbar werden. Modellmenüs wie `/model` sollen befüllbar sein.
- Die klickbare Desktop-Oberfläche wird mit **Avalonia** umgesetzt. Der Nutzer
  hat den bisherigen Kandidaten im Zuschnitt der Arbeitspakete konkret gewählt.
- **Hintergrundbetrieb unabhängig von der UI** ist bestätigt: Usage wird weiter
  erfasst und Skills bleiben erreichbar, wenn das Fenster geschlossen ist.
- **Linux zuerst** wird tatsächlich abgenommen; Windows/macOS werden bei der
  Architektur berücksichtigt. Erste echte Zugänge sind **Codex-/ChatGPT-Abo
  und Command Code**.
- Entwicklung, Tests und paralleler Betrieb von lain sollen den **bestehenden
  persönlichen Proxy unangetastet lassen**. Eigene Prozesse, Ports, Konfiguration,
  Credentialspeicher und Usage-Erfassung sind dafür verbindlich. Gemeinsame
  Anbieterlimits bei späteren Live-Tests sind von lokaler Isolation zu unterscheiden.
- Viele Accounts, auch mehrere beim selben Anbieter, und weitere Anbieter sollen
  darstellbar und deren Kontingente abfragbar sein. Die gewünschte Größenordnung
  ist noch nicht quantifiziert.
- **Kontingente als aufklappbare Accountkacheln**: Benannte Zeitfenster,
  Verbrauch in Prozent, verfügbare Restmenge und gemeldeter Reset müssen bereits
  auf den geschlossenen Kacheln erkennbar sein. Eine Angabe wie „2 budget windows“
  ersetzt diese Werte nicht. Ein Klick öffnet weitere Details und Accountaktionen.
  Unbekannte Werte bleiben sichtbar unbekannt; unterschiedliche Budgets werden
  nicht addiert. Diese Präzisierung des Nutzers vom 23.10.2026 ersetzt die
  zwischenzeitlich entworfene primäre Kontingenttabelle.
- Farben und geeignete lokal gebündelte Schriften folgen **grassmann-it.de**;
  die bisherige TUI dient als Referenz für nützliche Usage-Informationen.
  **GUI-Tests ausschließlich auf privatem Xvfb**, ohne Testfenster auf dem Desktop.
- **Historische Proxy-Usage** gehört in den Kern: Tokens und Requests nach Tag,
  Modell und weiteren sinnvollen Dimensionen, nachvollziehbare Summen und Verläufe.
  Ziel ist eine gut lesbare Übersicht ähnlich der geschätzten bisherigen Usage-
  Ansicht, ausbaubar über Harnesse und Anbieter hinweg.
- **Profile anlegen und exportieren** gehört zum gewünschten Produkt. Ein Profil
  soll insbesondere die Auswahl der Modelle im Client unterstützen. Der genaue
  Umfang eines Profils wird noch entworfen.
- Bei Einschätzungen zu Modelleignung und Kosten je Account soll es eine Aktion
  **„Mit KI recherchieren“** geben, um Informationen neu prüfen zu lassen und die
  zugrunde liegenden Quellen zu öffnen. Der Lieferzeitpunkt ist noch nicht festgelegt.
- Für Worker sind **eigene Rollen und allgemeine Regeln zur Routenwahl nach Zweck**
  gewünscht. Rollen und Regeln müssen anbieter-/modellneutral bleiben: etwa
  „wenn Security-relevant, verwende diese Route“ oder „E-Mails über jene Route“.
  Ziele sind frei konfigurierbare Zugänge, beispielsweise GLM, Kimi oder
  Opus/Sonnet über Command Code. Daybreak ist ein mögliches Beispiel.
  Auch das optionale Klassifikationsmodell wird über eine wählbare Route
  zugeordnet. Konkrete Regelauswertung, Ausführungsort und Lieferstufe bleiben offen.
- Für den Proxy möchte der Nutzer auf bestehende lokale Proxysoftware aufbauen
  und sie als Abhängigkeit mitliefern beziehungsweise voraussetzen. Der vorhandene
  CLIProxyAPI-Core ist der bevorzugte Prüfkandidat; eigenes Neuschreiben des
  Inferenztransports ist keine gewünschte Ausgangsrichtung. Exakte Version und
  Lieferform werden anhand des Bestands geprüft.
- Fallbacks von direktem Anbieter zu freigegebenem Drittanbieter und später eine
  gute Nutzung vorhandener Kontingente sind Produktziele.
- Inspektor/Stream-Mitlesen folgt **später**. Der aktuelle persönliche Inspektor
  wird vom Nutzer als noch deutlich fehlerhaft beschrieben.
- Anime-/Manga-Referenzen zu Lain und Ghost in the Shell sind gestalterisch
  ausdrücklich erwünscht.

## Aktueller erster Zuschnitt

Der Nutzer priorisiert zunächst Anschluss/Modellauswahl und Usage („1 und 2“,
einschließlich `/model`). Eine automatische Verteilung nach Kontingenten kommt
später. Den ersten benutzbaren Stand hat er nun ausdrücklich als **eigenen von
lain verwalteten Proxy plus Accountverwaltung plus persistente Usage** gewählt.
Eine reine Oberfläche für den bestehenden persönlichen Dienst erfüllt diesen
Zuschnitt nicht. Die [Arbeitspakete](implementation-plan.md) beginnen mit einem
begrenzten Backendnachweis; davon abhängige Teile sind als solche markiert.

Als spätere Regel wurde ausdrücklich beschrieben:

> Bei einem 429 das Kontingent prüfen. Ist das direkte Abo bestätigt erschöpft,
> über die konfigurierte Drittanbieterroute weiterarbeiten, während die gewählte
> clientseitige Route erhalten bleibt.

Offen ist insbesondere, in welchen Zuständen das transparent möglich sein muss:
erster Request, Folgeanfrage mit Anbieterzustand, Toolfortsetzung oder bereits
laufender Stream. Das gewünschte „nahtlos“ wird nicht als technisch schon belegt
behandelt. Die Lieferstufe eines einfachen Fallbacks bleibt beim Zuschnitt zu klären;
die umfassende Kontingentoptimierung ist ausdrücklich nachgelagert.

## Aktuelle Vorschläge, noch keine Architekturentscheidungen

- Gemeinsame .NET-Anwendungsbibliothek, eigenständiger lokaler Host und typisierte
  Clientbibliothek für Avalonia/CLI; Skills greifen zunächst über die CLI zu.
  Der [Architekturvorschlag](architecture.md) beschreibt Zuständigkeiten und
  Alternativen. Die Fachlogik soll gemeinsam genutzt werden.
- Ein **Wizard** für Accountverbindung, Modellwahl, Profil und Clientanbindung.
  Der Wizard läuft in lain; daraus entstehen passende Clientkonfigurationen.
- Ein optionaler **KI-Chat** kann dieselben Einstellungen in natürlicher Sprache
  bearbeiten. Die klickbare Einrichtung soll vollständig eigenständig funktionieren.
- Eine **Skill-Bibliothek mit Projektarbeitsweisen** wurde vom Nutzer zur Prüfung
  eingebracht, besonders für selbstständige Parallelisierung und Modellwahl je
  Workeraufgabe. Der beigefügte Ablauf mit einmaliger Projektabstimmung ist eine
  Entwurfsgrundlage; konkrete Defaults, Workerzahlen und Lieferstufe sind offen.
  Bibliotheksinhalte, dauerhafte Projektregeln und Harness-Agentenkonfiguration
  sollen im Entwurf zusammenspielen. Ein eigener Scheduler ist nicht beschlossen.
  Für den Worker-Einstieg bevorzugt der Nutzer den Namen **`lain:workers`**;
  weitere Aufrufe und ihre konkrete Syntax je Harness werden noch entworfen.
- Ob ein eigener **Approver** mit gewähltem Modell an die automatischen Freigaben
  eines Harness angeschlossen werden kann, wird auf Nutzerwunsch geprüft.
  Native Modellwahl und eigene Hookintegration sind unterschiedliche Möglichkeiten;
  ein allgemeiner Austausch von „Approve for me“ ist noch nicht zugesagt.
- Profile bündeln eine nachvollziehbare Modellauswahl und Clientdarstellung;
  Routen-/Fallbackregeln können später hinzukommen. Exportformat und Aktivierung
  sind noch offen.
- OpenCode-spezifische Einrichtung und eine gemeinsame Nutzungsauswertung über
  mehrere Harnesse. Ein Funktionsvorsprung gegenüber OpenCode ist noch kein
  belegtes Ergebnis; dessen vorhandene Funktionen werden in der Anschlussmatrix erfasst.

## Noch zu entscheiden

- Welche konkreten Clientbuilds qualifiziert der erste Nachweis? Codex CLI ist
  als erster Referenz-Harness vorgeschlagen; weitere Harnesse folgen demselben Vertrag.
- Gewählter CLIProxyAPI-Build und dauerhafter Usage-Anschluss samt Accountidentität
  werden im Backendnachweis entschieden.
- Umfang und Portabilität eines Profils; mehrere gleichzeitig aktive Profile und
  Bindung an Clients/Projekte statt eines einzigen globalen Modellmenüs.
- Linux-Installation, Update-/Rückwechselverfahren und spätere Betriebssystemadapter.
- Prioritäten einer späteren Auswahlregel: Qualität, erste Partei, verfallendes
  Kontingent, Kosten, Latenz; erlaubte Modellwechsel und Fortsetzungsgrenzen.
- Lizenz/Veröffentlichungsform des eigenen Produkts.

Der aktuelle Auftrag umfasst Repositoryeinrichtung, Bestandsaufnahme und Sparring.
Eine Produktimplementierung oder Veröffentlichung ist damit noch nicht beauftragt.
