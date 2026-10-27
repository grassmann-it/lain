# Öffentlicher Quellenreview: Poe, Requesty, OpenCode Go / Go Plus, StepFun

Stand: **25.10.2026**. Implementierungsbezug: Repo `/home/tomka/Desktop/lain`, HEAD `cbf083f`; installierter Host `5f41fba` wird hier nicht als neuerer Entwicklungsstand behandelt. Der installierte Host wurde nicht ausgeführt. Gezielt gelesen: aktuelle Login-/Quotaadapter, Hostregistrierung, Providerkataloge, vorhandene `quota-auth-research`, `account-connect-research` und Requesty-CLI-Qualifikation. Die Berichte vom 24.10. sind Suchspuren, keine Beschreibung des heutigen Codes.

Alle nachstehenden öffentlichen URLs wurden am **25.10.2026** per unauthentisiertem GET abgerufen, höchstens zwei gleichzeitig. Keine privaten Credentials, Cookies, Browseranmeldung, Providerquota-/Inferenceaufrufe, Käufe, Installation, Builds oder Tests. HTTP 200 auf einer Dokumentationsseite beweist keine funktionierende Kontoberechtigung. Es wurden keine Accountdaten erhoben. Änderungen ausschließlich an dieser Berichtdatei.

## Ergebnis für die Anschlussoberfläche

| Anbieter / Produkt | Aktueller lain-Code | Öffentlich belegter nächster Weg | DE-Grund / Aktion für UI und CLI |
|---|---|---|---|
| Poe | Balanceadapter registriert; vollständiger Browseradapter vorhanden, verfügbar nur bei qualifizierter Maintainerregistrierung | Eigener offizieller PKCE-Codeflow erzeugt User-API-Key; derselbe Key liest Restpunkte | **„Poe-Anmeldung ist noch nicht für diese lain-Version freigegeben; vorhandenen Poe-API-Key verwenden.“** Maintainerclient qualifizieren, dann Browserlogin anbieten. |
| Requesty | Self-/monatlicher Cap-/Usageadapter registriert; offizieller CLI-Kandidat bewusst unavailable | Offizielle CLI kann Browser-PKCE und automatische Keyerstellung; sicheres eigenes CLI-Profil noch unqualifiziert | **„Requesty-CLI-Anmeldung ist noch nicht qualifiziert; vorhandenen Requesty-API-Key verwenden.“** Keine zusätzliche Managementberechtigung für Key-self verlangen. |
| OpenCode Go | Workspace-Key-Anschluss, Tarifreferenzen; kein Go-Restquotareader und kein OpenCode-Loginadapter | Offizielles Console-MCP mit OAuth/DCR; konkrete Go-Lesetools unbekannt. Externe Inferenz offiziell mit Workspace-Key | **„Go-Restkontingent und Reset sind über lain noch unbekannt; Workspace-Key verbinden und Console öffnen.“** DCR-MCP-Lesefähigkeiten qualifizieren. |
| OpenCode Go Plus | Gleicher Anschluss, eigenständige Plus-Planidentität; kein Plus-Restquotareader | Wie Go; Plus muss am Workspace belegt sein | **„Go-Plus-Restkontingent und Reset sind über lain noch unbekannt; Workspace-Key verbinden und Console öffnen.“** Plus nicht aus Keyform oder Go ableiten. |
| StepFun PAYG | Persönlicher `.com`-PAYG-Balancereader mit Produkt-/Scope-/Regionprüfung registriert | Persönlicher Plattformkey liest `/v1/accounts`; kein zweiter Monitorkey nötig | **„Persönlichen StepFun-PAYG-Key verbinden; Organisations- und Step-Plan-Guthaben werden getrennt behandelt.“** Unklarer Scope bleibt unbekannt. |
| Step Plan | Inferenzkatalog vorhanden; PAYG-Adapter weist Planbasis ab; kein eigener Planlogin/-quotareader | Offizielle Step-Code-Quelle belegt Cloud-Pollinglogin und persönlichen Usage-GET mit Login-Key | **„Step-Plan-Anmeldung und persönlicher Usagezugriff sind in lain noch nicht qualifiziert; Plan-Key für Inferenz und Console verwenden.“** Herstellerflow/-Handoff qualifizieren. |

„Unqualifiziert“ benennt eine konkrete Produktgrenze; es bedeutet weder „OAuth unmöglich“ noch „ein Pro-Abo ist erforderlich“. Ob eine einzelne Subscription oder freie Nutzungsstufe den APIzugriff erlaubt, darf ohne Produktbeleg nicht aus Loginfähigkeit abgeleitet werden.

