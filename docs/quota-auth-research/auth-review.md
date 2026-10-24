# Auth-/Quota-Gegenprüfung: OpenRouter, Fireworks und gemeinsame Architektur

Stand: 2026-10-24. Bezugsstand: `5dc916f`. Research und Architekturvorschläge,
keine beschlossene Umsetzung. Ausschließlich öffentliche Dokumentation und
Projektcode gelesen; keine Credentials, Konten, Logins, Registrierungen,
Provider-Abfragen, Dienste, Builds oder Produktcode verwendet/verändert.

## Ergebnis

OpenRouter ist ein unmittelbar geeigneter Kandidat für einen eigenen kleinen
Loginadapter: öffentlich dokumentierter PKCE-Keybootstrap einschließlich lokalem
Callback. Fireworks bietet einen konkreten Weg ohne manuelles Keykopieren über
seine offizielle CLI: anmelden, richtigen Account auswählen, benannten Nutzer-Key
erstellen und privat übernehmen. Dies ist CLI-Delegation mit Keybootstrap, kein
nachgewiesener öffentlicher OAuthvertrag für eine lain-Drittapp.

Login, Inferenzberechtigung und lesbarer Monitor sind drei getrennte Nachweise.
Ein Account kann mehrere dieser Fähigkeiten haben. Die bestehende stabile
lain-Account-ID bleibt ihr gemeinsamer Anker; ein zusätzlicher Monitor erzeugt
keinen weiteren Account und ein erneuerter Schlüssel kein zusätzliches Budget.

## OpenRouter

### Belegte Fähigkeiten

