# Kimi und Z.ai: bestehende Referenzanschlüsse für Account Connect

Stand **2026-10-24**, Arbeitsbasis **5dc916f**. Ergänzender Recherchevorschlag, keine akzeptierte Architektur und keine Implementierung. Nur öffentliche Dokumentations-/Source-GETs; keine Konto-API-Abfragen, Logins, privaten Konfigurationsdateien, CLI-Ausführung oder Schlüsselanlage. Die früheren Berichte bleiben unverändert. Die [JSON-Begleitung](reference-kimi-zai.json) enthält pro Produkt den vollständigen Ablauf, Credentials, Codefolgen und die SHA256 der geöffneten Quellen.

**Bestehende Coding-Keys bleiben der schnelle Weg:** Kimi und Z.ai können im heutigen qualifizierten Umfang denselben Inferenz-Key zur Quote verwenden. Account Connect darf dafür keinen zweiten Browserlogin oder Wechsel auf OAuth erzwingen. Für neue Konten existieren konkrete offizielle Loginwege: Kimi Device OAuth und ZCode Browser-Pairing mit kurzlebigem Project Access Token. Ihre Einbindung in lain ist noch zu qualifizieren; fehlende Qualifikation ist weder ein Nachweis technischer Unmöglichkeit noch ein pauschaler Registrierungszwang für jeden persönlichen Connector.

## Kimi / Kimi Code

### Heutiger lain-Anschluss und minimaler Ablauf

`KimiQuotaAdapter` liest nur bei eigenem Credential auf `https://api.kimi.com/coding/v1` oder `https://api.kimi.ai/coding/v1` das passende `/usages`, mit `Authorization: Bearer <derselbe Key>`. Fremde Gateway-Keys werden nicht an Kimi gesendet. Native Fenster sind 5 Stunden, 7 Tage, Monat gesamt und Monat Code; Verhältnisse bleiben Prozentwerte. Eine konservative Altformatbehandlung übernimmt nur genau eine `limits`-Zeile. Ein erfolgreicher historischer Abnahmestand ist vorhandene Projektevidenz, kein neuer Probeaufruf dieser Recherche.

Vorgeschlagen bleibt **Kimi verbinden**, mit **Modelle und Nutzung** oder **Nur Nutzung**. Vorhandenes passendes Coding-Konto: Zugang wiederverwenden, Quote lesen, Ergebnis und Zeitstempel zeigen. Kein erneuter Login, kein neuer Key. Gespeichert/configured, Login gültig, Quote gelesen und Inferenz getestet sind getrennte Zustände.

Für ein neues Konto ist **Mit Kimi anmelden** konkret belegbar:

1. Device-Flow starten, zurückgegebene Verifikations-URL öffnen und gegebenenfalls Code anzeigen.
2. Nutzer meldet sich bei Kimi an und autorisiert; Owner pollt mit Anbieterintervall und berücksichtigt Ablauf/Abbruch.
3. Ein Owner hält Access-/Refresh-Token. Quote mit Access-Token lesen; Monitor erhält nur nötige Zugriffsprojektion.
4. Modelle erst mit qualifiziertem Transport/Clientzugang anbieten. Erfolgreiches Monitoring bleibt auch bei noch ungeklärtem Modelltransport erhalten; Coding-Key ist der direkte Fallback.

