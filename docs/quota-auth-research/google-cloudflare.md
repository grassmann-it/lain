# Gemini Developer API und Cloudflare Workers AI: Auth- und Quota-Recherche

Stand: **24.10.2026**, Bezugsstand lain `5dc916f`. Recherche und Architekturvorschlag;
keine neue akzeptierte Produktentscheidung. Gelesen wurden `AGENTS.md`, der
Architecture-Sparring-Skill, `docs/providers/quota-sources.md` und
`docs/providers/monitor-auth.md`. Keine Sparring-Konfiguration war vorhanden.
Alle unten als Belege verwendeten Webseiten bzw. öffentlichen Quelldateien wurden
geöffnet. Es wurden keine privaten Zugangsdaten, Kontositzungen, Providerabfragen
mit Kontodaten, Logins, Registrierungen, Käufe oder Provisionierungen verwendet.
Kein Produktcode und keine Installation wurden geändert; keine Builds ausgeführt.

## Ergebnis und Priorität

| Produkt | Tragfähiger Weg | Verbleibende Grenze |
| --- | --- | --- |
| Gemini Developer API | Eigener Desktop-OAuth-Client; Cloud Monitoring **mit den modellbezogenen `generativelanguage.googleapis.com/quota/...`-Metriken**, dazu Cloud Quotas für effektive Limits und Fenster. Direkte Gemini-OAuth-Inferenz ist offiziell beschrieben. | Eigener Client/Refresh fehlen. Ein eng begrenzter Projekt-Read muss die tatsächlich exportierten Serien und die genaue Verbindung `limit_name` ↔ `quotaId` bestätigen. Verzögerte Telemetrie ist keine Echtzeit-Freigabe für die nächste Anfrage. |
| Cloudflare Workers AI | Eigener öffentlicher OAuth-PKCE-Client und explizites Account-Consent sind offiziell möglich. `aiInferenceAdaptiveGroups.sum.totalNeurons` ist ein konkret belegter Schema-Kandidat, zusätzlich existiert ein offizieller Billing-Usage-v2-Read. | Für das Neuron-Feld fehlt noch die aktuelle offizielle Introspektion samt Account-Freigabe, Sampling und Abrechnungsabdeckung. OAuth-Scopes müssen aus Cloudflares Scope-Katalog stammen; Loopback- und Refresh-Details sind noch zu qualifizieren. |

**Empfehlung:** Gemini zuerst zu einem automatisch erneuerten Projektmonitor
ausbauen und direkte OAuth-Inferenz mit demselben ausgewählten Projekt qualifizieren.
Cloudflare parallel als kleine Quellenqualifikation behandeln. „Keine API“ oder
„OAuth unmöglich“ wären bei beiden Anbietern falsche Schlussfolgerungen.

## Gemini Developer API

### Iststand und neu belegte Datenquelle

lain hat einen separaten, ablaufenden Google-Access-Token-Snapshot und eine explizite
Projekt-ID. `GeminiMonitoringQuotaAdapter` liest bisher nur
`serviceruntime.googleapis.com/api/request_count` für den UTC-Tag. Das ist
projektweite API-Nutzung ohne Modellnenner, kein Restbudget. Eigener OAuth-Login
und Refresh fehlen. Dieser Iststand bleibt korrekt, ist aber keine Grenze des
Anbieters.

Die aktuelle offizielle [Metrikliste D–H][G1] enthält **direkte Gemini-Quota-Metriken**.
Unter dem Präfix `generativelanguage.googleapis.com/` sind unter anderem folgende
Familien mit `/usage`, `/limit` und `/exceeded` beschrieben:

| Familie unter `quota/` | Relevanz |
| --- | --- |
| `generate_content_free_tier_requests` | Requests des Free Tiers |
| `generate_content_free_tier_input_token_count` | Input-Token-Verbrauch des Free Tiers |
| `generate_requests_per_model` | Generate-Requests des Paid Tiers |
| `generate_content_paid_tier_input_token_count` | Input-Token-Verbrauch des Paid Tiers |
| `generate_content_paid_tier_2_requests`, `generate_content_paid_tier_3_requests` | Weitere Paid-Tier-Familien, derzeit ALPHA |
| `generate_content_paid_tier_2_input_token_count`, `generate_content_paid_tier_3_input_token_count` | Weitere Input-Token-Familien, derzeit ALPHA |

