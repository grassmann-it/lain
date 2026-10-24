# Weitere Provider für lain

Recherche: **24.10.2026**, ausschließlich öffentlich zugängliche Herstellerquellen.
Bezugsstand: `main` **d6e36ed**. Dies ist eine priorisierte Recherche, keine
Integrationsfreigabe oder Live-Qualifikation. Keine Accounts angelegt, Logins,
Credentials, kostenpflichtigen Proben oder Inferenz-/Account-API-Aufrufe verwendet.

## Ausgangslage und Empfehlung

Der Katalog unter `src/Lain.Host/Providers/Catalog/` enthält bereits Alibaba Model
Studio, Cloudflare Workers AI, Gemini API, Groq, Inception, Mistral, Nebius, NVIDIA
Build, OpenCode Go, OpenRouter, Poe, Requesty, StepFun, Synthetic und xAI.
Direkte Quotaadapter gibt es außerdem für Codex, Command Code, Kimi und Z.ai.
Keiner der folgenden acht Kandidaten besitzt an diesem Stand einen eigenen
Katalogeintrag oder Quotaadapter. Die DeepSeek-Modelle bei Nebius/Synthetic und
Fireworks als Inferenzanbieter hinter einzelnen Fremdrouten ersetzen keinen
direkten Accountanschluss; eine generische kompatible Route ist ebenfalls keine
Qualifikation dieser Accounts.

**Praktische Shortlist: MiniMax, DeepSeek, Fireworks.** MiniMax ergänzt ein
Coding-Abo mit Zeitfenstern; DeepSeek bietet einen besonders klar dokumentierten
Balance-Lesezugriff; Fireworks verbindet breite Gateway-Eignung mit einer
öffentlichen Quota-API. Chutes ist ebenfalls interessant, benötigt aber mehr
Klärung der Antwortsemantik und Abo-Nutzungsgrenzen. Für einen gezielt europäischen
Gateway-Zugang ist Scaleway die stärkste Ergänzung dieser Liste.

Priorität ist eine Einschätzung für lain, kein Anbieterbenchmark:

| Rang | Kandidat | Mehrwert | Beleg für Monitor ohne Inferenz | Noch offen |
| --- | --- | --- | --- | --- |
| 1 | MiniMax direkt | Coding-Abo und eigener Herstellerzugang | Offiziell benannter Usage-Endpunkt | Antwortschema, Einheiten und konkrete Resetfelder |
| 2 | DeepSeek direkt | Herstellerzugang mit einfacher Balance | Offizielles `GET /user/balance` | Kein Reset/Prozent aus dieser Balance ableitbar |
| 3 | Fireworks | Viele offene Modelle, Coding-Anschluss | List/Get Quotas mit Limit und Nutzung | Einheiten je Quota-ID, Aktualität und Reset |
| 4 | Chutes | Offene Modelle, API-Key und OAuth | `GET /users/me/quotas`, modellbezogene Quota-Usage | Schema, minimale Scopes, Abo-/PAYG-Grenzen |
| 5 | Scaleway Generative APIs | EU-Inferenz, OpenAI-Protokolle | Kein passender Rest-/Reset-Endpunkt belegt | Erst Gateway; Monitor separat prüfen |
| 6 | Cerebras | Alternative für schnelle Inferenz | Kein read-only Rest-/Reset-Endpunkt belegt | Token-Bucket-Semantik und Abofähigkeiten |
| 7 | Together AI | Breiter Modellkatalog | Billing-Usage-API; **keine Restquote** | Beta-Freischaltung; Balance/Reset separat |
| 8 | SambaNova Cloud | Weiterer Inferenzanbieter, Free Tier | Limit-/Rest-/Reset-Header in Antworten | Nicht verbrauchende Abfrage dieser Header |

„Nicht belegt“ bedeutet: in den hier geprüften Quellen nicht gefunden. Es ist
keine Behauptung, dass der Anbieter keine solche Schnittstelle besitzt. Ein
Inferenz-Header allein qualifiziert keinen regelmäßig pollenden Monitor.

