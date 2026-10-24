# Kontingente und Anmeldung ohne Keykopieren

Stand: **24.10.2026**, Quellbasis lain **`5dc916f`**. Ergebnis von **sechs GPT-6.1-Sol-Workern und zwei Astra-Workern** mit anschließender Zusammenführung und Gegenprüfung. Alle **20 aktuellen Kataloganbieter plus Xiaomi MiMo** sind abgedeckt; Produktvarianten sind in den Einzelberichten getrennt. Codex, Command Code, Kimi und Z.ai bleiben bestehende Referenzanschlüsse.

Der anschließende Nutzerauftrag zur grundlegenden Einrichtung wird im
[Account-Connect-Entwurf](account-connect-redesign-2026-10-24.md) behandelt.
Er ergänzt auch die vier bestehenden Referenzanschlüsse und trennt die
Anmeldung von Modellwahl, Tarifvoraussetzungen und Monitoringrechten.

**Recherche abgeschlossen; neue Integrationen sind damit noch nicht implementiert oder live geprüft.** Geprüft wurden öffentliche Herstellerdokumentation, offizielle SDK-/CLI-Quellen, ausgelieferte öffentliche Console-Bundles und der vorhandene lain-Code. Keine privaten Kontositzungen, Zugangsdaten, Logins, Anbieterregistrierungen, Käufe oder authentifizierten Providerabfragen. Installation, Accounts, Routen, Historie und Dienste wurden nicht verändert. Andere laufende Providerrecherche bleibt erhalten.

## Ergebnis

**Ja: „Anmelden, lain übernimmt den Rest“ ist für mehrere Anbieter ein realistischer Weg.** Die bisherige Einordnung fehlender Kontingente war teilweise zu pauschal. Es gibt übersehene APIs, offizielle CLI-Anmeldungen und konkrete Consolequellen. Die Lücken heißen je nach Anbieter: fehlender lain-Adapter, fehlende Leserechte, noch ungeprüfter Drittclientvertrag oder fehlender belastbarer Datenvertrag.

- **OpenRouter und Poe** dokumentieren Anmeldung für externe Anwendungen mit PKCE und automatischer API-Key-Ausgabe. Der ausgegebene Key kann Inferenz und bereits vorhandenes Monitoring bedienen; bei OpenRouter zunächst **Keylimits**, bei Poe **Restpunkte**. Das verspricht weder OpenRouter-Kontoguthaben noch Poe-Resetzeiten.
- **Gemini** veröffentlicht modellbezogene Quota-Metriken. Ein eigener Google-Login kann den Projektmonitor automatisch erneuern; direkter OAuth-Zugriff für Inferenz ist ebenfalls dokumentiert. Die genaue Serien-/Fensterzuordnung muss am ausgewählten Projekt geprüft werden.
- **Kilo und Requesty** haben konkrete Guthaben- beziehungsweise Keybudget-Reads mit dem bereits vorhandenen Inferenzcredential. **Synthetic** hat ein neueres Kontingentschema, das unser Adapter noch nicht verarbeitet.
- **MiMo, Mistral, Alibaba und Fireworks** besitzen offizielle Browseranmeldungen über ihre CLIs. Diese sind praktische Alternativen zum Keykopieren. Ein Hersteller-CLI-Login ist jedoch noch kein nachgewiesener öffentlicher Loginvertrag für eine eigene lain-App.
- **MiMo, Upstage, SambaNova und Cohere** liefern konkrete Consolequellen. Eine bewusst eingerichtete eigene Websitzung ist ein untersuchbarer Monitorweg. Die Inferenzkeys allein gewähren diesen Zugriff nicht nachweislich.

Für den gewünschten **Monitor ohne lokalen Proxy** zählt nur eine unabhängig lesbare Quelle. Antwortheader von Groq, SambaNova und Nebius verbessern den Gatewaybetrieb; ohne beobachtete Anfragen liefern sie keine neuen Werte. Ein Refresh darf dafür keine Inferenz auslösen.

## Konkreter Nutzerbefund: Kilo

Der Nutzer meldet nach Hinzufügen seines Kiloaccounts fehlende Kontingente und
als einzige Tarifauswahl „Explicit Kilo free models“. Der lesende Abgleich des
installierten `lain usage --json` bestätigt: Account vorhanden, Quelle
`unavailable`, Zustand `unsupported`, Erklärung **„Quota lookup is not implemented
for Kilo“**. Das ist kein belegter Authfehler und kein fehlendes Kontoguthaben.

