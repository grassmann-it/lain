# OpenRouter, Requesty und Cloudflare Workers AI

Offizielle Quellen geprüft am 24.10.2026 (Europe/Berlin). Die JSON-Manifeste unter
`src/Lain.Host/Providers/Catalog/` enthalten dieselben Quellen mit Prüfzeitpunkt.
Keine Zugangsdaten vorhanden; echte Erreichbarkeit, Inferenz und Kontostände sind
**nicht getestet**. Keine Registrierung, Käufe oder Live-Inferenz wurden ausgeführt.

## Grenzen der Vorbereitung

Die Manifeste sind deklarative Onboarding-/Katalogdaten. Sie sind keine bereits
aktivierten Core-Routen. Der Integrator muss sie in den Katalog aufnehmen und den
OpenRouter-Quotaadapter über den vorhandenen Host-HTTP-Client ohne Redirects
registrieren. `GatewayRouteValidation` prüft Provider-spezifische Voraussetzungen;
ein späterer Konfigurations-/Requestwriter muss diese Prüfung auch anwenden und
alle tatsächlichen Fallbacks und Zusatzdienste erfassen. Ein UI-Schalter allein
erzwingt keine kostenlose Inferenz.

`FreeOnlyRejections` verlangt eine positiv qualifizierte aktuelle Modellmenge
und belegten harten Kostenstopp. Diese Eingaben sind Prüfresultate, keine aus einem
Namenssuffix oder einer Kontobezeichnung abgeleiteten Wahrheiten. Kostenpflichtige
und unbekannt bepreiste Fallbacks werden verworfen. Gateway-Policy-Aliase und
kostenpflichtige oder ungeprüfte Extras sind ebenfalls ausgeschlossen. Die
Cloudflare-Manifestrouten bleiben bewusst nicht freigegeben: derselbe Modellpfad
kann unter einem Paid-Konto zusätzliche Kosten verursachen.

## OpenRouter

