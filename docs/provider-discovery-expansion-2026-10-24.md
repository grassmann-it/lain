# Provider-Ausbau: Was lain zusätzlich anbieten könnte

**Prüfdatum: 24.10.2026.** Ergänzung zur [ausführlichen Acht-Anbieter-Recherche](provider-discovery-2026-10-24.md).
Auftrag: deutlich mehr geeignete Anbieter finden. Dies ist ein **breites Screening**,
keine abgeschlossene Sechs-Punkte-Qualifikation aller Einträge. Öffentliche offizielle
Quellen wurden gelesen; keine Accounts, Logins, Zugangsdaten, Inferenzrequests oder
kostenpflichtigen Tests verwendet. Alle neuen Anschlüsse bleiben live `not_tested`.
Die laufende CLI-/Tray-/UI-Abnahme, Dienste und Implementierung wurden nicht geändert.

## Empfehlung

**Nachtrag zur Priorität:** Die [Budget-/Nutzenbewertung aller Kandidaten](provider-priorities-budget-2026-10-24.md)
berücksichtigt den anschließend genannten Wunsch nach günstigen eigenen Live-Tests.
Ihre Reihenfolge hat Vorrang vor der folgenden rein technischen Empfehlung.

Die Budgetrecherche ergänzt außerdem offizielle Billing-/History-Referenzen für
Tinfoil und Parasail sowie explizite Anthropic-API-Credits für berechtigte
Max-/Team-Pläne. Die zuvor offenen Angaben unten sind insoweit fortgeschrieben;
ein positiver Rest-/Reset-Monitor oder eine Livequalifikation folgt daraus nicht.

Die größte Lücke sind **direkte OpenAI- und Anthropic-API-Konten**, weitere
Inferenzhosts, europäische Clouds und **lokale Modellserver**. Vorhandene
Codex-/Command-Code-Konten ersetzen eigenständige API-Verträge und Abrechnung nicht.
Besonders interessant für den Monitor sind **NanoGPT, Featherless, Venice, Novita
und SiliconFlow**; DeepInfra bietet ebenfalls dokumentierte Billing-Abfragen.

Vorgeschlagene Reihenfolge, zusätzlich zu DeepSeek/MiniMax/Fireworks aus der ersten Recherche:

1. **NanoGPT-Monitor:** dedizierter Token mit ausschließlich `usage:read`, echte
   Kontingente und Resetzeitpunkte. Für das erste Monitor-Ziel sehr passend.
2. **Featherless und Venice:** Guthaben, Gateway und externe Nutzungshistorie;
   Tarif-/Rechtefragen vor produktiver Freigabe klären.
3. **Novita und SiliconFlow:** zunächst kleine, reine Balance-Anschlüsse;
   Geldfelder und regionalen Accounttyp sauber qualifizieren.
4. **Direkte OpenAI-/Anthropic-API-Konten**, **DeepInfra**, **Hugging Face** und
   **Kilo Gateway** für mehr Gateway-Auswahl. OpenAI/Anthropic-History ist ein
   eigener Admin-/Organisationsanschluss, kein universeller Abo-Monitor.
5. **Ollama und LM Studio**, danach llama.cpp/vLLM/SGLang als lokale Backends.
6. Europäische Clouds, weitere Hosts und große Cloudplattformen nach Nachfrage.

Das sind Prioritätenvorschläge, keine akzeptierte Architektur oder Implementierungszusage.

## Bewertungsgrenzen für alle Einträge

- **Gateway**: dokumentierter technischer Anschluss. Streaming, Tools, Modellliste,
  Tokenzählung und Abbruch müssen trotzdem pro Modell/Protokoll qualifiziert werden.
- **Monitor**: eigenständige Accountabfrage ohne Inferenz. Guthaben ist weder
  Tokenkontingent noch Verbrauchsprozent. Ratenlimits sind keine Abo-Restmengen.
- **Historie**: Anbieteraggregate, Billing-Buchungen und Einzelrequests unterscheiden.
  Ohne belegte externe API kann lain nur seine eigene durchgeleitete Nutzung erfassen.
  Diese deckt andere Clients nicht ab. Quellen nicht ungeprüft addieren.
- **Rechte**: offizielle Integrationsbeispiele belegen technische Unterstützung.
  Für keinen neuen Eintrag ist damit pauschal persönliches Proxying, beliebige
  Workerlast, Schlüsselweitergabe, Inferenzweiterverkauf oder kommerzieller Vertrieb
  von lain vertraglich freigegeben. Diese Szenarien getrennt prüfen. Explizite
  Einschränkungen und Widersprüche stehen unten; der übrige Vertragscheck ist offen.
