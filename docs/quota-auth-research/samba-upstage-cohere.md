# SambaNova, Upstage und Cohere: Quota und Auth

Stand: 2026-10-24. Bezugsstand: `5dc916f`. Architekturvorschlag, keine angenommene
Produktentscheidung und keine Implementierung. Geprüft wurden öffentliche
Dokumentation, OpenAPI/SDK-Quelltexte und öffentlich ausgelieferte Console-Bundles.
Keine Anmeldung, Account-/Inferenzanfrage, Registrierung oder Serviceaktion.

Die Gatewayanschlüsse bestehen laut Auftrag bereits. Im geprüften Quellstand
stehen alle drei Anbieter in `QuotaService.Unsupported` auf `console`; die
Kataloge verwenden private API-Keys und leere `monitorAccessOptions`. Die älteren
Paketdokumente beschreiben zum Teil noch „prepared“: daraus wird kein aktueller
Installationsfehler abgeleitet. `QuotaReading` kann Fenster, Scope, Modell,
native Einheiten und Frische schon getrennt darstellen; das passive Groq-Muster
ist ein naheliegender Baustein. `QuotaCredential` enthält absichtlich keine
Refresh-Credentials. Ein neuer Login gehört in den Auth-/SecretStore-Bereich,
der Monitor bekommt nur eine begrenzte Zugriffsprojektion.

## Ergebnis und Priorität

| Produkt | Jetzt belegbar integrierbar | Login ohne Keykopieren | Nächster begrenzter Nachweis |
| --- | --- | --- | --- |
| SambaCloud Free / Developer | Passive RPM/RPD-Header; kein Extraaufruf | Persönliche Browser-Session plus Console-Keybootstrap ist als Alternative konkret belegt; kein öffentlicher OAuth-Vertrag gefunden | Headerfixtures; dann separate Qualifikation der öffentlichen Console-Clientverträge |
| Upstage direkte API, Signupcredit / Prepaid / PAYG | Lokale Requesttokens; Console liefert echte Org-Spend-/Credit-/Usage-Kandidaten | Browser-Session mit Org-Auswahl kann Monitor und Keybootstrap verbinden; eigener Session-/BFF-Adapter fehlt | Statische Verträge vervollständigen, danach genau ein autorisierter Read je Spend/Credit in bestehender Session |
| Cohere Evaluation | Lokale Calls/Tokens, veröffentlichte Limits; Console-Usage als gesonderter Kandidat | Tatsächlich ausgelieferter CLI-Browser-Pairingflow vorhanden; Ausgabetoken und Drittclientzulassung ungeklärt | CLIAuth-Vertrag/Lifetime/Org-Bindung und offizielle Verteilbarkeit klären |
| Cohere Production | Kostenrelevante native `billed_units` und Console-Abrechnung getrennt | Derselbe Login-/Bootstrapkandidat, aber Production-Key und Modellfreigabe separat | Dieselbe Authqualifikation, danach lesende Billing-/Usage-Belege; kein Upgrade |

„Nicht öffentlich dokumentiert gefunden“ bedeutet eine noch offene
Qualifikation. Die nachfolgenden Console-Pfade sind **First-Party-Clientbelege,
keine zugesicherten öffentlichen APIs**. Weder ihre gegenwärtige Erreichbarkeit
noch ihre Authwirkung wurde mit einem Account geprüft.

## SambaNova: SambaCloud

### Monitoring