`usage` ist bei diesen Request-/Token-Familien **DELTA / INT64 / Einheit 1**,
`limit` **GAUGE / INT64 / Einheit 1**. Die Labels enthalten `limit_name` und
`model`; Usage zusätzlich `method`. Limits werden alle 60 Sekunden erfasst;
die dokumentierte Sichtbarkeitsverzögerung beträgt bis zu 150 Sekunden.
Der [Resource-Typ][G2] ist `generativelanguage.googleapis.com/Location` mit
`resource_container` und `location`. Diese Labels unterscheiden sich vom
bisherigen `consumed_api.project_id`-Reader.

Das ist ein offiziell dokumentierter Modellbezug. Die exportierten `model`-Werte
sind trotzdem nicht ungeprüft gleichzusetzen mit beliebigen Aliasnamen aus einer
lokalen Modellliste. Insbesondere sind `*_internal`-Metriken in derselben Liste
als interne Accounting-/1P-Familien beschrieben; diese sind kein Ersatz für die
normalen Developer-API-Quoten. Separate Batch-, Embedding-, Predict- und
Grounding-Familien dürfen ebenfalls nicht in ein einziges Budget summiert werden.

Die Daten werden unmittelbar bei Google gelesen. Dafür muss keine Inferenz durch
lain gelaufen sein; vorhandener projektweiter Verbrauch anderer Clients kann
mit erfasst werden. Ein bisher ungenutztes Projekt kann trotzdem keine Zeitreihe
liefern. Das ist von der Fähigkeit zum Monitoring unabhängig vom Proxy zu trennen.

### Effektive Limits und korrekte Zuordnung

Die [AI-Studio-IAM-Tabelle][G3] verknüpft das Usage-Dashboard ausdrücklich mit
`monitoring.timeSeries.list` und das Rate-Limit-Dashboard zusätzlich mit
`cloudquotas.quotas.get`. Das belegt die Plattformkette wesentlich stärker als
ein Drittanbieter, der ein Dashboard nachbaut.

Der offizielle [Cloud-Quotas-Read][G4] lautet:

```text
GET https://cloudquotas.googleapis.com/v1/projects/{PROJECT_NUMBER}/locations/global/services/generativelanguage.googleapis.com/quotaInfos
```

Er erfordert OAuth `https://www.googleapis.com/auth/cloud-platform` und IAM
`cloudquotas.quotas.get`. [`QuotaInfo`][G5] liefert `metric`, `quotaId`, `dimensions`,
`refreshInterval`, `isPrecise`, `isConcurrent` und `dimensionsInfos`.
[`DimensionsInfo.details.value`][G6] bezeichnet den aktuell durchgesetzten Wert;
spezifischere Dimensionskombinationen haben Vorrang vor allgemeinen Einträgen.
Die Ressource liegt unter `locations/global`, auch wenn ihre Quota-Dimensionen
regionale Werte enthalten. Der Endpoint liefert Konfiguration, keinen Verbrauch.

Vorgeschlagener Join nach erfolgreicher Quellenprobe:

1. Projekt-ID und Projektnummer auf dasselbe ausgewählte Projekt auflösen.
2. Eine `QuotaInfo.metric` der korrespondierenden Monitoring-Familie zuordnen;
   `quotaId` und den gelieferten `limit_name` vergleichen, nicht bloß Anzeigenamen.
3. `model`, `location` und sämtliche weiteren Quota-Dimensionen erhalten. Die
   genau passende `dimensionsInfo` wählen. Fehlende Modellzuordnung bleibt offen.
