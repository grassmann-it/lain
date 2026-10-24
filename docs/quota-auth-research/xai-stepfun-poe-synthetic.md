# xAI, StepFun, Poe und Synthetic: Kontingente und Anmeldung

Recherche 24.10.2026, lain-Basis `5dc916f`. Vorschläge, keine akzeptierte neue Architektur. Nur öffentliche Herstellerdokumentation und offizielle Repositories gelesen; keine privaten Credentials/Konten, Provider-API-Aufrufe, Anmeldung, Registrierung, Käufe, Dienste, Builds oder Implementierung. Die `.architecture-sparring/config.json` fehlt in diesem Worktree; AGENTS und architecture-sparring-Skill wurden gelesen. Strukturierte Belege und offene Punkte stehen in der gleichnamigen JSON-Datei.

## Ergebnis und Priorität

| Produkt | Was lain schon kann | Empfohlene Ergänzung | Ein Login für Inferenz + Monitor? |
| --- | --- | --- | --- |
| Poe | Restpunkte, History-Primitiv, vorbereiteter PKCE-Flow | Bestehenden OAuth-Baustein im Host fertig verdrahten | **Ja, offiziell:** Consent erzeugt denselben User-API-Key für beides. Clientregistrierung durch lain-Maintainer fehlt. |
| Synthetic Abo | Gleiches API-Key, `/v2/quotas`, altes `subscription`-Schema | Aktuelles offizielles Octofriend-Schema und passiven Header ergänzen | Ein **API-Key** genügt. Öffentlicher OAuth-/Keyprovisioning-Vertrag nicht belegt. |
| Step Plan | Inferenzrouten; Quota bewusst abgewiesen | Offiziellen Step-Code-Login + persönlichen Usagevertrag qualifizieren | **Quellseitig ja**, aber `/usage`-Deployment und Fremdclientverteilung offen. |
| xAI API-Team | Inferenz-Key; keine xAI-Quota | Management-Key-Monitor, optional Keyprovisioning | **Kein belegtes öffentliches OAuth für API-Team-Billing.** Ein Management-Key kann Inferenz-Key erzeugen und Billing lesen, mit passenden unterschiedlichen Rechten. |
| StepFun PAYG | `/v1/accounts`-Kontostand | Scope/Kontotyp/Region präzisieren, Organisation getrennt | Gleicher persönlicher API-Key liefert Inferenz + Kontostand; PAYG-OAuth nicht belegt. |
| Synthetic PAYG | Aboquote, kein Geldsaldo | PAYG getrennt als unbekannt/Console darstellen | Inferenz-Key vorhanden, öffentlicher Billing-Reader nicht belegt. |

Für „OAuth anmelden, lain macht den Rest“ ist **Poe sofort konkret planbar**. Step Plan ist die stärkste neue Untersuchungsspur; Synthetic liefert eine konkrete Adapterlücke. Diese Befunde ersetzen keine vorhandenen funktionierenden Kontingentfelder durch „fehlt“.

## Poe: offizielle delegierte Keyausgabe, bestehende Bausteine vervollständigen

Der [offizielle OAuth-Vertrag][poe-oauth] verwendet Authorization Code mit verpflichtendem S256-PKCE. Ein Maintainer legt auf `poe.com/api/clients` eine Anwendung samt Anzeigename/Redirects an und verteilt deren öffentlichen Client-ID-Wert mit lain. Benutzer brauchen keine eigene Clientregistrierung. `localhost`-Redirects funktionieren laut Doku ohne Vorregistrierung **für lokale Entwicklung**; ein Release muss Produktionsfreigabe, Hostform und Ports für die tatsächlich ausgelieferte Desktopcallback-Variante qualifizieren. Die localhost-Ausnahme belegt nicht automatisch jede `127.0.0.1`-/IPv6- oder beliebige Remote-Callback-Variante.

Der Browser öffnet `https://poe.com/oauth/authorize`, Scope `apikey:create`. Die Anwendung tauscht den kurzlebigen einmaligen Code an `POST https://api.poe.com/token` mit Verifier ein. Zurück kommen `api_key` und `api_key_expires_in` (Sekunden oder null); der Nutzer kann ein Ablaufdatum wählen. Ein Refresh-Grant ist nicht dokumentiert. Keyablauf erfordert erneute Verbindung. Der Key steht anschließend unter `poe.com/api/keys` zur Verfügung. Kein dokumentierter Monitor-only-Scope: dieser User-Key kann die Punkte des Nutzers ausgeben.

