# OAuth- und Loginübersicht der Provider

Stand: 26.10.2026. Inventar der gelesenen Arbeitskopie bei HEAD
`fef478f601f46b315939fa53f02caa9791086f1d` einschließlich vorhandenem WIP.
**Implementiert, im Katalog angeboten, ausgeliefert und real am Provider geprüft
sind verschiedene Zustände.** Diese Übersicht prüft Source und dokumentierte
Nachweise, nicht die aktuelle private Installation. Kein Update vorgesehen.

## Einstieg: Welche Loginoptionen gibt es tatsächlich?

I = Inferenz, M = Modelle, Q = Quota/Usage; Q verspricht keinen Geldsaldo.
„Angeboten“ bezeichnet den Sourcekatalog, keine in dieser Recherche bestätigte
Anbieteranmeldung. API-Keys bleiben als eigener Verbindungsweg erhalten.

| Provider / Methode | Aktueller tatsächlicher Weg | Zweck / Erneuerung | Was die nächste Umsetzung oder Abnahme benötigt |
| --- | --- | --- | --- |
| Codex `codex-owned` | Eigener Browserlogin über vorhandenen Gateway/Core | I/Q; Core besitzt den Grant | Vorhandenen Weg erhalten. Separater Standalone-Monitor braucht isolierten offiziellen app-server und eigenen Lifecycle-Nachweis. |
| OpenRouter `browser` | Implementiertes öffentliches S256-PKCE → eigener API-Key | I/M/Q; statischer Key, kein OAuthrefresh | Reale Callback-/Key-/Reconnect-Abnahme; keine eigene Clientregistrierung nötig. |
| Kilo `device` | Implementierter persönlicher Device-Keyflow | I/M/Q; `static-key`, bei Ablehnung Reconnect | Echter neuer Login, Identität und Transport/Quota prüfen; kein Client-ID-Setup. |
| Kimi `kimi-device` | Implementierter Deviceflow, globale/CN-Region ausdrücklich wählen | **Q allein**; eigener Hostowner `kimi-oauth` | Echter Login, Neustart und Refresh samt `/me`-/Usage-Bindung; bestehende Inferenzkeys bleiben separat. Keine zusätzliche Registrierung im akzeptierten Toolkitvertrag. |
| NanoGPT `browser` | Implementierter Shortcut-PKCE → appbezogener Inferenzkey | I/M; statischer Key, kein Refreshowner | Echte Ausgabe-/Reconnect-Abnahme. Q derzeit separat per Managementtoken; Same-Key-Q ist ein neues Paket. Keine manuelle Clientregistrierung für Shortcut. |
| Google `official-cli` | Implementierter Login mit offizieller gcloud 584.0.0 in eigenem privaten Profil; lokal installationsabhängig | Q allein; CLI hält Refreshgrant, Hostowner `google-gcloud` projiziert Access | Passende gepinnte CLI und echte Login-/Projekt-/Renewal-/Monitoring-Abnahme. Keine eigene OAuthregistrierung für delegierten CLIweg. |
| Google / Cloudflare `browser` | OAuthadapter implementiert, **Maintainergate**, keine nachgewiesene eigene Registrierung | Q allein; `google-oauth` / `cloudflare-oauth` | Eigene Clients, akzeptierte Scopes/Callbacks und echte Refresh-/Ressourcen-/Readabnahme; Details unten. |
| Poe `browser` | PKCE-Keyadapter implementiert, **Registrierungs-/Callbackgate** | I/M/Q; ausgegebener API-Key, ggf. Ablauf, kein Refreshgrant | Eigener Poe-Client und exakter Releasecallback, echte Key-/Reconnect-Abnahme. |
| Requesty `official-cli` | Allgemeiner Produktadapter **unavailable**; eine dokumentierte isolierte offizielle CLI-Anmeldung mit privatem Keyexport war erfolgreich | Übernommener statischer Key: I/M und Self-Q; kein lain-OAuthrefresh | Bewährten Operator als allgemeinen isolierten Adapter samt Recovery/Identitätsbindung produktisieren. Einzelfall aktiviert keinen Button. |
| Fireworks `official-cli` | Konkreter Adapter implementiert, **produktiv hart gesperrt**, auch mit passender Binary | Geplant I/M/Q nach offizieller CLI-Anmeldung und sichtbarer neuer Keyanlage; statischer Key | firectl 1.8.19 bindet `:18000` an alle Interfaces. Callbackisolation zuerst lösen und prüfen, danach echte SSO-/Account-/Keyabnahme. |
| MiMo `console-browser` | Eigene Consolesitzung implementiert; Linux/Browser/Desktop-Gate; **kein OAuthadapter** | Q allein; browsergebundene Sitzung, Reconnect nach Schließen/Neustart | Authentifizierte UID-/Regions-/Individual-Readabnahme fehlt; keine neue Browserautomation in diesem Vorhaben. |
| Codex / Google Snapshot | Bewusster externer Access-Token-Snapshot mit Identität/Ressource und Ablauf | Q allein; **kein Refresh oder Revoke durch lain** | Nach Ablauf erneute bewusste Übergabe; niemals zu einem eigenen Grant umetikettieren. |

