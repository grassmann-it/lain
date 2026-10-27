# Providerreview: Alibaba, Mistral, Nebius und Inception

Stand **25.10.2026**, geprüfter lain-Quellstand **`cbf083f`**. Lesender Review;
keine Aussage über die installierte Hostversion oder Rechte eines realen Kontos.
Öffentliche Docs und offizielle CLI-Quellen wurden heute neu abgerufen, jeweils
höchstens zwei öffentliche GETs gleichzeitig. Keine Logins, privaten Profile,
Credentials/Cookies, Quota-/Billing-/Inferenzrequests, Käufe, Builds oder Installationen.
Die Forschungsdateien vom 24.10. bleiben unverändert. Diese Datei ist ein Vorschlag,
keine Freigabe einer bisher gesperrten Verbindungsmethode.

## Ergebnis für die Anzeige

| Anbieter / Produkt | Kurzer deutscher Grund bei fehlender Usage | Konkreter nächster Schritt |
| --- | --- | --- |
| Alibaba gewöhnliche Model-Studio-/DashScope-API, Trial | „Trialrest ist beim Anbieter lesbar. Der isolierte Nutzungszugang ist in lain noch nicht freigeschaltet.“ | Maintainer: offiziellen CLI-Connector samt eigener Console-Capability fertig qualifizieren und registrieren; bis dahin Model-Studio-Free-Quota-Seite öffnen. Kein weiterer gewöhnlicher API-Key erforderlich. |
| Alibaba gewöhnliche API, PAYG | „Der API-Key liefert keinen Cloud-Kontostand. Die regionale Nutzungsverbindung fehlt noch.“ | Workspace-Usage über denselben qualifizierten Console-Connector; Finanzsaldo nur optional über getrennte BSS-Rechte. |
| Alibaba Coding Plan | „Coding-Plan-Nutzung ist separat lesbar. Ein eigener Subscription-Monitor fehlt noch.“ | Bestehende Subscription über isolierte Consoleanmeldung lesen; kein `sk-sp`-Key für reines Monitoring und kein Aboabschluss. |
| Mistral Free / Pro / Education / Team | „Nutzung und Credits sind im Mistral-Dashboard sichtbar. Eine passende programmatische Restquelle ist hier noch nicht nachgewiesen.“ | Admin-Panel öffnen. Vibe-Login verbessert die Keyübernahme, liefert aber keinen nachgewiesenen Restzähler. **Kein Enterprise-/Pro-Pflichtfeld auf Verdacht.** |
| Mistral Enterprise Admin | „Für Organisationsberichte fehlen der separate Admin-Zugang und der lain-Reader.“ | Bei bereits freigeschaltetem Enterprise-Konto dedizierten Org-Admin-Key als eigenständigen Monitor anbinden; zuvor offiziellen Headerwiderspruch klären. |
| Nebius Token Factory Serverless | „RPM/TPM sind aus normalen Antworten lesbar; Kontoguthaben ist damit nicht messbar.“ | Passive Headeraufnahme ergänzen; Org-Billing für Guthaben öffnen. Ohne beobachtete Antwort „Noch keine Ratenlimits beobachtet“. |
| Nebius Dedicated | „Dedicated-Metriken sind separat lesbar; ein Reader und die Projektbindung fehlen.“ | Bestehenden Projektkey + konkrete Projekt-ID für Prometheusreader qualifizieren. Keine neue Dedicated-Ressource anlegen. |
| Inception Mercury | „Lokal beobachtete Tokens sind verfügbar. Ein Kontorest- oder Billing-Reader ist noch nicht nachgewiesen.“ | Billing-Seite öffnen; bei Hersteller unterstützten Account-Read anfragen. 100 Mio. Angebotstokens niemals als gemessenen Rest anzeigen. |

„Nicht nachgewiesen“ bezeichnet den hier geprüften öffentlichen Umfang. Es ist
keine Behauptung, dass eine unveröffentlichte/anders berechtigte Anbieterfunktion
nicht existiert. Ein zusätzliches Secret ist nur sinnvoll, wenn es eine belegte
Quelle entsperrt. Keine Inferenz zur Aktualisierung der Monitoranzeige auslösen.

## Geprüfter lain-Code: Fähigkeiten und tatsächliche Lücken

- `src/Lain.Host/Accounts/ConnectionMethodCatalog.cs` veröffentlicht gewöhnliche
  API-Keymethoden für alle vier Anbieter mit `inference/models`, **ohne `quota`**.
  Alibaba und Mistral stehen zusätzlich als `official-cli` auf `unavailable`.
  Die hier teilweise erwartete Klasse `AccountConnectionMethodCatalog` heißt im
  aktuellen Host tatsächlich `ConnectionMethodCatalog`.
- `src/Lain.Host/Accounts/Monitoring/MonitorAccessCatalog.cs` enthält für diese
  vier Anbieter **keine** Monitoroption. `Program.cs` registriert keinen
  Alibaba-, Mistral-, Nebius- oder Inception-Quotaadapter. `QuotaService` kann eine
  dokumentierte Quelle ohne registrierten Reader nicht aus dem Anbieterlink ableiten.
