# Harness-Anschlüsse

Stand 23.10.2026. lain soll für mehrere Harnesse nützlich sein. Die Anschlussarten
werden je Oberfläche unterschieden: Modellinferenz durch den Proxy, lokale
Konfiguration/Modellmenü, Account-/Usageübersicht und optionale Status-Tools.
Eine dokumentierte Schnittstelle ist noch kein bestandener Kompatibilitätstest.

| Client | Bisher belegter Anschluss | Grenze / offene Prüfung |
| --- | --- | --- |
| Codex CLI | Eigener Provider, `base_url`, Responses, Auth, Modellkatalog | Streaming, Tools, Continuation und Metadaten pro unterstütztem Build testen |
| Lokales Codex in ChatGPT/Desktop-Oberflächen | Offizielle Gateway-Doku beschreibt CLI sowie macOS-/Windows-App und lokale Konfiguration | Konkreten Modus/Build benennen; GUI übernimmt Terminal-Umgebungsvariablen nicht zwingend |
| Gewöhnlicher ChatGPT-Chat | In den gelesenen Quellen keine freie Inferenzumleitung belegt | Nicht aus lokalem Codex-Support ableiten; mögliche Tool-/Statusintegration separat beurteilen |
| Claude Code | Konfigurierbarer Gateway über `ANTHROPIC_BASE_URL`, Messages, mehrere Authverfahren; Modell-Discovery über `/v1/models` bei entsprechender Aktivierung | Streaming/Tools/Headers erhalten; Anthropic unterstützt die Umleitung auf Nicht-Claude-Modelle nicht, auch wenn andere Anbieter solche Kompatibilitätswege dokumentieren |
| Claude Desktop | Third-Party-Inference/Gateway offiziell dokumentiert; eigene Appkonfiguration und optionale Modell-Discovery | Modus, Betriebssystem und Version qualifizieren; CLI-Umgebungsvariablen konfigurieren die Desktop-App nicht automatisch |
| OpenCode | Eigene Provider, Basis-URLs, Credentials, Modellkataloge; V2 kann bestehende Provider auf Proxy-Endpunkte legen | Clientversion erkennen: V1 `provider/npm/options`, V2 `providers/package/settings`; Protokoll hängt am Providerpaket |
| Z.ai-Zugänge | Offizielle Anleitungen für Claude Code, OpenCode und Codex; Messages- und Responses-Anschlüsse | Coding-Plan-, Team- und Standard-API-Zugänge unterscheiden; unterstützte Tools teilen Planquota |

Der Wizard läuft in **lain** und erzeugt geprüfte Einstellungen für den jeweiligen
Client. Ein eigenes interaktives UI innerhalb des fremden `/model` ist damit
nicht zugesagt. Profile sollen eine gemeinsame Absicht beschreiben; Clientadapter
übersetzen sie in unterstützte Einstellungen und melden nicht abbildbare Teile.

## Offizielle Quellen

