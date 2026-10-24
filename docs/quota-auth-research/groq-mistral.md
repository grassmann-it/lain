# Groq und Mistral: Quota-, Usage-, Budget- und Login-Recherche

Stand und Abrufdatum: **24.10.2026**. Lain-Bezugsstand: `5dc916fcea702f2a196520903937570745e26dac` (`main`). Diese Datei ist ein Recherche-/Architekturvorschlag, keine akzeptierte Produktentscheidung und keine Implementierung. Nur öffentliche Herstellerdokumentation und öffentliche offizielle SDK-/CLI-Quellen wurden gelesen. Keine Credentials, Anbieterabfragen mit Authentifizierung, Browserkonten, Logins, Registrierungen, Käufe, Builds oder Services. Kein Live-Nachweis für einen Nutzeraccount.

## Ergebnis

| Anbieter / Zugang | Jetzt in lain | Unabhängiges Polling öffentlich belegt | Bequemer Login | Konsequenz |
| --- | --- | --- | --- | --- |
| Groq Free/Developer | Sechs passive Antwortheader durch Core → Host → Quota-Service/UI | Kein geeigneter allgemeiner Usage-/Spend-/Remaining-Endpunkt in den geprüften öffentlichen Quellen qualifiziert | Key aus Console; kein offizieller Drittanbieter-OAuth-/Device-/CLI-Bootstrap gefunden | Passive Quelle behalten; kein zusätzlicher Monitor-Key erforderlich. Geldbudget bleibt unbekannt. |
| Groq Enterprise | Derselbe passive Pfad | Prometheus/VictoriaMetrics: Requests, Tokens, Latenzen, Cache, nach Modell/Projekt; fünfminütige Raten | Derselbe Groq-API-Key dokumentiert, aber Enterprise-Freischaltung separat | Optionaler Usage-/Performance-Adapter möglich; kein Geld-/Restquotenadapter daraus ableiten. |
| Mistral Free/Pro/Education/Team | Inferenz-/Modellkatalog, **kein** Quotaadapter | Offizielle Usage-/Spend-/Rate-Admin-API ausdrücklich nicht verfügbar | Offizieller Vibe-CLI-Browserflow provisioniert einen API-Key | Komfort für passende Vibe-/Inferenzroute untersuchen; dieser Login schafft keine Reportingrechte. Dashboard ist der belegte Budgetweg. |
| Mistral Enterprise | Kein Admin-Quotaadapter oder Monitorzugang registriert | Admin API Preview: Usage, detaillierte Usage-Abfrage, Spend-Limit samt aktueller Nutzung, konfigurierte Raten | Dedizierter organisationsgebundener Admin-Key, kein gewöhnlicher Inferenzkey | Konkreter optionaler Admin-Leseadapter möglich; Header-/Scope-Widersprüche und Schema-/Frischenachweise vor Aktivierung klären. |

**Wichtiger Unterschied:** Mistral hat inzwischen einen offiziellen Browser-Login für seinen Vibe-CLI. Das ist öffentlich belegter Credential-Bootstrap und darf nicht als „nur manuelle API-Key-Erstellung verfügbar“ zusammengefasst werden. Der Ablauf liefert einen API-Key, nicht belegte OAuth-Access-/Refresh-Tokens. Für Admin Reporting verlangt Mistral ausdrücklich eine andere Credentialklasse.

## Groq

### Tatsächlicher lain-Stand

`proxy/adapter/internal/lain/quota_observations.go` speichert die sechs erlaubten Header aus normalen, eindeutig zugeordneten Groq-Chatantworten; `src/Lain.Host/Quota/GroqQuotaAdapter.cs` liest den privaten Core-Endpunkt und ist in `src/Lain.Host/Program.cs:86` registriert. Es handelt sich nicht mehr um einen alleinstehenden Headerparser. Die bestehende Prozessqualifikation in `docs/provider-implementation-2026-10-24.md` und `tests/acceptance/evidence/providers-20261024/groq-process.json` ist synthetisch. Ein echtes Headerereignis am Nutzeraccount fehlt weiterhin. Hier wurden keine Tests wiederholt.

