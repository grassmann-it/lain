# Google und Cloudflare: Free-Zugang, Monitoring und Anmeldung

Stand und öffentlicher Quellenabruf: **25.10.2026**. Bezugsstand des Auftrags: lain
`cbf083f`; gezielte Quellprüfung der vorhandenen Arbeitskopie, keine Abnahme des
installierten Dienstes. Architektur-Sparring/Recherche, keine neue akzeptierte
Implementierungsentscheidung. Ausschließlich dieses Dokument geändert. Keine
Zugangsdaten, Browserprofile, Cookies, Nutzer-CLI-Konfigurationen oder Hostdaten
geöffnet; keine authentifizierten Anbieterabfragen, Logins, Registrierungen,
Inferenz, Käufe, Builds oder Installation. Öffentliche GETs mit höchstens zwei
parallelen Requests. Cloudflares Dokumentationswebsite lieferte bei drei
Einstiegsseiten HTTP 403; stattdessen wurden die öffentlichen offiziellen
`cloudflare/cloudflare-docs`-Quelldateien gelesen. Das ist kein Account-403.

## Antwort auf die eigentliche Frage

**Free-Accounts sind kein pauschaler Monitoringblocker.** Gemini dokumentiert
Free-Tier-Modellmetriken und eine OAuth-Alternative zum Inferenzkey. Cloudflare
gewährt Workers AI auf Workers Free, nennt eine tägliche freie Allokation und
OAuth für Desktop/CLI. Ein Google- oder Cloudflare-Login alleine erteilt jedoch
weder Projekt-/Accountrechte noch einen bezahlten Tarif oder die Verfügbarkeit
eines bestimmten Analytics-Datasets.

Für **Google-Monitoring** ist der in lain bereits vorhandene isolierte offizielle
`gcloud`-Owner ein konkreter Weg ohne eigene lain-Clientregistrierung. Für
**Cloudflare** ist ein eigener öffentlicher PKCE-Client offiziell möglich;
Wrangler ist inzwischen ein besser belegter Token-/Refresh-Kandidat als zuvor,
aber dessen aktueller Scopekatalog beweist den benötigten Workers-AI-Analytics-
Zugriff noch nicht. Beides ist reguläres OAuth mit Anbieter-CLI bzw. registrierter
App, keine Übernahme einer Browser-Sitzung.

Eine automatische **Neuregistrierung eines vollständigen Anbieteraccounts** mit
AGB-Zustimmung, Region-/Altersprüfungen, IAM, Billing oder Bezahldaten ist damit
nicht belegt. Innerhalb eines vorhandenen, berechtigten Accounts können einzelne
Ressourcen offiziell über APIs angelegt werden; dafür wären zusätzliche
Schreibrechte und ein ausdrücklicher Provisionierungsumfang nötig.

## Mess- und Zugriffsmatrix