Die alte TUI kann es tatsächlich: Ihr Collector verwendet `model-status`, dessen
[lokaler Adapter](/home/tomka/.local/share/modelcoach/provider_status.py:403)
mit demselben Gateway-Key `/api/profile/balance` liest. Für persönliche Accounts
liest er zusätzlich `kiloPass.getState`. Der
[Normalizer](/home/tomka/.local/share/modelcoach/provider_status.py:239)
trennt USD-Wallet, Passstatus, Basisgutschrift, Periodenausgaben, erwarteten/
ausgestellten Bonus und nächste Gutschrift/Abrechnung. Er erfindet aus diesen
Passfeldern weder monatliches Restguthaben noch Prozentverbrauch. Für einen
Orgwallet ruft er den persönlichen Pass nicht ab. Quellcode wurde nur gelesen;
die alte TUI und ihr Collector wurden nicht gestartet, Credentials nicht geöffnet.
Die aktuellen öffentlichen Herstellerbelege dazu stehen auch im
[Kilo-Bericht](quota-auth-research/kilo-opencode-requesty.md).

Ursache in lain: [kilo.json](../src/Lain.Host/Providers/Catalog/kilo.json) führt
nur `free-models` und eine daran gebundene freie Route. Die
[Einrichtungsoberfläche](../src/Lain.Desktop/MainWindow.Providers.cs) übernimmt
das als verpflichtenden „Billing plan“. Ein Kilo-Quotaadapter ist nicht
registriert. Die anfänglich schmale Freimodellroute wurde dadurch irreführend
zur einzigen Accounttarifwahl; sie beschreibt nicht den realen Wallet-/Passstatus.

**Vorgeschlagene Korrektur:** Kilo-Account unabhängig von Freimodellen verbinden,
vorhandenen Key direkt für Walletmonitoring verwenden, Passdaten separat
erkennen/anzeigen. „Nur kostenlose Modelle“ gehört zur Modell-/Routenauswahl;
es ist weder ein Accounttyp noch eine notwendige Monitorvoraussetzung. Das
erfordert keine automatische Freischaltung bezahlter Routen. Der kleinste
Reader liefert `Remaining` in USD, ohne erfundenes Limit, Reset oder Prozent;
Passstatus und Bonus benötigen eventuell eigene Darstellungsfelder.

Die zusätzlich gewünschte Browser-/Device-Anmeldung spart manuelles Keykopieren.
Der offizielle Kilo-Flow ist belegt; Drittclientintegration und Tokenlebenszyklus
bleiben zu qualifizieren. **Für die bereits mögliche Guthabenanzeige wird kein
OAuthwechsel benötigt.** OAuth allein verleiht keine zusätzlichen Usagefelder.
Dieser Befund konkretisiert das vorgeschlagene erste Umsetzungspaket; in dieser
Recherche wurde kein neuer Adapter installiert und kein Account geändert.

## Vollständige Anbietermatrix

„Monitor möglich“ bezeichnet einen belegten Quellenweg, **keine ausgelieferte Funktion**. „Kandidat“ bedeutet, dass ein entscheidender Vertrag, Scope oder Rollout noch fehlt. Jede Zeile verlinkt Quellen, konkrete Endpunkte, Produktgrenzen und nächste Prüfungen.