**Reale Nachweise:** Der [OAuth-Lieferbericht](oauth-login-implementation-2026-10-25.md)
belegt Kilo/Kimi-Paketierung, native Offlineabnahme und damalige Installation,
ausdrücklich keinen neuen echten Providerlogin/Refresh. Er dokumentiert separat
den erfolgreichen Requesty-CLIlogin und einen automatischen Self-Read, keine
Inferenz und keinen Organisationswalletsaldo. [EC1–EC4](evaluation-account-connections-2026-10-26.md)
belegen NanoGPT/DeepSeek-Source und Paketabnahme, aber keine dort abgeschlossene
Installation/Anbieteranmeldung; Fireworks blieb gesperrt. Der
[gcloud-Nachweis](account-connect-qualification/cloud/google-gcloud-2026-10-25.md)
ist eine Source-/Offlinequalifikation, kein echter Login-/Projektread.
Der [native OpenAI-Usage-Nachweis](native-openai-usage-delivery-2026-10-26.md)
belegt einen separaten nativen Codexlogin samt Usage; er qualifiziert nicht
nachträglich einen neuen lain-Standalone-Owner.

## Maßgebliche Implementierung und Grenzen

Die Wahrheit über angebotene Methoden steht in
[`ConnectionMethodCatalog.cs`](../src/Lain.Host/Accounts/ConnectionMethodCatalog.cs),
die tatsächlich registrierten Adapter/Refreshowner in
[`Program.cs`](../src/Lain.Host/Program.cs). Historische
[Account-Connect-Pakete](account-connect-work-packages/README.md) und ihre
[Provider-Matrix](account-connect-work-packages/provider-map.md) beschreiben
teilweise frühere Planungsstände. Insbesondere „Kilo Device später“, „Kimi
unqualifiziert“ und „NanoGPT-Connector fehlt“ sind als heutiges Inventar überholt.