`PoeOAuthAttempt` enthält bereits PKCE, lokalen Statevergleich, Einmalverbrauch und Ablaufparser. Es fehlen Host-Callback-Lebenszeit, Browserstart, Fehler/Abbruch, tatsächlicher Tokenaustausch, SecretStore-Persistenz und Ablauf-/Reconnectführung. Das ist **keine fehlende OAuth-Kryptographieimplementierung**. `PoeQuotaAdapter` ist registriert und liest bereits [kombinierte Restpunkte][poe-balance] aus `GET /usage/current_balance`. Derselbe ausgegebene Key genügt für Inferenz, Balance und History. [Externe Apps][poe-guide] mit Benutzerkeys/OAuth sind ausdrücklich vorgesehen.

Ein Restwert beweist keine Monatszuteilung: Plan- und Add-on-Punkte sind zusammengefasst; weder ursprünglicher Umfang, Verbrauch, Aufteilung, Ablauf noch Reset stehen in diesem Vertrag. Keine USD-/Tokenumrechnung. Punkte werden kontoweit auch außerhalb lain verbraucht. Das bestehende `PoeUsageHistory`-Primitiv kann [History][poe-history] bereits seitenweise lesen: höchstens 100 Einträge/Seite, rückwärts nach Erstellungszeit, 30 Tage, Unix-Mikrosekunden, Cursor aus letzter `query_id`. `API`, `Chat`, `Canvas App` bleiben getrennte Herkunftslabels. Kein vollständiger Monat aus einer Seite; kein lain-only-Verbrauch aus API-Gesamthistory. Host-/UI-Zugriff auf dieses Primitiv fehlt im geprüften Suchstand. Balance hat kein Provider-Updatefeld/SLA; lokale Beobachtung und Alter sichtbar halten.

**Begrenzte nächste Probe:** offline Hostcallback gegen vorhandenen Helper prüfen (State, Replay, Deny, Abbruch, Ablauf, tatsächlich gewählte Callbackadresse), anschließend separat autorisierte Maintainer-Clientregistrierung und ein Consent, ein Balance-GET, optional eine Historyseite. Kein Modellaufruf für Monitoringqualifikation erforderlich.

## Synthetic Abo: aktueller Herstellerclient zeigt mehr als die Quota-Doku

Die [öffentliche Quota-Seite][syn-quota] dokumentiert unverändert `GET https://api.synthetic.new/v2/quotas` mit demselben API-Key und kostenfrei gegen das Abo. Ihr Beispiel `subscription.limit/requests/renewsAt` wird von `SyntheticQuotaAdapter` schon gelesen. Es darf als bewiesener Altvertrag weiter unterstützt werden; `requests` bleibt Anbietergewichtung statt erfundener gewöhnlicher Anfragen oder Tokens.

Der [offizielle Octofriend-Parser][syn-parser] an Commit `87d7fa9c` und dessen [Fetch-/Anzeigequelle][syn-fetch] verwenden **denselben Endpoint**, erkennen jedoch zusätzlich:

| Block | Tatsächliche Felder | Bedeutung für lain |
| --- | --- | --- |
| `planWeeklyLimit` | `percentRemaining`, `nextRegenAt`, `nextRegenPercent` | Aktuelle Wochenkredite als Prozent; nächste **Teilregeneration**, kein voller Reset. Der Herstellerclient bevorzugt diesen Block. |
| `weeklyTokenLimit` | `percentRemaining`, `maxCredits`, `remainingCredits`, `nextRegenCredits`, `nextRegenAt` | Älterer Wochenkreditvertrag; Kreditmengen als exakte Strings erhalten. Der Feldname beweist keine Tokenquote. |
| `rollingFiveHourLimit` | `remaining`, `max`, `nextTickAt`, `tickPercent` | Älteres gewichtetes Requestfenster, nur anzeigen, wenn tatsächlich gemeldet. |