| Anbieter | Aktueller lain-Stand | Bester gefundener Quota-/Usageweg | Anmeldung ohne Keykopieren | Ohne Proxy / nächster Nachweis |
| --- | --- | --- | --- | --- |
| [Alibaba Model Studio](quota-auth-research/alibaba-nebius-inception.md) | Inferenz, kein Quotaadapter | Offizielle CLI: modellbezogener Trialrest mit Ablauf, Workspaceusage; API für konfigurierte Modell-/Workspacelimits | `bl auth login --console`; Console-Token und gewöhnlicher Inferenzkey getrennt | **Möglich über CLI**, zunächst Beijing/Singapore. Profilisolierung, Rohzeitstempel und Rechte prüfen. Coding Plan und separaten OAuth-Callpool nicht vermischen. |
| [Cloudflare Workers AI](quota-auth-research/google-cloudflare.md) | Inferenz und separate Monitorcredential-Aufnahme; kein Neuronreader | Konkreter GraphQL-Kandidat `aiInferenceAdaptiveGroups.sum.totalNeurons`; zusätzlich Billing Usage v2, Alpha/Restricted | Eigener öffentlicher PKCE-Client offiziell vorgesehen | **Kandidat.** Aktuelle Introspektion, Rechte, Sampling und Abrechnungsabdeckung prüfen; OAuth-Loopback/Scopes ebenfalls offen. Keine geschätzten Tokens in Neurons umrechnen. |
| [Cohere](quota-auth-research/samba-upstage-cohere.md) | Inferenz; kein Kontomonitor | Console-RPCs für Usage/Billing und Monatsbudget; Evaluation/Production getrennt | Öffentlich ausgelieferter CLI-Pairingflow; Tokenrechte und Drittclientnutzung offen | **Session-/Pairingkandidat.** Orgbindung, Lebensdauer und Readschema fehlen. 1.000 Trialcalls minus lokale Calls ergibt keine verlässliche Restquote. |
| [Fireworks](quota-auth-research/auth-review.md) | Quota-RESTadapter für Accountlimits/Usage vorhanden | Bestehende Quotas; zusätzlich bewertete Kosten über `billing/summary` und `usageCosts:query` | Offizielles `firectl signin`, danach benannten Nutzer-Key erzeugen und privat übernehmen | **Möglich**, bestehenden Key zuerst nutzen. CLI-Profil/JSON qualifizieren; `SELF`/`ACCOUNT` und Adminrecht trennen. Spendlimit ist kein Guthaben. |
| [Gemini Developer API](quota-auth-research/google-cloudflare.md) | Optionaler Access-Snapshot; bisher nur generische Projektrequests | Modellbezogene `generativelanguage.googleapis.com/quota/...`-Usage/Limit-Metriken plus Cloud Quotas | Eigener Desktop-OAuth; direkter Bearer-Inferenzzugang dokumentiert | **Offizieller Quellenweg.** Projekt, `limit_name`, Modell, Dimensionen und Fenster zusammenführen und prüfen. Bis zu 150 s Verzug; kein sicherer RPM-/TPM-Rest für sofortiges Routing. |
| [Groq](quota-auth-research/groq-mistral.md) | Passive RPD-/TPM-Header bereits bis Anzeige integriert; reale Beobachtung fehlt | Vorhandene Header; Enterprise zusätzlich Prometheus-Usage/Performance | Kein geeigneter öffentlicher Drittapp-Login qualifiziert | **Header nur Gateway.** Enterprise-Metrics liefern Raten, keine Geld-/Restbudgets. Gewöhnlichen kontoweiten Spendreader weiterhin qualifizieren. |
| [Inception](quota-auth-research/alibaba-nebius-inception.md) | Inferenz, lokale Usage; kein Kontomonitor | Öffentlicher Balance-/Quota-Read im geprüften Umfang nicht gefunden | API-Key; kein geeigneter delegierter Flow qualifiziert | **Offen.** Herstellervertrag oder gezielter eigener Console-Readnachweis erforderlich. Neukundengrant und lokale Tokens nicht als Kontorest ausgeben. |
| [Kilo](quota-auth-research/kilo-opencode-requesty.md) | Inferenz; Quota noch als nicht unterstützt geführt | Offizieller CLI-Client: `/api/profile/balance`, persönlicher Dollarstand mit demselben Bearer | Offizieller Device-Flow; eigener Drittclientvertrag offen | **Konkreter Readadapter möglich.** Personal-/Orgkontext und Nullsaldo prüfen. Kein Aufladen des erschöpften Kontos; Org-Token und Kilo Pass separat. |
| [Mistral](quota-auth-research/groq-mistral.md) | Inferenz; kein Quotaadapter | Enterprise Admin API Preview: Usage, Spendcap, konfigurierte Raten; gewöhnliche Pläne ohne diesen Adminzugang | Vibe-Browserlogin erzeugt einen Workspace-/Produktkey | **Enterprise-Reader möglich**, separater Admin-Key nötig. Authheader-Widerspruch klären. Vibe-Login liefert keine belegte gewöhnliche Planquota. |
| [Nebius Token Factory](quota-auth-research/alibaba-nebius-inception.md) | Inferenz; kein Quotaadapter | Offizielle RPM-/TPM-Remainingheader; Dedicated-Endpunkte separat mit Prometheus | PKCE → AI_STUDIO-Serviceaccountkey ist Forschungsoption, kein fertiger persönlicher Login | **Header nur Gateway.** Serverlessbalance bleibt offen. Serviceaccount-/Tenantrechte und TokenFactory-Kompatibilität vor Bootstrap klären. |
| [NVIDIA Build](quota-auth-research/mimo-nvidia.md) | Inferenz; kein Quotaadapter | Offizielle statische Angebotslimits, kein beobachteter Rest; Browser-Keyanlage belegt | Consolelogin, kein geeigneter öffentlicher delegierter Flow qualifiziert | **Offen.** Eigene Konsole/Hersteller auf lesbaren Kontostand prüfen. Bis zu 40 RPM / 10.000 RPD sind keine Restwerte; NGC/Enterprise nicht als Ersatz verwenden. |
| [OpenCode Go / Plus / Zen](quota-auth-research/kilo-opencode-requesty.md) | Go-/Plus-Inferenz; kein Kontomonitor | Offizielles Console-MCP mit OAuth-DCR für externe Clients; konkrete Readtools noch unbekannt | Hersteller-Device-OAuth existiert; eigene Device-ID nicht dokumentiert. MCP bietet einen eigenen externen Weg | **MCP-Kandidat.** Readtools, Workspace, Fenster und Scopes qualifizieren. Native Request-Allowance ist Verbrauch, kein Gesamt-Restbudget; Zen/PAYG getrennt. |
| [OpenRouter](quota-auth-research/auth-review.md) | `/key`-Monitoring vorhanden; manuelle Einrichtung | Key-, Member- und Workspacelimits im Keyvertrag; Accountcredits gesondert | Öffentlich dokumentierter PKCE-Keybootstrap, lokale Callbacks | **Möglich.** Hostlogin/Reconnect ergänzen, `/key` zur Bindung verwenden. Aktuelle `/credits`-Doku verlangt Management-Key; kein pauschales Creditversprechen. |
| [Poe](quota-auth-research/xai-stepfun-poe-synthetic.md) | Restpunkteadapter und vorbereiteter PKCE-Helper | `/usage/current_balance`; separat paginierte Punktehistory | Öffentlicher Drittapp-PKCE liefert denselben API-Key für Inferenz und Reads | **Möglich.** Eigenen Client registrieren und Produktionscallback qualifizieren; Hostverdrahtung fehlt. Restpunkte liefern keinen Nenner oder Reset. |
| [Requesty](quota-auth-research/kilo-opencode-requesty.md) | Inferenz; kein Quotaadapter | `/apikey/self` und `/apikey/self/usage` mit Inferenzkey; Org-Balance mit zusätzlichem manage-read | Offizielle CLI: PKCE → Gruppe wählen → Key erstellen; eigene Clientregistrierung offen | **Konkreter Self-Reader möglich.** `monthly_limit=0` heißt ohne Keycap. Orgfreiquote nicht aus Keyrequests ableiten. |
| [SambaNova](quota-auth-research/samba-upstage-cohere.md) | Inferenz; kein Quotaadapter | Offizielle RPM-/RPD-Header; Console-Usage und Creditbalance als separate Sessionquelle | Browser-Session plus Keyanlage als Kandidat; kein öffentlicher OAuthvertrag gefunden | **Header nur Gateway; Sessionmonitor Kandidat.** Headerresetformat, Consolewährung und Sessionvertrag prüfen. |
| [StepFun / Step Plan](quota-auth-research/xai-stepfun-poe-synthetic.md) | Persönlicher PAYG-Balancereader vorhanden; kein Planreader | PAYG `/v1/accounts`; offizieller Step-Code-Vertrag für persönlichen `/step_plan/v1/usage` | Step-Code-Browser-/Pollinglogin liefert Plankey; Drittclientvertrag offen | **PAYG möglich; Plan Kandidat.** Schema-1-Vertrag belegt, öffentlicher Rollout ungeprüft; 404 bedeutet laut Vertrag nicht deployed. Plan/Org/Region nicht gleichsetzen. |
| [Synthetic](quota-auth-research/xai-stepfun-poe-synthetic.md) | `/v2/quotas`, aber älteres Schema | Aktueller Herstellerclient verarbeitet `planWeeklyLimit`; zusätzlich passiver `x-synthetic-quotas`-Header | Vorhandener Inferenzkey reicht; kein öffentlicher Loginbootstrap qualifiziert | **Möglich.** Aktuelle und alte Schemas getrennt unterstützen. Nächste Teilregeneration ist kein vollständiger Wochenreset; PAYG-Geldsaldo bleibt offen. |
| [Upstage](quota-auth-research/samba-upstage-cohere.md) | Inferenz; kein Kontomonitor | Console-BFF für Org-Spend, Creditcoupons/Ablauf, Budget und paginierte Usage | Eigene Browseranmeldung mit Orgwahl als Kandidat; kein öffentlicher Drittapp-OAuth belegt | **Sessionmonitor Kandidat.** Gelddezimalwerte, Rolle und Cookie-/CSRF-Vertrag qualifizieren; Monitoring erzeugt/enthüllt keine Keys. |
| [xAI API](quota-auth-research/xai-stepfun-poe-synthetic.md) | Inferenz; kein xAI-Reader | Management API für Prepaidledger, Postpaidlimits/Rechnung und Usage | Management-Key kann mit zusätzlichen Schreibrechten Inferenzkey erzeugen; kein belegter API-Team-OAuth | **Readadapter möglich**, getrennte Managementcredential. Signierte Centwerte und Teamscope qualifizieren. Grok-Build-Login ist ein anderes Produkt. |
| [Xiaomi MiMo](quota-auth-research/mimo-nvidia.md) | Importierte Inferenzrouten; kein direkter Quotaadapter | Console: Individualplan `usage.items[]`, Teamplan/Seat, PAYG-Balance | Offizieller MiMo-Code-Browserflow liefert verschlüsselten Inferenzkey + UID/Base URL | **Sessionmonitor Kandidat.** CLI-Callback liefert keine Monitorsession. Aktuelles Schema statt altem CodexBar-Parser; monatliche/jährliche Credits, Team und PAYG trennen. |