| Codebeleg relativ zu `src/Lain.Host/` | Einordnung |
| --- | --- |
| `Accounts/CodexOwnedLoginAdapter.cs`, `HttpCodexOwnedLoginBackend.cs` | Bestehender Corelogin; kein allgemeiner neuer Hostrefreshowner. Coreerneuerung aktiver angewandter Owned-Bindungen ist kein Beleg für routeunabhängiges Monitoring. |
| `Accounts/PublicLogin/PublicBrowserLoginAdapter.cs`, `OpenRouterLoginAdapter.cs`, `NanoGptLoginAdapter.cs`, `PoeLoginAdapter.cs` | Gemeinsamer Browser-/Callback-/Checkpoint-Lifecycle; OAuth als Keybootstrap. Keine erfundenen Refreshgrants. NanoGPT bietet im Katalog nur I/M. |
| `Accounts/Kilo/KiloDeviceLoginAdapter.cs`, `KiloTransportHandoff.cs` | Persönlicher Device-Key, Identitätsprüfung und bestandserhaltender Reconnect; Organisationskonten sind nicht pauschal unterstützt. |
| `Accounts/Managed/KimiDeviceLoginAdapter.cs`, `KimiDeviceProtocol.cs` | Echter registrierter Deviceadapter und `ICredentialOwner`; allein Q. Die alte Kimi-Platzhalterklasse ist kein Gegenbeleg: der Katalog filtert deren Eintrag heraus. |
| `Accounts/Cloud/CloudOAuthAdapter.cs`, `CloudMaintainerConfiguration.cs` | Vollständiger Code-/Refreshpfad hinter eigener qualifizierter Registrierung; `Program.cs` nimmt nur `Qualified`-Clients auf. |
| `Accounts/Cloud/GoogleGcloudLoginAdapter.cs`, `GoogleGcloudProcess.cs` | Eigener isolierter offizieller CLI-Owner. Keine Übernahme fremder gcloud-/ADC-Stores; kein Export des CLIrefreshsecrets in einen zweiten Owner. Grant ist breit, nicht „read-only OAuth“. |
| `Accounts/CliConnections/RequestyCliLoginAdapter.cs`, `CommandCodeCliLoginAdapter.cs`, `MistralVibeCliLoginAdapter.cs` | Gesperrte Klassen mit begrenzten Hilfsverträgen, keine freigegebenen Loginimplementierungen. |
| `Accounts/CliConnections/AlibabaCliLoginAdapter.cs`, `QualifiedCliLoginAdapter.cs` | Vorbereiteter ausführbarer CLIpfad/Profilreader, aber weder im aktiven Adapterarray noch im Methodenkatalog freigeschaltet. Kein fertiger OAuthmonitor. |
| `Accounts/CliConnections/FireworksCliLoginAdapter.cs`, `FireworksCliQualification.cs` | Registrierter, jedoch konkret blockierter Adapter. `Validate()` verweigert den Produktweg; ein Hash oder Availabilityflag beseitigt den Callbackblocker nicht. |
| `Accounts/Managed/ManagedLoginQualification.cs`, `CodexStandaloneConnectAdapter.cs`, `ZaiConnectAdapter.cs` | Bewusst unbrauchbare Kandidaten/Protokollbausteine; kein Standalone-Codex- oder Z.ai-Produktlogin. |
| `Accounts/Monitoring/MonitorAccessCatalog.cs` | Separate Monitorsecrets und externe Snapshots; ihre Existenz belegt keine OAuthmethode. |

Ein statischer API-Key wird von lain gespeichert, aber nicht durch OAuthrefresh
erneuert. Ein CLIexport eines solchen Keys ist kein rotierender OAuth-Snapshot.
Bei delegierten erneuerbaren Grants besitzt die offizielle CLI den Refresh;
der Host ruft nur den qualifizierten Helper im eigenen Profil auf. Externe
Snapshots bleiben vollständig unter externer Refreshhoheit.

## Vollständige Providerabdeckung

Abdeckung: 22 aktuelle Katalogdateien plus die fünf zusätzlichen
Provider Codex, Command Code, Kimi, MiMo und Z.ai = **27 Provider**.
Jede Zeile beschreibt die heutige Sourcefähigkeit, keine pauschale Liveabnahme.
Für jeden gewöhnlichen Key gilt: kein OAuthrefresh; Ersatz/Reconnect über den
Anbieter. Zusätzliche OAuthregistrierung ist für Keyeingabe nicht erforderlich.
„Nicht belegt“ meint den geprüften Scope, keine weltweite Nichtexistenz.

