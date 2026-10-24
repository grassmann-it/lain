# Alibaba Model Studio, Nebius Token Factory, Inception: Quota und Anmeldung

Stand: **24. Oktober 2026**. Forschungsbasis lain `5dc916f`; Architekturvorschläge,
keine beschlossene Implementierung. AGENTS.md, architecture-sparring-Skill,
`docs/providers/trials.md`, `docs/providers/quota-sources.md` und die drei
Providerkataloge gelesen. Eine `.architecture-sparring/config.json` ist in diesem
Worktree nicht vorhanden. Nur öffentliche Dokumentation, offizieller Quellcode,
SDK-Paketquellen und zwei abgemeldete Weboberflächen untersucht. Keine privaten
Konten, Credentials, Anmeldung, Provider-API-Abfrage, Inferenz, Registrierung,
Käufe, Builds, Dienste oder Produktcodeänderungen. Die Aussagen sind
**quellenqualifiziert, nicht mit einem Kundenkonto live qualifiziert**.

| Produkt | Konkreter nächster Anschluss | Ein Login für Inferenz + Monitoring? |
| --- | --- | --- |
| Alibaba gewöhnliche regionale API | Offizielle Bailian-CLI-Anmeldung mit Consolezugang und API-Key-Bootstrap; Trialrest + Limits + Workspaceusage lesen | Für Beijing/Singapore grundsätzlich belegt, jedoch zwei Credentials und separate Rechte; kein belegter dauerhafter OAuth-Refresh ohne AK/STS |
| Alibaba Coding Plan | Eigenständiges Console-Usage-Lesen möglich; bleibt außerhalb des bestehenden lain-Inferenzadapters | Subscription-Inferenzkey wird durch Consolelogin nicht ersetzt; nicht mit API-Trial verbinden |
| Nebius öffentliche/serverlose Token Factory | Passive RPM-/TPM-Header aus ohnehin erfolgenden Inferenzantworten | Derselbe Inferenzkey reicht; kein zusätzlicher Login. OAuth-Keybootstrap noch qualifizieren |
| Inception | Vorhandener API-Key; optional lokal beobachtete Tokenusage; Billing manuell | Gemeinsamer OAuth-/Reportinglogin öffentlich nicht belegt |

## Alibaba: gewöhnliche Model-Studio-/DashScope-API

Der bestehende Katalog verlangt regionalen gewöhnlichen API-Key, Region,
Workspace und Service Deployment Scope; QuotaSupport ist `unsupported`.
Coding Plan ist ausdrücklich ausgeschlossen. Das ist ein Implementierungsstand,
keine Aussage, dass Alibaba keine Quellen bietet.

### Authentifizierung und Verteilung