Der offizielle OpenAI-kompatible Basisendpunkt ist
`https://openrouter.ai/api/v1`; eine persönliche Inferenz-API-Key-Credential wird
als Bearer verwendet. Schlüssel werden vom Nutzer unter
[OpenRouter Keys](https://openrouter.ai/keys) angelegt.

Die [Preisseite](https://openrouter.ai/pricing) bestätigt 20 Requests/Minute und
50/Tag für kostenlose Modelle; nach mindestens 10 USD insgesamt gekauften Credits
steigt das Tageslimit auf 1.000. Das ist eine Kaufbedingung für eine höhere
Requestgrenze, keine kostenlose Tokenmenge oder garantierter Kontostand. Die
[Limitdokumentation](https://openrouter.ai/docs/api_reference/limits) beschreibt
zusätzliche Kontodetails einschließlich Schwellentoleranz. Der Adapter verwendet
deshalb niemals `is_free_tier` als Rechenformel für Requests oder Restbudget.

Konkrete auswählbare, offiziell als kostenlos dokumentierte Beispielrouten sind
[`meta-llama/llama-3.3-70b-instruct:free`](https://openrouter.ai/meta-llama/llama-3.3-70b-instruct:free)
mit 131.072 Kontexttokens und
[`qwen/qwen3-coder:free`](https://openrouter.ai/qwen/qwen3-coder:free/uptime)
mit 1.048.576 Kontexttokens. Nicht belegte Ausgabegrenzen oder Fähigkeiten bleiben
`null`. [Free-Varianten](https://openrouter.ai/docs/guides/routing/model-variants/free)
sind eigene Katalogeinträge mit eigenem Kontext und Endpoints; ein beliebig
angehängtes `:free` erstellt keinen solchen Eintrag.

[`openrouter/free`](https://openrouter.ai/docs/guides/routing/routers/free-router)
ist eine separate Option mit zufälliger Auswahl aus geeigneten kostenlosen
Modellen. Sie eignet sich nicht für Modellvergleiche mit festem Modell. Hersteller,
Familie und tatsächlicher Inferenzanbieter werden erst aus der beobachteten Antwort
belegt. Beim konkreten Modell beschreibt der Herstellerpräfix ebenfalls nicht
zwangsläufig den ausführenden Anbieter.

Eine `models`-Fallbackliste kann eine kostenpflichtige Alternative enthalten und
diese normal abrechnen, auch wenn das erste Modell kostenlos war. Für kostenloses
Routing müssen alle Alternativen bekannte kostenlose Einträge sein; der zufällige
Free-Router wird allein verwendet. Kostenpflichtige Plugins oder andere Extras
dürfen die Modellkostenprüfung nicht umgehen.

Der read-only Adapter ruft ausschließlich
[`GET /api/v1/key`](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-key)
auf. Er normalisiert `free_model_daily_requests` als Requestzähler, `limit` und
`limit_remaining` als USD-Schlüsselausgabenlimit sowie die separat gelieferten
`budgets` als überlappende USD-Beschränkungen. `effective_budget` wird nicht ein
zweites Mal addiert. Unbegrenzte/null-Schlüssellimits sind kein Nullguthaben.
Eine monatliche Schlüsselgrenze wird mit `usage_monthly`, nicht Lifetime-Usage
verglichen. Die API-Felder liefern keinen Grund, Requestreste aus USD-Werten oder
aus der Herstellerfamilie abzuleiten. Fehlende Werte und nicht gelieferte
Resetzeitpunkte bleiben unbekannt. Budget-Identitäten bleiben bei umsortierten
API-Antworten stabil.

## Requesty

Der offizielle [Quickstart](https://docs.requesty.ai/quickstart) verwendet
`https://router.requesty.ai/v1` mit eigenem Bearer-API-Key. Das Konto und der
Schlüssel werden unter [Requesty](https://app.requesty.ai) eingerichtet.

Die [Free-Model-Dokumentation](https://docs.requesty.ai/features/free-models)
(Stand der Seite 12.10.2026) bestätigt einen **gemeinsamen Organisationspool**:
neue Organisationen 200 Requests/Tag und 20/Minute, zahlende Organisationen
1.000/Tag und 60/Minute. Das ist kein je Modell oder API-Key unabhängiges Budget.
Die genaue Resetuhrzeit und das aktuelle Restkontingent wurden nicht belegt.

Die Manifeste enthalten explizit dokumentierte kostenlose IDs:
`nvidia/nemotron-3-super-120b-a12b`, `nvidia/nemotron-3-nano-30b-a3b` und
`google/gemma-4-31b-it`. Aktuelle Preise/Fähigkeiten müssen über den dokumentierten
[`GET /v1/models`](https://docs.requesty.ai/features/supported-models) beziehungsweise
die Model Library qualifiziert werden. Ein Herstellername ist kein Beleg für den
tatsächlichen Inferenzbetreiber; dieser bleibt ohne Endpointnachweis unbekannt.

Für kostenlose Nutzung müssen vollständige Provider-/Modell-IDs fest ausgewählt
werden. Nackte Aliase und `policy/*`-Routen werden nicht als konkrete kostenlose
Route akzeptiert. Die offiziellen
[Fallback Policies](https://docs.requesty.ai/features/fallback-policies) können
zwischen kostenlosen und bezahlten Modellen wechseln. Es ist keine erfundene
OpenRouter-Option `provider.allow_fallbacks` für Requesty implementiert. Ohne
geprüfte aktuelle Modellpreise und Konto-/Policykonfiguration bleibt die
Free-only-Aktivierung aus. Ein Requesty-Restbudgetendpoint wurde nicht qualifiziert;
die Darstellung muss unbekannt beziehungsweise manuell bleiben.

Die [Terms](https://www.requesty.ai/terms) und die Modellmetadaten unterscheiden
Training Permitted Models. Das Setup verweist deshalb auf die konkrete
Modell-Datenrichtlinie; es behauptet keine pauschale Nichtverwendung aller
kostenlosen Eingaben.

## Cloudflare Workers AI

Der [offizielle REST-Einstieg](https://developers.cloudflare.com/workers-ai/get-started/rest-api/)
benötigt **Account-ID und API-Token**. Der Token braucht Workers AI Read und Edit
für das gewählte Konto. Ein eigener Worker muss für direkten lokalen API-Zugriff
nicht deployt werden. Der native Modellendpunkt ist
`https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai/run/{MODEL}`.
Der [OpenAI-kompatible Endpunkt](https://developers.cloudflare.com/workers-ai/configuration/open-ai-compatibility/)
hat die Basis
`https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai/v1`.
Account-ID-Platzhalter müssen vor Benutzung expandiert und validiert werden.
Responses unterstützt dort nur GPT-OSS und nicht-streamende Requests; daraus
folgt keine allgemeine Responses-/Streamingfähigkeit aller Modelle.

Die [Preisdokumentation](https://developers.cloudflare.com/workers-ai/platform/pricing/)
bestätigt 10.000 **Neurons** pro Tag, Reset 00:00 UTC. Neurons messen Compute und
sind keine Tokens. Die Zahl ist die dokumentierte Allokation, kein beobachteter
Reststand. Ein verlässlicher read-only Neuron-Restbudgetendpoint wurde hier nicht
qualifiziert; es wird kein Restwert aus lokaler Tokennutzung errechnet.

Workers Free stoppt an der kostenlosen Allokation. Workers Paid rechnet zusätzliche
Nutzung ab. Prepaid AI Gateway Credits sind noch ein anderer Abrechnungsweg.
Deshalb genügt die Existenz eines Free Tiers nicht für Free-only; unbekannte oder
Paid-Kontopositionen bleiben ausgeschlossen, solange kein verifizierter harter
Kostenstopp besteht.

Ein konkreter Free-plan-zugänglicher Kandidat ist
[`@cf/google/gemma-4-26b-a4b-it`](https://developers.cloudflare.com/workers-ai/models/gemma-4-26b-a4b-it/)
(Google als Hersteller, Cloudflare als Inferenzbetreiber): 256.000 Kontexttokens,
Tools, Reasoning, Bilder und Streaming sind in der Modellseite dokumentiert.
Die Ausgabegrenze und Structured Output bleiben ungeprüft. Auch
`@cf/zai-org/glm-4.7-flash` ist im
[Changelog zu Free-plan-Zugriff](https://developers.cloudflare.com/changelog/post/2026-07-28-models-require-workers-paid/)
genannt. Diese Routen sind keine universell nullpreisigen PAYG-Modelle.

Die aktuelle Preisnotiz verlangt bezahlte Abrechnung für Kimi K2.6/K2.7 Code,
GLM 5.2/5.3/5.3 Flash sowie DeepSeek V4 Flash 0731/Pro 0813. Das
[aktuelle Changelog](https://developers.cloudflare.com/changelog/product/workers-ai/)
nennt auch DeepSeek V4.1 Flash 1017 als Paid-/Gateway-Credit-Modell. Eine starre
Negativliste wäre damit schon unvollständig: kostenloses Routing braucht eine
positiv geprüfte Modellmenge und blockiert unbekannte Neuzugänge.

## Synthetische Qualifikation

`dotnet run --project tests/gateways/Gateways.Tests.csproj` prüft ausschließlich
Fake-HTTP: festen GET-Endpunkt, Bearer-Header, keine fremden Accountheader,
fehlende/Null-/Nullstandwerte, USD-/Requesttrennung, Budgetreihenfolge,
ausgelastete Budgets und bereinigte HTTP-Fehler. Die Policytests prüfen
kostenpflichtige/ungeklärte Fallbacks, Policyaliase, Zusatzdienste, falsche
Accountendpunkte und unbekannte Cloudflare-Abrechnung. Das ist keine bestandene
Live-Providerverbindung. Ausführung erst im vom Integrator serialisierten Buildslot.