4. DELTA-Usage innerhalb des **bestätigten** Fensters summieren. Nur disjunkte
   `method`-Serien derselben Quota dürfen zusammengeführt werden; weder mehrere
   Limits noch Tierfamilien addieren. GAUGE-Limits werden nicht summiert.
5. Bei einem Tagfenster Pacific-Mitternacht mit `America/Los_Angeles` und DST
   verwenden. [Gemini RPD][G7] setzt dort zurück, nicht um UTC-Mitternacht.
   `refreshInterval=minute` allein beweist keinen UTC-kalendergebundenen Reset.

Das ist ein Umsetzungsentwurf, keine bereits nachgewiesene Join-Gleichheit im
Nutzerprojekt. Die öffentliche Metrikliste zeigt beispielsweise
`generate_requests_per_model_per_day_internal/usage`, aber keine gewöhnliche
`generate_requests_per_model_per_day/{usage,limit}`-Familie. Ein Tageslimit darf
daher nicht durch Erfinden dieses Metrikpfads ergänzt werden. Die tatsächlichen
`limit_name`-Ausprägungen der vorhandenen Familien und Cloud-Quotas-Antwort lösen
diese Frage in einer begrenzten Probe.

Für vollständig abgedeckte Tagesintervalle kann lain ein **gemeldetes Restbudget
zum Datenstand** ableiten. Die jüngsten bis zu 150 Sekunden fehlen möglicherweise;
für RPM/TPM ist das länger als ein ganzes Fenster. Solche Werte eignen sich als
Limit-/Nutzungsanzeige, nicht als sichere Echtzeit-Routingentscheidung. Auch
spendbasierte, Priority- und andere zusätzliche Limits können gelten [G7]. Ein
positiver Restwert beweist daher keine verfügbare Inferenzkapazität.

Als Ausweichquelle bietet die offizielle [Service-Usage-API v1beta1][G8]
`services.consumerQuotaMetrics.list` mit `view=FULL` effektive/default Limits,
Quota-Units und Overrides. Sie braucht `serviceusage.quotas.get`; sie ist ein
Konfigurationsfallback, kein Nutzungszähler. Cloud Quotas ist für den ersten
Versuch vorzuziehen, weil die aktuelle AI-Studio-Dokumentation diesen Pfad benennt.

### OAuth, Inferenz und Bootstrap

Google unterstützt [Desktop-OAuth][G9] mit eigenem registriertem Desktop-Client,
Systembrowser, S256-PKCE, State, Loopback-Callback und erneuerbaren Tokens. Eine
Client-ID darf verteilt werden; der Desktop ist ein Public Client und kann kein
Geheimnis schützen. Client-Provisionierung, Consent-Screen und Produktion sind
separate Arbeit. Google dokumentiert fehlende inkrementelle Autorisierung für
Installed Apps: der anfängliche Scope-Satz und spätere Reconnect müssen darauf
ausgelegt sein. Refresh-Ausfälle und Widerruf bleiben normale Zustände. Externe
Testing-Projekte mit diesen Scopes können Refresh-Tokens nach sieben Tagen
verlieren [G10]. Öffentliche Verteilung benötigt die einschlägige
[Produktions-/Scope-Prüfung][G11]; technischer Flow ist keine bereits erteilte
Freigabe für lain.

| Variante | Rechte und praktische Bewertung |
| --- | --- |
| API-Key-Inferenz plus eigener Monitor-OAuth | Schonendster Ausbau des heutigen Accounts. `monitoring.read` reicht für Zeitreihen [G12]; zusätzliche Cloud-Quota-Abfrage verlangt `cloud-platform`. Projekt-IAM bleibt unabhängig notwendig. |
| Ein eigener OAuth-Zugang für Inferenz und Monitor | Bevorzugtes Ziel für „anmelden, lain übernimmt“. [Geminis eigener OAuth-Quickstart][G13] beschreibt OAuth statt API-Key, Desktop-Client, Refresh und `Authorization: Bearer` mit `x-goog-user-project`. Das Beispiel verwendet `cloud-platform` plus `generative-language.retriever`; die minimal nötigen Scopes und IAM für die konkret verwendeten Inferenzmethoden sind gesondert zu bestätigen. Nicht unbesehen den Retriever-Scope als kleinsten `generateContent`-Scope ausgeben. |
| OAuth erzeugt danach einen Inferenzschlüssel | Technisch größerer Bootstrap. [API Keys create][G14] benötigt `apikeys.keys.create` und `cloud-platform`; das allein bildet heutige AI-Studio-Schlüssel nicht vollständig ab. |
| User-eigener Desktop-Client | Mögliche private Vorstufe ohne zentrale lain-Veröffentlichung. Mehr Nutzeraufwand; kein Ersatz für einen produktionsfähigen gemeinsamen Client. |