- **Alibaba ist weiter als „kein Adapter“:** `AlibabaConsoleQuotaAdapter.cs`,
  `AlibabaCliConsoleReadOwner.cs`, `AlibabaOwnedProfileReader.cs` und
  `AlibabaCliLoginAdapter.cs` existieren. Der Reader erlaubt genau Trialrest,
  Schutzstatus und Online-Workspaceusage. Die CLI-Methode bleibt gesperrt;
  `BeginAsync` lehnt `quota` und gemischte Zwecke vor Prozessstart ab. Die heutige
  Veröffentlichung kann ein statisches Inferenzcredential übernehmen, aber nicht
  gleichzeitig seine getrennte Console-Capability veröffentlichen. Daher fehlt
  **Production-Wiring und Ownership-Integration**, nicht die ganze Parserarbeit.
- Alibaba-Manifest `docs/account-connect-qualification/cli/alibaba.json`:
  `disabled-unqualified`, kein Artifacthash; Binary/Isolation/Identität ungeprüft.
  Der Parser benötigt zusätzlich qualifizierte Modellunits und aktive
  Grantstatuswerte. Standardmäßig leere Status-Allowlist ergibt bewusst **keine
  verfügbare numerische Trialallowance**. Ein bloßes Registrieren des Adapters
  wäre daher keine vollständige Lösung. Der bestehende Reader nutzt Beijing /
  Singapore und exakten Site-/Workspace-/Bindingabgleich; fremde Regionen fallen
  nicht auf Beijing zurück. Daten mit unbekannter Providerfrische bleiben unbekannt.
- **Mistral:** `MistralVibeCliLoginAdapter.cs` ist ein
  `UnavailableCliLoginAdapter`; kein Admin-Billingreader implementiert. Sein
  Blocker nennt private Keyring-/`.env`-Isolation, gezielte neue Keyübergabe und
  externe Studio-/Vibe-Workspaceberechtigung. Eine Vibe-Planidentität gilt nicht
  als verbleibende Allowance. Manifest ebenfalls `disabled-unqualified`.
- **Nebius/Inception:** Katalogstatus `prepared`, `LiveVerification=not_tested`,
  `QuotaSupport=unsupported`. Keine entsprechenden Quotaadapter. Nebius hat im
  Host auch keinen Groq-artigen passiven Headeradapter. Dass das Core-/Gateway-
  Journal lokale Requests/Tokens erfassen kann, erzeugt keinen Kontorestreader.
- `TrialProviderConfiguration.cs` trennt gewöhnliche Alibaba-Keys/Workspacehosts
  von Coding Plan und erlaubt nur ordinary Trial/PAYG. Das ist fachlich durch
  aktuelle Coding-Plan-Bedingungen begründet, nicht bloß ein fehlendes OAuth.
  Für Nebius/Inception sind Region/Workspacefelder ausdrücklich unzulässig;
  ein Trial-/PAYG-Klick ist dort kein unterschiedlicher Authvertrag.
- `DirectApiModelAdapter.cs` kann Metadaten lesen. Ein `GET /models` bestätigt
  Modelllisten-Zugriff, weder Inferenzberechtigung noch Billing/Trialrest.

Die vorhandenen lokalen CLI-Fixtureprüfungen sind keine echte Alibaba-/Mistral-
Release-, Login- oder Anbieterreadqualifikation; siehe
`docs/account-connect-qualification/cli/README.md`. Dieser Review führte sie
nicht erneut aus.

## Alibaba: gewöhnliche API, Trial und PAYG

**Produkt/Tarif.** Die heute gelesene Free-Quota-Doku [A1] verlangt Singapore +
Service Deployment Scope `International`, vervollständigte Kontoinformationen
und Dienstaktivierung/Agreement. Sie verlangt für dieses Angebot keinen Proplan.
Trial gilt je konkretem Modell/Snapshot, accountweit mit RAM-Nutzern geteilt,
Input + Output gemeinsam. Für neue Aktivierungen gilt regulär 90 Tage ab dem
spätesten relevanten Ereignis; tatsächlichen Ablauf vom Grant lesen. Kein
monatlicher Reset. Vor September aktivierte Accounts können andere Regeln haben.
Dass das Angebot existiert, beweist weder Grant noch Rest eines bestehenden Kontos.

Gewöhnliche Keys verbrauchen automatisch `free quota > resource plan > savings
plan > pay-as-you-go`; Token-Plan-/Coding-Plan-Keys verbrauchen diesen Trial nicht.
PAYG kann nach Verbrauch/Ablauf automatisch folgen. `Free Quota Only` existiert,
ist standardmäßig aus, wirkt verzögert und lässt sich nicht für jedes Modell
setzen. Beobachteter Schutzstatus ist kein Nachweis sofortiger Durchsetzung.

**Bestes Onboarding.** Heute vorhandenen gewöhnlichen regionalen Key einmal
übernehmen. Ziel nach Qualifikation: offizielles
`bl auth login --console --console-site international --config lain` in eigenem
`BAILIAN_CONFIG_DIR`, Browserlogin, gezielte Übergabe des dabei neu erhaltenen
ordinary API-Keys plus separat gehaltene Console-Session. Bereits bestehender
Inferenzaccount braucht für einen reinen Monitorconnect keinen zweiten API-Key.
Kein Zugriff auf gewöhnliche Bailianprofile oder Browsercookies.

