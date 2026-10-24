# AC-03 / AC-04: vorhandene Keys lesen und öffentliche Browserlogins

Stand 24.10.2026, Istcode `5dc916f`; zwei getrennt abnehmbare Umsetzungspakete als Vorschlag.
Grundlage: [Connect-Entwurf](../account-connect-redesign-2026-10-24.md),
[Kilo/Requesty/OpenRouter](../account-connect-research/kilo-opencode-requesty-openrouter.md),
[Synthetic/Step/xAI/Poe](../account-connect-research/xai-stepfun-poe-synthetic.md).
Nur Code und abgeschlossene öffentliche Recherche geprüft; keine Implementierung, Builds,
Logins, Providerreads oder Commits in diesem Planauftrag.

## Gemeinsame Grenzen und Übergaben

- AC-01 (Accountbasis/Connectoperations) liefert stabile Account-ID, Credentialrevision, bestätigten Provider-/Produktscope
  und Ressource; Monitor-only darf keine Route, Tarifwahl oder Coreinstallation verlangen.
- AC-01 liefert Methode/Zweck/Zielaccount/Revision, begrenzte Laufzeit und Anzahl,
  Callback-/Abbruchzustände und sichere Credentialübernahme. Kein zweiter Loginwizard.
- Foundationworker besitzt `src/Lain.Host/Accounts/AccountService.cs` und SecretStore in
  `src/Lain.Host/Operations/OwnStatePaths.cs`; dort benötigte Speicherung/Rotation nur anfordern.
- AC-00 (Root) besitzt `src/Lain.Core/Contracts.cs`, `src/Lain.Host/Quota/QuotaContracts.cs`,
  `src/Lain.Host/Program.cs` einschließlich `HostQuotaSource`, Projekt-/Builddateien und Registrierung.
  Reader-/Adapterpaket liefert dafür konkrete Integrationsanforderungen und keine parallelen Änderungen.
- Desktop/CLI und Darstellung gehören AC-02 (UX); Katalogdateien besitzt AC-09 (UX). Die Pakete liefern geheimnisfreie Zustände/Semantik;
  keine eigenen Änderungen in `src/Lain.Desktop/` oder `src/Lain.Cli/`.
- Quotaadapter erhalten nur aktuelle private Zugriffsprojektionen, keine Refresh-/Create-/Revokehoheit.
  Host besitzt gespeicherte API-Keys; Provider widerruft sie. Lokales Entfernen ist kein Remote-Revoke.

## AC-03 — vorhandene Keyreader zuerst

**Ziel:** Ein vorhandener geeigneter Key zeigt seine belegten Werte am selben Account, auch
ohne Modelle. Neue Logins sind keine Voraussetzung. **Status: lokal planbar** für die unten
belegten Reader-/Parserteile; Hersteller-/Scopefragen begrenzen nur die ausdrücklich offenen Teile.

**Istcode:** `SyntheticQuotaAdapter` liest bereits `subscription`, `StepFunQuotaAdapter`
bereits persönlichen `.com`-PAYG-Saldo. Beide sind registriert. `QuotaService` besitzt
Refresh, Teilfehler und letzte erfolgreiche Beobachtung. Kilo-/Requesty-Adapter und
xAI-Teammetadatenread fehlen; vorhandene funktionierende Reads werden nicht neu gebaut.

**Schritte und genaue Dateien** (neue Dateien sind als solche bezeichnet):

1. **Kilo Wallet:** neu `src/Lain.Host/Quota/KiloQuotaAdapter.cs`; vorhandenen persönlichen
   Gateway-Bearer für `GET https://api.kilo.ai/api/profile/balance` verwenden. `balance`
   direkt USD-Rest; 0 erhalten, fehlend/ungültig unknown. Keine Obergrenze, Prozent oder Reset.
   Für Ressourcenbindung bei Bedarf Profilread aus Herstellerquelle, kein E-Mail-Merging.
   Orgvariante nur mit bestätigter Orgzuordnung und eigenem qualifiziertem Tokenlifecycle.
2. **Kilo Pass:** neu `src/Lain.Host/Quota/KiloPassReader.cs`, persönlicher Scope, derselbe
   Bearer; belegtes `kiloPass.getState` über festen `api.kilo.ai/api/trpc`-GET (Batchinput gemäß
   Herstellerquelle). Status/Tier, Basiscredits, Periodenusage, Bonus und nächste Termine getrennt.
   Keine Bonusaddition/Reserveberechnung, keine Gleichsetzung von Periodenusage mit Walletverbrauch.
   Root muss Status-/Terminprojektion freigeben; bis dahin keine Scheinquota in Zahlenfelder pressen.
