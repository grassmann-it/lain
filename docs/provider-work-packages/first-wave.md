# Erste neue Anbieterpakete

Stand 24.10.2026. Konkretere nächste Pakete aus dem [Register](README.md),
noch kein durch diesen Plan erteilter Live-/Kaufauftrag. [M0](modularity.md)
beschreibt den Vorschlag für den gemeinsamen Modulrahmen. Die passende
vorhandene Infrastruktur verwenden; kein Provider wartet auf alle späteren
Auth-, Referral- oder Historyerweiterungen.

## Gemeinsamer Abschluss je Anbieter

Das Ergebnis ist ein einzelner anschlussfähiger Anbieter mit belegten Fähigkeiten,
eigenen Quellen und ehrlichem Qualifikationsstand. Manifeste/Fixtureprüfung können
ohne Entwickleraccount entstehen. Ein fehlender persönlicher Zugang bleibt
**Liveprobe ausstehend**, nicht „Implementierung unmöglich“ und nicht „fertig live“.

Für Gateway: Host/Account/Route und privater Transport korrekt zugeordnet,
konkretes Modell, begrenzter Textrequest/Stream, Toolroundtrip soweit unterstützt,
Abbruch und sanitisiertes Fehlerverhalten. 401/403/429, unvollständigen Stream
und falsches Modell lokal simulieren, keine echten Limits absichtlich ausreizen.
Token-/Kostenabdeckung und Accountidentität in der vorhandenen lokalen History
prüfen, Neustart/Replays erhalten dieselben Belege. Nur benötigte Regressionen.

Für Monitor: Inferenzhandler im Fixture darf nie aufgerufen werden. Gültiger
Nullsaldo, unvollständiges Schema, fehlender Scope, Timeout/429 und veralteter
letzter Erfolg sind unterscheidbar. Einheit, Kontoscope, Beobachtungszeit und
tatsächlich gemeldeter Reset erhalten. Kein Prozent aus Geldsaldo, kein Reset
aus updateTime. GUI und CLI sehen denselben Hoststand.

Echte Proben später sequenziell mit geringem Kontext und begrenzter Ausgabe,
nach passendem Zugang und gewähltem Kostenrahmen. Keine Workerlast/Benchmarks
oder bezahlte Guthabenerschöpfung nötig. Technik- und Vertragsnachweis getrennt.

## PV-SAMBA — SambaNova

**Ergebnis:** Ein zusätzlicher Gateway über bestehenden OpenAI-Chat-Transport;
kein eigenständiger Kontingentmonitor ohne belegten Pollingendpunkt.

**Vorbereitung:** Anbieterbeschreibung, konkreter heute verfügbarer Modellweg,
Auth-/Stream-/Toolgrenzen und Quellen; vorhandene generische Einrichtung nutzen.
Geeignete Transport-/Parserfixtures aus M0, ohne Providerkarte in der UI zu kopieren.

**Noch nötig:** Persönlicher Free-Account/API-Key und konkreter Modellzugang.
Dokumentierter Free Tier ohne Karte ist ein Angebot, keine erfolgte eigene
Registrierung. Kein Referrallink als technische Voraussetzung.

**Abnahme:** Gemeinsamer Gatewayabschluss. Modellexistenz/Modellliste beweist
keine Tools; ungeprüfte Fähigkeiten bleiben unbekannt. Erster eigener Nachweis
dieses Anbieters, kein Rückschluss von Groq/anderem Chat-Host.

