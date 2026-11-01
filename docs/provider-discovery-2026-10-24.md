# Weitere Provider für lain

**Prüfdatum: 24.10.2026.** Öffentlich zugängliche offizielle Dokumentation,
Preis- und Vertragsseiten; keine Accounts angelegt, keine Logins, vorhandenen
Credentials, Inferenzrequests oder authentifizierten Account-APIs verwendet.
Alle Kandidaten bleiben **live `not_tested`**. Recherche fortgeschrieben gegenüber
dem ersten Stand auf `d6e36ed`; Arbeitskopie beim Beginn der Ergänzung: `ad5471a`.
Die laufende CLI-/Tray-/UI-Abnahme und deren Dateien wurden nicht verändert.

Dies ist eine Entscheidungsgrundlage, keine Integrations- oder Vertragsfreigabe.
„Dokumentiert“ bedeutet Herstellerbeleg, „abgeleitet“ unsere Einschätzung,
„offen“ fehlender Nachweis. Ein dokumentiertes API-Format beweist weder die
Erlaubnis jedes Nutzungsszenarios noch fehlerfreie Kompatibilität jedes Modells.

Die [Erweiterung mit weiteren Kandidaten und lokalen Backends](provider-discovery-expansion-2026-10-24.md)
ergänzt diesen Bericht um ein breites Screening. Sie unterscheidet zusätzliche
Gatewayanschlüsse, offizielle Monitor-/History-APIs und noch offene Kandidaten.

## Empfehlung

**Budget-Nachtrag:** Die [aktuelle Nutzen-/Testkostenbewertung](provider-priorities-budget-2026-10-24.md)
priorisiert alle Kandidaten nach günstigem eigenem Testzugang. Sie ergänzt die
fachliche Eignung unten und stellt teure neue Abos zunächst zurück.

**Zuerst DeepSeek als Balance-Monitor, MiniMax als Abo-Monitor und Fireworks
für Gateway plus Quotas qualifizieren.** Scaleway rückt gegenüber der ersten
Recherche auf: Es bietet neben EU-Inferenz auch offiziell dokumentierte
Cockpit-/Billing-Abfragen für externe Verbrauchshistorie. Für den zunächst
geplanten Monitor mit **Restkontingenten und Resets** bleibt das nur eine
Ergänzung. Chutes ist fachlich interessant, verlangt aber eine klare Trennung
zwischen gelegentlicher Abo-Nutzung und automatisierter PAYG-Last.

| Kandidat | Gateway | Monitor ohne Inferenz | Externe Historie | Aufwand / Priorität |
| --- | --- | --- | --- | --- |
| **DeepSeek** | OpenAI, Responses und Anthropic dokumentiert | **Ja: Geldguthaben**, kein Reset/Prozent | Keine öffentliche Usage-History-API belegt | S Balance, M Gateway / zuerst |
| **MiniMax** | OpenAI, Responses und Anthropic; Abo/PAYG getrennt | **Ja, offiziell benannter Endpunkt**; Schema offen | Konsole; kein öffentlicher Text-Usage-Export belegt | M–H / zuerst Abo-Leseprobe |
| **Fireworks** | OpenAI, Responses, Anthropic | **Ja: Quotas**; Einheiten und Aktualität qualifizieren | Offizielle Mengen-/Kostenabfragen und CSV-Export | M–H / zuerst, Vertragsfrage beachten |
| **Scaleway** | OpenAI, eingeschränkte Responses | **Verbrauch/Kosten ja**, Rest/Reset nicht belegt | Cockpit-Zeitreihen, Billing nach Monat/Projekt | M Gateway, H History / danach |
| **Chutes** | OpenAI; native Anthropic-API nicht belegt | Offizielle Quota-Routen; Feldsemantik/Scopes offen | Export-Routen vorhanden, Accountabdeckung offen | H / nach Überlauf- und Vertragsklärung |
| **Cerebras** | OpenAI, Streaming und Tools | Geeignete öffentliche Balance-/Rest-API nicht belegt | Konsole mit Logs/CSV; Abruf-API nicht belegt | M Gateway, H Monitor / später |
| **Together AI** | OpenAI; Anthropic via lokalem Übersetzer | **Billing-Verbrauch ja, Beta**, kein Rest/Reset | Offizielle organisationsweite Mengen-/Kostenaggregate | M Gateway, H History / später |
| **SambaNova Cloud** | OpenAI, Streaming und Tools | Nur Antwortheader belegt; eigenständiger Monitor offen | Geeignete öffentliche History-API nicht belegt | M Gateway, H Monitor / Free-Tier-Kandidat |

S = kleines abgegrenztes Leseschema; M = Gateway-/Accountanschluss mit
Modellqualifikation; H = zusätzliche Schema-, Rechte-, Vertrags- oder
Abrechnungsarbeit. Keine Zeitschätzung und kein Implementierungsauftrag.
Alle acht ergänzen direkte Accounts; ein gleichnamiges Modell bei einem bereits
vorhandenen Provider ersetzt diesen Anschluss nicht.

## Gemeinsame Auswertungsregeln

- **Offizielle API**: öffentlich dokumentierter Anbieterzugriff. **Interne Web-API**:
  eine nur vom Dashboard verwendete Schnittstelle. **Antwortheader**: Beobachtung
  einer ohnehin erfolgten Anfrage. Für keinen Kandidaten wurde eine interne
  Dashboard-API als stabiler Monitoranschluss qualifiziert; keine Cookies übernommen.
- `fetched_at` belegt unseren Abrufzeitpunkt, nicht die Aktualität beim Anbieter.
  Datenstand, Abrechnungsverzug und Quota-Reset sind getrennte Angaben. Ohne
  Anbieterzeitstempel bleibt die Datenfrische teilweise unbekannt.
- Geldguthaben, Aboeinheiten, Tokens, Requests, Raten und GPU-Kapazität bleiben
  getrennt. Kein Verbrauchsprozent ohne belegten Bezugsrahmen; kein Reset aus
  `updateTime`, Monatsnamen oder einem kontinuierlich auffüllenden Token Bucket.
- Ein lokales Journal deckt nur durch lain geleitete Requests ab. Externe
  Accountdaten können andere Clients enthalten und verzögert sein. Beide Quellen
  nicht addieren, solange Identität, Abdeckung und Deduplizierung ungeklärt sind.
  Kontingentdifferenzen ergeben keine Request-/Tokenhistorie.
- **BYOK-Verteilung** bedeutet hier: Nutzer schließen ihren eigenen Anbietervertrag,
  hinterlegen ihren eigenen Schlüssel lokal und zahlen selbst. Der Verkauf der
  lain-Software, die Übertragung von Keys und der Weiterverkauf von Inferenz sind
  unterschiedliche Sachverhalte. Fehlendes ausdrückliches Proxyverbot ist keine
  ausdrückliche Vertriebserlaubnis; offene Klauseln sind unten benannt.