| Fall / Messobjekt | Plan und Ressource | Auth / Mindestrecht / Endpoint | Aussage und DE-Reason | Konkrete nächste Aktion |
| --- | --- | --- | --- | --- |
| Gemini **Developer API / AI Studio**, Free: Requests und Inputtokens pro Modell | Bestehendes aktiviertes Gemini-Projekt, unterstützte Region/Accountbedingungen; **keine Paid-Tier-Voraussetzung für die dokumentierten Free-Metrikfamilien** | OAuth `monitoring.read` reicht für `GET monitoring.googleapis.com/v3/projects/{PROJECT_ID}/timeSeries`; IAM `monitoring.timeSeries.list`. Eigener Desktop-OAuth, offizieller gcloud-Owner oder ablaufender Snapshot; Inferenzkey reicht nicht [G1–G4] | `generate_content_free_tier_requests/{usage,limit}` und `generate_content_free_tier_input_token_count/{usage,limit}` unter `generativelanguage.googleapis.com/quota/`. Usage DELTA, Limit GAUGE; Ressource `generativelanguage.googleapis.com/Location`, Modell/`limit_name`/Location erhalten. **Reason:** „Monitoring-Zugang für dieses Gemini-Projekt fehlt oder ist nicht bestätigt.“ | Vorhandenen Gemini-Account unverändert mit Google Cloud CLI als Monitor verbinden; exaktes Projekt wählen. Bei bereits verbundenem Monitor zuerst IAM/API/Projekt prüfen, dann nur vorhandene Serien lesen. Ein leeres Ergebnis ist unbekannt, nicht null. |
| Gemini Developer API, Paid: Modellusage und Limits | Projekt mit aktiver Billing-Verknüpfung; Tier ist projekt-/billingbezogen, kein Gemini-App-Pro-Abo [G1] | Derselbe Monitoring-Read. Familien `generate_requests_per_model` und `generate_content_paid_tier_input_token_count`; zusätzliche Tierfamilien existieren, nicht blind summieren [G3] | **Reason:** „Modellusage ist verzögert; sofort verfügbare Restkapazität unbekannt.“ Bis zu 150 s Sichtbarkeitsverzug. Limits können unabhängig von Usage angezeigt werden. | Aktive Tier-/Modellfamilien und `limit_name` im ausgewählten Projekt qualifizieren; zusätzliche Familien erst bei belegtem Bedarf ergänzen. Keine automatische Hochstufung. |
| Gemini effektive Quotakonfiguration | Free oder Paid nicht pauschal ausgeschlossen; konkrete Antwort des Projekts entscheidend | `GET cloudquotas.googleapis.com/v1/projects/{PROJECT_NUMBER}/locations/global/services/generativelanguage.googleapis.com/quotaInfos`; OAuth **`cloud-platform`**, IAM `cloudquotas.quotas.get`. Projektauflösung optional `resourcemanager.projects.get` [G2,G5] | Liefert Limitkonfiguration/Dimensionen, **keine Verbrauchsbilanz**. `locations/global` ist die Verwaltungsressource, nicht die Behauptung einer globalen Inferenzregion. **Reason:** „Effektive Limits benötigen zusätzliche Projektleserechte.“ | Vorhandene Monitoring-`/limit`-Serien zuerst nutzen. Cloud Quotas nur als getrennten Fallback; alle Model-/Location-/Quota-Dimensionen erhalten. |
| Generischer Gemini-Projektrequestzähler | Gleiches Projekt, Export muss existieren | `serviceruntime.googleapis.com/api/request_count`, `consumed_api`, Service `generativelanguage.googleapis.com`, Monitoring-Read [lokaler Sourceabgleich] | Alle erfassten API-Methoden/Fehler, keine reine Modellinferenz. Bestehender Reader zählt **UTC-Tag** und kann bis 1.800 s hinterherliegen. **Reason:** „Nur allgemeine Projektrequests verfügbar; Modellrest unbekannt.“ | Als klar benannten Fallback erhalten; nicht mit Pacific-Tag/Modelllimits verrechnen. |
| Gemini Spend/Billing-Cap | Eigenes Messobjekt; Billingberechtigung unabhängig vom Usage-Read | AI-Studio-Tabelle nennt `billing.resourceCosts.get`, `billing.resourcebudgets.read`, zum Setzen `billing.resourcebudgets.write`, Billingkontoansicht `billing.accounts.get` [G2] | Requests/Tokens und Billingkosten sind unterschiedliche Größen. **Reason:** „Kostenansicht benötigt Billingrechte.“ Kein belegter Spend-Endpoint in dieser Teilrecherche qualifiziert. | Bei gewünschter Kostenansicht gesondert recherchieren/qualifizieren; keinen Billing-Admin-Grant für reine Usage verlangen. |
| **Vertex AI**, heute in mehreren Docs zu „Gemini Enterprise Agent Platform“ umgeleitet: Modellinvocations/Input-/Outputtokens | Eigenes Cloudprodukt; Standardpfad braucht Projekt, API-Aktivierung, Billing, IAM und Modell-/Regionwahl. Express Mode ist gesonderter begrenzter Free-Einstieg für berechtigte neue Gmail-Nutzer, bis 90 Tage ohne Billingdaten [V1–V3] | Inferenz ADC/OAuth, gewöhnlich `roles/aiplatform.user`; Region oder `global`. Beispiel OpenAI-Endpoint `https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}/endpoints/openapi`. Monitoring separat mit `monitoring.timeSeries.list`; `aiplatform.googleapis.com/publisher/online_serving/{model_invocation_count,token_count}` [V1,V4,V5] | Eigenes Ressourcen-/Quota-/Billingmodell; AI-Studio-Free und `generativelanguage`-Metriken sind kein Vertex-Guthaben. **Reason:** „Vertex benötigt einen gesondert qualifizierten Cloudzugang.“ Ein universelles verbleibendes Requestbudget ist nicht belegt. | PV-VERTEX eigenständig lassen; vorhandenen Developer-API-Account nicht umdeuten. Erst gewünschtes Vertex-Produkt/Standard- oder Express-Modus, Projekt/Region/Abrechnung bestätigen. |
| Cloudflare Workers AI, **Workers Free**: täglich freie Neurons | 10.000 Neurons/UTC-Tag; größere Nutzung erfordert Workers Paid. Einzelne Modelle haben abweichende Paid-/Unified-Billing-Voraussetzungen [C1] | Dashboard ist dokumentiert. Kandidat `POST https://api.cloudflare.com/client/v4/graphql`, gebundener `accountTag`, `aiInferenceAdaptiveGroups { sum { totalNeurons } }`. Generische GraphQL-Doku verlangt Account → **Account Analytics → Read**, auf den ausgewählten Account eingeschränkt [C2] | Free-Allocation ist belegt, aktuelle konkrete Dataset-Freigabe/Minimalrechte/Freshness/Billingabdeckung für diesen Account **nicht**. **Reason:** „Workers-AI-Analytics für diesen Account noch nicht freigegeben oder nicht verfügbar.“ Keine Aussage „nur Pro“. | Zuerst exakte Accountbindung und Tokenrecht prüfen; danach in autorisierter Quellenprobe Schema **und** accountbezogene Settings (`enabled`, `availableFields`) lesen. Bei verfügbarer Quelle vorhandenen abgeschlossenen UTC-Tag mit Dashboard vergleichen [C3]. |
| Cloudflare Workers AI, **Workers Paid** | Ebenfalls 10.000 freie Neurons/UTC-Tag, darüber $0,011/1.000 Neurons im beschriebenen Neuronpfad; Unified Billing/modelbezogene Ausnahmen separat [C1] | Gleicher Analytics-Kandidat; Paid allein beweist keinen Datasetgrant | Adaptives Dataset kann Schätzwerte liefern. **Reason:** „Gemeldete Neuron-Nutzung; exakte Abrechnungsabdeckung und verbleibender Freianteil unbekannt.“ Ein Freianteil ist kein Gesamtausgabelimit. | Nur `usage-estimate` anzeigen, bis Coverage/Sampling/Plan bestätigt sind. Bei Paid nicht bei 10.000 einen erschöpften Gesamtaccount darstellen. |
| Cloudflare Billing Usage v2 | Offizieller historischer Kandidat, **Alpha/Restricted**; diese Runde nicht frisch endpunktseitig qualifiziert | `GET /accounts/{account_id}/billable/usage`; kleinster Grant, Workers-AI-Metric-ID und Zugriff offen [vorige Recherche] | Kein belastbarer universeller Free-Monitorfallback. **Reason:** „Billingquelle für diesen Account nicht qualifiziert.“ | Erst bei vorhandenem autorisiertem Zugang und belegter Produktmetrik separat abstimmen; kein Upgrade/privater Endpoint als Voraussetzung verlangen. |