Die [aktuelle Limitseite][syn-limits] sagt ausdrücklich: heute nur Wochenlimit, keine zusätzlichen Fünfstunden-/Monatslimits; alle 202 Minuten regenerieren 2 % der Wochenkapazität. Das ist **kein fixer wöchentlicher Vollreset**. Deshalb nicht aus dem Tarif neue 5h-Fenster erzeugen. Ältere Konten/Antwortformen dürfen nur anhand tatsächlich gemeldeter Daten erscheinen. Einmalige „Usage Reset/month“-Planleistung der Preisseite ist ebenfalls keine monatliche Verbrauchsquote.

Zusätzlich liest [Octofriend `state.ts`][syn-header] `x-synthetic-quotas` aus Inferenzantworten mit demselben Parser. Damit ist ein passiver, originqualifizierter Updatepfad primär belegt. Nur beim richtigen Provider/Account annehmen und Beobachtungszeit aus Headerempfang setzen; die Quelle belegt kein serverseitiges `updatedAt`. Neue Blöcke und altes `subscription` nicht addieren. Falls mehrere Formen zusammentreffen: präziser dokumentierte aktuelle Darstellung bevorzugen, alternative Provenienz erhalten; Schemafehler bedeuten unbekannt, niemals unbegrenzt.

**Empfehlung:** bestehenden Adapter um diese getrennten Formen ergänzen; Prozent und nächste Regenerationsmenge/-zeit brauchen passende Semantik, statt `nextRegenAt` in `ResetsAt` als Vollreset zu missbrauchen. Inferenz-Key genügt bereits. [Getting Started][syn-start] zeigt den Key erst nach Benutzerlogin; ein öffentlicher delegierter OAuth-/Device-/Create-Key-Vertrag ist in Dokumentation und geprüftem Herstellerclient nicht belegt. Websitecookies bringen für die bekannte Quota keinen Vorteil.

**Begrenzte nächste Probe:** reine Fixtures gegen gepinnte Herstellerquelle: neue/alte Blöcke, Koexistenz, 0/100 %, invalide Zahlen/Zeitzonen, fehlende Daten und stringgenaue Credits. Später separat autorisiert genau ein Quota-GET und Abgleich mit Usageansicht; für den Header zunächst keine bezahlte Inferenz auslösen.

## Synthetic API und Extra Usage: andere Abrechnungsdimension

Die [Preisseite][syn-pricing] unterscheidet Abo und zusätzliche Nutzung zu API-Preisen. Das ist kein Beleg, dass `/v2/quotas` Geldsaldo, PAYG-Credits oder Rechnungsverbrauch liefert. Die [Einstiegsdoku][syn-start] verlinkt zur Billingseite. Kein öffentlicher geeigneter PAYG-Geldsaldo-/Historyendpoint, Admin-/Readkey oder delegiertes Provisioning wurde verifiziert.

Abo-Prozent darf somit nicht das PAYG-Restbudget ersetzen. Automatischen bezahlten Überlauf nur nach bewusster Nutzereinstellung verwenden; keine Preise mal Restprozent als Cash-Budget ausgeben. Nächster begrenzter Schritt ist Herstellerklärung zu unterstützter Billing-API, Scope und Leserechten, keine private Console-Abfrage. Es wurde keine Nachricht gesendet.

## StepFun PAYG: Balance funktioniert bereits, Organisation separat halten

`StepFunQuotaAdapter` ist registriert, fordert API-Key plus exakte `https://api.stepfun.com/v1`-Basis und liest den [offiziellen `/v1/accounts`-Vertrag][step-accounts]. `object=account`, `type=prepaid|postpaid`, `balance` sind Kontostanddaten; `total_cash_balance`/`total_voucher_balance` sind **kumulierte Aufladungen/Geschenke**, keine Ausgabelimits oder Prozentnenner. Null ist bekannt. CNY folgt dem Preis-/Produktkontext, nicht einem Currencyfeld der Antwort. Geldsaldo hat keinen automatischen Wochenreset und kein Update-/Frischefeld. Der generische nichtnegative Zahlparser würde einen negativen Postpaidwert verwerfen; dessen reale Bedeutung ist ungeprüft, deshalb vor einer Änderung qualifizieren.

[Step-Code-Modelldoku][step-models] nennt auch `.ai` für Overseas. lain lässt derzeit nur `.com` zu. Die `.ai`-Variante benötigt eigenen offiziellen Accountvertrag/Verifikation, bevor Credentials an den weiteren Host gehen. Ein Plattform-Key hat keine bewiesene Step-Plan-Quota-Berechtigung.