Die offizielle [Login-Dokumentation](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/docs/en/guides/getting-started.md) unterscheidet Kimi Code OAuth und Kimi Platform API key. Der [Python-CLI-Source](https://raw.githubusercontent.com/MoonshotAI/kimi-cli/main/src/kimi_cli/auth/oauth.py) belegt Device Authorization, Token Polling, Refresh und die Verwendung des OAuth-Access-Tokens als Inferenzcredential. Eine zusätzliche API-Key-Anlage ist für diesen offiziellen OAuth-Modus nicht erforderlich.

Das neuere TypeScript-Projekt veröffentlicht sogar ein [browserfähiges Device-Toolkit](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/device.ts), dessen Kommentar externe Aufrufer den Flow request → URI → poll → store selbst führen lässt. [Konfiguration](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/constants.ts) enthält `https://auth.kimi.com` und eine offizielle Client-ID. Das ist konkrete öffentliche Implementierungsevidenz, aber keine automatische Berechtigung, diese Clientidentität in lain zu übernehmen. Eigene Verteilung/Admission ist gezielt zu klären; eine allgemein notwendige separate Registrierung ist durch diese Quellen nicht belegt.

### Offizieller CLI-Owner als Alternative

Der dokumentierte experimentelle [lokale Server](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/docs/en/reference/server-api.md) von `kimi web` bietet unter anderem:

| Operation | Verhalten |
| --- | --- |
| `GET /api/v1/auth` | Managed-Provider-Loginstatus; `models_ready` prüft keine Credentials |
| `POST /api/v1/oauth/login` | Device-Login; bereits authentifiziert ohne Nutzerinteraktion |
| `GET /api/v1/oauth/login` | Pending/authenticated/denied/expired/cancelled mit Ablauf und Intervall |
| `DELETE /api/v1/oauth/login` | Pending-Login abbrechen |
| `GET /api/v1/oauth/usage` | Live-Planquoten und nullable Booster-Wallet |
| `POST /api/v1/oauth/logout` | OAuth-Credential und Managed-Provider entfernen; kann Remote Control dieser Maschine beenden |

Der Server authentifiziert mit einem **eigenen lokalen Bearer**, standardmäßig auf Loopback; der [Web-Guide](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/docs/en/guides/web.md) beschreibt Startup und Rotation. Account Connect könnte den offiziellen CLI-Owner referenzieren, ohne Refresh-Token zu kopieren. Die genaue sichere Übergabe des lokalen Bearers und Versionsbindung sind vor Umsetzung zu qualifizieren. Kein Scan echter Credential-Dateien.

`GET oauth/usage` verlangt Prüfung von Envelope `code` **und** `data.kind`: ein Upstreamfehler kann trotz erfolgreichem Envelope als `kind:error` erscheinen. `quota.usages` enthält nur vorhandene `limit5h`, `limit7d`, `monthTotal`, `monthCode`; `extraUsage` enthält eine eigenständige Booster-Wallet. [Offizieller Parser/Reader](https://raw.githubusercontent.com/MoonshotAI/kimi-code/main/packages/oauth/src/managed-usage.ts) nutzt dieselben Coding-Hosts und Access-Token-Bearer. Booster fehlt dem aktuellen lain-Adapter und darf nicht als bereits umgesetzt gelten.

Dieser Server ist ein Agent mit Sessions, Tools und ACP-Anschluss. Seine Dokumentation belegt keinen generischen OpenAI-kompatiblen Proxy für beliebige lain-Routen. OAuth-Login ist daher noch kein Beleg für fertig qualifizierte lain-Inferenz. Der lokale Bearer hat zudem breitere Rechte als bloß Quote lesen; Aufrufrichtlinien müssen begrenzen. Lokales Trennen darf keinen globalen CLI-Logout oder Tokenrotation auslösen.

**Nur Nutzung** darf keine Inferenz, Keyanlage oder automatische Modellkonfiguration auslösen. Offizielles Login verändert teilweise Default-Modell/Providerkonfiguration: dafür einen isolierten oder bereits bestehenden Owner verwenden bzw. die tatsächliche Verbindungsauswirkung offenlegen. Python `kimi-cli` und TypeScript `kimi-code` sind unterschiedliche Projekte/Versionen mit unterschiedlichen Homes; nicht austauschbar voraussetzen.

**Produktgrenze:** Kimi Code ist nicht Moonshot Open Platform. Der [Plattform-Source](https://raw.githubusercontent.com/MoonshotAI/kimi-cli/main/src/kimi_cli/auth/platforms.py) trennt Coding von `api.moonshot.cn/v1` und `api.moonshot.ai/v1`. Erfolgreiches Coding-Monitoring qualifiziert keine PAYG-Balance oder Regionsübertragung. Codefolge: `AccountService.CanonicalProvider` erkennt derzeit `kimi.ai`, aber noch nicht `kimi.com` anhand des Hosts; separat lösen und vorhandene Account-IDs erhalten.

## Z.ai / GLM Coding Plan

### Heutiger lain-Anschluss und bestehende Keys

`ZaiQuotaAdapter` liest `https://api.z.ai/api/monitor/usage/quota/limit` mit **rohem Key in Authorization**, ohne zusätzliches Bearer. Er akzeptiert nur eigene Credentials auf dem exakten internationalen Host und bekannten API-Pfaden. [Offizielles Usage-Plugin](https://raw.githubusercontent.com/zai-org/zai-coding-plugins/main/plugins/glm-plan-usage/skills/usage-query-skill/scripts/query-usage.mjs) belegt dasselbe Inferenzcredential für Monitoring. Native Credits-/Token-/MCP-Fenster samt `type/unit/number` bleiben erhalten.

**Z.ai verbinden** mit vorhandenem passendem Key bedeutet wiederverwenden → Quote lesen → unabhängige Resultate anzeigen. Kein Zusatzlogin. Die [Claude-Anleitung](https://docs.z.ai/devpack/tool/claude.md) unterscheidet persönliche Coding- und Team-Keys. [Coding Tool Helper](https://docs.z.ai/devpack/extension/coding-tool-helper.md) fragt ausdrücklich nach einem vorhandenen Key; seine Konfigurationsautomatik ist allein kein Beleg für automatisches Login oder Keybootstrap.

Internationales Coding, standardmäßige API-Abrechnung und CN BigModel bleiben getrennte Produkte. Ein akzeptierter Standard-API-Pfad beweist keinen Coding-Anspruch. Aktuelles lain deckt weder CN-Hosts noch Teamprojekt-Header/`type=2` ab.

### Tatsächlich belegter neuer Login mit Tokenbootstrap

Das offizielle ZCode-Repository liefert einen konkreteren Loginpfad als die Helper-Anleitung. [CLI-Pairing](https://raw.githubusercontent.com/zai-org/ZCode/main/apps/zcode-cli/packages/adapters/src/auth/cli-oauth.ts), [Login-Owner](https://raw.githubusercontent.com/zai-org/ZCode/main/apps/zcode-cli/packages/bootstrap/src/auth-login.ts), [Business-Exchange](https://raw.githubusercontent.com/zai-org/ZCode/main/apps/zcode-cli/packages/adapters/src/auth/coding-plan-api-key.ts) und [Project-Token-Client](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/shared/src/project-access-token-client.ts) korrespondieren:

1. Client erzeugt zufälligen 32-Byte-Polltoken. `POST https://zcode.z.ai/api/v1/oauth/cli/init`, Body `{provider:"zai"}`, Polltoken als Bearer. Antwort: `authorize_url`, `expires_at`, `flow_id`, `poll_interval_sec`.
2. Browserfreigabe an der zurückgegebenen HTTPS-URL. `GET …/oauth/cli/poll/{flow_id}` mit demselben Polltoken bis `ready`, `failed` oder Ablauf. `ready` liefert ZCode-Sessiontoken, Nutzer-ID und getrennt `zai.access_token`.
3. `POST https://api.z.ai/api/auth/z/login` mit `{token: OAuthAccessToken}` liefert Business-JWT. OAuth-, ZCode- und Business-Token haben verschiedene Zwecke.
4. `GET /api/biz/customer/getCustomerInfo` mit rohem Business-JWT liefert Organisationen/Projekte. Bei mehreren passenden Kontexten muss lain den Nutzer wählen lassen; keine blinde Übernahme des offiziellen first/default-Projekts.
5. `GET /api/biz/v1/organization/{org}/projects/{project}/api_keys` findet dedizierten Datensatz; fehlender Eintrag wird im offiziellen Flow per POST mit Name und `usageScene:1` angelegt. Für Team gelten `keyType:2`, Kontext und zusätzliche Header.
6. **`apiKey` in dieser Antwort ist eine nicht geheime Key-ID.** Danach `POST …/api_keys/{id}/access_tokens` mit Bearer Business-JWT und offiziellen Client-/Versionsangaben erzeugt den kurzlebigen Inferenztoken. Nicht die Key-ID in lain als vermeintlichen Secret speichern.

[Token-Validierung](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/shared/src/project-access-token.ts) erwartet `accessToken`, `tokenType:"Bearer"`, `expiresIn`, `expiresAt` und berücksichtigt `enable`. Owner erneuert innerhalb des gültigen Login-/Projektkontexts, bündelt parallele Erneuerung und verwirft abgelehnte Generationen. Die [korrelierte Migrationsnotiz](https://raw.githubusercontent.com/zai-org/ZCode/main/docs/model-provider-project-access-token.md) und der tatsächliche [Quota-Provider](https://raw.githubusercontent.com/zai-org/ZCode/main/packages/services/src/usage-stats/providers/bigmodelUsageQuotaProvider.ts) belegen Tokenverwendung für Modell und frühere API-Key-Businessoperationen. Der Reader löst account request auth auf und sendet dessen Credential roh an die Quote. Teamkontext setzt Org-/Project-Header und `type=2`.

Das ermöglicht als Vorschlag **eine sichtbare Verbindung mit intern mehreren Tokenarten**, ohne Nutzerkopie eines langlebigen Inferenz-Secrets. Modelle und Nutzung darf nach vorab offengelegter Folge einen passenden dedizierten Keydatensatz anlegen und PAT ausstellen. Eine eigene lain-Clientkennung, unterstützte Admission/Version und Drittverteilung sind zu qualifizieren. `clientType:"zcode"`, offizieller OAuth-Client oder `zcode://`-Callback sind keine automatisch für lain freigegebenen Identitäten. Die Quellen belegen keine allgemeine Pflicht zur eigenen Drittanbieterregistrierung; ebenso wenig einen schon fertigen lain-Zugang.

### Nur Nutzung und notwendige Codegrenze

Der offizielle gemeinsame Resolver **kann beim ersten Nutzungsabruf einen Keydatensatz anlegen**. Ein UI-Schalter allein macht ihn nicht read-only. lain benötigt getrennte nicht anlegende Discovery und zulässige Tokenausstellung aus bestehender Key-ID. Vorhandenes API-Key-/PAT-Material wiederverwenden; wenn es fehlt, Kontologin als Teilerfolg zeigen und vorhandenen Key oder Console anbieten. Ob ein reiner Business-Token-Lesepfad ohne Keydatensatz nutzbar ist, bleibt eng begrenzt offen. Nicht als unmöglich behaupten und nicht durch versteckte Anlage ersetzen.

Auch ZCode-CLI-Login verändert Standalone-Konfiguration/Default-Modell; ihn nicht undifferenziert als Nur-Nutzung-Kommando starten. Reset-, Kauf- und andere Schreiboperationen im umfangreicheren offiziellen Quotaservice gehören nicht zum Monitor.

Die eigentliche Implementierungsfolge ist ein qualifizierter Login-/Credential-Owner mit kurzlebiger Projektion. PAT nicht als statischen API-Key persistieren und seine Erneuerung verlieren; kein Refresh im Quotaadapter. Account/Familie/Origin/Org/Projekt/Key-ID/Login-Generation begrenzen Cache und Identität. Bestehende Keykonten und read-only Snapshotimporte bleiben erhalten. Connect löst keinen kostenpflichtigen Inferenztest, Planwechsel oder Kauf aus.