3. **Requesty Self:** neu `src/Lain.Host/Quota/RequestyQuotaAdapter.cs`; Inferenzkey für
   `GET https://api-v2.requesty.ai/v1/manage/apikey/self` und `/apikey/self/usage?start=…`
   unter derselben Managementbasis. Key-ID/Gruppe, Permissions/Expiry, `monthly_limit`,
   `monthly_spend` sowie klar begrenzte Selfusage lesen; `monthly_limit=0` heißt uncapped.
   Zeitraum ausdrücklich führen; Monatscap nur mit passendem Monatsverbrauch vergleichen.
   Kein manage-Recht, Orgwallet, anderer Key oder accountweiter Freepool aus Self ableiten.
4. **Synthetic:** `src/Lain.Host/Quota/SyntheticQuotaAdapter.cs` erweitern, denselben
   `GET https://api.synthetic.new/v2/quotas` und Altvertrag erhalten. `planWeeklyLimit`
   mit `percentRemaining/nextRegenAt/nextRegenPercent` bevorzugen; tatsächlich gemeldete
   `weeklyTokenLimit` und `rollingFiveHourLimit` getrennt erkennen, keine Pools addieren.
   Stringcredits exakt erhalten; `nextRegenAt` ist Teilregeneration, kein Vollreset.
   Präzisions-/Regenerationsfelder bei Root anfordern; keine stillen Doubleverluste oder
   Umdeutung zu `ResetsAt`. Passiver Headerpfad ist gesonderte Transportarbeit, hier kein Muss.
5. **Step PAYG:** `src/Lain.Host/Quota/StepFunQuotaAdapter.cs` behalten. Nur belegte Lücke
   schließen: `prepaid/postpaid` und persönlichen Produktscope erhalten, Unknown bei ungeklärtem
   Orgscope. `total_cash_balance/total_voucher_balance` bleiben kumulierte Aufladungen/Geschenke,
   keine Limits. Negativen Postpaidwert erst nach Vertragsklärung unterstützen; `.ai`-Host,
   Orgreader und Step Plan nicht aus `.com /v1/accounts` ableiten. Kein neuer Balanceendpoint.
6. **xAI Discovery:** neu `src/Lain.Host/Providers/Xai/XaiKeyMetadataReader.cs`; mit dem
   Inferenzkey ausschließlich `GET https://api.x.ai/v1/api-key`: `team_id`, `api_key_id`,
   ACL-/Disabled-/Blockstatus erfassen. Bestätigtes Team an Foundation übergeben; keine Team-ID
   vom Namen raten. Kein Billingreader in AC-03; Managementscope/Teamabgleich ist eigenes Paket.
7. Integration über Root: neue Adapter registrieren, Kilo-Console-Only-Fallback in
   `src/Lain.Host/Quota/QuotaService.cs` passend ersetzen, vorhandene Stale-/Teilfehlerlogik nutzen.
   AC-09 erhält Updates für `src/Lain.Host/Providers/Catalog/{kilo,requesty,synthetic,stepfun,xai}.json`
   nur zu tatsächlich erweiterten Fähigkeiten; Kilo Free bleibt Modellwahl.

**Scope und Owner:** Kilo/Requesty/Synthetic/Step behalten ihren ausgabefähigen API-Key,
auch bei Monitor-only. Kein zusätzlicher Monitorkey, kein technisches Read-only-Versprechen.
Requesty Self gehört dem Key/gegebenenfalls seiner Gruppe, xAI-Metadaten dem belegten Team;
keine identitätslosen Keys zu einem Geldpool zusammenlegen. Transport nur zu festen,
quellbelegten Hosts mit begrenzter Antwort, Redirectabwehr und bereinigten Fehlern.

**Abhängigkeiten:** Parser/Fake-HTTP lokal unabhängig von neuer OAutharbeit; durchgängiges
Monitor-only und Reconnect benötigen AC-01. AC-00 erhält fachliche Passstatus-/Regenerations-/
Präzisions-/Scopeanforderungen, keinen generischen Metadatenbeutel; AC-01 Provideridentität/Revision.
Nach Rotation/Scopewechsel alte Snapshots invalidieren; stale gilt nur für dieselbe Credentialbindung.
Keinen generischen Numberparser für alle Provider still verändern; Step-Vorzeichen bleibt offen.