- **Gratiszugang**: Ohne ausdrücklichen Beleg bleiben kostenlose Modelle, dauerhafter
  Free Tier, Trial/Referral, Ablauf, Kartenpflicht und Länderzugang **je Anbieter offen**.
  Für keinen offenen Eintrag Kostenfreiheit versprechen. Ein Werbeangebot ist kein
  dauerhaftes Kontingent. Ein Budgetfeld ist noch kein belegter harter Kostenstopp.
- **Aktualität**: eigener Abrufzeitpunkt, Anbieter-Datenstand, Abrechnungsverzug und
  Reset sind getrennte Werte. Fehlende Zähler/Nullwerte bedeuten nicht automatisch null
  Verbrauch. In diesem Screening wurde keine interne Dashboard-API qualifiziert;
  reine Inferenzheader werden nicht als selbstständiger Monitor eingestuft.

## A. Besonders passende neue Monitor-/Gateway-Kandidaten

### NanoGPT — Priorität für echte Abo-Kontingente

- **Anschluss:** offizieller OpenAI-kompatibler API-Zugang mit Bearer- oder
  `x-api-key`-Authentifizierung und dokumentierten Coding-Tool-Integrationen.
  Abo-Textmodelle separat über `GET /api/subscription/v1/models?detailed=true`,
  Abo-Chat über `/api/subscription/v1/chat/completions`. Explizite Providerwahl
  wird laut Support PAYG abgerechnet. Streaming/Tooldetails pro Route noch prüfen.
- **Monitor, offiziell:** `GET /api/management/v1/subscription/usage` mit einem
  **Management-Token**, ausschließlich Scope `usage:read`. Dieser kann keine Modelle
  ausführen, kein Guthaben ausgeben und keine Inferenzkeys erstellen. Liefert Status,
  Limits, `used`, `remaining`, `percentUsed` als Bruch und `resetAt` in Unix-Millisekunden.
  Fenster können null sein; gestörte Abfragen liefern unbekannte Zähler und
  `degraded: true`. Zurückgegebene Resets verwenden, keine Fenster fest einbauen.
  Token standardmäßig 90, maximal 365 Tage gültig. Keine Guthaben-/Historydaten
  in diesem Endpoint; unabhängige Datenfrische noch qualifizieren.
- **Historie:** Usage-Seite und optionale Request-/Response-Logs dokumentiert;
  öffentliche History-Abruf-API in diesem Screening nicht belegt. Management-Quota
  ausdrücklich kein Requestarchiv.
- **Nutzungsgrenzen:** persönliches Abo nur für eine Person; kein Teilen, Pooling,
  Weiterverkauf oder Bedienen mehrerer Nutzer. Für Team-/kommerzielle gemeinsame
  Workloads verweist der Anbieter auf PAYG. Das ist vom Verkauf eines lokalen
  BYOK-Clients zu unterscheiden; dessen Vertragsprüfung bleibt offen.
- **Kostenkontrolle:** Abo-Überlauf kann PAYG nutzen. Support nennt dafür mehrere
  Voraussetzungen: „Use balance after limits“, Freigabe bezahlter API-Modelle,
  geeigneter Key-Abrechnungsmodus, Spending-Limit und Guthaben. Ein Key im Modus
  **Subscription only** oder mit $0-Spend-Limit soll bei erschöpftem Kontingent
  weiter 429 liefern. Das ist ein guter Prüfpfad, noch kein live verifizierter Schutz.
  Trialvarianten erscheinen im Managementschema; Berechtigung, Ablauf, Karte und
  Länder wurden nicht qualifiziert.
- **Eignung/Aufwand:** beides; Monitor S–M, Gateway M. Erster späterer Test:
  reiner Quota-Abruf mit `usage:read`, Null-/Degraded-Zustände und 403 beim
  lesenden Keylistenabruf prüfen. Keine Inferenz zum Nachweis nötig.