**Tagesgrenzen:** Gemini-RPD setzt um Mitternacht **Pacific** zurück [G1], daher
`America/Los_Angeles` mit DST und nicht dauerhaft UTC−8. Cloudflare setzt um
**00:00 UTC** zurück [C1]. Die jeweilige Reporting-Window-Grenze ist nicht
automatisch der Reset eines beliebigen einzelnen Limits. RPM/TPM, Usage, Limits,
Credits und Spend nicht zu einer Zahl vermischen. Fehlende Messwerte bleiben
unbekannt; eine ausdrücklich gemessene Null bleibt null.

## OAuth, offizielle CLIs und automatisches Onboarding

| Weg | Öffentlich belegt / Voraussetzungen | Nutzbarer Maschinenpfad und verbleibende Grenze |
| --- | --- | --- |
| Google eigener Desktop-Client | Offizieller Native-App-Flow; eigener Desktop-OAuth-Client, Systembrowser, PKCE/State/Loopback/Refresh. Für öffentliche Auslieferung passende Consent-/Produktionsqualifikation. Appregistrierung ist üblicherweise Maintainerarbeit, nicht ein neuer Client je Endnutzer [G6] | Monitor minimal mit `monitoring.read`; Cloud Quotas erfordert `cloud-platform`. Breiter Scope ersetzt IAM nicht. Bestehender lain-CloudOAuth-Pfad verlangt derzeit explizit `cloud-platform`. Keine CLI-Client-ID in diesen Flow kopieren. |
| **Offizielle gcloud-CLI** für Google Monitor | `gcloud auth login` ist der offizielle webbasierte Flow. `CLOUDSDK_CONFIG` erlaubt einen eigenen schreibbaren Konfigurationsordner; das offizielle CLI behält OAuth-/Refreshhoheit [G7,G8] | `gcloud auth print-access-token ACCOUNT` ist dokumentiert. Es liefert allein keine belastbare neue Tokenexpiry. Aktueller lain-Owner nutzt stattdessen versions-/hashgebunden `config config-helper --format=json --force-auth-refresh` mit `access_token`, `token_expiry`, Account. Öffentliche Referenz-URL für `config-helper` liefert 404: **Source-/Versionsvertrag, kein stabil zugesicherter öffentlicher CLI-Befehl**. Nicht als neu eine Stunde gültig etikettieren. `X-Goog-User-Project` kann `serviceusage.services.use` auf diesem Projekt erfordern [G9]. |
| Google CLI UX / Isolation | Offizielle CLI besitzt die Clientidentität. `--launch-browser` ist Browserflow; `--no-launch-browser` verlangt Copy/Paste eines Codes; `--no-browser` ist Remote-Bootstrap über eine andere vertrauenswürdige Maschine [G7] | Bestehende lain-Implementierung auf privaten Profilordner, feste Argumente, begrenzte Laufzeit/Output, Browser-Bridge, Account+Projektbindung und Ownerrefresh abgleichen. Keine existierenden CLI-Accounts importieren; `--update-adc` nicht für diesen isolierten Weg, da es ADC mit verändert. Technisch durchführbar ist bereits vorhanden; kein Liveconsent dieser Runde. |
| Google **OAuth-Inferenz** statt API-Key | Geminis eigener OAuth-Quickstart zeigt Bearerzugriff auf `generativelanguage.googleapis.com` mit `x-goog-user-project`, eigenem Desktop-Client und Refresh; Beispiel verwendet `cloud-platform` und `generative-language.retriever` [G10] | Offiziell unterstützter OAuth-Zugang ist belegt. Das Beispiel ist **kein Mindestgrantnachweis für jede konkrete `generateContent`-/OpenAI-Route**. Transport, Scope/IAM und Quota-Projekt je Methode qualifizieren. Vorhandener lain-gcloud-Weg ist derzeit ein **Monitorweg**, kein dadurch bewiesener OAuth-Inferenzanschluss. |
| Google nach OAuth automatisch API-Key erzeugen | API Keys API unterstützt `POST https://apikeys.googleapis.com/v2/projects/{project}/locations/global/keys`, OAuth `cloud-platform`, IAM `apikeys.keys.create` [G11]. AI Studio nennt für Create außerdem `serviceusage.services.enable`, `iam.serviceAccountApiKeyBindings.create`, `iam.serviceAccounts.create` [G2] | Prinzipiell offizielle Ressourcenprovisionierung, aber nicht „Login genügt“. Seit 28.05.2026 sind neue AI-Studio-Keys serviceaccountgebundene **Auth Keys**; unrestricted Standard Keys werden abgewiesen, explizit eingeschränkte Standard Keys sind eigene Variante [G12]. Der einfache Keys-create-Call beweist keinen vollständigen AI-Studio-Auth-Key-Bootstrap. Binding-Create-REST-Pfad in dieser Runde nicht qualifiziert (versuchter IAM-v1-Dokupfad 404). Zuerst direkte OAuth-Inferenz prüfen; keine Serviceaccounts/Keys ungefragt anlegen. |
| Cloudflare eigener Desktop-/CLI-Client | Offiziell Authorization Code mit **S256-PKCE**, Tokenauth `none`; kein Secret im Public Client. Registrierung durch Super Administrator, Administrator oder OAuth Client Write. Private Clients nur Mitglieder des registrierenden Accounts; öffentliche Sichtbarkeit braucht Client-URL-Domainverifikation [C4] | Scopes aus `GET /client/v4/oauth/scopes`, nicht aus geratenen Strings. Auth/Token/Revoke unter `https://dash.cloudflare.com/oauth2/` [C5]. Konkrete Analytics-/Inferenzscope-IDs, zugelassener Loopback, Refresh-Lifetime/Rotation und Endpointakzeptanz noch qualifizieren. Kein Beleg für Paid-Zwang bei Registrierung. |
| **Wrangler offizielle CLI** | Aktueller offizieller Source bietet `wrangler login --scopes …`, `--scopes-list`, Browser und Callbackoptionen, sowie **`wrangler auth token --json`**. JSON ist `{type:"oauth",token:…}` oder API-Token/Global-Key aus Environment; Refresh erfolgt bei Bedarf vor Tokenrückgabe [C6,C7] | Sehr konkreter Kandidat ohne eigene lain-Clientregistrierung. **Nicht bereits als fertigen Monitor freischalten:** Wrangler-Katalog akzeptiert `account:read` („account details, analytics, memberships“) und `ai:write`, aber **nicht** `account-analytics.read`; Validator weist unbekannte Scopes ab [C8]. Gleichsetzung von `account:read` mit erforderlichem Analytics-Datasetgrant bleibt unbelegt. `auth token --json` enthält keine Expiry/Scopes/Accountbindung. Type prüfen und geerbte Credentials-Env neutralisieren; Output niemals ins Log. |
| Wrangler Isolation / andere offizielle Cloudflare-CLI | Aktueller Pfadresolver nutzt XDG, bevorzugt jedoch existierendes `~/.wrangler`; `WRANGLER_HOME` steht ausdrücklich noch als TODO im Source [C9]. Named Profile existiert, Login versteckt `--profile`, `auth create` ist gesonderter Befehl [C6] | Kein erfundenes `WRANGLER_HOME` und kein globales Profil umschalten. Privaten Profil-/Keyring-/Legacy-Fallback-Vertrag zuerst qualifizieren. Derselbe offizielle Repositoryzweig enthält für eine **separate cf-Clientregistrierung** `account-analytics.read`, `ai.read`, `ai.write` [C10]; das beweist weder Wrangler-Freigabe noch hier eine installierte/releaste cf-CLI. Nicht deren Client-ID nachbauen. |
| Cloudflare Inferenz / Tokenprovisionierung | REST-Quickstart dokumentiert Account-ID und API-Token; selbst erstellte Tokens sollen **Workers AI Read und Edit** haben. Endpoint `/client/v4/accounts/{ACCOUNT_ID}/ai/run/{model}` [C11] | Allgemeines OAuthangebot und Wrangler-`ai:write` sind ein ernsthafter Weg für direkten Bearerzugriff; genaue Inferenz-/Analyticsakzeptanz gesondert beweisen. Tokenerzeugung ist zusätzliche Verwaltungsfähigkeit, kein Bestandteil eines Readgrants. Kein neuer API-Token nötig, wenn ein qualifizierter OAuthgrant die beiden Datenpfade abdeckt. |

