# API-Anbieter: Quota, Auth und Onboarding

Stand: **25.10.2026**, Quellcodebezugsstand **`cbf083f`**. Scope: Groq,
NVIDIA Build, Cohere, SambaNova, Upstage, Fireworks, DeepSeek, MiniMax und xAI.
Nur öffentliche unauthentifizierte GETs auf Dokumentation, öffentliche
Herstellerquellen und ausgelieferte Webbundles; keine lokalen Credentials,
Provider-Accountreads, Inferenz, Anmeldung, Refresh, Installation oder Builds.
Die relevanten gelesenen Produktdateien hatten keinen Working-Tree-Diff.
Dieses Dokument ist ein Research-/Abnahmevorschlag, keine Produktlieferung.

Alle nachfolgend als **heute gelesen** verwendeten URLs wurden am 25.10.2026
erfolgreich abgerufen. Wo HTML 403 lieferte, wurde die offizielle `.md`-Fassung
gelesen. Consolebundles belegen existierenden First-Party-Code, **keinen
zugesicherten öffentlichen Integrationsvertrag**. Fehlende Dokumentation ist
`unverified`/`source-unqualified`, kein Beweis für eine prinzipiell unmögliche API.

Gegengeprüft: [quota-sources](../providers/quota-sources.md),
[monitor-auth](../providers/monitor-auth.md),
[Entscheidungsmatrix](../provider-decision-matrix-2026-10-24.md),
[Providerpakete](../provider-work-packages/README.md) samt first-wave und
parallel-handoff, sowie die vertiefenden [Auth-/Quota-Berichte](../quota-auth-research-2026-10-24.md).
Diese fremden Dokumente bleiben unverändert. Ihre historischen Angaben zu
fehlenden Adaptern/Connectors werden hier nicht als heutiger Iststand übernommen.

## Entscheidung auf einen Blick

| Anbieter/Produkt | Im Quellstand vorhanden | Tatsächlich belastbare Messgröße | Unabhängiger Monitor / größte Lücke |
| --- | --- | --- | --- |
| Groq Free/Developer | Gateway, persistierte passive Header | Beobachtetes RPD-/TPM-Limit, Rest und Reset | Kein unabhängig lesbarer gewöhnlicher Kontoreader qualifiziert; Header benötigen echte beobachtete Anfragen. |
| Groq Enterprise | Zusätzlich registrierter Prometheusreader | Modell-/Projekt-Raten über 5 Minuten | Unabhängig lesbar, aber **kein Restbudget**; Enterpriseberechtigung und Liveprobe offen. |
| NVIDIA Build Developer | Gateway, statische Produktreferenz | Öffentliches „bis zu 40 RPM / 10.000 RPD“ | Keine accountbezogene Usage-/Rest-/Resetquelle oder delegierte Auth qualifiziert. |
| Cohere Evaluation/Production | Gateway; Consoleconnector ausdrücklich unavailable | Einzelrequest-Usage; veröffentlichte Tariflimits | Console-RPCs/Pairing sind konkrete Kandidaten, noch kein angebundener Accountmonitor. |
| SambaCloud Free/Developer | Gateway; Consoleconnector unavailable | Dokumentierte passive RPM-/RPD-Header; Console-Usage/Credits als Kandidat | Headerbrücke/Reader fehlen; Consoleauth separat unqualifiziert. |
| Upstage direkte API | Gateway; Consoleconnector unavailable | Einzelrequesttokens; Console-Org-Spend/Credits/Usage als Kandidat | Kein angebundener Kontomonitor; Session-/Org-/Rollenvertrag fehlt. |
| Fireworks Standard Serverless | Gateway, registrierter Quota-RESTreader | Accountconstraints mit `value`, `maxValue`, `usage` | Kostenhistory noch nicht angebunden; Prepaidbalance nicht aus Spendcap ableiten. CLI-Login **gesperrter Platzhalter**. |
| DeepSeek direkte API | **Geplant**, kein eigener Katalog-/Quotaadapter | Öffentlich dokumentierte Geldbalance | Kleiner Reader mit normalem API-Key möglich; vorhandener Account noch nicht qualifiziert. |
| MiniMax M Plan / PAYG | **Geplant**, kein eigener Katalog-/Quotaadapter | Offizieller CLI-Vertrag für Planfenster; separat PAYG-Balance | Offizieller Device-/CLIweg und Schema vorhanden; Semantik, Produkt-/Regionsbindung und Livezugang fehlen. |
| xAI API-Team | Gateway und API-Key-Teamdiscovery | Management-API: Prepaidledger, Postpaidlimit/-Rechnung, Usagehistory | Separater Managementreader und kleinste Billing-ACL fehlen; Grok-Build-Login ist anderes Produkt. |

„Vorhanden“ bedeutet Quellcode/Registrierung, nicht installiert oder live
abgenommen. Modellfamilien bei anderen Hosts sind keine direkten
DeepSeek-/MiniMax-Provideranschlüsse.

## 1. Groq: Free-/Developer-Header und Enterprise-Metriken trennen