`cloud-platform` ist ein breiter OAuth-Scope, kein Read-only-Versprechen. Die
fachliche Trennung von Inferenz, Monitoring und Ressourcenverwaltung muss auch
bei einem gemeinsamen Grant erhalten bleiben; Projekt-IAM und tatsächlich
gewährte Scopes bestimmen den Zugriff. Ein erfolgreicher Google-Login bestimmt
weder das Inferenzprojekt noch dessen Billing-Status.

Der Projektpicker kann offiziell [`projects.search`][G15] nutzen; sichtbar sind
Projekte mit `resourcemanager.projects.get`. Die Auswahl und beide Projektkennungen
werden dauerhaft an den lain-Account gebunden. API-Aktivierung ist eine gesonderte
[Service-Usage-Schreiboperation][G16] mit `serviceusage.services.enable`. Das
Minimalziel verwendet ein bestehendes aktiviertes Projekt; es benötigt keine
automatische Billing-Verknüpfung oder einen Kauf.

**Wichtige Bootstrap-Änderung:** [Geminis aktuelle Key-Dokumentation][G17] beschreibt
seit 28.05.2026 neu erstellte AI-Studio-Keys als an Service Accounts gebundene
Authorization Keys. Unrestricted Standard Keys werden abgewiesen, explizit
eingeschränkte Standard Keys bleiben eine eigene Variante. AI-Studio-Erstellung
nennt zusätzlich `iam.serviceAccounts.create` und
`iam.serviceAccountApiKeyBindings.create`. „Login → einfacher Key-create-Call →
fertig“ wäre deshalb unvollständig. Direkter OAuth-Zugriff vermeidet diesen
Provisionierungsteil, muss aber im Inferenztransport qualifiziert werden.
Dies betrifft die Gemini **Developer API**, nicht Gemini-App-Abos oder
Gemini-Code-Assist-/CLI-Kontingente.

### Fehlende Umsetzung und begrenzte nächste Probe

Fehlend sind ein eigener OAuth-Client/Token-Lifecycle, Projektpicker und
ID-Auflösung, der neue Quota-Metrikreader samt Join/Fenstern, sowie bei der
Ein-Login-Variante ein Bearer-Inferenztransport mit explizitem Quota-Projekt.
Der bestehende Access-Snapshot bleibt ein Fallback; er erhält keine nachträglich
unterstellte Refresh- oder Widerrufsbefugnis.

Die nächste qualifizierende Probe braucht nur ein bereits genutztes,
ausdrücklich ausgewähltes Projekt: QuotaInfos, vorhandene MetricDescriptors und
genau passende Zeitreihen lesen; einen abgeschlossenen Pacific-Tag sowie wenige
Minuten auswählen; Rückgabedimensionen, `limit_name`, Tierfamilien und Zeitpunkte
mit AI Studio vergleichen. Ein TimeSeries-Read verlangt einen konkreten
Metriktyp, keine erfundene Sammelabfrage. Keine zusätzliche Inferenz zum Erzeugen
von Daten ist erforderlich. Leer bedeutet unbekannt, nicht null; ein expliziter
Nullpunkt ist null. Tagesrest darf nur bei hinreichender Intervallabdeckung
erscheinen. Das offene Ergebnis lautet **live unqualifiziert**, nicht unmöglich.

