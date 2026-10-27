# Monitoring und Anmeldung: Codex/OpenAI, Command Code, Z.ai, Kimi, MiMo, Synthetic

Recherchezeit **2026-10-25**, Codebezug **lain `cbf083f`**. Öffentliche GETs auf Dokumentation, offizielle Repository-Dateien und unangemeldet ausgelieferte Herstellerassets. Keine lokalen Credentials, Browserprofile, Cookies oder Datenbanken gelesen; kein Login, Tokenrefresh, Clientregistration, Inferenz oder echter Accountquota-Read ausgeführt. Kein Build/Install und keine Produktdatei geändert. Dokumentationsstand, Source-Fähigkeit, Implementation, Installation und Liveaccount sind fünf getrennte Aussagen. Die älteren WIP-Berichte wurden nur als Quellenwegweiser benutzt und nicht verändert.

**Entscheidung:** Für keinen dieser sechs Anbieter ist die pauschale Begründung „Monitoring nur mit Pro/OAuth“ korrekt. Vorhandene passende API-Keys reichen bei Command Code, Z.ai Coding Plan, Kimi Coding, Synthetic und Kimi Open Platform für konkrete Reads. Codex-Aboquoten benötigen ChatGPT-/Codex-Dienstauth statt eines normalen OpenAI-API-Keys. MiMo-Planmonitoring hat einen konkret belegten Console-Sessionpfad; der Inferenzkey allein ist dafür noch nicht als unterstütztes Credential belegt. Neue Logins und ein weiterer Tarifkauf sind nicht pauschal Voraussetzung.

## Direkt verwendbare Kurzgründe und nächste Schritte

Die Texte sind **zustandsabhängige Vorlagen**, keine Diagnose tatsächlich angeschlossener Privatkonten.

| Produkt/Zustand | Kurzer Grund im Produkt | Nächste Aktion |
| --- | --- | --- |
| Codex mit vorhandenem qualifiziertem ChatGPT-Zugang | „Codex-Limits werden über deinen ChatGPT-Zugang gelesen. Ein OpenAI-API-Key zeigt dieses Abo nicht.“ | Vorhandenen Zugang verwenden; fehlgeschlagenen Read samt ursprünglichem Datenzeitpunkt anzeigen. Offiziellen App-Server als unabhängigen Login-/Monitoringowner qualifizieren. |
| OpenAI API, nur Inferenzkey | „Dein API-Key liefert Rate-Limits bei normalen Anfragen. Für den organisationsweiten Verbrauch fehlt ein Admin-Lesezugang.“ | Passive Header beobachten; optional passenden Admin-Key für Usage/Costs separat verbinden. Kein ChatGPT-Abo kaufen. |
| Command Code | „Dein Command-Code-Key kann vorhandene Creditdaten lesen. Monatliche Modellrestmengen sind damit noch nicht vollständig belegt.“ | Vorhandenen Key und zurückgelieferte Organisation verwenden; für unbekannte Modellbudgets Studio-Usage öffnen. Kein Pro-/OAuthzwang. |
| Z.ai Coding | „Planlimits sind mit dem Coding-Key lesbar. Ein Standard-API-Key gehört zu einer anderen Abrechnung.“ | Keyklasse und Personal-/Teamkontext klären; bestehenden richtigen Key nutzen, Teamreader getrennt ergänzen. |
| Kimi Coding | „Planlimits sind mit dem Coding-Zugang lesbar. Neue Pläne haben kein Wochenlimit; fehlende Fenster sind kein Fehler.“ | Nur gelieferte Fenster anzeigen; Plus genügt für Coding. Booster und offiziellen lokalen Server als getrennte Erweiterungen qualifizieren. |
| Kimi/Moonshot Open Platform | „Für dein API-Guthaben ist ein offizieller Balanceabruf mit demselben Plattform-Key verfügbar; lain hat dafür noch keinen Reader.“ | Regional passenden `/users/me/balance`-Reader ergänzen, ohne Coding-Abo-/OAuthwechsel. |
| MiMo Individual Token Plan | „Für die Plananzeige fehlt die MiMo-Konsolenanmeldung. Der Modell-Key überträgt diese Sitzung nicht.“ | Vorhandenen separaten Console-Monitor verbinden; keine neue Pro-Subscription verlangen. |
| MiMo PAYG oder Team | „Die Konsole hat passende Daten, aber lain liest derzeit nur den Individualplan.“ | PAYG-Balance bzw. explizites Teamprojekt/Seat als eigene Reader ergänzen. |
| Synthetic Plan | „Dein API-Key reicht für die Planquote. Bei einem Abruffehler fehlt ein aktueller Messwert, kein zusätzlicher Login.“ | Bestehenden Reader nutzen, Fehler/Staleness differenzieren. Nächste Teilregeneration statt Vollreset zeigen. |
| Synthetic PAYG/Extra Usage | „Die Planquote ist lesbar; ein dokumentierter Geldsaldo-Read ist noch nicht qualifiziert.“ | Billing öffnen oder gezielt Herstellervertrag für Geldsaldo/Usage klären; keinen Abo-Prozentwert als Geldsaldo darstellen. |

## 1. Codex / OpenAI

### Tarif und Messobjekt

