# Positionierung und Namensrecherche

Stand: **24. Oktober 2026**. Vorschlag zur Entscheidung, keine beschlossene
Umbenennung. Grundlage sind README, Produktumfang und der aktuelle
UI-Strukturauftrag. Alle externen Quellen wurden am 24.10.2026 abgerufen;
Fremdprodukte wurden anhand ihrer öffentlichen Primärquellen eingeordnet,
nicht installiert oder gegeneinander getestet.

## Empfehlung

**Die App als persönliche Desktop-Verwaltung für eigene AI-Zugänge positionieren:
Accounts und Kontingente überblicken, Modellprofile wechseln und die Nutzung
des eigenen lokalen Proxys nachvollziehen.** Der erste klare Adressat ist ein
Linux-Entwickler, der mehrere Zugänge und Coding-Clients benutzt.

Für die derzeitige Marke sofort verwendbarer Entwurf:

> **lain — Local AI Gateway**
>
> Deine AI-Accounts. Deine Modelle. Ein lokaler Zugang.
>
> Verwalte deine eigenen AI-Accounts, behalte Kontingente und Resets im Blick
> und nutze Modellprofile über einen lokalen Proxy. Mit Desktop-Oberfläche,
> CLI und dauerhafter Nutzungsübersicht. Linux zuerst.

Wenn der Name vor einer öffentlichen Veröffentlichung verändert werden soll,
ist **Lain Relay** mein bevorzugter Kompromiss: Die bisherige Identität bleibt
erkennbar, der Zusatz beschreibt die Vermittlungsfunktion, und der vollständige
Name lässt sich gezielter suchen. Für eine vollständig eigenständige Marke
ist **Muxmori** der interessanteste noch offene Kandidat dieses Durchgangs;
er braucht allerdings einen Verständlichkeits- und Erinnerungstest.

Ein Untertitel verbessert die Einordnung. Er löst **nicht** alle Probleme des
Namens `lain`: Ein anderes AI-Entwicklerwerkzeug verwendet bereits genau
diesen CLI-Befehl. Bei einer späteren öffentlichen Distribution gehören
Produktname, Paketname und ausführbarer Befehlsname deshalb in dieselbe
Entscheidung. Hier wurde nichts umbenannt.

## Warum „lain“ allein schwer einzuordnen ist