Neue organisatorische Scopefalle: [Projektkeys][step-keys] werden ohne zusätzlichen Organisationsheader benutzt, belasten aber den **Organisations-Creditpool**, nicht den persönlichen Account. Die [Organisation-Regeln][step-org] beschreiben gleichzeitig Poolrest, Projektmonatsrest und Mitglied-im-Projekt-Monatsrest. Wirksam ist deren engste Grenze. Caps setzen am Monatsersten 00:00 zurück; die gelesene Regel nennt keine explizite Zeitzone. Diese Credits und Identitäten lassen sich nicht aus dem persönlichen `/accounts`-Beispiel ableiten. Die Konsole zeigt eigene/Organisationsnutzung und Exporte; ein entsprechender öffentlicher Reader ist im geöffneten Index nicht belegt.

**Empfehlung:** funktionierenden persönlichen Reader behalten, Typ/Scope sichtbar machen; organisatorische API-Keys nicht automatisch als persönlichen Cashpool normalisieren. Persönliche PAYG-Inferenz und Balance können denselben Key verwenden. Schlüsselanlage bleibt Consolearbeit; offizielles Step Code bietet für PAYG API-Keyprofile, keinen belegten delegierten PAYG-Browserlogin.

**Begrenzte nächste Probe:** offline Prepaid/Postpaid/Null getrennt normalisieren. Später ein autorisierter persönlicher Account-GET und Währungs-/Typvergleich; Organisations-/Overseaskeys separat qualifizieren.

## Step Plan: konkrete offizielle Login- und Usagequelle, Deployment offen

Die bisherige lain-Doku hat keinen öffentlich dokumentierten Plan-Reader gefunden und weist Plan-Keys zu Recht am PAYG-Reader ab. Neu liefert **das offizielle Step-Code-Repository** einen engeren, stärkeren Beleg. Commit `81e79bc4` enthält einen persönlichen Usagevertrag und einen Login, der Inferenz-Key und optionale Accountsession gemeinsam liefert. Das rechtfertigt eine begrenzte Untersuchung, noch keine Behauptung funktionierenden Livezugangs.

[Loginquelle][step-login] und [Providerwrapper][step-provider] zeigen keinen generischen Standard-OAuth-Codeflow, sondern einen ausgehenden Cloud-Pollingflow:

1. Lokalen zufälligen `poll_token` erzeugen; nur im Authorizationheader führen.
2. `POST {platform origin}/api/stdhttp/v1/cli-login/init` mit `profile`, `origin`, `client={name,version,platform}`.
3. Vom bestätigten Plattformorigin `.../cli-login-remote?flow_id=...` im Browser öffnen. `flow_id` allein ist kein einlösbares Secret.
4. `GET .../api/stdhttp/v1/cli-login/poll/{flow_id}` mit Polltoken bis `ready`, Ablauf oder Deny.
5. `credential.api_key`, optional `uid` und `credential.login` speichern; mit Erfolg Polling stoppen.

Der Quellcode bezeichnet Clientmetadaten ausdrücklich als Auditdaten, nicht Authentifizierung. Es gibt darin kein `client_id` und kein öffentliches Fremdclient-Registrierungsportal. **Das beweist keine beliebige Drittanwendungszulassung.** Ein lain-Versuch muss wahrheitsgemäße Clientidentität verwenden und eine vom Provider unterstützte Verteilung/CLI-Handoff klären. Nicht Step Code imitieren.

Der Wrapper benutzt den erhaltenen bestehenden API-Key als OAuth-`access`, einen statischen Refreshmarker und lokal unbegrenzte Laufzeit. Das ist keine Garantie, dass der Anbieter ihn nie widerruft. Die optionale Accountsession enthält Access-/Refresh-/Device-/Appdaten und hat einen gesonderten [Passport-Refreshpfad][step-refresh] mit `Oasis-Token`-Cookie. Diese Session ist **nicht** das Modellcredential und für den belegten Usage-GET nicht notwendig. Ein minimales lain-Design muss sie nicht nur vorsorglich übernehmen. Auth-Lifecycle gehört in einen Credentialowner, nicht in den Quotaadapter.