- **Codex Free/Go:** Die aktuelle [Pricing-Doku](https://learn.chatgpt.com/docs/pricing.md) nennt GPT-6 Luna in der Desktop-App „subject to rollout“. Das ist kein Beleg für allgemeinen CLI-/Drittappzugriff und kein Beleg, dass diese Accounts keine messbaren Limits hätten. Account-/Surfaceberechtigung bleibt konkret zu prüfen.
- **Plus/Pro/Business/Enterprise/Edu:** Plus enthält ausdrücklich CLI/Web/IDE. Pro ist nicht Monitoringvoraussetzung. Dieselbe aktuelle Seite sagt **„Pro plans currently have no five-hour limit“**; deshalb auch bei Codex keine festen 5h- und 7d-Zeilen aus dem Tarif erzeugen. Flexible Enterprise-/Edupläne sind creditbasiert; andere Enterprise-/Edupläne können Sitzlimits haben. Work und Codex teilen Usage. `/status` und das [Usage-Dashboard](https://chatgpt.com/codex/settings/usage) sind die offizielle manuelle Anzeige.
- **OpenAI API Free-Tier/Trial/PAYG:** Das ist eine eigene API-Organisation, nicht das ChatGPT-Abo. Die [Rate-Limit-Doku](https://developers.openai.com/api/docs/guides/rate-limits.md) listet Free sowie Build/Launch/Grow und trennt Organisations-/Projektlimit, Spendlimit und Ratelimit. Ein Free-Tier-Tabellenwert ist weder kostenloses Guthaben noch tatsächlich verbleibender Betrag. Keine pauschale aktuelle Trialcredit-Zusage aus diesen Quellen.

### Offizielle Read- und Authwege

| Quelle | Messobjekt | Auth und Scope | Grenze |
| --- | --- | --- | --- |
| [Codex App-Server](https://developers.openai.com/codex/app-server.md), JSON-RPC `account/rateLimits/read` | `usedPercent`, `windowDurationMins`, `resetsAt`; `rateLimitsByLimitId`; optionale Plan-/Workspacecredits; verdiente Resetcredits separat | Codex-dienstgestützter ChatGPT-Zugang im offiziellen App-Server. Managed Browser-/Deviceflow übernimmt Persistenz und Refresh. | Beispielwerte sind keine Tarifkonstanten. Fehlender Bucket ist unbekannt/nicht geliefert. Verdiente Resetcredits sind nicht Geldguthaben. |
| Derselbe App-Server, `account/usage/read` | Lifetime-/Tages-Tokenaktivität, optionale Tagesbuckets | Doku explizit: ChatGPT, external ChatGPT tokens, agent identity, personal access token; **nicht API-key-only oder Bedrock** | Historischer Verbrauch, nicht Restkontingent. |
| Normale [OpenAI API-Antwortheader](https://developers.openai.com/api/docs/guides/rate-limits.md) | `x-ratelimit-{limit,remaining,reset}-{requests,tokens}`, ggf. projektbezogene Tokenheader | Bestehender Inferenzkey auf seiner tatsächlichen Organisation/Projekt/Modellroute | Beobachtete Ratekapazität zum Antwortzeitpunkt, keine Cashbalance. Refresh darf keine Inferenz auslösen. |
| `GET https://api.openai.com/v1/organization/usage/completions`; `GET .../organization/costs` | Aggregierte Tokens/Requests bzw. USD-Kosten mit Zeitbuckets und Projekt-/Key-/Modellgruppierung | [Offizielles Cookbook](https://developers.openai.com/cookbook/examples/completions_usage_api): **„Set up an Admin Key“**, Bearer Admin-Key. Orgweit, Filter begrenzen Ergebnis, nicht automatisch Credentialrechte. | Kein automatischer verbleibender Prepaidcredit, kein ChatGPT-Usage-Endpoint. Normale Projektkeys nicht als Admin-Keys behandeln. |

Offizieller App-Server ist ausdrücklich ein Produktintegrationsinterface: „Use it when you want a deep integration inside your own product“. Browserlogin: `account/login/start {type:"chatgpt"}`; Device: `{type:"chatgptDeviceCode"}`. [Auth-Doku](https://developers.openai.com/codex/auth.md) belegt `codex login --device-auth`, ggf. Freischaltung in Sicherheits-/Workspaceeinstellungen. Das ist ein existierender offizieller Owner, kein fremder Tokenexport und keine Terminalscreen-Scrapingpflicht.

**Drittclient-/OAuthgrenze:** Die App-Server-Doku erlaubt bestehende lokale/Open-Source-Integrationen weiter, empfiehlt SIWC und erklärt: „App-server authentication has never been permitted for commercial or hosted services.“ Nicht auf jede persönliche lokale Nutzung eine Partnerzulassung übertragen.

[Sign in with ChatGPT](https://developers.openai.com/siwc/token-sharing-open-source/sign-in.md) ist inzwischen ein **tatsächlich dokumentierter eigener öffentlicher Flow**: erster Login `client_id=dynamic_agent_client`, eigener konsistenter Appname, persistenter `ext_agent_host_id`, Browser-PKCE S256, `127.0.0.1`-Callback; Callback liefert eigene Client-ID. Kein Clientsecret oder Partner-API-Key nötig. Auth `https://auth.openai.com/api/accounts/authorize`, Token `.../oauth/token`, Scopes `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`, Resource `https://api.openai.com/v1`. Das [Cookbook vom 2026-09-28](https://developers.openai.com/cookbook/articles/sign-in-with-chatgpt) nennt lokal laufende persönliche/Open-Source-Projekte und eligible Plus/Pro; bezahlte oder remote gehostete Apps benötigen Zugang über Waitlist.

**Wichtiger Gegenbeleg zu einer zu großen Schlussfolgerung:** SIWC beweist eigene delegierte Identität und Planinferenz, **nicht dieselben Codex-Quota-Leserechte**. Seine [Preview-Doku](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations.md) nutzt im App-Server einen eigenen Responses-Provider und hostverwaltete Tokenrenewal; seine UI verweist auf ChatGPT Settings → Usage. Ein SIWC-only-Token darf ohne weiteren Vertrag nicht an `wham/usage` geschickt oder als vorhandener Codex-Login ausgegeben werden. Kein öffentliches SIWC-Monitor-only-Scope wurde hier belegt.

### lain-Stand und konkrete Arbeit

`CodexQuotaAdapter` in `cbf083f` liest weiterhin `https://chatgpt.com/backend-api/wham/usage` mit qualifiziertem Codex-Access-Token und passender ChatGPT-Account-ID. Er akzeptiert **keinen normalen API-Key**, verarbeitet primäres/sekundäres Fenster, aber nicht den gesamten neu dokumentierten App-Server-Multibucket-/Credits-/Usagevertrag. Das ist ein bestehender interner Direktpfad, nicht eine in dieser Recherche offiziell dokumentierte öffentliche REST-API. `CodexAppServerQuotaParser` existiert; `CodexStandaloneConnectAdapter` erbt aber `UnqualifiedManagedLoginAdapter`, und der Parser ist nicht als laufender Quotaadapter verdrahtet.

Nächste Arbeit: offiziellen App-Server als eigenständigen Owner versionsgebunden qualifizieren; vorhandene owned Credentials/Snapshots unverändert getrennt behandeln; App-Server-RPC statt `/status`-Text auswerten. API-Usage/Costs und passive API-Header sind **separate** Pakete. Kein Anspruch, dies sei schon installiert oder am Privatkonto live geprüft.

## 2. Command Code

### Tarif und heutige Fähigkeiten

Die tatsächlich geladene [Provider-Referenz](https://commandcode.ai/docs/provider) sagt ausdrücklich **„Every plan except the Go plan has API access“**: GOAT, Pro, Max, Team, Provider. **GOAT genügt**, nicht erst Pro. Der API-Zugriffsausschluss für Go beweist keinen Ausschluss von Monitoring: Die [Preisseite](https://commandcode.ai/pricing) nennt auch für Go „Limited usage analytics“. Login und Billing-Read sind ihrerseits kein Nachweis einer erlaubten Inferenzroute.

- **Free/Trial:** Aktuelle Seiten nennen kostenlose/promotional Modelle (Glyph bis 100/Tag, Beam ohne daily request limit, Laguna solange Kapazität reicht). Das sind Modellangebote, kein allgemein belegter kostenloser Accounttarif oder Trialguthaben. Null Modellpreis bedeutet nicht unendliche Account-/Infrastrukturrate.
- **Coding-Abos:** Monthly credits, zusätzliche modellabhängige Allowances und mögliche kurze/lange Usagefenster separat. Plancredits sind nicht gleich der monatlichen modellbezogenen Restmenge. Aktuelle Preiswebsite ist intern widersprüchlich: GOAT-Karte/FAQ nennt `$80`, Vergleichstabelle `$70`; auch Providertext sagt einerseits `$15/month`, andererseits „No monthly minimums or commitments“. Keine feste neue Quotatable aus Werbetext bauen.
- **Provider PAYG/Top-ups:** API-Doku belegt Guthaben und Top-ups, „Credits never expire“ und „Provider plan … has no usage windows/limits“. Das meint Planusagefenster, nicht keinerlei technische Ratelimits; dieselbe API listet 429 upstream rate limiting. Subscriptioncredits, gekaufte Credits, Freicredits und Orgpool getrennt.

### Read/Auth/CLI

`cbf083f` hat einen aktiven Same-Key-Adapter: zuerst `GET https://api.commandcode.ai/alpha/whoami?limits=1`, danach `GET .../billing/credits?orgId=<returned-org.id>` und `GET .../billing/subscriptions?orgId=...`, **Bearer vorhandener Command-Code-API-Key**. Gelieferte `monthlyCredits`, `purchasedCredits`, `freeCredits`, `windowLimits.fiveHour/weekly` und `orgLimits` mit `limit/spent/resetAt/model/scope` werden verarbeitet. Partial Billingfehler dürfen andere Daten erhalten. Dieser tatsächliche Implementierungsweg ist **kein in den geöffneten öffentlichen Docs belegter stabiler Billing-API-Vertrag**. Ein Mindest-Abo oder getrenntes Monitorcredential dafür wurde nicht gefunden. Ebenfalls kein vollständiger Read für alle modellmonatlichen Restbudgets belegt.

Die [Quickstart-Doku](https://commandcode.ai/docs/quickstart) belegt `cmd login`: „This opens a browser to authenticate“, temporärer lokaler OAuthcallback, Erfolg „API key stored …“. Die [Provider-Doku](https://commandcode.ai/docs/provider) belegt **„The same key authenticates the CLI and the API.“** Manuelle Studio-Keyanlage bleibt nutzbarer Fallback. Browserbootstrap ist real, aber die Quellen liefern keinen öffentlichen Drittclientregistrierungs-/Scope-/Refreshvertrag für einen unabhängigen lain-Login. Nicht aus „OAuthcallback“ ableiten, lain müsse einen OAuthrefresh-Token besitzen.

Die [CLI-Referenz](https://commandcode.ai/docs/reference/cli) dokumentiert `cmd status --json`, allerdings ausdrücklich **Authentifizierungsstatus**, nicht einen vollständigen JSON-Billingreport. `--output-format json` betrifft Inferenz-/Agentereignisse; keine Quotaabfrage damit erfinden. `/usage` oder menschliche Studioanzeige ist nicht allein ein stabiler Automationsvertrag.

### lain-Stand und nächste Arbeit

Der bestehende Quotaadapter bleibt wertvoll. `CommandCodeCliLoginAdapter` ist jedoch ein `UnavailableCliLoginAdapter`; dessen enges Problem ist unqualifizierte Executableversion, isolierter owned Cache und gezielter privater Keyexport. Nächster Schritt: Version/Resultat/Isolation des offiziellen CLI-Bootstraps qualifizieren oder bestehenden Studio-Key benutzen. **Keine zweite Billinganmeldung** pauschal fordern. Alpha-Billingvertrag und tatsächliche per-model Remainingquelle separat klären. Die historische `PlanCredits`-Map im Adapter ist kein aktueller, serverbestätigter Nenner; sie darf bei unklaren Legacy-/Promotionplänen keine Genauigkeit vortäuschen.

Die bisherigen Links `/docs/plans` und `/docs/api` lieferten am 25.10. HTTP 404; aktuelle belastbare Seiten sind `/docs/provider`, `/pricing`, `/docs/quickstart`, `/docs/reference/cli`. Das ist ein Link-/Quellenpflegeproblem, kein Accountproblem.

## 3. Z.ai / GLM

### Produkt-/Keytrennung ist wichtiger als OAuth

Der [API Key Management Guide](https://docs.z.ai/devpack/notice/api-key-management.md), veröffentlicht **2026-10-16**, sagt: „API keys for the standard model API … and GLM Coding Plan are managed separately“.

| Variante | Credential und Budget | Was daraus nicht folgt |
| --- | --- | --- |
| Standard PAYG / Usage bundles, etwaige Free-/Promoangebote | Standard API Key; Accountbalance oder passende Bundles | Coding-Plan vorhanden macht diesen Key nicht zum Plankey. Kein öffentlich qualifizierter universeller PAYG-Restsaldoreader in den hier geöffneten Quellen. |
| Individual Lite/Pro/Max | Personal Coding Plan API Key; gewichtete Plancredits | **Lite genügt** für Planusage. Pro ist keine Monitoringanforderung. |
| Team | Team Plan Key; Seat-/Projektregeln, ggf. aktivierte Overage | Personalfenster ohne Org/Projekt passen nicht automatisch zum Team. |
| Vor 16.10.2026 erstellter Compatible key | Historisches Verhalten; bei `/api/paas/v4` APIbilling, bei Coding/Anthropic/Responses abhängig von Planhistorie | Weder Host noch Schlüsseltext allein beweisen die Billingklasse. |

Neue Keys werden über ihre Klasse abgerechnet, **nicht durch Wechsel des Endpoints**. Doku: „Changing the endpoint does not change the key's billing type“. Standardkey in Codingtool kann PAYG-Ausgaben verursachen. Abgelaufene Compatible-Planroute schaltet nicht automatisch zu PAYG um.

Die aktuelle [Übersicht](https://docs.z.ai/devpack/overview.md) dokumentiert Lite/Pro/Max jeweils 5h- und Wochencredits: 2.000/10.000, 12.000/60.000, 28.000/140.000. Modell-/Cache-/MCPfaktoren und Offpeakrabatt ändern Verbrauch. Statische Tabellen ersetzen niemals das tatsächliche Remaining. Die [FAQ](https://docs.z.ai/devpack/faq.md) belegt Wochenreset ab Bestellung; Resetkarten können Zyklus und Resetzeit neu starten. Es gibt in diesen Quellen keinen allgemein zugesicherten kostenlosen Coding-Plantrial. Fehlende Trialdoku begründet keinen Kaufzwang für vorhandenes Monitoring.

### Offizielle Reads und bessere Anmeldung

Das heute erneut geladene [offizielle Usage-Plugin](https://raw.githubusercontent.com/zai-org/zai-coding-plugins/main/plugins/glm-plan-usage/skills/usage-query-skill/scripts/query-usage.mjs) liest mit dem vorhandenen `ANTHROPIC_AUTH_TOKEN`, **roh in `Authorization`, ohne Bearer**:

- `GET https://api.z.ai/api/monitor/usage/quota/limit` — Planfenster, `data.limits[]`.
- `GET .../model-usage?startTime=...&endTime=...` — historische Modellnutzung.
- `GET .../tool-usage?startTime=...&endTime=...` — Toolnutzung.

Es kann auch offizielle Bigmodel-Domains ansprechen; Region/Produkt nicht mit dem internationalen lain-Reader vermischen. Der Pluginpostprocessor benennt jeden `TOKENS_LIMIT` pauschal „5 Hour“: **das ist eine vereinfachte ältere Anzeige**, kein Beweis, dass heute nur ein 5h-Fenster existiert. Neuere Credits/mehrere Fenster müssen erhalten bleiben.

[ZCodes aktueller Quota-Provider](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/services/src/usage-stats/providers/bigmodelUsageQuotaProvider.ts) bestätigt raw Auth; Teamreads benötigen `bigmodel-organization`, `bigmodel-project` und `type=2`. Er enthält zusätzlich Creditaktivität/-details und Modellperformance; deren Vertrag ist optional und separat. Planberechtigung und leere/fehlgeschlagene Usage sind dort ebenfalls getrennt. Daraus folgt: Same-Key-Planmonitoring ist belegbar; **kein OAuthzwang**, und ein fehlender Plan oder Teamheader darf nicht als „keine Anbieter-API“ enden.

**Offizieller Login ohne Copy/Paste existiert:** [ZCode CLI Pairing](https://raw.githubusercontent.com/zai-org/ZCode/main/apps/zcode-cli/packages/adapters/src/auth/cli-oauth.ts) erzeugt zufälligen Polltoken, startet `POST https://zcode.z.ai/api/v1/oauth/cli/init {provider:"zai"}` und pollt `/oauth/cli/poll/{flow_id}`. Ergebnis enthält ZCode-/Provideraccess und ggf. Refresh; Browser-URL kommt vom Anbieter. Das ist dokumentierter Herstellerquellcode, kein hier ausgeführter Login.

Der [Project-Token-Client](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/shared/src/project-access-token-client.ts) löst Konto/Organisation/Projekt/Keylocation auf, kann bei Bedarf einen Key anlegen und stellt einen kurzlebigen Access-Token über `POST /api/biz/v1/organization/{org}/projects/{project}/api_keys/{key}/access_tokens` aus (`clientType:"zcode"`). Er speichert abgeleitete Credentialwerte im Memory und erhält die Scopebindung. Somit ist dieser Bootstrap **nicht bloß ein Read und nicht automatisch ein freier Monitoring-OAuthscope**. Ein öffentlicher eigener lain-Clientregistrierungsvertrag ist aus diesen Quellen nicht belegt. Offiziellen Owner delegieren oder eigene Admission gezielt klären; keine fremde Clientidentität still übernehmen. Die Existenz dieses Flows widerlegt allerdings „Z.ai kann nur manuelle API-Keys“.

### lain-Stand und nächste Arbeit

`ZaiQuotaAdapter` ist registriert, akzeptiert eigenen API-Key an offizieller internationaler Basis, parst `CREDIT_LIMIT`, `TOKENS_LIMIT`, `TIME_LIMIT` samt `currentValue`, `usage`, `remaining`, `percentage`, `nextResetTime`. `ZaiConnectAdapter` ist noch unqualifizierter Managed-Stub. Für Team fehlen im direkten Reader die eben belegten Kontextheader und Queryumschaltung. Bestehenden Personalkey verwenden; Keyklasse im Setup erklären; Teamreader und scoped Owner ergänzen. Historische Modell-/Toolnutzung und Standard-PAYG-Balance nicht durch Planquote ersetzen. ZCode enthält außerdem einen eigenständigen Start-Plan-Billingzweig; das ist kein Nachweis, dass jeder Standardkey denselben Saldo lesen darf.

## 4. Kimi Code und Kimi/Moonshot Open Platform

### Aktuelle Tarifkorrektur, nicht „nur Pro“

Die am 25.10. geladene offizielle [Membership-Seite](https://www.kimi.com/code/docs/kimi-code/membership.html) ist hier entscheidend:

> „新会员取消每周额度限制 … Go 档无 coding 额度，Plus 及以上档位可使用 Kimi Code“

Deutsch: **Neue Mitglieder haben kein Wochenlimit; Go enthält keine Codingquota; Plus und höher können Kimi Code verwenden.** Alte Pläne behalten 7d-Fenster; neue und alte haben 5h-Rollfenster und teilen Monatsgesamtquota mit Kimi. Pro/alte Allegretto betreffen u.a. 1M-Kontext und Highspeed, **nicht allgemeines Planmonitoring**. Alle Geräte/Keys teilen dieselben Quoten. Kein allgemeines Free-/Trialcodingbudget ist hier zugesagt; einen fehlenden `limit_7d` bei neuem Plan nicht als Fehler darstellen.

**Extra Usage/Booster:** separate Wallet, mit Web-Kimi geteilt, optionale monatliche Ausgabebegrenzung. Zum Aktivieren/Aufladen Mitgliedschaft; verbleibender Betrag darf laut Seite nach Subscriptionablauf weiter verwendet werden, aber ohne aktive Subscription keine neue Aufladung. Daher „ohne aktives Pro kein Budget“ doppelt falsch. Regionale Währung aus Antwort übernehmen: die chinesische Membershipseite spricht RMB, der Herstellerparser hat USD-Fallback; ein internationaler Reader muss Currency/Missing ausdrücklich behandeln.

**Open Platform PAYG:** Eigener Plattformaccount, eigener API-Key, eigene regionale Balance. Coding-Abos und Booster sind nicht diese PAYG-Balance. Die [aktuelle Produktübersicht](https://www.kimi.com/code/docs/) nennt für eigene Produkte/Teamverwaltung ausdrücklich die Open Platform. Keine dokumentierte Free-/Trialcredit-Zuteilung aus einem erfolgreichen Login folgern.

### Messobjekte, Endpoints, Credentials

| Weg | Daten und Auth | Einschränkung |
| --- | --- | --- |
| `GET https://api.kimi.com/coding/v1/usages` bzw. global `https://api.kimi.ai/coding/v1/usages` | Bearer Coding-Key **oder** offizieller verwalteter OAuthaccess; [aktueller offizieller Reader](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/managed-usage.ts) liest `usages.limit_5h`, `limit_7d`, `limit_month_total`, `limit_month_code` mit `used_ratio`/`reset_time`, optional `boosterWallet`. | Nur tatsächlich vorhandene Fenster. Prozent ist Ratio 0..1. Keine universelle feste Tokenanzahl oder Modellrestzahl. |
| Offizieller lokaler Server `GET http://127.0.0.1:<port>/api/v1/oauth/usage` | [Server-API](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/docs/en/reference/server-api.md): lokaler Bearer, remote Owner hält OAuth; JSON `code=0`, `data.kind=ok|error`, `quota.usages`, `extraUsage`. | Experimenteller versionsgebundener Serververtrag. HTTP 200 + code 0 kann trotzdem `kind:error` vom Upstream enthalten. Lokaler Serverbearer ist breit, nicht automatisch read-only. |
| Open Platform `GET https://api.moonshot.ai/v1/users/me/balance` | [Offizielle Balance-Doku](https://platform.kimi.ai/docs/api/balance): **derselbe MOONSHOT_API_KEY als Bearer**; USD `available_balance`, `voucher_balance`, `cash_balance`; `code=0`, `status`. | Cash kann negativ sein; available ist bei negativer Cashbalance laut Doku gleich Voucher, also nicht immer naive Summe. Keine Reset-/Gesamtzuteilung erfinden. Regional falscher Key ergibt 401. |

Die Balance-Doku wurde über `https://platform.moonshot.ai/docs/api/balance` geladen und offiziell nach `platform.kimi.ai` weitergeleitet. Sie sagt ausdrücklich, Keys von `platform.kimi.ai` und `platform.kimi.com` seien unabhängig. Chinesischen `/api.moonshot.cn/`-Geldvertrag und Währung separat qualifizieren, statt den USD-Reader blind umzulenken.

### OAuth und CLI/Harness

[Offizielles Getting Started](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/docs/en/guides/getting-started.md) trennt „Kimi Code (OAuth) — device-code flow“ und „Kimi Platform API key“. `KIMI_CODE_HOME` erlaubt eigene Datenbasis. Die aktuelle Website sagt zudem, der alte Python-CLI werde nicht mehr gepflegt; neue Integration deshalb auf TypeScript-Kimi-Code versionsbinden, nicht die alte Python-CLI als einzig aktuellen Owner betrachten.

Der weiterhin öffentlich einsehbare [Python-Authsource, Pin `9ab1286b`](https://raw.githubusercontent.com/MoonshotAI/kimi-cli/9ab1286b8fe4e6bcd116949a27ce5e0ac3389c82/src/kimi_cli/auth/oauth.py) belegt den zugrundeliegenden Flow: `https://auth.kimi.com/api/oauth/device_authorization`, feste offizielle CLI-Client-ID; `/api/oauth/token` mit Devicegrant bzw. Refreshgrant; Response liefert Scope, Access-, Refresh- und Ablaufdaten. Kein Clientsecret im Flow. Die Client-ID und ein MIT/Apache-artig lizenzierter Client beweisen **nicht selbst** beliebige Drittclientzulassung oder öffentliches Dynamic Client Registration. Keine separate Drittregistrierung als pauschale Nutzerpflicht behaupten, solange kein solcher Vertrag belegt ist.

Beste Delegationsalternative ist **offiziellen Kimi-Code-Owner** über den dokumentierten lokalen Server nutzen: Login starten/pollen/canceln, Userinfo und Usage als JSON lesen; kein `/usage`-Screenscraping, kein Refresh-Tokenkopieren notwendig. Isolation, Version, Bearerübergabe und Accountbindung bleiben Implementierungs-/Qualifikationsarbeit. Für einen vorhandenen Coding-Key ist das alles **optional**. Die offizielle Produktseite fordert echte Clientidentität; User-Agent-Manipulation kann Rechte sperren. Lain nicht als Kimi CLI tarnen.

### lain-Stand und nächste Arbeit

`KimiQuotaAdapter` liest Same-Key-Codingfenster; `KimiManagedUsageParser` existiert separat, ist aber noch kein aktiver delegierter Serverreader; `KimiConnectAdapter` ist unqualifizierter Managed-Stub. Booster ist im direkten aktiven Reader nicht verarbeitet. Für die konkrete Open-Platform-Geldbalance ist kein entsprechender aktiver Reader vorhanden. Nächste Pakete: API-Key-Coding behalten, moderne fehlende Wochenfenster korrekt darstellen; Booster mit präziser Geldsemantik; offizieller delegierter Serverowner; **separater Same-Key-PAYG-Balancereader**. Keine neue Pro-Subscription erforderlich.

## 5. Xiaomi MiMo

### Produkt, Tarif und genaue Quotaquelle

Die [Individual-Plan-Doku](https://mimo.mi.com/static/docs/tokenplan/Token%20Plan/subscription.md) nennt Lite/Standard/Pro/Max und gewichtete **Credits**, monatliche oder jährliche Gesamtquota. Lite ist eine echte Planvariante; Pro ist keine Monitoringvoraussetzung. Modell-/Cache-/Offpeak-/Audiofaktoren machen Credits zu etwas anderem als rohe Tokens. Alle kompatiblen Tools teilen den Pool. Annual Fixed Quota nicht durch zwölf teilen und als Monatsreset ausgeben. Planende nicht automatisch Refill. Erschöpfung belastet nicht automatisch PAYG-/Geschenkguthaben.

PAYG verwendet einen eigenen Standardkey und Geldbalance; Team verwendet eigenes Projekt/Seat; Individualplan einen `tp-...`-Key an der vom Konto gelieferten regionalen `token-plan-{cn,sgp,ams}.xiaomimimo.com`-Basis. Free-/Promotion sind getrennt: Die [MiMo-Code-Anleitung](https://mimo.mi.com/static/docs/tokenplan/integration/mimo-code.md) bewirbt zeitlich begrenzt **1.000 Websuchen/Tag** nach Autorisierung, setzt für Login aber ausreichende Balance oder gültigen Plan voraus. Das ist weder 1.000 kostenlose Modellrequests noch ein universeller Trialplan.

### Offizieller Bootstrap und Console-Reads

`mimo auth login`, `/login` bzw. `/connect` sind offiziell dokumentiert: Browserautorisierung oder Plattformcode; Auswahl PAYG/Token Plan, kein manuelles Keykopieren. [Aktueller offizieller MiMo-Code-Source](https://raw.githubusercontent.com/XiaomiMiMo/MiMo-Code/main/packages/cli/src/plugin/mimo.ts) zeigt `https://platform.xiaomimimo.com/authorize?pk=...&redirect_uri=...&kn=mimocode&key_name=...`, X25519/AES-GCM-verschlüsselten Callback mit `{sk,uid,url}`. Technisch ein **API-Key-Bootstrap**. Keine standardisierte Access-/Refresh-Token-/Scopeverhandlung und kein öffentlicher eigener Drittclientregistrierungsvertrag in den geöffneten Quellen. Der Keycallback liefert keine Konsolencookies.

Die am 25.10. erneut geladenen **offiziellen öffentlichen Webassets** (die aktuelle unangemeldete Plattform-HTML referenziert weiterhin genau dieses Hauptbundle) [Hauptbundle](https://platform.xiaomimimo.com/static/main.569e3e15e1d2bea2.chunk.js) und [Planansicht](https://platform.xiaomimimo.com/static/279.17edd5ab4b3a18cd.chunk.js) belegen diese Console-GETs unter `https://platform.xiaomimimo.com/api/v1`:

| GET-Route | Objekt/Scope |
| --- | --- |
| `/tokenPlan/usage` | Individual `data.usage.items[]`, `name/used/limit/percent`; Ratio als Prozent skalieren, mehrere Pools erhalten |
| `/tokenPlan/detail` | Individualplan und `currentPeriodEnd`; Gültigkeitsende separat von Quotareset |
| `/project/{projectId}/teamTokenPlan/usage` | Teamgesamtusage, `usedPercent`, `nextResetTime` |
| `/project/{projectId}/teamTokenPlan/my/seat` | Seat `creditsUsed/creditsTotal`, historische Seatnutzung und Reset; nicht eigener Benutzerverbrauch seit Null |
| `/project/{projectId}/teamTokenPlan/detail` | Teamplan/Settlement/Seatlimit/Gültigkeit |

Das Hauptbundle verwendet **`credentials:"same-origin"`**, `x-timeZone` und `Accept-Language`. Cookiepolicy nennt Login-Cookies mit 24 Stunden; der UI-Disclaimer sagt Datenverzug bis fünf Minuten, Tagesfinalisierung 07:00 UTC am Folgetag. Dies sind Belege für eine aktuelle Console-Implementation, **keine zugesicherte stabile öffentliche Drittanbieter-API oder unbeaufsichtigte Refreshgarantie**. Balance-/Usage-Console existiert; ein stabiler öffentlicher Inferenzkey-Readvertrag für `/balance` ist hier nicht belegt. PAYG-Balancefeldschema war im älteren Bericht teilweise aus CodexBar abgeleitet; es wird hier nicht ungeprüft zur neuen offiziellen API-Garantie hochgestuft.

### lain-Stand und beste Anbindung

Wichtig gegenüber den alten WIP-Dokumenten: **`cbf083f` hat bereits `MiMoQuotaAdapter` und eine separate Console-Browserfähigkeit für Individual Token Plan.** Der Adapter kontrolliert Identität vor/nach dem Usage-Read, verwendet private Profilreferenz und Capabilitygeneration, setzt Planende separat. Er verlangt Wiederanmeldung nach Browserschließung, Hostrestart oder Sessionablauf. Der nackte Fallbacktext „No verified direct MiMo quota API“ darf dieses vorhandene Console-Monitoring nicht verschweigen. PAYG und Team sind im aktiven Reader nicht umgesetzt.

Beste nächsten Schritte: bestehenden Individual-Console-Monitor verwenden und dessen Ablauf klar darstellen; offiziellen CLI-Keybootstrap getrennt für Inferenz qualifizieren; Team-/Seatreader mit expliziter Projektauswahl, PAYG-Balancereader mit tatsächlichem Schema ergänzen. Native eigene Browserdelegation braucht genaue Hersteller-/Clientvertragsklärung, aber kein pauschales „OAuth unmöglich“. Eigene isolierte Nutzersitzung ist eine konkret belegte Alternative; keine bestehenden Browsercookies heimlich importieren. Monitoringlogin allein ist kein Inferenzcredential und umgekehrt.

## 6. Synthetic

### Heute belegter Anschluss

Die [offizielle Quota-Doku](https://dev.synthetic.new/docs/synthetic/quotas) dokumentiert:

> `GET https://api.synthetic.new/v2/quotas`, `Authorization: Bearer SYNTHETIC_API_KEY`; „/quotas requests do not count against your subscription limits!“

**Derselbe Inferenzkey reicht**, kein Managementkey oder OAuth erforderlich. Das Dokumentbeispiel ist ein älteres `subscription.limit/requests/renewsAt`-Schema. Die aktuelle [offizielle Octofriend-Parserquelle](https://raw.githubusercontent.com/synthetic-lab/octofriend/main/source/utils/quota.ts) und [Fetch-/Anzeigequelle](https://raw.githubusercontent.com/synthetic-lab/octofriend/main/source/components/menu-quota-indicator.tsx) lesen denselben Endpoint und qualifizieren neue Formen:

| Form | Verlässlich ablesbar | Nicht verwechseln |
| --- | --- | --- |
| `planWeeklyLimit` | `percentRemaining`, `nextRegenAt`, `nextRegenPercent` | Prozent verbleibender gewichteter Plancredits; nächste Teilregeneration, kein Vollreset |
| `weeklyTokenLimit` | `maxCredits`, `remainingCredits`, `nextRegenCredits` als Strings, Prozent, nächste Regenzeit | Feldname bedeutet nicht rohe Tokenquota |
| `rollingFiveHourLimit` | `remaining`, `max`, `nextTickAt`, `tickPercent` | Legacy-/explizit gelieferte Nebenbegrenzung; keine aktuelle 5h-Planvorgabe |
| `subscription` | dokumentierter Altvertrag | Nur als passende Alternative, nicht zu moderner Wochenquote aufsummieren |

Die [heutige Limitseite](https://synthetic.new/rate-limits) sagt „Synthetic only uses weekly limits … there aren't additional five-hour or monthly limits“ und alle **202 Minuten +2%**. Keine lokal berechnete wöchentliche Vollauffüllung. [Pricing](https://synthetic.new/pricing) nennt Go $9, Standard $30, Big $90; keine Pro-Pflicht. Free/Trial ist in den geöffneten aktuellen Preis-/Quota-/Einstiegsseiten nicht als allgemeiner garantierter Plan belegt. Dies sagt nichts über individuell vorhandene Credits/Aktionen.

Extra Usage/PAYG zu APIpreisen ist eine eigene Abrechnungsdimension. `/v2/quotas` beweist keinen Geldsaldo oder globale PAYG-Costhistory. Fastmode verbraucht mehr Quote; Infinite-Mode ist nur bestimmte Big-/Modellfunktion, kein allgemeines „unlimited“. Die Preisseite nennt „1 Usage Reset/month“ als Leistung; das ist kein monatliches Consumptionfenster.

### Login, Baseline und konkrete Lücken

[Getting Started](https://dev.synthetic.new/docs/api/getting-started) sagt „Log in to see your API key“ und verlinkt Billing. Dokumentierter öffentlicher eigener OAuth-/Device-/Create-Key-Delegationsvertrag ist in diesen Quellen nicht belegt; Octofriend nutzt `auth.type === "apiKey"`. Das ist eine begrenzte Nichtfund-Aussage. Für bekannte Planquote bringt ein Cookie-/Browsermonitor keinen notwendigen Mehrwert. API-Key einmal privat verbinden genügt; für PAYG-Billing bleibt die Console eine konkrete Alternative.

**Codekorrektur gegenüber dem 24.10.-WIP:** `SyntheticQuotaAdapter` in `cbf083f` verarbeitet bereits `planWeeklyLimit`, `weeklyTokenLimit`, `rollingFiveHourLimit` und den Legacyfallback, einschließlich exakter Creditstrings und eigener NextRegeneration-Felder. Daher nicht nochmals als komplett fehlender moderner Adapter planen.

**Frisch gefundenes präzises Einheitenproblem:** Der offizielle Octofriend-Renderer berechnet bei `rollingFiveHourLimit` `entry.tickPercent * 100`; beim neuen `planWeeklyLimit.nextRegenPercent` dagegen keine Multiplikation. Lain übergibt `tickPercent` derzeit unmittelbar an `NextRegenerationPercent`. Diese zwei Einheiten sind also **nicht austauschbar**; Fixture mit z.B. `tickPercent:0.02` muss 2% nächste Regeneration ergeben, `nextRegenPercent:2` bleibt 2%. Das ist eine Implementierungslücke, keine Auth-/Tarifgrenze. Zudem bevorzugt der Herstellerparser `planWeeklyLimit` exklusiv, während lain bei gleichzeitig vorhandenem Rollingblock noch eine Zeile ergänzt; Koexistenzsemantik gezielt qualifizieren, keine doppelten Pools behaupten.

Beste nächste Arbeit: Einheitenkorrektur/Schemafixtures, Quota-Read mit bestehendem Key bei später autorisierter Accountqualifikation, optional passiver Headerpfad separat. Für PAYG-Geldsaldo gezielten Herstellervertrag suchen/erfragen; nicht „nur Pro/OAuth“ anzeigen.

## Übergreifende Grenzen und Akzeptanzkriterien

1. **Keine echte Accountdiagnose aus Doku:** `not-configured`, `permission-denied`, `expired`, `source-unqualified`, `reader-unimplemented`, `no-data`, `stale`, `exhausted` brauchen unterschiedliche Gründe und Aktionen. Ein 401/403 oder fehlendes JSONfeld ist keine Null und kein Tarifnachweis.
2. **Ausreichenden vorhandenen Zugang wiederverwenden:** Kein zweiter Login für Same-Key-Reads. Ein eigener Admin-/Console-/CLIzugang nur dort anbieten, wo er das konkrete fehlende Messobjekt erschließt.
3. **Scope und Region sichtbar:** Coding vs PAYG, Personal vs Team/Seat, CN vs global, Account vs Org vs Key. Ein externer Gateway mit Kimi-/MiMo-Modellnamen ist nicht der direkte Anbieterpool.
4. **History ist keine Remainingquota:** Lokale Requests/Tokens helfen immer nur für den über lain beobachteten Verkehr; sie ersetzen weder anbieterweiten Verbrauch noch gewichtete Aboressourcen. Unbenutzter API-Key und erfolgreiches Login beweisen kein Guthaben.
5. **CLI gezielt delegieren:** Version, isolierte Datenbasis, JSONvertrag, lokaler Bearer, Abbruch und Ownership qualifizieren. Ein interaktiver `/usage`-Text und ein kopierter Refresh-Token sind kein gleichwertiger Integrationsvertrag. Keine Inferenz zum Erzeugen eines Quota-Messwerts.
6. **Keine ungeprüften Vollresetzeiten/Nenner:** Codex Pro ohne 5h, neue Kimi-Pläne ohne 7d, Synthetic Teilregeneration, MiMo Jahresgesamtquota und Command-Code-Promotions sind konkrete Gegenbeispiele.

Quellenabrufe erfolgten am 2026-10-25. Webseiten sind veränderlich; öffentliche Quellcodes unter `main` belegen den an diesem Tag ausgelieferten Stand, keine künftige Vertragsstabilität. Der verfügbare allgemeine Websuchtool-Aufruf lieferte einen leeren Fehler; alle tragenden Aussagen oben stammen aus danach tatsächlich geladenen offiziellen Seiten/Dateien. Alte oder fehlgeschlagene URLs werden nur als Recherchegrenze, nicht als Nachweis fehlender Anbieterfähigkeit verwendet.