Der Adapter nutzt `ObservedAt`/`DataAsOf` der ursprünglichen Antwort. Ein Refresh des lokalen Core-Snapshots macht sie nicht jünger. `FreshUntil` ist spätestens Beobachtung + fünf Minuten beziehungsweise der frühere übermittelte Reset. Stale Werte werden nicht lokal aufgefüllt. Ohne Beobachtung: `awaiting-observation`, kein Wert 0. Die aktuelle Scope-Beschriftung lautet `organization-model-observed-via-account`; angesichts der Projektgrenzen ist dies keine ausreichend belegte Poolidentität für alle Antworten.

### Quellen, Daten und Grenzen

1. **Antwortheader [G1].** `x-ratelimit-{limit,remaining,reset}-requests` beziehen sich ausdrücklich auf **RPD**, `...-tokens` auf **TPM**. `retry-after` ist nur bei 429 gesetzt und in Sekunden. Resetbeispiele `2m59.56s`/`7.66s` sind Dauern relativ zur Antwort, keine Unixzeit. Die übrigen Header sind laut Doku immer enthalten. Sie geben zwei beobachtete Ratenfenster wieder, nicht RPM/TPD/ASH/ASD/ITPM/OTPM vollständig und nicht Monatskosten oder Credits. Dokumentarische Beispielwerte sind keine Accountquote.
2. **Organisation und Projekt [G1, G2].** Rate-Limits gelten organisationsweit; Projekte können engere modellbezogene Raten haben. Projektkeys werden im gewählten Projekt erstellt. Die Organisation bleibt die gemeinsame Obergrenze. ITPM/OTPM werden nur auf Organisationsebene konfiguriert. Ohne belegte Org-/Projekt-ID darf lain weder zwei Keys als unabhängige Budgets noch alle Modellzeilen als separaten Geldpool behandeln. Die Headerdoku erklärt nicht eindeutig, welche der überlappenden Projekt-/Orggrenzen die sechs Header im konkreten Fall darstellen. Diese Zuordnung ist offen.
3. **Enterprise-Metrics [G3].** Basis `https://api.groq.com/v1/metrics/prometheus`, darunter dokumentierte Prometheus-Pfade `/api/v1/query`, `/api/v1/query_range`, `/api/v1/series`, `/api/v1/labels`, `/api/v1/label/<label_name>/values`, `/api/v1/status/tsdb`. Authentifizierung: `Authorization: Bearer <Groq API key>`. Enterprise-only, Freischaltung über Enterprise-Team. Keine zusätzliche OAuth-Identität ist dokumentiert; ein gewöhnlicher Free-/Developerkey beweist keine Berechtigung.
4. **Was Metrics tatsächlich liefert [G3].** `model_project_id_status_code:requests:rate5m`, `model_project_id:tokens_in:rate5m`, `model_project_id:tokens_out:rate5m`, Token-/Latenzhistogramme und Cache-Hits/-Misses. Alle werden nach Modell und Projekt-ID aufgeteilt, teils nach Status und Histogrammgrenze `le`. Es sind **Raten über fünf Minuten**, keine kumulativen Rechnungssummen, keine konfigurierten Grenzwerte und keine Restbudgets. Ratenintegration kann höchstens eine ausdrücklich geschätzte Usage ergeben; sie ist kein exakter Monatszähler. Exportverzug, Scrapeintervall, Retention und garantierte Vollständigkeit sind auf der Seite nicht angegeben. Fünf Minuten Glättungsfenster sind kein Frische-SLA. Prometheus-Timestamps erhalten; Abrufzeit getrennt ausweisen. Kein Budget-Reset für solche Raten erfinden.
5. **Geldbudget [G4, G5].** Developer ist PAYG, Zahlungsmethode erforderlich. Console Usage zeigt Nutzung/Kosten annähernd in Echtzeit. Die Spend-Limits-Funktion ist für bezahlte Accounts mit Organisation-Ownerrechten dokumentiert: USD, gesamtes Org-Budget über alle Keys, monatlich, Reset am Ersten des Monats; Zeitzone nicht genannt. Aktualisierung alle **10–15 Minuten**, deshalb mögliche Überziehung. Bei Erreichen blockiert neue Inferenz mit HTTP 400, `blocked_api_access`; kein gewöhnliches Raten-429. Die Doku zeigt Consolebedienung, keinen öffentlichen Spend-Read-Endpunkt. Eine Anzeige im Dashboard ist kein Nachweis eines unterstützten öffentlichen API-Vertrags.