Einzelne aufgeführte Quellen sind bereits implementiert, andere fehlen nur in lain. Bestehende funktionierende Werte bleiben erhalten. „Alle Anbieter untersucht“ bedeutet nicht „alle Anbieter haben eine vollständige öffentliche Restquota-API“.

## Ein Login: drei verschiedene Ergebnisse

Die Benutzeroberfläche kann bei allen drei Varianten **„Mit Anbieter anmelden“** sagen. Intern sind sie verschieden und benötigen unterschiedliche Lebenszyklen:

1. **Anmeldung liefert einen Inferenzzugang.** OpenRouter/Poe erzeugen einen API-Key; herstellereigene Keyvarianten gibt es bei Requesty, MiMo, Mistral und Step. Kilo liefert einen Hersteller-Bearertoken/JWT; die Erstellung eines separaten Keys ist dort nicht belegt. Der Nutzer kopiert kein Secret. Ohne dokumentierten Refreshgrant führt Ablauf zu erneuter Anmeldung.
2. **OAuth-Token bleibt der Zugang.** Google und Cloudflare sind konkrete Kandidaten. Eigene Clientregistrierung, erlaubte Scopes, Ressourcenwahl und atomare Refreshrotation gehören zum Connector. Ein Nutzerlogin allein wählt kein Projekt und bewilligt keine zusätzlichen IAM-Rechte.
3. **Offizielle CLI oder eigene Websitzung bleibt zuständig.** Die CLI kann Anmeldung und ausgewählte Reads übernehmen, oder einen benannten Key bereitstellen. Ein Sessionmonitor kann Consolewerte lesen. Das ist kein stiller Import vorhandener Browsercookies und keine automatische Umwandlung externer Refresh-Tokens in lain-eigene Tokens.