Quelle: [offizielle Limits](https://docs.sambanova.ai/docs/en/models/rate-limits),
Rechteprüfung im [Hauptbericht](../provider-discovery-2026-10-24.md).

## PV-FIREWORKS — Fireworks

**Ergebnis A:** Serverless-Gateway. **Ergebnis B:** unabhängiger Quotaabruf, mit
eigenem Abschluss. Externe Billing-/Requesthistorie bleibt Folgepaket.

**Vorbereitung:** Manifest und Standardtransport wie SambaNova; eigener
Quotaadapter gemäß geprüftem List/Get-Schema. Quotaarten/-einheiten nicht in
ein vermeintliches Gesamttokenbudget umrechnen. Fehlenden Reset offen lassen.

**Noch nötig:** Account/API-Key, Accountkennung/Quota-Berechtigung, tatsächlich
gewährte Startercredits und deren Ablauf. Kein Dedicated Deployment. Ein
optionaler AMD-Förderantrag wäre gesondert, kein Blocker und kein Referral.

**Abnahme:** Gemeinsamer Gateway- und Monitorabschluss getrennt. Quotas ohne
Inferenz und ohne dedizierte GPU abrufbar. Rechte-/Billinggrenzen dokumentiert.

Quelle: [Starterverhalten](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit),
Quota-/Authquellen im [Hauptbericht](../provider-discovery-2026-10-24.md).

## PV-NANO — NanoGPT

**Drei unabhängige Abschlüsse:** PAYG-Gateway mit API-Key; Management-Monitor mit
geeigneten Leserechten; später OAuth/PKCE mit optionaler nativer Referralzuordnung.

**Vorbereitung:** Bestehendes Standardprotokoll und Modellauswahl. Management-
Schema separat normalisieren; Credentialzwecke nicht aus einem Feld erraten.
Ein kleines PAYG-Guthaben beweist keine aktiven Abofenster.

**Noch nötig für Gateway:** Account/API-Key, ausgewähltes Modell und vorhandenes
Guthaben oder später ausdrücklich gewählter kleiner Kauf. Für Monitor separater
geeigneter Managementtoken, etwa `usage:read`; positives Abo nur mit tatsächlich
vorhandenem passendem Plan prüfen. Für Referral eigener Code sowie aktuelle
Verdienst-/Auszahlungs-/Kanalregeln. Kein neues Abo wird vorausgesetzt.

**Abnahme:** Zuerst Gateway. Managementtoken für Monitor soll keine Inferenz-
Ausgaberechte benötigen; unbekannte/inaktive Abozustände sind keine Nullquote.
OAuth erst nach Login-/Credentialzuschnitt: PKCE/State/Loopback, Abbruch/Widerruf,
secretfreier Status, bestehende Attribution erhalten. Referral ablehnen oder
ungültiger Code darf normalen Login nicht blockieren. Callback ist kein
Provisionsnachweis; kein bezahlter Referralabschluss zum Test nötig.

Quellen: [Preise](https://nano-gpt.com/pricing),
[Management](https://docs.nano-gpt.com/api-reference/management-api),
[OAuth](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce),
[Vergütung und Ausschlüsse](https://nano-gpt.com/support).

## PV-DEEPSEEK — DeepSeek

**Ergebnis A:** kleiner direkter Guthabenmonitor; **B:** später eigener Gateway.

**Vorbereitung:** `GET /user/balance`, mehrere Währungen, Gesamt-/Bonus-/
aufgeladenes Guthaben getrennt; Fixture für echten Nullsaldo und fehlende Werte.
Monitor-only-Einrichtung aus M-MONITOR braucht keinen erfundenen Modellweg.
Der Adapter kann bereits im Gatewaybetrieb direkt geprüft werden; das ist
noch kein vollständig installierbarer Standalone-Monitor.

**Noch nötig:** Account/Key, möglichst ohne Aufladung. Wenn Keyerstellung erst
nach Kauf möglich ist, tatsächlichen Mindestbetrag klären und Liveprobe offen
lassen. Kein Kauf auf Verdacht. Für Gateway positiver Modellzugang separat.

**Abnahme:** Direkter Balanceabruf ohne Inferenz, keine Prozent-/Resetableitung;
gemeinsamer Monitorabschluss. Positiver Saldo nicht durch Nullsaldo behauptet.

Quelle: [Balance API](https://api-docs.deepseek.com/api/get-user-balance).

## PV-OLLAMA / PV-LMSTUDIO — lokale Backends

**Jeweils eigenes Ergebnis:** Nutzer kann einen eigenen lokalen Server ohne
Cloudaccount und ohne erfundenen Pflichtkey verbinden. Die zweite Laufzeit
erhält einen eigenen Nachweis; kein Sammelstatus „lokal funktioniert“.

**Abhängigkeit:** M-LOCAL, weil Katalog/UI heute Cloud-HTTPS und genau ein Secret
erwarten, Compiler/Runtime noch keinen allgemein qualifizierten No-Auth-Pfad
zusagen. Serverauth optional nur soweit konkret belegt unterstützen.

**Noch nötig:** Passende vorhandene Hardware, laufender Server, ein kleines
Modell mit passender Lizenz/Template und lokale Adresse. Keine neue Installation,
großer Modelldownload oder GPUmiete wird durch dieses Paket automatisch ausgeführt.

**Abnahme:** Loopbackziel in UI/Host/Transport konsistent validiert, kein
Cloudkey an lokalen/fremden Server weitergereicht, explizites Verhalten bei
authfrei/authpflichtig. Text/Stream/Abbruch und lokale Usage funktionieren;
Tools nur für konkretes geeignetes Modell/Template. Kein Guthabenmonitor erfinden.

Quellen: [Ollama](https://docs.ollama.com/api/openai-compatibility),
[LM Studio](https://lmstudio.ai/docs/developer/openai-compat).

## PV-COHERE / PV-UPSTAGE / PV-KILO — kostenlose weitere Gateways

Drei getrennte Abschlüsse mit gemeinsamer Gatewayabnahme, keine gegenseitige
Abhängigkeit. **Cohere:** Evaluation-Key, Modell-/Protokollweg konkret prüfen,
Evaluation nicht zur Produktionsfreigabe erklären. **Upstage:** tatsächliche
Startcredits und Ablauf/Kartenpflicht bestätigen; sonst Liveprobe zurückstellen.
**Kilo:** aktuell verfügbares explizites Free-Modell, kein kostenpflichtiger
Fallback. Ein Free-Modell allein erweitert nicht automatisch die strenge
`free_only`-Policy; dafür braucht es qualifiziertes Runtime-Enforcement.

Kilos Cashaffiliate braucht eigene Zulassung und erlaubten Kanal; die Provision
gilt für Passabschlüsse und ist vom kostenlosen Gatewaytest unabhängig.

Quellen: [Cohere Limits](https://docs.cohere.com/v2/docs/rate-limits),
[Upstage Einstieg](https://console.upstage.ai/docs/getting-started),
[Kilo Billing](https://kilo.ai/docs/gateway/usage-and-billing),
[Kilo Affiliate](https://kilo.ai/partners).

## PV-NOVITA / PV-SILICONFLOW / PV-DEEPINFRA — bedingte nächste Monitorpfade

Je Provider eigener kleiner lesender Nachweis; erst nach bestätigtem kostenlosem
oder kleinem passenden Zugang live. Novita: Einheit 1/10.000 USD, `creditLimit`
und mögliche Verbindlichkeit erhalten; Affiliatebasis separat klären.
SiliconFlow: regionaler Host, Währung und Konto strikt zusammenhalten.
DeepInfra: Keylimits/Monatsbudget/Kontoguthaben anhand tatsächlicher Endpunkte
unterscheiden, keinen beliebigen Billingwert als Restquote ausgeben.

Externe Request-/Kostenhistorie nur als Folgepaket registrieren, nicht in diese
Balanceänderung einschleusen. Die [Matrix](../provider-decision-matrix-2026-10-24.md)
und [Erweiterungsrecherche](../provider-discovery-expansion-2026-10-24.md) enthalten
die einzelnen offiziellen technischen Quellen und Grenzen.