| Provider-ID | Loginstatus neben API-Key | Inferenz / Monitoring im heutigen Scope |
| --- | --- | --- |
| `alibaba-model-studio` | Offizielle CLI vorbereitet, gesperrt; kein eigener OAuthclient | Regionaler Key für I/M; Console-/Coding-Plan-Monitoring kein allgemein freigegebener CLIlogin. |
| `cloudflare-workers-ai` | Eigener Browser-OAuthadapter hinter Registrierungsgate | I/M über Accounttoken; Q mit Analyticsrecht/Dataset separat. OAuth derzeit Q, keine qualifizierte OAuth-Inferenz. Reale Datasetdiagnose ergab keine nutzbare Neuronzahl. |
| `codex` | Core-eigener Browserlogin; externer Snapshot; Standalone nur Kandidat | Core I/Q; Snapshot Q und Ablauf; routeunabhängige Erneuerung offen. |
| `cohere` | Key; Consolemethode unavailable | I/M; Evaluation/Trial/Production getrennt, kein qualifizierter OAuth-/Consolequotamonitor. |
| `commandcode` | Key; offizieller CLIadapter unavailable | I/M und direkter Keyquotareader; Login-/Cache-/Exportvertrag nicht produktqualifiziert. |
| `deepseek` | Key; kein OAuthadapter | I/M und Same-Key-Balance implementiert; native Währungen, keine Prozentquote. EC-Abnahme ist kein echter Saldoabruf. |
| `fireworks` | Key; offizieller SSO-CLIadapter blockiert | I/M; Q mit berechtigtem Key und Account-ID. Ressourcenquotas sind kein Guthaben und kein Fire Pass. |
| `gemini-api` | Key; gcloud implementiert; eigener Browser hinter Registrierung; externer Snapshot | I/M per Key; Googleprojekt-Q getrennt. OAuth-I für den konkreten Transport bleibt offen. Projektusage ist keine garantierte Restkapazität. |
| `groq` | Key; kein OAuthadapter | I/M; passive Ratenheader, optional berechtigte Enterprise-Metrics. Weder kontoweiter Geldsaldo noch allgemeine Restquote zugesagt. |
| `inception` | Key; kein OAuthadapter | I/M und lokal beobachtete Nutzung; kein qualifizierter autonomer Restquotaread. |
| `kilo` | Implementierter Device-Keyflow | I/M/Q mit persönlichem Zugang; Wallet und Pass getrennt. Free-Modellfilter ist kein eigenes Loginprodukt. |
| `kimi` | Implementierter eigener Device-OAuthowner | OAuth Q allein, Coding-Key für I/M und vorhandenen Keyreader erhalten; Moonshot Platform ist anderer Scope. |
| `mistral` | Key; Vibe-CLIadapter unavailable | I/M; Vibeplan/Studio und Enterprise-Adminmonitor sind getrennte Zugänge, kein bestätigter universeller OAuthquotareader. |
| `nanogpt` | Implementierter PKCE-Keybootstrap | I/M; heutiger Q-Reader verlangt getrennten `usage:read`-Managementtoken ohne Accountselector. Öffentlich belegter Same-Key-Read noch nicht implementiert. |
| `nebius` | Token-Factory-Key; kein OAuthadapter | I/M, passive Ratenwerte soweit angebunden; Cloud-IAM/Dedicated-Observability kein allgemeiner Serverlesssaldo. |
| `nvidia-build` | Public-Endpoint-Key; kein OAuthadapter | I/M; kein qualifizierter Kontorestreader. NGC-/Enterprise-Login ist kein Ersatzbeleg. |
| `opencode-go` | Workspace-Key; kein lain-OAuthadapter | I/M; Go/Plus/PAYG getrennt. MCP-Client-OAuth belegt keinen Go-Quotatool oder Inferenzgrant. |
| `openrouter` | Implementierter öffentlicher PKCE-Keybootstrap | I/M/Q; Keycap/Keyusage sind kein Accountwallet. Managementrechte separat. |
| `poe` | Implementierter PKCE-Keyadapter hinter Maintainergate | I/M/Q über ausgegebenen Key; Punkte, Ablauf und Reset nur nach tatsächlichem Schema. |
| `requesty` | Produkt-CLI unavailable; erfolgreicher offizieller Einzellogin dokumentiert | I/M per übernommenem Key, Self-Q belegt; Orgbalance/-rechte nicht daraus ableiten. |
| `sambanova` | Key; Consolemethode unavailable | I/M; gebundene Consolecredits/-session nicht qualifiziert, kein neuer OAuthauftrag aus Portal-SSO. |
| `stepfun` | PAYG-Key; Step-Plan-Login nur recherchierter Herstellerpfad | I/M; bestehender Q-Reader nur persönliches PAYG auf qualifizierter Basis, nicht Step Plan/Org/Projekt. |
| `synthetic` | Key; kein OAuthadapter | I/M und Same-Key-Aboquota; PAYG-Geldsaldo kein Teil derselben Messgröße. |
| `upstage` | Key; Consolemethode unavailable | I/M; Org-/Consoleusage und Credits ohne qualifizierte Sessionbrücke nicht als OAuth-Q anbieten. |
| `xai` | API-Key; kein API-Team-OAuthadapter | I/M mit Teamdiscovery; Management-/Billingzugang separat. Grok-Consumer/Build-Sitzung ist anderes Produkt. |
| `xiaomi-mimo` | Key plus implementierte eigene Consolesitzung, kein OAuth | I/M per Key; Q für gebundene Individual-Session vorbereitet, echter authentifizierter Read offen. Team/PAYG nicht gleichsetzen. |
| `zai` | Coding-Key; Managed-Login unavailable | I/M/Q mit bestehendem internationalen Key; unabhängiger Client-/nicht anlegender CLIvertrag fehlt. PAT-Projektion/Discovery sind kein fertiger OAuthowner. |