## Cloudflare Workers AI

### Iststand und Quellenkette für Neurons

lain kann einen bestehenden Inferenz-Token für Monitoring auswählen oder einen
separaten Token mit derselben Account-ID aufnehmen. Ein Neuronreader ist nicht
registriert. Die bisherige Quellengrenze war korrekt vorsichtig, kann jetzt aber
auf konkrete Kandidaten eingegrenzt werden.

1. Cloudflares [Pricing][C1] dokumentiert Neurons als Abrechnungseinheit,
   10.000 kostenlose Neurons pro UTC-Tag und die Dashboard-Anzeige. Workers Paid
   und AI-Gateway-Unified-Billing sind getrennte Abrechnungswege.
2. Ein **historischer offizieller** [Cloudflare-Skill-Commit][C2] enthält
   `aiInferenceAdaptiveGroups` ausdrücklich als Workers-AI-Analyse mit
   `modelId`, Zeitdimension und Input-/Output-Tokens. Die Datei wurde im
   [Commit vom 14.10.2026][C3] wegen duplizierter/gedrifteter Referenzen entfernt.
   Ein Suchtreffer auf `main` ist damit kein aktueller offizieller Vertrag.
3. Das tatsächlich geöffnete [öffentliche Schema-Abbild][C4] enthält
   `AccountAiInferenceAdaptiveGroupsSum.totalNeurons: float64!`, dessen
   Confidence-Feld, sowie Dimensionen `modelId`, `requestSource`, `date`,
   `datetime*` und `neurons`. Es ist **kein von Cloudflare herausgegebener
   aktueller API-Vertrag**. `cfdata.lol` lieferte ebenfalls passende Suchtreffer,
   war als Vollseite durch Größenlimit/403 nicht lesbar; es dient nicht als
   tragender Vollbeleg.
4. Das geöffnete Drittanbieterprogramm [flareclerk][C5] verwendet exakt
   `aiInferenceAdaptiveGroups` mit `sum.totalNeurons`, gruppiert nach Modell und
   Tag. Sein [Auth-Code][C6] fordert Workers AI Read statt Account Analytics Read.
   Das ist ein nützlicher Hinweis auf möglicherweise datasetbezogene Rechte,
   **kein Beweis des kleinsten offiziellen Scopes**. Seine Null-Fallbacks und
   geschätzte Kostenrechnung werden nicht übernommen.
5. Die maßgebliche aktuelle Plattformkette ist Cloudflares offizielle
   [GraphQL-Introspektion][C7] plus accountbezogener Settings-/Verfügbarkeitsread.
   Ein Schemafeld kann existieren, obwohl es für einen bestimmten Account nicht
   abfragbar ist. Diese letzte begrenzte Probe wurde hier nicht ausgeführt.

`totalNeurons` wäre **echte vom Anbieter erfasste Neuron-Telemetrie**, keine
Umrechnung aus lokalen Tokenzahlen. Das Dataset hat jedoch `Adaptive` im Namen:
Cloudflare beschreibt solche [Analytics als adaptive Stichproben][C8], deren
Aggregate bei großem Volumen Schätzwerte sind. Daher zunächst
„gemeldete/geschätzte Neuron-Nutzung“ mit Confidence, nicht ein garantiert exakter
Abrechnungszähler. Es fehlt eine Workers-AI-spezifisch belegte Freshness-SLA.
Auch dieser Kandidat liest Cloudflare direkt und setzt keinen bisherigen
Inferenzverkehr durch lain voraus.

Für eine spätere Kandidatenabfrage: genau ein Account, UTC-Tagesbeginn bis zum
Abfragezeitpunkt, `sum.totalNeurons`; eine zweite Abfrage kann nach Modell und
Zeit gruppieren. Verfügbare Filter/Typen zuvor introspektieren. Die Gruppenmenge
muss vollständig sein; ein beliebiges `limit: 100` darf nicht heimlich den
Tagesverbrauch abschneiden. Ohne Gruppen braucht es keine summierte Top-N-Liste.
Keine zusätzlichen `tag`-Dimensionen, die Mehrfachzählungen verursachen könnten.