Die offizielle [Rate-Limit-Seite](https://docs.sambanova.ai/docs/en/models/rate-limits)
nennt sechs Header in jeder Antwort:

| Fenster | Limit | Verbleibend | Reset |
| --- | --- | --- | --- |
| Requests/Minute | `x-ratelimit-limit-requests` | `x-ratelimit-remaining-requests` | `x-ratelimit-reset-requests` |
| Requests/Tag | `x-ratelimit-limit-requests-day` | `x-ratelimit-remaining-requests-day` | `x-ratelimit-reset-requests-day` |

Reset ist als Zeit *bis* zum Reset beschrieben; Format und Zeiteinheit sind dort
nicht spezifiziert. Kein blindes Interpretieren als Unixzeit oder Sekunden.
Ein defensiver Parser darf Zahlenfelder schon übernehmen und `ResetsAt` bei
unbekanntem Format offenlassen. Rohwerte nur in begrenzten, nicht geheimen
Diagnosedaten bzw. Fixtures erhalten. Erfassung erfolgt beim normalen
Inferenzrequest, auch bei 429 soweit vorhanden. Refresh sendet keine Inferenz.

Für das konfigurierte `Meta-Llama-3.3-70B-Instruct` gelten veröffentlicht Free
20 RPM / 20 RPD / 200.000 TPD, Developer 240 RPM / 48.000 RPD. Developer hat
zusätzlich 20 Millionen Tokens/Tag über alle Modelle. Free gilt ohne hinterlegte
Zahlungsmethode, Developer mit Zahlungsmethode. TPD-Restwerte sind durch diese
sechs Header **nicht** abgedeckt. Veröffentlichte Limits sind keine beobachteten
Restwerte; Keyzahl multipliziert keine Berechtigung. Datum, Konto und Modell
speichern, User-/Modell-Pool nur soweit belegt binden, keine künstliche
Keyisolierung. Fenster getrennt halten. Nach Reset oder kurzer lokaler TTL
(Vorschlag maximal fünf Minuten, bei RPM eher eine Minute) Beobachtung als alt
kennzeichnen, keinen Reststand hochzählen. Die TTL ist lain-Policy, keine
Provider-Frischezusage.

Die [offizielle Inferenz-Spezifikation](https://raw.githubusercontent.com/sambanova/sambanova-inference-api-spec/refs/heads/main/openapi.documented.json)
enthält Inferenz/Modelle und Bearer-Keyauth, keinen Billing-/Quota-Pollvertrag.
Der [Python-Client](https://raw.githubusercontent.com/sambanova/sambanova-python/main/src/sambanova/_client.py)
benutzt API-Keyauth; daraus ergibt sich kein Cloud-CLI-Login. SambaStack und
SambaStudio besitzen andere Plattform-/Endpointzugänge und dürfen nicht als
Cloud-Login importiert werden.

### Console-Alternative und Auth

Das öffentlich geladene [Console-Datenbundle S1](https://cloud.sambanova.ai/_next/static/chunks/35360-10acd791f887f198.js)
belegt same-origin GET `/api/usage?user_name=…&start_date=…&end_date=…` mit
optionalen `model_id` und `api_key_hash`, sowie `/api/credits?user_name=…` und
`/api/auth:me`. Der [Usage-Client S2](https://cloud.sambanova.ai/_next/static/chunks/app/plans/usage/page-e51f0f756261f20d.js)
verwendet UTC-Monatsgrenzen, `model_wise_usage`, `input_token`, `output_token`
und jeweils eigene Preisfelder. Das sind Account-/Modell-/Keyfilter und
historischer Verbrauch, keine RPM-/TPD-Reststände.

Das [Billingbundle S3](https://cloud.sambanova.ai/_next/static/chunks/59468-13418f0de96abfb8.js)
belegt GET `/resources/credits/balance`, Rückgabefelder `amount` und `readAt`.
Die UI zeigt eine Abwicklungsverzögerung an; ein Abrufzeitpunkt beweist keine
sofort verbuchte Inferenz. Sie hat fünf Sekunden Refreshsperre und 300 Sekunden
Clientcache; das ist kein Backend-SLA. Die Währungssemantik von `amount` vor
Übernahme separat bestätigen. Das [Billing-Seitenbundle S4](https://cloud.sambanova.ai/_next/static/chunks/app/plans/billing/page-99d49997704cb773.js)
verarbeitet außerdem `credits[]` mit `credit_value`, `balance_credit_value`,
`end_date` und `credit_type`; Werte werden im Client durch 100 geteilt. Gratis-,
gekaufte und ablaufende Grants bleiben einzelne Grants, kein universelles
Tokenbudget. Keine Bonusgranthöhe aus UI-Heuristiken ableiten.

Das [Layoutbundle S5](https://cloud.sambanova.ai/_next/static/chunks/app/layout-df257fd00f1eac70.js)
enthält POST `/api/keys` mit `userName`, `apiKeyName` und optional `expiresAt`.
Die [Keydokumentation](https://docs.sambanova.ai/docs/en/get-started/api-keys-urls)
beschreibt bis zu 25 Keys/User und nur einmal sichtbare neue Keys. Ein
persönlicher Browserlogin könnte also Credits/Usage lesen und einen gesonderten
Inferenzkey erzeugen und direkt privat speichern. **Nicht nachgewiesen**:
Antwortschema des Create, Session-/CSRF-Vertrag, Lifetime/Refresh,
Drittclient-/Verteilungsrechte und ein dedizierter Readkey. Eine Cloud-OAuth-
Clientregistrierung, Device Grant oder OAuth-Keybootstrap ist in den geöffneten
öffentlichen Verträgen nicht dokumentiert. Social-Weblogin ist kein OAuth-Grant
an lain. Keybootstrap wäre eine gesondert autorisierte Mutation, nicht Teil
eines lesenden Monitors.

**Empfehlung:** passive Header zuerst; optionaler persönlicher Consolemonitor
als gesonderter, austauschbarer Adapter. Ein Login kann konzeptionell beide
Bedarfe bedienen, aber dafür werden mindestens Sessioncredential und erzeugter
API-Key getrennt benötigt. Die bloße Existenz des Keyendpoints qualifiziert noch
keinen auslieferbaren Loginbutton. Nächster Nachweis ohne Account: Response-
Headerfixtures und Rückgabe-/Sessionvertrag des öffentlich ausgelieferten
Keyclients dokumentieren; erst danach ein explizit autorisierter, lesender
Consoleversuch auf bestehendem Konto. Keine Anmeldung hier ausgeführt.

## Upstage: direkte Console API

### Monitoring und Produktgrenzen

Die [Chat-Referenz](https://console.upstage.ai/api/chat.md) dokumentiert
`usage.prompt_tokens`, `completion_tokens`, `total_tokens` und
`completion_tokens_details.reasoning_tokens`. Sie messen einzelne Antworten,
kein Org-Guthaben. Die [Limits](https://console.upstage.ai/docs/guides/rate-limits.md)
unterscheiden RPM/TPM sowie für andere Fähigkeiten RPS/PPM. Solar Mini 4 hat
auf Tier 0 100 RPM und 50.000 TPM. Andere Commitment- und Legacy-Tiers getrennt
beibehalten; sie sind keine Restwerte oder automatische Grants. In den
geöffneten Dokumenten ist kein standalone Usage-/Balancepolling mit
Inferenzkey oder dokumentierter Remaining-/Reset-Headersatz nachgewiesen.

[Setup](https://console.upstage.ai/docs/getting-started) bewirbt $10 Signupcredit,
Prepaid und PAYG. Das ist keine Bestätigung eines individuellen Saldos oder
Ablaufs. [FAQ](https://console.upstage.ai/docs/resources/faq.md): Organisationen
teilen Keys, Abrechnung und Limits; Budget ist monatlich UTC und PAYG-bezogen.
Durch bis zu eine Sekunde Durchsetzungsverzug sind kleine Überschreitungen
möglich. Erschöpfte Credits können auf hinterlegte Zahlmittel übergehen.
Studio hat dagegen separate Sitz-/Aboquoten und tägliche Free-Refills; diese
dürfen nicht als direkte Solar-API-Freimenge angezeigt werden.

### Konkreter Consolemonitor und Loginoption

Das [öffentlich geladene Clientbundle U1](https://console.upstage.ai/_next/static/chunks/7157-768d821a4d850f5c.js)
enthält einen same-origin BFF-Transport: `/api/console/v3` vor folgenden Pfaden,
`credentials: "same-origin"` und `x-console-request: 1`. Der Client nutzt
Anmeldestatus, nicht den Solar-Inferenzkey. Der Backend-Ursprung ist dort als
`https://ap-northeast-2.apistage.ai/v3/consoleapi` hinterlegt; der BFF-Pfad ist
der beobachtete Browservertrag.

| GET-Kandidat unter `/api/console/v3` | Belegte Clientfelder / Bedeutung |
| --- | --- |
| `/organizations/{organizationId}/spend` | `month_to_date`, `period` |
| `/organizations/{organizationId}/promotions` | `credit_balance`, `total_credit`, `credit_coupons[]`; Coupons mit `paid`, `deducted_amount`, `started_at_ms`, `expired_at_ms` |
| `/organizations/{organizationId}/usage-limit` | `amount`; eingestellte Budgetgrenze |
| `/organizations/{organizationId}/rate-limit` | `rate_limit_role`; Tier, kein Reststand |
| `/organizations/{organizationId}/usage` | Query `from`, `to`, `offset`, `limit`, optional `product`, `api_key_id`, `owner_email`; `items`, `page.total`, `total` |

Geldobjekte verarbeitet der Client über
`amount_decimal.value * 10 ** amount_decimal.exp`; eine lain-Implementierung
soll Dezimalarithmetik verwenden und weder fehlende Werte noch Parsingfehler
wie der UI-Fallback als Null behandeln. Provider-as-of fehlt beim
Creditnormalizer (`asOfIso` ist leer); `fetchedAt` ist kein Buchungszeitpunkt.
Bei Credits jede Couponart, `paid` und Ablauf erhalten. Usage ist paginiert;
laut FAQ sind UTC-Zeitfilter bis 92 Tage und CSV möglich. Sichtbarkeit ist
rollenabhängig: Member sehen Org-Balance, Spend, Rechnungen und Limits lesend,
per-Key-Usage aber nur eigene Keys; Owner sehen alle. **Ein Member ist kein
dedizierter Monitor-IAM-Key**, kann außerdem eigene API-Keys verwalten.

U1 enthält `/organizations/{organizationId}/api-keys` und
`/organizations/{organizationId}/api-keys/{keyId}/reveal`; die FAQ bestätigt
Orggebundene Keyerstellung. Ein persönlicher Browserlogin könnte den Monitor
und anschließend den sicheren Import eines Inferenzkeys bedienen. Dafür
fehlen lain ein eigener Browser-Sessionconnector mit Org-/Rollenbindung,
Lifecycle/Refresh und ein qualifizierter Bootstrapvertrag. Keine öffentliche
native OAuth-/Device-/CLI-Loginregistrierung oder read-only API-Keyscope ist
in den geöffneten Quellen gefunden. Das ist eine offene Drittclientfrage,
kein behauptetes Verbot. OAuth bei Upstream-IdPs ersetzt den fehlenden
Upstage-Grant für lain nicht.

**Empfehlung:** den U1-Vertrag als aussichtsreichen persönlichen Sessionadapter
qualifizieren; Monitoring und Inferenzcredential getrennt speichern und
zugeordnete Org IDs abgleichen. Alternativ dokumentierter manueller CSV-Import,
der Datenzeitraum und Sichtbarkeit zeigt. Nächster Nachweis: statisch
Response-/Cookie-/CSRF-Vertrag und Loginfortsetzung vervollständigen; später
mit eigener bestehender Session nur Spend und Promotions lesen und gegen die
Console vergleichen. Kein Keyreveal/-create, Budgetwechsel oder Kauf im
Monitorrefresh. Keine Freischaltung von `free_only` durch eine Creditprobe.

## Cohere: Evaluation strikt von Production trennen

### Evaluation

Die aktuelle [Limitseite](https://docs.cohere.com/v2/docs/rate-limits) nennt
Evaluation/Trial kostenlos, 1.000 API-Calls/Monat und für Command A+ 20 RPM.
Die Resetzeitzone und genaue Monatsgrenze des Trialzählers sind nicht belegt.
Die [Going-Live-Seite](https://docs.cohere.com/docs/going-live) ordnet
Produktion einer gesonderten Freigabe/Vertragsannahme zu. Ein lokaler Zähler
kann nur „durch lain beobachtete Calls“ darstellen; 1.000 minus lokale Calls
ist kein zuverlässiger Restwert, besonders bei anderen Clients, Endpoints
oder Keys. Fehlgeschlagene Calls nicht ohne Providervertrag als verbrauchte
oder freie Calls klassifizieren.

### Production

Production ist bezahlt. Für neuere Chatvarianten bleiben Produktionskeys
teilweise auf Trialbedingungen; A+ hat **keine veröffentlichte normale
Production-RPM**, sondern Saleskontakt. Für Command A/R/R+/R7B nennt die
aktuelle Limitseite 500 RPM. Die ältere FAQ nennt pauschal 1.000 RPM; hier
hat die aktuelle modellspezifische Tabelle Vorrang. Ein Production-Key ist
folglich weder ein genereller A+-Upgradebeweis noch unbegrenzter Monatszugang
für die konfigurierte Route.

Die [Preissemantik](https://docs.cohere.com/docs/how-does-cohere-pricing-work.md)
unterscheidet native `usage.tokens` von `usage.billed_units`, jeweils Input/
Output. Billed Tokens können kleiner sein. Embed zählt Tokens, Rerank
Search Units. Native [Chat](https://docs.cohere.com/reference/chat.md) und
[Compatibility](https://docs.cohere.com/docs/compatibility-api.md) sind
verschiedene Antwortverträge; die installierte OpenAI-Kompatibilitätsroute
liefert nicht automatisch alle nativen Billingfelder. Kosten aus lokalen
Tokenwerten daher als Schätzung und nur mit passender Preisbasis, niemals
als Org-Saldo ausgeben. Monatsbudget und Prepaidcredit ebenfalls trennen.

### Öffentliche API gegenüber Console- und CLI-Login

Die [offizielle OpenAPI](https://raw.githubusercontent.com/cohere-ai/cohere-developer-experience/main/cohere-openapi.yaml)
und der [Python-Client](https://raw.githubusercontent.com/cohere-ai/cohere-python/main/src/cohere/base_client.py)
verwenden Bearer-Keyauth und enthalten keinen allgemeinen Billing-/Callquota-
Reader. GET `/v1/datasets/usage` liefert `organization_usage` in **Bytes**
für Datasetstorage, keine Inferenzquota.
[POST `/v1/check-api-key`](https://docs.cohere.com/reference/check-api-key)
bestätigt `valid`, optional `organization_id`, `owner_id`, aber keinen
Trial-/Productiontyp oder Reststand; in der aktuellen Navigation ist es
unter Deprecated. Connector-OAuth autorisiert einen Connector, nicht den
Login eines Inferenz-/Billingclients. Ein eigener eingeschränkter Readkey
ist in diesen Verträgen nicht dokumentiert.

Es gibt dennoch eine konkrete Alternative: Das öffentliche
[Dashboard-Appbundle C1](https://dashboard.cohere.com/_next/static/chunks/pages/_app-93016fb49a1d0e50.js)
enthält POST-RPCs unter
`https://production.api.os.cohere.com/rpc/BlobheartAPI/`:

| Methoden | Öffentlich belegte Clientsemantik |
| --- | --- |
| `CLIAuthBegin` | liefert `browserURL` und `secret` |
| `CLIAuthConfirm` | erhält `secret`; bestätigt im Browser |
| `CLIAuthCheck` | liefert `token` |
| `GetAPIUsage`, `GetAPIUsageV2` | liefern `usages` |
| `GetBillingActivity`, `GetBillingHistory` | liefern `activity` bzw. `history` |
| `CreateAPIKey`, `ViewRawAPIKey`, `GetOrCreateDefaultAPIKey` | Keybootstrapoperationen existieren; keine davon hier aufgerufen |

Das [separat geladene CLI-Seitenbundle C2](https://dashboard.cohere.com/_next/static/chunks/pages/welcome/auth-cli-bd07116a0aa4d76a.js)
belegt `/welcome/auth-cli`, angemeldete Session, `cliSession`-Query und
Allow/Deny. Das ist ein **real vorhandener proprietärer Pairingflow**, aber
kein belegter OAuth Device Grant. Für Auth-/Refreshmethoden bestehen ebenfalls
Clientstubs. C1 fügt autorisierten RPCs einen Bearer und `Request-Source:
playground` hinzu. Der Namensgeber dieses Headers legitimiert keinen lain-
Headerwert und keine Nachahmung eines offiziellen Clients.

Das [Billing-Seitenbundle C3](https://dashboard.cohere.com/_next/static/chunks/pages/billing-4a7f7fc5f062303e.js)
verwendet Org-/Zeitfilter, Usagepagination, `quantity`, `productUnit`, `total`
und `activity.balanceForMonthlyLimit`; Budgetgrenzen kommen aus
`organization.balance_limit`/`balance_limit_enabled`. Diese Budgetauslastung
ist kein „Resttrialcalls“-Feld. [Teamrollen](https://docs.cohere.com/v2/reference/teams-and-roles)
erlauben Usern Usagehistory und Trialkeyverwaltung; Owner verwalten zusätzlich
Rechnungen und Zahlung. Kein reiner Read-only-Login daraus ableitbar.

**Kann ein Login beides bedienen?** Als Session plus anschließender
API-Keybootstrap technisch plausibel und durch Clientbausteine gestützt.
Nicht nachgewiesen ist, ob das CLIAuthCheck-Token selbst Inferenz und Billing
gleichermaßen akzeptiert, welche Organisation es bindet, wie es abläuft,
refresht oder widerrufen wird und ob fremde Clients den Pairingvertrag
benutzen dürfen. Eine öffentlich dokumentierte lain-Clientregistrierung,
Redirect-/Scope-Policy oder Verteilungsfreigabe fehlt. Keinen offiziellen
Client-Identifier übernehmen. Die nur historisch suchbare CLI-Repositoryspur
`cohere-ai/co` lieferte aktuell 404; das beweist weder Abschaffung noch einen
auslieferbaren aktuellen CLIvertrag.

**Empfehlung:** Evaluation und Production als getrennte Credential-/Produkt-
zustände führen. Lokale Messungen sofort nutzbar lassen; Console-RPC und
Pairing als aussichtsreiche, gesondert zu qualifizierende Connectoroption
weiterführen. Nächster begrenzter Nachweis: offiziellen CLI-/Pairingvertrag
mit Tokenlifetime, Orgbindung und Drittclientbedingungen beschaffen bzw.
öffentlichen Client ergänzend statisch prüfen. Danach separat autorisierter
Read-only-Vergleich von Usage/Billing, ohne Keyerstellung, Go-Live oder
Inferenz. Keine erhaltene Trialquote aus `balanceForMonthlyLimit` erfinden.

## Quellenqualität und Auslieferungsgrenze

Alle URLs wurden am 2026-10-24 tatsächlich geöffnet (Webreader oder öffentliche
HTTP-GETs). Keine Searchsnippets allein tragen eine Aussage. Die maschinenlesbare
Begleitdatei enthält pro Produkt Quellen, Authoptionen, offene Punkte und
URL/Aussage/Datum. Hashes der öffentlichen Bundles sichern den untersuchten
Stand; wechselnde Chunknamen sind ein Stabilitätsrisiko, keine APIversion.

Unabhängig vom Anbieter braucht ein auslieferbarer Login: verifizierte
Zulassung für den lain-Client, klare Origins/Redirects oder Pairingbindung,
Org-/Accountauswahl, geringstmögliche Rechte, lokale geschützte Speicherung,
Refresh/Widerruf und getrennte Monitorprojektionen. Für einen persönlichen
Consoleadapter kann eine bestehende interaktive Session eine realistische
Zwischenstufe sein. Das wäre ausdrücklich ein Sessionconnector mit begrenzter
Stabilität, kein erfundener OAuth-Support und kein Monitor, der heimlich Keys,
Zahlmittel oder Pläne verändert.