Der aktuelle offizielle CLI-Commit ist weiterhin
`3c8031f4d994c30820eb4d145d9404ba4a0acd39` (GitHub `commits/main` heute geprüft,
Commitdatum 23.10.2026); Sourcepaket 2.5.1 ist kein qualifiziertes Releasebinary.
Der erneut gelesene Login [A2] öffnet
`https://modelstudio.console.alibabacloud.com/console-login?notice=127.0.0.1:<port>?state=<random>`;
`needapikey=true` kann Keybootstrap anfordern. Callback enthält Consoletoken und
optional `api_key/base_url/workspace_id/console_region/console_site`, 15 Minuten
Timeout, Zufallsstate. Das ist ein **offizieller CLI-Browserflow**, kein belegter
frei registrierbarer lain-OAuthclientvertrag. Für eine eigene OAuthimplementierung
fehlen belegte Clientregistrierung, Redirects/Scopes und Refreshvertrag.

**Lesbare Werte / genauer Transport.** Der aktuelle offizielle Gatewaycode [A3]
nutzt `POST https://bailian-singapore-cs.alibabacloud.com/cli/api.json?action=IntlBroadScopeAspnGateway&product=sfm_bailian&api=<encoded-name>`.
Auth: `Authorization: Bearer <ConsoleToken>`; form-urlencoded `params` (JSON mit
`Api`, `V:1.0`, `Data` und `cornerstoneParam`) plus `region`. Beijing-International:
`https://bailian-cs.console.alibabacloud.com/cli/api.json`, Aktion
`BroadScopeAspnGateway`; domestic besitzt eigene Hosts. CLI-Regionentabelle
belegt ausschließlich Beijing/Singapore. Weitere Inferenzregionen beweisen keine
entsprechenden Console-Reportingrouten.

| Readoperation | Scope und messbare Daten | Grenze |
| --- | --- | --- |
| `zeldaEasy.bailian-commerce.freeTrial.queryFreeTierQuota`, Payload `queryFreeTierQuotaRequest.models` | Modellgrant: `quotaInitTotal`, `quotaTotal`=Rest, `quotaValidityPeriod`=Epoch-ms, `quotaStatus` | Trialablauf statt Reset; Units/aktive Statuswerte qualifizieren; leere Antwort ≠ 0. |
| `zeldaEasy.bailian-commerce.freeTrial.queryFreeTierOnlyStatus` | `freeTierOnlyStatuses`: Modell + Boolean | Schutzbeobachtung, kein Schreibauftrag und keine Durchsetzungsgarantie. |
| `zeldaEasy.bailian-telemetry.model.getModelUsageStatistic` / `listModelUsageStatisticData` | Workspace, `reqDTO.startTime/endTime`, `filterWorkspaceId`, `modelCallSource: Online`; Calls + `usages[key,value,unit]` | Usagehistory, kein Geldrest; kein Batch-Vollständigkeitsversprechen. |
| `GET /api/v1/models/limits` am passenden regionalen Model-Studio-Host (Sourcebeleg aus Bericht 24.10. wiederverwendet) | gewöhnlicher API-Key; konfigurierte Modell-/Workspacegrenzen und Perioden | Obergrenzen ohne Verbrauch, kein Remaining. |

[A4] bestätigt Trialfelder und Telemetrynamen erneut. Rohe Consoleantwort erhalten,
da manche menschenlesbaren CLI-Kommandos den Ablauf auf lokale Tagesdaten kürzen.
Providerzeit und Abholzeit getrennt; Monitoring ist minutenweise und ausdrücklich
keine Abrechnungsgrundlage [A1]. Workspace-/Console-Scopes bleiben getrennt von
API-Keyberechtigung: Modellmonitoring benötigt einmalige Aktivierung durch den
Cloudkontoinhaber und `ModelObservation-FullAccess`; Keymanagement benötigt
Super-/Workspaceadministrator bzw. entsprechende API-Key-Seitenberechtigung [A5].

**Finanzen separat.** BSS `QueryAccountBalance`, Version `2017-12-14`, liefert
`AvailableAmount`, `AvailableCashAmount`, `CreditAmount`, `Currency` [A6]. Das ist
Cloudkontofinanzsaldo, kein Trialtokenpool. BSS `QueryBill` ist monatliche,
paginierte Settlementhistory. Auth: getrennte signierte Cloud-AK/SK bzw. STS;
RAM `AliyunBSSReadOnlyAccess` oder enger `bssapi:QueryAccountBalance` /
`bssapi:QueryBill`, Resource `*` [A7]. Keine dieser Rechte folgt aus Inferenzkey
oder Consolelogin. **Internationale BSS-Endpointzuordnung und Model-Studio-
Produktfilter sind weiterhin nicht vollständig qualifiziert**; keinen geratenen
finanziellen Host ausliefern. Optionaler Finanzconnector, kein nachträglicher
AK/SK-Zwang im gewöhnlichen Login.

