# Account Connect: Kilo, OpenCode, Requesty und OpenRouter

Stand 2026-10-24, Quellbasis `5dc916f`. Empfehlungen für einen späteren Connect-Flow,
keine beschlossene Architektur und keine Umsetzung. Die vorherigen Dateien unter
`docs/quota-auth-research/` bleiben unverändert. Nur öffentliche Dokumentation,
öffentlicher Herstellerquellcode und lokaler Produktcode wurden gelesen. Keine
Credentials, Browserkonten, Anmeldungen, Registrierungen, Provider-API-Abfragen,
Inferenz, Dienste oder Builds verwendet.

## Die kurze Anschlussentscheidung

| Produkt | Kleinster konkreter Anschluss | Was noch vor einem eigenen „Anmelden“ fehlt |
| --- | --- | --- |
| OpenRouter | Eigener öffentlicher Browser-PKCE-Flow erzeugt den Inferenzkey; derselbe Key liest eigene Limits. | Hostadapter und Callbackabsicherung; kein Hersteller-Clientvertrag nötig. |
| Requesty | Offizielle CLI anmelden lassen, benanntes Profil wählen und `auth token --profile …` privat übernehmen. | CLI-Version, Prozess-/Profilisolation und Ausgabeübernahme qualifizieren. Für natives lain-OAuth zusätzlich eigene Clientzulassung. |
| Kilo | Vorhandenen Gateway-Key übernehmen und damit Wallet sowie optional persönlichen Pass lesen. | Eigene Device-Nutzung oder unterstützte CLI-Tokenübergabe bestätigen. Ein konkreter Hersteller-Deviceflow existiert. |
| OpenCode Go | Workspace-Key für Inferenz; öffentliches Console-MCP-OAuth als eigenständigen Monitorzugang qualifizieren. | MCP-Readtools für Go-Restquote und sicherer CLI-Übergabevertrag. |
| OpenCode Go Plus | Wie Go; tatsächlich gebuchtes Plus-Produkt am Workspace bestätigen. | Gleiche Authfragen, eigenständiger Quotanachweis für Plus. |
| OpenCode Zen / Console PAYG | Workspace-Key für Inferenz; Console-MCP-OAuth für Workspace-Monitor qualifizieren. | Konkrete Balance-/Budget-Readtools und Rechte; eigener PAYG-Katalogpfad in lain. |

„Anmelden“ soll nur dort angeboten werden, wo der gesamte beworbene Anschluss
qualifiziert ist. Bei OpenCode früh sagen: Ein Browserzugang zum Console-MCP ist
öffentlich vorgesehen; ob er die benötigten Monitorwerte liefert, ist noch offen.
Die dokumentierte Inferenzanbindung anderer Clients benutzt einen Workspace-Key.
Dieser Befund ist kein generelles OAuth-Unmöglichkeitsurteil.

Die UI beginnt mit dem Provider, anschließend mit dem konkret verfügbaren Anschluss.
Nur bei mehreren entdeckten Ressourcen folgt eine Workspace-/Gruppenauswahl. Fehlende
Leserechte werden am Monitor erklärt, ohne die Inferenzverbindung zu verwerfen.
Modellwahl und kostenpflichtige Zusatznutzung sind nachgelagerte eigene Entscheidungen.
Insbesondere ist **Kilo Free eine Modellwahl und kein Accountabo**.

## OpenRouter

**Aktuell:** lain kann API-Key-Inferenz und liest mit demselben Key bereits
`GET https://openrouter.ai/api/v1/key`. Ein eigener Loginadapter fehlt.

