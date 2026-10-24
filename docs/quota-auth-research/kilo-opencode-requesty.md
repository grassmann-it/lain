# Kilo Gateway, OpenCode Go / Go Plus / Zen und Requesty: Login und Kontomonitoring

Geprüft am **24.10.2026**, Arbeitsbasis `main` / `5dc916f`. Recherche und Architekturvorschläge, keine akzeptierte Implementierungsentscheidung. Nur öffentliche Herstellerdokumentation und öffentliches Hersteller-Quellmaterial gelesen. Keine Credentials, Anmeldung, Registrierung, Nutzerbrowser, authentifizierte Providerabfrage, Inferenz, Käufe, Dienste oder Builds. Keine Runtimeänderung. Kilo ist laut Nutzer erschöpft; Aufladen und bezahlter Fallback sind ausgeschlossen.

## Ergebnis für „anmelden, den Rest macht lain“

| Produkt | Tatsächlich vorhandener Login | Mit demselben Zugang sinnvoll lesbar | Nächste Integration |
| --- | --- | --- | --- |
| Kilo Gateway | Offizieller CLI-Device-Login liefert Kilo-Token; persönlicher Gateway-Key ist JWT | CLI verwendet denselben Token für Inferenz, Profil und Geldguthaben; Team über explizite ID | Balance-Adapter mit vorhandenem persönlichen Key zuerst; eigener Device-Login nach Klärung Drittappvertrag |
| OpenCode Go | Console-Device-OAuth im offiziellen Client; externe Anleitung weiterhin API-Key | Inferenz meldet Go-Allowance je Request; kein verifizierter Gesamtquoten-Readvertrag | Native Request-Allowance erhalten; Console MCP auf read-only Quota-/Usage-Tools qualifizieren |
| OpenCode Go Plus | Gleicher Console-Login; eigenes Abo mit anderen Modellreferenzlimits | `billing_source=go-plus`, nicht als Go oder PAYG verbuchen | Gleiche Technik, eigene Planidentität und Fenster |
| OpenCode Zen / aktuelles Console PAYG | Console-Login bzw. Workspace-Key | Kosten je Request; Billing-/Usage-Leserechte dokumentiert, konkreter REST-Balancevertrag nicht gefunden | Console MCP als eigener Monitoring-OAuth-Kandidat; PAYG getrennt halten |
| Requesty | Offizielle CLI hat Authorization Code + PKCE und automatische API-Key-Erstellung | Neuer Inferenzkey liest eigenes Key-Budget und eigene Usage; Org-Guthaben mit zusätzlichem manage-read Key | Self-Monitor jetzt konkret implementierbar; danach lain-Clientregistrierung für PKCE-Bootstrap klären |

Die alte pauschale Beschreibung „keine qualifizierte Quelle“ ist für Kilo-Balance und Requesty-Key-Usage zu grob. Fehlender OAuth-Clientvertrag, fehlende lain-Implementierung und fehlende Livequalifikation sind jeweils andere Lücken.

## Kilo Gateway

### Vorhandener lain-Stand

`src/Lain.Host/Providers/Catalog/kilo.json` und `docs/providers/cohere-kilo.md` bereiten persönlichen API-Key und `https://ai-gateway.kilo.ai/api/v1` vor; Quota noch unsupported. Der Nutzer meldet erschöpftes Konto. Ein erfolgreicher Login würde das Guthaben nicht wiederherstellen.

### Offizieller Device-Flow und gemeinsamer Token

Der aktuelle offizielle Client ist mehr als ein Hinweis: [device.ts](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/auth/device.ts) und [device-auth-tui.ts](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/auth/device-auth-tui.ts) zeigen:

1. `POST https://api.kilo.ai/api/device-auth/codes`, JSON-Content-Type, ohne Client-ID oder Scope im beobachteten Client. Antwort: `code`, `verificationUrl`, `expiresIn`.
2. Browser öffnet die gelieferte Verifikationsadresse. `GET https://api.kilo.ai/api/device-auth/codes/{code}` pollt alle 3 Sekunden, bis Ablauf. 202 pending, 403 denied, 410 expired; approved liefert `token`, `userEmail`.
3. Token wird als `access` **und** `refresh` gespeichert; der Client setzt ein Jahr Ablauf. Das ist **kein belegter Refresh-Grant** und keine serverseitig garantierte Einjahres-Laufzeit. Keine PKCE-/RFC-8628-Konformität aus dem bloßen Device-Namen ableiten.
4. [plugin.ts](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/plugin.ts) lädt den Access-Token als `kilocodeToken`; [provider.ts](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/provider.ts) verwendet ihn als Inferenz-Bearer. API-Key-Modus verwendet denselben Credentialtyp ohne Browser.

[profile.ts](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/profile.ts) und [constants.ts](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/constants.ts) belegen genau:

```http
GET https://api.kilo.ai/api/profile
Authorization: Bearer <Kilo-Token>

GET https://api.kilo.ai/api/profile/balance
Authorization: Bearer <derselbe Token>
X-KiloCode-OrganizationId: <nur bei gewähltem Team>
```

Profil enthält Benutzer, `organizations` mit ID/Name/Rolle, `selectedOrganizationId`, `hasPersonalAccount`. Teamzuordnung beim Login validieren, danach explizite lokale Auswahl erhalten. Die Balanceantwort enthält `balance: number`; die [offizielle Oberfläche](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-console/src/routes/profile/profile-utils.ts) stellt sie direkt als Dollarbetrag dar. Deshalb nicht wie `cost_microdollars` durch eine Million teilen. Der Client setzt fehlende Balance auf null/0; **lain muss fehlende/ungültige Werte unbekannt lassen**, echte 0 aber erhalten. Kein Total, Verbrauch, Reset oder Credit-Expiry aus einer Balance erfinden.

Zusätzlich gibt es im [Kilo-Pass-Client](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/kilo-pass.ts) `GET /api/trpc/kiloPass.getState?batch=1&input={"0":null}` mit demselben Bearer. Subscription-Felder: `currentPeriodBaseCreditsUsd`, `currentPeriodUsageUsd`, `currentPeriodBonusCreditsUsd`, `nextBillingAt` / `nextRenewalAt`, Status. Das ist ein versionierter interner Hersteller-Clientvertrag, **keine öffentlich zugesicherte allgemeine Billing-API**. Für einen späteren getrennten Pass-Monitor qualifizieren; nicht mit persönlicher Gesamtbalance oder freiem IP-Fenster addieren.