Der [Usagecontroller][step-usage] leitet ab:

- CN: `GET https://api.stepfun.com/step_plan/v1/usage`.
- Global: `GET https://api.stepfun.ai/step_plan/v1/usage`.
- Bearer ist der **im persönlichen Planlogin gelieferte API-Key**; gespeicherte Profile `step_plan`/`step_plan_oversea`. Explizites `STEP_API_KEY`, `--api-key`, Gatewaykeys, Plattform- und unbekannte Profile werden ausgeschlossen, selbst wenn Inferenz möglich ist.

Schema 1 verlangt `scope=personal_account`, `account.id` als exakten Dezimalstring und `observed_at_ms`. Gegen Login-UID prüfen. `plan.family=coding` liefert getrennte `five_hour`-/`weekly`-Fenster mit `limited`, `used_percent`, `resets_at_ms`. `plan.family=token` liefert `quota.credit_buckets` für `subscription` und `topup`, exakte Stringmengen `total`/`remaining`, Providerprozent sowie `resets_at_ms`/`expires_at_ms`. `quota.state=partial` ist kein Nullsaldo; unbekannte Buckettypen unsupported, `allowed/reason` gesondert erhalten. Monatsabo und Add-on nicht mit Codingfenstern addieren. Die [aktuelle Token-Plan-Preisseite][step-pricing] erklärt monatliche Credits, Restverfall, separat 30 Tage gültige Add-ons und frühesten Ablauf zuerst; sie beweist den konkreten Kontotarif nicht.

**Entscheidende Einschränkung:** die [offizielle Account-Doku im Repository][step-usage-doc] sagt ausdrücklich, dass das Backend Schema 1 erst auf `/step_plan/v1/usage` bereitstellen muss. `404` bedeutet dort „nicht deployed“. Quellvertrag ist belegt; öffentlicher Rollout wurde nicht getestet. Kein Endpoint wurde hier aufgerufen.

Herstellerclient-Frischepolitik: 30 Sekunden Cache, danach/bei Resetgrenze stale; nach fünf Minuten keine Anzeige des alten Snapshots. 12 Sekunden Timeout, Singleflight, Abbruch und `Retry-After`; 401/403 löschen Snapshot, keine Credentials. Das sind sinnvolle Integrationsreferenzen, kein zugesichertes Backend-SLA. lain hat bereits 5-Minuten-Snapshots; Planfenster benötigen feinere `FreshUntil`/Providerbeobachtung als dessen bloßen Standard.

**Empfehlung:** zuerst separate offline Plan-Normalisierung und Herkunfts-/Identitätsprüfung planen. Dann vom Provider unterstützten eigenen Client oder bewusstes offizielles CLI-Handoff qualifizieren. Ein kontrollierter Readversuch könnte Inferenz und Monitor nach einer Anmeldung verbinden. Bis zur Rolloutprobe „source-backed / deployment-unverified“, bei 404 weiterhin verfügbarer Consolelink. Keine Console-BFF-/Oasis-Sessions aus Drittmonitoren als primären Vertrag übernehmen.

## xAI API-Team: Management-Key statt Consumer-Anmeldung

Der [Managementvertrag][xai-management] trennt `management-api.x.ai` von `api.x.ai`. Ein Management-Key wird über Console Settings erstellt; Benutzer brauchen dafür `Management Keys` Read + Write oder Administratorhilfe. Das ist die Berechtigung zur Keyverwaltung, **kein Beleg für eine minimale Billing-Read-ACL**. Die genaue kleinste ACL-Zeichenfolge bleibt offen. [Validation][xai-auth] `GET /auth/management-keys/validation` benötigt selbst keine ACL und liefert `scope`, `scopeId`, `acls`; `teamId` ist deprecated. Ein Organisationsscope darf nicht still als Team-ID behandelt werden.

lain besitzt heute bereits `QuotaMonitorCredential` samt separatem Secret und ResourceId. Die alte Providerdoku, wonach überhaupt kein separater Credentialvertrag existiere, ist daher auf Basis `5dc916f` zu pauschal. **Es fehlen xAI-Provideroption, tatsächliche Validierung und Reader**, nicht die allgemeine Monitorsecret-Grundstruktur. Kein aktueller xAI-Adapter ist registriert.