## Kandidaten und konkrete Grenzen

### 1. MiniMax direkt — Abo und PAYG getrennt qualifizieren

**Bestätigt:** Der M Plan unterstützt einen eigenen Subscription Key über
OpenAI-kompatibel `https://api.minimax.io/v1` und Anthropic-kompatibel
`https://api.minimax.io/anthropic`. Damit passt er zu den gewünschten Coding-
Harnessen. Ein gewöhnlicher API-Key verbraucht dagegen PAYG-Guthaben.
[Anschlusswerte](https://platform.minimax.io/docs/m-plan/other-tools),
[OpenAI-API und Thinking-Erhalt](https://platform.minimax.io/docs/api-reference/text-openai-api).

Die offizielle FAQ nennt `GET https://www.minimax.io/v1/token_plan/remains` mit
Bearer-Key; `mmx quota` ist ebenfalls vorgesehen. M Plan hat unabhängige 5-Stunden-
und Wochenfenster, die mit erster Nutzung starten. Geteilte Nutzung aller Tools
und Modalitäten ist relevant; Video hat nur das Wochenfenster. Subscription Keys
können nach Aboerschöpfung vorhandene Credit Packs nutzen. Das Abo ist für
individuelle interaktive Nutzung gedacht, PAYG für Produktion empfohlen.
[FAQ](https://platform.minimax.io/docs/m-plan/faq),
[Fenster und Überlauf](https://platform.minimax.io/docs/m-plan/usage-rules).

**Offen / Risiko:** Das geprüfte FAQ dokumentiert kein vollständiges JSON-Schema.
Keine festen Tokenmengen, Prozentwerte oder Resetfelder erfinden. Internationalen
`.io`-Zugang und regionale Zulassung prüfen; chinesische Domains/Keys nicht
stillschweigend gleichsetzen. Kein API-Free-Tier für den Subscription-Key belegt.
Die vollständige Assistant-/Thinking-Historie muss bei Toolfortsetzungen erhalten
bleiben.

### 2. DeepSeek direkt — guter erster Balance-Adapter

**Bestätigt:** API-Key/Bearer; OpenAI-kompatibel `https://api.deepseek.com`,
Anthropic-kompatibel `https://api.deepseek.com/anthropic`. Offizielle Guides nennen
Coding-Agent-Anschlüsse. Ein direkter Anschluss ergänzt die bereits vorhandenen
Fremdanbieter-Routen mit DeepSeek-Modellen.
[Hersteller-Einstieg](https://api-docs.deepseek.com/).

`GET https://api.deepseek.com/user/balance` liefert `is_available` und
`balance_infos[]`: `currency` (`CNY` oder `USD`), `total_balance`,
`granted_balance`, `topped_up_balance` als Strings. Das ist ein dokumentierter
Account-Lesezugriff mit demselben Bearer-Key.
[Balance-Schema](https://api-docs.deepseek.com/zh-cn/api/get-user-balance/).

**Offen / Risiko:** Guthaben ist Geld, kein Tokenkontingent. Das Schema enthält
weder periodischen Reset noch Gesamtbudget, Verbrauchsprozentsatz oder Grant-
Ablaufdatum. Mehrere Währungen getrennt zeigen; Gesamtguthaben enthält die beiden
Teilbeträge und darf nicht nochmals mit ihnen addiert werden. Kein dauerhaft
kostenloser API-Tarif oder Coding-Abo aus diesen Quellen belegt. Länderzulassung,
Zahlungsvoraussetzungen und Datenregion vor Einrichtung gesondert prüfen; der
Endpoint allein belegt keine EU-Verarbeitung.

### 3. Fireworks — Gateway plus echte Quotas

**Bestätigt:** API-Key/Bearer; OpenAI-kompatibel
`https://api.fireworks.ai/inference/v1`. Das Streaming liefert Usage im letzten
Chunk; eigener Modellressourcenname erforderlich.
[Kompatibilität](https://docs.fireworks.ai/tools-sdks/openai-compatibility).

`GET /v1/accounts/{account_id}/quotas` und die Einzelabfrage unter
`/quotas/{quota_id}` liefern `name`, `value`, `maxValue`, `usage`, `updateTime`.
`value` ist das tatsächlich erzwungene Limit, `maxValue` die maximal genehmigte
Höhe. Accountquotas umfassen unterschiedliche Größen wie Requestrate, GPUs und
Monatsausgaben; **nur passend identifizierte Quotas** lassen sich als Restbudget
anzeigen.
[List Quotas](https://docs.fireworks.ai/api-reference/list-quotas),
[Get Quota](https://docs.fireworks.ai/api-reference/get-quota),
[Quota-Arten](https://docs.fireworks.ai/guides/quotas_usage/account-quotas).

Prepaid-Credits, optionaler Auto Reload und monatliches Ausgabenlimit sind
getrennte Grenzen. Total Spend kann deutlich verzögert eintreffen. US-only
Serverless hat eigene Modellrouten/Host; EU-only ist laut Dokumentation über
Sales anzufragen.
[Billing](https://docs.fireworks.ai/faq-new/billing-pricing/how-does-billing-and-credit-usage-work),
[Regionen](https://docs.fireworks.ai/serverless/us-only-serverless).

**Offen / Risiko:** `updateTime` ist kein Reset. Minimale Leserechte, Quota-IDs,
Einheiten, Frische und Monatsgrenze qualifizieren. PAYG ist belegt; ein aktuelles
Abo-/Gratisversprechen wird hier nicht abgeleitet.

### 4. Chutes — interessant, aber Abo-Überlauf beachten

**Bestätigt:** OpenAI-kompatibel `https://llm.chutes.ai/v1`, Bearer mit `cpk_`-
API-Key, Modelle über `/v1/models`. API-Key und OAuth sind offiziell vorgesehen;
OAuth kennt unter anderem `account:read` und `billing:read`.
[Anschluss](https://chutes.ai/integrations),
[OAuth-Scopes](https://chutes.ai/docs/sign-in-with-chutes/overview).

Die Users-Referenz nennt `GET /users/me/quotas` sowie
`GET /users/me/quota_usage/{chute_id}` am Managementhost `api.chutes.ai`.
Die ebenfalls sichtbaren **Admin**-Balance-/Quota-Routen sind kein Nachweis für
normale Nutzerrechte.
[Users API](https://chutes.ai/docs/api-reference/users).

PAYG und optionale Plus-/Pro-Abos sind dokumentiert; Abos kombinieren tägliche
Requests, Monats- und 4-Stunden-Grenzen. Überschreitungen können PAYG auslösen;
ein Free Tier wird aktuell verneint. Die Abo-Bedingungen erlauben keine großen
automatisierten Inferenzlasten oder Limitumgehung durch mehrere Abos.
[Preise und Grenzen](https://chutes.ai/pricing),
[Nutzungsbedingungen](https://chutes.ai/terms).

**Offen / Risiko:** Quota-Antwortschema, Resetzeit, Balancezugriff mit minimalen
Rechten und die zulässige konkrete Workerlast prüfen. TEE ist keine belegte
EU-Datenregion; Länderzulassung und regionale Bindung bleiben offen. Keine
Abo-Route als kostenfrei erzwingen, solange der PAYG-Überlauf nicht kontrolliert ist.

### 5. Scaleway Generative APIs — europäische Gateway-Ergänzung

**Bestätigt:** Bearer mit Scaleway Secret Key und passenden IAM-Rechten;
OpenAI-kompatibel `https://api.scaleway.ai/v1`. Die API-Referenz nennt Paris
(`fr-par`) und unter anderem Chat Completions, Responses, Models und Embeddings.
[Quickstart](https://www.scaleway.com/en/docs/generative-apis/quickstart/),
[API-Referenz](https://www.scaleway.com/en/developers/api/generative-apis).

Modelle für Code und Tool Calling sind dokumentiert. Die Responses-Kompatibilität
hat Grenzen: keine Stateful Conversations und keine eingebauten Tools jenseits
Function Calling. Die Produktseite nennt einmalig 1 Mio. kostenlose Tokens für
Neukunden und danach PAYG; sie enthält zugleich einen älteren FAQ-Satz über eine
kostenfreie Beta. Daher vor Tarifübernahme gegen verbindliche Abrechnung prüfen.
[Kompatibilität](https://www.scaleway.com/en/docs/generative-apis/reference-content/openai-compatibility/),
[Produkt und Preisangaben](https://www.scaleway.com/en/generative-apis/).

**Offen / Risiko:** Kein eigener dokumentierter Rest-/Reset-Lesezugriff in den
geprüften Quellen. Billing oder ein nominelles Free Allowance genügt nicht für
Monitorwerte. IAM-Zuschnitt, KYC/Zahlungsanforderungen und aktuelle Freimenge
qualifizieren; keine automatisch wiederkehrende Monatsfreimenge versprechen.

### 6. Cerebras — alternative Inferenz, Monitor noch unqualifiziert

**Bestätigt:** API-Key, OpenAI-kompatibel `https://api.cerebras.ai/v1`; relevante
Modellbesonderheiten sind dokumentiert, etwa Einschränkungen bei gleichzeitigen
Tools und Structured Output.
[Kompatibilität](https://inference-docs.cerebras.ai/resources/openai).

Die aktuelle Doku beschreibt zeitlich/finanziell begrenzte Trial-Credits nach
verifizierter Zahlungsmethode, PAYG sowie monatliche Modellabos. Keinen dauerhaft
kostenfreien Tarif unterstellen. Rate Limits gelten pro Organisation/Modell;
Token Buckets füllen sich kontinuierlich auf. Uncached- und Total-Token-Limits
sind unabhängig, zusätzliche Projektlimits möglich.
[Limits und Trial](https://inference-docs.cerebras.ai/support/rate-limits),
[Billing und Abos](https://inference-docs.cerebras.ai/console/account-billing).

**Offen / Risiko:** Die geprüfte Doku verweist für aktuelle Balance/Limits auf die
Konsole. Kein geeigneter read-only Rest-/Reset-Endpunkt belegt. Eine kontinuierliche
Auffüllung darf nicht als fester Reset dargestellt werden. Datenregion,
Länderzulassung und nutzbarer konkreter Abotarif müssen geprüft werden.

### 7. Together AI — Gateway und spätere Billing-Historie

**Bestätigt:** API-Key/Bearer, aktuelle OpenAI-Basis `https://api.together.ai/v1`.
[Kompatibilität](https://docs.together.ai/docs/inference/openai-compatibility).
Die aktuelle Billing-Doku beschreibt Prepaid, Mindestkauf zum Einstieg und keinen
Free Trial. Projekte teilen das Organisationsguthaben; API-Keys besitzen laut
Spend-Control-Doku keine unabhängigen Ausgabenlimits.
[Credits](https://docs.together.ai/docs/billing-credits),
[Spend Controls](https://docs.together.ai/docs/billing-spend-controls).

`GET /v1/billing/usage` ist offiziell als Beta mit vorheriger Freischaltung
beschrieben. Es liefert organisationsweite, kostenannotierte Nutzungspositionen,
auch beim Aufruf mit einem Projektschlüssel; Daten sind verzögert. Das ist für
spätere externe Historie interessant, **keine aktuelle Balance oder Resetquote**.
[Billing-API und Berechtigungsumfang](https://docs.together.ai/docs/billing-spend-controls).

**Offen / Risiko:** Restguthaben-/Reset-Endpunkt unbestätigt. Geo-spezifische
Inferenz ist im offiziellen [Dokumentationsindex](https://docs.together.ai/llms.txt)
für USA/Kanada aufgeführt, die verlinkte Detailseite war hier nicht abrufbar;
konkrete Hosts/Modelle und EU-Möglichkeit vor Einrichtung prüfen. Breiter
Organisationszugriff braucht sichtbare Herkunft und Shared-Budget-Zuordnung.

### 8. SambaNova Cloud — begrenzter Free Tier, Antwortheader

**Bestätigt:** Account und API-Key, OpenAI-kompatibler Anschluss. Der offizielle
VS-Code-Guide nennt `https://api.sambanova.ai/v1/chat/completions` mit Bearer;
SambaStack/Dedicated kann andere URLs haben.
[Quickstart](https://docs.sambanova.ai/docs/en/get-started/quickstart),
[Cloud-Endpunkt](https://docs.sambanova.ai/docs/en/integrations/vscode).

Die Limits-Doku unterscheidet Free Tier ohne Zahlungsmethode und Developer Tier
mit Zahlungsmethode. Sie dokumentiert RPM/RPD sowie Tokenlimits und die Header
`x-ratelimit-limit-requests`, `x-ratelimit-remaining-requests`,
`x-ratelimit-reset-requests` samt Tagesvarianten. PAYG-Modellpreise sind öffentlich.
[Limits und Header](https://docs.sambanova.ai/docs/en/models/rate-limits),
[Preise](https://cloud.sambanova.ai/plans/pricing).

**Offen / Risiko:** Kein nicht verbrauchender Endpunkt belegt, der diese Header
zuverlässig liefert; keine Inferenz nur zum Quota-Polling voraussetzen. Doku-
Tabellenbeschriftungen sind teils uneinheitlich: keine statischen Freimengen
übernehmen, Accounttier live prüfen. Kein Abo, Balance-API, EU-Datenregion oder
Länderverzeichnis aus den geprüften Quellen gesichert.

## Nächste Qualifikation für die Shortlist

| Kandidat | Erster abgegrenzter Nachweis | Erst danach zusagen |
| --- | --- | --- |
| MiniMax | Subscription-Key-Lesezugriff gegen offizielles `remains`-Schema aufnehmen; Fenster, Einheiten, Reset und Abo/Credit-Pack-Trennung belegen; internationalen Accountzugang prüfen. | Monitor mit zwei unabhängig benannten Fenstern; Gateway erst nach Tool-/Thinking-/Streaming-Fortsetzungstest. |
| DeepSeek | Offizielles Balance-Schema als Fixture für Strings, mehrere Währungen, leere/fehlende Werte und `is_available=false`; später zulässige echte Lesestichprobe. | Geldguthaben mit Herkunft/Frische und geteiltem Accountbudget; kein erfundener Reset oder Prozentwert. |
| Fireworks | Mit minimal berechtigtem Key und Account-ID zunächst nur List/Get lesen; Quota-IDs samt Einheiten und `usage`-Abdeckung sichern, Updatezeit von Reset unterscheiden. | Monitor für die konkret belegten Budgetarten; PAYG-Guthaben, Monatslimit und Rate Limit bleiben getrennt. |

Bei einer später beauftragten Umsetzung zunächst Katalog/Einrichtung und Quota-
Adapter getrennt qualifizieren. Alle acht Kandidaten bleiben **live `not_tested`**.
Die Gatewayprobe braucht je konkretem Modell Streaming, Toolfortsetzung,
Reasoning und vollständige Usage; allgemeine „OpenAI-kompatibel“-Aussagen reichen
nicht. Für Monitor darf kein lokaler Proxy oder Inferenzrequest notwendig werden.
Unbekannte Werte bleiben unbekannt. Budgetänderungen werden nicht in vermeintliche
Requests oder Tokens umgerechnet. Dieser Zuschnitt folgt dem bestätigten
[Produktziel](product-scope.md) und dem noch vorgeschlagenen
[Monitor-Modus](monitor-mode.md).