[Gateway-Authentifizierung](https://kilo.ai/docs/gateway/authentication) trennt persönliche JWTs, Organisationstoken mit 15 Minuten Ablauf und BYOK. Eine Org-ID ist keine eigene Berechtigung. Beschaffung/Refresh des kurzlebigen Org-Tokens ist durch die hier untersuchte Device-Implementierung nicht belegt. Kein dokumentierter read-only Balance-Scope oder separater Readtoken gefunden; der persönliche Token ist daher auch ein Inferenzsecret. [Kilo-Anmeldung](https://kilo.ai/docs/getting-started/setup-authentication) per Google/GitHub/ChatGPT/SSO ist Kilo-Identität. Kilo nennt Anmeldung mit ChatGPT und Verbindung der ChatGPT-Subscription ausdrücklich getrennte Optionen. BYOK-/Upstream-OAuth berechtigt nicht automatisch zum Kilo-Billingmonitor.

[Billing](https://kilo.ai/docs/gateway/usage-and-billing) belegt 200 freie Requests/Stunde **pro IP**, auch authentifiziert; kein accountbezogener Restzähler. Organisationslimits pro Nutzer resetten um Mitternacht UTC. Persönliche Credits sind Geldguthaben, keine periodische Freiquote. Kilo meldet BYOK-Kosten auf Kilo-Seite als 0; Upstream kann trotzdem abrechnen. [Terms §1(A)](https://kilo.ai/terms) sehen Gatewayzugang durch eigene Anwendungen ausdrücklich vor; die Existenz offenen CLI-Codes und dieser Gateway-Erlaubnis ist aber kein öffentlicher Drittapp-Device-Registrierungsvertrag. §5(C) enthält Einschränkungen automatischer Extraktion; daher offizielle Bearer-Reads bevorzugen, Websession-Automation nicht als pauschal erlaubt darstellen.

**Vorschlag:** Erst einen eng begrenzten Balance-Readadapter über den schon vorhandenen persönlichen Key vorbereiten. Spätere autorisierte Qualifikation liest nur `/api/profile` und `/api/profile/balance`, vergleicht Personal-/Org-Kontext, 0/fehlend/401/403 und Dollarsemantik mit Dashboard. Keine Inferenz, keine Defaults laden, keine Credits oder Auto-Fallbacks. OAuth zusätzlich nach Herstellerklärung: darf lain den Deviceflow unter eigenem Namen nutzen, gibt es Clientregistrierung/Scopes, tatsächliche Tokenexpiry/Reauth?

## OpenCode Go, Go Plus und Zen / Console PAYG

### Produkt- und Berechtigungsgrenzen

lain hat `opencode-go.json` und `OpenCodeGoAdapter`; Go und Plus sind Planvarianten, Zen/PAYG bisher kein eigener qualifizierter Katalog. [Go](https://opencode.ai/v2/docs/console/go/) kostet $10, Plus $40/Monat, jeweils Single-Member-Workspace. Andere Coding-Agenten sind ausdrücklich vorgesehen, mit eigenem User-Agent und stabiler Gesprächs-ID. Die drei Planfenster sind 5h = 20%, Woche = 50%, Monat = 100%. Modellabhängige USD-Referenzlimits gewichten die Allowance; keine additiven Cashkonten. Exakte Konto-Resetzeitpunkte und vollständige Cross-Model-Formel bleiben unqualifiziert. **Console-Budgets** resetten dagegen laut [Budgets](https://opencode.ai/v2/docs/console/budgets/) am Monatsersten 00:00 UTC und zählen Go-Inklusivusage nicht; diesen Reset nicht auf Go übertragen.

[Zen-URL](https://opencode.ai/docs/zen/) leitet aktuell zu Console Models/PAYG weiter. Diese Produktentwicklung dokumentieren, ohne alte Zen-Guthaben als Go-Abo umzubenennen. PAYG nutzt `https://opencode.ai/inference`, Go/Plus `/inference/go`. [Harness-Doku](https://opencode.ai/v2/docs/console/harnesses/) unterstützt andere Clients; freie Console-Chatmodelle sind ausdrücklich auf OpenCode beschränkt. Go Extra Usage kann bei erschöpfter Allowance PAYG-Credits verwenden; für den gewünschten Betrieb ausgeschaltet lassen.

[API-Key-Doku](https://opencode.ai/v2/docs/console/api/) belegt Workspace-gebundene persönliche Keys und Serviceaccounts, optionales Ablaufdatum, Rollen und `Usage: Read`, `Budgets: Read`, `Billing: Read`. Read-only darf Modelle listen, nicht Inferenz senden. Nur Owners/Admins können weitergehende persönliche Rechte vergeben; normale Mitglieder erstellen nur Inferenzkeys. Ein Key kann Inferenz und Reads kombinieren, wenn Rolle und konkrete Rechte reichen; ein separater restricted Monitor-Key wäre möglich. Das ersetzt keinen verifizierten Usage-/Balance-Endpoint. Persönliche Keys können **keine persönlichen Keys erzeugen**. Serviceaccount-Key-Provisioning ist nur mit ausdrücklich gesetztem `This account's API keys: Write` für den eigenen Serviceaccount und innerhalb seiner Rechte zulässig. Kein generischer Browser-login → create-personal-key-API-Flow aus dieser Doku.

### Echter Console-Device-OAuth im offiziellen Client

[OpenCode-Plugin](https://github.com/anomalyco/opencode/blob/53b044d3139ddb28a4504c934cd4fa1e7e9829d7/packages/core/src/plugin/provider/opencode.ts) und [ConsoleAccess](https://github.com/anomalyco/opencode/blob/53b044d3139ddb28a4504c934cd4fa1e7e9829d7/packages/core/src/console-access.ts) zeigen einen echten Herstellerlogin:

- Server `https://opencode.ai/console`; `POST /auth/device/code` mit JSON `{client_id:"opencode-cli", supports_org_scope:true}`. Antwort enthält device/user-code, verification URI, Ablauf und Pollintervall.
- `POST /auth/device/token`, Device-Grant; pending/slow_down behandelt der Client. Token enthält Access-/Refresh-Token, `expires_in`, optional `org_id`. Refresh nutzt denselben Endpunkt mit `grant_type=refresh_token` und persistiert rotierte Tokens. Kein PKCE im beobachteten Deviceflow.
- `GET /api/user`, `GET /api/orgs` identifizieren Nutzer und Workspace. `GET /api/v2/config` mit Access-Bearer + `x-org-id` liefert Modell-/Providerkonfiguration; derselbe Credential wird auch für Console-MCP durchgereicht. SSO-Fehler und widerrufene Sessions erfordern Reauth; Console-SSO ist kein separater API-Grant.

Der Flow existiert; er ist **kein Beleg, dass lain einfach die Hersteller-Client-ID übernehmen darf**. Eigene Clientregistrierung, zugelassene Scopes und expliziter Drittapp-Devicevertrag sind offen. Öffentlicher aktueller Console-Servercode mit Billinghandlern wurde nicht gefunden; frühere Mirrors und alte Console-Endpoints sind kein aktueller Vertrag.

### Ein besserer OAuth-Monitorpfad und sofort nutzbare Response-Daten

Die [offizielle Harness-Doku](https://opencode.ai/v2/docs/console/harnesses/#console-mcp) nennt `https://opencode.ai/console/mcp` für Workspaceverwaltung mit **OAuth und Dynamic Client Registration**, ausdrücklich für externe Harnesses. Das ist ein konkreter, wesentlich besserer Kandidat für „lain anmelden“ als ein pauschales OAuth-Nein. Öffentlich gelesene Doku/Clientquellen belegen jedoch **keine Toolnamen, Readscopes, Quota-/Usageantworten oder Resetfelder**. Weder diese erfinden noch einen MCP-Token als universellen Inferenztoken behandeln. Das öffentliche [Client-Metadata-Dokument im Quellcode](https://github.com/anomalyco/opencode/blob/53b044d3139ddb28a4504c934cd4fa1e7e9829d7/packages/console/app/src/routes/oauth/opencode/client.json.ts) betrifft OpenCodes **ausgehende** MCP-Anmeldung, nicht lain-Registrierung am Console-Server.

Die [Inference-Doku](https://opencode.ai/v2/docs/console/api/inference/#cost) hat einen sofort verwertbaren Vertrag: `usage.cost` in USD, `opencode.billing_source` = `free|balance|go|go-plus|byok`, `opencode.list_cost`, Go-`opencode.allowance` und billed token breakdown. Anthropic nutzt `usage`, Gemini `usageMetadata`; Streaming liefert die Daten am protokollspezifischen finalen Usageereignis. `cost=0` kann Go-Inklusivusage sein und beweist keine kostenlose Route. BYOK kann `cost` auslassen. Die gemeldeten Kosten stehen vor der finalen Kontoverbuchung fest und können höher als die tatsächlich abgebuchte Summe sein. **Einzelrequest-Allowance ist keine gemessene verbleibende 5h-/Wochen-/Monatsquote** und nur durch lain beobachtete Historie keine vollständige Kontonutzung.

**Vorschlag:** Native OpenCode-Abrechnungsfelder beim bestehenden Transport verlustfrei erhalten und planrichtig im lokalen Verlauf anzeigen. Für den Kontomonitor als nächsten begrenzten Versuch offizielle Console-MCP-Discovery/DCR-Dokumentation und nur `tools/list` nach autorisierter Anmeldung qualifizieren; keine Schreibtools ausführen. Erfolgskriterium: belegtes read-only Tool für Go/Plus-Fenster samt Workspace, used/limit/reset/as-of oder für PAYG-Balance. Fehlt es, Hersteller nach dem vorhandenen `Usage: Read`-/`Billing: Read`-RESTvertrag fragen. API-Key bleibt eine tragfähige externe Inferenzoption, solange ein eigener Console-Deviceclient nicht freigegeben ist.

## Requesty

### Aktueller lain-Stand und sofortige Quellen

`requesty.json` hat persönlichen Inferenzkey, Org-Freimodellpool und manuelle Quota. Neu erschlossen: offizielle Management-API **`https://api-v2.requesty.ai/v1/manage`**, nicht die Inferenzbasis `https://router.requesty.ai/v1`. Die [Key-Management-Doku](https://docs.requesty.ai/features/key-management-api) sagt verfügbar auf jedem Plan; GET benötigt manage-read, Mutationen manage-write. [Get Organization](https://docs.requesty.ai/api-reference/endpoint/manage-org-get) liefert `GET /org` → `{name, balance}`. [Billing](https://docs.requesty.ai/features/billing) definiert Org-Guthaben in USD; post-paid Organisationen können negatives Guthaben haben. Negative Balance daher nicht grundsätzlich als kaputte Antwort verwerfen. Rückgabe von Org-ID/Topup-/Resetfeldern ist nicht belegt.

[Org Usage](https://docs.requesty.ai/api-reference/endpoint/manage-org-get-usage): `GET /org/usage`, RFC3339 `start`, optional `end` (Standard 24h nach Start), maximal 100 Tage, `resolution=hour|day|month`, optionale Gruppierung. [Key Usage](https://docs.requesty.ai/api-reference/endpoint/manage-apikey/manage-api-key-get-usage): `GET /apikey/{id}/usage`; generierte Doku zeigt hier einen **GET-Body**, während Org-Usage Queryparameter nutzt. Nicht stillschweigend gleichsetzen. Der offizielle CLI-Code belegt für **self** die funktionale Queryvariante; für arbitrary IDs muss die Body-/Querydiskrepanz separat geklärt werden.

### Offizielle CLI macht bereits OAuth → API-Key → Inferenz und Self-Monitor

Die [Requesty CLI](https://github.com/requestyai/cli/tree/21809398e99d888565178d718186b637f3de4566) ist Hersteller-Code (Apache-2.0), kein fremdes OAuth-Plugin. [flow.go](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/oauth/flow.go), [PKCE](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/oauth/pkce.go) und [Callback](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/oauth/callback.go) zeigen:

1. Authorization Code mit Public-Client `requesty-cli`, kein Clientsecret, **S256 PKCE**, zufälliger state, IPv4-Loopback `http://127.0.0.1:<ephemeral>/callback`, 5-Minuten-Callbacktimeout.
2. Browserziel aus Code: `https://api-v2.requesty.ai/v1/oauth/authorize`; Scope **`manage:group:r manage:apikey:w`**, Resource `https://api-v2.requesty.ai/v1/manage`. README-Screenshot nennt abweichend app.requesty.ai; für den konkreten Request ist der Code maßgeblich.
3. Form-POST `https://api-v2.requesty.ai/v1/oauth/token` mit authorization_code, exakt gleichem Redirect, client_id, code_verifier. Kurzlebiger Access-Bearer wird einmal benutzt; laut Code liefert der Server auch Refresh, den die CLI bewusst verwirft. Kein eigener Refreshvertrag verifiziert.
4. [Onboarding](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/onboarding/flow.go): `GET /v1/manage/group`, Auswahl bei mehreren Gruppen; `POST /v1/manage/apikey` mit Name und ggf. `group_id`. Ohne Gruppe persönlicher Key, sofern Org-Policy dies zulässt. Antwort `api_key_id`, einmal sichtbarer `api_key`. CLI überlässt Permissions/Budget den Organisationsdefaults; README sagt neuer Login-Key kann Inferenz und eigene Usage, keine allgemeine Verwaltung. Nicht behaupten, dass der Client manage:none ausdrücklich setzt.
5. Nur der neue API-Key wird gespeichert. Er authentifiziert Inferenz **und** [Self-Key-Details](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/client/api_keys.go) `GET /v1/manage/apikey/self` sowie [Self-Usage](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/client/usage.go) `GET /v1/manage/apikey/self/usage?start=...&end=...&resolution=...&group_by=...`. README bestätigt, dass jede Keyart die eigene Usage lesen darf. Ein Kommentar im Usagecode sagt „organization“; **Endpoint und README zeigen Key-Scope**, keine vollständige Organisation.

Self-Details enthalten `id`, `name`, `monthly_limit`, `monthly_spend`, `expires_at`, `permissions`, ggf. group. **monthly_limit=0 bedeutet kein Key-Cap**, nicht Nullbudget. Null expiry bedeutet ohne Ablauf. Die CLI-Usage-Structs geben Periodenmap `usage` mit `total_requests`, Modalitätsrequestzahlen, `spend`, `input_tokens`, `output_tokens`, `total_tokens`, `grouped_data` vor. Dezimalwerte präzise lesen; fehlende Felder nicht Null setzen. `group_by` kann u.a. Modell, Provider, API-Key, Nutzer, BYOK trennen. Das ist Verbrauch in einem expliziten Abfragefenster, keine serverseitig gelieferte freie Restquote und kein belegter Buchungs-Latenzvertrag.

**Readtoken:** Für Self reicht derselbe Inferenzkey, weitere Authentifizierung ist unnötig. Org-Guthaben, andere Keys und Org-Usage benötigen manage-read. Dafür optional eigener Key mit `permissions.manage=read`, `permissions.completions=none`; [Create-API-Key](https://docs.requesty.ai/api-reference/endpoint/manage-apikey/manage-api-key-create) und [offizielles OpenAPI](https://docs.requesty.ai/api-reference/requesty_management-openapi.json) belegen diese Permissionachsen (`none|read|write`). Die untersuchten OAuthscopes erlauben Bootstrap, belegen aber keinen frei wählbaren Org-Balance-OAuthscope oder Recht zur Erstellung beliebig privilegierter Monitor-Keys. Kein manage-write-Rootkey als Pflicht für normalen Self-Monitor.

[Enterprise-SSO](https://docs.requesty.ai/features/sso) betrifft explizit die Dashboard-Anmeldung und ersetzt API-Keys nicht. Das ist getrennt vom vorstehenden PKCE-Grant und von BYOK. Eigene App-/Gatewaynutzung wird durch Quickstart und CLI-Harnessintegration technisch vorgesehen; ein öffentliches Verfahren, **lain** mit eigener Client-ID/Redirects zu registrieren, wurde nicht gefunden. Die frei lesbare Terms-Seite lieferte im Reader nur die Hülle, daher hier keine umfassende kommerzielle Lizenzfreigabe aus ihr ableiten. Herstellerclient-ID nicht still unter lain ausgeben.

Die [freie Org-Quote](https://docs.requesty.ai/features/free-models) ist 200/Tag + 20/min für neue, 1.000/Tag + 60/min für zahlende Organisationen, über alle freien Modelle geteilt. Exakte Zeitzone/Resetinstanz fehlen. Aus self total_requests lässt sich die orgweite freie Restquote nicht berechnen. `GET /org/usage` kann später passende Modell-/Zeitraumfilter erlauben, doch vollständige Free-only-Zählregeln, Tier und Reset bleiben nötig.

**Vorschlag:** Self-Details + Self-Usage mit vorhandenem API-Key als erste konkrete lain-Integration; funktioniert unabhängig von OAuth-Onboarding. Schema und GET-Query sind im offiziellen Client vorhanden. Danach eigener PKCE-Bootstrap mit Hersteller-Clientregistrierung klären; Implementierung der Callback-/State-/PKCE-Lebensdauer, Gruppenauswahl, einmaliger Key-Provisionierung und Secretablage. Alternativ bewusst autorisierter Import eines durch die offizielle CLI erzeugten Keys; keine automatischen Credentialsuchen. Org-Balance als separat aktivierbarer manage-read Monitor, mit sichtbarem Org-Scope und Verbindung zum richtigen Inferenzkey.

## Begrenzte Websession-Alternative und nächste Proben

Websessions sind keine abstrakt nutzlose Alternative: OpenCode Activity/Billing enthält Quoten, Resetdaten und CSV-Export; Requesty bietet [Analytics-CSV/PDF](https://docs.requesty.ai/features/analytics-exports); Kilo zeigt persönliche und Team-Usage. Ein usergesteuerter Export oder gezieltes Ablesen im später ausdrücklich geöffneten Konto kann konkrete fehlende Felder/schema erklären. Denkbarer begrenzter Fallback ist eine isolierte, vom Nutzer angemeldete lokale Webview, nur sichtbare Werte/Export, klarer account scope, as-of, Logout/Expiry und keine Hintergrundgarantie. Das ist noch keine akzeptierte Architektur und kein Zugriff in dieser Recherche.

Cookie-/SSO-Sessionimport würde eine weitreichendere Credentialart einführen und fragile interne Requests benötigen. Deshalb bei Kilo und Requesty zuerst die nun belegten Bearerquellen; bei OpenCode zuerst offiziellen MCP-OAuth/Readvertrag qualifizieren. Websession nur gezielt für den verbleibenden Gap bewerten, einschließlich Herstellerbedingungen, MFA, Rollen, Selektoren und read-only Nachweis. Keine erfundenen privaten Console-Endpoints und keine Browserautomatik zum Anlegen von Zahlungsdaten/Keys.

Priorisierte nächste Arbeit (jeweils eigener Umsetzungsauftrag):

1. **Requesty Self-Monitor**: echte Self-Key-Details/Usage gegen lokale Fixtures, fehlend/0/unlimited, Dezimalbeträge, Zeitraum und Accountisolierung; anschließend ein autorisierter read-only Livevergleich ohne Inferenz.
2. **Kilo-Balance**: fester Host/Endpoint, persönlicher Scope zuerst, 0 unbekannt sauber unterscheiden; spätere Live-GETs auch bei erschöpftem Konto ohne Kauf. Gateway- und Monitorfähigkeit separat bewerten.
3. **OpenCode**: Native Response-Billingdaten erhalten. MCP-Discovery und nur Toolinventar mit eigenem DCR-Client qualifizieren; daraus Readschema, Go/Plus/PAYG-Scope und echte Resetdaten ableiten. Gerätelogin nicht ohne eigenen Clientvertrag übernehmen.
4. **OAuth-Produktisierung**: Requesty eigene PKCE-Client-ID und Kilo eigene Devicefreigabe klären; Integration nach dem Herstellerlogin automatisch mit dem minimal ausreichenden Inferenz-/Monitorcredential verbinden. Mehrere Secrets nur dort, wo Org-Leserechte tatsächlich zusätzlich nötig sind.