Der separat dokumentierte **OAuth-Inferenzpool 2.000 Calls/Tag** [A1] ist ein
anderer Pool. Ordinary-Key-Trial und Coding Plan messen dessen Verbrauch nicht.
Unterstützte Drittclients, exakter Verbrauchsread und Resetzeitzone sind hier
nicht nachgewiesen; ihn weder als unmöglich noch als bereits verfügbaren lain-
Login darstellen. Der CLI-Consolelogin allein qualifiziert diesen Pool nicht.

## Alibaba Coding Plan: eigene Subscription und erlaubte Harnessnutzung

Heute dokumentiert: Pro 50 USD/Monat; Lite ist für neue Abschlüsse/Renewals
eingestellt. Pro ist damit eine **belegte Produktbedingung für neue Coding-Plan-
Abschlüsse**, keine allgemeine Alibaba-Monitorvoraussetzung. Aktive ältere Lite-
Subscriptions können bis Ablauf bestehen. Begrenzte Slots werden laut [A8]
täglich 00:00 UTC+08 aufgefüllt; dies betrifft einen neuen Kauf, nicht das Lesen
eines bestehenden Plans. RAM-Nutzer benötigen laut Kaufanleitung Workspace-
Administratorrechte zum Abonnieren. Ein reiner Usageconnect kauft nichts.

Inferenz: eigener `sk-sp-…`-Key, OpenAIbase
`https://coding-intl.dashscope.aliyuncs.com/v1`, Anthropicbase
`https://coding-intl.dashscope.aliyuncs.com/apps/anthropic`. Gewöhnlicher `sk-…`
und Workspacehost sind nicht austauschbar. Aktuelle Bedingungen erlauben
interaktive Programmierwerkzeuge einschließlich aufgeführter Agent-/CLIharnesses,
verbieten ausdrücklich automatisierte Skripte, Applicationbackends und andere
nichtinteraktive Nutzung [A8]. Für lains bestehenden Backendpfad bleibt der
Ausschluss folgerichtig; daraus folgt **kein Verbot eines separaten Console-
Nutzungsreaders** und kein pauschales „alle Agenten verboten“.

Offizieller CLI-Read [A9], Console-Bearer/Gateway wie oben:
`zeldaEasy.broadscope-bailian.codingPlan.queryCodingPlanInstanceInfoV2`, Payload
`queryCodingPlanInstanceInfoRequest:{commodityCode:sfm_codingplan_public_intl,onlyLatestOne:true}`
(domestic: `sfm_codingplan_public_cn`). Response:
`codingPlanInstanceInfos[].status/codingPlanQuotaInfo` mit je
`per5Hour/perWeek/perBillMonth` + `UsedQuota`, `TotalQuota`, `QuotaNextRefreshTime`.
Messgröße **Modellcalls**, nicht Userprompts oder Tokens. Drei gleichzeitige Caps:
5h rollend (minutenweise Freigabe des Verbrauchs vor 5h), Woche Montag
00:00 UTC+08, Monat am Verlängerungstag 00:00 UTC+08 [A8]. Source wählt erste
`VALID`-Instanz; das ist keine robuste Zuordnung mehrerer Subscriptions. Instanz-
und Accountbindung qualifizieren, fehlende Felder nicht 0 setzen.

Ziel: eigener monitor-only Produkteinstieg; vorhandenen eigenen Consoleconnector
bei nachgewiesen identischem Account/Site/Region wiederverwenden. Kein
Subscription-Inferenzkey erforderlich. Lain implementiert diese Coding-Plan-
Lesemethode derzeit nicht; der ordinary Alibaba-Adapter schließt sie explizit aus.

## Mistral: gemeinsame Pläne, Vibe-Keybootstrap, Enterprise-Reader

**Free / Subscription / PAYG.** Aktuelle Docs [M1/M2] bestätigen Free mode ohne
Kreditkarte und globale Pläne Free/Pro/Education/Team/Enterprise. Monatlich
enthaltene Usage wird zwischen Studio/API/Vibe geteilt und zuerst verbraucht.
PAYG ist ausdrücklich kein separater APIplan, sondern optionaler prepaid Overflow.
Nach Ende der enthaltenen Usage kann Nutzung ohne PAYG bis zur nächsten
Billingperiode stoppen. Die individuelle Allowance/Höhe ist aus dem Login nicht
abzulesen; Kalender-Monatsusage ist nicht automatisch Abobudgetreset.

**Onboarding jetzt:** gewöhnlicher bestehender Studio-Workspacekey, einmal
verdeckt übernehmen, direkte Keyseite öffnen. Auth an Inferenz
`https://api.mistral.ai/v1`: `Authorization: Bearer <WorkspaceKey>`. Kein
zusätzliches Enterprisecredential verlangen, solange nur dieser Produktzugang
gewünscht ist. Derselbe Vibe-Key funktioniert laut [M3] in Free mode, Paid und
PAYG; ein Paid-/Pro-Klick ist kein besonderer Authschritt.