## Eigene Registrierungen: konkret nötig oder nicht?

[Maintainerablauf](oauth-maintainer-registration-2026-10-25.md) und
[`scripts/prepare-oauth-maintainer.py`](../scripts/prepare-oauth-maintainer.py)
sind offline vorbereitet; Staging belässt alle Qualifikationsgates auf false.
Keine Zugangsdaten oder Maintainerkonfiguration wurden hier gelesen.

| Weg | Registrierungsarbeit und verbleibendes Gate |
| --- | --- |
| Google direkt | Eigener Google-Auth-Platform-**Desktop**-Client, Branding/Consent, ggf. Produktions-/Scopereview; S256/IPv4-Loopback und echte Erneuerung prüfen. Aktueller Adapter verlangt `cloud-platform`, keinen schmalen Monitoringgrant. IAM/APIs/Billing werden nicht automatisch eingerichtet. |
| Cloudflare direkt | Eigener accountgebundener Public Client (`none`, S256), reale OAuth-Scope-IDs und `offline_access`; feste Callbackregistrierung, ggf. Domainverifikation/öffentliche Freigabe. Registrierung erfordert Verwaltungsrecht; keine anonyme DCR. Neurondatenquelle bleibt eigenes Gate. |
| Poe | Eigene App unter `poe.com/api/clients`; exakten Releasecallback registrieren/prüfen. Localhost-Ausnahme für Entwicklung qualifiziert nicht beliebige Release-URIs. `apikey:create` liefert einen Key, keinen Refreshgrant. |
| OpenRouter / NanoGPT Shortcut / Kilo | Öffentlicher Keybootstrap ohne von Nutzern zu beschaffende Client-ID. NanoGPT Standard-OAuth unterstützt daneben Registrierung; der jetzige Shortcut braucht diese manuelle Vorarbeit nicht. |
| Kimi | Projekt akzeptiert den veröffentlichten Toolkitclient mit ehrlicher lain-Hostidentität; keine zusätzliche eigene Registrierung als unbelegte Pflicht einführen. Individuelle Herstellerfreigabe/npm-Veröffentlichung wird nicht behauptet. |
| Offizielle gcloud-/Requesty-/Fireworks-CLI | Originaltool führt seinen eigenen Flow aus. Eigene lain-Clientregistrierung ist dafür nicht nötig; Isolation, private Übergabe und Produkt-/Identitätsbindung bleiben nötig. Fremde Client-ID nicht zu eigenem Flow umbauen. |
| Z.ai / Step Plan | Keine belegte allgemeine lain-Registrierungsanleitung. Weder Registrierung als sicher notwendig behaupten noch offizielle Appidentität kopieren. Erst unterstützten externen Vertrag bzw. echten CLI-Handoff nachweisen. |
| OpenAI SIWC OSS | Separate dynamische Registrierung je Nutzer/Workspace und persistente Host-ID im dokumentierten Flow; keine Übernahme des Codex-Firstpartyclients. Nur als neue Integration prüfen. |