### Login, Keyverwaltung und Rechte

Der offizielle Quickstart [G6] führt zur Console-Key-Erstellung und `GROQ_API_KEY`; offizielle Python-SDK-Oberfläche [G7] enthält Inferenz, Modelle, Batches und Dateien. Kein öffentlicher Org-/Projekt-/Usage-/Billing-/Keymanagement- oder OAuth-/Device-Clientvertrag wurde dort qualifiziert. Das ist eine Aussage über den geprüften öffentlichen Umfang, kein Beweis, dass Groq intern keine weiteren APIs betreibt. Ein Browser-SSO zur Console ist keine delegated API-Berechtigung. OAuth für externe MCP-Connectoren ist ebenfalls keine Groq-Accountautorisation für lain.

Console-Projektrollen [G2]: Owner volle Verwaltung, Developer laut aktueller Doku derzeit wie Owner, Reader nur lesend einschließlich Usage/Spend. Spend-Limits-Verwaltung [G4] verlangt spezifisch Owner; daraus keinen Reader-Schreibzugriff ableiten. Der Prometheus-Guide nennt API-Key plus Enterprise, aber keinen minimalen Key-/Org-Rollenscope und keine öffentlich registrierbare OAuth-App. Diese Details bleiben offen. Kein frei erfundener `usage.read`-Scope.

### Konkrete Empfehlung und nächster Nachweis

- **Jetzt:** vorhandene passive Quelle beibehalten. Konto/Route/Modell und ursprüngliche Zeit erhalten; Darstellung als RPD/TPM-Ratenfenster. Projektscope als nicht verifiziert kennzeichnen. Keine Inferenz nur zur Überwachung auslösen.
- **Fehlende Umsetzung:** optionaler Enterprise-Prometheusreader samt eigener Capability-/Entitlementanzeige; weitergehende Console-Usage/Geldadapter sind nicht implementiert und haben zusätzlich keinen qualifizierten öffentlichen Vertrag. Ein allgemeiner OAuth-Button ist derzeit nicht durch Quellen begründet.
- **Nächster Nachweis ohne neue Account-/Kaufannahme:** bei einem später ohnehin autorisierten normalen Groq-Request Status/alle sechs Header plus Account/Route/Modell und Beobachtungszeit sicher erfassen; Org-/Projektbezug aus ausdrücklich bereitgestellten Metadaten bestätigen. Dies qualifiziert die vorhandene Quelle im Nutzerkonto. Für Enterprise später ausdrücklich vorhandene Freischaltung bestätigen, dann eine reine Query mit bekanntem Key gegen den dokumentierten Metrics-Pfad und projektgefilterte Serie samt Timestamp lesen. Vorher minimalen Rollenzugang, Datenverzug und Retention beim Hersteller belegen. 403 = fehlendes Recht/Entitlement, nicht 0 und kein Grund für automatisches Upgrade.

## Mistral

### Tatsächlicher lain-Stand

`src/Lain.Host/Providers/Catalog/mistral.json` beschreibt eine direkte Studio-API-Key-Route; `DirectApiModelAdapter` kann `GET https://api.mistral.ai/v1/models` lesen. `quotaSupport` steht auf `unsupported`. Weder ein Mistral-Quotaadapter noch ein Mistral-Eintrag in `MonitorAccessCatalog.Options` ist vorhanden. Die Host-Registrierung enthält Mistral nicht. Modelllisten-Erfolg wäre kein Budget-, Plan- oder Reportingnachweis. Der aktuelle Setuptext verlangt manuelle Keyerstellung; der inzwischen dokumentierte Vibe-Browserflow ist dort noch nicht berücksichtigt.

### Produkt, Scope und Geld getrennt halten

Aktuelle offizielle Docs [M1, M2, M3] beschreiben Free mode, Pro, Education, Team und Enterprise als globale Pläne für Studio/API/Vibe Code. Enthaltene monatliche Usage wird geteilt und zuerst verbraucht. PAYG ist eine optionale vorausbezahlte Erweiterung, kein eigener API-Plan. Positive Credits **und** aktiviertes PAYG sind für weitere Nutzung erforderlich. Bei aufgebrauchten Credits können Anfragen über die enthaltene Usage hinaus mit 402 scheitern. Credits werden einschließlich Steuer belastet, Spend-Limits vor Steuer; Creditbalance und verbleibender Spend-Cap sind deshalb verschiedene Werte. Verarbeitung ist verzögert, geringfügig negativer Creditstand möglich; keine genaue Verzugsdauer angegeben.