Bei Poe/OpenRouter genügt der ausgegebene Key für die genannten Reads. MiMo benötigt für den belegten Monitor zusätzlich eine Plattformsession. Mistral-Enterprise- und xAI-Reporting haben gesonderte Rechte. Diese Unterschiede kann ein Connect-Vorgang führen, ohne den Nutzer mit allen technischen Details zu überladen.

**Eigene Appregistrierung ist gewöhnlich Maintainerarbeit**, keine Aufgabe jedes Nutzers. Bei Google/Cloudflare und Poe ist sie vor allgemeiner Auslieferung erforderlich beziehungsweise zu qualifizieren; für OpenRouter dokumentiert der Hersteller keine nötige Registrierung. Die Client-IDs offizieller Hersteller-CLIs werden nicht als eigene ausgegeben. Quellcode beweist einen vorhandenen Flow, aber nicht dessen öffentlichen Drittclientvertrag.

## Vorschlag für die bestehende lain-Architektur

Die Ziele sind bestätigt: Anmeldung möglichst ohne Keykopieren; Inferenz und Monitoring am selben Account; optionaler Monitor ohne Core, zunächst Kontingente und Resets. **Die folgenden Änderungen sind Empfehlungen, noch kein neuer Implementierungsauftrag.**

### Vorhandene Grundlage erweitern