Es gibt eine starke, ausdrücklich gewünschte kulturelle Verbindung zu
[Serial Experiments Lain](https://www.serial-experiments-lain.com/). Sie trägt
Stimmung und Wiedererkennung für Fans, erklärt aber den Nutzen der App nicht.
Darüber hinaus existieren konkrete Verwechslungen im relevanten Umfeld:

| Bereits verwendeter Name | Beleg und Bedeutung für dieses Produkt |
| --- | --- |
| **Lain**, MCP-Codegraph für Coding-Agenten | [spuentesp/lain](https://github.com/spuentesp/lain) richtet sich ebenfalls an Nutzer von AI-Coding-Werkzeugen. Das README installiert den Befehl `lain` und beschreibt ausdrücklich mögliche `EEXIST`-Konflikte mit fremden gleichnamigen Dateien. Das ist auch ein Distributionsproblem. |
| **Lain Agent / Lain Cloud** | [lainagent.ai](https://lainagent.ai/) bewirbt einen lokalen Windows-Agenten und einen Cloud-Workspace. „Lain AI“ allein grenzt unser Produkt daher nicht zuverlässig ab. |
| **Lain OS** | [lain-os.com](https://lain-os.com/) positioniert sich als Agent und Desktop für Finanz-/Blockchain-Aufgaben. Erneut überschneiden sich AI und Desktop, auch wenn der Anwendungszweck anders ist. |
| **Lain**, lokaler Medienserver | [enrell/lain](https://github.com/enrell/lain/blob/main/README.md) verwendet `lain` als CLI und verweist auf einen Linux-Desktop-Client. „Lain Linux“ oder „Lain Desktop“ sind dadurch ebenfalls unscharf. |

Das belegt Namensmehrdeutigkeit, keine gemessene Suchmaschinenposition und kein
Suchvolumen. Die Behauptung „mit lain kann man nicht gefunden werden“ wäre zu
absolut. Ein konsequent verwendeter vollständiger Name, ein beschreibender
Seitentitel und ein klares Repository können trotzdem auffindbar werden.
„Lain AI“, „Lain Desktop“ und „Lain OS“ wären hier besonders schwache Zusätze.

## Markt: Der konkrete Nutzen muss sichtbar werden

Die engsten Vergleiche sind bereits Desktop-Apps rund um eigene Providerzugänge.
Ein Vergleich nur mit großen Cloud-Gateways würde die tatsächliche Konkurrenz
unterschätzen.

| Produkt | Öffentliche Positionierung/Funktionen | Konsequenz für lain |
| --- | --- | --- |
| [Quotio](https://www.quotio.dev/) | Mac-App für Quoten, Accounts und Coding-Agents; verwaltet CLIProxyAPI. Die Website beschreibt zusätzlich eine Linux-CLI. | „Accounts + Quoten + lokaler Proxy“ ist eine etablierte Produktkategorie. Ein guter Ausgangspunkt für die Erklärung, kein Alleinstellungsbeweis. |
| [ProxyPal](https://github.com/heyhuynhgiabuu/proxypal) | Desktop-Hülle für CLIProxyAPI mit eigenen Abos, Usage, Request-Monitoring und Client-Einrichtung; Linux x64 wird ausdrücklich angeboten. | Sehr naher Vergleich. „Linux“, „Desktop“ und „Usage“ allein reichen zur Abgrenzung nicht. |
| [VibeProxy](https://github.com/automazeio/vibeproxy) | macOS-Menüleisten-App für vorhandene Abos und Coding-Tools; CLIProxyAPI, mehrere Accounts und Routing. | Zeigt eine verständliche Nutzenkommunikation über vorhandene Zugänge. Eine pauschale Kostenersparnis sollte lain daraus nicht übernehmen. |
| [CC Switch](https://github.com/farion1231/cc-switch) | Providerwechsel, Routing/Aggregation, Quoten, Usage sowie MCP, Skills und Prompts; Windows, macOS und Linux. | Sehr breiter Mitbewerber. Lain sollte einen einfachen, belegbaren Arbeitsablauf zeigen, statt ebenfalls „All-in-One“ zu behaupten. |
| [CodexBar](https://github.com/steipete/CodexBar) | Sichtbare Nutzungslimits, Resets und Spend; das aktuelle README dokumentiert auch eine Linux-Desktop-App. | Wer nur Restkontingente sehen möchte, hat schlankere Alternativen. Lains Mehrwert beginnt beim Zusammenhang zwischen Accounts, Modellprofilen und tatsächlich vermittelter Nutzung. |
| [LiteLLM](https://docs.litellm.ai/docs/proxy/quick_start) / [Portkey](https://portkey.ai/docs/product/ai-gateway) | AI-Gateway-Kategorie mit Routing, Beobachtung und Verwaltungsfunktionen bis hin zu Team-/Produktionsbetrieb. | „AI Gateway“ ist ein brauchbarer Gattungsbegriff. „Persönlich“, „Desktop“ und „eigene Accounts“ grenzen den Anwendungsfall verständlicher ein als ein Enterprise-Versprechen. |

Diese Übersicht ist keine Aussage, dass Mitbewerbern bestimmte Detailfunktionen
fehlen. Dazu wäre ein separater praktischer Vergleich nötig. Auch native UI,
persistente Usage und Profile sind zunächst **eigene belegbare Stärken**, keine
bewiesenen exklusiven Eigenschaften.

## Zielgruppe und Kauf-/Nutzungsgrund

Die beste erste Zielgruppe sind technisch versierte Einzelpersonen mit mindestens
zwei AI-Zugängen oder mehreren Accounts: Entwickler, Berater und kleine Teams,
deren Mitglieder ihre Zugänge jeweils selbst verwalten. Ihr konkretes Problem
lautet: „Welcher Account hat noch Kontingent, welches Modellprofil verwende ich
gerade, und wohin ging meine Nutzung?“

Der zu demonstrierende Ablauf passt direkt zur neuen Navigation:

1. **Accounts:** Restkontingente und gemeldete Resets des eigenen Bestands sehen.
2. **Models / Profiles:** Gewünschte Zugänge auswählen und ein Profil anwenden.
3. **Usage:** Requests, Tokens und erfasste Performance nach Account/Modell prüfen.
4. **Live:** Einen aktuellen eigenen Request ansehen, wenn etwas unklar ist.

Dieser Ablauf gibt der Marke Inhalt. Die Katze und Mint auf dunklem Hintergrund
machen die App wiedererkennbar; die ersten Screenshots müssen gleichzeitig echte
Quotenwerte und den Wechsel von Profil zu Usage erklären. Ein abstraktes Logo
oder ein Anime-Slogan kann das nicht allein leisten.

Der jetzige Stand sollte als **Linux-first Desktop-App mit lokalem LLM-Proxy**
beschrieben werden. „Lokal“ bezeichnet Betrieb und Verwaltung des Proxys:
Modellanfragen gehen weiterhin an die jeweils eingerichteten Anbieter. Es ist
kein Versprechen von Offline-Inferenz oder vollständig lokal bleibenden Prompts.

Die Produktkommunikation bleibt bei der aktuellen Qualifikation:

- Nicht „funktioniert mit jedem Client“; konkrete getestete Anschlüsse nennen.
- Die 15 zusätzlichen Adapter sind lokal mit Testanbietern geprüft und ohne
  echte Zugangstests weiterhin `not_tested`. Diese Zahl nicht als 15 produktiv
  verifizierte Integrationen verkaufen.
- Automatische Kontingentoptimierung, autonome Modellwahl und garantiert
  unterbrechungsfreie Fallbacks sind noch kein heutiges Leistungsversprechen.
- Usage ist die erfasste Proxy-Nutzung; nicht automatisch die gesamte Nutzung
  eines Accounts außerhalb von lain. Live ist eine begrenzte laufende Ansicht,
  kein vollständiges dauerhaftes Gesprächsarchiv.
- Lizenz, Preis und Geschäftsmodell bleiben offen. Die Entwürfe versprechen
  weder „kostenlos“ noch „Open Source“, unbegrenzte Nutzung oder Einsparungen.

## Namensoptionen

Alle neuen Namen sind Vorschläge. Die Bewertung ist redaktionelles Urteil,
kein Ergebnis einer Nutzerstudie. Die Basissuche umfasste exakte Websuchbegriffe,
GitHub-Repositories über die öffentliche API und ausgewählte `.com`-RDAP-Abfragen.

| Option | Warum sie passt | Schwäche / Prüfbefund | Einschätzung |
| --- | --- | --- | --- |
| **lain — Local AI Gateway** | Kleinster Eingriff; etablierte persönliche Identität plus verständliche Kategorie. Im App-Fenster bleibt `lain` möglich. | Bekannte Lain-Verwechslungen und CLI-Konflikt bleiben. Der vollständige Zusatz muss außerhalb der App konsequent erscheinen. | Beste unmittelbare Kommunikationsverbesserung ohne Markenwechsel. |
| **Lain Relay** | Behält die gewünschte Referenz und erklärt Vermittlung. Leicht zu sprechen; nicht an einen Provider oder nur Quoten gebunden. | „Relay“ allein ist breit. „Local AI Gateway“ bleibt als Untertitel nötig. Kein einschlägiger exakter Webtreffer und kein `lainrelay`-Repository bei der Abfrage; `.com`-RDAP lieferte 404. | **Favorit für eine behutsame Namensweiterentwicklung.** |
| **Muxmori** | Eigenständiges Kunstwort; `mux` deutet auf mehrere zusammengeführte Zugänge. Kurz genug für App, CLI und ein Maskottchen. Nicht auf Quoten oder eine Plattform begrenzt. | Funktion ist ohne Untertitel unklar; Aussprache und Merkbarkeit testen. Kein einschlägiger exakter Webtreffer, kein Repository mit diesem Namen bei der Abfrage; `.com`-RDAP 404. | Interessanteste offene Richtung für eine völlig neue Marke, noch kein fertiger Sieger. |
| **Mintmux** | Verbindet die bestehende Farbidentität mit einem technischen Hinweis auf Multiplexing. Knapp, gut als CLI schreibbar. | `mux` ist erklärungsbedürftig; „Mint“ kann an Linux Mint oder Finanz-/NFT-Produkte erinnern. Kein Repositorytreffer, aber **mintmux.com ist registriert**. Die Website ließ sich im Webwerkzeug nicht öffnen. | Reservekandidat; schwächer als Lain Relay und Muxmori. |

RDAP-Belege: [lainrelay.com](https://rdap.verisign.com/com/v1/domain/lainrelay.com),
[muxmori.com](https://rdap.verisign.com/com/v1/domain/muxmori.com),
[mintmux.com](https://rdap.verisign.com/com/v1/domain/mintmux.com).
Für Mintmux meldete die Registry eine Registrierung vom 01.04.2026.
Eine 404-Antwort ist nur das Ergebnis dieser Registry-Abfrage; sie ist **keine
Registrierungszusage**. Die Recherche ermittelt weder Domain-Verfügbarkeit bei
einem Registrar noch Markenrechte. Andere TLDs, Paketregister und Markenregister
wurden nicht vollständig geprüft.

Einige zunächst passend klingende Ideen wurden bewusst verworfen:

- **Purrmux:** Sehr guter Katzen-/Technikbezug, aber bereits ein
  [Kitty-/Terminalprojekt](https://github.com/DimitrisMilonopoulos/purrmux).
- **QuotaPaw:** Bereits ein
  [lokaler Claude-/Codex-Usage-Monitor mit Pixelkatze](https://github.com/CoderLineChan/QuotaPaw).
- **QuotaLoom:** Bereits ein
  [lokales Desktop-Usage-Dashboard](https://github.com/EricsmOOn/quota-loom/blob/main/README.en.md).
- **ModelPier / ModelMoor:** Bereits
  [AI-Gateway](https://modelpier.com/) beziehungsweise
  [lokale Modell-API-App](https://github.com/Kingfish404/ModelMoor).
- **RelayCat:** Bereits im
  [AI-Coding-Umfeld](https://github.com/JackyZhang8/relaycat-core) und als
  [Gateway-Name](https://www.relaycat.best/) verwendet.

Purrmux und QuotaPaw wurden erst durch die direkte GitHub-API-Abfrage gefunden,
obwohl die ersten exakten Websuchen keinen passenden Treffer lieferten. Das ist
ein konkreter Grund, Suchabwesenheit nicht als „Name frei“ zu behandeln.

## Konkrete Texte und Metadaten

Die folgenden Entwürfe verwenden den aktuellen Namen und können bei einer
Namensentscheidung einheitlich ersetzt werden. Sie wurden nicht veröffentlicht.

**Englischer Seitentitel / README-Titel**

> lain — Local AI Gateway for Linux

**Englische Kurzbeschreibung**

> A Linux-first desktop app and local LLM proxy for your own AI accounts,
> quota windows, model profiles and persistent usage tracking.

**Deutsche Kurzbeschreibung**

> Verwalte eigene AI-Accounts, Kontingente und Modellprofile über einen lokalen
> Proxy — mit Desktop-Oberfläche und dauerhafter Nutzungsübersicht für Linux.

**Repository-Beschreibung**

> Linux-first desktop app and local LLM proxy: AI accounts, quotas, model
> profiles and persistent usage.

**Sinnvolle Repository-Topics**

`ai-gateway`, `llm-proxy`, `cliproxyapi`, `quota-monitor`, `usage-tracking`,
`model-routing`, `linux`, `desktop-app`, `avalonia`.
Clientnamen als Topics nur ergänzen, wenn der konkrete Anschluss qualifiziert
und dokumentiert ist. Ein Providerkatalogeintrag allein genügt dafür nicht.

**Desktop-Eintrag, Textentwurf**

```ini
Name=lain
GenericName=AI Account and Proxy Manager
Comment=Manage AI accounts, quotas, model profiles and proxy usage locally
Keywords=AI;LLM;Proxy;Accounts;Quota;Usage;Profiles;
```

Die eigentliche App-Marke bleibt im Launcher kurz. `GenericName`, `Comment` und
Suchwörter erklären, wofür sie da ist. Bei einem Namenswechsel sollte dort der
vollständige neue Markenname stehen; ein bloßes `Name=Lain Relay` bei dauerhaft
unverändertem Paket-/CLI-Namen wäre keine vollständige Konfliktlösung.

Das README sollte oben Nutzen, Zielgruppe, aktuellen Linux-Status und einen
echten Accounts-Screenshot zeigen; anschließend ein kurzer Ablauf von Verbindung
zu Profil und Usage. Interne Prüfzahlen, Architekturdetails und historische
Qualifikationsstände gehören weiter unten beziehungsweise in verlinkte Dokumente.
Die Prüfgrenzen bleiben leicht erreichbar. Zum Start reicht eine eindeutige
Produktseite unter einer bereits kontrollierten Domain, etwa
`grassmann-it.de/lain`; eine neue Wunschdomain ist keine Voraussetzung.

## Wie Nutzer die App entdecken könnten

Die relevanten Suchanliegen beschreiben ein Problem, nicht den noch unbekannten
Markennamen. Priorisierte Themen für reale Dokumentation und Demos:

| Suchanliegen | Dazu passende Seite / Demonstration |
| --- | --- |
| `Linux AI account manager`, `local AI gateway desktop` | Kurze Produktseite mit lokalem Betriebsmodell und einem aktuellen Screenshot. |
| `Codex quota monitor Linux`, `AI subscription quota tracker` | Accountkacheln mit echten Zeitfenstern und Erklärung von unbekannten/veralteten Werten; klarer Hinweis auf zusätzliche Proxy-/Profilfunktionen. |
| `CLIProxyAPI GUI Linux` | Architektur-/Betriebsseite: eigener verwalteter Proxy, Einrichtung, Datenhaltung und Verhältnis zum verwendeten Core. |
| `LLM proxy usage analytics`, `AI model profile switching` | Konkreter Ablauf Profil → Request → Usage; die heute qualifizierten Clients benennen. |

Das sind Hypothesen zu passenden Suchintentionen, keine gemessenen
Traffic-Prognosen. Ein kurzer demonstrierter Arbeitsablauf dürfte für diese
Zielgruppe hilfreicher sein als viele generische SEO-Seiten.

Für einen später autorisierten Start bieten sich das CLIProxyAPI-Ökosystem,
Linux-Entwicklergemeinschaften und die jeweiligen qualifizierten Client-Communities
an. Eine spätere Einreichung in passende Projektlisten ist sinnvoller, wenn eine
installierbare Version und eine saubere Anschlussmatrix vorliegen. Es wurden
keine Nachrichten, Einreichungen oder Veröffentlichungen vorgenommen.

Vor einer endgültigen Namenswahl reichen zunächst drei Varianten auf demselben
Screenshot: `lain — Local AI Gateway`, `Lain Relay — Local AI Gateway` und
`Muxmori — Local AI Gateway`. Fünf passende Nutzer sollten nach kurzem Ansehen
sagen können, wofür die App da ist, und den Namen später schreiben beziehungsweise
suchen können. Das prüft Verständlichkeit und Erinnerung; die technische und
rechtliche Namensprüfung bleibt davon getrennt.

## Prüfprotokoll

- Lokale Quellen: `README.md`, `AGENTS.md`, `docs/product-scope.md`,
  `docs/ui-structure-implementation.md`, `docs/brand-reference.md`;
  Arbeitskopiebasis `f06ff31`.
- Websuche: `lain software AI proxy github`, `"lain" "AI" gateway`,
  `"lain" software github`, lokale Proxy-/Quota-Desktop-Vergleiche sowie
  exakte Kandidatennamen mit `software OR github OR app` und getrennten
  Schreibweisen, soweit sinnvoll.
- Direkte öffentliche GitHub-API: `/search/repositories?q=<name> in:name`.
  Ergebnisse am 24.10.2026: `lainrelay` 0, `muxmori` 0, `mintmux` 0,
  `purrmux` 1, `quotapaw` 1. Die Suche deckt öffentliche Repositories im
  Namensfeld ab, keine vollständige Software-/Firmen-/Markenlandschaft.
- Öffentliche `.com`-Registry: Verisign RDAP, oben verlinkte Abfragen.
- Kontrollierte Aktualitätsstichprobe über GitHub-HEAD: spuentesp/lain zuletzt
  21.10.2026, ProxyPal 13.09.2026, Quotio 23.10.2026, VibeProxy, CC Switch
  und CodexBar 24.10.2026. Das sind Repository-Aktivitätsdaten, keine
  Veröffentlichungstermine einzelner Funktionen.
- Keine Builds, Installationen, Provider-/Credentialzugriffe, Käufe,
  Domainregistrierungen oder Änderungen an Produktbranding und UI.