Die [offizielle Installation](https://www.alibabacloud.com/help/en/model-studio/cli/installation)
und [Konfiguration](https://www.alibabacloud.com/help/en/model-studio/cli/config)
belegen `bl auth login --console --console-site international`: Browserlogin und
Autorisierung entsperren Consolefunktionen wie `usage free` und konfigurieren
gewöhnliche Inferenzcredentials automatisch. Der [offizielle README](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/README.md)
bezeichnet diesen Weg als OAuth. Er kann bei Bedarf einen gewöhnlichen API-Key
erzeugen. Ein allein importierter Console-Access-Token erzeugt dagegen keinen
Inferenzkey. Subscriptionkeys benötigen einen eigenen API-Key-Login.

Die [Implementierung des Logins](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/auth/login-console.ts)
öffnet `/console-login?notice=127.0.0.1:<port>?state=<random>` am gewählten
Providerorigin; `needapikey=true` fordert bei fehlendem Key dessen Bootstrap an.
Sie empfängt Console-Access-Token sowie optional API-Key, API-Host, Workspace,
Site und Region über einen Loopbackcallback; der Vorgang hat 15 Minuten Timeout.
Das ist ein konkret belegter providerbetriebener CLI-Flow. Es ist **kein Nachweis
für frei registrierbare Drittanbieter-OAuth-Clients, Device Grant oder einen
von lain selbst nutzbaren PKCE-Authorization-Server-Vertrag**. Weder ein fremder
Client-Identifier noch eine private Browsersession muss kopiert werden, wenn lain
zunächst die offizielle CLI als explizit gewählten Connector benutzt.

Die CLI unterscheidet [drei Credentialdomänen](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/auth/types.ts):
Model API-Key, Console-Token + Site/Region, und signierte OpenAPI AK/SK/optional
STS. Der [Refreshcode](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/auth/refresh-token.ts)
kann den Console-Token nur mit gespeicherten AK/SK und optional STS über
`GenerateCLIAccessToken`, API-Version `2026-02-10`,
`POST /modelstudio/cli/generateAccessToken` erneuern. Ohne diese Credentials gibt
er `null` zurück. Ein dauerhaftes OAuth-Refresh-Token ist dort nicht belegt;
Browserneuanmeldung bei abgelaufener Session bleibt nötig. Die exakte
Console-Tokenlebensdauer und minimale RAM-Aktion für GenerateCLIAccessToken sind
noch offen. Temporäres STS läuft seinerseits ab und ist kein ewiger Refresh.

**Regionengrenze:** Die [CLI-Gatewaytabelle](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/console/gateway.ts)
qualifiziert nur `cn-beijing` und `ap-southeast-1`, jeweils mit domestic /
international Site. Der Singapore-International-Gateway ist
`https://bailian-singapore-cs.alibabacloud.com/cli/api.json`, Aktion
`IntlBroadScopeAspnGateway`, Produkt `sfm_bailian`, mit Bearer-Console-Token.
Beijing nutzt je Site einen anderen Host und `BroadScopeAspnGateway`.
Unbekannte Regionen werden im Sourcecode sogar auf den Beijing-Gateway
zurückgeführt; lain muss unbekannte Regionen ablehnen und darf diesen Fallback
nicht als Regionalunterstützung behandeln.

Die [Inferenzregionen](https://www.alibabacloud.com/help/en/model-studio/regions)
umfassen auch Frankfurt, Tokyo, Hongkong und Virginia; Keys, Host und Modellliste
sind regional. Workspacehosts grenzen Zugriff auf den Workspace ein. Für diese
vier weiteren Regionen ist der gemeinsame CLI-Reportingpfad noch nicht belegt.
Region und Ausführungsscope sind unterschiedliche Werte. Die [Berechtigungen](https://www.alibabacloud.com/help/en/model-studio/application-permission-management-overview)
trennen RAM-/Consolerechte, Workspacerechte und Modell-API-Rechte. Ein RAM-Nutzer
braucht für Keyerzeugung Administrator-/API-Key-Seitenrechte; für Model Monitoring
ist `ModelObservation-FullAccess` dokumentiert. Ein Cloudkontoinhaber muss
Monitoring einmal aktivieren. Keines dieser Rechte folgt allein aus erfolgreichem
Cloudlogin. Der [Keyleitfaden](https://www.alibabacloud.com/help/en/model-studio/get-api-key)
bestätigt Region, Workspace und eingeschränkte Modell-/IP-Berechtigungen.

### Konkrete Quellen und Datenvertrag

Die [CLI-Referenz](https://www.alibabacloud.com/help/en/model-studio/cli/usage-quota)
bietet `usage free`, `usage stats`, `quota list` und `quota check`. Die nachstehenden
Gateway-APIs sind durch offiziellen **ausführbaren CLI-Quellcode**, nicht durch
erratene Consoleendpoints, belegt:

| Quelle | Operation/Auth | Daten und Grenzen |
| --- | --- | --- |
| Trialrest | `zeldaEasy.bailian-commerce.freeTrial.queryFreeTierQuota`, Console; Request `queryFreeTierQuotaRequest.models` | `freeTierQuotas`: `model`, `quotaInitTotal` (ursprünglich), `quotaTotal` (restlich), `quotaValidityPeriod` (Epoch-ms), `quotaStatus`; je konkretem Modell/Snapshot |
| Schutzstatus | `zeldaEasy.bailian-commerce.freeTrial.queryFreeTierOnlyStatus`, Console | `freeTierOnlyStatuses`: Modell + Boolean; Lesen ist kein Setzen und kein Nachweis sofortiger Wirksamkeit |
| Usage | `zeldaEasy.bailian-telemetry.model.getModelUsageStatistic` bzw. `listModelUsageStatisticData`, Console | `reqDTO.startTime/endTime` Epoch-ms, `filterWorkspaceId`, `modelCallSource: Online`; erfolgreiche Calls + Usagefelder und Units; optional Modellfilter |
| Ratenobergrenzen | `GET /api/v1/models/limits`, gewöhnlicher API-Key am passenden regionalen API-Host | `output.quotas[]`: Modell, optional workspace_id, getrennte `model_limit`/`workspace_limit`; request_limit + Periode Sekunden, usage_limit + Feld + Periode, Asyncqueue/-concurrency; kein beobachteter Rest |

Belege: [Trialparser](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/shared.ts),
[Trialcommand](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/free.ts),
[Usagecommand](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/stats.ts),
[Limitcommand](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/quota/list.ts).
Usage ist ein angefragtes gleitendes Intervall, standardmäßig letzte sieben Tage,
kein tägliches oder monatliches Quotareset. `Online` ist nicht sämtliche
Batchusage. Modelle anderer Modalitäten haben Bilder/Sekunden/Zeichen statt
Texttokens; Units niemals pauschal umetikettieren.

Trialtokens werden [laut Regeln](https://www.alibabacloud.com/help/en/model-studio/new-free-quota)
zwischen Account und RAM-Nutzern geteilt, je Modell getrennt, Input + Output
gemeinsam. Der qualifizierte Trial gilt für Singapore + International Scope.
Der Angebotsbetrag ist kein beobachteter Rest. Ablauf ist regulär einmalig nach
90 Tagen ab dem spätesten relevanten Ereignis; tatsächlichen Epochwert lesen,
keinen regelmäßigen Reset konstruieren. Die Konsole aktualisiert minutenweise;
exakte Reportinglatenz ist nicht garantiert. `ObservedAt` ist Abrufzeit,
`DataAsOf` bleibt ohne Providerzeit unbekannt. Fehler/leere Responses sind kein
Nullrest. Bereits abgelaufene Grants sind keine nutzbare Kapazität.

Vorsicht beim CLI-JSON: `usage free` formatiert `expires` nur als lokales Datum,
und `usage stats` formatiert Intervallgrenzen ebenfalls als Datum. Für exakte
Ablauf-/Fensterwerte die dokumentierte [generische Console-Leseoperation](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/console/call.ts)
mit einer **festen Read-Allowlist** verwenden oder den offiziellen Client
adaptieren. Dabei Rohschema/Status erhalten. CLI-Defaultfilter blenden ungültige
Grants aus; das darf nicht als explizite Null pro bekanntem Modell erscheinen.

Die Model-Usage-Webdoku beschreibt noch ausschließlich freie Quoten und
Billenexport; die aktuellere CLI zeigt dagegen Workspace-Telemetry. Diese
Dokudifferenz und echte regionale Verfügbarkeit müssen im ersten genehmigten
Kontoread geklärt werden. Die Sourceprüfung ist keine Livebestätigung.

### Geldsaldo und Abrechnungsusage als eigener Anschluss

[BSS QueryAccountBalance](https://www.alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance)
liefert `AvailableAmount`, `AvailableCashAmount`, `CreditAmount`, `Currency`.
Das ist der Alibaba-Cloud-Kontofinanzsaldo, **keine** Model-Studio-Trialrestquota.
[BSS QueryBill](https://www.alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-querybill)
liefert Settlement-Bills für `BillingCycle: YYYY-MM`, mit ProductCode/-Type,
Owner-/Account-ID, Geldfeldern und Currency; paginiert, bis 300 Zeilen pro Seite.
Kein Modelltokenzähler oder aktueller Remaining-Pool wird daraus hergeleitet.
Model-Studio-ProductCode/-Type und internationale BSS-Endpunktzuordnung vor
Implementierung konkret aus offiziellem Produktmapping bzw. Kontobill belegen.

[RAM-Autorisierung](https://www.alibabacloud.com/help/en/user-center/developer-reference/api-calling-authorization):
`AliyunBSSReadOnlyAccess` oder enger `bssapi:QueryAccountBalance` /
`bssapi:QueryBill`, Resource `*`; kompatible Aktionen sind `bss:DescribeAcccount`
(Schreibweise des Anbieters) und `bss:DescribeBillList`. Produktbedingungen können
Billzugriff eingrenzen. AK/SK-/STS-Signatur und Finanzrechte separat vom
Inferenzkey verwalten. Die Existenz des Consolelogins verleiht diese nicht.

**Empfehlung:** Zuerst expliziter offizieller CLI-Connector mit benanntem Profil,
Beijing/Singapore-Grenze und gewöhnlichem Keybootstrap. Nur Trial-/Schutzstatus,
exakte Roh-Usage und Konfigurationslimits lesen; keine `usage freetier`,
`quota update/delete` oder Subscriptionmutation. User kann ohne manuelles Keykopieren
verbinden. Für eine eigene lain-OAuth-App vorher beim Anbieter Clientregistrierung,
Redirectzulassung, Scopes und Refreshvertrag klären. Finanzconnector optional
separat, weil er weitergehende Berechtigungen hat. Noch fehlen Connector,
Credential-/Profilauswahl, regionaler Scopeabgleich, Parser/Freshness und Liveprobe.

### Eigenständiger OAuth-Inferenzpool und Coding Plan

Die [Trial-FAQ](https://www.alibabacloud.com/help/en/model-studio/new-free-quota)
beschreibt zusätzlich einen unabhängigen OAuth-Inferenzpool von 2.000 Calls/Tag.
Die normale Free-Quota-Seite zeigt dessen Nutzung ausdrücklich nicht. Das ist
weder der CLI-Console-Keybootstrap noch die normale API-Key-Tokenquota; ein
Calling-/Reportingvertrag für lain sowie Zeitzone/Reset und erlaubte Clients
sind damit nicht belegt. Getrennte zukünftige Produktqualifikation erforderlich.

[Coding Plan](https://www.alibabacloud.com/help/en/model-studio/coding-plan)
nutzt Subscriptionkey `sk-sp-…` und `coding-intl.dashscope.aliyuncs.com`; nicht mit
gewöhnlichen regionalen Keys mischen. Das existierende lain-Verbot für dessen
Backendnutzung bleibt. Der offizielle [Usagecommand](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/coding-plan.ts)
bietet separat per Consoleauth
`zeldaEasy.broadscope-bailian.codingPlan.queryCodingPlanInstanceInfoV2`,
`queryCodingPlanInstanceInfoRequest: {commodityCode: sfm_codingplan_public_intl,
onlyLatestOne: true}`. Die Antwort enthält `codingPlanInstanceInfos`, Status und
`codingPlanQuotaInfo` mit `per5Hour/perWeek/perBillMonth` UsedQuota, TotalQuota,
QuotaNextRefreshTime. CLI wählt nur die erste VALID-Instanz; mehrere Instanzen
nicht ohne ID abgrenzen. Daten sind **Modellcalls**, keine Tokens.

Fünfstundenbudget rollt minutengenau über alte Calls zurück; `resetTime` bedeutet
nicht, dass auf einmal der ganze Pool aufgefüllt wird. Woche beginnt Montag
00:00 UTC+08, Abrechnungsmonat am individuellen Verlängerungstag 00:00 UTC+08.
Limits gleichzeitig anwenden. Eine eigenständige, nur lesende Subscriptionkachel
könnte dieselbe Consoleanmeldung nutzen; sie erzeugt keinen Subscriptionkey und
macht Coding-Plan-Inferenz nicht für den lain-Backendpfad zulässig. Public API
`GetSubscriptionStats` betrifft dagegen **Token Plan Seats/Credits** und ist kein
Beleg für Coding Plan oder gewöhnliche API-Trialtokens.

## Nebius Token Factory: Serverless vom Cloud-/Dedicatedprodukt trennen

lain hat einen Token-Factory-API-Key und `https://api.tokenfactory.nebius.com/v1`,
QuotaSupport `unsupported`. [Authentifizierung](https://docs.tokenfactory.nebius.com/api-reference/introduction.md)
nutzt einen in Project → API Keys erzeugten statischen Bearerkey. Der
[Quickstart](https://docs.tokenfactory.nebius.com/quickstart.md) nennt Google-/GitHub-
Kontologin; das ersetzt den API-Key für Inferenz nicht. Ein offizieller
[Nebius-CLI-Integrationsleitfaden](https://github.com/nebius/nebius-physical-ai/blob/main/docs/workbench/token-factory.md)
trennt den Token-Factory-Key explizit vom Nebius-Cloud-IAM-Token.

**Direkt implementierbar:** [offizielle Rate-Limit-Header](https://docs.tokenfactory.nebius.com/ai-models-inference/rate-limits.md)
aus ohnehin autorisierten Antworten passiv erfassen:

| Headergruppe | Bedeutung |
| --- | --- |
| `x-ratelimit-limit-requests`, `remaining-requests` | Requests pro Minute, beobachtete Restkapazität |
| `x-ratelimit-limit-tokens`, `remaining-tokens` | Tokens pro Minute; Tokenkapazität wird kontinuierlich aufgefüllt |
| `x-ratelimit-reset-requests`, `reset-tokens` | Relative Sekunden bis zur angegebenen Rückstellung; keine Unixzeit |
| `x-ratelimit-dynamic-scale-requests/tokens` | Aktuelle dynamische Skalierungsfaktoren |
| `x-ratelimit-dynamic-period-remaining` | Sekunden bis zur nächsten dynamischen Anpassung |
| `x-ratelimit-dynamic-period-usage-requests/tokens` | Mittlere Auslastung der Anpassungsperiode in Prozent |
| `x-ratelimit-over-limit: yes`, `Retry-After` | Über nomineller Grenze dennoch verarbeitet bzw. Wartezeit in Sekunden; kein zusätzlicher Budgetpool |

Dynamische Anpassung ist eine eigene rollende 15-Minuten-Betrachtung; diese ist
nicht das RPM-/TPM-Fenster. Kein lokales Hochrechnen oder Auffüllen. Lokalen
Account, Keyreferenz und angefragtes Modell speichern; die Doku belegt keine
universelle organisationsübergreifende Pool-ID oder Sharinggrenze je Modell.
Originale Beobachtungszeit erhalten; Freshness höchstens bis zum früheren Reset
und einer kurzen lokalen TTL (Vorschlag 60 Sekunden, kein Provider-SLA).
`Retry-After` nicht als Reset aller Pools verwenden. Fehlender Header ist
unbekannt; explizite Null bleibt Null. Refresh liest nur gespeicherte
Beobachtungen, schickt keine kostenpflichtige Inferenz. So reicht der vorhandene
Inferenzkey für passives Monitoring, ohne weiteren Login.

[Organisationsmodell](https://docs.tokenfactory.nebius.com/team-access/org-projects.md):
Organisation → Projects → Resources, Projektkey und multiregionale Projekte.
[RBAC](https://docs.tokenfactory.nebius.com/team-access/groups.md) trennt Org Admin /
Billing Manager von Project Admin / Member. Billing Manager sieht Orgusage, hat
aber keinen Projekt-/Observabilityzugriff; Project Member darf eigene Keys
erstellen, hat dadurch keinen Orgfinanzzugriff. SSO setzt [SAML-Föderation](https://docs.tokenfactory.nebius.com/team-access/sso.md)
voraus, dokumentiert sogar eine CLI-Profileinstellung mit
`--federation-endpoint auth.tokenfactory.nebius.com`; frisch föderierte Benutzer
haben erst nach Gruppenzuweisung Produktrechte.

### OAuth-/Keybootstrap-Kandidat, noch kein fertiger Connectvertrag

Der [offizielle Python-SDK-Flow](https://github.com/nebius/pysdk/blob/main/src/nebius/aio/token/federation_bearer/auth.py)
implementiert Authorization Code + S256-PKCE, Loopbackcallback, `scope: openid`,
`/oauth2/authorize` und `/oauth2/token`. Er nimmt einen Client-ID-Parameter;
freie Drittanbieterregistrierung und Redirectfreigabe sind daraus nicht belegt.
[Renewal](https://github.com/nebius/pysdk/blob/main/src/nebius/aio/token/federation_account.py)
kann erneut Browserautorisierung benötigen; keine garantierte stille
Offline-Refresh-Session versprechen.

Wichtiger konkreter [offizieller API-Schemakandidat](https://github.com/nebius/api/blob/e855cccf88420a80741b04075674528d75f3e238/nebius/iam/v1/static_key_service.proto):
`nebius.iam.v1.StaticKeyService/Issue` am Dienst `cpl.iam` gibt einmalig `token`
und eine Operation zurück. [StaticKeySpec](https://github.com/nebius/api/blob/e855cccf88420a80741b04075674528d75f3e238/nebius/iam/v1/static_key.proto)
unterstützt aktuell nur Serviceaccounts, einen Serviceenum **AI_STUDIO = 3**
(OBSERVABILITY = 1 ist getrennt) und `expiresAt`. Das ist eine belastbare Spur für
OAuth → berechtigten Serviceaccount → AI_STUDIO-Key, **kein Nachweis**, dass ein
gewöhnlicher Token-Factory-Benutzer ihn ohne Adminsetup ausstellen darf oder dass
der Key unmittelbar alle heutigen Serverless-/Billingendpoints abdeckt.

Begrenzter nächster Nachweis: Anbieterbeleg für Token-Factory-`aitenant`/
Projektzuordnung, erforderliche Issue-/Serviceaccount-Gruppen und Annahme eines
AI_STUDIO-Keys durch Serverlessmodellliste und Headertransport; kein Kauf oder
Inferenz zum Authnachweis nötig. Erst danach einen expliziten OAuth-Keybootstrap
planen. Ein Cloud-IAM-Token niemals einfach an `/v1` weiterreichen.

### Balance und Dedicated Observability

[Billing & Consumption](https://docs.tokenfactory.nebius.com/other-capabilities/billing-new.md)
beschreibt Org → Usage (Filter Region/Projekt/Service/Produkt) und Geldsaldo /
Transaktionen. Neukundenangebot, Kartendebit und Grants sind keine beobachteten
Restwerte. Kein öffentlich dokumentierter Token-Factory-Balance-/Serverless-
Abrechnungsendpoint samt Readscope, Schema und Aktualität wurde qualifiziert.
Ältere Billingdocs beschreiben einen anderen Ablauf; aktuelle Billing-new-Regeln
nicht als Nachweis eines konkreten Kontotarifs behandeln.

Der [Nebius-Cloud-Billingexport](https://github.com/nebius/api/blob/e855cccf88420a80741b04075674528d75f3e238/nebius/billing/v1alpha1/one_time_export_service.proto)
bietet Create/Get/List von OneTimeExports unter einem **Contract**. Er exportiert
monatliche FOCUS-CSV-Abrechnungsdaten und keine aktuelle Balance. Die Verbindung
zum Token-Factory-Orgvertrag und enthaltenen Serverless-SKUs fehlt; Cloudexport
nicht ersatzweise als qualifizierte Token-Factory-Quelle einbauen.

[Dedicated Observability](https://docs.tokenfactory.nebius.com/ai-models-inference/observability.md)
steht ausdrücklich **nur Dedicated Endpoints** zur Verfügung, ist projektbezogen,
near-real-time typischerweise nach einigen zehn Sekunden und nicht zur
Billingabstimmung gedacht. Das [Integrationsdokument](https://docs.tokenfactory.nebius.com/ai-models-inference/observability-api-integrations.md)
belegt `https://read.monitoring.api.nebius.cloud/projects/<project_ID>/service-provider/prometheus/federate`
mit `match[]` und statischem **Token-Factory-Key**; empfohlenes Scrapeintervall
mindestens 15 Sekunden. Trotz Cloudhost ist hier der Token-Factory-Bezug explizit.
Metriknamen/Labels müssen vor Adapterbau konkret qualifiziert werden. Keine
Serverless-Balance aus Dedicated Tokens/min erzeugen; kein Observability-Key
als automatisch identischer AI_STUDIO-Key behandeln.

Öffentliche, abgemeldete Weboberfläche und ihr Einstiegsscript
`https://static.nebius.com/app/ai-studio-ui/main.cb5233174a4943f4.js` gelesen:
Org-Usage-/Billing-/Transactions- und Projekt-Key-/Rate-Limit-Routen erkennbar,
aber kein belastbarer Balance-Request-/Responsevertrag. Keine Childchunks,
Cookies oder eingeloggte Session gelesen. Ein späterer explizit autorisierter,
begrenzter Read-only-Trace der konkreten Org-Usage-/Balanceansicht könnte Schema,
Owner-ID, Currency, Responsezeit und Authrefresh klären; bis dahin manueller Link.

**Fehlende Umsetzung:** Header-Allowlist und Store/Adapter analog der vorhandenen
Groq-Beobachtung, Tests von Sekundenreset/dynamischen Fenstern/fehlend vs. Null,
Scope und Staleness; davon unabhängig Serviceaccount-/OAuth-Keybootstrap-Nachweis
und echtes Balance-/Billingreporting. Kein weiterer Token fürs Headerlesen nötig.

## Inception Mercury API

lain verwendet API-Key am Inception-API-Host und markiert QuotaSupport
`unsupported`. Die [offizielle Authseite](https://docs.inceptionlabs.ai/get-started/authentication)
verlangt für alle API-Requests einen API-Key; [offizielle SDKs](https://docs.inceptionlabs.ai/get-started/sdks)
lesen `INCEPTION_API_KEY`. Keine dokumentierten OAuth-/Device-Grants,
Read-only-/Organisationstoken oder Keybootstrap-Operationen qualifiziert.
Dashboard-/Sociallogin wäre ohne expliziten Vertrag kein delegierter lain-Login.

Der [öffentliche Dokuindex](https://docs.inceptionlabs.ai/llms.txt), Auth, SDK und
FAQ wurden tatsächlich geöffnet. Die [PyPI-Releasequelle](https://pypi.org/project/inceptionai/0.3.0/)
(über die API heruntergeladene sdist, nur im Speicher gelesen, nicht installiert)
enthält API-Clientklassen für Chat/FIM/Edit/Decisions/Models und Bearer-API-Key-
Auth; keine Billing-/Balance-/Quota-/OAuthressource. Das Paket nennt ein derzeit
öffentlich nicht abrufbares GitHubrepo; aus dessen 404 folgt keine Aussage über
zukünftige Produktfähigkeiten. Releasehash:
`143b38d230fcfdb2ab42d834ba66ebac9d4bc50f79f0c440575975ebc3bf9047`.

Die [FAQ](https://docs.inceptionlabs.ai/resources/faq)
belegt Tokenusage je Antwort: `prompt_tokens` enthält Cachetokens,
`completion_tokens` enthält Reasoningtokens, `total_tokens` ist deren Summe;
Unterfelder nicht doppelt zählen. Streams können mit
`stream_options.include_usage: true` eine Schlussusage liefern. Das ermöglicht
**lokal beobachteten Verbrauch**, deckt aber weder fremde Clients noch Grants,
Kartenzahlungen, Kontobalance oder globalen Rest ab. Eine Preisrechnung bleibt
Schätzung und keine offizielle Billingbeobachtung.

Das einmalige 100M-Neukundenguthaben ist laut FAQ kontoweit über Modelle geteilt,
kein monatlicher Reset und kein Beweis, dass dieses Konto noch 100M Tokens hat.
Ablauf und tatsächlicher Rest sind unbekannt. Öffentliche Rate-Limit-Seite ließ
sich nur als Seitenskelett öffnen; keine Header-/Reset-Semantik dadurch
qualifiziert. Tarifwerte aus Suchtreffern würden keinen Accountlimitbeleg liefern.

Die abgemeldete Plattformloginseite lieferte HTTP 429; nicht wiederholt oder mit
anderen Konten umgangen. Somit keine private Websessionquelle qualifiziert.
**Empfehlung:** API-Key-Inferenz beibehalten, optional separat beschriftete lokale
Tokenbeobachtung aus ohnehin vorhandenen Antworten, offizieller
[Platformzugang zur Billingansicht](https://platform.inceptionlabs.ai). Die spätere
begrenzte Nachweisfrage an Inception lautet: unterstützter read-only Endpoint
für Kontotokenrest/Finanzsaldo und Abrechnungsusage, Auth-/Orgscopes, Aktualität,
Ablaufwerte sowie Desktop-OAuth-/Keybootstrapregistrierung. Falls es nur eine
Websessionquelle gibt, vor Implementierung eine ausdrücklich autorisierte
Read-only-Billingansicht mit minimalem Request-/Responseschema und Session-
Erneuerung qualifizieren; keine Cookieimporte als fertiges Produkt verkaufen.