## 1. MiniMax — Abo-Monitor und Gateway, getrennte Zugangsarten

**Anschluss.** OpenAI-Basis `https://api.minimax.io/v1`, Anthropic-Basis
`https://api.minimax.io/anthropic`; Chat/Messages mit Streaming und Toolcalls,
zusätzlich eine Responses-Schnittstelle. OpenAI-Seite mit Bearer-Key;
Anthropic-SDK mit entsprechendem API-Key. `GET /v1/models` ist inzwischen
öffentlich dokumentiert. Subscription Key und gewöhnlicher PAYG-Key als
verschiedene Accountzugänge behandeln; zulässige Modelle je Zugang lesen bzw.
prüfen. Thinking und vollständige Assistant-/Tool-Historie müssen erhalten bleiben.
[OpenAI](https://platform.minimax.io/docs/api-reference/text-openai-api),
[Anthropic](https://platform.minimax.io/docs/api-reference/text-anthropic-api),
[Responses](https://platform.minimax.io/docs/api-reference/responses-create),
[Modellliste](https://platform.minimax.io/docs/api-reference/models/openai/list-models),
[Einrichtung eigener Tools](https://platform.minimax.io/docs/m-plan/other-tools).

**Zulässigkeit.** Die Produktdokumentation unterstützt eigene kompatible Tools
und Coding-Agenten ausdrücklich; parallele eigene Tools teilen ein Budget.
M Plan ist für persönliche interaktive Entwicklernutzung vorgesehen, für
Produktion wird PAYG empfohlen. Das ist keine unbeschränkte Freigabe für
unbeaufsichtigte Workerfarmen. Die Vertragsseiten ergänzen Dienst-/Tarifregeln
und schränken Übertragung/Weiterverkauf von Diensten und Zugangsschlüsseln ein.
Lokales persönliches BYOK ist durch den vorgesehenen Toolanschluss gestützt;
kommerzieller Vertrieb eines neutralen Clients ist nicht ausdrücklich als
lain-Szenario freigegeben. Modell-/Planrechte und konkurrierende oder weiterverkaufte
Dienstangebote separat klären. Keine feste Workerzahl aus älteren Tarifseiten
übernehmen: aktuell greifen dynamische Ratenbegrenzungen.
[FAQ](https://platform.minimax.io/docs/m-plan/faq),
[Terms, 30.03.2026](https://platform.minimax.io/protocol/terms-of-service),
[Paid Agreement, 19.08.2026](https://platform.minimax.io/protocol/paid-agreement).
Die Vertragsseiten lieferten beim direkten Abruf keine auswertbare Textfassung;
diese Vertragsaussagen stützen sich auf den Suchindex der offiziellen Seiten.
Vollständigen Vertrag bei späterer Einrichtung nochmals direkt prüfen.

**Monitor.** Offiziell genannt ist
`GET https://www.minimax.io/v1/token_plan/remains`, `Authorization: Bearer <API Key>`.
Damit ist die Route trotz Web-Host keine bloß vermutete interne Dashboard-API.
Ein vollständiges öffentliches Antwortschema, exakte Feldnamen für Rest/Limit,
Messgröße, Resetzeitstempel, Datenstand und ein minimaler Read-only-Scope wurden
nicht gefunden. Auch die tatsächlich akzeptierte Keyart ist mit einem vorhandenen
Subscription Key zu qualifizieren. Ältere inoffizielle JSON-Beispiele sind kein
API-Vertrag. Dokumentiert sind zwei unabhängig mit erster Nutzung beginnende
Fenster: 5 Stunden und 7 Tage; nach Ablauf beginnt erst die nächste Nutzung ein
neues Fenster. Video betrifft nur die Woche. Laufende Fenster nicht aus dem
Pollzeitpunkt neu starten. [Usage-Regeln](https://platform.minimax.io/docs/m-plan/usage-rules).

**Historie.** Die Usage-Seite enthält Analysen/Historie; `mmx quota` ist ein
Kontingentzugriff. Keine öffentliche Text-Request-/Token-/Kostenhistorie-API in
den geprüften Referenzen belegt. Ohne weiteren Nachweis nur das lokale lain-
Journal für durchgeleitete Requests anbieten; ein Quota-Snapshot ist keine Historie.
[Usage](https://platform.minimax.io/docs/m-plan/usage-rules).

**Gratis, Überlauf, Zugang.** Kein dauerhafter kostenloser Text-API-Tarif mit
allgemein garantierten Start-/Referral-Credits belegt; kostenloser Webchat wäre
kein API-Free-Tier. Kartenpflicht und Aktionsbedingungen bleiben für einen
konkreten internationalen Account offen. **Subscription Keys belasten laut FAQ
kein gewöhnliches PAYG-Kontoguthaben, verbrauchen nach Aboerschöpfung aber
gekaufte Credit Packs.** Diese gelten ein Jahr; ein allgemeiner Schalter gegen
diesen Verbrauch ist nicht belegt. Daher kein „nur Abo, niemals bezahlter
Überlauf“ versprechen, wenn Packs vorhanden sind. PAYG kennt optionales
Autobilling. Internationale `.io`-Verträge nicht auf chinesische Domains/Keys
übertragen; Länderzulassung/Verifikation bleiben kontospezifisch offen.
[FAQ](https://platform.minimax.io/docs/m-plan/faq),
[PAYG-Autobilling](https://platform.minimax.io/docs/faq/about-account).

**Erster Test, später mit autorisiertem vorhandenem Zugang:** ausschließlich
`remains` lesen; anonymisierte Antwort mit Keyart/Accountregion festhalten,
Einheiten und beide Fenster gegen die Konsole prüfen. Fehlende Resetfelder
bleiben unbekannt. Credit-Pack-Status separat nachweisen. Erst anschließend ein
begrenzt autorisierter Streaming-/Toolfortsetzungstest pro Modell. Monitor M–H,
Gateway M; beides aussichtsreich, noch nicht live qualifiziert.

## 2. DeepSeek — besonders klarer Balance-Monitor

**Anschluss.** `https://api.deepseek.com` für OpenAI Chat/Responses,
`https://api.deepseek.com/anthropic` für Anthropic; Streaming und Tools sind
modellbezogen dokumentiert. Auth mit eigenem API-Key, OpenAI/Balancelesezugriff
über Bearer. `GET /models` ist offiziell. Keine separate Read-only-Keyrolle für
Balance belegt: derselbe Key kann grundsätzlich auch Inferenz auslösen.
Die Responses-API ist zustandslos: `previous_response_id`, `store` und mehrere
andere Parameter liefern nicht die volle OpenAI-Semantik.
[Modelle/Fähigkeiten](https://api-docs.deepseek.com/quick_start/pricing/),
[Modellliste](https://api-docs.deepseek.com/api/list-models/),
[Responses und SSE](https://api-docs.deepseek.com/guides/responses_api/),
[Anthropic-Grenzen](https://api-docs.deepseek.com/guides/anthropic_api/).

**Zulässigkeit.** Die speziellen Open-Platform-Terms vom 22.04.2026, wirksam
29.04.2026, erlauben die Integration in interne und externe Anwendungen/Tools.
Das ist eine deutlich bessere Grundlage als allein Protokollkompatibilität.
Coding-Agenten werden offiziell eingerichtet; parallele Requests sind innerhalb
der Account-/Modelllimits vorgesehen. Keys dürfen weder geteilt/veröffentlicht
noch in Browser-/Clientcode offengelegt werden. Abgeleitet: ein geschützter
lokaler BYOK-Host für den Kontoinhaber passt dazu; ein eingebetteter gemeinsamer
Produktschlüssel nicht. Kommerzielle eigene Anwendungen sind vom
Entwicklerzuschnitt gedeckt; Serviceweiterverkauf und Markenauftritt unterliegen
weiterhin Einschränkungen. Kein Coding-Abo belegt.
[API-Vertrag §§1–3](https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html),
[Allgemeine Terms](https://cdn.deepseek.com/policies/en-US/deepseek-terms-of-use.html),
[Coding-Tools](https://api-docs.deepseek.com/guides/coding_agents/),
[Concurrency/Isolation](https://api-docs.deepseek.com/quick_start/rate_limit/).

**Monitor.** Offizielles `GET https://api.deepseek.com/user/balance` ohne
Inferenz. Antwort: `is_available` und `balance_infos[]` mit `currency` (`CNY`,
`USD`), `total_balance`, `granted_balance`, `topped_up_balance`; Geldbeträge sind
Strings. Dezimalgenau verarbeiten, Währungen getrennt halten. Gesamtbetrag
enthält Bonus plus Aufladung, also nicht nochmals addieren. `granted_balance`
bezeichnet noch nicht verfallenes Bonusguthaben. **Keine** Felder für ursprüngliches
Budget, Verbrauch, Prozent, Reset, Bonusablauf oder Anbieterzeitstempel. Nur
Abrufzeitpunkt anzeigen; Aktualitäts-SLA nicht belegt.
[Antwortschema](https://api-docs.deepseek.com/api/get-user-balance/).

**Historie.** Die Hilfe verweist auf Nutzung nach API-Key in der Konsole;
eine öffentliche Request-/Token-/Kostenhistorie-API wurde nicht belegt.
Responses mit `store:false` sind ebenfalls kein später abrufbares Requestarchiv.
Lain kann nur seine durchgeleitete Nutzung selbst protokollieren.
[Hilfe](https://api-docs.deepseek.com/faq/),
[Responses](https://api-docs.deepseek.com/guides/responses_api/).

**Gratis, Überlauf, Zugang.** Kein allgemein zugesagter dauerhafter Free Tier
oder aktueller universeller Trial-/Referral-Betrag belegt. Ein mögliches
Bonusguthaben beweist keinen kostenlosen Accountanspruch; Ablauf und
Karten-/Zahlungsanforderungen bleiben offen. Bonus wird vor bezahltem Guthaben
verbraucht: kein belegter „nur Bonus“-Schalter. Bei unzureichender Balance ist
HTTP 402 dokumentiert; das beweist keinen centgenauen Stopp bereits laufender
paralleler Requests. Keine aus dem Schema ableitbare Gratis-/Resetgarantie.
Aktuelle serverseitige Search-/Fetch-Aufrufe über Coding-Tools sind nur bis
01.11.2026, 00:00 Beijing (31.10., 16:00 UTC) kostenlos; kein kostenloses
Sprachmodell daraus ableiten. Länderzugang ist laut Vertrag nicht überall
garantiert; Sanktionen/Exportregeln gelten, kein vollständiger Länderkatalog geprüft.
[Preise/Abzug](https://api-docs.deepseek.com/quick_start/pricing/),
[Fehlercodes](https://api-docs.deepseek.com/quick_start/error_codes/),
[Vertragsgebiet](https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html).

**Erster Test:** offizielles Beispiel als Fixture mit Dezimalstrings, mehreren
Währungen und `is_available=false`; dann eine autorisierte reine Balanceleseprobe.
Gateway erst nach Modellliste, SSE-Endereignis, Tools und Usage prüfen. Aufwand
S für Balance, M für Gateway. **Beides geeignet**, Monitor ausdrücklich als Geldanzeige.

## 3. Fireworks — Quotas und echte externe Nutzungshistorie

**Anschluss.** OpenAI `https://api.fireworks.ai/inference/v1`; native
Anthropic-Basis `https://api.fireworks.ai/inference` mit `/v1/messages`;
Responses ist separat dokumentiert. Streaming und Toolcalls sind vorhanden,
Usage im Abschlusschunk berücksichtigen. Standard-Key/Bearer, zusätzliche
Account-ID für Managementabfragen. Vollständige Ressourcen-IDs
`accounts/.../models/...` bzw. Router verwenden. Modellressourcen können über
die Management-API gelistet werden; nicht jeder gelistete Upload ist serverless
aufrufbar. Rolle `inference-user` genügt laut Rollenmatrix für Inferenz und
Ressourcenansicht; Quota-Leserechte gesondert testen.
[OpenAI](https://docs.fireworks.ai/tools-sdks/openai-compatibility),
[Anthropic](https://docs.fireworks.ai/tools-sdks/anthropic-compatibility),
[Responses](https://docs.fireworks.ai/guides/response-api),
[Modelle](https://docs.fireworks.ai/api-reference/list-models),
[Rollen](https://docs.fireworks.ai/accounts/users).

**Zulässigkeit.** Gateways wie LiteLLM und Coding-Harnesses werden offiziell
beschrieben, ebenso automatisierte Service Accounts. Das stützt persönliches
lokales BYOK und gewöhnliche API-Agentennutzung. **Offene Vertragsfrage:** Die
aktuell verlinkten Terms (PDF, 10.07.2026) gewähren persönliche/interne
Geschäftsnutzung, enthalten zugleich weit gefasste Einschränkungen zu
Automatisierung (§2.2b), konkurrierenden Produkten (§2.2i), Keytransfer (§2.2d)
und Serviceverteilung (§2.2e). Daraus keine pauschale Freigabe für beliebig viele
Worker oder einen kommerziellen Multi-Anbieter-Gateway ableiten. Die Spannung
zur offiziellen Gateway-/Automatisierungsdoku sollte vor einer entsprechenden
Vertriebszusage geklärt werden; sie ist auch kein Beleg für ein generelles
Verbot lokaler Clients.
[Gateways](https://docs.fireworks.ai/nexus/llm-gateways),
[Service Accounts](https://docs.fireworks.ai/accounts/service-accounts),
[aktuell verlinkte Terms](https://fireworks.ai/terms-of-service).

**Monitor.** Offiziell `GET /v1/accounts/{account_id}/quotas` und
`GET /v1/accounts/{account_id}/quotas/{quota_id}` am Host `api.fireworks.ai`.
`name`, `value` und `maxValue` (int64 als Strings), `usage` (Zahl), `updateTime`
(Datum). `value` ist das erzwungene Limit, `maxValue` die genehmigte Obergrenze.
**Keine Einheit und kein Resetfeld im generischen Schema.** Request-Raten,
GPU-Anzahlen und Monatsausgaben nicht vermischen. Erst nach belegter Quota-ID,
Einheit und vergleichbarer `usage` darf `value - usage` als Rest gelten.
`updateTime` ist die Aktualisierungszeit der Quota, kein Reset oder garantierter
Messzeitpunkt der Nutzung. Exakte Monatsgrenze und Poll-/Aktualitätsgarantie bleiben offen;
Total Spend kann einen Tag oder länger nachlaufen.
[List](https://docs.fireworks.ai/api-reference/list-quotas),
[Schema](https://docs.fireworks.ai/api-reference/get-quota),
[Quotatypen](https://docs.fireworks.ai/guides/quotas_usage/account-quotas),
[Abrechnungsverzug](https://docs.fireworks.ai/faq-new/billing-pricing/how-does-billing-and-credit-usage-work).

**Historie.** Mehr als nur das lokale Journal ist möglich:
`GET /v1/accounts/{account_id}/billingUsage` liefert gemessene Mengen,
`GET /v1/accounts/{account_id}/billing/summary` Kostenpositionen;
`POST /v1/accounts/{account_id}/usageCosts:query` ist eine lesende Kostenabfrage
mit Gruppierung/Pagination. Letztere kennt `SELF` und `ACCOUNT` (Administrator
nötig), außerdem `evaluationTime`; das ist keine Vollständigkeitsgarantie.
`firectl billing export-metrics` exportiert Ereigniszeit und Tokenmengen als CSV,
maximal 31 Tage pro Abfrage. Keine Prompttexte/vollständigen Tracearchive daraus
ableiten; Retention und stabile Deduplizierung offen. Die Übersichtsseite ist
bei Kosten pro Key teilweise widersprüchlich; für gruppierte Kosten die
spezifische `usageCosts:query`-Referenz verwenden. Import später getrennt vom
lain-Journal qualifizieren.
[Mengenübersicht](https://docs.fireworks.ai/accounts/exporting-usage-and-costs),
[Kosten und Rechte](https://docs.fireworks.ai/api-reference/query-usage-costs),
[Ereignisexport](https://docs.fireworks.ai/accounts/exporting-billing-metrics).

**Gratis, Überlauf, Zugang.** Die FAQ nennt einen **$1 Starter-Credit**;
Nutzung ohne Zahlungsmethode wird nach dessen Verbrauch ausgesetzt.
Gültigkeitsdauer und allgemeine aktuelle Vergabebedingungen sind nicht eindeutig
belegt, kein dauerhafter Gratis-Tarif. Prepaid ohne Auto Reload pausiert bei
Null; monatlicher Spend Cap ist eine eigene Grenze. Kein Beleg für eine
centgenaue Echtzeitgarantie. **Fire Pass separat behandeln:** experimentell,
aktuell nicht offen für neue Abos, bestehende Nutzer/Promoempfänger; eigener
`fpk_`-Key, nur zugelassene Router, persönliche Agentennutzung, keine Responses-
API. Ein Standard-`fw_`-Key ist kein Pass-Key. Kein universelles kostenloses
Coding-Abo versprechen. Länderzulassung/Exportbestimmungen prüfen;
US-only-Routen und EU-only-Verfügbarkeit via Sales sind eigene Angebote.
[Starter-Credit](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit),
[Billing](https://docs.fireworks.ai/faq-new/billing-pricing/how-does-billing-and-credit-usage-work),
[Fire Pass](https://docs.fireworks.ai/firepass),
[Regionen](https://docs.fireworks.ai/serverless/us-only-serverless).

**Erster Test:** mit vorhandenem minimal berechtigtem Key und Account-ID nur
List/Get lesen; je Quota ID, Einheit, Berechtigung und fehlende Nutzung erfassen.
Anschließend kleine bestehende Zeitspanne über `SELF`-Kosten/Mengen lesen,
ohne Traffic zu erzeugen. Erst später Streaming-/Toolprobe. M–H; **beides
geeignet**, Vertragsfrage für kommerziellen Vertrieb bleibt ausdrücklich offen.

## 4. Chutes — Abo-Quota interessant, Automatisierung auf PAYG begrenzen

**Anschluss.** OpenAI `https://llm.chutes.ai/v1`, Bearer mit `cpk_`-Key;
`GET /v1/models` für aktuelle IDs. Streaming und Toolfähigkeit je Modell
qualifizieren; Integrationen nennen Coding-Agenten und LiteLLM. Native
Anthropic-Kompatibilität in den geprüften Quellen nicht belegt, ein lokaler
Übersetzer wäre eine eigene Fähigkeit. OAuth unterstützt PKCE und unter anderem
`chutes:invoke` bzw. `chutes:invoke:{chute_id}`, `account:read`, `billing:read`.
Dokumentation beschreibt Appregistrierung und Server-Tokenaustausch: ein
Desktop-Public-Client ohne eingebettetes Client Secret bleibt zu qualifizieren.
[Integrationen](https://chutes.ai/integrations),
[LLM-/Streaming-Beispiele](https://chutes.ai/docs/examples/llm-chat),
[OAuth](https://chutes.ai/docs/sign-in-with-chutes/overview).

**Zulässigkeit.** Eigene Apps und lokale Proxys sind über offizielle
Integrationen vorgesehen; OAuth belastet das eigene Konto des Nutzers.
Die Terms beschränken **Abos auf gelegentliche direkte Nutzung einschließlich
niedrigvolumiger API-Nutzung**. Hohe Parallelität, große automatisierte
Inferenzlasten und Produktionsanwendungen müssen PAYG nutzen; mehrere Abos
zur Limitumgehung sind verboten. Coding-Agent als unterstütztes Tool heißt
also nicht, dass jeder autonome Workerbetrieb Abo-Nutzung sein darf. Eine
numerische Grenze für „low volume“ ist nicht angegeben. BYOK-Clientvertrieb
ist durch App-/OAuth-Integration gestützt, eine ausdrückliche kommerzielle
lain-/Resellerfreigabe nicht belegt. Modelllizenzen gelten zusätzlich.
[Terms](https://chutes.ai/terms),
[App-/OAuth-Zuschnitt](https://chutes.ai/docs/sign-in-with-chutes/overview).

**Monitor.** Offiziell dokumentierte Management-Routen am Host `api.chutes.ai`:
`GET /users/me/quotas` und `GET /users/me/quota_usage/{chute_id}`.
Die Users-Referenz enthält keine hinreichenden Antwortfelder für Rest, Limit,
Einheiten, Resets, Balance oder Datenstand; Minimal-Scopes pro Route bleiben
offen. `billing:read` ist ausdrücklich für Balance/Credits vorgesehen,
beweist aber noch kein konkretes Balance-Antwortschema. Als „Admin Balance
Lookup“ bezeichnete Routen sind keine normalen Nutzerrechte. Keine intern
erratene `/balance`-Route als Ersatz verwenden.
[Users-API](https://chutes.ai/docs/api-reference/users),
[Scopes](https://chutes.ai/docs/sign-in-with-chutes/overview).

**Historie.** Offizielle Invocations-Referenz nennt
`GET /invocations/exports/{year}/{month}/{day}/{hour_format}` und
`GET /invocations/exports/recent`, daneben LLM-Statistik. Nicht belegt sind
normale Nutzer-/Accountfilter, Token-/Kostenfelder, Retention und eine sichere
Zuordnung zum eigenen Billing. Insbesondere öffentliche Netzwerk-/Minerstatistik
nicht als persönliche Account-Historie importieren. Bis zur Qualifikation nur
lain-Traffic selbst erfassen; keine Promptaufzeichnung beim Anbieter unterstellen.
[Invocations-Referenz](https://chutes.ai/docs/api-reference/invocations).

**Gratis, Überlauf, Zugang.** Aktuell laut Preis-FAQ kein Free Tier;
keine allgemein garantierten Trial-/Referral-Credits oder Ablaufbedingungen
belegt. Plus/Pro kombinieren tägliche Requests, monatliche PAYG-äquivalente
Dollargrenzen und ein rollendes 4-Stunden-Burstlimit. **Überschreitungen fallen
automatisch auf PAYG zurück**, mit Abo-Rabatt. Die Tageszahl garantiert daher
keine entsprechende nutzbare Abo-Menge. Ein „nur Abo“-Schalter, centgenauer
Prepaidstopp und Umgang mit laufenden Requests sind nicht belegt. Zahlungen
unter anderem via Karten, Bank und Krypto; somit keine generelle Kartenpflicht
für alle Zahlwege. Länderliste/Identitätsanforderungen nicht gesichert; dezentrale
TEE-Ausführung belegt weder EU-Verarbeitung noch regionale Verfügbarkeit.
[Preise und Limits](https://chutes.ai/pricing).

**Erster Test:** nur `/users/me/quotas` und eine bestehende modellbezogene
Quota lesen; tatsächliche Auth/Scopes, Schema und Fenster gegen die Konsole
abgleichen. Keine Aboerschöpfung provozieren. Vor Worker-Gateway-Qualifikation
zulässigen PAYG-Zugang und Kostenkontrolle festlegen. **Beides potenziell**, H;
Abo-Gateway für unbeaufsichtigte parallele Worker nicht als freigegeben markieren.

## 5. Scaleway — EU-Gateway und Verbrauchshistorie, kein Rest-/Resetnachweis

**Anschluss.** OpenAI `https://api.scaleway.ai/v1` oder explizit projektbezogen
`https://api.scaleway.ai/{project_id}/v1`; Bearer mit Scaleway Secret Key.
IAM-Application statt Owner-Key bevorzugen, Permission Set
`GenerativeApisModelAccess` für Modellzugriff. `GET /models`, Streaming und
Function Calling sind dokumentiert; Fähigkeiten modellabhängig. Responses
unterstützt keine zustandsbehafteten Gespräche und keine eingebauten Tools
jenseits Function Calling. Native Anthropic-API nicht belegt. Dokumentierte
Serverless-Region ist Paris (`fr-par`).
[API](https://www.scaleway.com/en/developers/api/generative-apis),
[OpenAI-Grenzen](https://www.scaleway.com/en/docs/generative-apis/reference-content/openai-compatibility/),
[IAM-Sets](https://www.scaleway.com/en/docs/iam/policies-permissions/permission-sets/).

**Zulässigkeit.** Offizielle SDK-/Toolintegration und automatisierte API-Nutzung
stützen persönliche lokale BYOK-Nutzung und Coding-Agenten innerhalb der
Modell-/Ratenlimits. Die geprüften französischen AI-Sonderbedingungen vom
07.04.2026 beschreiben API-Nutzung und zusätzliche Modelllizenzen; sie ersetzen
nicht die allgemeinen, kontobezogenen Vertragsbedingungen. Konkrete
Weiterverkaufs-/Wettbewerbsklauseln und eine ausdrückliche Erlaubnis des
kommerziellen lain-Vertriebs sind damit noch nicht abschließend geprüft.
Kein Coding-Abo aus dem Serverless-PAYG-Angebot ableiten.
[Quickstart](https://www.scaleway.com/en/docs/generative-apis/quickstart/),
[AI-Sonderbedingungen, französische Fassung](https://www-uploads.scaleway.com/Conditions_Particulieres_Services_IA_61a1a5f301.pdf),
[Kontoverträge](https://www.scaleway.com/en/docs/account/support/download-scaleway-contracts/).

**Monitor.** Offizielle Abfragen ohne Inferenz sind belegt: Billing
`GET https://api.scaleway.com/billing/v2beta1/consumptions` nach Organisation,
Projekt und Monat; Auth hier `X-Auth-Token` mit Secret Key, `BillingReadOnly`
genügt. Für Tokenmengen nennt die FAQ Cockpit/Prometheus:
`https://{data-source-id}.metrics.cockpit.fr-par.scw.cloud/prometheus/api/v1/query_range`
mit `generative_apis_tokens_total` und Bearer-Cockpit-Token mit **Query**-Recht.
Das ist eine offizielle Monitoring-Schnittstelle, keine interne Web-API.
Kein Restkontingent-/Reset-/Balancefeld daraus belegt.
[Billing-API](https://www.scaleway.com/en/developers/api/billing/),
[offizielles PromQL-Beispiel](https://www.scaleway.com/en/docs/generative-apis/faq/#how-can-i-give-access-to-token-consumption-to-my-users-outside-scaleway),
[Cockpit-Query-Recht](https://www.scaleway.com/en/docs/cockpit/how-to/create-token/).

**Historie.** Cockpit liefert Projekt-Zeitreihen, Billing Monats-/Projektverbrauch;
keine einzelne Requestchronik samt Inhalt daraus ableiten. Graphen können bis
5 Minuten nachlaufen, der Gesamtzähler wird laut Troubleshooting in Echtzeit
aktualisiert. PromQL-Aggregationen können bei geringer Last über lange Zeiträume
abweichen; Retention und Zählerresets separat qualifizieren. Projekt-/IAM-Labels
nicht mit einer bestätigten lain-Accountidentität gleichsetzen. Eigene
Requestdetails bleiben im lain-Journal, externe Mengen sind eine zweite Quelle.
[Monitoring und Genauigkeit](https://www.scaleway.com/en/docs/generative-apis/faq/),
[Aktualität](https://www.scaleway.com/en/docs/generative-apis/troubleshooting/fixing-common-issues/).

**Gratis, Überlauf, Zugang.** 1 Mio. Tokens und 60 Audiominuten als Free Tier
benannt; die Produktseite spricht von Neukunden. Wiederkehrende Monatsfreimenge
oder konkreter Ablauf nicht verlässlich belegt; ältere „kostenlose Beta“-Sätze
auf der Produktseite widersprechen aktuellen PAYG-Angaben. Danach automatische
Abrechnung, **laut FAQ kein konfigurierbarer harter Ausgabenstopp**. Alerts sind
kein Stopp. Allgemeines Onboarding verlangt zur Ressourcenbestellung eine
Kreditkarte, mit €1-Autorisierung; die AI-Sonderbedingungen nennen zusätzlich
KYC und validierte Zahlung. Keine kartenfreie API-Zusage. Vollständige
unterstützte Länderliste und Referral-Aktionen nicht geprüft; EU-Serverstandort
ist keine Aussage über die Zulassung aller Herkunftsländer.
[Free Tier/kein Hard Cap](https://www.scaleway.com/en/docs/generative-apis/faq/),
[Produktseite](https://www.scaleway.com/en/generative-apis/),
[Onboarding](https://www.scaleway.com/en/docs/account/setup-lifecycle/create-an-account/),
[AI-Vertragsbedingungen §3](https://www-uploads.scaleway.com/Conditions_Particulieres_Services_IA_61a1a5f301.pdf).

**Erster Test:** vorhandenen Projektzugang mit `GenerativeApisModelAccess`
qualifizieren; separat vorhandene Cockpit-Daten nur mit Query-Token und Billing
nur mit `BillingReadOnly` lesen. Leere Zeitreihe bedeutet nicht automatisch
Nullverbrauch. Kein absichtlicher Free-Tier-Überlauf. **Gateway M, Monitor für
Verbrauch H; Kontingent-/Resetmonitor vorerst unqualifiziert.**

## 6. Cerebras — Gateway gut dokumentiert, Monitorzugang offen

**Anschluss.** OpenAI `https://api.cerebras.ai/v1`, Bearer mit Projekt-API-Key,
`GET /v1/models`; Streaming und Toolcalls dokumentiert. `tools` plus
`response_format` ist nicht bei allen Modellen kombinierbar; nur standardmäßiges
`stream:true`, kein `tool_stream`. Native Anthropic-/Responses-Kompatibilität
wird aus diesen Quellen nicht zugesagt. Projektmitgliedschaft/Keyberechtigung
muss zum Zielprojekt passen; Billingrechte folgen nicht aus einem Inferenzkey.
[Kompatibilität](https://inference-docs.cerebras.ai/resources/openai),
[Models](https://inference-docs.cerebras.ai/api-reference/models/list-models),
[Keys](https://inference-docs.cerebras.ai/console/api-keys).

**Zulässigkeit.** Die Terms erlauben persönliche/geschäftliche API-Nutzung und
unter „License“ ausdrücklich Entwicklung, Test, Support und **Verteilung eigener
API-Integrationen an Endnutzer**. Dokumentationskonforme APIs sind vom allgemeinen
Automatisierungsverbot ausgenommen. Das ist eine belastbare Grundlage für
lokales BYOK und Softwareverteilung. Keys dürfen nicht weitergegeben/verkauft
werden; Serviceweiterverkauf, konkurrierende Produkte und Modelllizenzen bleiben
beschränkt. Ein neutraler Client ist damit besser gestützt, die Einordnung
spezieller weiterverkaufter Gateway-Dienste bleibt separat. Worker müssen
Organisations- und Projektlimits gemeinsam einhalten. Modellabos existieren,
ihre individuellen Einsatz-/Überlaufregeln sind nicht vollständig belegt.
[Terms, „License“ und „Restrictions“](https://www.cerebras.ai/terms-of-service),
[Limits](https://inference-docs.cerebras.ai/support/rate-limits).

**Monitor.** Die Doku führt für tatsächliche Balance/Limits zur Konsole.
Kein öffentlicher Shared-Inference-Endpunkt für Rest, Verbrauch, Budget,
Guthaben und Reset ohne Inferenz belegt. Eine Dedicated-Management-API ist
kein Beleg dafür. Token Buckets füllen kontinuierlich auf; keine festen
Resetzeitpunkte erfinden. Mehrere Tokenkategorien und Projekt-/Orggrenzen
separat berücksichtigen; Frische einer künftigen internen Web-API unbekannt.
[Limits](https://inference-docs.cerebras.ai/support/rate-limits),
[Konsole](https://inference-docs.cerebras.ai/console/usage-monitoring).

**Historie.** Konsole: Usage/Cached Usage/Cost, Requestlogs mit Request-ID und
CSV-Downloads. Das belegt vorhandene externe Daten, noch keine öffentliche
Lese-API für lain oder einen automatisierbaren Export. Ohne diesen Nachweis
nur durchgeleitete Nutzung im lokalen Journal erfassen.
[Usage & Monitoring](https://inference-docs.cerebras.ai/console/usage-monitoring).

**Gratis, Überlauf, Zugang.** Aktuelle Doku: **$5 Trial nach verifizierter
Zahlungsmethode, Ablauf nach 30 Tagen**; ohne Zahlungsmethode bleiben API und
Playground inaktiv. Ausdrücklich kein dauerhaft wiederkehrender Free Tier.
Nach Verbrauch/Ablauf stoppt der Trialzugriff bis zum Guthabenkauf;
Auto-Recharge ist standardmäßig aus. Keine zahlungspflichtige Fortsetzung
allein durch Hinterlegung der Zahlungsmethode laut Trialbeschreibung.
Später aktivierte Abos/Recharge separat prüfen. Referral-Freimengen nicht belegt;
Export-/Sanktionsregeln in den Terms, vollständige Länderzulassung offen.
[Trial und Stopp](https://inference-docs.cerebras.ai/support/rate-limits),
[Billing/Auto-Recharge](https://inference-docs.cerebras.ai/console/account-billing),
[Terms](https://www.cerebras.ai/terms-of-service).

**Erster Test:** Fixture für Modellliste/Usage und dokumentierte Toolgrenzen,
später vorhandenen Projektkey nur zur Modellliste verwenden. Keinen Trialaccount
anlegen; Monitor erst nach offizieller Schnittstellenklärung. **Gateway M;
Monitor H/offen.**

## 7. Together AI — Gateway, Beta-Billing für spätere Historie

**Anschluss.** OpenAI `https://api.together.ai/v1`, Bearer mit projektbezogenem
Key, Streaming und Function Calling je Modell. Der offizielle Modellkatalog
ist belegt; eine aktuelle dynamische List-Models-Route ist hier nicht
qualifiziert. IDs im Format `<provider>/<model>` verwenden.
Anthropic-/Claude-Code-Anschluss ist über einen ausdrücklich beschriebenen
**lokalen LiteLLM-Übersetzer** möglich; das beweist keine native Messages-API.
Cache-/Reasoning-Usage hat modellabhängige Felder.
[OpenAI](https://docs.together.ai/docs/inference/openai-compatibility),
[Modellkatalog](https://docs.together.ai/docs/serverless/models),
[Keys/Projektrechte](https://docs.together.ai/docs/api-keys-authentication),
[lokaler Coding-Proxy](https://docs.together.ai/docs/using-together-with-litellm).

**Zulässigkeit.** Eigene lokale Proxys und Agenten sind durch die Integration
fachlich vorgesehen; Parallelität muss Modell-/Organisationslimits einhalten.
Die Terms verbieten unter anderem eigenständigen Serviceweiterverkauf,
konkurrierende Produkte und übermäßige Last. Persönlicher BYOK-Proxy ist
durch den offiziellen Guide gut gestützt. Ob der kommerzielle Vertrieb des
konkreten lain-Gateways von Wettbewerbsklauseln erfasst wird, bleibt offen;
kein gemeinsamer mitgelieferter API-Key oder Dienstweiterverkauf aus der
Toolanleitung ableiten. Kein allgemeines Coding-Abo belegt.
[Terms §4](https://www.together.ai/terms-of-service),
[Proxyguide](https://docs.together.ai/docs/using-together-with-litellm).

**Monitor.** Offizielles `GET /v1/billing/usage`, **Beta und Freischaltung pro
Organisation**; ohne Freischaltung laut Referenz 404. Kostenannotierte
Nutzungspositionen nach Monat und Stunde/Tag, UTC, paginiert. Aktuell kann
**jeder Projektschlüssel die ganze Organisation lesen**, sogar externe
Projektmitarbeiter; nicht auf das Projekt beschränkt und nicht auf Admins
begrenzbar. `organization_id` bereits mitsenden, später soll es Pflicht sein.
Keine Restguthaben-/Limit-/Reset-API daraus ableiten.
`earliest_window_start`/`latest_window_end` bezeichnen Datenabdeckung,
keinen Kontingentreset.
[Schema und Rechte](https://docs.together.ai/reference/billing-usage),
[Spend Controls](https://docs.together.ai/docs/billing-spend-controls).

**Historie.** Dieselbe API liefert Menge, Einheit, Stückpreis und Kosten je
Zeitfenster/Position, zum Teil Projekt-/Keyzuordnung; **Aggregate, keine
vollständige Einzelrequestchronik**. Laufender Monat bis eine Stunde,
Vormonate bis 24 Stunden verzögert. Für Monitor ohne Gateway nutzbar, sobald
freigeschaltet; das zunächst gewünschte Rest-/Resetmonitoring erfüllt sie nicht.
Account-/Organisationsdaten getrennt vom lain-Journal behandeln.
[Billing-Referenz](https://docs.together.ai/reference/billing-usage).

**Gratis, Überlauf, Zugang.** Aktuell **kein Free Trial**, erster Kauf mindestens
**$15**, weitere Käufe ab $5; gekauftes Guthaben derzeit ohne Ablaufdatum.
Registrierung „for free“ bedeutet keinen kostenlosen API-Zugang. Auch vorhandene
Promocredits ersetzen laut Doku die Einstiegsvoraussetzung nicht. Zahlungsmethode
für Kauf notwendig; Karten und US-ACH sind dokumentiert, Auto-Recharge nur mit
Karte als Default. Organisation/Projekte teilen Guthaben; keine eigenen Keycaps.
Der Nullsaldo-Cutoff hat **Abrechnungsverzug und garantiert ausdrücklich kein
exaktes Ausgabenlimit**. Laut Billing-Doku lässt sich Auto-Recharge deaktivieren;
das verhindert den dokumentierten Nachlauf nicht. Zusätzlich enthält §6 der
Terms eine breite Ermächtigung zur automatischen Wiederaufladung. Das Verhältnis
zum deaktivierbaren Konsolenschalter bleibt vor einer Kostengarantie zu klären.
Budgets/Alerts werden als geplant beschrieben.
Trial-/Referral-Aktionen und vollständige Länderzulassung offen; Exportregeln
beachten, US-ACH ist kein weltweiter Zahlungsanspruch.
[Credits](https://docs.together.ai/docs/billing-credits),
[Zahlungen](https://docs.together.ai/docs/billing-payment-methods),
[Cutoff-Grenzen](https://docs.together.ai/docs/billing-spend-controls),
[Terms](https://www.together.ai/terms-of-service).

**Erster Test:** vorhandenen freigeschalteten Zugang für einen kleinen
abgeschlossenen Billingzeitraum lesen; 404 nicht als „keine Nutzung“ werten;
Orgweite, UTC-Fenster, Pagination, Kostenfelder und Nachlauf prüfen.
Gateway separat mit Modellliste/Streaming/Tools. **Gateway M; Monitor für
spätere History H/Beta, Rest/Reset offen.**

## 8. SambaNova Cloud — Free-Tier-Gateway, Header kein eigenständiger Monitor

**Anschluss.** OpenAI `https://api.sambanova.ai/v1`, Bearer mit persönlichem
API-Key; Streaming, `stream_options.include_usage` und Function Calling
dokumentiert. Modellfähigkeiten am aktuellen SambaCloud-Katalog prüfen;
SambaStack-/SambaStudio-Verwaltungsrouten sind kein Nachweis für dieselbe
öffentliche Cloud-API. Ein geeigneter Cloud-`GET /models`-Nachweis wurde in den
geprüften Seiten nicht gefunden; Modellkatalog vorhanden. Native Anthropic-
oder Responses-Kompatibilität und granulare Read-only-Keyscopes bleiben offen.
[Keys/URLs](https://docs.sambanova.ai/docs/en/get-started/api-keys-urls),
[Streaming](https://docs.sambanova.ai/docs/en/get-started/quickstart),
[Function Calling](https://docs.sambanova.ai/docs/en/features/function-calling),
[Cloud-Katalog](https://docs.sambanova.ai/docs/en/models/sambacloud-models).

**Zulässigkeit.** Cloud-Terms §§1.1–1.5 erlauben Kundennutzung und autorisierte
Nutzer, begrenzen Weiterverkauf/Sublicensing und konkurrierende Produkte.
Lokaler persönlicher API-Zugang ist durch SDK-/Clientanleitungen gestützt;
eine ausdrückliche Freigabe jedes Proxy-/Worker- oder kommerziellen
Clientvertriebsszenarios fehlt. Parallelität innerhalb der Tier-/Modelllimits
qualifizieren. Free Tier ist kein Beleg für eine Freigabe hochvolumiger
Produktion; Previewmodelle sind ausdrücklich nicht für Produktion vorgesehen.
Kein Coding-Abo in den geprüften Quellen belegt.
[Cloud-Terms](https://sambanova.ai/cloud-end-user-license-agreement),
[Limits/Previewgrenzen](https://docs.sambanova.ai/docs/en/models/rate-limits).

**Monitor.** Dokumentiert sind Antwortheader
`x-ratelimit-{limit,remaining,reset}-requests` und die entsprechenden
`-requests-day`-Varianten. Reset bedeutet hier **Zeit bis Reset**, keinen
ungeprüft zu übernehmenden Unixzeitstempel. RPM/RPD und Free-Tier-TPD sind
unterschiedliche Grenzen; keine freie Tokenmenge aus Requestheadern errechnen.
Kein belegter nicht verbrauchender Endpunkt, der diese Header zuverlässig
liefert, und keine offizielle Balance-/Quota-Lese-API gefunden. Ein `HEAD` oder
Modelllistenaufruf liefert sie nicht automatisch. Nur passiv an ohnehin
beauftragten Gatewayantworten beobachten; keine Inferenz zum Polling starten.
Messzeit ist die Antwortzeit, nachfolgende Fremdnutzung bleibt unsichtbar.
[Headerreferenz](https://docs.sambanova.ai/docs/en/models/rate-limits).

**Historie.** Keine geeignete öffentlich dokumentierte Cloud-Request-/Token-/
Kostenhistorie-API belegt. Dashboard-/Billingansichten sind kein API-Vertrag.
Durch lain geleitete Requests können mit zurückgelieferter Usage lokal erfasst
werden; keine vollständige Accountabdeckung oder Kostenabrechnung versprechen.
[API-Übersicht](https://docs.sambanova.ai/docs/en/api-reference/overview),
[Usage im Stream](https://docs.sambanova.ai/docs/en/get-started/quickstart).

**Gratis, Überlauf, Zugang.** Limits-Doku unterscheidet **Free Tier ohne
Zahlungsmethode** und Developer Tier mit Zahlungsmethode. Das ist ein
wiederkehrend begrenzter Free Tier, kein belegter befristeter Trial; dauerhafte
Unveränderlichkeit ist nicht zugesagt. Pro Modell/Tier verschiedene RPM/RPD/TPD,
keine statische Universalmenge übernehmen. Ohne Zahlungszugang wird nach
Limitüberschreitung gedrosselt; daraus keinen kostenfreien Developer-Tier-
Überlauf ableiten. Preis-/Promocredits, Referral, Ablauf und verlässliche
Ausgabencaps nach Kartenhinterlegung sind nicht ausreichend belegt. Export-/
Sanktionsregeln gelten; vollständige Länderzulassung offen.
[Free/Developer Tier](https://docs.sambanova.ai/docs/en/models/rate-limits),
[Preise](https://cloud.sambanova.ai/plans/pricing),
[Exportregel §11.10](https://sambanova.ai/cloud-end-user-license-agreement).

**Erster Test:** vorhandenen ausdrücklich kostenlosen Accounttier bestätigen,
Modellliste zunächst gegen offiziellen Katalog festlegen. Später eine
freigegebene kleine Streaming-/Toolprobe mit Usage und Rateheadern; vorher
keine Requests dafür erzeugen. **Gateway M, eigenständiger Monitor H/offen.**

## Integrationsreihenfolge und Abnahmegrenzen

1. **DeepSeek-Balance:** dokumentiertes Schema als Fixture; mehrere Währungen,
   Dezimalstrings, fehlende Werte und Verfügbarkeit. Danach autorisierte
   Lesestichprobe. Kein Reset/Prozent und keine Tokens daraus erzeugen.
2. **MiniMax-Abo:** Schlüsselart, anonymisierte `remains`-Antwort, unabhängige
   Fenster und Credit-Pack-Überlauf belegen. Ohne Schema keine feste Zuordnung
   vermeintlicher Tokenfelder; kein kostenpflichtiges Erschöpfungsexperiment.
3. **Fireworks-Quotas:** minimalen Leser, Quota-ID/Einheit/Scope und Frische
   prüfen. Historie danach als eigene Quelle; kommerzielle Vertragsfrage vor
   entsprechender Zusage klären.
4. **Scaleway:** EU-Gateway, dann Cockpit/Billing als externe Verbrauchsquelle.
   Kein „Free Tier mit sicherem Hard Cap“ anbieten. Chutes erst mit
   qualifizierter Abo/PAYG-Trennung und geklärtem Lastprofil.
5. **Cerebras/Together/SambaNova:** als Gateway-Ergänzungen; Monitor erst mit
   belegtem Zugriff bzw. ausdrücklich als verzögerte Billing-Historie.

Für alle Gatewaytests später: konkretes Modell, normale und SSE-Antwort,
Toolcall samt Folgeantwort, Thinking-/Reasoning-Erhalt, Usage inklusive Cache,
Abbruch und 401/403/402/429. Keine automatische PAYG-Fallbackroute bei
Aboerschöpfung, solange der Nutzer diese nicht ausdrücklich konfiguriert hat.
Monitor muss unabhängig von einem Inferenzproxy funktionieren und darf keine
Inferenzrequest-Schleife benötigen. Unbekannte Werte sichtbar unbekannt lassen.

Die Vorschläge orientieren sich am bestätigten [Produktziel](product-scope.md)
und am noch vorgeschlagenen [Monitor-Modus](monitor-mode.md). Diese Recherche
ändert weder die Architekturverträge noch den Implementierungsumfang.