**Vibe kann Browserbootstrap.** Aktuelle CLI-Doku [M3]: standardmäßig Browser-
Sign-in, `vibe --setup`, Credentialprovisionierung; Speicherung OS-Keyring,
Fallback private `~/.vibe/.env`. Offizieller Sourcecommit
`63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477` ist laut heute geprüftem
`commits/main` weiterhin aktuell (22.10.2026). Er belegt [M4]:
`POST https://console.mistral.ai/api/vibe/sign-in` mit S256-Challenge →
`process_id/sign_in_url/poll_url/expires_at` → Browser + Polling →
`POST /vibe/sign-in/{process_id}/exchange` mit `exchange_token/code_verifier` →
**API-Key**. Workspace-Keyerstellungsrechte sind relevant; Source hat einen
expliziten Fehler „Your current workspace can't be used for Mistral Vibe“.

Das ist kein nachgewiesener offener OAuth-/Devicegrant für neue Drittclients,
kein Admin-Key und kein dokumentierter Refreshgrant. Bester nächster
Maintainer-Schritt: offizielle CLIversion/Binary, vollständig getrennte private
Persistenz, gezielte frische Keyübergabe und Studio-/Vibe-Keyproduktscope
qualifizieren; erst dann CLIconnector oder unterstützten eigenen Flow aktivieren.
Kein gewöhnliches Keyring-/`.env`-Auslesen. `whoami`/Planidentität liefert keine
Remainingallowance. Die fehlende lain-Qualifikation ist kein Beleg einer fehlenden
Anbieter-Browserloginfähigkeit.

**Enterprise Admin ist tatsächlich beschränkt.** Heute [M5/M6]: Enterprise-only,
nicht self-service; Kontakt Account-/Salesteam. Dedizierter Admin-Key aus
Enterprisebereich des Admin Panel, eine Organisation, Erstellung verlangt
Enterprise-Account-Admin. Ein Standardkey funktioniert ausdrücklich unabhängig
von der Rolle seines Inhabers nicht. Kein enger read-only Adminscope oder
öffentlicher Admin-OAuthvertrag in diesen Quellen nachgewiesen.

| Endpoint unter `https://api.mistral.ai/v1/admin` | Was messbar ist | Kein Ersatz für |
| --- | --- | --- |
| `GET /usage?month=&year=&workspace_id=&api_zone=` | Kalender-Monatsusage, Preise/Währung, Modell-/Produktkategorien | Aktuelle Prepaidbalance oder enthaltene Planallowance |
| `POST /usage/query` (Leseabfrage) | Zeitbereich `start` inkl./`end` exkl., Gruppierungen/Filter, Paging `limit/offset/has_more`, Mengen und optional distinct Calls | Berechnete Kosten: Referenz sagt ausdrücklich keine Preise/Währung/Kosten |
| `GET /spend-limit` | Orgcap und gegen ihn gezählte Usage, Währung; detaillierte nullable Felder aus 24.10.-Schemaqualifikation | Prepaidcredits oder verbleibendes Abobudget |
| `GET /rate-limit` | RPS und pro Modell TPM/Token pro Monat | Verbrauch, Remaining oder Resetcountdown |

**Headerwiderspruch heute weiterhin vorhanden:** Authguide und Usageguide verlangen
`x-api-key: <AdminKey>` [M5/M6], gerenderte Billingreferenz zeigt weiterhin
`Authorization: Bearer YOUR_APIKEY_HERE` [M7]. Vor Auslieferung exakten Vertrag
mit Anbieter/aktuellem OpenAPI klären, danach nur bereits autorisierte Readprobe.
Nicht beide Header versuchsweise senden. Org-/Workspacebindung, null vs 0,
Reportverzug und exakte Resetzeitzone bleiben echte Datenqualifikationsaufgaben.
`vibe_usage` heißt in Referenz „Legacy … Always 0“, Beispiel zeigt trotzdem 14;
nicht zur modernen Restberechnung verwenden. Analyticsguide enthält weiterhin
TODO zum tatsächlichen Host der `/analytics/*`-Routen [M6].

Enterprise-Monitor muss eigenständig ohne Inferenzkey funktionieren können.
Admin-Keymanagement kann mit bereits vorhandenem Admin-Key Inferenzkeys
erzeugen, löst dessen Bootstrap aber nicht. Breite Adminrechte nur zum
Copy/Paste-Vermeiden sind kein vernünftiger Free-/Pro-Standardweg. Für normale
Plan-/Prepaidrestwerte bleiben Dashboard und gezielte Herstellerklärung;
Enterprise-Readfähigkeit nicht als universelle Lösung ausgeben.

## Nebius Token Factory: Serverless und Dedicated getrennt