`MonitorAccessService` und der private Monitorsecret-Speicher existieren bereits. Es fehlt kein allgemeiner zweiter Credentialspeicher. Die aktuellen Bedingungen in [MonitorAccessService](../src/Lain.Host/Accounts/Monitoring/MonitorAccessService.cs) (`CredentialKind == "api-key"`) und [HostQuotaSource](../src/Lain.Host/Program.cs) (Inferenzsecret muss existieren) verhindern allerdings einen eigenständigen Monitoraccount.

Empfehlung: stabile Accountidentität behalten, Inferenz- und Monitorfähigkeiten unabhängig anhängen. Ein reiner Monitor braucht weder Dummykey noch Modellroute oder Coreprozess. Beim späteren Verbinden der Inferenz bleibt derselbe Account mit seiner Historie erhalten.

`IQualifiedLoginAdapter` und `LoginOperationService` in [AccountService.cs](../src/Lain.Host/Accounts/AccountService.cs) sind der vorhandene Einstieg. Ergänzt werden müssten Methode, Zweck, Zielaccount/Neuanlage, erwartete Revision und Ressourcenbindung. Keine neue allgemeine Pluginengine und keine pauschale OAuthimplementierung für alle Anbieter. Der bereits vorbereitete [PoeOAuthAttempt](../src/Lain.Host/Providers/Poe/PoeOAuth.cs) wird wiederverwendet.

Vier getrennte Informationen reichen als fachliches Arbeitsmodell:

| Information | Warum sie gebraucht wird |
| --- | --- |
| Beschaffung: öffentlicher OAuth, Keybootstrap, Hersteller-Device, CLI, Session, manueller Key | Bestimmt Einrichtung und mögliche Wiederanmeldung. |
| Zuständigkeit: lain, externer Snapshot, delegierte CLI, vom Nutzer bereitgestellter Key | Bestimmt, wer erneuern oder widerrufen darf. |
| Fähigkeiten: Inferenz, Quota, Usage, Kosten, Identität | Erfolgreicher Login ist kein Beweis aller Rechte. |
| Ressource und Nachweis: Account/Org/Projekt/Workspace/Plan, Quelle, Datum, Prüfstatus | Verhindert fremde oder doppelt gezählte Pools und unverdiente „funktioniert“-Kennzeichnung. |

Ein neuer E-Mail- oder Anzeigename ist keine neue Budgetidentität. Reconnect darf nicht still einen anderen Workspace an alte History binden. Gemeinsame Pools werden anhand belegter Ressourcen-/Budget-IDs erkannt, nicht über Anbieternamen oder ähnliche Labels.

### Daten und UI

Restmenge, Geldsaldo, Spendcap, konfigurierte Rate, gemessener Verbrauch und Teilregeneration bleiben unterschiedliche Werte. Prozentbalken brauchen einen bekannten passenden Nenner; unbekannt bleibt `null`. Die nächsten 2 % Synthetic-Regeneration dürfen nicht wie ein voller Reset beschriftet werden. Geldguthaben ohne Gesamtzuteilung bekommt keinen erfundenen Prozentbalken.

Abrufzeit, fachlicher Datenstand, Zeitraum und erwarteter Verzug bleiben getrennt. Google-Telemetrie mit bis zu 150 Sekunden Verzögerung ist keine Echtzeit-Kapazitätsreservierung. Eine wiederholt gelesene Groq-Beobachtung wird nicht frisch. Gleiches gilt für Anbieterconsole-Daten ohne Aktualisierungsfeld.

Für die Accountsseite empfehle ich einen normalen Wert oder **eine konkrete nächste Aktion**, statt wiederholter Credentialwarnungen:

| Situation | Kurze Anzeige / Aktion |
| --- | --- |
| Unterstützte Quelle, noch nicht verbunden | „Kontingente verbinden“ |
| Anmeldung abgelaufen | „Erneut anmelden“; letzten Wert mit Alter erhalten |
| Fehlendes Leserecht | „Leserechte ergänzen“ mit genau passendem Anbieterweg |
| Passive Quelle ohne Beobachtung | Im Gateway: „Nach der ersten Anfrage über lain verfügbar“; im reinen Monitor: „Benötigt eine Quelle für beobachtete Anfragen“ |
| Noch unqualifizierte Console-/APIquelle | „Kontingente im Anbieterportal“; Anschlussstatus in Details |
| Echter Abruffehler | Letzter Stand und nachvollziehbarer Fehler, kein Nullsaldo |

„Nur Kontingente“ und „Auch als Gateway verwenden“ sollten Fähigkeiten desselben Accounts sein. Barebones zeigt Werte und nächste Aktion; die technischen Quellen-/Scopeinformationen bleiben in Details erreichbar. Dies ist ein Bedienvorschlag, keine durch diese Recherche bereits geänderte Oberfläche.

## Empfohlene nächste Arbeitspakete

Die Reihenfolge bevorzugt komplette kleine Nutzerabläufe. Quellenbelege sind noch keine Liveabnahme. Registrierung, private Anmeldung und ggf. neue Secrets gehören in einen später ausdrücklich beauftragten Connect-/Implementierungsschritt.

### 1. Kontingente mit vorhandenen Zugängen nutzbar machen

Kilo-Balance, Requesty-Self-Keybudget und aktuelles Synthetic-Schema. Kleine native Reader über den bestehenden Quota-Service; keine zusätzliche Anmeldung erzwingen. OpenRouter-Keylimits erhalten und aktuelle Creditsberechtigung separat führen. Das liefert zuerst neue Werte auch ohne Inferenzverkehr.

Prüfung: synthetisch Null/fehlend/unlimited, negative zulässige Geldwerte, Partialantworten, Account-/Orgtrennung, Schemakombinationen und Teilregeneration. Danach getrennt dokumentierte reine Live-Reads bei bereitgestellten Konten; keine Inferenz und kein Kauf nötig. Kilo darf mit echtem Nullsaldo korrekt funktionieren.

### 2. Ein durchgängiger Browserlogin

OpenRouter zuerst: öffentliches PKCE, lokale Callbacks, bestehender Reader. Poe unmittelbar danach mit eigenem Client und qualifiziertem Produktionscallback. Gemeinsamen Loginstart gezielt um stabile Accountbindung und Zweck erweitern; bestehende API-Keys bleiben als Alternative nutzbar. Der Abschluss zeigt die tatsächlich verfügbaren Kontingente, ohne danach Keykopieren zu verlangen.

Prüfung: Statebindung, Replay, Ablauf, Abbruch, parallele Accounts, falscher Workspace, Konfliktrevision und verspäteter Callback nach Entfernen. Host speichert privat; UI/CLI/Logs bekommen keinen Token. Bereits beim Anbieter erzeugter Key nach lokalem Fehler muss erkennbar behandelt werden; keine unbelegte Revokebehauptung.

### 3. Google als vollständigen Projektmonitor qualifizieren

Zuerst echtes Modellquota-/Limitmapping auf einem vorhandenen aktivierten Projekt lesen. Anschließend eigener Desktop-OAuth und Projektauswahl; Inferenz mit demselben OAuthgrant gesondert qualifizieren. Der vorhandene Inferenzkey kann währenddessen bestehen bleiben. Automatische Serviceaccount-/Key-/Billingprovisionierung ist kein notwendiger erster Schritt.

Prüfung: tatsächliche Serien, Dimensionen und Abdeckung gegen AI Studio; Pacific-Tageswechsel einschließlich DST, verspätete/fehlende Daten, IAM-403 und Refreshrotation. Aktuelle AI-Studio-Authorization-Keys benötigen mehr als einen einfachen API-Key-create-Aufruf; deshalb direkten OAuthzugang bevorzugt untersuchen.

### 4. Offizielle CLI- und Consolewege gezielt erschließen

Fireworks, Alibaba und MiMo zuerst als offizielle CLI-Connectoren qualifizieren; die jeweiligen Profile, Produkt-/Regionsbindung und ausgegebenen Secrets isolieren. Requesty/Kilo-eigenen Login erst nach Klärung des Drittclientvertrags. Mistral-Vibe nur für den passenden Produktkey, nicht als vermeintlichen Enterprise-Monitorlogin.