Der [offizielle Billingvertrag][xai-billing] und [Herstellerproto][xai-proto] ermöglichen:

| Leseoperation | Kontoaussage | Grenzen |
| --- | --- | --- |
| `GET /v1/billing/teams/{team_id}/prepaid/balance` | signierter Centledger `total.val`, Änderungen | Kein Tokenbudget, kein Wochenfenster, kein Limitnenner. |
| `GET .../postpaid/invoice/preview` | laufende Monatsrechnung, `effectiveSpendingLimit`, `defaultCredits`, `billingCycle` | Vorsteuer/Nachsteuer und Korrekturen beachten; nicht einfach beliebige Rechnungsfelder vom Cap abziehen. |
| `GET .../postpaid/spending-limits` | automatisches/hartes/effektives und optional weiches Monatslimit | Limit begrenzt Postpaid, nicht den vorher verbrauchten Prepaidsaldo. |
| `POST .../usage` | Aggregation für expliziten Zeitraum, `usd`/SUM, Zeitzone | `limitReached` bedeutet Teilresultat; Query-Ende exklusiv, nicht unbemerkt einen Monat behaupten. |

`Cent.val` ist int64, JSON oft String. Das REST-Beispiel zeigt nach einem Kauf `total.val=-1000`; Proto definiert Kauf negativ, Verbrauch positiv. Deshalb signiert einlesen, kein `abs()`, keine pauschale Abweisung negativer Salden. Ein spendbarer positiver Betrag durch Vorzeichenumkehr muss gegen tatsächliche Console-/Ledgersemantik qualifiziert werden. Getrennte Prepaid-/Postpaidzeilen zuerst; kein künstliches „Gesamt-Restprozent“. Keine Providerfrische-SLA gefunden: Abfragezeit, angefragtes Fenster und Datenzeit gesondert speichern.

[Keymanagement][xai-guide] kann `POST /auth/teams/{teamId}/api-keys` einen neuen Inferenz-Key erzeugen; `apiKey` erscheint nur bei Erstellung. Modell-/Endpoint-ACL, Ablauf, qps/qpm/tpm und Propagationsabfrage existieren. Somit kann **ein ausreichend berechtigter Managementzugang** das Setup um API-Inferenzkey plus Monitor automatisieren. Das ist ein gesonderter Schreibschritt mit breiterer Berechtigung als Monitoring; ein Billing-Readkey allein kann nicht als Keycreator angenommen werden. Kein Kauf/Topup und keine Ausgabenlimitänderung gehören dazu.

Auch ohne Management-Key bietet [exakte Antwortkosten][xai-cost] `usage.cost_in_usd_ticks`, 1 USD = 10^10 Ticks, bereits Inferenz-Key-gestützte Messung. Das umfasst Toolkosten und Discounts pro Anfrage; bei REST/OpenAI-Streaming im finalen Usagechunk mit `include_usage`. Das liefert lain-beobachtete Ausgaben, **keinen kontoweiten Geldsaldo**. Keine entsprechende bestehende Erfassung im geprüften lain-Suchstand gefunden.

### Grok Build als separate Alternative

[Offizielles Grok CLI][grok-cli] unterstützt Browser-/Device-Login. Seine [offizielle Configquelle][grok-config] nennt `https://auth.x.ai`, einen festen Firstparty-Client, `grok-cli:access` und `api:access` neben weiteren Scopes. Das [offizielle Billingmodul][grok-billing] braucht grok.com-Auth und liest seine CLI-Proxyroute `billing?format=credits` mit `creditUsagePercent`, `currentPeriod`, `prepaidBalance`; alte Monatsfelder nur Fallback. Diese Oberfläche ist **nicht API-Team-Managementbilling**. Auch `api:access` im Token ist kein nachgewiesenes Management-Billingrecht.

Die [X-Subscription-Doku][grok-pool] beschreibt wöchentliche Sitzpools; selbstverwaltete Teams haben pro Sitz eigene Wochenpools und separaten zusätzlichen Prepaidsaldo. Wer konkret Grok Build integrieren will, kann dessen offiziellen CLI-/ACP-Weg gesondert untersuchen. Kein Fremdclientregistrierungs-/Verteilungsvertrag wurde verifiziert; feste Firstparty-ID nicht als automatisch nutzbaren lain-Client behandeln. Consumercredentials oder Scraping dürfen den API-Team-Reader nicht „reparieren“.