**Angebote und Registrierung.** Aktuelle Billingdoku [N1] sagt wörtlich:
„Billing setup is mandatory—you cannot complete onboarding without it“ und
„requires a bank card“. „Upon first sign-up … $1 in trial credit, valid for
30 days.“ Dies ist ein **am 25.10. neu gelesener Angebotsbeleg**, kein SDK-
Sourceartifact, kein Kontoread, keine Beobachtung des tatsächlichen individuellen
Start-/Ablaufdatums. Kein generelles wiederkehrendes Freebudget. PAYG kann
automatische Kartenbelastung bei Billingthreshold bzw. negativem Monatsanfang
auslösen. Optionaler Buildercredit aus dem bestehenden Katalog ist ein eigener
Angebots-/Eligibilitypfad; sein Betrag/Ablauf wurde hier nicht neu qualifiziert
und darf nicht zum Trialrest addiert werden.

**Einfachster Connect:** bestehender Projektkey von
`https://tokenfactory.nebius.com/project/api-keys`, einmal übernehmen. Kein
Pflichtformular für Org-/Projekt-ID/Region/Trial-PAYG, solange nur Serverless-
Inferenz und passive Header gewünscht sind. [N2] dokumentiert
`Authorization: Bearer <TokenFactoryKey>` an
`https://api.tokenfactory.nebius.com/v1`, Key nur bei Erstellung sichtbar.
Kein nachgewiesener persönlicher OAuth-Keybootstrap für diesen Minimalweg.

**Raten:** aktuelle offizielle Header [N3] zeigen für normale APIantworten
`x-ratelimit-limit-{requests,tokens}`, `remaining-{requests,tokens}`,
`reset-{requests,tokens}` (relative Sekunden), dynamische Skalierungsfaktoren,
`dynamic-period-remaining`, `dynamic-period-usage-*`, `over-limit` und `Retry-After`.
Minute-Ratenkapazität wird kontinuierlich nachgefüllt; das 15-Minuten-Fenster
ist die Skalierungsbewertung, kein finanzieller Reset. Beispiele 60 RPM/400k TPM
sind keine zugesagte individuelle Quote. Vor beobachteter Antwort gibt es keinen
passiven Messwert; Refresh darf keinen Testprompt senden. Serverlessbalance und
Usage außerhalb lains werden dadurch nicht erschlossen.

Billingdoku [N1] verweist für orgweite Ausgaben auf die Console-Usage mit
Projekt-/Service-/Region-/Produktfiltern, nennt dort keinen öffentlichen
Serverless-Balanceendpoint. Auch heute gelesener offizieller Index [N4] liefert
keinen solchen Vertrag. **Nicht gefunden / nicht nachgewiesen**, nicht
„Nebius besitzt keine Billing-API“. Cloud-Billingexport oder Dedicatedmetrics
nicht ungeprüft auf dieses Token-Factory-Serverlessprodukt übertragen.

**Dedicated ist unabhängig lesbar:** [N5] nennt ausdrücklich „only accessible
for Dedicated Endpoints inference“, Projektrollen Org-/Project Admin oder
Project Member; Org Billing Manager hat keinen Observabilityzugriff.
Near-real-time normalerweise zehnsekündiger Bereich, aggregierte Operational-
Metriken, ausdrücklich keine Billingreconciliation. [N6] belegt:
`GET https://read.monitoring.api.nebius.cloud/projects/<project_ID>/service-provider/prometheus/federate?match[]=<selector>`
mit Bearer-Static-Key aus Token Factory. Querybase für Grafana/PromQL:
`https://read.monitoring.api.nebius.cloud/projects/<project_ID>/service-provider/prometheus`.
Projekt-ID erst für diese Capability nötig. Konkrete Serien/Units/Abdeckung
vor einem lain-Reader qualifizieren; Raten/Requests/Tokenhistory sind kein
Restguthaben. Kein Kauf oder Endpointdeployment als Monitorconnectvoraussetzung.

**OAuth-/CLIoption richtig einordnen:** Nebius Cloud bietet PKCE/Föderations-
Primitiven, aber das erneut gelesene offizielle `StaticKeySpec`-Artefakt [N7]
sagt explizit „only service accounts are supported for now“ und nennt `AI_STUDIO`.
Der fest gepinnte Sourcebeleg ist kein Beweis einer heute allgemein verfügbaren
Personenkeyissue. Rollen/aitenant/Projektbindung und Token-Factory-Kompatibilität
dieses Bootstraps bleiben offen. Kein neuen Serviceaccount/Cloud-IAM-Token als
vermuteten Ersatz für einen bestehenden persönlichen Inferenzkey verlangen.

## Inception Mercury: Key, Trial und lokale Tokens

Die heute erfolgreich gelesenen HTMLdocs [I1–I5] dokumentieren APIkeyauth und
offizielle Python-/TypeScript-SDKs `inceptionai`. Ordinary Auth:
`Authorization: Bearer <InceptionKey>`, Base `https://api.inceptionlabs.ai/v1`.
Minimal: vorhandenen Key behalten oder auf
`https://platform.inceptionlabs.ai/dashboard/api-keys` erzeugen und einmal
übernehmen. Weder Org/Projekt noch Regionsauswahl sind für diesen Vertrag
erforderlich. Kein zusätzlicher Pro-/Enterpriseplan für normale Keynutzung belegt.