API-Keys sind workspacegebunden und nutzen dessen Ressourcen/Quoten [M4]. Workspace-Caps überschreiben keine Orggrenze und sind keine zusätzlichen Credits. Workspace-Rate-Limits werden zwischen allen Keys dort geteilt [M5]. Die Übersicht nennt Orgverkehr und modellbezogene TPM/RPS; die Workspace-Doku nennt zusätzlich Tokens/Monat. Den tatsächlichen Scope daher an der jeweiligen API-Antwort qualifizieren, keine getrennten Budgets pro Key konstruieren.

Monatliche Spend-Caps werden laut Billing-/Workspace-Doku zum nächsten Monat wieder nutzbar; genaue Zeitzone/Uhrzeit ist nicht genannt. Das enthaltene Planbudget wartet laut Abo-Doku auf die nächste **Billingperiode**. Ein Kalender-Monatsreport ist damit nicht automatisch der Resetzeitpunkt jedes enthaltenen Abobudgets.

### Unabhängige offizielle Admin-Abfrage

Die Admin API ist **Enterprise-only, Preview und nicht self-service** [M6, M7]. Zugang über Account-/Sales-Team. Ein **dedizierter Admin API key**, erstellt im Enterprise-Bereich des Admin Panel, ist erforderlich. Er ist laut Authguide an genau eine Organisation gebunden. Erstellen verlangt Enterprise-Account-Adminzugang. Ein normaler User-/Inferenzkey funktioniert ausdrücklich **unabhängig von der Rolle des Nutzers nie** für Admin-Endpunkte. Optionales Ablaufdatum; Volltext nur bei Erstellung sichtbar. Kein öffentlich dokumentierter enger read-only Admin-Keyscope oder Admin-OAuthflow wurde gefunden.

Offizielle Basis: `https://api.mistral.ai/v1/admin`. Quellen [M8, M9] belegen folgende **Lesefunktionen**, die ohne neue Inferenz pollbar sind:

| Operation | Daten / Semantik | Nicht gleichsetzen mit |
| --- | --- | --- |
| `GET /usage?month=…&year=…&workspace_id=…&api_zone=…` | Fester Kalender-Monatsreport: `start_date`, `end_date`, `date`, `currency`, `prices`, kategorisierte Modell-/Capability-Usage, `vibe_code`; Workspace/Region optional | Garantierte aktuelle Creditbalance oder verbleibende Planallowance. `date` ist Referenzdatum, kein belegtes Datenfrischefeld. |
| `POST /usage/query` | **Leseabfrage trotz POST**: Pflicht `start` inklusiv und `end` exklusiv; Filter u. a. Workspace, Key, User, Org, Region, Usage-Typ; Gruppierung/UTC-Buckets; `limit`/`offset`, `metadata.has_more`; optional Aggregate und distinct `nb_api_calls` | Kosten: Referenz sagt ausdrücklich nur Mengen, keine Preise/Währung/berechneten Kosten. Unterschiedliche `billing_metric`-/`billing_group`-Einheiten nicht addieren. |
| `GET /spend-limit` | `limits.currency`, `limits.completion.{monthly_limit_reached,no_monthly_limit,total_usage,usage,usage_limit,vibe_usage}`, Geldwerte nullable; Payment-Failureflags | Prepaid-Creditbalance, enthaltenes Planbudget oder lokale Inferenzkosten. `no_monthly_limit=true` entfernt nur diesen Cap, macht weder Credits noch Nutzung unbegrenzt. |
| `GET /rate-limit` | `requests_per_second`; `tokens_limits_by_model` mit `tokens_per_minute` und `tokens_per_month` | Bereits verbrauchte/übrige Ratenkapazität oder Reset-Countdown. Statische Limits ohne passenden Verbrauch liefern keinen Reststand. |