## Poe

### Iststand gegen Quellenfähigkeit

`src/Lain.Host/Accounts/PoeLoginAdapter.cs` ist heute ein echter `PublicBrowserLoginAdapter`: PKCE-Helper, Tokenresponseparser, Credentialcheckpoint, Ablauf und unbestätigte Resourcebindung sind angeschlossen. `Program.cs` registriert ihn nur bei `IsAvailable`, das `RegistrationQualified: true` verlangt; Maintainerkonfiguration liegt unter `maintainer/poe-login.json`. Die alte Aussage „Hostcallback, Austausch und Persistenz fehlen“ gilt deshalb **nicht mehr für HEAD**. Das öffentliche Loginangebot hängt weiterhin an einer qualifizierten Maintainerclient-/Callbackkonfiguration; dieser Review hat keine lokale private Konfiguration gelesen und behauptet keine aktive Verfügbarkeit auf dem installierten Host.

`PoeQuotaAdapter` ist registriert und liest `GET https://api.poe.com/usage/current_balance` mit eigenem API-Key und offizieller APIbasis. Er zeigt kombinierte verfügbare Punkte, keine Allocation/Prozent-/Resetzahl. Die getrennte Historyfähigkeit ist kein aktueller Usergesamtverbrauch oder Resetreader.

### Primärbelege, öffentlich abgerufen 25.10.2026

