# Synthetic und xAI/Grok

Stand: 24.10.2026. Offizielle öffentliche Dokumentation gelesen; kein Account,
kein Key, keine Live-Inferenz und kein Nachweis einer konkreten API-Berechtigung.
Die Katalogeinträge sind vorbereitet und bleiben bis zum Verbindungstest ungeprüft.
**xAI/Grok ist ein anderer Anbieter als Groq.**

## Synthetic einrichten

1. Eigenen Account unter <https://synthetic.new/users/create> anlegen; das erfolgt
   durch den Nutzer, nicht durch lain.
2. Im Account einen passenden API-Tarif wählen. Die offizielle Preisseite führt
   aktuell Go für 9, Standard für 30 und Big für 90 US-Dollar pro Monat; reguläre
   nutzungsabhängige API-Abrechnung ist ebenfalls möglich. Ein bestehender Tarif
   ist nicht aus einem API-Key ableitbar.
3. Den persönlichen API-Key auf <https://dev.synthetic.new/docs/api/overview>
   abrufen und im privaten Credential-Speicher von lain hinterlegen.
4. OpenAI-kompatible Basis `https://api.synthetic.new/openai/v1` verwenden.
   Die offizielle Einstiegshilfe dokumentiert zusätzlich `/v1`; für Anthropic
   ist `https://api.synthetic.new/anthropic/v1` dokumentiert. Die Auswahl eines
   Anthropic-Protokolls setzt dessen tatsächliche Backendunterstützung voraus.
5. Modell explizit auswählen und separat Verbindung/Fähigkeiten prüfen.
   Ein fehlender Key ist „Zugangsdaten fehlen“, ein vorhandener Key zunächst
   „Verbindung ungeprüft“. Keiner dieser Zustände bedeutet ein Gratisbudget.

Die Abos verwenden einen **gemeinsamen, über die Woche regenerierenden Kreditpool**
mit unterschiedlichen Modellgewichten. Die Modell-Tokenschätzungen der Preisseite
sind alternative Nutzungsszenarien, keine addierbaren Modellbudgets. Zusätzliche
Nutzung zu API-Preisen muss bewusst gewählt werden; sie ist kein kostenloser
Fallback. Big enthält Beta-Modi: Fast verbraucht doppelt so viel Kontingent;
Infinite ist langsamer, erfordert Streaming und ist aktuell auf eine bestimmte
DeepSeek-Aliasroute beschränkt. Diese enthaltene Aboleistung ist kein dauerhaft
kostenloses API-Angebot. Kein Modus wird automatisch aktiviert.

### Synthetic-Routenmatrix

| Konkrete Modell-ID über OpenAI | Hersteller | Inferenzanbieter | Kontext laut offizieller Liste | Max. Ausgabe | Tools / Streaming / Reasoning / Bilder / JSON-Schema |
| --- | --- | --- | --- | --- | --- |
| `hf:deepseek-ai/DeepSeek-V4.1-Flash-1017` | DeepSeek | Synthetic | 512k | unbekannt | API-Parameter dokumentiert / ja / Parameter dokumentiert / modellabhängig, ungeprüft / Parameter dokumentiert |
| `hf:moonshotai/Kimi-K3` | Moonshot AI | Synthetic | 512k | unbekannt | API-Parameter dokumentiert / ja / Parameter dokumentiert / als Vision-Alias dokumentiert / Parameter dokumentiert |
| `hf:zai-org/GLM-4.7-Flash` | Z.ai | Synthetic | 192k | unbekannt | API-Parameter dokumentiert / ja / Parameter dokumentiert / unbekannt / Parameter dokumentiert |

`tools`, `parallel_tool_calls`, `reasoning_effort` (`low`, `medium`, `high`),
`reasoning_content`, `response_format` und SSE sind im Chat-API-Vertrag beschrieben.
Eine allgemeine Parameterbeschreibung ist kein erfolgreicher Fähigkeitstest jeder
Modellroute. Die Katalogeinträge lassen unbewiesene modellbezogene Fähigkeiten
unbekannt. `/models` liefert weitere Details nach Einrichtung. `syn:large:text`
und weitere `syn:`-Aliase wechseln ihr Ziel; fest ausgewählte IDs können laut
Anbieter bei Rotation entfernt werden. Gateway ist bei direktem Zugriff keiner;
das abrechnende Konto ist das eigene Synthetic-Konto. Für Embeddings nennt die
Dokumentation dagegen Fireworks als Inferenzanbieter; das darf nicht pauschal
Synthetic zugeschrieben werden.