Die expandierbaren Felder der gerenderten Referenz [M9] wurden einschließlich servergeladener Schemainformationen gelesen; ihre `.md`-Variante lieferte 404. `usage_limit`, `total_usage`, `usage` und `vibe_usage` können fehlen/null sein. Die Beschreibung nennt `total_usage` „Total current usage counted against the limit“, `usage` „Current usage counted against the limit“. Daher zuerst diese Felder roh und benannt qualifizieren; keine zusätzliche Summe aus `usage+vibe_usage+total_usage` bilden. Restcap nur nach Bestätigung, dass `total_usage` und `usage_limit` dieselbe Monatsgrenze abbilden, als **berechnet** `usage_limit - total_usage` anzeigen. Negative Überziehung nicht auf 0 verstecken. Ohne Nenner keine Prozentzahl.

**Widersprüche/Qualifikationsgrenzen:**

- Authguide [M7/M8] verlangt `x-api-key`; generierte Referenz [M9/M10] klassifiziert `AdminApiKey` als HTTP Bearer und zeigt `Authorization: Bearer`. Das ist ein echter offizieller Dokumentationswiderspruch. Implementierung darf nicht still beide Header senden oder bei Authfehler Credentials an andere Hosts ausprobieren. Vor Aktivierung aktuellen OpenAPI-Vertrag/Herstellerauskunft und eine autorisierte reine Leseprobe zum passenden Header bestätigen.
- Narrativ ist ein Admin-Key **eine Organisation**; detaillierte Usage-Referenz bietet Orgfilter „within the authenticated customer“ und `metadata.query_params.customer_id`. Das erlaubt keine automatische Annahme, der Key dürfe sämtliche Organisationen des Enterprise Account lesen. Erwartete Org-/Workspace-ID explizit festlegen und Antwort-/Filteridentität prüfen.
- Die Referenz bezeichnet `vibe_usage` im Monatsreport als **Legacy, immer 0**, „use the Vibe usage API instead“; das generierte Beispiel zeigt dennoch 14. `vibe_code`-Objekt und moderne Usage-Query separat qualifizieren. Legacyfeld nicht als zuverlässigen Vibe-Budgetverbrauch nutzen.
- Kein SLA für Reportverzug, Export-/Abrechnungsfrische oder genaue Resetzeitzone in den gelesenen Adminquellen. `ObservedAt=Abholung` ist zulässig, `DataAsOf=jetzt` ohne Lieferantentimestamp nicht. Zeitbereiche/Bucketzeit sind keine Vollständigkeitsgarantie.
- `/analytics/lechat/...` und `/analytics/vibe/...` sind im Guide [M8] als Produktaktivitätsberichte dokumentiert, keine Planbudget-API. Der Markdowntext enthält selbst ein TODO zur endgültigen Basis-Hostzuordnung; verlinkte gerenderte Analytics-Referenz lieferte 404. Diese Endpunkte deshalb noch nicht als gleich qualifiziert wie Billing behandeln.
- Kein offizieller öffentlich qualifizierter Admin-Leseendpunkt für **positive Prepaidbalance**, PAYG-Schalter oder genaue enthaltene Monatsallowance im hier gelesenen Billingvertrag. Die Console zeigt diese Werte; `/spend-limit` ersetzt sie nicht.

### Keymanagement und Bootstrap

Die öffentliche Admin-Keymanagement-Referenz [M10] enthält `GET /v1/admin/api-keys` (limit/offset/name, Metadaten mit Key-/Workspace-ID), `POST /v1/admin/api-keys` (Pflicht `user_id`, `workspace_uuid`, optional `name`, `expiration`) und `DELETE /v1/admin/api-keys/{key_id}`. Plaintext nur bei Erstellung. Das kann Enterprise-Inferenzkeys provisionieren, **setzt aber den Admin-Key bereits voraus** und löst dessen Bootstrap nicht. Keine dieser Mutationen wurde durchgeführt. Bestehende Inferenzkeys brauchen für Reporting keinen Austausch; Adminzugang wird separat angehängt. Breite Organisationsverwaltung als Zwangsvoraussetzung für Free-/Pro-Monitoring wäre weder technisch möglich noch angemessen.