**Begrenzte nächste Probe:** offline signierte-Cent-/Scope-/Partial-Fixtures. Erst später mit separat autorisiertem Management-Readkey: Validierung + ein Prepaid-GET + ein Invoicepreview, gegen Console vergleichen. Keine Topups, Limitänderungen, API-Keyerstellung oder Consumeranmeldung in dieser Probe.

## Übergreifende Integrationsgrenzen

Die kleinste sinnvolle Erweiterung ist provider-/produktgenau: Poe-Consent sofort auf dessen vorhandenen API-Key-/Balancepfad führen; Synthetic-Schema ergänzen; Step Plan Quelle+Rollout getrennt qualifizieren; xAI Managementsecret neben Inferenzsecret anbieten. Alle Snapshots brauchen Quelle, Scope, Beobachtungszeit und bekannte/fehlende Fenster. Poolteilung erst bei belegter Account-/Teamidentität. Prozentfenster, regenerierende Credits, Geldsaldo und Inferenz-Antwortkosten sind verschiedene Aussagen.

Die Drittmonitore DSH/CodexBar erschienen als Suchhinweise für Step-Console-BFF/Oasis-Token bzw. xAI. Empfehlungen/Endpoints oben beruhen stattdessen auf geöffneten Herstellerquellen. Sessionbasierte Varianten sind als separate Alternativen bewertet; weder deren breite Sessions noch private Consoleendpoints wurden verwendet. Kein negativer Befund bedeutet, dass ein Anbieter grundsätzlich keinen privaten Endpoint besitzen könne; er bedeutet, dass kein geeigneter öffentlicher Vertrag in den geöffneten Quellen belegt wurde.

[poe-oauth]: https://creator.poe.com/docs/external-applications/sign-in-with-poe
[poe-balance]: https://creator.poe.com/api-reference/getCurrentBalance
[poe-history]: https://creator.poe.com/api-reference/getPointsHistory
[poe-guide]: https://creator.poe.com/docs/external-applications/external-application-guide
[syn-quota]: https://dev.synthetic.new/docs/synthetic/quotas
[syn-start]: https://dev.synthetic.new/docs/api/getting-started
[syn-pricing]: https://synthetic.new/pricing
[syn-limits]: https://synthetic.new/rate-limits
[syn-parser]: https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/utils/quota.ts
[syn-fetch]: https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/components/menu-quota-indicator.tsx
[syn-header]: https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/state.ts
[step-accounts]: https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md
[step-keys]: https://platform.stepfun.com/docs/zh/guides/organization/api-keys.md
[step-org]: https://platform.stepfun.com/docs/zh/guides/organization/credits.md
[step-pricing]: https://platform.stepfun.com/docs/zh/step-plan/pricing.md
[step-models]: https://platform.stepfun.com/docs/zh/step-code/configuration/models.md
[step-login]: https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/login-client.ts
[step-provider]: https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/index.ts
[step-refresh]: https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/login-refresh.ts
[step-usage]: https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/coding-agent/src/step/usage/account.ts
[step-usage-doc]: https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/coding-agent/docs/account-usage.md
[xai-management]: https://docs.x.ai/developers/rest-api-reference/management
[xai-guide]: https://docs.x.ai/developers/management-api-guide
[xai-auth]: https://docs.x.ai/developers/rest-api-reference/management/auth
[xai-billing]: https://docs.x.ai/developers/rest-api-reference/management/billing
[xai-proto]: https://github.com/xai-org/xai-proto/blob/6499fba5e5081a25a9504c96d01dc396adf35d2b/proto/xai/management_api/v1/billing.proto
[xai-cost]: https://docs.x.ai/developers/cost-tracking
[grok-cli]: https://docs.x.ai/build/cli/reference
[grok-config]: https://github.com/xai-org/grok-build/blob/2bdd1d6a6369de0e8c68132ea4539e9abd9e14a8/crates/codegen/xai-grok-login/src/config.rs
[grok-billing]: https://github.com/xai-org/grok-build/blob/2bdd1d6a6369de0e8c68132ea4539e9abd9e14a8/crates/codegen/xai-grok-shell/src/extensions/billing.rs
[grok-pool]: https://docs.x.ai/x-subscription/usage-limits