Ein daraus abgeleitetes `max(0, 10000 − usage)` darf erst nach Bestätigung der
Abrechnungsabdeckung als **geschätzter verbleibender Freianteil** erscheinen.
Es ist auf Workers Paid kein gesamtes Ausgabelimit. Gateway-Credits und Modelle
mit eigener Paid-Voraussetzung bleiben eigenständig [C1]. Accountweite Nutzung
ist nicht nur die Nutzung dieses einen lokalen API-Tokens.

### Zweiter offizieller Kandidat: Billing Usage v2

Die aktuelle [Billing-API v2][C9] dokumentiert
`GET /accounts/{account_id}/billable/usage` als **Alpha, Restricted**. Sie liefert
pro Tag und Billable Metric auch metered Nutzung innerhalb kostenloser Kontingente.
Relevante Felder sind `ConsumedQuantity`, `ConsumedUnit`, `ChargePeriodStart/End`,
`x_BillableMetricId` und `x_ProductFamilyId/Name`; die Zeitspanne ist auf 31 Tage
begrenzt. Der aktuelle Methodenname ist `get_account_usage_v2`; der alte
`get`-Dokulink ist als veraltet markiert.

Das ist ein besserer Kandidat zur Abrechnungsabstimmung als ein lokal errechneter
Preis. Noch nicht belegt sind die konkrete Workers-AI-Metric-ID, Neuron-Einheit,
Account-Zugänglichkeit, Freshness und kleinste Berechtigung. Diese Felder dürfen
nicht als universell verfügbare tägliche Neuronquelle vorweggenommen werden.
Die [v1-Billable-Usage-API][C10] hängt zudem vom Billing-Cycle-Ankertag ab;
sie eignet sich nicht als schneller Ersatz für heutiges Restkontingent.

Die geöffneten AI-REST- und Billing-Referenzen lieferten keinen schon ausreichend
qualifizierten universellen Remaining-Neurons-Endpunkt. Das ist ein begrenztes
Rechercheergebnis, kein Nachweis seiner Nichtexistenz.

### OAuth, Scopes und Inferenz

Cloudflares [Registrierungsanleitung][C11] nennt Desktop/CLI explizit: Authorization
Code, `token_endpoint_auth_method=none`, **S256-PKCE erforderlich**. Lain braucht
einen eigenen registrierten Client. Für allgemeine Verteilung muss dessen
Sichtbarkeit öffentlich werden; dafür sind Branding, Client-URL und verifizierte
Domain nötig. Private Clients gelten nur für Mitglieder des registrierenden
Accounts. Das öffentliche Sichtbarmachen ist dauerhaft. Diese Einmalregistrierung
wurde nicht vorgenommen.

Die [Client-API][C12] erlaubt `authorization_code` plus optional `refresh_token`.
`offline_access` wird passend zur Grant-Konfiguration ergänzt. Die API verlangt
punktgetrennte Scope-Namen; **Wranglers historische Doppelpunkt-Scopes sind kein
Template für fremde Clients**. OAuth-Scopes müssen aus dem offiziellen
`GET /client/v4/oauth/scopes`-Katalog stammen. Die geöffneten Quellen liefern
noch keine verlässlich zitierbaren konkreten Scope-IDs für Workers-AI-Inferenz
und Analytics. Deshalb stehen hier keine geratenen Scope-Strings.

Die offiziellen [Auth-/Token-/Revoke-Endpunkte][C13] liegen unter
`https://dash.cloudflare.com/oauth2/`. [Consent][C14] lässt den Nutzer Accounts
und optionale Rechte auswählen. Account-Admins können neue öffentliche
OAuth-Autorisierungen sperren. Ein verweigerter optionaler Monitoring-Scope muss
Inferenz und Monitoring in lain getrennt darstellen können.