Standardkeys [M4] gibt es für Studio, Vibe und Legacy Mistral Code. Workspace, Connectorzugriff und Ablauf sind beim Erstellen gebunden und danach nicht veränderbar. Entwickler verwalten eigene Keys im Studio-Userprofil, Orgadmins über das Admin Panel. Keyablaufpolicy kann Organisation/Workspace weiter einschränken. Ein Workspace-/Produktkey ist nicht ein neuer separater Aboplan.

### Offizieller Vibe-CLI-Browserlogin: echte neue Option, begrenzte Bedeutung

Die CLI-Doku [M11] sagt ausdrücklich: Browser-Sign-in für Mistral-Provider standardmäßig aktiv; der Flow provisioniert und speichert Credentials. Der offizielle öffentliche Vibe-Code am Commit `63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477` bestätigt [M12–M15]:

1. Lokaler zufälliger PKCE-Verifier und SHA-256/S256-Challenge.
2. Defaultbrowserbasis `https://console.mistral.ai`, API-Basis `https://console.mistral.ai/api`.
3. `POST /vibe/sign-in` mit `code_challenge` und `code_challenge_method: S256` auf dieser **Console-API**, nicht `api.mistral.ai/v1`.
4. Antwort: `process_id`, `sign_in_url`, `poll_url`, `expires_at`. Browser-/Poll-URLs werden gegen konfigurierte Ursprünge/Pfade validiert. Systembrowser wird geöffnet; Polling standardmäßig alle drei Sekunden, bis Ablauf/Ablehnung/Fehler/Abschluss.
5. Bei Abschluss `POST /vibe/sign-in/{process_id}/exchange` mit `exchange_token` und `code_verifier`, Ergebnis **`api_key`**. Geeigneter Workspace und Berechtigung zum Erzeugen eines Workspacekeys sind erforderlich; CLI hat eigens eine Fehlermeldung für nicht berechtigte Workspaces.
6. CLI persistiert im OS-Keyring, mit privatem `.env`-Fallback; kein untersuchter Access-/Refresh-Tokenzyklus. `vibe --setup` ist der offizielle Einstieg. Keine Ausführung/Installation hier.

Das ist **kein belegter RFC-8628-Devicegrant**: kein dokumentiertes `device_code`/`user_code`-/`verification_uri`-/OAuth-Tokenvertrag. Auch kein allgemeiner registrierbarer OAuthclient mit dokumentierten Scopes, Redirectregistrierung, Tokenrotation oder Admin-Delegation. Die Existenz eines First-Party-Flows ist ausreichend für „Browserlogin ist möglich“, aber nicht für eine pauschale Zusage „lain darf ihn als eigene öffentliche Integration verwenden“. Zulässige Drittanbieter-Nutzung, Stabilitätsvertrag und genaue Produkt-/Modellrechte des so erzeugten Keys sind vor einem eigenen Flow zu klären. Keine erfundenen Client-IDs/Scopes. Ein möglicher Anwenderweg wäre später der offizielle CLI-Setup und eine **explizit beauftragte** lokale Credentialübernahme; hier wurden keine privaten CLI-Dateien gelesen.

Der offizielle Vibe-Identitätsreader [M16] nutzt den Inferenzkey mit Bearer bei `https://console.mistral.ai/api/vibe/whoami`; liefert Plan-/Organisationstyp, Customer-ID, Tenantdomains und `primitive_access_scope`, **keine verbleibende Allowance**. Die dortige sechs Stunden dauernde CLI-Identitätscache-TTL ist keine Reportingfrische. Dies nicht mit dem getrennten öffentlichen SDK-`GET /v1/whoami` verwechseln: dessen Security verlangt Dashboard-User-/Service-Account-Context [M17], kein gewöhnlicher Key ist damit automatisch legitimiert. Keiner dieser Identitätswege macht einen Vibe-Key zum Admin-Key.

Die VS-Code-Doku [M18] beschreibt Browser-Sign-in noch als im Rollout; der bisherige terminalbasierte Keysetup bleibt dort beschrieben. CLI-Quelle/Doku belegen also eine konkrete Fähigkeit, aber nicht identischen Rollout auf jeder Oberfläche.

Workload Identity Federation [M19] ist ebenfalls eine offizielle schlüssellose Möglichkeit: Organisationsadmin registriert OIDC-Issuer, Workspaceadmin bindet Service Account, Kubernetes projiziert kurzlebige JWTs mit Audience `api-gateway`. Das ist Infrastruktur-/Workloadauth, **kein Desktop-Userlogin** und kein belegter Ersatz für den dedizierten Enterprise-Admin-Key.