Quellen: [API](https://nano-gpt.com/api),
[Management API](https://docs.nano-gpt.com/api-reference/management-api),
[Support: Abos, Überlauf, Teams und Logs](https://nano-gpt.com/support).

### Featherless — Balance, Planlimits und echte Einzelrequest-Historie

- **Anschluss:** OpenAI-kompatibel, Bearer-Key; `/v1/models`, `/v1/plan` sowie
  Chat/Completions dokumentiert. Der Plan liefert Kontextgrenze, Modellgröße und
  Concurrency. Modellkontext und gebuchter Plankontext können sich unterscheiden.
- **Monitor, offiziell:** `GET /credits/balance`; **Org-Admin-Key mit
  `manage_billing`** erforderlich. `balance`, `reserved`, `available` getrennt;
  exakte Ganzzahlstrings in **Nano-USD = 1/1.000.000.000 USD**. Verfügbar ist
  Guthaben minus Reservierungen. Abrechnung in Fünf-Minuten-Fenstern, daher
  verzögert; laut Dokumentation darf einmal pro Minute abgefragt werden.
- **Historie, offiziell:** `GET /usage/activity/summary` und
  `/usage/activity/requests`, ebenfalls `manage_billing`. Organisationsweite
  Token-/Kostenaggregate und paginierte Einzelrequests mit Request-ID, Key-ID,
  Cachetokens und `cost_status`. Cachetokens sind Teil der Inputtokens.
  Bei Audio können Tokenfelder Zeichen zählen; unbekannte und pauschal abgegoltene
  Kosten sind nicht automatisch null USD.
- **Tarif-/Vertragskonflikt:** aktuelle Billingdoku beschreibt Developer als
  monatliche Creditfinanzierung ab $50 mit Tokenabrechnung, nicht pauschal
  unbegrenzte API-Nutzung. Öffentlich abrufbare Terms von 2024 begrenzen
  Individual-Pläne auf interaktive Nutzung/Prototyping und erlauben beliebige
  Anwendungen einschließlich Resale für Scale-Pläne. Die Zuordnung der heutigen
  Developer-/Business-Namen zu diesen alten Vertragskategorien ist **offen**.
  Unbeaufsichtigte parallele Agentlast deshalb noch nicht als erlaubt markieren.
- **Kosten:** gekaufte Credits verfallen laut Developer-Doku nicht. Bei null oder
  darunter werden neue Request-Pricing-Calls blockiert. Das ist keine Zusage, dass
  laufende Requests keinen Restüberhang erzeugen. Monatliche Folgezahlungen und
  Top-ups getrennt vom Inferenzstopp behandeln. Gratiszugang offen.
- **Eignung/Aufwand:** beides, M; Vertragsklärung zusätzlich. Erster Test:
  Balance und Plan mit getrennten Rollen lesen, 403 für unzureichende Billingrechte,
  danach vorhandene History über zwei Cursorseiten ohne Doppelzählung importieren.

Quellen: [Kompatibilität](https://featherless.ai/docs/api-overview-and-common-options),
[Plan](https://featherless.ai/docs/api-reference-plan),
[Balance](https://featherless.ai/docs/api-reference-credits),
[History](https://featherless.ai/docs/api-reference-usage-activity),
[Developer-Credits](https://featherless.ai/docs/request-pricing-and-credits),
[Terms](https://featherless.ai/legal/terms-of-service).

### Venice — Guthaben, belegtes Tageskontingent und Billing-Historie

- **Anschluss:** OpenAI-kompatible API; Inference- und Admin-Keys unterscheiden.
- **Monitor, offiziell:** `/api/v1/api_keys/rate_limits` ist laut DIEM-Doku auch
  mit Inference-Key lesbar und liefert Guthaben, Rates und `nextEpochBegins`.
  `/api/v1/billing/balance` benötigt dagegen **Admin-Key**; Inference-Key erhält
  laut Guide 401. Separate USD-, DIEM-, gebündelte und verdiente Credits;
  `consumptionCurrency`, `canConsume`, `diemEpochAllocation`.
- **Reset:** ausschließlich das belegte DIEM-Tageskontingent erneuert sich
  um **00:00 UTC**, unverbrauchtes DIEM-Kontingent verfällt. DIEM-Rest und
  Epochengesamtmenge erlauben hier einen belegten Bezugsrahmen für Prozent.
  Daraus keinen Reset für USD oder alle anderen Guthabentypen ableiten.
- **Historie, offiziell:** `/api/v1/billing/usage-history` mit Keyset-Cursor;
  Token-/Requestdetails können vorhanden oder null sein. Ein Request kann mehrere
  SKU-Buchungen erzeugen; Buchungen nicht als einzelne Requests zählen oder
  wiederholte Tokenfelder summieren. Alte Route `/billing/usage` ist laut Index
  entfernt (410). Rechte, Aufbewahrung und Verzögerung der History noch qualifizieren.
- **Überlauf:** Verbrauchsreihenfolge DIEM → earned → bundled → USD. **Tageslimit
  erreicht bedeutet also nicht automatisch Kostenstopp.** Key-Epochenlimits sind
  dokumentiert; die Verhinderung eines USD-Fallbacks im gewünschten Kontomodell
  muss separat geprüft werden. Staking ist kein kostenloser Free Tier.
  Preise/Karte/Länder und kompletter Vertragscheck offen.
- **Eignung/Aufwand:** beides, M. Erster Test: read-only beide Keytypen und
  Kontingentfelder vergleichen, fehlendes DIEM richtig darstellen; anschließend
  vorhandene SKU-Historie anhand Request-ID gruppieren. Kein Staking erforderlich.

Quellen: [Überblick](https://docs.venice.ai/overview/about-venice),
[Rate-Limits](https://docs.venice.ai/api-reference/endpoint/api_keys/rate_limits),
[Balance](https://docs.venice.ai/api-reference/endpoint/billing/balance),
[DIEM, Reset, Keyrechte und Ausgabereihenfolge](https://docs.venice.ai/overview/vvv-diem),
[History](https://docs.venice.ai/api-reference/endpoint/billing/usage-history),
[API-Index](https://docs.venice.ai/llms.txt).

### Novita — kleiner offizieller Balance-Anschluss

OpenAI-kompatibler LLM-Zugang und offizielles
`GET https://api.novita.ai/openapi/v1/billing/balance/detail` mit Bearer-Key.
Felder `availableBalance`, `cashBalance`, `creditLimit`, `pendingCharges`,
`outstandingInvoices` als Ganzzahlstrings in **1/10.000 USD**. Ein Kreditlimit
belegt gerade keinen ausschließlich vorausbezahlten Betrieb. Das derzeit mit
null vorbelegte `pendingCharges` beweist keine vollständige Echtzeitabrechnung.
Kein Reset oder Prozent aus dem Geldguthaben ableiten. Externe Requesthistory,
Keyrollen, Datenfrische, Gratisdetails, harter Kostenstopp und Vertragsfragen offen.
**Beides, S–M für Balance/M für Gateway.** Erster Test: reiner Balanceabruf,
Einheiten und Konto-/Projektabdeckung gegen die vorhandene Konsole vergleichen.

Quellen: [Model APIs](https://docs.novita.ai/api-reference/model-apis-introduction),
[Balance](https://docs.novita.ai/api-reference/basic-get-user-balance).

### SiliconFlow — offizielle User-/Balance-API, Regionen trennen

Offizielle OpenAPI-Spezifikation mit OpenAI-ähnlichen Modell-/Chat-Routen,
Bearer-Auth und `GET /user/info`. Antwortfelder unter `data`:
`balance`, `chargeBalance`, `totalBalance` als Strings. Die geprüfte
internationale Spezifikation nennt `https://api.ap.siliconflow.com/v1`;
chinesische Referenz nennt `api.siliconflow.cn`. **Keine Schlüssel/Salden über
Regionen hinweg gleichsetzen.** Währung, Bonussemantik, Berechtigungen,
Historien-API, Datenfrische und Gratis-/Länder-/Kostenregeln noch qualifizieren.
Aus den Feldnamen allein keine vollständige Finanzsemantik ableiten.
**Beides, S–M Balance/M Gateway.** Erster Test: User-Info mit einem bekannten
regionalen Account lesen und Geldfelder samt Währung gegen dessen Dashboard prüfen.

Quellen: [offizielles OpenAPI-Repository](https://github.com/siliconflow-inc/siliconflow-api/blob/main/openapi.yaml),
[chinesische User-Info-Referenz](https://siliconflow.readme.io/reference/user-info).

### DeepInfra — zusätzlich offizielle Billing- und Keylimit-APIs

OpenAI-/Anthropic-kompatible LLM-Zugänge sind im aktuellen Dokumentationsbestand
genannt. Offiziell veröffentlicht sind außerdem `GET /payment/config` mit
Ausgabenlimit in USD, `/payment/key-limits` mit monatlichem Keylimit und
`spend` (kann noch null sein) sowie `/payment/usage/tokens` mit zeitlichen
Modell-/Kostenaggregaten. Kostenganzzahlen der Usage-Antwort sind **Cents**,
Keylimits dagegen USD. Spezifikationen nennen Bearer-Auth und optionales
Sessioncookie; ob normale Inferenzkeys für alle Billingrouten genügen, bleibt
live offen. Das sind veröffentlichte API-Referenzen, keine bloß erratenen
Dashboardrouten. **Kein Barguthaben und kein Requestarchiv daraus ableiten.**
Die ebenfalls dokumentierte GPU-Limit-API misst GPU-Kapazität, kein Tokenabo.
Enforcement, Resetzeitzone, Datenfrische, Grants und Vertragscheck offen.
**Beides, M.** Erster Test: Keylimits und einen abgeschlossenen Usage-Zeitraum
lesen; fehlenden `spend` nicht als null Verbrauch darstellen.

Quellen: [Chat](https://docs.deepinfra.com/chat/overview),
[API-Index](https://docs.deepinfra.com/llms.txt),
[Config](https://docs.deepinfra.com/api-reference/billing/get-config),
[Keylimits](https://docs.deepinfra.com/api-reference/billing/get-key-limits),
[Usage](https://docs.deepinfra.com/api-reference/billing/usage-tokens).

### OpenAI API und Anthropic API — zwei eigenständige Accountanschlüsse

| Kandidat | Anschluss und unabhängige Daten | Grenzen / Aufwand / erster Test |
| --- | --- | --- |
| **OpenAI API direkt** | Native API. Offizielle Organisations-Usage und `GET /v1/organization/costs`, separater Admin-Key. | Gute externe Mengen-/Kostenaggregate, kein belegtes Cash-/ChatGPT-Abo-Restguthaben oder Einzelrequestarchiv. Admin-Key ist kein normaler Inferenzkey. Gateway M, Org-History M–H. Zuerst vorhandenen Tageszeitraum nur lesend abrufen und Projekt-/Keyabdeckung prüfen. |
| **Anthropic API direkt** | Native Messages-API; `/v1/organizations/usage_report/messages` und `/v1/organizations/cost_report`. Admin-Key, OAuth `org:admin` oder andere ausdrücklich zugelassene organisationsweite Credentials; Workspace-Key reicht nicht. | **Admin API nicht für Individual-Accounts**. Claude Enterprise Analytics und Pro-/Max-Abos nicht gleichsetzen. Externe Aggregate, kein universeller Balance-/Reset-Monitor. Gateway M, Org-History M–H. Zuerst Organisationsberechtigung und einen vorhandenen Usage-Zeitraum prüfen. |

Gratiszugang, regionale Verfügbarkeit, aktuelle harte Ausgabenlimits, persönliche
Proxy-/Workerregeln und Vertriebsbedingungen wurden für diese beiden direkten
API-Produkte hier nicht vollständig neu geprüft. Aus vorhandenen Coding-Abos
keine API-Credits automatisch ableiten. Nachträglicher Befund: Die aktuelle
Anthropic-Hilfe beschreibt explizite monatliche API-Credits für berechtigte
Max-/Team-Pläne; Bedingungen und Testpriorität im Budgetbericht.

Quellen: [OpenAI Admin APIs](https://developers.openai.com/api/docs/guides/admin-apis),
[OpenAI Costs](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/costs),
[Anthropic Usage/Cost, Rechte und Accountgrenzen](https://platform.claude.com/docs/en/manage-claude/usage-cost-api).

## B. Weitere Gateway-Auswahl mit offiziellen technischen Quellen

**Für jede Zeile sind Monitor-/History-APIs, vollständige Vertragsprüfung,
Free-/Trial-Details, Karte, Länder und harter Kostenstopp offen, soweit nicht
ausdrücklich anders angegeben.** „Nicht belegt“ heißt nicht „existiert nicht“.
Ohne externen Historybeleg zunächst nur lokale lain-Historie versprechen.
M = üblicher Gatewayanschluss; H = zusätzliche Cloud-/Protokoll-/Rechtearbeit.

| Kandidat und offizielle Quelle | Belegter Nutzen / Anschluss | Eignung, Aufwand und sinnvoller erster Test |
| --- | --- | --- |
| **[Hugging Face Inference Providers](https://huggingface.co/docs/inference-providers/index)** | Viele Hosts über HF-Token und gemeinsames Routing; eigener Provider-Key ist ein anderer Abrechnungsweg. | Gateway M. Zuerst Tokenberechtigung, Modell-/Providerroute und Abrechnungsinhaber prüfen. Aktuelle [Preisseite](https://huggingface.co/docs/inference-providers/pricing): **Free Users keine monatlichen Credits**, PRO $2 allgemeine Computecredits; kein pauschaler kostenloser LLM-Zugang. |
| **[Kilo AI Gateway](https://kilo.ai/docs/gateway/api-reference)** | Eigenständiger Aggregatoraccount, OpenAI-kompatibel; Streaming, Tools, `/models`; Base `https://ai-gateway.kilo.ai/api/v1`. | Gateway M. Zuerst öffentliche Modellliste lesen, dann bei später autorisiertem Test Toolstream/Abbruch und Tokenusage prüfen. Monitor nicht aus eingebauten Balancechecks ableiten. |
| **[Tinfoil](https://docs.tinfoil.sh/introduction)** | OpenAI-kompatible vertrauliche Inferenz; eigene SDKs für verifizierbare Ausführung. | Gateway H, wenn Attestierung Teil des Versprechens wird. Zuerst SDK-/Attestierungsweg prüfen; bloßer generischer HTTP-Anschluss belegt keine Verifikation. |
| **[FriendliAI](https://friendli.ai/docs/guides/model-apis/quickstart)** | Model APIs, Personal API Key, OpenAI-SDK-Beispiele einschließlich Streaming. | Gateway M. Zuerst Modell-/Endpointauswahl und Key-Abdeckung, danach einen belegten Tool-fähigen Stream qualifizieren. |
| **[Parasail](https://docs.parasail.io/parasail-docs)** | OpenAI-kompatibles Serverless; Elastic und Dedicated sind andere Kapazitäts-/Preismodelle. | Gateway M für Serverless, H für Deployments. Zuerst Serverless-Basisroute, Auth und Modellliste prüfen; GPU-Stunden nicht als Tokenkosten behandeln. |
| **[Cohere](https://docs.cohere.com/docs/compatibility-api)** | Offizieller OpenAI-Kompatibilitätspfad neben nativer API. | Gateway M. Zuerst unterstützte Modelle/Parameter und Toolroundtrip gegenüber nativem Format abgleichen. |
| **[Perplexity](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/overview)** | Such-/Agent-APIs; Sonar und Agent API unterscheiden. | Gateway M–H für Rechercheprofile. Zuerst Produkt/Endpoint festlegen und Such-/Toolkosten erfassen; kein austauschbarer Coding-Standardprovider. |
| **[Upstage Solar](https://console.upstage.ai/api/responses)** | OpenAI-kompatible Responses, Bearer-Key, Streaming; dokumentierte Einschränkungen wie zustandsloser Textbetrieb. | Gateway M. Zuerst Parameter-Matrix lesen, dann Tool-/Reasoningevents testen; ignorierte Parameter nicht als unterstützt markieren. |
| **[IONOS AI Model Hub](https://docs.ionos.com/cloud/ai/ai-model-hub)** | Europäische Cloudoption mit OpenAI-Endpunkten; native `/predictions` laut aktueller Doku seit Mai 2026 eingestellt. | Gateway M. Zuerst aktuelle OpenAI-Route/Auth und verfügbare Modelle prüfen. EU-Produktname allein belegt keine vollständige Datenresidenz. |
| **[STACKIT AI Model Serving](https://docs.stackit.cloud/products/data-and-ai/ai-model-serving/how-tos/use-the-models/)** | OpenAI-kompatibler regionaler Endpoint und Bearer-Token, getrennte Verwaltung. | Gateway M. Zuerst Service-/Projekt-/Tokenzuordnung und Modellliste prüfen. |
| **[OVHcloud AI Endpoints](https://help.ovhcloud.com/csm/en-ie-public-cloud-ai-endpoints-code-assistant?id=kb_article_view&sysparm_article=KB0067278)** | Offizielle Continue-Integration; OpenAI-kompatible Base unter `oai.endpoints.kepler.ai.cloud.ovh.net/v1`. | Gateway M. Zuerst Public-Cloud-Projekt, Token und aktuellen Dienststatus/Tarif prüfen; keine Gratiszusage aus früheren Betaphasen. |
| **[AWS Bedrock](https://docs.aws.amazon.com/bedrock/latest/userguide/apis.html)** | Native APIs und inzwischen auch modellabhängige OpenAI-/Anthropic-Pfade; [API-Kompatibilität](https://docs.aws.amazon.com/bedrock/latest/userguide/models-api-compatibility.html). | Gateway H. IAM, Region, Modellzugang und Runtime/Mantle-Endpunkte zuerst festlegen; Quotas und Billing separat qualifizieren. |
| **[Azure/Foundry](https://learn.microsoft.com/en-us/azure/ai-foundry/foundry-models/concepts/endpoints?tabs=python)** | Eigener Cloudaccount, OpenAI-v1-Endpoint, Modellangabe kann Deploymentname sein. | Gateway H. Zuerst Ressource/Deployment, Region und Auth prüfen; allgemeines Azure-Budget nicht als harten Inferenzstopp versprechen. |
| **[Google Vertex AI / Agent Platform](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/start/openai)** | Eigenständiger GCP-Zugang zusätzlich zum vorhandenen Gemini-API-Anschluss; OpenAI-Chat, ADC/kurzlebige Access-Tokens. | Gateway H. Zuerst Projekt/Region/IAM und Tokenrefresh qualifizieren; kein statischer API-Key-Ersatz. Offizielle Dokumentation wird inzwischen unter Agent Platform geführt. |
| **[Oracle OCI Generative AI](https://docs.oracle.com/en-us/iaas/Content/generative-ai/openai-compatible-api.htm)** | OpenAI-Chat/Responses für unterstützte Modelle/Regionen, OCI-GenAI-Keys oder IAM; native OCI-API separat. | Gateway H. Zuerst Region, Modell, Compartment-/IAM-Kontext und konkrete Route prüfen. |

## C. Zusätzliche Coding-Abos und Monitorprodukte

| Kandidat | Offizieller Befund | Einordnung / erster Test |
| --- | --- | --- |
| **GitHub Copilot** | [Billing Usage API](https://docs.github.com/en/rest/billing/usage) mit User- und Org-Endpunkten, inzwischen **AI-Credit-Usage** zusätzlich zu älteren Premium-Request-Berichten. Userroute `/users/{username}/settings/billing/ai_credit/usage`, Fine-grained PAT mit Userrecht **Plan: read**. Persönliche Route gilt für selbst gekauftes Abo; orgfinanzierte Nutzung ist dort nicht enthalten. | Monitor M; Usage-/Kostenbericht, kein automatisch vollständiger Restkontingent-Snapshot. Creditmenge, Brutto/Discount/Netto und Plan gesondert abgleichen. [Aktuelle Individual-Abrechnung](https://docs.github.com/en/copilot/concepts/billing-and-usage/individuals/billing): monatlicher Reset am 1. um 00:00 UTC; Extra-Budget separat. Erster Test: vorhandenen Monatsbericht lesen, Einheiten/Accountabdeckung prüfen. Daraus keine allgemeine Gatewayfreigabe ableiten. |
| **Cursor Teams/Organizations** | [Admin API](https://cursor.com/docs/account/teams/admin-api.md): Basic Auth `API_KEY:`, `/teams/daily-usage-data`, `/teams/spend`, Usageevents. Tages-/Eventdaten stündlich aggregiert; höchstens stündlich pollen empfohlen. | Team-Monitor/History M–H. Persönlichen Pro-Account nicht damit als unterstützt erklären. Zuerst aktuelle API-Keyrolle und Teamplan prüfen, abgeschlossenen Tageszeitraum lesen. Rest einer persönlichen Subscription und freie Inferenzweiterleitung bleiben offen. |
| **Windsurf / inzwischen Devin Desktop** | Alte Docs leiten auf [aktuelle Enterprise-API](https://docs.devin.ai/desktop/accounts/api-reference/api-introduction) um. Service-Key im JSON-Body. [GetTeamCreditBalance](https://docs.devin.ai/desktop/accounts/api-reference/get-team-credit-balance) benötigt **Billing Read**, nicht nur Teams Read-Only; liefert Seats, Add-on-Credits und Billingzyklus. | Enterprise-Monitor M–H, kein belegter persönlicher Pro-Monitor. Creditantwort gilt nur für laufenden Zyklus; Add-on-Verfügbarkeit ist kein lebenszeitlich gekauftes Gesamtguthaben. Zuerst Tarif/Rechte und Zyklusfelder lesen. Aktuelle Datenfrische und Produktmigration prüfen. |
| **BytePlus ModelArk Coding Plan** | [Übersicht](https://docs.byteplus.com/en/docs/modelark/coding-plan-personal-plan-overview): OpenAI- und Anthropic-kompatibel, ausdrücklich **nur zugelassene Codingtools**, keine allgemeine API-Nutzung; Missbrauch kann zur Sperre führen. [FAQ](https://docs.byteplus.com/en/docs/modelark/coding-plan-personal-faq): nach Quotaende kein Verbrauch anderer Pakete/Guthaben, sondern auf Reset warten. | Gateway für Abo **vorerst Vertragsfrage**; separaten PAYG-Zugang getrennt prüfen. Monitor-API nicht belegt, nur Konsole. Dokumentierte 5h-/Wochen-/Monatsfenster; Zeitzone und abweichende Beispielmengen zwischen Seiten offen. Erster Test ist Doku-/Vertragsklärung für lokalen Passthrough; falsche PAYG-Base kann Kosten auslösen. |
| **Xiaomi MiMo** | [Offizielle Plattform](https://platform.xiaomimimo.com/) bewirbt OpenAI-/Anthropic-/Responses-Anschluss und Token Plan. Verlinkte Unterseiten waren teilweise nur als Portal erreichbar. | Gateway-Kandidat M, Monitor offen. Erst stabile API-/Auth-/Plan-Referenz samt internationalem Accountzugang beschaffen; noch kein tief geprüfter Anschluss oder belegtes Gratisangebot. |

## D. Weitere regionale Kandidaten für die nächste Recherchewelle

Diese Einträge sind bewusst nur **belegte Entdeckung**, keine vollständige Qualifikation.
Rechte, Streaming/Tools, Modelllisten, Monitor, History und Gratisbedingungen müssen
noch im Detail geprüft werden. Erster Schritt jeweils öffentliche API-/Accountreferenz
und für Deutschland verfügbaren Vertrag qualifizieren, noch keine Inferenz.

| Kandidat | Offizielle Quelle / Grund für Aufnahme | Offener Hauptpunkt |
| --- | --- | --- |
| **GMI Cloud** | [Serverless Endpoints](https://docs.gmicloud.ai/inference-engine/marketplace/serverless), Modellendpunkte und Streaming dokumentiert. | Vollständiger API-/Auth-/Billinganschluss; Gateway wahrscheinlich M. |
| **Baidu Qianfan / Token Plan** | [Token Plan](https://cloud.baidu.com/product/codingplan) nennt OpenAI-/Anthropic-Protokolle und breitere Agent-/Content-Szenarien. | Nicht alte Coding-Plan-Regeln übernehmen; aktueller Vertrag, Verifikation, Länder, Quota-/History-APIs offen. Aufwand H bis geklärt. |
| **Tencent Hunyuan / TokenHub** | [Offizielle OpenAI-Referenz](https://cloud.tencent.com/document/product/1729/111007) enthält Streaming-/Toolbeispiele und verweist auf Migration zu TokenHub. | Für neue Accounts aktuellen TokenHub-Zugang qualifizieren, keinen alten Hunyuan-Endpoint blind integrieren. Aufwand H bis Migration geklärt. |

## E. Lokale und selbst betriebene Backends fehlen als eigene Kategorie

| Backend | Offizieller Anschluss | Erster Test / Aufwand |
| --- | --- | --- |
| **Ollama** | [OpenAI-Kompatibilität](https://docs.ollama.com/api/openai-compatibility) | Modellliste, Chatstream, Toolroundtrip und Abbruch am eigenen Server prüfen. M. |
| **LM Studio** | [OpenAI-Kompatibilität](https://lmstudio.ai/docs/developer/openai-compat) | Geladenes Modell, Authoption und Streamingusage prüfen. M. |
| **llama.cpp server** | [Offizieller Server](https://github.com/ggml-org/llama.cpp/tree/master/tools/server) | Chattemplate, unterstützte Routen und Toolparser zur konkreten Modellversion prüfen. M. |
| **vLLM** | [Online Serving](https://docs.vllm.ai/en/latest/serving/online_serving/) | Serverversion und Modelltemplate binden; Chat/Responses/Tools nicht pauschal gleichsetzen. M–H. |
| **SGLang** | [Offizielle OpenAI-API-Referenz](https://docs.sglang.io/docs/basic_usage/openai_api.md) | Konkrete Server-/Modellversion, Auth und Toolparser qualifizieren. M–H. |

Das sind fünf Backendtypen, keine fünf neuen Cloud-Abos. Kein monetäres
Anbieterkontingent erfinden; optional Zustand/Kapazität und eigenes lain-Journal.
Softwarelizenzen, Modelllizenzen und mögliche Bündelung/Distribution getrennt prüfen.
Ein bloßer HTTP-Anschluss autorisiert kein Mitliefern von Modellen oder Laufzeiten.

## F. Bewusst streichen oder getrennt halten

- **GitHub Models ist eingestellt:** [aktuelle offizielle Meldung](https://docs.github.com/en/github-models)
  nennt den **30.07.2026**; Playground, Inferenz, Katalog und BYOK beendet.
  Alte Suchtreffer mit `models:read` und Free-Tier-Anleitungen sind keine aktuelle
  Integrationsgrundlage. **GitHub Copilot ist ein anderes Produkt** und bleibt oben.
- **Hugging Face Freecredits:** ältere $0,10-Angaben nicht übernehmen; aktuelle
  Preisseite nennt für Free Users keine monatlichen Credits.
- **Windsurf:** Quellenumzug zu Devin Desktop und konkreten Tarif beachten;
  keine alte persönliche Quota-API aus einer Enterprise-Referenz ableiten.
- **Modellhersteller nicht doppelt zählen:** ein DeepSeek-/Qwen-/GLM-Modell bei
  OpenRouter/HF ist kein direkter DeepSeek-/Alibaba-/Z.ai-Accountanschluss.
- OpenCode-Produkte und vorhandene Provider nicht durch neue Tarifnamen als
  unabhängige neue Anbieter zählen. Unterschiedliche Entitlements dennoch getrennt
  darstellen, wenn Keys, Verträge, Kontingente oder Abrechnung abweichen.

## G. Wie aus der Breite ein belastbares Angebot wird

Vorschlag: ein breiter Entdeckungskatalog mit **getrennten Fähigkeitsangaben**
für Gateway, unabhängigen Monitor und externe Historie. Dazu sichtbarer
Qualifikationsstand: recherchiert, technisch qualifiziert, live geprüft.
Eine offizielle Setupverknüpfung darf schon im Katalog stehen, ohne eine fertige
Integration oder Vertragsfreigabe zu behaupten. Ein generisches Endpointprofil
kann Gatewaybreite schaffen; individuelle Auth-, Billing- und Quotaadapter bleiben
je Anbieter nötig. Das ist ein Produktvorschlag, keine hier durchgeführte Änderung.

Vor Freigabe jedes Eintrags die sechs Punkte des Hauptberichts vollständig schließen:
konkrete Protokolle/Modelle/Rechte, Vertrag je Nutzungsszenario, Monitorsemantik,
Historyabdeckung, Gratis-/Überlaufbedingungen und begrenzter erster Test.
Insbesondere parallele Worker nicht allein wegen eines hohen Concurrencywerts
als vertraglich erlaubt markieren. Ein lokaler Kostenstopp schützt außerdem
nur durch lain geleitete Nutzung; Kontonutzung anderer Clients bleibt unabhängig.