### Synthetic-Kontingentadapter

Der offizielle, kostenfrei abfragbare `GET https://api.synthetic.new/v2/quotas`
verwendet denselben Bearer-Key. Dokumentiertes Beispiel:

```json
{"subscription":{"limit":135,"requests":0,"renewsAt":"2025-09-21T14:36:14.288Z"}}
```

`SyntheticQuotaAdapter` liest ausschließlich dieses Schema. Die dokumentierte
Feldbezeichnung `requests` wird als Anbieter-Einheiten dargestellt, weil die
aktuelle Preisseite gewichtete Kredite beschreibt. Keine Umrechnung in Tokens
oder gewöhnliche Requests, kein erfundener Wochenreset. `renewsAt` wird nur bei
expliziter Zeitzone übernommen. Fehlen Limit oder Verbrauch, bleibt Rest unbekannt;
negative/nichtendliche Mengen und widersprüchlicher Rest werden nicht erfunden.
Ein Konto-Pool darf explizit vererbt werden, ein gemeinsamer Pool mehrerer Keys
wird ohne belegte Accountidentität nicht automatisch erzeugt.

Der Adapter akzeptiert nur offizielle API-Basen, sendet ausschließlich GET an
`/v2/quotas`, verwendet den bestehenden begrenzten Quota-HTTP-Reader und leitet
keine Zugangsdaten an frei konfigurierbare Hosts weiter. Beim Host-HttpClient
muss `AllowAutoRedirect=false` gesetzt sein. Unbekannte Schemaänderungen bleiben
sichtbar unbekannt. Parallelitätslimits und aktuelle Ratendaten sind ohne Account
nicht verifiziert; keine festen Werte werden aus dem Tarifnamen errechnet.

## xAI/Grok einrichten

1. Eigenen API-Account unter <https://console.x.ai/> anlegen und API-Billing
   selbst einrichten. Laut Quickstart benötigt die API Guthaben.
2. Unter <https://console.x.ai/team/default/api-keys> einen eigenen Inferenz-Key
   erstellen und privat speichern. Modell-/Endpoint-ACLs können dessen Zugriff
   zusätzlich beschränken. Ein Consumer-Grok-/X-Abo ist kein API-Key und hier kein
   Beleg für enthaltenes API-Guthaben.
3. `https://api.x.ai/v1` als Basis und beispielsweise `grok-4.7` verwenden.
   Responses und Chat Completions sind offiziell dokumentiert; das Backend muss
   das jeweils gewählte Protokoll tatsächlich unterstützen.
4. Team-Modelle und tatsächliche Limits unter
   <https://console.x.ai/team/default/models> prüfen. Bis dahin bleiben Account,
   Entitlements und Limits ungeprüft.

Abrechnung ist PAYG. Ein allgemeines, dauerhaft kostenloses API-Budget ist in den
geprüften offiziellen Quellen **nicht bestätigt**. xAI bleibt im Modus „Nur
kostenlos“ ausgeschlossen. Hersteller und Inferenzanbieter sind xAI; Gateway ist
bei direktem Zugriff keiner, der Pool gehört zum abrechnenden API-Team. Ein
Team-Pool darf erst mit belegter Teamidentität über mehrere Keys geteilt werden.

| Modell und Route | Kontext | Max. Ausgabe | Tools | Streaming | Reasoning | Bilder | Structured Output |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `grok-4.7` direkt `/v1` (Responses / Chat) | 500.000 | unbekannt | ja | SSE | low / medium / high (Standard) / xhigh; nicht abschaltbar | Eingabe | ja |

Der offizielle Modellstand umfasst auch `grok-4.6`, `grok-4.5`, `grok-4.3` und
spezifische datierte Varianten; diese sind keine pauschal identischen Routen.
Für reproduzierbare Vergleiche Modell-ID und konkrete Route erhalten. Grok 4.7
kostet laut Modellseite unter 200k Prompt-Tokens 2 USD/M Input und 6 USD/M Output;
ab 200k gelten 4 beziehungsweise 12 USD für alle Tokens der Anfrage. Dies ist
keine Accountpreisgarantie. Toolgebühren können hinzukommen.