### Konkrete Empfehlung und nächster Nachweis

- **Free/Pro/Team:** manuelle Studio-Keyroute funktionsfähig lassen; zusätzliche Einrichtung nicht erzwingen. Budgetanzeige mit gezieltem Admin-Panel-Link und verständlichem Status „öffentliche programmatische Abfrage für diesen Plan nicht verfügbar“. Lokale Lain-Usage separat benennen: nur durch lain beobachtete Requests/Tokens, keine Orgusage oder Restquote. Keine Browsercookies oder undokumentierten Console-Billingrouten als offizielle API ausgeben.
- **Loginverbesserung:** den offiziellen Vibe-Browserbootstrap als untersuchten Kandidaten aufnehmen. Zuerst Herstellervertrag/Unterstützung für externe Desktopintegration, Keyproduktscope und Nutzung auf der konkreten lain-Route bestätigen. Danach entweder einen eigenen nachweislich unterstützten Flow implementieren oder eine bewusste Importoption aus offiziellem CLIsetup; kein stilles Keyringlesen und kein behaupteter OAuthrefresh. Dieser Kandidat ist nicht durch fehlende lain-Implementierung „anbieterunmöglich“.
- **Enterprise-Reporting:** eigener Monitorcredentialtyp `mistral-admin-key` als Vorschlag, explizite erwartete Organisation/Workspace, Ablaufdatum optional, nie an Inferenz exportieren. Nur nach vorhandener Herstellerfreischaltung; keine automatische Registrierung/Kaufhandlung. Adapter getrennt für Org-Spendcap, konfigurierte Raten und Usage. Kein automatisches Keyprovisioning oder Spend-Limit-Schreiben erforderlich.
- **Nächster Nachweis:** aktuellen Admin-Authvertrag (Headerwiderspruch) klären; danach bei bereits berechtigtem Konto ausdrücklich autorisierte `GET /spend-limit`, `GET /rate-limit`, Monats-`GET /usage` und gegebenenfalls eine kleine `POST /usage/query`-Leseabfrage mit Org-/Workspacefilter. Redigierte Antwortschemas samt Währung, null/0, Monatszeitraum, Scope, Serverzeit und beobachtetem Verzug qualifizieren. Prüfen, welches Usagefeld wirklich den Cap belastet. 401/403, kein Enterprise, abgelaufener Key, fehlende Serie und null sind getrennte Zustände. Erst dann Restcap/Prozentberechnung und Adapterregistrierung aktivieren. Prepaid-/Planbudget bleibt unbekannt, bis eine eigene geeignete Quelle belegt ist.

## Quellenregister (alle abgerufen am 24.10.2026)

### Groq

- **G1:** https://console.groq.com/docs/rate-limits.md — Headersemantik RPD/TPM, Organisationslimits, 429/retry-after.
- **G2:** https://console.groq.com/docs/projects.md — projektgebundene Keys, Rollen, engere Projektlimits unter Orgobergrenze, Usage im Consoleprojekt.
- **G3:** https://console.groq.com/docs/prometheus-metrics.md — Enterprise-only, Basis/Pfade, Bearer-Key, `rate5m`, Modell-/Projektlabels.
- **G4:** https://console.groq.com/docs/spend-limits.md — Paid + Owner, USD-Orgcap, 10–15 Minuten Verzug, monatlicher Reset, `blocked_api_access`.
- **G5:** https://console.groq.com/docs/billing-faqs.md — Developer/PAYG, Zahlung, Usage-/Billingconsole.
- **G6:** https://console.groq.com/docs/quickstart.md — manuelle Console-Keyerstellung, `GROQ_API_KEY`.
- **G7:** https://github.com/groq/groq-python/blob/55066d94acca028c3cb74e050f2711b80ade6298/api.md — gelesene offizielle SDK-API-Oberfläche ohne Admin-/Spend-/OAuthverwaltung; auch https://console.groq.com/docs/api-reference.md gelesen.
- **G8:** https://console.groq.com/llms.txt und https://console.groq.com/sitemap.xml — öffentlicher Dokumentationsumfang für die begrenzte Nichtfund-Aussage.