| Capability | Offizieller Rechtebeleg / Schlussfolgerung |
| --- | --- |
| REST-Inferenz | Der [Workers-AI-Quickstart][C15] nennt Workers AI Read **und** Edit für selbst erstellte Tokens sowie Bearer-Auth und Account-ID. API-Endpunktfreigabe mit OAuth separat qualifizieren; generelle OAuth-Unterstützung allein beweist nicht jeden Datenpfad. |
| GraphQL-Monitoring | [Analytics-Auth][C16] dokumentiert Account → Account Analytics → Read. Bestehender Inferenz-Token kann dafür genügen, wenn er dieses Recht besitzt. Der flareclerk-Hinweis auf Workers AI Read ist eine gezielte Minimalrechte-Probe, keine stillschweigende Absenkung. |
| Billing Usage v2 | API-Token-Auth ist dokumentiert, aber der geöffnete Methodenvertrag benennt keinen hinreichend klaren kleinsten Scope. Alpha-/Restricted-Zugang separat prüfen. |
| Token-Provisionierung | [Cloudflare Tokens via API][C17] benötigt selbst die Fähigkeit, weitere Tokens zu erstellen; das ist kein Bestandteil eines normalen Inferenz-/Analytics-Logins. Direkt nutzbare OAuth-Tokens wären vorzuziehen. |

Eine Ein-Login-Variante ist damit ein realer, priorisierter Weg: eigener PKCE-Client,
ausgewählter Account, erforderliche Inferenzrechte und wahlweise Reportingrechte,
Refresh im lokalen Credential-Dienst. Der Public-Client-Flow braucht keinen
eingebetteten Client-Secret. Noch offen sind die zulässige Registrierung von
Desktop-Loopback-Redirects, Token-Lebensdauer/Rotation und die tatsächliche
Endpunktakzeptanz für diese konkreten Scopes. Die Cloudflare-One-/Access-Dokumentation
über Managed OAuth wäre hierfür das falsche Produkt und wurde nicht übertragen.

### Fehlende Umsetzung und begrenzte nächste Probe

Fehlend sind Clientregistrierung/Verteilung, OAuth-Lifecycle und Accountauswahl,
Scope-Capability-Mapping, GraphQL-Schemaverifikation sowie ein Reader mit
Sampling-/Freshness-Vertrag. Die vorhandene Tokenaufnahme kann als qualifizierbare
Vorstufe bleiben. Keine Notwendigkeit für zusätzliche Tokens, falls der vorhandene
Token die benötigten Rechte bereits hat.

Nächste kleine Probe, erst in einem dafür freigegebenen Konto: die genannten
GraphQL-Typen und Settings introspektieren, einen abgeschlossenen UTC-Tag lesen,
`totalNeurons` nach Quelle/Modell mit dem Workers-AI-Dashboard vergleichen, dann
die Gegenprobe für Unified Billing. Bestehende Nutzung reicht aus. Bei freigegebenem
Billing-v2-Zugang genau dieselben Tage und belegte Workers-AI-Metric-IDs abgleichen.
Für OAuth nach eigener Registrierung die Scopes und Accountbindung, Loopback,
Refresh/Rotation sowie Read-only-Reporting getrennt von Inferenz bestätigen.
Keine inferenzseitige „Testnutzung“ allein für Monitoring erforderlich.

### Websession als Alternative

Cloudflare dokumentiert selbst das [Erfassen von Dashboard-GraphQL-Abfragen][C18].
Das kann eine spätere einmalige Quellenqualifikation unterstützen, falls die
öffentlichen Typen die Abrechnung nicht erklären. Für das Produkt sind reguläres
OAuth bzw. API-Token dauerhafter als ein Cookie-/Dashboard-Scraper. Ein manuell
geöffneter Dashboard-Link bleibt bei fehlender API-Freigabe sinnvoll. Weder eine
Browser-Sitzung noch ein fremder Wrangler-Client werden in dieser Recherche
übernommen. Für Google gilt entsprechend: AI-Studio-UI ist Vergleichs-/Fallbackweg;
die offiziellen Monitoring-/Quota-APIs machen Websession-Scraping nicht erforderlich.

