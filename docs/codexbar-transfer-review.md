# CodexBar: sinnvolle Übernahmen für lain

Quellvergleich vom 24.10.2026, ausdrücklich als Architektur-Sparring beauftragt.
Zwei Astra- und zwei Sol-Worker haben UI/UX, Providerarchitektur, Anbieterbestand
und Linuxfunktionen getrennt geprüft. Das ist ein **Transferentwurf**, keine
Freigabe sämtlicher vorgeschlagener Umbauten und kein Live-Anbieternachweis.
Das parallel laufende CLIProxyAPI-Upgrade bleibt ein eigener Auftrag.

Referenz: offizielles [steipete/CodexBar](https://github.com/steipete/CodexBar),
Commit **`d34fbb47afe73f81dd591bae28a60f72c39a9fe4`** (0.81.1 development).
Lain-Vergleich: `2f34b067bc9008bb97b38265ccdeff3ac48b522f`.
Keine CodexBar-Programme, Builds, Browserlogins oder privaten Anbieterabrufe
ausgeführt. Öffentliche API-Dokumentation wurde bei ausgewählten Kandidaten
zusätzlich geprüft. Dokumentationsangaben wurden am Detailcode gegengeprüft.

## Empfehlung

Die stärksten Übernahmen sind eine kleine Kontingentansicht, gemeinsame
Objektaktionen und klar beschriebene Anbieterquellen. Die Umsetzung sollte die
vorhandenen C#-Dienste und Avalonia-Komponenten nutzen. Ein vollständiger Import
von Swift-/Qt-/Pluginlaufzeiten würde für diese Verbesserungen zusätzliche
Technik einführen, ohne den ersten Monitorumfang zu vereinfachen.

CodexBar besitzt inzwischen eine offizielle **Linux-Qt6-Oberfläche**, nicht nur
eine macOS-Menüleiste: [Linux-README](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Integrations/Linux/README.md),
[Mac/Linux-Vergleich](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Integrations/Linux/MAC_COMPARISON.md).
Die Linuxoberfläche nutzt den gemeinsamen Core/CLI für Anbieterabrufe und besitzt
Polling, Tray, eine Quick View und private lokale Snapshots. Sie ist kein
Inferenzproxy. Das ist eine passende Referenz für den bestätigten
[lain-Monitor ohne Proxy](monitor-mode.md).

## UI/UX und Funktionen

| Priorität | Übernahmevorschlag | Was davon neu wäre / wichtige Grenze |
| --- | --- | --- |
| Zuerst | Wirklich kleine Barebones-/Kontingentansicht, auch vom Tray erreichbar | Schmal und einkolumnig statt heutiger Mindestgröße 800×560; vorhandene Daten und Kacheln verwenden. Keine zweite Overview-Seite. Alle gemeldeten Fenster bleiben sichtbar. Im Gateway bleiben Profiles und Usage direkt erreichbar; Monitor zeigt nur tatsächlich eingerichtete Fähigkeiten. |
| Zuerst | Gemeinsame Commands für Rechtsklick, sichtbares `⋯` und Tastatur | Refresh, Pin, Usage, Models, Check, Enable/Disable existieren schon. Neu sind gemeinsame Commanddefinitionen und bessere Zugänge statt zusätzlicher Buttonreihen. Stabile Objekt-ID erfassen und vor Ausführung aktuellen Stand prüfen. |
| Zuerst | `Reset in 1h 35m · 17:40` | Relative Anzeige ergänzt genauen Zeitpunkt. Ablauf bedeutet weiterhin „Reset erwartet · erneut prüfen“, nicht bestätigte Erneuerung. |
| Zuerst | Konfigurierbare Quotaschwellen und bestätigte Erholungsmeldungen | Lain hat bereits feste 20%-Meldung, Cooldown, Shared-Pool-/Fensterdedup und separate 429-Meldungen. Bestehende Identität/Fehlerbehandlung erhalten. Ein Timer allein darf keine Resetmeldung erzeugen. |
| Danach | Kurze Fehlerzusammenfassung, bereinigte Details/Kopieren und Anbieterportal am Account | Kataloglinks sind vorhanden; direkter Console-Kurzzugang am Account bisher vor allem für MiMo. Klassifizierte Fehler helfen stärker als rohe Fehlermeldungen. |
| Danach | Öffentlicher Anbieterstatus | Eigene Quelle/Messzeit; unabhängig von Accountgültigkeit, Quotaabruf und lokalem Hoststatus. Zunächst wenige belegte Statusfeeds. |
| Danach | Diagnoseexport aus vorhandenen Snapshots | App/Host/Core-Versionen, Adapter, Quelle, Alter und klassifizierter Fehler. Keine pauschalen Logs/Configdateien. CodexBars Diagnose führt echte Fetches aus; für lain erneuten Abruf ausdrücklich als eigene Aktion anbieten. |
| Später | CLI-Guard und Panel-/Waybar-Snapshot | Expliziten Account und echtes Fenster auswählen; ausreichend/unter Schwelle/unbekannt getrennt. Keine Kapazitätsreservierung oder automatische Umleitung. Gemeinsame Host-/CLI-Projektion, kein zweiter Poller. |
| Später | Datenschutzdarstellung | Accountnamen für Screenshots redigieren, Kontingente erhalten. Separate Präferenz; Barebones ist kein Datenschutzmodus. |
| Später | Verbrauchstempo/Burndown | Braucht belegte Fensterdauer/-startzeit und gegebenenfalls Quotasamples. Aus Labeln oder Quotadifferenzen keine Tokenhistorie erfinden. |

Belege für die ersten UI-Transfers:

- [Linux QuickView.qml](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Integrations/Linux/qml/QuickView.qml#L6)
  und [Trayaktionen](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Integrations/Linux/main.cpp#L151).
- [Kompakte Accountzeile](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBar/MenuCardCompactAccountRow.swift#L90)
  und [Aktionsdeskriptor](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBar/MenuDescriptor.swift#L24).
  Ein umfassendes Objektkontextmenüsystem ist dort nicht belegt; dessen Ausbau
  ist eine eigene lain-Empfehlung, unterstützt durch MeowChan.
- [Relative/absolute Resetformatierung](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/UsageFormatter.swift#L151).
- [Quota-Transitionsreducer](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBar/SessionQuotaNotifications.swift#L155),
  [Providerstatus](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/ProviderStatusFetcher.swift#L45),
  [Diagnoseexportmodell](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Providers/ProviderDiagnosticExport.swift#L18).

Lain besitzt bereits vollständige Accountfenster, Pins/Suche/Filter, stabile
bewusste Sortierung, Traytooltip/-menü, Settings/About und kompakte CLI. Die
relevanten Anschlussstellen sind `MainWindow.AccountTiles.cs`,
`MainWindow.Accounts.cs`, `MainWindow.Settings.cs`,
`Tray/TrayWorkspaceController.cs` und `Tray/DesktopSignalMonitor.cs` unter
`src/Lain.Desktop/`. Diese vorhandenen Funktionen sind keine neuen Featurelücken.

**Nicht übernehmen:** CodexBars
[Smart-Compact-Planer](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/AccountMenuLayoutPlanner.swift#L80)
faltet ab vier Accounts gesunde Einträge, sortiert nach knappstem Rest und begrenzt
Fensterdetails. Das widerspricht den vollständigen lain-Kacheln und ihrer stabilen
Sortierung. Auch die große CodexBar-Settingsstruktur und ein Tray-Gesamtmeter aus
den ersten zwei Fenstern des ersten Providers passen nicht ohne eigene Auswahl.
Gateway-Profilwechsel bleiben von Accountauswahl und gespeichertem Stand getrennt;
Enable/Visible/Apply sind keine austauschbaren Aktionen.

## Anbieterarchitektur

CodexBar trennt [Descriptor/Registry](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Providers/ProviderDescriptor.swift#L335)
und [geordneten Fetchplan](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Providers/ProviderFetchPlan.swift#L239).
Jede Strategie meldet verfügbar/übersprungen/fehlgeschlagen/erfolgreich sowie
klassifizierte Auth-, Berechtigungs-, Rate-Limit-, Netzwerk- oder Parserfehler.
Das ist als C#-Konzept nützlich; UI-Metadaten und Quota-/Gatewayfähigkeiten sollten
in lain getrennt bleiben.

Lain registriert derzeit acht echte Quotaadapter: Codex, Command Code, Kimi, Z.ai,
StepFun, Poe, OpenRouter und Synthetic (`src/Lain.Host/Program.cs`). Ein Eintrag
im Modell-/Anbieterkatalog bedeutet noch keinen Quotaadapter oder Livezugang.
CodexBar hat erheblich mehr Provider-IDs, aber nicht überall echte Kontingente:
API-Guthaben, lokale History, Konfigurations-/Verbindungsproben und geschätzte
Denominatoren müssen getrennt eingeordnet werden. Die [vollständige Matrix mit 110 IDs](codexbar-provider-inventory.md) hält diese
Unterschiede pro registriertem Zugang fest.

Für zusätzliche Quellen sollte lain folgende vorhandene Garantien behalten:

- **Unbekannt bleibt `null`.** CodexBars generisches `RateWindow.usedPercent`
  verlangt eine Zahl und ergänzt Placeholder-/Known-Flags. Diese Fälle nicht als
  gemessene 0% importieren. [Quellmodell](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/UsageFetcher.swift#L3).
- **Quellenwechsel benötigt gleiche Identität.** API, OAuth, CLI und Browser
  müssen denselben Account, dasselbe Produkt und Budget meinen. CodexBar sperrt
  seinen CLI-Fallback beispielsweise bei ausgewähltem Codex-Workspace.
  [Codex-Fetchplan](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Providers/Codex/CodexProviderDescriptor.swift#L150).
- **Credentialbesitz bleibt explizit.** CodexBar trennt eigene und fremde
  Refresh-Tokens ebenfalls. Lains externer Snapshot darf auch nicht über einen
  neu eingeführten CLI-Aufruf heimlich erneuert werden. Ein solcher Zugriff wäre
  eine andere separat gewählte Zugangsart.
- **Poolscope, Messzeit und Abdeckung bleiben erhalten.** CodexBars
  exact/estimated/percentOnly/unknown ersetzt keine lain-Budgetpoolidentität.
  Einen alten Reset nicht mit der neuen Prozentmessung als frisch ausgeben.
  Letzten Erfolg behalten, fehlgeschlagenen aktuellen Abruf trotzdem anzeigen.
- **Fensteridentität von Quellenpfad trennen.** Der heutige Quota-Cache gleicht
  über `Reading.Source` ab. Mehrere Strategien brauchen vorher stabile Fenster-IDs,
  sonst entstehen doppelte oder falsch zusammengeführte Fenster.

Eine JS-Providerdatei ist keine unabhängige Integration. CodexBars
[Pluginmanifest](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Plugins/ProviderPluginManifest.swift#L35)
und [Snapshotmapper](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Plugins/ProviderPluginSnapshotMapper.swift#L37)
benötigen deklarierte Hosts/Auth/Cookies/Storage, Host-HTTP, Grenzen und eine
Engine wie QuickJS. Einen kompatiblen .NET-Pluginhost jetzt einzuführen wäre ein
zusätzliches Vorhaben. Browsercookie-/WebKit-/Keychainpfade sind teilweise
macOS-spezifisch; unter Linux reicht der gleiche Parser allein nicht.

## Konkrete erste Anbieter

Für Kontingente und Resets sind **Ollama** (monatliche Included-Allowance plus
separate Kaufcredits), **ClinePass** (5h/Woche/Monat) und **MiniMax Coding**
interessant. **DeepSeek und Moonshot** sind kleine reine Balanceadapter; daraus
darf keine Prozentquote entstehen. **ElevenLabs** liefert Zeichenallowance und
Reset, falls Audiokonten zum gewünschten Umfang gehören. **Chutes/Neuralwatt**
liefern weitere echte Quotaoberflächen bei passendem Zugang. Details,
Berechtigungen und feste Quellen stehen in der Matrix.

Bei vorhandenen Anbietern sind OpenRouter-Accountcredits zusätzlich zu Keycaps
und tatsächlich gelieferte Codex-/Kimi-Fenster besonders naheliegend. StepFun
ist ein gutes Gegenbeispiel für oberflächliches Matching: lain liest die
offizielle PAYG-Balance, CodexBar den separaten Step-Plan über die Konsole.
Fireworks liefert im geprüften CodexBar-Pfad vor allem Ausgaben; eine
registrierte Integration beweist keine passive Restquota.

## Kleine technische Folge

1. **Quota-Vertrag und Fehlerprojektion präzisieren:** stabile Fenster-ID,
   Quellenkennung, strukturierter Abruffehler, Account-/Produkt-/Poolscope und
   getrennte Erhebung/Frische. Bestehende nullable und partielle Werte erhalten.
2. **Schlankes C#-Anbietermodul an vorhandenen Adaptern erproben:** Credentials,
   Quotas und Gatewayrouting als getrennte Fähigkeiten; kein dynamischer
   Pluginhost nötig. Eine direkte neue API und ein ausdrücklich lesender
   Client-Access-Snapshot zeigen anschließend, ob das Modell trägt.
3. **Monitor und kleine Ansicht auf dieselbe Datenquelle setzen:** kein Proxy
   für Quotas nötig; kein zweiter Collector. Die vorhandenen Loginabhängigkeiten
   vom Core im Monitor bewusst auflösen, nicht `ProxyReady=false` als Monitorwahl
   missverstehen. Gemeinsame Objektcommands erlauben Tray/`⋯`/Rechtsklick.
4. **Abrufplanung verbessern:** Retry-After/Cooldowns und einmaliger Abruf kurz
   nach einer Resetgrenze. Adaptive 2/5/15/30-Minuten-Intervalle erst nach Trennung
   von Abrufintervall und Frische; lain markiert derzeit nach fünf Minuten stale.
   [CodexBar Resetplanung](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBar/UsageStore%2BResetBoundaryRefresh.swift#L45).
5. **Browser-/CLI-Fallbacks und Pace später gezielt:** nur für konkrete Anbieter
   mit belegtem Mehrwert. Sie sollen einen ersten guten Quota-Monitor nicht blockieren.

Alternative für einen begrenzten Versuch wäre eine gepinnte CodexBar-CLI als
externe Datenquelle. Dafür fehlen noch eine qualifizierte JSON-Abbildung,
Scope-/Credentialzuordnung, Nebenwirkungsregeln und Aktualitätsvertrag. Deshalb
ist sie eine Untersuchungsoption, keine bereits passende universelle Integration.

## Übernahme und Herkunft

Der CodexBar-Code steht unter [MIT](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/LICENSE).
Bei direkter Codeübernahme bleiben Copyright-/Lizenzhinweise erhalten. Kleine
deterministische Kerne wie Transitionreducer, Resetplanung oder Guardentscheidungen
lassen sich in C# übertragen, ohne Swift-/Qt-Laufzeit in lain einzubauen.

Providerlogos haben eigene dokumentierte Herkunft;
[die Artworkdokumentation](https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/docs/provider-brand-icons.md)
unterscheidet erlaubte Quellen und ungeklärte Bilder. Konkrete Logos anhand dieser
Primärquellen prüfen; lains Katzenlogo bleibt unabhängig davon bestehen.
WidgetKit/App-Groups, NSStatusItem und Sparkle sind macOS-spezifisch. Der offizielle
Linuxdesktop liefert keinen automatischen Updater, den lain einfach übernehmen könnte.