## Befund zum vorhandenen lain-Stand und Reihenfolge

Gelesen: `docs/providers/{quota-sources,monitor-auth,cloudflare-workers-ai-monitoring}.md`,
Google-Qualification-/gcloud-Blätter, vorherige Google-/Cloudflare-Recherche,
Provider-Work-Packages (`PV-GEMINI`, `PV-CLOUDFLARE`, `PV-VERTEX`) und gezielt
`GeminiModelQuotaAdapter`, `GeminiCloudQuotaAdapter`, `GeminiMonitoringQuotaAdapter`,
`CloudflareWorkersAiReader`, `CloudflareQuotaAdapter`, Google-gcloud-Owner/-Prozess.
Die älteren allgemeinen Quellenblätter beschreiben teils einen überholten
Implementierungsstand: heute existieren die Free/Paid-Gemini-Modellreader samt
separaten Limit-Gauges und ein isolierter gcloud-Lifecycle; Cloudflare versucht
bereits einen begrenzten accountgebundenen `aiInferenceAdaptiveGroups`-Read.
Das beweist weder installierte Verfügbarkeit noch reale Quellenqualifikation.

1. **Gemini `genericQuotaSetupRequired`:** Kein Beleg für fehlenden Bezahlplan.
   Wenn nur der Inferenzkey vorliegt, ist ein zusätzlicher erneuerbarer
   Monitoringgrant nötig. Vorhandenen Account mit gcloud-Owner und genau seinem
   Projekt verbinden; keine Ersatzkeys, kein Vertex-Account und kein Billingupgrade.
   Bereits vorhandene Modul-/Host-/UI-Verfügbarkeit vor einem neuen Bau prüfen.