### Mistral

- **M1:** https://docs.mistral.ai/admin/billing-usage/subscriptions.md — globale Pläne, geteilte Monatsusage, optionale PAYG-Erweiterung.
- **M2:** https://docs.mistral.ai/admin/billing-usage/usage-limits.md — Org-/Workspacecaps, Enterprise-Programmzugriff, Modellraten.
- **M3:** https://docs.mistral.ai/admin/billing-usage/billing.md — Creditbalance, Steuerunterschied, 402, Verarbeitung verzögert, Admin/Billing-Rolle im Panel.
- **M4:** https://docs.mistral.ai/admin/identity-access/api-keys.md — Workspace-/Produktkeys, Connectorrechte, Ablauf/Policy, keine plangebundenen Keys.
- **M5:** https://docs.mistral.ai/admin/workspaces/usage-limits.md — geteilte Workspaceraten, TPM/Monat, Monatscap unter Orgcap.
- **M6:** https://docs.mistral.ai/admin/admin-api/overview.md — Enterprise-only, nicht self-service, Preview, separater Adminkey.
- **M7:** https://docs.mistral.ai/admin/admin-api/authentication.md — Enterprise-Account-Admin erstellt dedizierten Orgkey; x-api-key, Standardkey nie ausreichend.
- **M8:** https://docs.mistral.ai/admin/admin-api/usage-metrics.md — konkrete Usage-/Spend-/Rate-Readoperationen, Produktanalytics, offener Analytics-Host-TODO.
- **M9:** https://docs.mistral.ai/api/endpoint/beta/admin/billing — tatsächlich gelesene gerenderte Referenz inklusive expandierbarer servergeladener Schemata; Monatsreport, Mengenquery, Spendfelder und Raten; Bearer/x-api-key-Widerspruch.
- **M10:** https://docs.mistral.ai/api/endpoint/beta/admin/api-keys — tatsächlich gelesene gerenderte Key-CRUD-Referenz; Admincredential erforderlich.
- **M11:** https://docs.mistral.ai/vibe/code/cli/api-keys-profiles.md — Browserflow Standard, Keyring/.env, `--setup`, Budget-/PAYGhinweise.
- **M12:** https://github.com/mistralai/mistral-vibe/blob/63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477/vibe/setup/auth/browser_sign_in.py — S256, Ablauf, Dreisekundenpolling, Ergebnis API-Key.
- **M13:** https://github.com/mistralai/mistral-vibe/blob/63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477/vibe/setup/auth/http_browser_sign_in_gateway.py — konkrete Console-API-Pfade, URLvalidierung, Exchange, Workspacefehler.
- **M14:** https://github.com/mistralai/mistral-vibe/blob/63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477/vibe/core/config/_defaults.py — Default Console-/API-Basen.
- **M15:** https://github.com/mistralai/mistral-vibe/blob/63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477/vibe/setup/auth/api_key_persistence.py — Keyring, .env-Fallback, Persistenz/Reconnect.
- **M16:** https://github.com/mistralai/mistral-vibe/blob/63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477/vibe/setup/auth/whoami.py — Vibe-Identitäts-/Tenantlookup mit Inferenzkey, keine Allowance, Identitätscache.
- **M17:** https://github.com/mistralai/client-python/blob/c5b1fcf7503a4151268a164598655e62ba29ae11/docs/sdks/users/README.md — getrennte SDK-Identität und Org-/Workspacelisten mit Dashboard-Context-Security.
- **M18:** https://docs.mistral.ai/vibe/code/vs-code-extension/install-authenticate.md — Extension-Rolloutgrenze für Browserauth.
- **M19:** https://docs.mistral.ai/admin/identity-access/workload-identity.md — OIDC-Service-Account-Workloadauth, kein Desktop-/Adminlogin.

Öffentliche Quellen wurden als Inhalte gelesen, nicht allein Suchtreffer übernommen. Ausfälle: anfängliche HTML-403 bei Groq wurden durch öffentlich angebotene `.md`-Seiten behoben; einige Mistral generierte API-`.md`-Routen sind 404, die gerenderten Billing-/Key-Seiten wurden stattdessen gelesen. Kein Kontozugriff wurde daraus abgeleitet.