## Quellenverzeichnis

Alle Belege zuletzt tatsächlich geöffnet am **2026-10-24**; einzelne Fehler/
Suchtreffer ohne lesbare Vollquelle sind oben ausdrücklich als solche bezeichnet.
Das JSON enthält dieselbe Quellenbewertung maschinenlesbar.

[G1]: https://docs.cloud.google.com/monitoring/api/metrics_gcp_d_h?hl=en
[G2]: https://docs.cloud.google.com/monitoring/api/resources#tag_generativelanguage.googleapis.com/Location
[G3]: https://ai.google.dev/gemini-api/docs/troubleshoot-ai-studio
[G4]: https://docs.cloud.google.com/docs/quotas/reference/rest/v1/projects.locations.services.quotaInfos/list
[G5]: https://docs.cloud.google.com/docs/quotas/reference/rest/v1/projects.locations.services.quotaInfos
[G6]: https://docs.cloud.google.com/docs/quotas/reference/rest/v1/folders.locations.services.quotaInfos#QuotaInfo.DimensionsInfo
[G7]: https://ai.google.dev/gemini-api/docs/rate-limits
[G8]: https://docs.cloud.google.com/service-usage/docs/reference/rest/v1beta1/services.consumerQuotaMetrics/list
[G9]: https://developers.google.com/identity/protocols/oauth2/native-app
[G10]: https://developers.google.com/identity/protocols/oauth2
[G11]: https://developers.google.com/identity/protocols/oauth2/production-readiness/policy-compliance
[G12]: https://docs.cloud.google.com/monitoring/api/ref_v3/rest/v3/projects.timeSeries/list
[G13]: https://ai.google.dev/gemini-api/docs/oauth
[G14]: https://docs.cloud.google.com/api-keys/docs/reference/rest/v2/projects.locations.keys/create
[G15]: https://docs.cloud.google.com/resource-manager/reference/rest/v3/projects/search
[G16]: https://docs.cloud.google.com/service-usage/docs/reference/rest/v1/services/enable
[G17]: https://ai.google.dev/gemini-api/docs/api-key
[C1]: https://developers.cloudflare.com/workers-ai/platform/pricing/
[C2]: https://raw.githubusercontent.com/cloudflare/skills/00eec280fa5c788000cd32f27a58b39dba15edfc/skills/cloudflare/references/graphql-api/patterns.md
[C3]: https://github.com/cloudflare/skills/commit/16b44fd80a852a02a53fbd79695ceeb5867b757e
[C4]: https://pages.johnspurlock.com/graphql-schema-docs/cloudflare.html
[C5]: https://raw.githubusercontent.com/michaloo/flareclerk/main/src/commands/ai.js
[C6]: https://raw.githubusercontent.com/michaloo/flareclerk/main/src/commands/auth.js
[C7]: https://developers.cloudflare.com/analytics/graphql-api/features/discovery/introspection/
[C8]: https://developers.cloudflare.com/analytics/graphql-api/sampling/
[C9]: https://developers.cloudflare.com/api/resources/billing/subresources/usage/methods/get_account_usage_v2/
[C10]: https://developers.cloudflare.com/api/resources/billing/subresources/usage/methods/get_account_usage_v1/
[C11]: https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/
[C12]: https://developers.cloudflare.com/api/resources/iam/subresources/oauth_clients/methods/create/
[C13]: https://developers.cloudflare.com/fundamentals/oauth/integrate-with-cloudflare/
[C14]: https://developers.cloudflare.com/fundamentals/oauth/authorizing-an-application/
[C15]: https://developers.cloudflare.com/workers-ai/get-started/rest-api/
[C16]: https://developers.cloudflare.com/analytics/graphql-api/getting-started/authentication/api-token-auth/
[C17]: https://developers.cloudflare.com/fundamentals/api/how-to/create-via-api/
[C18]: https://developers.cloudflare.com/analytics/graphql-api/tutorials/capture-graphql-queries-from-dashboard/