Die [offizielle PKCE-Anleitung](https://openrouter.ai/docs/guides/overview/auth/oauth)
beschreibt Browserautorisierung über `https://openrouter.ai/auth`, S256 und den
Codeaustausch über `POST /api/v1/auth/keys`. Das Ergebnis ist ein API-Key. Sie
unterstützt localhost-Callbacks auf beliebigen Ports, `state`, vorgewählte bzw.
erzwungene Workspaces und einen Headless-Ablauf ohne Callback. Im Headless-Fall
kopiert der Nutzer einen einmaligen, zehn Minuten gültigen Code; dort kommt kein
`state` zurück. Der [offizielle Repository-README](https://github.com/OpenRouterTeam/skills/blob/main/skills/openrouter-oauth/README.md)
nennt ausdrücklich keine nötige Clientregistrierung.

[`GET /key`](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key)
liest den verwendeten Key samt Limits und dokumentiert unter anderem
`creator_user_id`, `workspace_id`, `organization_id`, `expires_at`, Free-Requestwerte
und überlappende Budgetbedingungen. Der bestehende Adapter nutzt diesen Weg.
Die [aktuelle Credits-Referenz](https://openrouter.ai/docs/api/api-reference/credits/get-remaining-credits)
verlangt dagegen ausdrücklich einen **Management-Key**. Die alte Inventuraussage,
dass ein normaler Inferenzkey Accountcredits lesen könne, ist damit nicht mehr
belegt. Root berichtet zugleich einen früheren echten lokalen 200-Beleg mit
Standardkey. Die neue Dokumentation beweist daher nicht, dass jeder Standardkey
immer scheitert; abweichendes Altverhalten muss getrennt nach Quelle/Account
qualifiziert werden. Accountcredits und Keyspendlimits bleiben verschiedene Größen.

### Konkreter vorgeschlagener Anschluss

1. Einen `OpenRouterLoginAdapter` am vorhandenen `LoginOperationService`
   registrieren. Der Host erzeugt pro Vorgang Verifier und `state`, bindet den
   Callback nur an Loopback und öffnet den Systembrowser. Kein Clientsecret,
   Embedded Browser, fremder Client oder Consolecookie wird benötigt.
2. Zielaccount, Zweck, erwartete Revision und gegebenenfalls Workspace vor dem
   Login binden. Bei Reconnect `required_workspace_id` verwenden, sofern bekannt.
   Zurückgelieferten Key ausschließlich im privaten Host übernehmen und über
   `/key` Identität, Ablauf und Scope bestätigen. Bei fehlender Identität den
   Zustand als unbestätigt erhalten; nicht auf E-Mail oder Anzeigename vertrauen.
3. Den erzeugten Key als `api-key` speichern und den bestehenden Inferenz- und
   `/key`-Monitorpfad wiederverwenden. Acquisition-Metadatum `pkce-key-bootstrap`
   ergänzt die Herkunft; es macht den Key nicht zum OAuth-Access-Token.
4. Kein Refresh-Token erfinden. Bei Ablauf/Widerruf erneut anmelden. Die Anleitung
   erlaubt einen Link zur einzelnen Keyverwaltung aus SHA-256 des Keys; diesen
   für gezieltes Entfernen verwenden, ohne den Klartext in URLs zu setzen.

Ein Remote-/Headless-Host braucht eine kleine Eingabe für den Autorisierungscode;
das ist für diesen besonderen Fall erforderlich, aber kein API-Keykopieren.
Code und Verifier bleiben privat. Die Standard-Desktopvariante braucht diese
Eingabe nicht. Callback nach Abbruch, Replay, falschem `state` oder Ablauf wird
verworfen. Alle Listener werden begrenzt und geschlossen.

Nicht voraussetzen: Der Browserbootstrap liefert einen Management-Key, erlaubt
eigene `/credits`-Reads oder gewährt Zugriff auf alle Workspaces. Auch
[`POST /auth/keys/code`](https://openrouter.ai/docs/api/api-reference/oauth/create-authorization-code)
ist kein Ersatz für den Browserbootstrap ohne vorhandenen privilegierten Zugang.
Die erzeugte Credential kann auf Anbieter-Seite bereits bestehen, wenn lokale
Übernahme/Abbruch fehlschlägt; eine echte Löschung darf nur behauptet werden,
wenn ein dokumentierter Revoke/Delete tatsächlich erfolgreich war.

## Fireworks

### Belegte Anmeldung und Keybootstrap

[Authentication](https://docs.fireworks.ai/tools-sdks/firectl/commands/authentication)
und [Getting started](https://docs.fireworks.ai/tools-sdks/firectl/firectl)
dokumentieren `firectl signin` für Google-SSO sowie `firectl signin <account>` für
Custom SSO. Das ist mehr als ein Consolelink. Die öffentlichen Quellen belegen
damit keinen registrierbaren lain-OAuthclient, keine frei zu kopierende
Client-ID und keinen allgemein unterstützten Tokenexport.

Die CLI unterstützt [`account list -o json`](https://docs.fireworks.ai/tools-sdks/firectl/commands/account-list)
und [`api-key create --key-name <name> -o json`](https://docs.fireworks.ai/tools-sdks/firectl/commands/api-key-create)
mit explizitem `--account-id` und `--profile`. Letzteres erzeugt einen Key für den
angemeldeten Nutzer; ein Serviceaccount ist optional. Die
[Create-API-Referenz](https://docs.fireworks.ai/api-reference/create-api-key)
dokumentiert `key`, `keyId` und `expireTime`; der Klartextkey wird bei Erstellung
ausgegeben. [`List Accounts`](https://docs.fireworks.ai/api-reference/list-accounts)
ist zusätzlich mit API-Key-Bearerauth dokumentiert.

### Konkreter vorgeschlagener Anschluss

Ein kleiner `FireworksCliBootstrapAdapter` kann die installierte offizielle CLI
delegiert nutzen: `signin`, Accountliste, Auswahl des gemeinten Ressourcenaccounts,
einmaliges Erstellen eines benannten Nutzer-Keys, privates Übernehmen in den
bestehenden Account. Dieser Key versorgt Inferenz und bestehende Quota-RESTreads.
Das Erstellen eines neuen Nutzer-Keys muss als Bestandteil des Connect-Vorgangs
sichtbar sein. Es ist keine Registrierung oder Anlage eines Serviceaccounts.

Vor Implementierungsfreigabe bleibt gezielt zu qualifizieren: unterstützte
CLI-Version, tatsächliche JSON-Ausgabe von `api-key create`, Profilisolierung des
Signin-Vorgangs, Abbruch/Fehler sowie der Umgang mit bereits erzeugtem Key nach
lokalem Fehler. Die Dokumentation nennt sowohl `signin` als auch in Billingguides
`login`; den Auth-Seiten folgen und die Aliasfrage offline anhand der CLIhilfe
prüfen. Nicht ungeprüft `~/.fireworks/auth.ini` übernehmen, die bestehende globale
CLI-Anmeldung überschreiben oder fremde Refreshdaten in lain konvertieren.

Ein schmaler Fallback ohne Credentialübernahme ist das dokumentierte
[`firectl quota list -o json`](https://docs.fireworks.ai/tools-sdks/firectl/commands/quota-list)
mit explizitem Profil/Account. Dabei besitzt die CLI ihre Session; lain besitzt
nur die Delegationsreferenz. Native RESTreads nach Keybootstrap sind für den
Dauerbetrieb die einfachere Variante. Der vorhandene Inferenzkey bleibt der
erste Versuch, wenn bereits ein Account in lain existiert.

### Reporting und seine Grenzen

| Quelle | Aussage | Auth/Identität und Grenze |
| --- | --- | --- |
| [List Quotas](https://docs.fireworks.ai/api-reference/list-quotas), [Quota-Guide](https://docs.fireworks.ai/guides/quotas_usage/account-quotas) | `value`, `maxValue`, `usage`; `monthly-spend-usd` ist ein Spendlimit | API-Key + expliziter Account. Mindestrolle weiterhin nicht sauber ausgewiesen. Rest unter Spendlimit ist kein Creditguthaben. |
| [billingUsage](https://docs.fireworks.ai/api-reference/get-billing-usage) | Vor allem Mengen, etwa Tokens/GPU-Zeit | API-Key; Konto-/Modell-/Keydimensionen. Sonderfeld `costNanoUsd` laut Schema nur begrenzt autoritativ; keine allgemeine Kostenquelle. |
| [billing/summary](https://docs.fireworks.ai/api-reference/get-billing-summary) | Bewertete Kostenpositionen mit Währung, optional Tagesblöcke | API-Key + Account; Tagesdaten mit exklusivem Enddatum. Rechnungskorrekturen/Credits sind getrennt. |
| [usageCosts:query](https://docs.fireworks.ai/api-reference/query-usage-costs) | Bewertete Kosten nach maximal zwei Dimensionen; `subtotal`, `evaluationTime`, `attributionCompleteness` | `ACCOUNT` benötigt Admin; `SELF` ist auf authentifizierten Principal beschränkt. Scope niemals still umdeuten. Voller Subtotal nicht je Seite aufsummieren. |
| [CLI-Costexport](https://docs.fireworks.ai/accounts/exporting-usage-costs) | Serverless-Kosten in UTC-Tagen | Admin, maximal 31 Tage pro Abfrage, Beginn höchstens 100 Tage zurück. Kein vollständiges Accountbudget; kein Echtzeitbeleg. |

Die [Usage-&-Cost-Anleitung](https://docs.fireworks.ai/accounts/exporting-usage-and-costs)
enthält eine erkennbare redaktionelle Inkonsistenz: der Überblick verneint
Per-Key-Dollarwerte, während ihre neue Endpointübersicht und die eigene
`usageCosts:query`-Referenz sie dokumentieren. Die spezifische Referenz trägt den
positiven Befund. Die pauschale Aussage „any key“ im Guide hebt die explizite
Adminanforderung für `ACCOUNT` nicht auf. `evaluationTime` ist Auswertungszeit,
kein garantierter Vollständigkeitszeitpunkt aller Verbrauchsdaten. Die direkt
geöffnete `billingUsage`-Referenz enthält außerdem `costNanoUsd`: laut Schema
liefert derzeit nur huggingface autoritative Kosten, und `0` kann fehlende
Kostenerfassung bedeuten. Das ist keine kostenlose Nutzung und ersetzt die
bewerteten Kostenendpunkte nicht.

## Gemeinsame Architekturempfehlungen

### Vier unabhängige Achsen statt eines OAuth-Flags

Die folgenden Felder sind ein Entwurf für schmale Katalog-/Statusmetadaten:

| Achse | Beispiele | Zweck |
| --- | --- | --- |
| Acquisition | public-oauth, pkce-key-bootstrap, vendor-device, official-cli, manual-key, console-session | Wie kommt der Zugang zustande? |
| Ownership | lain-owned, external-snapshot, delegated-cli, user-supplied-key | Wer darf refreshen, ersetzen oder widerrufen? |
| Capability | inference, quota-read, usage-read, cost-read, identity-read | Was ist mit genau diesem Zugang belegt? |
| Scope/qualification | subject, tenant/account/project/workspace, plan, source, checked-at; documented/implemented/live-qualified | Welche Ressource und welcher Nachweis tragen die Anzeige? |

Ein herstellereigenes Deviceprotokoll ohne registrierbaren Drittclient ist kein
Beleg für öffentliches OAuth. Ebenso ist ein selbstgewählter `app`-Parameter
keine Erlaubnis zur Sessionübernahme. Consolezugang bleibt eine eigene,
ausdrücklich eingerichtete Fähigkeit mit klarer Ablauf-/Logoutsemantik; kein
stiller Browsercookieimport. Ein Login kann einen reinen Inferenzkey liefern.

### Bestehende Trennstellen nutzen

`IQualifiedLoginAdapter` und `LoginOperationService` sind die richtige vorhandene
Schiene. Kleine konkrete Module ergänzen; keine dynamische Pluginengine bauen.
Additiv braucht der Startkontext mindestens Zielaccount/Neuanlage, Authmethode,
Zweck, erwartete Revision und Ressourcenbindung. Ein Ergebnis muss die betroffene
Account-ID erkennen lassen. Der Host übernimmt Secrets; Desktop und CLI sehen
nur Status und Anbieter-URL. Nicht jeden Provider hinter einen allgemeinen
Tokenendpoint mit erratenen Parametern pressen.

`MonitorAccessService` bindet Monitorsecrets bereits separat an Account und
Provider, schützt Revisionen und verhindert stillen Fallback. Diese Eigenschaften
behalten. Die aktuelle Forderung `CredentialKind == "api-key"` sowie die
`HostQuotaSource`-Bedingung eines vorhandenen Inferenzsecretfiles sind für den
eigenständigen Monitor jedoch zu eng. Ein stabiler Account muss allein eine
Monitorfähigkeit besitzen können. Keinen Dummy-Inferenzkey speichern und keine
Route anlegen, um einen Google-/Console-/CLI-Monitor zu ermöglichen.

Bei Keybootstrap `api-key` beibehalten und nur Herkunft/Lifecycle ergänzen.
Owned-OAuth-Sessions benötigen einen eigenen privaten Sessiondatensatz, dessen
Access-Projection Monitoradapter lesen; externe Snapshots werden nicht nachträglich
zu owned Sessions. Mehrere lokale Schlüssel desselben Anbieteraccounts sind
nicht automatisch unabhängige Budgets; gemeinsamer Pool nur aus konkreten
Ressourcen-/Budgetidentitäten, nicht Anbietername oder Login-E-Mail.

### Refresh, Entfernen und Speichern

- Externe Access-Snapshots: kein Refresh und kein Revoke, Ablauf sichtbar,
  optional erneuter ausdrücklich veranlasster lesender Import. Import entfernt
  Refreshmaterial; Keybootstrap ist etwas anderes.
- Eigene OAuthsessions: Refresh pro Credential serialisieren, Rotation atomar
  speichern, weggelassenes Refresh-Token erhalten, Grants/Audience/Scope prüfen.
  Credentialgeneration schützt gegen verspätete Refresh-/Callbackantworten nach
  Disconnect. `invalid_grant` führt zu Reconnect, nicht Endlosschleife.
- Delegierte CLI: Session bleibt Eigentum der CLI. Profil/Account explizit binden;
  weder selbst Refreshdaten schreiben noch globales CLI-Logout auslösen.
- API-Key: kein OAuthrefresh. Bei Erneuerung stabile Account-ID und Historie
  erhalten; Ressourcenbindung erneut prüfen. Alter Monitor darf nicht still
  dem neuen, möglicherweise anderen Anbieteraccount zugeschlagen werden.
- Monitor entfernen, lokalen Account entfernen und Provider-Key widerrufen sind
  verschiedene Operationen. `AccountService.Remove` behält derzeit Secrets für
  laufende Requests; das ist **keine Credentiallöschung**. Eine neue
  Disconnect-/Cleanupfunktion muss diesen Lebenszyklus ausdrücklich auflösen.
- Vorhandene atomare private Speicherung mit 0700-Verzeichnissen/0600-Dateien und
  Secret-freien DTOs weiterverwenden. Diese Dateirechte sind keine Verschlüsselung.
  Keine Tokens in Status, Logs, Fehlern, Kommandozeilenargumenten oder Shellhistory;
  CLI-stdout mit erzeugtem Key privat begrenzen/übernehmen und nie durchreichen.

### Monitor-Modus und Anzeige

Der bestätigte erste Monitorumfang ist Kontingente und Resets ohne eigenen Proxy
([Projektvertrag](../monitor-mode.md)). Direkte Quota-/Balance-APIs funktionieren
dafür grundsätzlich; Gatewayresponse-Header dagegen nur mit tatsächlich
beobachtetem Verkehr. Ohne Proxy und ohne externe dokumentierte Quelle kann ein
Headeradapter keine neuen Werte liefern. Keine künstliche Inferenz als Refresh;
letzte Beobachtung bleibt alt oder der Zustand `awaiting-observation`.

Zwei getrennte Zustände anzeigen: Zugang (`configured`, `expired`,
`reauth-required`, fehlende Rechte) und Datenquelle (`awaiting-observation`,
`source-unqualified`, `fresh`, `stale`, nicht verfügbar). Login-Erfolg liefert
keinen Nullverbrauch, kein Free-Tier-Restbudget und keinen Reset. 401/403/429
sind weder null Credits noch unbegrenzte Nutzung. Rategrenzen, Spendcaps,
Prepaidguthaben und gemessene Nutzung bleiben benannte eigene Zeilen.

## Erste Gegenprüfung der anderen Recherchepfade

Diese Punkte beruhen auf Workerberichten; Quellenprüfung der finalen Gesamtmatrix
bleibt eine eigene Abschlussstufe.

- Kilo: Worker belegt vendoreigenes Deviceprotokoll ohne `client_id`; derselbe
  Token wird in offizieller CLI für Gateway und Balance verwendet. Bestehenden
  Key für Balance priorisieren; Drittapp-Deviceflow nicht aus CLIcode ableiten.
- MiMo: Worker belegt Browser-Keybootstrap mit verschlüsseltem Inferenzkey und
  `uid`/`base_url`, getrennt nach PAYG/Token Plan. Das ist kein Refresh-OAuth.
  Console-`usage.items[]` und Sessionrechte müssen separat an Plan und Account
  gebunden werden; Keybootstrap belegt diesen Sessionmonitor nicht.
- Google: öffentlicher Desktop-OAuthvertrag und echte Quota-Metriken sind zwei
  positive Befunde. Jede konkrete Limit-/Usage-Serie benötigt identische
  Project-/Service-/Model-/Windowzuordnung; identischer Login allein reicht nicht.
- Cloudflare: OAuthfähigkeit und Dataset-Schemabeleg getrennt führen. Ein
  `totalNeurons`-Schema-Mirror ist ein Kandidat, noch kein qualifizierter offizieller
  Account-/Billing-/Freshnessvertrag.
- Step: offizieller CLIcode mit `/usage` ist ein konkreter Integrationskandidat;
  Backendrollout und Berechtigung bleiben offen. Keine Abwesenheit behaupten,
  aber auch kein erfolgreiches Polling aller Nutzer.

### Ergänzende Abschlussgegenprüfung

Die vorliegenden Sol-Berichte wurden auf Authmethoden, Rechte und Monitorbetrieb
gegengelesen; folgende Primärquellen wurden zusätzlich selbst geöffnet:

- **Poe:** [Sign in with Poe](https://creator.poe.com/docs/external-applications/sign-in-with-poe)
  dokumentiert eine eigene Drittappregistrierung, S256, `state` und
  `apikey:create`; der ausgegebene Key trägt auch den
  [Current-Balance-Read](https://creator.poe.com/api-reference/getCurrentBalance).
  Daher neben OpenRouter hohe Priorität. Die automatische localhost-Ausnahme
  ist ausdrücklich als lokale Entwicklung beschrieben; ihre Produktionsnutzung
  bleibt separat zu qualifizieren. Keine Refresh-Tokens erfinden, und kombinierte
  Plan-/Add-on-Punkte nicht in vermeintliche monatliche Restkontingente zerlegen.
- **Nebius:** Das geöffnete [Static-Key-Proto](https://github.com/nebius/api/blob/e855cccf88420a80741b04075674528d75f3e238/nebius/iam/v1/static_key.proto)
  unterstützt derzeit nur Serviceaccounts und unterscheidet `AI_STUDIO` von
  `OBSERVABILITY`. Die [SSO-Anleitung](https://docs.tokenfactory.nebius.com/team-access/sso)
  belegt CLI-/Federationseinrichtung, keine vollständige persönliche
  Inferenzkeybereitstellung. PKCE plus AI_STUDIO ist ein konkreter Prüfpfad;
  eine erforderliche Serviceaccountanlage wäre ein zusätzlicher privilegierter
  Schritt und gehört nicht still in einen persönlichen Login. Finanzreporting
  bleibt unabhängig davon unqualifiziert.
- **Mistral/xAI:** Vibe-Login erzeugt keinen Enterprise-Admin-Key; Grok-Build-Login
  liefert keinen belegten API-Team-Managementzugang. Produkt, Tenant und Rechte
  müssen getrennt bleiben. Die Workerberichte stellen dies zutreffend heraus.
- **OpenCode:** Öffentliches MCP-DCR ist ein valider Kandidat, aber es fehlen
  konkrete Quota-Readtools. Die feste offizielle Device-Client-ID ist nicht der
  Registrierungsvertrag für lain; MCP-Audience ist keine allgemeine Inferenz-
  oder Billingberechtigung.
- **Samba/Cohere/Nebius:** Passive Header bzw. lokale Tokenzählung können gute
  Gatewayergänzungen sein. Als erste Umsetzung für eigenständigen Monitorbetrieb
  erfüllen sie das Ziel nicht. In der Gesamtmatrix getrennt als Gateway-only
  ausweisen; tatsächliche Console-/CLI-Reader weiter qualifizieren.

## Schmale Reihenfolge und Abnahmekriterien

1. Poe- und OpenRouter-PKCE-Keybootstrap sowie Fireworks-CLI-Keybootstrap zuerst; existierende
   `/key`-/Quotaadapter weiterverwenden. Ressourcenbindung und Reconnect als
   gemeinsame kleine Vertragsänderung definieren.
2. Eigene öffentliche OAuthmodule nur für positiv dokumentierte Drittclients
   mit passender Registrierung; offizielle CLI als eigene Alternative führen.
3. Reporting-Erweiterungen pro Quelle separat: Fireworks `SELF`/`ACCOUNT`,
   Google konkrete Quotaserien, Cloudflare Schemaqualifikation. Keine Loginzahl
   als Zahl funktionierender Quotaadapter zählen.

Für eine spätere Umsetzung genügen gezielte Prüfungen: zwei parallele Accounts,
falscher Workspace/Project, abgelaufener Code, falsches `state`, Replay, Abbruch,
Refreshrotation, Disconnect während Refresh, kein Secret in DTO/Logs,
403 ohne Credentialfallback, partielles Reporting ohne Nullbehauptung und
Monitorbetrieb ohne Start eines Coreprozesses. Die vorliegende Recherche führt
keinen dieser privaten Livechecks aus.