**Free/Trial:** 100 Mio. initiale Tokens pro Neukonto ohne Zahlungsdetails;
FAQ sagt einmalig, über alle Modelle geteilt. Keine periodische Neubefüllung
oder individuelle Expiry in diesen Seiten angegeben. PAYG durch gesondertes
Hinterlegen der Zahlungsinformation in Billing. Kein Subscriptionbudgetvertrag
in diesem geprüften Umfang nachgewiesen.

**Neuer konkreter Katalogbefund:** [I3] nennt veröffentlichte Rate-Limits:
Free 1.000 Requests / 1 Mio. Inputtokens / 100.000 Outputtokens je Minute;
PAYG 3.000 / 3 Mio. / 300.000; Enterprise jeweils custom. Der aktuelle lain-
Katalog führt Trial-/PAYG-Raten noch als `null`. Das sind **veröffentlichte
Tarifobergrenzen**, keine live beobachteten accountbezogenen Remainingwerte.
Nicht über fehlenden Kontorest auf „keine veröffentlichten Limits“ schließen.
Account-Ausnahmen, konkurrierende Clients, Reset- und Headervertrag sind damit
nicht qualifiziert. Enterprise für höhere Customlimits ist keine allgemeine
Monitorgate.

FAQ [I4] beschreibt `usage.prompt_tokens`, `.completion_tokens`, `.total_tokens`;
Cache ist in Prompttokens, Reasoning in Completiontokens bereits enthalten.
Unterfelder nicht doppelt addieren. Bei Streaming `stream_options.include_usage:true`
für abschließenden Usagechunk. Diese normalen Antworten unterstützen lokale
Requestusage und Preisabschätzung, weder Accountbalance noch Trialrest.

Im geprüften Auth-/SDK-/Quickstart-/FAQ-/Ratelimitumfang kein dokumentierter
öffentlicher Kontorest-, Billing-, delegierter OAuth- oder CLI-Keybootstrapvertrag.
Das bleibt **nicht nachgewiesen**, keine Anbieter-Unmöglichkeitsaussage.
Ein Keybootstrap würde ohne solche Reportingquelle die Usagekachel nicht lösen.
Gezielter nächster Maintainerbeleg: Anbieter nach unterstütztem Account-
Credit/Trialread mit Scope/Unit/Expiry und möglichem delegierten Login fragen;
bis dahin Portal + klar bezeichnete lokale Usage. Keine privaten Sessions lesen.

## Quellenregister: heute gelesen, mit prüfbaren Textstellen

Alle folgenden Abrufe **25.10.2026**. GitHub-`main`-Metadaten wurden öffentlich
gelesen; gelesene Quellinhalte sind auf die festen Commits gepinnt. Keine Inhalte
aus Accountantworten. `get-started.md` und `/llms.txt` von Inception lieferten
zunächst 403; die kanonischen HTMLseiten funktionierten mit normalem User-Agent.
403 ist keine Aussage über einen fehlenden Produktvertrag.