- [OpenAI: Gateway anschließen](https://learn.chatgpt.com/docs/enterprise/connect-to-a-gateway)
- [OpenAI: Gateway-Kompatibilität](https://learn.chatgpt.com/docs/enterprise/gateway-compatibility)
- [OpenAI: Modelle und Oberflächen](https://learn.chatgpt.com/docs/models)
- [OpenAI: MCP](https://learn.chatgpt.com/docs/extend/mcp)
- [Claude Code: Gateway anschließen](https://code.claude.com/docs/en/llm-gateway-connect)
- [Claude Code: Gateway-Protokoll](https://code.claude.com/docs/en/llm-gateway-protocol)
- [Claude Code: Gateway-Supportgrenzen](https://code.claude.com/docs/en/llm-gateway)
- [Claude Desktop: Gateway](https://claude.com/docs/third-party/claude-desktop/gateway)
- [Claude apps gateway](https://code.claude.com/docs/en/claude-apps-gateway)
- [OpenCode V2: Provider](https://opencode.ai/v2/docs/providers)
- [OpenCode V2: CLI-Statistik](https://opencode.ai/v2/docs/cli/commands#stats)
- [OpenCode V2: API / SessionStats](https://opencode.ai/v2/docs/api)
- [Z.ai: Claude Code](https://docs.z.ai/devpack/tool/claude),
  [OpenCode](https://docs.z.ai/devpack/tool/opencode),
  [Codex](https://docs.z.ai/devpack/tool/codex),
  [Plan-FAQ](https://docs.z.ai/devpack/faq)

MCP kann Status-/Verwaltungswerkzeuge anbinden. Das allein leitet keine
Modellinferenz durch lain. Für Hosted Work gelten andere Grenzen als für lokale
Clients; es gehört nicht zur bestätigten lokalen Startkonfiguration.

## Folgerungen für den Entwurf

Ein Clientadapter braucht neben dem Namen mindestens Version, Oberfläche,
Transport-/Toolfähigkeiten, Konfigurationsweg und Modellmenüvertrag. Responses und
Messages sind eigene Protokollverträge. Chat Completions wird für konkrete
Anschlüsse bewertet; ein „OpenAI-kompatibel“-Label garantiert keine Responses-
oder Toolfortsetzungskompatibilität.

Bei Claude Desktop sind Third-Party-Inference und MCP unterschiedliche
Integrationen. Die aktuelle Gateway-Doku umfasst bei entsprechender Aktivierung
Chat/Cowork/Code. Daraus folgt keine allgemeine Nutzbarkeit eines normalen
Claude.ai-Logins als Proxy-Upstream. Lokale MCP-Erweiterungen verbinden vom Gerät;
Remote-Connectoren können aus der Anbietercloud kommen und erreichen deshalb
nicht automatisch den lokalen lain-Dienst.

### Claude Code: vorhandenes Abo durch einen Proxy führen

Die am 23.10.2026 erneut gelesene offizielle Gateway-Doku unterscheidet zwei
Credentialwege ausdrücklich: Nur `ANTHROPIC_BASE_URL` zu ändern lässt einen
gespeicherten Claude.ai-Login aktiv; die Anfragen laufen dann mit dessen
Aboabrechnung durch den Proxy. Dieser muss die erforderliche OAuth-Capability
im `anthropic-beta`-Header an Anthropic erhalten. Ein zusätzlich konfigurierter
Gateway-Token/API-Key oder `apiKeyHelper` ersetzt dagegen die Clientauthentifizierung;
die Abrechnung richtet sich nach dem vom Gateway verwendeten Upstreamzugang.

Quelle: [Subscriptions and gateways](https://code.claude.com/docs/en/llm-gateway#subscriptions-and-gateways).
Für lain ist damit ein Durchleitungsmodus für Claude Code dokumentiert, ohne
daraus die freie Wiederverwendung dieses Logins in anderen Clients abzuleiten.
Persönliche Abodurchleitung und verwaltete Ersatzrouten brauchen einen eigenen
Kompatibilitätstest; OAuthdaten dürfen nicht an einen Ersatzanbieter geraten.

Claude Desktop wird separat über **Help → Troubleshooting → Enable Developer
Mode → Developer → Configure Third-Party Inference** eingerichtet. Die
entsprechende Gatewaykonfiguration verwendet Messages mit Streaming/Tools;
`GET /v1/models` ist optional, sonst wird die Modellauswahl explizit konfiguriert.
Diese Desktopkonfiguration übernimmt nicht automatisch die Claude-Code-
Umgebungsvariablen oder dessen Abodurchleitungsweg.

Z.ai dokumentiert Plananschlüsse für die genannten Harnesse, einschließlich
Responses für Codex. Planart und erlaubte Verwendung gehören in die
Accountfähigkeiten. Ein eigener lain-Konfigurationschat darf den Coding-Plan
nicht allein aufgrund dieser Harness-Anleitungen als allgemeinen Chatdienst
voraussetzen.

Für eine spätere Supportliste ist zwischen **herstellerdokumentiert**,
**lokal getestet** und **experimentell** zu unterscheiden. In dieser Runde wurden
die öffentlichen Dokumentationswege geprüft; keine neuen Client-/Provider-
Kompatibilitätsproben oder Konfigurationsänderungen durchgeführt.

## Claude: enthaltene API-Credits und getrennte Budgets

Offiziell geprüft am **23.10.2026**; keine Aussage über Freischaltung oder
Restguthaben eines konkret verbundenen Accounts.

| Tarif | Monatliche API-Credits |
| --- | --- |
| Max 5x | 100 USD |
| Max 20x | 200 USD |
| Team | 20 USD je Standard-/100 USD je Premium-Sitz; gemeinsamer Pool höchstens 500 USD |
| Free, Pro, Enterprise | Nicht für dieses Programm berechtigt |

Aktivierung: unter Claude-Billing eine Console-Organisation verknüpfen;
Neuabonnenten nach sieben Tagen, gegebenenfalls späterer Rollout.
Quelle: [Beträge, Berechtigung und Aktivierung](https://support.claude.com/en/articles/17154008-monthly-api-credits-for-max-and-team-plans).

Credits gelten für Claude API, Managed Agents, Agent SDK und Playground;
interaktives Claude Code und App-Extra-Usage sind ausgeschlossen. Sie verfallen
zum Zyklusende, werden vor gekauftem Guthaben verbraucht und gelten gemeinsam
für Schlüssel/Workspaces der verknüpften Organisation. API-Spend-/Rate-Limits
bleiben bestehen. Quelle: [Platform-Bedingungen](https://platform.claude.com/docs/en/about-claude/api-credits-for-subscribers).

Die Help-Center-FAQ schließt selbst gestartetes `claude -p`/Agent SDK mit dem
Console-API-Key ein; Starts durch IDE, Desktop oder GitHub Action sind dagegen
ausgeschlossen. Ein Abo-OAuth-Token ist kein API-Guthaben.

Für lain folgen daraus getrennte Darstellungen von Aboquota, enthaltenen Credits
und gekauftem Guthaben samt erlaubtem Nutzungszweck. Eine eigene Recherchefunktion
ist ein möglicher API-Anwendungsfall. Interaktiven Claude-Code-Fallback nicht als
durch diese Credits gedeckt darstellen; konkrete Route und Client bleiben zu prüfen.

## Konkreter Ansatz für OpenCode

OpenCode bietet bereits `opencode stats` mit Zeitraumfilter, Modellaufteilung,
Token-/Kostendetails und JSON-Ausgabe; Accountwechsel existiert ebenfalls.
Die experimentelle V2-API beschreibt Sitzungs-/Subagentdaten, Tokens, Kosten,
Modelle und Werkzeugstatistik. Tagesaktivität nach Schritten ist dort belegt,
eine tägliche Token-/Kostenmatrix lässt sich daraus nicht einfach ableiten.

Der Mehrwertvorschlag für lain liegt in Wizard/portablen Profilen und der
gemeinsamen Sicht über mehrere Harnesse, tatsächlich gewählte Upstreams, Accounts,
Restkontingente und Resetzeiten. Eine allgemeine Überlegenheit gegenüber OpenCodes
Statistik wird nicht behauptet. Native Sitzungsdaten wären eine zusätzliche
Quelle; dieselbe Nutzung darf nicht nochmals zum Proxyverbrauch addiert werden.

## Skills, Projektregeln und Workerprofile

Offizielle Dokumentation geprüft am 23.10.2026; noch keine Export-/Laufzeittests
für lain. Gemeinsame Skill-Inhalte können `SKILL.md` mit `name`, `description`
und relativen Ressourcen verwenden. Agentdefinitionen und dauerhaft geladene
Projektregeln benötigen dagegen einen Anschluss je Harness.

| Harness | Dokumentierter Anschluss | Wesentliche Grenze |
| --- | --- | --- |
| Lokales Codex | `.agents/skills`, `AGENTS.md`, `.codex/agents/*.toml`; Modell/Reasoning je Custom Agent; `agents.max_concurrent_threads_per_session` | Unteragenten erben unter anderem Provider und Berechtigungen; kein eigener Providerwechsel durch die Agentdatei. Die Obergrenze zählt offene erzeugte Threads ohne Hauptagent. |
| Claude Code | Skills, `CLAUDE.md`, `.claude/agents/*.md`, Modell-/Toolkonfiguration | Forks, Hooks und weitere Skillfelder sind Claude-spezifisch; das dokumentierte Concurrent-Limit hat Ausnahmen. Kontextvererbung hängt vom Agenttyp ab. |
| OpenCode V2 | Skills, Projektanweisungen, `.opencode/agents/*.md` oder JSONC-Agentdefinitionen; `provider/model#variant` | Eigene Berechtigungs-/Vererbungssemantik; in den geprüften Skill-/Agentseiten ist kein gleichwertiges globales Workerlimit belegt. V1 separat behandeln. |

Codex-Quellen: [Skills](https://learn.chatgpt.com/docs/build-skills),
[Projektregeln](https://learn.chatgpt.com/docs/agent-configuration/agents-md),
[Subagents](https://learn.chatgpt.com/docs/agent-configuration/subagents).
Weitere Quellen: [Claude Skills](https://code.claude.com/docs/en/skills),
[Claude Subagents](https://code.claude.com/docs/en/sub-agents),
[OpenCode V2 Skills](https://opencode.ai/v2/docs/skills),
[OpenCode V2 Agents](https://opencode.ai/v2/docs/agents).

Ein installierter Skill garantiert weder seinen Aufruf noch eine Workerzahl.
Bibliothekskompatibilität, erfolgreiche Einrichtung und beobachtete Ausführung
sind unterschiedliche Nachweise. Desktop-/Hosted-Oberflächen erben diese lokalen
Dateiverträge nicht automatisch. Eine Proxyroute schafft allein keine Workersteuerung.

## Approver und automatische Freigaben

Geprüft am 23.10.2026, ohne Konfigurationsänderung oder Ausführungstest.

| Harness | Belegt | Noch offen für lain |
| --- | --- | --- |
| Codex / lokale Desktop-Integration | „Approve for me“ nutzt einen eigenen Reviewer; Policy und zusätzliche Regeln sind konfigurierbar. `PermissionRequest`-Hooks können erlauben, ablehnen oder an den normalen Freigabeweg abgeben. | In der geprüften Konfigurationsreferenz keine freie Modellwahl für den eingebauten Approver belegt; eigener Modellaufruf über Hook wäre eine separate Integration. Zusammenspiel mit Auto-Review/verwalteten Vorgaben testen. |
| Claude Code | Synchroner `PermissionRequest` mit Command-/HTTP-Hook liefert echte Freigabeentscheidungen. | Modellwechsel des eingebauten Auto-Mode-Classifiers nicht belegt; eigenes Modell über Adapter aufrufen. Prompt-/Agent-Hooks sind hier kein gleichwertiger Ersatz. |
| OpenCode V2 | `ctx.permission.hook("evaluate")` kann ein explizit gewähltes Modell aufrufen und `allow`/`ask`/`deny` setzen. Konfiguriertes `deny` bleibt endgültig. | Plugin entwickeln/qualifizieren; V1 benötigt eigene Prüfung. Kein belegter allgemeiner nativer Approver-Modellschalter. |

Bei Codex bezeichnet `review_model` das Modell für **`/review`**, nicht den
Freigabe-Reviewer. Quellen: [Auto-review](https://learn.chatgpt.com/docs/sandboxing/auto-review),
[Konfigurationsreferenz](https://learn.chatgpt.com/docs/config-file/config-reference),
[PermissionRequest](https://learn.chatgpt.com/docs/hooks#permissionrequest).
Die Hooks brauchen ihre vorgesehene Vertrauenseinrichtung; eine Skillinstallation
allein aktiviert keine automatische Freigabe.

Claude-Quelle: [Hookvertrag](https://code.claude.com/docs/en/hooks#permissionrequest-decision-control).
Insbesondere wirkt `ok:false` eines Prompt-Hooks bei `PermissionRequest` laut
Referenz nicht als Ablehnung; Agent-Hooks werden dort übersprungen.
OpenCode-Quellen: [Pluginvertrag](https://opencode.ai/v2/docs/build/plugins/#permissions-1),
[Berechtigungen](https://opencode.ai/v2/docs/permissions).

Ein lain-Prüfprofil muss die echte Aktion am unterstützten Freigabepunkt bewerten.
Bloße Rollenanweisungen sind keine technische Freigabe. Abdeckung, Timeout,
Fehler und Zusammenspiel mehrerer Reviewer/Hooks müssen am Zielbuild geprüft
werden; fehlende Entscheidungen dürfen nicht als Zustimmung gelten.