2. **Cloudflare `CloudflareGraphQLPermissionError`:** Fehler ist derzeit absichtlich
   generisch und identifiziert weder „Free ist ausgeschlossen“ noch das exakt
   fehlende Datasetrecht. Erst Account-ID und Account Analytics Read bestätigen.
   Dann Datasetsettings und Schema accountbezogen prüfen. Die aktuelle
   Introspektion im Reader prüft nur Feldexistenz und nur bei Schemafehlern;
   `settings.enabled/availableFields` fehlt als differenzierende Diagnose.
   Historischer offizieller Datasetbeleg plus Drittanbieterschema aus der
   vorigen Recherche sind weiterhin Kandidatenevidenz, kein aktueller
   Freigabe-/Billingvertrag.
3. **Einfaches Cloudflare-Anmelden:** Zuerst begrenzte Quellenqualifikation und
   Wrangler-Scope-/Profilvertrag; alternativ eigenen PKCE-Client qualifizieren.
   Ein neuer OAuthflow ohne zugängliche Neuronquelle würde den Monitoringfehler
   lediglich anders authentifizieren. Die UX sollte Resource-/Grant-/Dataset-
   Blocker getrennt benennen.
4. **Ein Login für Inferenz und Monitoring:** Für Google direkte OAuth-Inferenz
   priorisiert gesondert qualifizieren; für Cloudflare konkrete Scopes und beide
   Endpoints. Keine automatisierte Accountanlage behaupten. Bestehende Keys und
   stabile Account-/Projekt-/Account-ID-Bindungen bleiben die Basis.