**Empfehlung und Nutzerweg:** „OpenRouter verbinden“ → Systembrowser → anmelden und
Zugriff sowie gegebenenfalls Workspace bestätigen → automatische Rückkehr → Account
mit Keylimits anzeigen. Keine Clientregistrierung, kein Clientsecret und kein
manuelles Keykopieren nötig. Der öffentliche Flow benutzt S256-PKCE an `/auth` mit
`callback_url`, `state` und anschließend `POST /api/v1/auth/keys`. Ergebnis ist ein
API-Key, kein dauerhaftes OAuth-Tokenpaar. Workspace kann optional vorgegeben oder
bei Reconnect mit `required_workspace_id` gebunden werden. Im Headless-Fall kann
statt Callback ein zehn Minuten gültiger Einmalcode eingegeben werden; dort fehlt
das zurückgegebene `state`. [OAuth-Anleitung](https://openrouter.ai/docs/guides/overview/auth/oauth),
[offizieller Integrations-README](https://github.com/OpenRouterTeam/skills/blob/main/skills/openrouter-oauth/README.md).

**Credentials und tatsächliche Fähigkeiten:** Bootstrap-Key kann Inferenz und eigene
Keydaten lesen. `/key` kann Identitätsanker, Ablauf, Keycap, Workspace-/Memberbudgets
und Free-Requestdaten liefern. Überlappende Caps bleiben einzelne Bedingungen;
`effective_budget` wird nicht als weiteres Guthaben addiert. Accountcredits sind
ein anderer Monitor: Die aktuelle `/credits`-Dokumentation verlangt einen
Management-Key. Ein älterer lokaler 200-Beleg mit gewöhnlichem Key ist eine
qualifizierungsbedürftige Abweichung, keine Garantie für neue Logins. Kein
automatisches Management-Key-Provisioning. [Eigener Key](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key),
[Credits](https://openrouter.ai/docs/api/api-reference/credits/get-remaining-credits).

**Lifecycle und Fallback:** Kein Refresh-Grant dokumentiert. Bei Widerruf/Ablauf erneut
anmelden und Credential am bestehenden lain-Account ersetzen. Manuelles Einfügen
eines vorhandenen Keys bleibt Alternative. Lokales Entfernen behauptet keinen
serverseitigen Widerruf. Eigene Browsercookies oder delegierte CLI sind unnötig.
Maintainer müssen Loopbacklistener, PKCE, state, Timeout, Abbruch/Replay und
Account-/Workspace-/Revisionsbindung implementieren. `POST /auth/keys/code` mit
vorhandener Authentisierung ersetzt den öffentlichen Bootstrap nicht.

## Requesty

**Aktuell:** API-Key-Inferenz ist vorbereitet, der Monitor ist manuell. Es fehlen
Reader für Key-self und optional Organisationsbalance.

**Empfehlung und Nutzerweg:** „Mit Requesty-CLI anmelden“ → lain startet die offizielle
CLI für ein explizites Profil `lain-<account>` → Browserlogin → nur bei mehreren
Gruppen passende Gruppe wählen → CLI erstellt einen Key → Host liest privat
`requesty auth token --profile <name>` → verbunden, eigener Keycap/-verbrauch wird
angezeigt. Kein vorgeschaltetes Feld für Org-ID, OAuthscope oder Management-Key.
Fehlende CLI wird vor Beginn mit konkretem Installationshinweis und Alternative
„Vorhandenen API-Key verwenden“ erklärt. Dies ist eine **konkrete CLI-Delegation**;
die CLI muss zur unterstützten Version passen. [CLI-README](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/README.md),
[gezielter Tokenhelper](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/cmd/auth.go).

**Credentials und Rechte:** Die CLI führt Authorization Code mit S256-PKCE, state und
Loopbackcallback aus. Ihr öffentlicher Herstellerclient `requesty-cli` erhält kurz
einen Bearer mit `manage:group:r manage:apikey:w` für `/v1/manage`; damit werden
Gruppen gelesen und ein Key erstellt. Dieser Bearer wird verworfen, nicht zum
Inferenzcredential umetikettiert. Keyerstellung setzt im untersuchten Code keine
expliziten Permissionfelder; Orgdefaults wirken. Der README beschreibt den
resultierenden Key als Inferenz+self-usage, ohne Management. Deshalb nach Übernahme
die tatsächlichen Key-self-Metadaten prüfen. Lain benötigt für den delegierten Weg
keine eigene OAuth-Client-ID. Für einen späteren eigenen nativen Browserflow fehlt
der öffentliche Registrierungs-/Redirectvertrag; Hersteller-ID nicht kopieren.
[PKCE](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/oauth/flow.go),
[Onboarding](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/onboarding/flow.go).

**Getrennter Monitor:** Mit Inferenzkey `GET /v1/manage/apikey/self` und
`/self/usage?start=…` auf `https://api-v2.requesty.ai`. Keycap, monatlicher Keyspend,
Ablauf und Keyusage sind keine Organisationsbalance; `monthly_limit=0` bedeutet
uncapped. Optional „Organisationsbalance ergänzen“ mit gesondertem
`manage=read, completions=none`-Key: `GET /v1/manage/org` liefert USD-Balance,
`/org/usage` Zeitreihennutzung. Readrechte für beliebige andere Keys/Org nicht aus
self ableiten. Org-Freepool, Keycap und BYOK-Kosten getrennt halten; Freepool-Rest
ist nicht aus lokalen Requests bestimmbar. [Selfclient](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/client/api_keys.go),
[Usageclient](https://github.com/requestyai/cli/blob/21809398e99d888565178d718186b637f3de4566/internal/client/usage.go),
[Managementrechte](https://docs.requesty.ai/features/key-management-api).

**Lifecycle und Voraussetzungen:** Profilauswahl explizit binden, nie das zufällig
aktuelle Profil verwenden. Der Helper schreibt genau den Key plus Newline nach
stdout; Host übernimmt ihn ohne Log/Frontend/Shellinterpolation. CLI installiert
und vertrauenswürdig, kein globales Profil überschreiben. Reconnect verwendet
gezielt `login --profile <name> --force`, ersetzt den lokalen Key am selben Account
und bewahrt Gruppe/Routerkontext. CLI hält ihre Profildaten; lain verwaltet nur den
übernommenen API-Key und seinen Herkunftsverweis. Browserbootstrap kann neue Keys
hinterlassen; lokales Unlink widerruft sie nicht. SSH-Loopback funktioniert laut
README nicht ohne passenden lokalen Rückkanal; dort manueller Key als Fallback.
Enterprise-SSO für das Dashboard ersetzt keinen API-Key, Cookieimport ist unnötig.

## Kilo

**Aktuell:** lain hat API-Key-Inferenz und nur den Eintrag „Explicit Kilo free models“;
kein Kilo-Quotaadapter ist registriert. Der alte TUI-Helfer belegt dagegen mit dem
Gateway-Bearer feste GETs für Wallet und persönlichen Pass. Kein Login ist nötig,
um einen bereits gespeicherten Key dafür zu verwenden.

**Empfehlung und Nutzerweg:** Bestehenden Kilo-Account öffnen → gespeicherten Key für
Balance verwenden → persönliches Wallet in USD, optional Passstatus anzeigen.
Neuer Account heute: „Gateway-Key verbinden“ → Kilo-Profil im Browser öffnen → Key
einmal übernehmen → Profilressourcen ermitteln → Auswahl nur bei Mehrdeutigkeit →
Wallet anzeigen. Freie Modelle wählt der Nutzer danach; der Account wird dadurch
nicht zu einem „Free-Abo“. Kein Topup oder bezahlter Ausweichpfad für den als
erschöpft gemeldeten Account. [Externe Gateway-Keys](https://kilo.ai/docs/getting-started/setup-authentication),
[Gatewayauth](https://kilo.ai/docs/gateway/authentication).

**Konkreter Login-Kandidat:** Die offizielle CLI hat einen Deviceflow mit
`POST https://api.kilo.ai/api/device-auth/codes`, Browserverification und Polling per
GET desselben Codes. Approval liefert Token und userEmail; 202=pending, 403=denied,
410=expired. Kein client_id, Scope oder PKCE im untersuchten Request. Das belegt
einen machbaren Herstellerflow, keinen veröffentlichten eigenen Drittclientvertrag.
Vor eigenem „Mit Kilo anmelden“ Herstellerfreigabe/Vertrag für diesen Flow oder
einen unterstützten privaten CLI-Tokenexport qualifizieren. Kein sicherer offizieller
Exportvertrag wurde in dieser Recherche nachgewiesen. Nicht einfach Authdateien
scrapen oder Herstellerclient als lain ausgeben. Persönliche Websession bleibt ein
separater Kandidat, falls ein Herstellervertrag/CLI-Export nicht verfügbar wird;
vor Nutzung Authentisierung, erlaubte API-Lesewege und Lifecycle qualifizieren.
Ein Browsercookie ist für den bereits belegten Keymonitor nicht erforderlich.
[Devicecode](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/auth/device.ts).

**Credential und Monitor:** Persönlicher Key/Device-Token wird als Bearer sowohl für
Gateway `https://ai-gateway.kilo.ai/api/v1` als auch `api.kilo.ai` verwendet.
`GET /api/profile` liefert Personal-/Orgkontext, `GET /api/profile/balance` die
Walletzahl direkt in USD. Ein separates Readscope ist nicht belegt. Orgreads
brauchen bestätigte Orgzuordnung und `X-KiloCode-OrganizationId`; dokumentierte
15-Minuten-Orgtokens haben einen eigenen noch ungeklärten Mint-/Refreshlifecycle.
Optional persönlicher Pass via `kiloPass.getState`: Status/Tier, Basiscredits,
Periodenusage, Bonus und nächste Termine jeweils getrennt. Bonus nicht addieren,
keinen Resttopf erfinden; Periodenusage kann andere Produkte enthalten. Fehlend ist
unknown, echter Nullsaldo bleibt 0 USD. Free-Limit 200/h ist
IP-bezogen, keine gemessene Accountrestquote. [Profilreader](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/profile.ts),
[Passreader](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/kilo-pass.ts).

**Lifecycle und Codefolge:** Der Herstellerclient speichert access=refresh und setzt
lokal ein Jahr Ablauf; daraus folgt kein echter Refresh-Grant oder garantierte
Tokenlebensdauer. Bei Authfehler denselben Account reconnecten. Zuerst festen
Balanceadapter registrieren, Accountmodell von Free-Modellrouting lösen, optionalen
Passmonitor gesondert ergänzen. Herkunft und Beobachtungszeit, stale/error sowie
Personal-/Orgscope erhalten. Alte TUI nur als zusätzlicher lokaler Readerbeleg:
`~/.local/share/modelcoach/provider_status.py:357–425` (nur Programmcode gelesen).

## OpenCode: gemeinsame Authbasis

Die aktuelle externe Anbindung ist ein persönlicher Workspace-Key. Nutzerweg:
„OpenCode verbinden“ → früh „Inferenz benötigt einen Workspace-Key; Browsermonitor
wird separat ergänzt“ erklären → Console/Account/Personal API keys/Add → Workspace
wählen, Namen vergeben, Key einmal übernehmen → Produktstatus zuordnen → verbunden.
Keine Serviceaccounts oder weitere Mitglieder automatisch anlegen; Go ist an einen
Workspace mit einem Mitglied gebunden. Derselbe Credential kann mehrere getrennte
Produktfähigkeiten tragen, ohne doppelte Accountbudgets zu erzeugen.
[Externe Clients](https://opencode.ai/v2/docs/console/harnesses/),
[API-Rechte](https://opencode.ai/v2/docs/console/api/).

**Drei separate Browseroptionen:**

- **Öffentlich eigener Login:** Console-MCP `https://opencode.ai/console/mcp` wird
  explizit mit OAuth und dynamischer Clientregistrierung für andere Harnesses
  dokumentiert. Eigene Registrierung/Metadata und Tokenlifecycle am MCP-Standard
  qualifizieren. Erlaubte Readtools, Schemas und Scopes für Balance/Goquota wurden
  nicht belegt; MCP-Zugang beweist auch keinen Inferenzkey. Falls dieser Weg die
  konkreten Readtools liefert: Browserlogin → Workspace nur bei mehreren → Monitor
  am gleichen lain-Account ergänzen. Keine MCP-Schreibtools aufrufen.
- **Offizielle CLI delegieren:** `opencode auth login opencode` ist im gepinnten
  dev-Quellstand mit Console-Deviceflow belegt. `auth export opencode` filtert nach
  Integration und schreibt deren Credentials als JSON nach stdout; ohne Ziel exportiert
  es alle Integrationen. Innerhalb des Filters können mehrere Accounts enthalten sein.
  Export ist keine dokumentierte Refreshdelegation: Zugriff auf aktive Credential,
  automatische Erneuerung, Versions-/Profilisolierung und alleinige Refreshhoheit
  müssen geklärt werden. Keine parallele Nutzung desselben rotierenden Refreshtokens
  in CLI und lain. Darum ein konkreter weiterzuqualifizierender Weg, kein bereits
  freigegebener Standardflow. [Export](https://github.com/anomalyco/opencode/blob/53b044d3139ddb28a4504c934cd4fa1e7e9829d7/packages/cli/src/commands/handlers/auth/export.ts),
  [Login](https://github.com/anomalyco/opencode/blob/53b044d3139ddb28a4504c934cd4fa1e7e9829d7/packages/cli/src/commands/handlers/auth/login.ts).
- **Persönliche Web-/Console-Session qualifizieren:** Hersteller-Deviceflow liefert
  echte access/refresh/expiry und User-/Orgkontext; `GET /api/v2/config` verwendet
  Bearer plus `x-org-id`. Ein eigener lain-Deviceclient ist öffentlich nicht belegt;
  `opencode-cli` nicht übernehmen. Ein persönlicher Sessionreader ist deshalb eine
  benannte Kandidatenklasse mit offenen Readendpunkten/Rechten/Expiry und kein
  pauschal ausgeschlossener Weg. DCR-MCP vor Sessioncookies bevorzugen.

Keys: normale Mitglieder können eigene Inferenzkeys erstellen. Owner/Admin können
bei persönlichen Keys zusätzlich Models Request, Usage Read, Budgets Read und
Billing Read passend einschränken. Persönliche Keys dürfen keine persönlichen Keys
provisionieren. Serviceaccount-Selbstprovisioning benötigt eigenes explizites Write
und kann nicht mehr Rechte delegieren. Keys laufen optional nie oder nach 1–365 Tagen
ab; abgelaufene werden neu erstellt. SSO kann aktive Sitzung verlangen. Reconnect
behält lain-Account und Workspace und ersetzt nur die passende Credential/Fähigkeit.

## OpenCode Go

**Aktuell:** `opencode-go` im Katalog, API-Key-Inferenz vorbereitet, Monitor unsupported.
**Produktweg:** obiger Workspace-Key-Ablauf für einen bereits Go-abonnierten Workspace;
keinen Kauf auslösen. Base `https://opencode.ai/inference/go`, Protokollpfad passend
zum Modell und stabile `x-opencode-session`; eigener User-Agent. Optionaler MCP-Monitor
erst nach nachgewiesenen Readtools. Kein pauschales Profil-/Org-/Scopeformular.

**Messung:** Go hat 5h-/Wochen-/Monatsfenster (20/50/100 Prozent der jeweiligen
Modellreferenz), keine Dollarwallet. Native Responseusage `opencode.billing_source=go`
und Allowanceverbrauch beschreibt die jeweilige Anfrage; kein Gesamt-Rest aller
Clients. Policybudgets zählen enthaltene Go-Nutzung nicht. Konkrete Resetzeitpunkte,
Mischmodellrechnung und tatsächliche verbleibende Goquote brauchen einen Readerbeleg.
Extra Usage kann PAYG-Balance verbrauchen und bleibt aus, solange nicht eigens gewählt.
Fallback: vorhandener Workspace-Key und Consolelink bei unbekannter Quote.
[Go](https://opencode.ai/v2/docs/console/go/),
[Usagefelder](https://opencode.ai/v2/docs/console/api/inference/).

## OpenCode Go Plus

**Aktuell:** eigene `go-plus`-Planidentität im gleichen Providerkatalog, Monitor fehlt.
**Produktweg:** gleicher Keyanschluss und gleiche `/inference/go`-Base, aber Plus am
gewählten Workspace nachweisen; weder Keyform noch fehlende Daten beweisen den Plan.
Workspacekonto und Monitorverbindung gemeinsam mit Go/PAYG nutzen, Pluslimits separat.
Browser-MCP, CLIdelegation und Sessionqualifizierung haben dieselben offenen Verträge
wie oben; keine stille Gleichsetzung mit Go. Native `billing_source=go-plus` ist ein
Anfragebeleg. Verbleibende Plusquote/Reset sind weiter offen; Dollarcredits und
Requestallowance nicht verrechnen. Extra Usage bleibt eine separate Wahl.
Fallback: Workspace-Key, Plan als unbestätigt falls keine Quelle, Consolelink.
[Go/Plus](https://opencode.ai/v2/docs/console/go/),
[Billingquelle](https://opencode.ai/v2/docs/console/api/inference/).

## OpenCode Zen / aktuelle Console PAYG

**Aktuell:** in dieser Quellbasis kein eigener qualifizierter Zen-/PAYG-Katalogpfad.
Historisches Zen nicht als extra Subscription neben Console erfinden.
**Produktweg:** Workspace-Key wie oben, bestehendes Guthaben verwenden, Basis
`https://opencode.ai/inference`. Browser-MCP als optionaler Workspace-Monitor nach
Readtoolqualifizierung. Privilegierte Leserrechte nur anbieten, wenn die betreffende
Rolle sie erteilen kann; sonst Inferenz verbunden und Monitor unbekannt ausweisen.

**Messung:** Workspacebalance, Member-/Keybudgets und historische Usage sind eigene
Größen. Budgetfenster beginnen laut Doku monatlich am 1. um 00:00 UTC; diesen Reset
nicht auf Goallowance übertragen. `billing_source=balance`/Responsekosten betreffen
Anfragen, keine vollständige Wallet. BYOK kann andere Kosten haben. Kostenangabe kann
höher als tatsächlicher finaler Debit sein. Console-Freechat ist extern gesperrt;
keinen Hersteller-User-Agent imitieren, um diese Beschränkung zu umgehen. Fallback:
Workspace-Key plus Consolemonitorlink. [Billing](https://opencode.ai/v2/docs/console/billing/),
[Budgets](https://opencode.ai/v2/docs/console/budgets/),
[aktuelle Modelle statt Zen](https://opencode.ai/docs/zen/).

## Gemeinsame Codefolgen als Empfehlung

`LoginOperationService` nimmt qualifizierte Adapter auf; daran OpenRouter sowie
explizite CLI-Bootstrapadapter anschließen. Die bestehende Credential-ID aus
Provider+Secret eignet sich nicht als einzige dauerhafte Reconnectidentität:
stabile Account-ID, Provideridentität, Workspace/Org/Gruppe, Credentialrevision und
Zweck des Vorgangs binden. Credentialrotation darf weder zweiten Account noch
weiteres Budget erzeugen. Host verwaltet Secrets; Frontend sieht Status, Browserurl,
Ressourcenauswahl und nachgewiesene Fähigkeiten.

Inferenz, Monitor und Loginherkunft als getrennte Fähigkeiten erfassen. Leere oder
verbotene Monitorantwort als unbekannt/fehlende Rechte zeigen. Nicht automatisch
Provisionierung, Schreibrechte oder Kauf als Fehlerbehebung auslösen. Mehrere
Monitorzeilen behalten Quelle, Scope, Fenster, Beobachtungszeit und Fehlerstatus;
Wallet, Requestgrenze und Caps werden nicht addiert. Implementierungsreihenfolge:
OpenRouter-eigener Bootstrap; Requesty-CLI und Selfreader; Kilo-Balancereader mit
vorhandenem Key; anschließend OpenCode-MCP-Readtoolqualifizierung und separat
Hersteller-/CLIverträge für verbleibende Komfortlogins.