**Nichtziele:** Kilo-Device/CLIlogin, Requesty-CLIbootstrap/Orgmonitor, Synthetic-PAYGsaldo,
xAI-Managementbilling/Keyprovisionierung, Step Plan/Overseas/Orgquota, neue Historyoberfläche,
Dummyroute, bezahlte Probe, Käufe, Core-/Profilanwendung und neue Polling-/SLAversprechen.

**Abnahme:** bestehende Suiten `tests/providers-cohere-kilo/Program.cs`,
`tests/providers-synthetic-xai/Program.cs`, `tests/providers-nvidia-step/Program.cs` sowie
`tests/quota/Program.cs` erweitern; neue Fake-HTTP-Fälle in
`tests/quota/KeyReaderChecks.cs` (neu; Einbindung durch Root).
Happy: Kilo 0 USD ohne Balken, Pass getrennt, Requesty Self ohne manage, Synthetic neu+alt,
Step unverändert und xAI bestätigtes Team; zwei Accounts bleiben getrennt.
Error: 401/403, Timeout/Redirect, falscher Host/Scope/Revision, Teilfehler Pass bei gültiger
Wallet; keine Secrets im Ergebnis, keine fremden Credentialziele, letzte Werte mit Alter erhalten.
Unknown: leere/driftende Antwort, ungültige Zahlen/Zeit, ungeklärte Identität, uncapped vs 0,
Synthetic-Koexistenz/Stringpräzision und Step-Org-/Vorzeichenfall ohne erfundene Restwerte.

**Gate:** lokale Fixture-/Transport-/Foundationintegration vor Livefreigabe. Danach gesondert
autorisierte minimale GETs je Produktscope und Dashboardvergleich; kein Inferenzrequest nötig.
Release weist lokale Prüfung und echte Quellenqualifikation getrennt aus. Offene Org-/Hostvarianten
bleiben gesperrt/unknown; Kilo-Pass benötigt den eigenen Quellen-/Schemabeleg zusätzlich zur Wallet.

## AC-04 — öffentliche Browserlogins OpenRouter / Poe

**Ziel:** Anbieter wählen → Systembrowser bestätigen → API-Key privat übernehmen → vorhandener
Reader am stabilen Account. Modelle optional später. **Status: lokal planbar** für Hostadapter,
Fakebrowser/Callback und Austausch; Poe-Produktionsclient/Callback ist Maintainerqualifikation.

**Istcode:** `OpenRouterQuotaAdapter` liest bereits `/api/v1/key`, `PoeQuotaAdapter`
bereits `/usage/current_balance`; beide registriert. `PoeOAuthAttempt` in
`src/Lain.Host/Providers/Poe/PoeOAuth.cs` besitzt S256/state, Einmalverbrauch und Ablaufparser.
`LoginOperationService` in `AccountService.cs` ist vorhanden, aber ohne benötigten
Methode-/Zweck-/Zielaccountkontext und umfassenden Lifecycle; AC-04 nutzt dessen gemeinsame Fortführung.

**Schritte und genaue Dateien:**

1. Neu `src/Lain.Host/Accounts/OpenRouterLoginAdapter.cs`: öffentlicher S256-Keybootstrap
   `https://openrouter.ai/auth` → `POST https://openrouter.ai/api/v1/auth/keys`;
   Callback/state/Verifier an gemeinsame Connectoperation binden. Keine Clientregistrierung nötig.
   Optional bekannten Workspace bei Reconnect mit `required_workspace_id` erzwingen.
2. Neu `src/Lain.Host/Accounts/PoeLoginAdapter.cs`: vorhandenes `PoeOAuthAttempt` verwenden,
   weder PKCE noch Parser duplizieren. Maintainerclient und exakt freigegebener Callback;
   `apikey:create` an `https://poe.com/oauth/authorize` → `POST https://api.poe.com/token`.
3. Gemeinsamen Callback-/Browserowner der Connectoperations nutzen: begrenzter Loopbacklistener,
   korrekter Pfad/state, Deny/Timeout/Abbruch/Replay, Listenerfreigabe und sichere Einmalübernahme.
   Nach Disconnect/Revisionwechsel dürfen verspätete Callbacks keine Credential wiederherstellen.