**Primärbelege, heute gelesen.**
[Rate Limits](https://console.groq.com/docs/rate-limits.md):
`x-ratelimit-limit/remaining/reset-requests` „Always refers to Requests Per Day
(RPD)“; entsprechende `-tokens` „Always refers to Tokens Per Minute (TPM)“.
Resetbeispiele `2m59.56s` und `7.66s` sind Dauern. Der Guide unterscheidet
Free, Developer und höhere Enterprise-Limits. Die Werte hängen von Modell und
Organisation ab; keine keybezogenen unabhängigen Pools behaupten.
[Prometheus](https://console.groq.com/docs/prometheus-metrics.md):
„This feature is only available to our Enterprise tier customers“ und
„All metrics are broken out by model and project id“, als `rate5m`.

**Endpoint/Auth/Scope.** Im gewöhnlichen Gatewaybetrieb werden die sechs Header
an der tatsächlichen Inferenzantwort mit dem vorhandenen API-Key beobachtet.
Es gibt dafür keinen unabhängigen Read-Endpoint. Enterprise nutzt
`GET https://api.groq.com/v1/metrics/prometheus/api/v1/query?query=…`,
`Authorization: Bearer <Groq API key>`, konkrete `project_id`-Filter.
Belegte Metriken sind `model_project_id_status_code:requests:rate5m`,
`model_project_id:tokens_in:rate5m`, `model_project_id:tokens_out:rate5m`.
Das sind Requests/Sekunde bzw. Tokens/Sekunde, keine Tageszähler, Kosten,
Prepaidbalance oder Restfenster. Eine 5-Minuten-Rate darf nicht ohne belegte
vollständige Zeitreihe zur Monatsusage aufintegriert werden.

**Onboarding/Workaround.** Vorhandenen Inferenzkey behalten; Console-Account und
Key bei Bedarf vom Nutzer unter [API Keys](https://console.groq.com/keys)
anlegen. Für Enterprise zusätzlich Projekt und vorhandene Berechtigung wählen;
ein optionaler separater Key ist keine zusätzliche Enterprisefreischaltung.
Kein öffentlicher eigener OAuth-/Deviceclient oder offizieller Quota-CLI-Login
in den geprüften Quellen qualifiziert. Die Prometheus-/Grafanaanbindung ist
ein dokumentierter Enterprise-Workaround, kein Free-Monitor. Free/Developer:
[Usage](https://console.groq.com/dashboard/usage) und
[Limits](https://console.groq.com/settings/limits) als konkrete manuelle Wege.
Kein Enterpriseabschluss nur für den Monitor und keine Probeinferenz zum Refresh.

**Iststand.** [GroqQuotaAdapter](../../src/Lain.Host/Quota/GroqQuotaAdapter.cs)
liest lokale Corebeobachtungen; [GroqAccountQuotaAdapter](../../src/Lain.Host/Quota/GroqAccountQuotaAdapter.cs)
ergänzt nur nach explizitem Monitoropt-in den bereits registrierten
[GroqEnterpriseUsageReader](../../src/Lain.Host/Quota/GroqEnterpriseUsageReader.cs).
Der Reader bewahrt zugrunde liegende Sampletimestamps, Projekt/Modell und
Ratenart. Ohne Monitoropt-in erfolgt kein Groq-Request. Wiederlesen alter Header
ist keine neue Beobachtung. Alte Dokumentation „nur passiv“ ist daher unvollständig;
„Groq-REST liest Abo-/Accountrest“ wäre ebenfalls falsch.

**Abnahmegrenze.** Getrennte Fälle Free/Developer ohne Beobachtung, mit alter
Beobachtung, Enterprise ohne Berechtigung und Enterprise mit passender Serie.
Keine Aufwertung einer Rate zur Restquota, keine neue Frische durch Polling,
kein implizites Enterpriseprobieren. Ein Pro-/Developerupgrade ist nicht als
automatische Monitorreparatur zu empfehlen.

## 2. NVIDIA Build: Produktlimit ist kein Konto-Restwert

**Primärbelege, heute gelesen.**
[Offizieller Runtimeinhalt](https://build.nvidia.com/api/runtime/content/copy.yaml)
liefert trotz Endung JSON:
`requestsPerMinute: "Up to 40 rpm"`,
`requestsPerDay: "10,000 requests per day"`,
„Rate limits may vary by model and traffic from other users may cause throttling.“
[Produkt-FAQ](https://docs.api.nvidia.com/nim/docs/product.md):
„Members of the NVIDIA Developer Program have free access to NIM API endpoints
for prototyping“. Selfhost-NIM/Enterprise-Lizenzen werden getrennt beschrieben.
[Quickstart](https://docs.api.nvidia.com/nim/docs/api-quickstart):
„Get API Key“, Anmeldung/Developer-Programm und „A pop up window will show your
new API Key“.

**Endpoint/Auth/Scope.** Der öffentliche Runtime-GET braucht keine Authentifizierung
und liefert keine Account-ID, Verbrauchsmessung, Remainingmenge oder Resetzeit.
Der NVIDIA Developer-/API-Catalog-Key autorisiert Build-Inferenz; ein belegter
Build-Kontoread mit diesem Key oder einem zusätzlichen minimalen Monitortoken
fehlt. NGC-Registryrollen, Cloud-Functions-Zugänge, Enterprise-Telemetrie und
die Enterprise-Testlizenz beantworten nicht den Build-Saldobedarf.

**Onboarding/Workaround.** Nutzer öffnet [Build](https://build.nvidia.com/),
meldet sich an und erzeugt den vorgesehenen Key. Kein neuer Key/NGC-Adminzugang
ist als Lösung für unbekannte Accountquota belegt. Öffentlicher eigener
OAuth-/Deviceclient und offizielle Build-Quota-CLI: in den untersuchten
Quellen **unverifiziert**. Das ist keine Aussage gegen möglichen privaten
Consoleinhalt. Manuell nach tatsächlich accountbezogener Usage/Resetanzeige
sehen; einen unterstützten Export/Endpoint gegebenenfalls vom Provider bestätigen
lassen. Gewöhnliche Codingharnesses würden nur denselben Inferenzkey nutzen.

**Iststand.** [NvidiaBuildPublicReference](../../src/Lain.Host/Quota/NvidiaBuildPublicReference.cs)
ist reine Offline-Normalisierung/datiertes Hilfetextmaterial, kein Poller und
keine gemessene `QuotaReading`. [QuotaService](../../src/Lain.Host/Quota/QuotaService.cs)
liefert diesen `console`-Hinweis. Die Datenquelle wurde heute erneut bestätigt;
das im Code feste Quelldatum 24.10. ist weiterhin Provenienz, kein letzter
erfolgreicher Accountabruf.

**Abnahmegrenze.** Kein „10.000 übrig“, „unbegrenzt“, Countdown oder Balken aus
diesem Angebotstext. Erst mit belegtem Accountendpoint + Auth + Einheit +
Pool/Reset + echter lesender Probe einen Kontomonitor freigeben.

## 3. Cohere: Evaluation, Production und Consolepairing

**Primärbelege, heute gelesen.**
[Limits](https://docs.cohere.com/docs/rate-limits.md): Evaluationkeys sind
„free but limited in usage“, Productionkeys „paid and much less limited“.
„Trial keys (and prod keys on newer Chat model variants) are limited to 1,000
API calls a month.“ Zudem: „Prod keys work like trial keys for newer model
variants such as Command A Reasoning“; passende Productionfreigabe ist gesondert.
Damit ist ein Productionkey allein **kein** Beleg höherer Modelllimits.
[Offizielle OpenAPI](https://raw.githubusercontent.com/cohere-ai/cohere-developer-experience/main/cohere-openapi.yaml)
enthält Bearer-Keyauth und Inferenz/Datasets/Modelle/Keycheck, aber keinen
allgemeinen Inferenzbudget-/Billingpoller im überprüften Pfadsatz.

**Messung/Endpoint.** Request-Usage beschreibt einzelne Antworten, nicht den
organisationsweiten Rest von 1.000 Calls. `/v1/datasets/usage` ist
Datasetstorage, kein Trialcallbudget; `/v1/check-api-key` keine Quotaquelle.
Die öffentliche Limitseite liefert keinen individuellen Resetzeitstempel,
Abrechnungszeitzone oder Zähler anderer Clients. Daher niemals
`1.000 − lokale lain-Calls` als echten Kontorest ausgeben.

**Konkrete zusätzliche Quelle.** Das heute erneut geladene
[Dashboard-Appbundle](https://dashboard.cohere.com/_next/static/chunks/pages/_app-93016fb49a1d0e50.js)
belegt RPCs unter `https://production.api.os.cohere.com/rpc/BlobheartAPI/`:
`GetAPIUsage`, `GetAPIUsageV2` → `usages`, `GetBillingActivity` → `activity`;
`CLIAuthBegin` → `browserURL, secret`, `CLIAuthCheck` → `token` und
`CLIAuthConfirm` existieren. Es ist ein realer First-Party-Pairingkandidat,
kein verifizierter Standard-OAuth-Device-Grant. Org-/Zeitfilter, Monatsbudget
und Billinghistory wurden im [Vorbericht](../quota-auth-research/samba-upstage-cohere.md)
an den Billingbundles beschrieben; ihre vollständige Antwort-/Rollenqualifikation
ist weiterhin offen. Der ausgegebene Token ist nicht als minimaler Readkey oder
als normaler Inferenzkey nachgewiesen.

**Onboarding/Workaround.** Nutzer wählt im
[Dashboard/API Keys](https://dashboard.cohere.com/api-keys) ausdrücklich einen
Evaluationkey; Production und einzelne Modellrechte separat. Ein eigener
öffentlich registrierbarer OAuthclient, ein heute qualifizierter CLI-Befehl
zum Accountquotalesen und automatischer Account-/Keybootstrap wurden nicht
belegt. Bundle-Keymethoden belegen Möglichkeit im Herstellerclient, keine
freigegebene Drittapp-Automation. Console-Usage manuell nutzen; offizielles
Pairing zunächst hinsichtlich Tool/Distribution, Tokenlebensdauer, Org und
Readscope qualifizieren. Kein Connector-OAuth mit Billinglogin gleichsetzen.

**Iststand/Abnahmegrenze.** Gateway vorhanden; `QuotaService` meldet
„Quota lookup is not implemented for Cohere“,
[ConnectionMethodCatalog](../../src/Lain.Host/Accounts/ConnectionMethodCatalog.cs)
führt Consolelogin unavailable. Kein Runtime-Billingreader aus den RPCnamen
ableiten. Erst eigene identitätsgebundene Session + repräsentative Usageantwort
und klare Evaluation-/Production-/Modellgrenzen abnehmen.

## 4. SambaNova: Free-/Developer-Header plus separater Consolekandidat

**Primärbelege, heute gelesen.**
[Limits](https://docs.sambanova.ai/docs/en/models/rate-limits):
„Free Tier: Applied when there is no payment method linked“, Developer mit
Zahlungsmethode, Developer zusätzlich „20M tokens per day across all models“.
Für `Meta-Llama-3.3-70B-Instruct`: Free 20 RPM/20 RPD/200.000 TPD,
Developer 240 RPM/48.000 RPD. Diese Zahlen sind Modell-/Tarifreferenzen, keine
Beobachtung eines konkreten Accounts. Die Seite dokumentiert:

| Grenze | Limit / Rest / Resetheader |
| --- | --- |
| RPM | `x-ratelimit-limit-requests`, `x-ratelimit-remaining-requests`, `x-ratelimit-reset-requests` |
| RPD | Entsprechende drei Namen mit Suffix `-day` |

Reset bedeutet „Time until reset“; öffentliches Format/Zeiteinheit bleibt in
dieser Seite offen. Keine TPD-Restheader daraus erfinden. Die heute gelesene
[offizielle Inferenz-OpenAPI](https://raw.githubusercontent.com/sambanova/sambanova-inference-api-spec/refs/heads/main/openapi.documented.json)
enthält Chat/Responses/Messages/Embeddings/Audio/Modelle und Bearer- bzw.
Messages-`x-api-key`-Auth, keinen unabhängigen Cloudquota-/Billingpfad.

**Consolequelle.** Das heute erneut geladene
[Datenbundle](https://cloud.sambanova.ai/_next/static/chunks/35360-10acd791f887f198.js)
belegt `GET /api/auth:me`, `/api/usage?user_name=…&start_date=…&end_date=…`
(optional `model_id`, `api_key_hash`) und `/api/credits?user_name=…` unter
`https://cloud.sambanova.ai`. Das ist historischer Verbrauch bzw. Credits,
nicht RPM-/TPD-Rest. Das Bundle enthält ausdrücklich den Hinweis
„New credits take a few minutes to appear“.
Der [Vorbericht](../quota-auth-research/samba-upstage-cohere.md) nennt zusätzlich
`/resources/credits/balance` → `amount, readAt` und Grants/Ablauf; dieser zweite
Pfad wurde in dieser Runde nicht erneut eigenständig qualifiziert.

**Auth/Onboarding/Harness.** Der vorhandene API-Key genügt zur passiven
Headererfassung, ohne weitere Grants. [Keydoku](https://docs.sambanova.ai/docs/en/get-started/api-keys-urls.md):
„Each user can generate and use up to 25 API keys“, Anlage im
[SambaCloud-Portal](https://cloud.sambanova.ai/apis), neue Keys sicher speichern.
Nutzer braucht für Free keine Zahlungsmethode; Keyanzahl vermehrt kein Budget.
Consolezugang ist eine andere Credentialfähigkeit, nicht nachgewiesener
Inferenzkey-Bearerzugang. Öffentlicher Cloud-OAuth-/Deviceclient oder passende
Cloudquota-CLI bleiben unqualifiziert; SambaStudio/SambaStack sind andere
Produkte. Ein Harness mit API-Key liefert nur neue normale Inferenzantworten,
keine unabhängige Monitorberechtigung.

**Iststand/Abnahmegrenze.** Gateway vorhanden; kein Samba-Quotaadapter und
Consolelogin unavailable. Passive Header nach dem vorhandenen Groqmuster
ergänzen, aber Groqs RPD-/TPM-Bedeutung nicht kopieren. Ungesichertes
Resetformat → Reset unbekannt. Ohne Gatewayverkehr bleibt der Monitor leer/alt.
Consoleadapter getrennt mit Useridentität, Grants, Währung und Verzögerung
qualifizieren; keine Cookieübernahme aus bestehenden Browserprofilen.

## 5. Upstage: API-Organisation, Credits und Monatsbudget

**Primärbelege, heute gelesen.**
[Getting started](https://console.upstage.ai/docs/getting-started.md):
„Sign up to receive $10 in free credit“, „Create new key“ und getrennte
Prepaid-/PAYG-Wege. Der Grant ist ein Angebot, kein individuell gemessener Saldo.
[FAQ](https://console.upstage.ai/docs/resources/faq.md):
Organisation besitzt „API keys, billing, usage, and limits“; Member haben
lesenden Zugriff auf Orgcreditbalance, Rechnungen, Spend und Limits, aber
per-Key-Usage nur für eigene Keys. Budget pro Kalendermonat **UTC**;
Durchsetzung kann bis zu einer Sekunde dauern. Bei erschöpften Credits werden
weitere Kosten einer hinterlegten Zahlungsmethode berechnet. Studio-Free-
Refills/Sitzabos sind kein direktes Solar-API-Budget.

**Konkreter First-Party-Readervertrag.** Heute erneut geladenes
[Consolebundle](https://console.upstage.ai/_next/static/chunks/7157-768d821a4d850f5c.js):
same-origin Sessiontransport mit `credentials: "same-origin"`,
`x-console-request: 1`, BFF-Prefix `/api/console/v3`:

| GET unter `https://console.upstage.ai/api/console/v3` | Bedeutung |
| --- | --- |
| `/organizations/{id}/spend` | `month_to_date`, `period`; bisheriger Orgspend |
| `/organizations/{id}/promotions` | `credit_balance`, `total_credit`, `credit_coupons`; echte Credit-/Grantkandidaten |
| `/organizations/{id}/usage-limit` | Eingestellter Budgetcap, keine Guthabenmenge |
| `/organizations/{id}/rate-limit` | Tierreferenz, kein Rest |
| `/organizations/{id}/usage` | Paginierte History nach Zeitraum/Produkt/Key/Owner |

Das Bundle setzt beim Creditnormalizer `asOfIso: ""`; Abrufzeit ist deshalb
kein Providerbuchungszeitpunkt. `amount_decimal.value * 10 ** amount_decimal.exp`
ist dezimale Geldnormalisierung; fehlende Werte nicht wie der UI-Fallback zu
Null machen. Inferenzantworttokens und veröffentlichte RPM/TPM bleiben andere
Messungen. Kein unabhängiger öffentlicher Usage-/Balancevertrag mit normalem
Solar-Key oder dokumentierter Remaining-/Reset-Headersatz wurde hier qualifiziert.

**Onboarding/Workaround.** Nutzer meldet sich in der Console an, wählt die
richtige Organisation, erstellt dort einen Key und prüft tatsächlichen Creditgrant.
Ein Member besitzt bereits mehr als nur Monitorfähigkeiten; keinen erfundenen
read-only API-Keyscope verlangen. Neue Keys/Keyreveal sind kein Monitorrefresh.
Öffentlicher eigener OAuth-/Device-/CLI-Grant bleibt unverifiziert.
Dokumentierter nützlicher Fallback: [Usage/CSV gemäß FAQ](https://console.upstage.ai/docs/resources/faq.md),
UTC-Filter, höchstens 92 Tage, gleiche Rollen-/Keygrenzen. CSV-Import könnte
Usagehistory liefern, keine momentane RPM-Restreserve.

**Iststand/Abnahmegrenze.** Gateway vorhanden; Consoleconnector unavailable,
Quota nicht implementiert. Ein eigener Browserconnector muss Org-/Rollenbindung,
Sessionlebensdauer, CSRF und sicheren lesenden Betrieb zuerst nachweisen.
Budget, Credits, Couponablauf und Usage getrennt; nicht mit automatisch
angenommener Zahlung/Free-only-Zusage verbinden.

## 6. Fireworks: Quotas bereits vorhanden, Kostenhistory und CLI getrennt

**Primärbelege, heute gelesen.**
[List Quotas](https://docs.fireworks.ai/api-reference/list-quotas.md):
`GET https://api.fireworks.ai/v1/accounts/{account_id}/quotas`,
Bearer Fireworks API-Key; `value` aktueller Wert, `maxValue` „maximum approved
value“, `usage` Quotanutzung, `updateTime` „update time for the quota“.
`pageSize` maximal 200, `nextPageToken`. Das ist kein Prepaidguthabenendpoint.
[Credits](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit.md):
$1 Startercredit; ohne Zahlungsmethode wird nach Erschöpfung suspendiert.
Autoreload kann neue Credits kaufen; ein monatliches Spendlimit ist gesondert.
Keine individuelle Grant-/Ablauf-/Tarifzusage aus dem FAQ ableiten.

**Weitere offizielle Reads.**

| Endpoint / Auth | Was er liefert und was nicht |
| --- | --- |
| `GET /v1/accounts/{id}/quotas/{quota_id}`; gleicher Bearer | Einzelconstraint; `monthly-spend-usd` ist Spendcap/Usage, kein Creditrest. |
| [`GET /v1/accounts/{id}/billing/summary`](https://docs.fireworks.ai/api-reference/get-billing-summary.md); API-Key | Bewertete Kosten, Tages-/Produktpositionen. „Costs are aggregated daily. Only the date portion … is used.“ Nicht Echtzeitrest. |
| [`POST /v1/accounts/{id}/usageCosts:query`](https://docs.fireworks.ai/api-reference/query-usage-costs.md); API-Key | Bewertete Kosten nach bis zu zwei Dimensionen HOUR/DAY/MODEL/USER/API_KEY; paginiert, `subtotal`, `evaluationTime`, `attributionCompleteness`. **`ACCOUNT` braucht Accountadmin; `SELF` nur authentifizierter Principal.** Lesende Query trotz POST, in dieser Recherche nicht aufgerufen. |

Die Mindestrolle für List/Get Quotas ist im geöffneten Quotaschema nicht
ausgewiesen. Deshalb vorhandenen passenden Key verwenden und 403 als
Berechtigungsproblem führen; kein pauschales Admin-Upgrade verlangen.
`SELF` nie still in accountweite Gesamtnutzung umetikettieren. Nicht jeden
paginierten `subtotal` addieren. `updateTime` ist weder Reset noch garantierter
Messdatenstand; Zeitstempel nicht aus einem Monatsnamen errechnen.

**Offizieller CLIweg, wirklich belegt.**
[Authentication](https://docs.fireworks.ai/tools-sdks/firectl/commands/authentication.md)
nennt `firectl signin` für Google SSO und `firectl signin <account>` für Custom
SSO. [Quota list](https://docs.fireworks.ai/tools-sdks/firectl/commands/quota-list.md)
bietet `firectl quota list -o json --account-id <id> --profile <profil>`.
[API-key create](https://docs.fireworks.ai/tools-sdks/firectl/commands/api-key-create.md)
erstellt einen Key für den angemeldeten Nutzer, optional Serviceaccount
(dieser nur Admin); `--key-name`, `--expire-time`, `-o json` sind dokumentiert.
Damit kann ein qualifizierter CLIconnector Login, Accountwahl und gezielte
Nutzer-Keyanlage führen. Ein Serviceaccount ist nicht notwendig. Kein öffentlicher
lain-OAuthclient oder Devicegrant folgt aus SSO im Herstellerwerkzeug.

**Iststand.** [FireworksQuotaAdapter](../../src/Lain.Host/Quota/FireworksQuotaAdapter.cs)
ist in [Program](../../src/Lain.Host/Program.cs) registriert und liest maximal
zehn Seiten mit jeweils 200 Quotas. Account-ID und vorhandener oder separat
ausgewählter Monitorkey sind vorgesehen; Scope wird geprüft. Nur exaktes
`monthly-spend-usd` erhält USD und rechnerischen Caprest; sonst bleibt die
Einheit unbekannt. Kostenhistoryreader fehlt.
[FireworksCliLoginAdapter](../../src/Lain.Host/Accounts/CliConnections/FireworksCliLoginAdapter.cs)
erbt ausdrücklich `UnavailableCliLoginAdapter`: Signinisolation sowie Standard-
Serverless-/Nicht-Fire-Pass-Keyausgabe sind unqualifiziert. Das ist **kein
funktionsfähiger Login**, obwohl Klassen-/Katalogeinträge vorhanden sind.

**Handgriffe/Abnahmegrenze.** Jetzt vorhandenen API-Key + explizite Account-ID
verbinden; bei fehlendem Key persönliche Consoleanlage. CLI-Dauerread erst
mit konkret geprüfter Version und echtem isoliertem Authprofil. Ein späteres
`--profile` beweist keine isolierte `signin`-Speicherung. Normaler Serverlesskey,
Fire Pass, Tenant, ausgegebener Keytyp und bestehender Login bleiben getrennt.
Keine neue Keyanlage, Budgetänderung oder Zahlung innerhalb eines Readrefreshs.

## 7. DeepSeek: kleiner direkter Geldbalancereader

**Primärbelege, heute gelesen.**
[Balance](https://api-docs.deepseek.com/api/get-user-balance):
`GET https://api.deepseek.com/user/balance`; `is_available` bedeutet ausreichend
Balance für API-Calls. `balance_infos[]` enthält `currency` (`CNY`/`USD`),
`total_balance` („total available balance“), `granted_balance` (nicht abgelaufene
Grants), `topped_up_balance`, jeweils Dezimalstrings. Keine Fenster-/Reset-
oder Kontingentnennerfelder. [API-Einstieg](https://api-docs.deepseek.com/)
dokumentiert normalen API-Key und `Authorization: Bearer …`.

**Auth/Onboarding.** Der normale API-Key ist der belegte minimal vorhandene
Credentialtyp; ein eigener Readscope/Managementkey ist in diesen Quellen nicht
benannt. Persönliches Plattformkonto/Keyanlage durch den Nutzer unter
[DeepSeek Platform](https://platform.deepseek.com/). Ob ein neuer internationaler
Account ohne Aufladung einen gültigen Key erhält, bleibt ungetestet. Keine
Aufladung als Voraussetzung für einen lesenden Integrationsversuch erfinden.
Keine öffentliche Drittapp-OAuth-/Deviceclientregistrierung oder CLIlogin-
Automatisierung aus den geöffneten Quellen belegt.

**Harness wirklich vorhanden, kein Monitorersatz.** Die offizielle API-Startseite
verlinkt „DeepSeek Harness … developer preview“ auf dessen
[Quickstart](https://deepseek-harness.github.io/deepseek-harness/en/guide/quickstart).
Dort: „Open Settings → Models, enter a DeepSeek API key, and save it.“ Das
belegt einen Hersteller-verlinkten Harness und BYOK, keinen Quota-/OAuthlogin.
Für eine Geldabfrage ist direkter REST einfacher als ein dauerhafter
Harnessprozess. Keine Testsession oder Inferenz nur zur Balancebeschaffung.

**Iststand/Abnahmegrenze.** Eigenständiger Provider, Reader und Loginmethoden
fehlen in Katalog/Registrierung; nur Modelle der Familie bei anderen Hosts sind
vorhanden. Vorgeschlagenes erstes Paket: dezimalgenauer Balance-GET, alle
Währungen getrennt, echtes Nullsaldo erhalten, `is_available` getrennt von
Geldmenge. Kein Summieren verschiedener Währungen, keine Prozentbalken und
keine erwarteten Tages-/Wochenresets. Zeit ist Abrufzeit; Provider-As-of bleibt
unbekannt. 401/403/429 sind kein Nullsaldo. Danach ausschließlich autorisierter
Read mit bestehendem Key und Consolevergleich, kein Kauf.

## 8. MiniMax: neuer offizieller CLI-/Devicevertrag mit wichtigen Semantikfallen

**Produkt/Primärbelege, heute gelesen.**
[M-Plan-FAQ](https://platform.minimax.io/docs/m-plan/faq.md) nennt Go, Explore,
Build; Subscription Keys für andere Tools, gemeinsame Nutzung eines Budgets
in mehreren Tools und separat gewöhnliche API-/PAYG-Keys.
Die FAQ dokumentiert ausdrücklich
`GET https://www.minimax.io/v1/token_plan/remains`, Bearer API-Key.
[Usage rules](https://platform.minimax.io/docs/m-plan/usage-rules.md): 5 Stunden
und 7 Tage beginnen jeweils mit erster Nutzung; die Fenster resetten unabhängig,
nach Ablauf startet erst die nächste Nutzung ein neues Fenster. Video belastet
nur das Wochenfenster. Credit Packs werden nach Planerschöpfung **standardmäßig**
verbraucht und gelten laut FAQ ein Jahr; nicht mit gewöhnlichem PAYG vermischen.

**Besserer Beleg als „Schema offen“.** Die
[offizielle CLI-Doku](https://platform.minimax.io/docs/m-plan/minimax-cli.md)
verlinkt `MiniMax-AI/cli` und `mmx quota`. Dessen heute öffentlich gelesener
Commit **`38aba5f26173dd1065b115b221d79c9ddd87c6bb`** belegt:

| Offizielle gepinnte Quelle | Konkreter Vertrag |
| --- | --- |
| [Quota command](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/commands/quota/show.ts) | `mmx quota show --output json`; gibt rohe Antwort aus; keine Inferenz. |
| [Endpoints](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/client/endpoints.ts) + [HTTP](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/client/http.ts) | GET `{base}/v1/token_plan/remains` für Plan/OAuth; normale `sk-api-`-Keys gehen stattdessen an GET `{base}/account/query_balance`, Bearer. Defaultregion global `https://api.minimax.io`, CN `https://api.minimax.cn`. |
| [Typen](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/types/api.ts) | `model_remains[]`, `start_time/end_time`, `remains_time`, `current_interval_*`, `current_weekly_*`, optionale `*_remaining_percent`, `*_status`, `weekly_boost_permille`; PAYG `available_amount`, `cash_balance`, `voucher_balance`, `credit_balance`, `owed_amount` als Strings. |
| [Herstellerfixture](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/test/fixtures/quota-response.json) | Konkrete Planantwort mit Epoch-Millisekunden und beiden Fenstern; Fixture ist **kein** Liveaccountbeleg. |
| [Zählerauflösung](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/utils/quota.ts) | „Older responses use the fields as remaining counts, while newer responses may use them as consumed counts“; explizites Remainingprozent entscheidet, ohne Prozent nutzt der Hersteller legacy remaining. |

Das neue Material schließt die reine Schemakenntnislücke aus der alten
Entscheidungsmatrix. Es schließt **nicht** die Semantik-/Kontobindungs-/Liveprüfung.
`*_usage_count` nicht nach Feldnamen als Verbrauch interpretieren. Für lain ist
bei fehlendem Prozent/Version eine explizite Unsicherheit besser als ein
unbegründet exakter Restwert. Status `1=limited`, `2=exhausted`, `3=unlimited`
ist im Typ kommentiert; bloße Nullcounts dürfen weder ausgeschöpft noch
unbegrenzt bedeuten. `weekly_boost_permille` kann laut Quellkommentar zu
Remaininganzeigen über 100 % führen. Nicht kommentarlos auf 100 begrenzen;
Basis und Bonus getrennt qualifizieren. Geldwährung, Kredit-/Schuldsemantik
und genaue Credit-Pack-Abdeckung des PAYG-Vertrags bleiben gezielt offen.

**Offizieller Deviceflow wirklich vorhanden.**
[OAuthquelle](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/auth/oauth.ts)
nutzt `POST {authHost}/oauth2/device/code`, danach `/oauth2/token`, S256-PKCE,
State, feste First-Party-Client-ID und Scopes `openid profile coding_plan`.
[Config](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/config/schema.ts)
bindet global an `https://account.minimax.io`, CN an
`https://account.minimaxi.com`; Antwort liefert Access-/Refreshtoken, absolute
Expiry (`expired_in` hier Unix-ms) und optional `resource_url`.
Dies ist **belegter herstellereigener Device+PKCE-Login**. Öffentliche Registrierung
eines lain-Clients / erlaubte Wiederverwendung dieser First-Party-ID bleibt
unverifiziert; ID nicht einfach übernehmen.

**Praktischer Delegationsweg.**
[Logincommand](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/commands/auth/login.ts)
belegt `mmx auth login --recommend --region=global` als OAuthweg; er verlangt
trotz `--recommend` einen interaktiven Prozess. Der Login zeigt anschließend
best-effort Quota, was allein Login-/Quotasuccess nicht gleichsetzt.
[Pfadquelle](https://github.com/MiniMax-AI/cli/blob/38aba5f26173dd1065b115b221d79c9ddd87c6bb/src/config/paths.ts)
unterstützt **`MMX_CONFIG_DIR`**, also ein konkret belegbares eigenes Profil.
Ein später qualifizierter offizieller CLIconnector kann Login/Refresh Eigentum
der CLI lassen und nur `quota show --output json` lesen. Keine fremde Session
importieren. Ohne OAuth vorhandenen Subscription Key aus
[Plan Details](https://platform.minimax.io/console/plan) privat eingeben;
kein Key als Prozessargument/Chatinhalt empfehlen.

**Iststand/Abnahmegrenze.** MiniMax ist im geprüften lain-Stand geplant. Zuerst
zwei getrennte offline Readerpfade Plan/PAYG, dann optional CLIdelegation.
Version, `MMX_CONFIG_DIR` durch Login **und** Refresh, TTY-Abbruch, keine
Standardprofiländerung, Herkunft/Region/Accountbindung und exakte JSON-Ausgabe
prüfen. Der CLI-HTTPcode schreibt unter `--verbose` ein Tokenpräfix; verbose
für Delegation ausschließen, stdout/stderr privat filtern. Noch kein
kostenfreier API-/M-Plan-Test oder genereller Drittapp-OAuthvertrag belegt;
nur vorhandenen passenden Zugang verwenden.

## 9. xAI: API-Teammanagement und Grok Build auseinanderhalten

**Primärbelege, heute gelesen.**
[Management guide](https://docs.x.ai/developers/management-api-guide):
„management key, which is separate from your API key“, Console → Settings →
Management Keys; Basis `https://management-api.x.ai`.
[Authreferenz](https://docs.x.ai/developers/rest-api-reference/management/auth):
`GET /auth/management-keys/validation` liefert `scope`, `scopeId`, `acls`;
`teamId` deprecated. Laut Guide benötigt Validation keine ACL. Team- und
Organisationsscope nicht austauschen. Die genaue kleinste Billing-Read-ACL
bleibt in den geöffneten Quellen **unverifiziert**; Validierung allein beweist
keine Billingberechtigung.

**Offizielle Kontoreads, Bearer Management-Key.**
[Billingreferenz](https://docs.x.ai/developers/rest-api-reference/management/billing):

| Endpoint unter `https://management-api.x.ai` | Bedeutung |
| --- | --- |
| `GET /v1/billing/teams/{team_id}/prepaid/balance` | Signierter USD-Centledger `total.val`, Änderungen; kein Tokenfenster/Prozentnenner. |
| `GET /v1/billing/teams/{team_id}/postpaid/invoice/preview` | Laufende Monatsrechnung, `effectiveSpendingLimit`, `defaultCredits`, `billingCycle`. |
| `GET /v1/billing/teams/{team_id}/postpaid/spending-limits` | Hartes/weiches/effektives Postpaidlimit. Prepaid wird vorher verbraucht, dieser Cap begrenzt nicht den Prepaidverbrauch. |
| `POST /v1/billing/teams/{team_id}/usage` | Historische Aggregation für gewählten Zeitraum; lesende Query, hier nicht ausgeführt. Vollständigkeit/`limitReached` und Ende exklusiv erhalten. |

Die REST-Beispielantwort enthält `total.val: "-1000"` nach Kauf.
Der heute erneut gelesene [Herstellerproto](https://github.com/xai-org/xai-proto/blob/6499fba5e5081a25a9504c96d01dc396adf35d2b/proto/xai/management_api/v1/billing.proto)
definiert „PURCHASE … amount … negative“, „SPEND … positive“.
Signierte int64-Cents verlustfrei lesen. Nicht `abs()` anwenden oder negative
Werte verwerfen; eine spendbare positive Anzeige erst gegen Ledger/Console
qualifizieren. Keine beliebige Invoicegröße vom Cap abziehen. Kein garantierter
Provider-As-of-/Buchungsverzug aus den Quellen ableitbar.

**Onboarding/Automatisierung.** Nutzer wählt vorhandenes API-Team und erstellt
oder erhält über dessen Admin einen **separaten** Management-Key mit gezielt
benötigten Reads; exakte ACL vor Umsetzung nachweisen, nicht vorsorglich `*`.
Bestehenden Inferenzkey behalten. Der Managementvertrag kann mit zusätzlichen
Schreibrechten `POST /auth/teams/{teamId}/api-keys` ausführen; `apiKey` erscheint
nur bei Erstellung, Modell-/Endpoint-ACL und Ablauf sind konfigurierbar.
Das wäre automatisierbarer Keybootstrap nach vorhandener Managementautorisierung,
keine automatische Accountanlage und keine Fähigkeit eines reinen Billingreadkeys.
Monitorrefresh erzeugt keine Keys, Topups oder Limitänderungen.

**CLI/Harness-Abgrenzung.** Die heute gelesene
[offizielle Grok-CLIreferenz](https://docs.x.ai/build/cli/reference) dokumentiert
`grok login` und `--device-auth` für Headless/Remote. Das belegt einen echten
Grok-Build-Login. Es belegt keinen API-Team-Managementgrant oder öffentlich
registrierbaren Drittappclient. Das [frühere gepinnte Herstellerreview](../quota-auth-research/xai-stepfun-poe-synthetic.md)
beschreibt einen gesonderten Grok-Build-Billingpfad; dieser wird hier nicht als
xAI-API-Quotaquelle umgedeutet. Kein Consumer-/X-Abo zum Reparieren des
Managementreaders empfehlen.

**Iststand/Abnahmegrenze.**
[XaiKeyMetadataReader](../../src/Lain.Host/Providers/Xai/XaiKeyMetadataReader.cs)
liest bereits `GET https://api.x.ai/v1/api-key` mit dem normalen API-Key und
bindet Team-/Keymetadaten; Reconnect ist vorhanden. Dies ist **keine Billingabfrage**.
Kein xAI-Quotaadapter in der Hostregistrierung, keine xAI-Monitoroption im
[MonitorAccessCatalog](../../src/Lain.Host/Accounts/Monitoring/MonitorAccessCatalog.cs).
Separates Managementcredential + Scopevalidation + signierte-Cent-Fixtures
und Teilresultate vor späterer autorisierter Readprobe. Teamdiscovery spart
manuelle IDarbeit, ersetzt aber keine Billingrechte.

## Konkrete kurze Reason-/Action-Texte für CLI und UI

Vorschläge, noch keine Produkttextänderung. Detailansicht ergänzt Quelle,
Stand, Produkt, Scope und ob Zugriff fehlt oder der Reader noch nicht existiert.

| Fall | Reason | Action |
| --- | --- | --- |
| Groq Free/Developer, keine Beobachtung | „Restlimits erscheinen nach beobachteten API-Anfragen. Ein unabhängiger Kontoreader ist für diesen Zugang nicht qualifiziert.“ | „Groq Usage öffnen“ / „Limits öffnen“ |
| Groq alter Header | „Letzte beobachtete Limits vom …; aktueller Rest unbekannt.“ | „Limits öffnen“ |
| Groq Enterprise | „Enterprise-Metriken zeigen Projektraten, kein Restbudget.“ | „Vorhandenen Enterprise-Zugang und Projekt verbinden“ |
| NVIDIA Build | „Veröffentlicht: bis zu 40 RPM / 10.000 RPD. Kontoverbrauch, Rest und Reset sind nicht verfügbar.“ | „NVIDIA Build öffnen“ |
| Cohere | „Accountquota ist noch nicht angebunden. Triallimits und Productionfreigaben gelten je Modell.“ | „Cohere Usage und Keytyp prüfen“ |
| SambaNova | „Accountmonitor noch nicht angebunden. Free-/Developer-Limits sind modellabhängig.“ | „SambaCloud Usage öffnen“ |
| Upstage | „Orgcredits und Monatsbudget sind in lain noch nicht angebunden.“ | „Upstage Usage/Credits öffnen“ / „Usage-CSV exportieren“ |
| Fireworks, Setup fehlt | „Für Accountquotas fehlt die passende Account-ID oder Leseberechtigung.“ | „Account-ID und bestehenden Key verbinden“ |
| Fireworks, Messwerte vorhanden | „Account-Spendlimit; kein Prepaidguthaben. Messdatenstand unbekannt.“ | „Credits in Fireworks öffnen“ |
| DeepSeek | „Direkter Geldbalancereader ist dokumentiert, aber noch nicht implementiert.“ | „Vorhandenen DeepSeek-Zugang für den Reader vormerken“ |
| MiniMax | „Plan-/PAYG-Reader noch nicht angebunden; offizielles mmx kann Quota lesen.“ | „Vorhandenen Plan in mmx prüfen“ |
| xAI | „API-Key-Team erkannt; Billing benötigt einen separaten Managementzugang.“ | „Management-Lesezugang im API-Team einrichten“ |

Kein „Free kann das grundsätzlich nicht“, „neuen Proplan kaufen“, „OAuth nicht
unterstützt“ oder „keine offizielle API“ als pauschaler Sammelgrund. Die Fälle
**falsches Produkt**, **fehlende Rechte**, **Herstellervertrag unbekannt**,
**Reader nicht implementiert**, **Liveprobe fehlt** und **alte Messung** sind
unterschiedliche Zustände.

## Begrenzte Arbeitspakete und sinnvolle Reihenfolge

| Paket | Ergebnis | Abnahmegrenze |
| --- | --- | --- |
| API-Q1 DeepSeek Balance | Kleiner direkter Readadapter + eigener Provider/Monitorzugang | Währung/Dezimalstrings/Null/fehlend/Fehler; kein Reset oder Nenner. Erst vorhandener Key, lesend, kein Kauf. |
| API-Q2 MiniMax Plan/PAYG | Gepinnte Herstellerfixtures, getrennte Routen/Typen, konkrete Semantikdiagnose | `usage_count`-Ambiguität, Prozent/Status/Boost, beide Fenster, Region/Account, Guthaben vs. Credit Pack; kein behaupteter Livevertrag aus Fixture. |
| API-Q3 MiniMax CLIdelegation | Installierte qualifizierte CLI mit eigenem `MMX_CONFIG_DIR`; interaktiver Device-Login, JSON-Read | Kein Copy der First-Party-ID; Profil-/Refreshisolation, TTY/Cancel, Limits unten, keine bestehende Konfigänderung. Liefern nur nach eigener Probe. |
| API-Q4 SambaNova Header | Passive RPM-/RPD-Beobachtungen im Gateway | Kein Inferenzrefresh; eigenes Resetformat qualifizieren; TPD unbekannt; fehlende/alte Header ehrlich. Kein Standaloneabschluss. |
| API-Q5 Fireworks History | Getrennte offizielle bewertete Kostenhistory; vorhandene Quotas erhalten | `SELF`/`ACCOUNT`, Admin, UTC-Tage, Pagination/Subtotal/Partial. Kein Geldrest aus Spendcap. CLIbootstrap bleibt eigenes Gate. |
| API-Q6 xAI Management | Separates Credential, Scopevalidation, Prepaid/Postpaidreader | Exakte minimale Billing-ACL, signierte Cents, Team/Org, Teilresultate, kein Keycreate/Topup im Read. |
| API-Q7 Consoleverträge | Jeweils eigener schmaler Upstage/Cohere/SambaNova-Spike | Sessionauth/Lifecycle, CSRF, Identität/Rolle, Quellstabilität und zulässige Integration; nur eigene bewusst eingerichtete Session. Keine stille Profilübernahme. |
| API-Q8 Groq/NVIDIA Wahrheit im Status | Bestehende Pfade und Produktreferenzen präzise erklären | Groq Rate vs. Rest vs. Beobachtung; NVIDIA publiziertes Limit vs. Konto. Neue NVIDIA-Quelle separat qualifizieren statt beliebigen neuen Token verlangen. |

**Prozessgrenzen für spätere CLI-/Harnessdelegation:** vorhandenen
[OfficialCliProcess](../../src/Lain.Host/Accounts/CliConnections/OfficialCliProcess.cs)
als Basis verwenden: exakte executable/version, keine Shell, vollständige
begrenzte Umgebung, private stdout/stderr, maximale Ausgabe, Timeout,
Cancellation und Kill des eigenen Prozessbaums. Der heutige Helper erlaubt
als Isolation nur `BAILIAN_CONFIG_DIR` bzw. Fixtures; **`MMX_CONFIG_DIR` oder
Fireworks sind dadurch noch nicht implementiert**. Vorschlag: ein Vorgang je
Account, Login höchstens 10 Minuten, einzelne Reads 15–30 Sekunden,
höchstens 1 MiB je Ausgabekanal, begrenzte Pollfrequenz, Backoff/Retry-After.
Ein CLIquota-Read kann automatisch durch die CLI refreshen; deren Session-
Ownership bleibt explizit und der Providerrefresh gehört nicht in den
lain-Quotaadapter. Keine normalen Modellprompts als Auth-/Quotatest.

**Kein pauschales Releaseurteil:** Öffentliche Quellenkenntnis, implementierter
Reader, erreichbarer Connectflow, installierte Version und realer Accountbeleg
sind separate Abnahmen. Hier wurde nur Quellen-/Codegegenprüfung geliefert.
Der direkte Nutzen entsteht bereits durch präzise Reason/Action und die
kleinen belegbaren Reader, während Console- und Drittclientfragen gezielt offen
bleiben.