Ratenlimits gelten pro Team und Modell; sie hängen von der durch API-Umsatz
bestimmten Tier sowie möglichen Key-Limits ab. Die offiziellen aktuellen Tier-0-
Werte von Grok 4.7 sind 150 RPS und 50 Mio. TPM. Das sind dokumentierte Obergrenzen,
kein bestätigtes Kontingent eines noch nicht vorhandenen Accounts; ein
Parallelitätslimit oder Restbudget wird daraus nicht abgeleitet.

Bei Responses liefert Grok 4.7 verschlüsselte Reasoning-Inhalte zurück; beim
Fortsetzen müssen diese gemäß Protokoll erhalten werden. Sie sind keine
lesbaren Gedankengänge und gehören nicht als Klartext in den Inspector.
Chat Completions hat dafür kein Ciphertext-Feld. Das ist ein Protokollunterschied,
keine automatisch austauschbare Unterstützung durch einen Kompatibilitätsproxy.

### xAI-Kontingente: bewusst nicht mit Inferenz-Key abfragen

Die dokumentierte Management-API hat eine andere Basis
`https://management-api.x.ai` und benötigt einen **separaten Management-Key**
sowie eine Team-ID. Die Billing-API beschreibt unter anderem
`GET /v1/billing/teams/{team_id}/prepaid/balance`. Deren `total.val` ist ein
vorzeichenbehafteter USD-Cent-Wert (das offizielle Beispiel zeigt `-1000` nach
Kauf); weder die `changes`-Liste noch ein Ausgabe-Ratenlimit sind Rest-Tokens.

Der aktuelle Credentialvertrag enthält keinen gesonderten Management-Key mit
Berechtigungsnachweis. Deshalb ist xAI-Quota hier ausdrücklich **nicht
unterstützt** und es erfolgt kein Management-Aufruf. Es gibt keinen geratenen
`/usage`-Endpoint und keinen Versuch, einen normalen Inferenz-Key umzuwidmen.
Ein späterer Managementadapter muss das getrennte Secret, Teamidentität, genaue
Vorzeichensemantik sowie getrennte Prepaid-/Postpaid-Zustände qualifizieren.

## Quellen und Prüfung

Alle folgenden Quellen wurden am 24.10.2026 ohne Anmeldung abgerufen:

- <https://synthetic.new/pricing> — Tarife, gewichteter gemeinsamer Pool, Fast/Infinite.
- <https://dev.synthetic.new/docs/api/overview> — offizielle Protokolle und Basen.
- <https://dev.synthetic.new/docs/api/getting-started> — eigener Key, `/v1`-Alias.
- <https://dev.synthetic.new/docs/api/models> — Modell-IDs, Kontext, Aliasrotation, Inferenzanbieter.
- <https://dev.synthetic.new/docs/openai/chat-completions> — Tools, Reasoning, SSE, JSON-Parameter.
- <https://dev.synthetic.new/docs/synthetic/quotas> — Endpoint und Beispielvertrag.
- <https://docs.x.ai/developers/quickstart.md> — Account, Credits, eigener API-Key, Responses-Basis.
- <https://docs.x.ai/developers/models.md> und <https://docs.x.ai/developers/models/grok-4.7.md> — Modelle, Fähigkeiten und Preise.
- <https://docs.x.ai/developers/model-capabilities/text/reasoning.md> — Reasoning-Modi und verschlüsselte Fortsetzung.
- <https://docs.x.ai/developers/model-capabilities/text/streaming.md> — Chat-SSE.
- <https://docs.x.ai/developers/rate-limits.md> — Team-/Modelllimits und Spend-Tiers.
- <https://docs.x.ai/developers/management-api-guide.md> — getrennte Management-Credentials.
- <https://docs.x.ai/developers/rest-api-reference/management/billing.md> — Prepaid-/Postpaid-Verträge.

Synthetischer Harness: `bash tests/providers-synthetic-xai/run.sh` (vom Integrator
nach Build-Freigabe auszuführen). Fake-HTTP prüft offiziellen Quota-Vertrag,
Null/Nullverbrauch, Schemaänderungen, unqualifizierte Zeitstempel, widersprüchliche
Mengen, Host- und Providerisolation sowie abgewiesene Redirects. Er liest keine
lokalen Secrets und trifft keine Live-Anbieter. Ein bestandener Mocktest beweist
keinen Livezugang und keine Modellberechtigung.