| ID | Offizielle Quelle | Heute gelesener Ausschnitt / Befund |
| --- | --- | --- |
| A1 | https://www.alibabacloud.com/help/en/model-studio/new-free-quota | „Only models in the Singapore region … International“; „same API key“; 90 Tage; getrennt „OAuth … 2,000 calls per day“. |
| A2 | https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/auth/login-console.ts | `CONSOLE_LOGIN_TIMEOUT_MS = 15 * 60 * 1000`, `needapikey=true`, Callback mit Consoletoken und optionalem Key/Scope. |
| A3 | https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/console/gateway.ts | `REGION_GATEWAYS`, `Authorization = Bearer`, `/cli/api.json`, Beijingfallback für unbekannte Regionen. |
| A4 | https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/shared.ts | `quotaInitTotal/quotaTotal/quotaValidityPeriod/quotaStatus`, `FREE_TIER_API`, `OVERVIEW_API`. |
| A5 | https://www.alibabacloud.com/help/en/model-studio/application-permission-management-overview | „Only an Alibaba Cloud account can grant OpenAPI permissions“; `ModelObservation-FullAccess`; einmalige Monitoringaktivierung. |
| A6 | https://www.alibabacloud.com/help/en/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance | `AvailableAmount`, `AvailableCashAmount`, `CreditAmount`, `Currency`; `bss:DescribeAcccount`. |
| A7 | https://www.alibabacloud.com/help/en/user-center/developer-reference/api-calling-authorization | `AliyunBSSReadOnlyAccess`, `bssapi:<APIName>`, Resource `*`, Produktbedingungen. |
| A8 | https://www.alibabacloud.com/help/en/model-studio/coding-plan | Pro / Lite-Historie, Modellcallfenster; „Do not use … automated scripts, application backends, or other non-interactive scenarios.“ |
| A9 | https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/coding-plan.ts | `queryCodingPlanInstanceInfoV2`; `onlyLatestOne:true`; erste `VALID`-Instanz; drei `QuotaNextRefreshTime`-Felder. |
| A10 | https://api.github.com/repos/modelstudioai/cli/commits/main | Aktueller main-SHA `3c8031f4…`, Commitdatum `2026-10-23T08:56:19Z`, Release-Merge 2.5.1. |
| M1 | https://docs.mistral.ai/getting-started/quickstarts/studio/activate-and-generate-api-key.md | „Free mode: API access is enabled by default with no credit card required.“ |
| M2 | https://docs.mistral.ai/admin/billing-usage/subscriptions.md | „plans are global“; „shared across Studio, the API, and Vibe Code“; PAYG prepaid, kein separater Plan. |
| M3 | https://docs.mistral.ai/vibe/code/cli/api-keys-profiles.md | „Browser-based sign-in is enabled by default“; Keyring/`.env`; derselbe Key Free/Paid/PAYG, PAYG off by default. |
| M4 | https://github.com/mistralai/mistral-vibe/blob/63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477/vibe/setup/auth/http_browser_sign_in_gateway.py | `/vibe/sign-in`, S256, Pollstatus, Exchange → `api_key`, konkreter Workspacefehler. |
| M5 | https://docs.mistral.ai/admin/admin-api/authentication.md | „Enterprise plans only … isn't self-serve“; „standard API key never“; eine Org, Enterpriseadmin, `x-api-key`. |
| M6 | https://docs.mistral.ai/admin/admin-api/usage-metrics.md | Billing-/Ratenreads, `x-api-key`; weiterhin TODO für Analyticsbasehost. |
| M7 | https://docs.mistral.ai/api/endpoint/beta/admin/billing | Usagequery „quantities only“; Kalender-Monat; Beispiele `Authorization: Bearer`; Legacy-Vibefeldwiderspruch. |
| M8 | https://api.github.com/repos/mistralai/mistral-vibe/commits/main | Aktueller main-SHA `63d0e8f1…`, Commitdatum `2026-10-22T20:42:29Z`. |
| N1 | https://docs.tokenfactory.nebius.com/other-capabilities/billing-new.md | Mandatory Billing + bank card; „$1 … valid for 30 days“; Org-Consoleusage. |
| N2 | https://docs.tokenfactory.nebius.com/api-reference/introduction.md | Bearer-APIkey; Token-Factoryhost; einmalige Keyanzeige. |
| N3 | https://docs.tokenfactory.nebius.com/ai-models-inference/rate-limits.md | Minute-RPM/TPM, kontinuierliches Replenishment, relative Sekunden, separate 15-Minuten-Skalierung. |
| N4 | https://docs.tokenfactory.nebius.com/llms.txt | Heutiger öffentlicher Produktindex; Serverless, Dedicated, Billing, Modelle und Auth getrennt. |
| N5 | https://docs.tokenfactory.nebius.com/ai-models-inference/observability.md | „only accessible for Dedicated Endpoints inference“; Rollen; „not billing reconciliation“. |
| N6 | https://docs.tokenfactory.nebius.com/ai-models-inference/observability-api-integrations.md | `read.monitoring.api.nebius.cloud`, Projektpfad, Bearer-Static-Key aus Token Factory. |
| N7 | https://github.com/nebius/api/blob/e855cccf88420a80741b04075674528d75f3e238/nebius/iam/v1/static_key.proto | `only service accounts are supported for now`; `ClientService.AI_STUDIO=3`. Gepinntes Artefakt erneut gelesen, nicht als aktueller main-SHA behauptet. |
| I1 | https://docs.inceptionlabs.ai/get-started/get-started | „100 million free tokens — no payment details required“; Billing gesondert; Mercury-3-APIbase. |
| I2 | https://docs.inceptionlabs.ai/get-started/authentication | „All API requests require … API keys“; `Authorization: Bearer`. |
| I3 | https://docs.inceptionlabs.ai/get-started/rate-limits | Free/PAYG/Enterprise-Tabelle mit getrennten Request-, Input-, Output-Minutenlimits. |
| I4 | https://docs.inceptionlabs.ai/resources/faq | Einmaliger kontoweiter Trial, Unterfelder bereits enthalten, Streamingusage-Option. |
| I5 | https://docs.inceptionlabs.ai/get-started/sdks | Offizielle Python-/TS-Clients `inceptionai`, `INCEPTION_API_KEY`; kein dort dokumentierter Login-/Creditreader. |

Ergänzende Details ausdrücklich **aus der Forschung vom 24.10. wiederverwendet**,
nicht heute erneut als Source abgerufen: Alibaba `BAILIAN_CONFIG_DIR`,
AK/SK-/STS-Refresh über `GenerateCLIAccessToken` ohne gewöhnliches Refreshgrant,
API-Limitoperation und BSS-Billpagination; Mistral detaillierte nullable
Spendfelder und `whoami`; Nebius Cloud-Federation-PKCE. Verweise und damalige
Abrufbelege stehen in `docs/quota-auth-research/alibaba-nebius-inception.md`,
`docs/quota-auth-research/groq-mistral.md` und den entsprechenden
`docs/account-connect-research/`-Berichten. Diese Details sind keine heutige
Livequalifikation. Der konkrete nächste Schritt ist jeweils Implementierung /
isolierte Releasequalifikation oder eine gezielte Vertragsklärung; pauschal
„noch einmal anmelden“, „Pro kaufen“ oder „neuen Key erstellen“ löst die
unterschiedlichen Lücken nicht.