- [Sign in with Poe](https://creator.poe.com/docs/external-applications/sign-in-with-poe): „Authorization Code Grant with mandatory PKCE“; Scope `apikey:create`; Austausch an `https://api.poe.com/token` liefert `api_key`, `api_key_expires_in`. Die Doku beschreibt ausdrücklich, dass die App einen API-Key im Namen des Nutzers erhält.
- Dieselbe Seite: „Go to poe.com/api/clients and click Create client“ sowie „localhost URIs do not need to be registered and work automatically for local development“. Das ist eine **Maintainerregistrierung**, kein Nutzerformular. Die Produktionscallback-/Loopbackfreigabe bleibt vor einem Release zu qualifizieren; localhost-Entwicklungsausnahme beweist keine beliebige Callbackvariante.
- [Get current point balance](https://creator.poe.com/api-reference/getCurrentBalance): „including both plan points and add-on points“; Antwort `current_point_balance` = „Your current available points“. Keine Query-/Pathparameter und kein Feld für Reset, ursprüngliche Zuteilung, Bucketaufteilung oder Ablauf.
- [Points history](https://creator.poe.com/api-reference/getPointsHistory): „Data available for up to 30 days“, „Maximum 100 entries per request“, Cursor `starting_after` mit letzter `query_id`. Historische Pointdebits bleiben Historie; eine Seite ersetzt keine Kontozuteilung oder vollständige Monatsrechnung.

### Konkreter Einrichtungsweg / Rechte

Nach Maintainerqualifikation: Poe wählen → Systembrowser mit frischem PKCE öffnen → Nutzer meldet sich an und bestätigt → Tokenaustausch erzeugt/übergibt den API-Key automatisch → privat speichern → Balance lesen. Nutzer kopiert weder API-Key noch Client-ID oder Konto-ID. Die Maintainerclient-ID wird mit lain konfiguriert; sie ist kein Clientsecret. Der Key ist auch inferenzfähig; ein öffentlich dokumentierter gesonderter Balance-read-only-Scope wurde nicht gefunden. `apikey:create` nicht als reines Leserecht beschriften. Kein Refreshgrant belegt; bei Ablauf/Widerruf erneut verbinden. Token-/Balanceschema belegt keine stabile User-ID; Reconnect darf einen frischen Key nicht ohne Identitätsklärung einem alten Konto zuordnen.

Free-/Plan-/Add-on-Split: Der Reader liefert genau den kombinierten Punktestand. Er identifiziert weder ein Abo noch eine freie Zuteilung. APIberechtigung eines konkreten Freeaccounts ist in diesem Quellenreview **unknown**; kein Pflichtabo erfinden. Fallback heute: vorhandenen Poe-Key einmal einfügen, derselbe Balanceadapter; keine weitere Kontonummer.

## Requesty

### Iststand gegen Quellenfähigkeit

`RequestyQuotaAdapter` ist in `Program.cs` registriert und liest bereits `GET /v1/manage/apikey/self` sowie `/self/usage?start=…&end=…` auf `api-v2.requesty.ai`. Er erhält Key-ID, Gruppe, Rechte/Ablauf/uncapped-Metadaten, behandelt `monthly_limit=0` als uncapped und trennt Keycap von Usage. Monat-zu-Beobachtung wird explizit angefordert. Resourcebindungen werden gegen Self geprüft; bei Mismatch wird Usage nicht als passender Account veröffentlicht. Die alte Aussage „Monitor manuell, Reader fehlen“ gilt **nicht mehr**.

`RequestyCliLoginAdapter` bleibt `UnavailableCliLoginAdapter`; er nennt als Blocker gepinntes Executable sowie vollständige Login-/Profil-/Keyringisolation. `docs/account-connect-qualification/cli/requesty.md` und `requesty.json` dokumentieren disabled/unqualified, keine echte CLI-Ausführung. `--profile` ist nur ein Block in gemeinsamer Herstellerkonfiguration, kein Nachweis separaten Speichers.

### Primärbelege, öffentlich abgerufen 25.10.2026

Die GitHub-API bestätigt `requestyai/cli` main als **`21809398e99d888565178d718186b637f3de4566`**, Commitdatum `2026-10-16T16:37:59Z`; die nachstehenden Dateien wurden aktuell über `raw.githubusercontent.com/.../main/...` gelesen und entsprechen dem vorhandenen Pin:

- [OAuthflow](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/oauth/flow.go): öffentlicher Herstellerclient `requesty-cli`, Scope `manage:group:r manage:apikey:w`; PKCE S256, state und Loopbackcallback. Diese Rechte dienen Gruppenlesen und **Keyerstellung**, nicht bloß Kontingentlesen.
- [Onboarding](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/onboarding/flow.go): `Groups(ctx)` wird gelesen; bei genau einer Gruppe automatisch gewählt, bei mehreren Auswahl; `CreateAPIKeyInput{Name: name}` mit optionalem `GroupID`, danach `cfg.APIKey = created.Secret`. Keine expliziten `Permissions` im Erstellungsinput, daher wirken Organisationsdefaults.
- [Keyclient](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/client/api_keys.go): Kommentar zu `SelfAPIKeyID`: „a key with no manage permission can still read its own record“; nil `Permissions` lässt „organization default in place“. Selfberechtigung ist nicht die Erlaubnis zum Lesen beliebiger fremder Keys.
- [Usageclient](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/client/usage.go): tatsächlicher Request `/v1/manage/apikey/self/usage`, Query `start`, optional `end`. Der allgemeine Kommentar „organization’s usage“ erweitert diesen konkreten Selfpfad nicht zu einem Orgreader.
- [Auth token](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/cmd/auth.go): „Print the API key of the profile named with --profile“; `Fprintln(...APIKey)`. [Config](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/config/config.go): `configPath()` benutzt `os.UserHomeDir()`. Ein isoliertes Profilverzeichnis ist damit nicht belegt.
- [Key Management](https://docs.requesty.ai/features/key-management-api.md): „Read permission is enough for the GET endpoints“; write für create/update/delete. [Orgreader](https://docs.requesty.ai/api-reference/endpoint/manage-org-get.md): `/v1/manage/org` beschreibt „name and current balance“. Orgmonitor ist ein **zusätzlicher Managementreader**, kein Ergebnis von Selfusage.
- [Free Models](https://docs.requesty.ai/features/free-models.md): „limited per organization“, neue Organisationen **200/Tag, 20/Minute**, zahlende **1.000/Tag, 60/Minute**, über alle Freemodelle kombiniert; „daily limit resets daily“. Keine exakte tägliche UTC-Uhrzeit und kein Restfreepool-Readvertrag in dieser Seite.

### Konkreter Einrichtungsweg / Rechte

Heute: vorhandenen Requesty-Key einfügen → Self erkennt Key-/Gruppenkontext → Keycap und zeitgebundene Usage anzeigen. Keine separate kopierbare Org-/Key-ID nötig; ID/Gruppe kommt aus Self. Selfmonitor braucht keinen pauschalen Manage-read-Key. Organisationssaldo optional mit ausdrücklich getrenntem `manage=read`, `completions=none`-Credential am gleichen Account ergänzen, wenn die tatsächliche Orgzuordnung nachweisbar ist; diese Ergänzung ist im aktuellen hier geprüften Selfadapter nicht implementiert.

Qualifizierbarer Komfortweg: offizielle, gepinnte CLI in vollständig eigenem Speicher starten → `requesty login --profile <owned>` → Browserlogin → nur bei mehreren Gruppen Auswahl → CLI erstellt **automatisch einen neuen API-Key** → Host übernimmt privat `requesty auth token --profile <owned>` ohne Logs/Frontend → tatsächliche Rechte und Identität per Self prüfen. Die CLI erzeugt keinen bloßen Monitor-only-Key. OAuth-Bootstrapscope und resultierende Keyrechte getrennt halten. Keine normale Nutzerkonfiguration durchsuchen oder überschreiben; Profilname allein und HOME-Umschreibung sind kein Ersatz für qualifizierten Speicher. Unterstützte Isolationsoption und Artefakt fehlen als Beleg; darum noch unavailable. Für einen eigenen nativen lain-OAuthclient wurde kein öffentlicher Registrierungs-/Redirectvertrag belegt; Herstellerclient-ID nicht einfach übernehmen.

Keycap, Organisationswallet, kostenlose Orgrequests und BYOK-Kosten sind verschiedene Größen. Selfusage zeigt Usage aller Zugriffe dieses Keys, nicht lain-only. Der Code berechnet seinen Monatszeitraum lokal; ein präziser serverseitig zurückgegebener Resetzeitpunkt ist damit nicht bewiesen. Freepoolrest bleibt ohne konkreten Reader **unknown**, nicht „Tageslimit minus lokale Requests“.

## OpenCode Go und Go Plus

### Iststand gegen Quellenfähigkeit

`opencode-go.json` hat Go und Go Plus als verschiedene Aboplans, offizielle Protokollrouten und vorsichtige `unknown`-Hinweise für aktuelle Restquote/Reset. Es gibt keinen registrierten OpenCode-Quotaadapter oder OpenCode-Loginadapter. Das ist eine fehlende **lain-Messfähigkeit**, kein Beleg gegen öffentlich vorhandenes OAuth. Der aktuelle Katalog verlangt Inferenzkey `Models Request`; diese Berechtigung allein beweist keine Usage-/Billingleserechte.

### Primärbelege, öffentlich abgerufen 25.10.2026

- [Go](https://opencode.ai/v2/docs/console/go/): Go **$10/Monat**, Go Plus **$40/Monat**. „5-hour — 20% of the monthly limit; weekly — 50%; and monthly — 100%“. Modellreferenzen bestimmen die verbrauchte Allowance; `opencode.allowance` beschreibt, „how much the request counted“. Das ist **Anfrageverbrauch**, kein kontoweiter Restreader oder exakter Resettermin. Schätzungen gewöhnlicher Requests auf derselben Seite sind keine verbindlichen Requestcaps.
- Dieselbe Seite: „Extra Usage ... fall back to your pay-as-you-go balance“. PAYG-Wallet bleibt getrennt von Go/Plus-Allowance; keine automatische Aktivierung oder rechnerische Addition.
- [Harnesses](https://opencode.ai/v2/docs/console/harnesses/): „Harnesses authenticate with a Console API key“; Account → Personal API keys → Add API key → Workspace → Name → Create → einmal kopieren. **Kein Workspaceheader nötig** laut APIseite, jeder Key ist an genau einen Workspace gebunden.
- Dieselbe Harnessseite: „Console MCP ... uses OAuth with dynamic client registration, so you sign in from the browser instead of pasting a key“, URL `https://opencode.ai/console/mcp`. Das ist ein expliziter öffentlicher Fremdclientweg; kein pauschaler eigener-Clientregistrierungsblocker. Konkrete Go-/Plus-Restquote-/Reset-Readtools, Scopes und Antwortschemas wurden hier **nicht belegt**. Aus „manage your workspace“ folgt weder Read-only noch automatische Inferenzkeyausgabe.
- [APIrechte](https://opencode.ai/v2/docs/console/api/): Models Read/Request, Usage Read, Budgets Read/Write, Billing Read/Write sind eigenständige Rechte. Mitglieder können persönliche Keys nur zum Modelllisten/-aufrufen erzeugen; Owner/Admin können Restricted/Read only bis zur eigenen Rolle vergeben. „A personal API key cannot create personal API keys.“ Keyerzeugung per Serviceaccount benötigt sein ausdrückliches eigenes Write und ist kein impliziter Bestandteil eines Monitorlogins. Ablauf optional Never oder 1–365 Tage; abgelaufene Keys müssen neu erzeugt werden.
- Harnessseite: „Free-tier chat models are only available from within OpenCode. Other harnesses receive HTTP 403 ... even with a Console API key.“ Freemodelle sind damit weder frei nutzbarer externer APIpool noch ein zusätzliches Go-Abo. Den Herstellerclient nicht imitieren.

Die aktuelle öffentliche GitHub-Baumabfrage `anomalyco/opencode` Branch `dev` zeigte die bekannten CLI-Authdateien und `packages/core/src/console-access.ts`, aber keinen daraus identifizierten konkreten Console-MCP-Goquotareader. `main` als Commitref lieferte 422; daraus folgt nichts über `dev`. Der alte Pin `53b044d...` ist hier lediglich Vorwissen aus dem 24.10-Bericht, **kein aktuell erneut geprüfter CLI-Exportvertrag**. Eine CLI-/Refreshdelegation bleibt unqualifiziert.

### Konkreter Einrichtungsweg / Rechte

Heute: vorhandenen Go-/Plus-Workspace auswählen → persönlichen Workspace-Key mit `Models Request` erzeugen und einmal übernehmen → lain verbindet Inferenz → Quote/Reset unknown mit Consolelink. Nutzer kopiert **keine Konto-/Org-ID**; Workspace wird bei Keyerstellung gewählt. Bei einem gewöhnlichen Mitglied keine stillen zusätzlichen Admin-/Billingrechte verlangen. Plusstatus muss aus einer autorisierten Quelle stammen; Keyformat allein beweist ihn nicht.

Nächste belegbare Recherche-/Qualifikationsstufe: eigener MCPclient per DCR → Browserconsent → nur bei mehreren Ressourcen Workspace auswählen → ausschließlich konkret nachgewiesene Readtools für den gleichen Workspace zulassen. Die Existenz von DCR rechtfertigt diese Untersuchung, aber noch kein Versprechen „ein Login verbindet Inferenz und alle Quoten“. Automatische Erzeugung eines API-Keys ist im MCPdokument **unknown**; kein Schreibtool als versteckte Fehlerbehebung aufrufen. Für Go und Plus bleiben dynamischer Rest, Messzeit, Resetbeginn/-ende und Mischmodellaggregation unknown, bis konkrete Readschemas vorliegen. Lokale Responsekosten/-allowance aller lain-Anfragen ersetzen diese Werte nicht.

## StepFun: PAYG und persönlicher Step Plan getrennt

### Iststand gegen Quellenfähigkeit

`StepFunQuotaAdapter` ist registriert und bewusst auf API-Key, `https://api.stepfun.com/v1`, Produkt `payg`, Ressource `personal`, Region `com` beschränkt. Organisations-/Projektsettings und unpassende Bindungen werden abgewiesen; `object=account`, `type=prepaid|postpaid`, `balance` werden normalisiert, deklarierte Org-/Projektscopes nicht als persönlich umetikettiert. Ausgabe ist CNY-Balance, kein Limit/Prozentsatz. Der Reader unterstützt **keinen Step Plan** und keine separat unqualifizierte `.ai`-Region.

Ein Step-Plan-Login-/Usageadapter ist nicht registriert. Das ist eine konkrete Erweiterungslücke trotz vorhandener Herstellerimplementierung, keine Aussage, dass nur Copy/Paste möglich wäre.

### Primärbelege, öffentlich abgerufen 25.10.2026

- [Accounts GET](https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md): `GET https://api.stepfun.com/v1/accounts`, `Authorization: Bearer YOUR_STEP_API_KEY`; `balance` = „当前账户可用余额“ (aktuell verfügbarer Kontostand); Typ prepaid/postpaid. `total_cash_balance` = kumulierte Aufladungen, `total_voucher_balance` = kumulierte geschenkte Beträge. Diese Summen sind keine ursprünglichen Spendlimits und kein aktueller Voucherrest.
- [Step Plan Preis-/Creditvertrag](https://platform.stepfun.com/docs/zh/step-plan/pricing.md): Tokenplan mit monatlicher Creditvergabe; Mini 400M, Plus 1.600M, Pro 8.000M, Max 40.000M. Quartals-/Jahreszahlung ändert nicht monatliche Ausgabe. Rest verfällt zum Periodenende, keine Übertragung. Top-ups haben eigene **30 Tage Gültigkeit**, zuerst verfallender Bucket wird zuerst verbraucht. Ein nominaler Umrechnungs-/Preissatz ersetzt keinen gemessenen Rest; Peakzeit kann anderen Verbrauch erzeugen.

GitHub-API bestätigt `stepfun-ai/Step-Code` main als **`81e79bc4b699148d6823fedde9b5e70d9a80255f`**, Commitdatum `2026-10-23T06:28:19Z`; die nachstehenden Dateien wurden am 25.10 aktuell gelesen:

- [Loginclient](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/login-client.ts): „Cloud-polled CLI login“; Prefix `/api/stdhttp/v1/cli-login`, öffentlicher `flow_id` im Browser, geheimer `poll_token` nur im Authorizationheader; fertige Antwort enthält `credential.api_key`, optional UID und zusätzliche Accountsession. „Terminal metadata for server audit logs. Never used for authentication.“ Name/version/platform sind Auditmetadaten, keine bewiesene Thirdpartyfreigabe. Die Antwort liefert einen API-Key; ob dabei stets **ein neuer Key angelegt** oder ein vorhandener ausgegeben wird, ist aus dem gelesenen Wirevertrag **unknown**.
- [Accountusagecontroller](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/coding-agent/src/step/usage/account.ts): nur persönliches Planlogin; `STEP_API_KEY`, expliziter Gateway-/Modellkey, Platform- und unbekannte Profile sind ausgeschlossen. Passender regionaler Usagepfad endet auf `/step_plan/v1/usage`; Schema1 validiert `used_percent`, `resets_at_ms` sowie getrennte Creditbuckets.
- [Usage-Doku](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/coding-agent/docs/account-usage.md): „Account queries the personal Step Plan usage endpoint with the stored CN/Global Plan credential“; CODING zeigt 5h-/Wochen-Restprozente und Reset, TOKEN getrennte Subscription-/Topup-Buckets mit Reset/Expiry. Credits bleiben exakte Dezimalstrings. „The backend must provide schema 1 ... before Account is available. A 404 explains that the endpoint is not deployed.“ Das Backenddeployment wurde hier **nicht** live geprüft; weder „deployed“ noch „undeployed“ behaupten. Overview/Status/History der CLI sind lokale Aktivität und kein kontoweiter Quotaersatz.

### Konkreter Einrichtungsweg / Rechte

PAYG heute: persönlichen `.com`-Plattformkey einmal einfügen → derselbe Key liest Accountbalance, keine extra Konto-ID und kein separater Quota-/Managementkey. Kein öffentlich belegtes automatisches PAYG-Keybootstrap oder delegiertes PAYG-OAuth in den geprüften Quellen. Bei unklarem Org-/Projektscope unbekannt anzeigen; nicht anhand von Keyform persönlich raten. Die Accountreferenz enthält keine periodische Reset-/Freegrantstruktur. Gratisvoucher, persönlicher Geldsaldo und Plancredits bleiben getrennte Produkte.

Step Plan als qualifizierbarer Weg: Produkt/Region bestimmen → offizieller Cloud-Polling-Browserlogin → Login-Key und optionale UID automatisch übernehmen → genau persönlichen Planusageendpoint lesen → Bucket-/Fenster-/Ablaufdaten anzeigen. Keine vom Nutzer kopierte Konto-ID oder eigene OAuthclient-ID ist im Herstellerwirevertrag verlangt; keinen PKCE-/Clientregistrierungsblocker erfinden. Eigener lain-Clientvertrag oder sicherer offizieller CLI-Handoff bleibt allerdings **unbelegt**; öffentliche Firstpartyquelle erlaubt noch keine fremde Clientverteilung. Ein unterstützter privater CLI-Tokenexport/-SDK-Handoff wurde hier nicht qualifiziert. Zusätzliche Access-/Refresh-/Device-/Appdaten der Accountsession nicht vorsorglich speichern: der belegte Usagepfad benutzt den Planlogin-Key. Kein read-only Plan-Keyscope belegt; der Key bleibt inferenzfähig.

Reconnect muss Region, Loginherkunft und bestätigte UID/Accountbindung erhalten; ein manuell eingefügter Key darf nicht als `personal-plan-login` gelten. Bis zur Qualifikation bleibt Planinferenz per vorhandenem Key plus ehrliche unknown-/Consoleanzeige. Kein automatischer Wechsel zu PAYG oder Topup bei erschöpften Credits.