Z.ai bleibt konkret begrenzt: Der [Source-Handoff](providers/zai-managed-login-handoff-2026-10-25.md)
belegt ZCodes eigenen Ablauf, aber keinen unabhängigen lain-Clientvertrag.
Der offizielle Bootstrap kann vor erfolgreicher Rückgabe einen Key anlegen;
`--no-browser` verhindert das nicht. Ein nicht anlegender Monitor benötigt einen
anderen nachgewiesenen Vertrag. Endliche Sitzung plus Reconnect wäre zulässig;
ein erfundener dauerhafter OAuthrefresh ist keine zusätzliche Mindestanforderung.

## Aktuelle offizielle Webprüfung und echte zusätzliche Chancen

Die folgenden offiziellen Seiten wurden am 26.10.2026 tatsächlich geöffnet.
Sie belegen öffentliche Verträge, keine funktionierende lain-Installation oder
Liveanmeldung. Suchtreffer allein wurden nicht als Implementierungsbeleg benutzt.

| Primärquelle | Verifizierte Aussage / Folgerung |
| --- | --- |
| [OpenRouter OAuth](https://openrouter.ai/docs/guides/overview/auth/oauth) | Öffentlicher PKCE-Keybootstrap; bestehender Adapter ist die passende Basis, kein neuer Refreshowner nötig. |
| [NanoGPT PKCE](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce) | Shortcut und Standard-OAuth liefern appbezogene API-Keys; Shortcut erzeugt/wiederverwendet serverseitig den Callbackclient. Bestehender Adapter deckt Shortcut ab. |
| [NanoGPT CLI Device Login](https://docs.nano-gpt.com/integrations/cli-login) | Öffentlicher Start-/Poll-Vertrag mit eigener `client_name`, geheimem Gerätecode und sichtbarem Nutzercode; Ergebnis API-Key. **Zusätzlicher konkret implementierbarer Deviceweg**, bisher kein lain-Adapter. Kein Refreshgrant aus dem Namen „Device“ ableiten. |
| [NanoGPT Subscription Usage](https://docs.nano-gpt.com/api-reference/endpoint/subscription-usage) | Dokumentierter Inferenzkey-Read auf `/api/subscription/v1/usage`, getrennt vom Managementpfad. **Same-Key-Monitoring ist eine echte zusätzliche Chance**, heute durch den Management-only-Reader nicht abgedeckt. |
| [Google Desktop OAuth](https://developers.google.com/identity/protocols/oauth2/native-app) | Eigener Desktopclient, PKCE und Loopback sind öffentlich dokumentiert; bestehendes Registrierungspaket fortsetzen. |
| [Cloudflare Client anlegen](https://developers.cloudflare.com/fundamentals/oauth/create-an-oauth-client/) | Accountrolle, Clientkonfiguration und Scopes sind nötig. Unterstützte Registrierung vorhanden, Reportingrechte damit nicht bewiesen. |
| [Poe Sign-in](https://creator.poe.com/docs/external-applications/sign-in-with-poe) | Eigener Client und S256-Codeexchange zu API-Key dokumentiert; kein pauschaler Refresh. |
| [Fireworks CLI Authentication](https://docs.fireworks.ai/tools-sdks/firectl/commands/authentication) | `firectl signin` für Google SSO, mit Accountargument für Custom SSO. Offizieller Weg existiert; Loopbackisolation ist dadurch nicht bewiesen. |
| [Requesty CLI README, gepinnte Quelle](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/README.md) | Offizieller Login und `auth token`-Integration; Ergebnis langlebiger Produktkey statt eigener lain-Refreshlogik. |
| [Kimi Toolkit README, Pin](https://github.com/MoonshotAI/kimi-code/blob/743f140978bddaf8b6aa524765fcd578f7c22fc9/packages/oauth/README.md) | Hersteller stellt OAuth-Toolkit im öffentlichen MIT-Quellbaum bereit. Details/akzeptierte Cliententscheidung sind im lokalen [Kimi-Nachweis](providers/kimi-device-login-2026-10-25.md) gepinnt; kurze README allein ist kein Livezulassungsbeleg. |
| [OpenAI Codex Auth](https://developers.openai.com/codex/auth/) | Offizielle CLI unterstützt Device-Code-Login; das qualifiziert keinen zusätzlichen lain-Core-Deviceadapter. |
| [OpenAI app-server](https://developers.openai.com/codex/app-server/) | Dokumentiertes Account-/Rate-Limit-RPC erlaubt einen begrenzten Standalone-Monitorversuch; stabiler Accountscope und isolierte Erneuerung bleiben lokale Nachweise. |
| [OpenAI SIWC für OSS/lokale Apps](https://developers.openai.com/siwc/token-sharing-open-source) | Dynamische Nutzer-/Workspaceregistrierung, stabile eigene Host-ID und ChatGPT-Plan-Inferenz über Responses sind dokumentiert. **Neue Integrationschance**, kein belegter Ersatz für den heutigen WHAM-Reader. |
| [OpenCode Console API](https://opencode.ai/v2/docs/console/api/) | Workspacegebundene API-Keys und getrennte Usage-/Budgetrechte sind dokumentiert. Allgemeines [MCP-OAuth im Client](https://dev.opencode.ai/docs/mcp-servers/) beweist keinen geeigneten Console-Quotatool. |

**Recherchegrenze:** Step Codes konkrete Dokumentationsseite war in dieser Runde
nicht abrufbar. Der ältere [Herstellerquellenbericht](account-connect-research/xai-stepfun-poe-synthetic.md)
bleibt ein datierter Kandidatenbeleg, keine neu bestätigte Deployment-/Drittclientfreigabe.
Für andere Provider wurde kein zusätzlicher allgemein nutzbarer OAuthvertrag in
diesem begrenzten Scope bestätigt. Das ist keine Behauptung, alle Herstellerwebsites
vollständig erneut untersucht zu haben. Portal-SSO, MCP-Clientfähigkeit, sichtbare
Firstparty-Client-IDs und private Browserendpunkte ersetzen keinen Produktvertrag.

## Konkrete nächste Pakete

Vorschläge für spätere Umsetzung, **kein laufender Update-/Loginauftrag**.
Vorhandene Arbeit weiterverwenden; je Paket ein eigenes überprüfbares Ergebnis.

| Priorität / Paket | Begrenztes Ergebnis | Go-/Abnahmekriterium und Stopgrenze |
| --- | --- | --- |
| 1 — Bestehende öffentliche Flows qualifizieren | OpenRouter, Kilo, Kimi und NanoGPT einzeln entlang vorhandener Adapter abnehmen | Jeweils später bewusst autorisierter Login, Identität, persistente Adoption/Neustart, Reconnect; bei Kimi zusätzlich echte Erneuerung. Keine neue Route oder Inferenz als Loginsideeffekt. Offenes Providerergebnis blockiert nur seine Methode. |
| 1 — Requesty produktisieren | Vorhandenen isolierten v0.1.16-Operator und gezielten privaten `auth token`-Export als allgemeinen Adapter integrieren | Gepinntes Artefakt, vollständige Store-/Keyringisolation, Keyanlageabsicht/Recovery, Account-/Gruppenbindung und gezielter Reconnect. Kein allgemeiner Configwizard, der andere Harnessdateien verändert; kein Orgbalanceversprechen. |
| 1 — NanoGPT Same-Key-Q | Separater Reader für den dokumentierten Inferenzkey-Subscriptionpfad, bestehender Managementmonitor bleibt | Schema-/Einheiten-/Reset-/Degradationsfixtures; Credentialauswahl strikt nach Pfad. Danach optional ein autorisierter Read. Keine Managementtokens für Inferenz; keine automatische kostenpflichtige Ausweichroute aus `routing`-Hinweisen. |
| 2 — NanoGPT Device | Neuer Start-/Poll-Keyadapter im bestehenden Loginoperation-Lifecycle | Timeout/Intervall, Cancel, Consume/Replay und Checkpoint vor Adoption; Gerätecode bleibt privat, keine doppelte Keyausgabe bei verlorener Antwort. Ergebnis zunächst I/M; Q nur nach eigener Readerqualifikation. |
| 2 — Poe Registrierung abschließen | Vorbereiteten Maintainerplan mit eigener App und exaktem Callback vervollständigen | Tatsächliche Registrierung/Callback/Keyablauf und Reconnect separat belegen. Offlineplan oder Flag allein reichen nicht. |
| 2 — Google direkter Monitor | Eigenen Desktopclient registrieren; bestehenden Cloudadapter und Discovery nutzen | Consent/Scopes, IAM/Projektbindung, Callback, Neustart, Refresh und echter Monitoringread; keine OAuth-Inferenz als Nebenpaket. Alternativ vorhandenen gcloud-Weg zuerst real qualifizieren. |
| 2 — Fireworks Callbackisolation | Ausschließlich lokale Erreichbarkeit des unveränderten offiziellen Signinlisteners trotz Wildcardbindung gewährleisten | Netz-/Callbackdesign zunächst offline mit echtem Tool ohne Login und externem Erreichbarkeitstest prüfen; erst danach getrennte SSO-/Account-/einmalige Keyausgabeabnahme. Ohne Beleg bleibt hart gesperrt. |
| 3 — Codex Standalone-Q | Isolierter offizieller app-server besitzt einen neuen eigenen Monitorgrant, dokumentiertes Rate-Limit-RPC | Ohne Core/Route: stabile Identität, Neustart, Ablauf/Erneuerung, Detach und keine fremden Caches. Bestehenden Coregrant niemals parallel refreshen. Kein privater WHAM-Neuadapter. |
| 3 — OpenAI SIWC Inferenz | Begrenzter Protokoll-/Transportversuch für OSS/lokal dokumentierte Responses-Inferenz | Eigene dynamische Registrierung/Host-ID, Token-/Scopevalidierung, ein exklusiver Owner; unterstützte Modelle/Previewgrenzen und spätere bewusste End-to-End-Abnahme. Kein Quota-/WHAM-Versprechen und kein stiller Ersatz bestehender Codexaccounts. |
| Nachrangig — Cloudflare | Registrierungsplan plus exakte Analytics-Scope-/Datasetzuordnung abschließen | Zuerst klären, ob der reale Account überhaupt eine verwertbare Neuronquelle hat. Erfolgreiches OAuth ohne Datenquelle rechtfertigt keinen verfügbaren Quota-Monitor. |
| Erkenntnispaket — Step Plan / OpenCode / Z.ai | Jeweils nur öffentlichen externen Login-/Readvertrag ermitteln und pinnen | Step: Admission/Region/Deployment; OpenCode: konkretes autorisiertes Readtool; Z.ai: eigener Client oder nicht anlegender offizieller Handoff. Ohne diese Belege keine Implementierung privater Endpunkte oder Browserautomation. |

Alibaba, Command Code und Mistral bleiben getrennte CLIqualifikationen nach
[AC-05](account-connect-work-packages/05-cli-connections.md), keine automatische
Erweiterung des Requesty-Pakets. Consolelogin für SambaNova/Upstage/Cohere und
weitere MiMo-Automation gehören nicht zu diesen OAuth-Chancen.

## Prüfumfang dieses Dokuments

Gelesen wurden Projektregeln und Architekturindex, aktuelle Adapterverdrahtung,
Methoden-/Monitorkatalog, relevante Provideradapter und die oben verlinkten
Implementierungs-, Maintainer-, Account-Connect- und Qualifikationsdokumente.
OpenAI-Aussagen wurden mit dem OpenAI-Docs-Skill und offiziellen geöffneten
Dokumentationsseiten abgeglichen. Die Webprüfung nutzte ausschließlich öffentliche
Dokumentation/Herstellerquellen; keine authentifizierten API- oder Discoveryaufrufe.

Nur diese Datei wurde für den Auftrag geschrieben. Keine Zugangsdaten gelesen,
keine CLIanmeldung, Livequota/Inferenz, Clientregistrierung, Installation,
Produktionsänderung oder Commits ausgeführt. Bereits bestehendes WIP und fremde
Recherche bleiben erhalten. Prüfung: lokale Linkziele, vollständige Providerliste
und Whitespace dieser Datei; kein Build/Testlauf für reine Dokumentation.