4. Foundation übernimmt API-Key mit Ablauf/Herkunft am Zielaccount, dann vorhandene Reader
   `src/Lain.Host/Quota/{OpenRouterQuotaAdapter,PoeQuotaAdapter}.cs` anschließen.
   Poe ohne belegte User-ID bleibt ausdrücklich identitätsunbestätigt; kein Auto-Merge nach Name.
   OpenRouter-Identity-/Workspacefelder nur bei tatsächlicher Antwort bestätigen.
5. Root verdrahtet Adapter/Operationen in `Program.cs` und Contracts; UX nutzt dieselben
   geheimnisfreien Zustände für CLI/Desktop. Fehlende Poe-Maintainerkonfiguration früh erklären,
   kein Endnutzerformular für Client-ID und kein funktionsloser Loginbutton.

**Scope/Credentialowner:** Host besitzt erzeugten API-Key, weder Provider liefert hier einen
belegten Refresh-Grant. Ablauf/Widerruf → neuer Consent am gleichen Account, Credentialrevision
ersetzen; History/Name/Routen bleiben. OpenRouter-Key erlaubt eigene Limits, nicht automatisch
Managementcredits; Poe kombiniert Plan-/Add-on-Restpunkte ohne Monatsnenner/Reset.
Remoteausgabe und lokale Speicherung sind nicht atomar: unklare Ausgabe nicht blind wiederholen,
Fortschritt sichern; verlorener einmaliger Key erfordert bewussten Neubootstrap statt Schein-Recovery.

**Abhängigkeiten/Nichtziele:** AC-00/AC-01 vor Ende-zu-Ende-Abnahme; AC-04 ist unabhängig von AC-03;
kein API-/SecretStore-Sonderpfad. Keine neuen Quotaadapter, Poe-History-UI, OpenRouter-Managementkeys,
Cookies, CLIdelegation, Refreshsimulation, Inferenzprobe, automatische Route oder Profilanwendung.
Headless-OpenRouter nur als eigener belegter Einmalcodepfad (10 Minuten, kein echoed state),
sonst manueller API-Key-Fallback; nicht still den Desktopcallback auf Remotehosts versprechen.

**Abnahme:** `tests/providers-poe-opencode/Program.cs` und `tests/gateways/Program.cs` erhalten;
neue Hostfälle in `tests/Lain.Accounts.Tests/PublicLoginChecks.cs` (Einbindung durch Root).
Happy: Consent → ein Account + eigener Reader, optional keine Route/Core; Reconnect erhält ID/History.
Error: falsches state, Replay, Deny, Timeout, Tokenerror, Abbruch nach Ausgabe, paralleler
Reconnect/Disconnect, fremder Workspace und SecretStorefehler; keine Klartextkeys in URLs/DTOs/Logs.
Unknown: Login erfolgreich bei ungültigem/fehlendem Quotaschema bleibt verbunden mit Quota unknown;
Poe ohne User-ID und OpenRouter ohne Identityfelder nicht als verifizierte Identität ausgeben.

**Gate:** Offline-Callback-/Austausch-/Lifecycleprüfung zuerst. Vor Poe-Live/Release Client durch
Maintainer registrieren und Produktions-/Loopbackhost-/Portvertrag bestätigen; kein Desktopsecret.
Dann separat autorisierter Browserconsent plus bestehender Quota-GET je Provider, ohne Modellprompt;
Redirect-/Keyexpiry-/Reconnectbeleg, Secretredaktion und UX-Abnahme auf privatem Xvfb vor Release.
OpenRouter benötigt keine Clientregistrierung, aber dieselbe Host-/Lifecyclequalifikation.

Primärbelege (Reports enthalten weitere): [Kilo Profil](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/profile.ts), [Kilo Pass](https://github.com/Kilo-Org/kilocode/blob/9723be621429771ea47a2614b0ac433448023cec/packages/kilo-gateway/src/api/kilo-pass.ts), [Synthetic Parser](https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/utils/quota.ts), [xAI Keymetadaten](https://docs.x.ai/developers/rest-api-reference/inference/other), [OpenRouter PKCE](https://openrouter.ai/docs/guides/overview/auth/oauth), [Poe PKCE](https://creator.poe.com/docs/external-applications/sign-in-with-poe).
Die alte Kilo-TUI ist ergänzender Lokalbefehl-/Darstellungsbeleg, keine alleinige übertragbare Specquelle.