Für **MiMo-Quota** getrennt einen eigenen angemeldeten Sessionmonitor mit aktuellem Individual-/Team-/Seat-Schema prüfen. Upstage- und Cohere-Consolequellen folgen mit den konkreten Lesepfaden aus den Berichten. Eine Sessionintegration benötigt einen eigenen begrenzten Prototyp; die öffentliche Existenz eines Bundles allein macht sie nicht auslieferungsreif.

Prüfung: keine Änderung anderer CLI-Profile/Browseranmeldungen; Produkt-/Tenantwechsel, Sessionablauf, Rollen, vollständige Pagination, Geld-/Credit-/Resetsemantik. Monitorrefresh führt weder Keyerstellung noch Reveal, Planwechsel oder Zahlung aus. Breite Cloudrechte sind kein stiller Fallback.

### 5. Offene Quellen mit kleinen Prüfaufträgen entscheiden

- Cloudflare: aktuelle GraphQL-Introspektion und Tagesvergleich; Billing-v2 nur bei bestehendem Zugriff, OAuthscope und APIakzeptanz getrennt.
- OpenCode: offiziellen MCP-Client registrieren und Read-Toolinventar qualifizieren; keine Verwaltungs-Schreibtools ausführen.
- Step Plan: persönlichen offiziellen Usagevertrag und tatsächlichen Rollout prüfen; 404 bleibt „noch nicht verfügbar“.
- Mistral Enterprise / xAI: nur mit vorhandener Berechtigung Admin-/Managementreader; genaue Header/ACLs, Scope und Geldsemantik vorher klären.
- Groq/SambaNova/Nebius: passive Header sauber anzeigen, als eigenes Gatewaypaket. Das schließt die offene standalone Kontomonitorlücke nicht.
- NVIDIA/Inception: konkreten unterstützten Konto-Read oder bewusst begrenzte Consoleprobe nachweisen; bis dahin Portal und lokale Usage ehrlich getrennt.

Jedes Paket dokumentiert **Quellenprüfung → Implementierung → lokale Qualifikation → Installation → Liveprüfung** getrennt. Ein Nutzeraccount wird durch einen Adaptertest nicht live qualifiziert; eine dokumentierte API ist noch kein installierter Reader. Der bestehende Proxy, normale Codexkonfiguration und ausstehende Profile bleiben davon unabhängig.

## Belegablage und Gegenprüfung

Jeder Bericht hat eine JSON-Begleitdatei mit Quellen, Authoptionen, Implementierungslücken und offenen Prüfungen:

- [MiMo / NVIDIA](quota-auth-research/mimo-nvidia.md)
- [Groq / Mistral](quota-auth-research/groq-mistral.md)
- [SambaNova / Upstage / Cohere](quota-auth-research/samba-upstage-cohere.md)
- [Kilo / OpenCode / Requesty](quota-auth-research/kilo-opencode-requesty.md)
- [Alibaba / Nebius / Inception](quota-auth-research/alibaba-nebius-inception.md)
- [xAI / StepFun / Poe / Synthetic](quota-auth-research/xai-stepfun-poe-synthetic.md)
- [Google / Cloudflare](quota-auth-research/google-cloudflare.md)
- [OpenRouter / Fireworks und Authgegenprüfung](quota-auth-research/auth-review.md)

Die [Abdeckungsmatrix](quota-auth-research/coverage.json) ordnet alle Katalog-IDs und MiMo den Produktvarianten und Quelldateien zu. Öffentliche Console-Bundles sind Herstellerbelege für beobachtetes Clientverhalten, keine Stabilitätsgarantie; offizielle CLI-Quellen sind keine automatische Drittapp-Freigabe. Die Belege enthalten keine persönlichen Zugangsdaten.

Dieser Bericht erweitert die Quellenlage von [quota-sources.md](providers/quota-sources.md) und [monitor-auth.md](providers/monitor-auth.md). Deren beschriebener installierter Adapterumfang bleibt der bisherige Stand. Insbesondere wurden Googles direkte Quotaserien, mehrere Browserlogins und die aktuelle Synthetic-Semantik erst in dieser Vertiefung konkret nachgewiesen.