## Frisch gelesene Quellen mit Belegsnippet

Alle folgenden Quellen am **2026-10-25** tatsächlich per öffentlichem GET gelesen.
Links auf `main`/`production` sind beweglich. Zum Abrufzeitpunkt: Cloudflare Docs
`ec32a21c71d1aa8747ef270e180ef0d31e5231f6` (00:35:29Z), workers-sdk
`9e05ade14e54e692c844a33bfb5b797804c3352a` (14:24:59Z); GETs der Dateien erfolgten
zuvor auf den jeweiligen Zweignamen, kein Gleichheitsnachweis aller Dateiblobs.

| ID | Offizielle Quelle / kurzes gelesenes Snippet |
| --- | --- |
| G1 | https://ai.google.dev/gemini-api/docs/rate-limits — „applied per project, not per API key“; „reset at midnight Pacific time“; Free „Active project or free trial“, Tier 1 „link an active billing account“. |
| G2 | https://ai.google.dev/gemini-api/docs/troubleshoot-ai-studio — „Usage dashboard … monitoring.timeSeries.list“; „Rate limit dashboard … cloudquotas.quotas.get“; Create-API-key-Rechte wie oben. |
| G3 | https://docs.cloud.google.com/monitoring/api/metrics_gcp_d_h — `quota/generate_content_free_tier_requests`, `/input_token_count`, Paid-Familien; „DELTA“, „GAUGE“, „model“, „limit_name“, „not visible for up to 150 seconds“. |
| G4 | https://docs.cloud.google.com/monitoring/api/ref_v3/rest/v3/projects.timeSeries/list — Scopes `cloud-platform`, `monitoring`, `monitoring.read`. IAM zusätzlich durch G2 belegt. |
| G5 | https://docs.cloud.google.com/docs/quotas/reference/rest/v1/projects.locations.services.quotaInfos/list — „cloud-platform“; „cloudquotas.quotas.get“; `projects/123/locations/global/services/…`. |
| G6 | https://developers.google.com/identity/protocols/oauth2/native-app — „public clients and cannot keep secrets confidential“; „Always use PKCE“; Systembrowser und „loopback IP address“; Refresh bis Widerruf oder Ablauf. Keine produktive lain-Clientfreigabe damit behauptet. |
| G7 | https://cloud.google.com/sdk/gcloud/reference/auth/login — „Obtains access credentials … via a web-based authorization flow“; `--no-browser` Remote-Bootstrap; `--update-adc`. |
| G8 | https://docs.cloud.google.com/sdk/docs/configurations — „config directory can be changed … CLOUDSDK_CONFIG“; „must be write-enabled“. |
| G9 | https://cloud.google.com/sdk/gcloud/reference/auth/print-access-token — „token itself may not be enough“; `X-Goog-User-Project`; „serviceusage.services.use“. |
| G10 | https://ai.google.dev/gemini-api/docs/oauth — `Authorization: Bearer`, `x-goog-user-project`, `cloud-platform,generative-language.retriever`; `InstalledAppFlow` und Refreshbeispiel. |
| G11 | https://docs.cloud.google.com/api-keys/docs/reference/rest/v2/projects.locations.keys/create — „only supported value for location is global“; „apikeys.keys.create“. |
| G12 | https://ai.google.dev/gemini-api/docs/api-key — „Starting May 28, 2026 … automatically created as auth keys“; „Bound directly to a Google Cloud service account“; unrestricted Standard Keys abgewiesen. |
| G13 | https://ai.google.dev/gemini-api/docs/available-regions — „Gemini API and Google AI Studio … following countries and territories“; Google-Terms/Region zusätzlich G2. |
| V1 | https://docs.cloud.google.com/vertex-ai/generative-ai/docs/start/quickstarts/quickstart-multimodal — „Verify that billing is enabled“; „roles/aiplatform.user“; `GOOGLE_CLOUD_LOCATION=global`. Aktueller Redirect/Branding Agent Platform. |
| V2 | https://docs.cloud.google.com/vertex-ai/generative-ai/docs/start/express-mode/overview — „New users … free … up to 90 days“; „don't need to provide billing information“; separate from Cloud Free Program. |
| V3 | https://docs.cloud.google.com/vertex-ai/generative-ai/docs/quotas — aktueller Redirect zu https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/quotas ; „quotas generally apply at … project level“; Modell-/Region-/Global-Quoten unterschiedlich. Kein pauschaler DSQ-Remaining-Vertrag aus alter Dokumentation übernommen. |
| V4 | https://docs.cloud.google.com/monitoring/api/metrics_gcp_a_b — `publisher/online_serving/model_invocation_count` und `/token_count`, DELTA, PublisherModel; Token-`type` input/output und weitere Labels. |
| V5 | https://docs.cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference — OpenAI-Clientbeispiel mit regionalem `aiplatform.googleapis.com`-Endpoint, Projekt/Location und `credentials.token`. |
| C1 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/workers-ai/platform/pricing.mdx — „both the Free and Paid Workers plans“; „10,000 Neurons per day“; „00:00 UTC“; „Some models require a paid billing method“. |
| C2 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/analytics/graphql-api/getting-started/authentication/api-token-auth.mdx — „Account … Account Analytics … Read“. |
| C3 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/analytics/graphql-api/features/discovery/settings.mdx — „enabled … available for a requester“; „availableFields“; „ALL plans for the essential datasets“, größere Pläne erweiterte Datasets. **Nennt Workers-AI-Dataset nicht als all-plan-freigegeben.** |
| C4 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/fundamentals/oauth/create-an-oauth-client.mdx — Desktop/CLI „none“, „Required, S256“; Scopekatalog; öffentliche/privat-Sichtbarkeit und Domainverifikation. |
| C5 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/fundamentals/oauth/integrate-with-cloudflare.mdx — `dash.cloudflare.com/oauth2/{auth,token,revoke,logout,userinfo}`. |
| C6 | https://raw.githubusercontent.com/cloudflare/workers-sdk/main/packages/wrangler/src/user/commands.ts — `auth token --json`, JSON-Union, `--scopes`, Callback und Environmentpräferenz. |
| C7 | https://raw.githubusercontent.com/cloudflare/workers-sdk/main/packages/workers-auth/src/core/factory.ts — „Read the OAuth access token from local state, refreshing first if needed“. |
| C8 | https://raw.githubusercontent.com/cloudflare/workers-sdk/main/packages/workers-auth/src/core/scopes.ts — `account:read`, `ai:write`; Validator `scope in DefaultScopes`; kein `account-analytics.read`. |
| C9 | https://raw.githubusercontent.com/cloudflare/workers-sdk/main/packages/workers-utils/src/global-wrangler-config-path.ts — „TODO … WRANGLER_HOME“; Legacy-Home vor XDG. |
| C10 | https://raw.githubusercontent.com/cloudflare/workers-sdk/main/packages/workers-auth/src/cf/scopes.ts — separate cf-Registrierung enthält `account-analytics.read`, `ai.read`, `ai.write`; Kommentar „Registration alone does not make a scope requestable“. |
| C11 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/workers-ai/get-started/rest-api.mdx — „both Workers AI - Read and Workers AI - Edit“; Bearer-Run-Endpoint. |
| C12 | https://raw.githubusercontent.com/cloudflare/cloudflare-docs/production/src/content/docs/analytics/graphql-api/limits.mdx — „different … plans“; accountscoped Query nur ein Account; spezifische Datasetlimits. |

Nicht frisch bestätigt bleiben: konkrete aktuelle Cloudflare-Workers-AI-
Dataset-/Grantkombination auf Free/Paid, Sampling-/Billingabdeckung,
Billing-v2-Grant/Metric-ID, OAuth-Endpunktqualifikation für diese Inferenzrouten,
produktiver lain-Public-Client, unterstützte Wrangler-Version und isolierte
Profil-/Expiryprojektion. Dafür fehlen Account- bzw. Lifecycleproben, nicht ein
Anlass für ein geratenes Upgrade oder Cookieimport.
