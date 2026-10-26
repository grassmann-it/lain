# Welche Provider lohnen sich bei kleinem Testbudget?

**Stand und Quellenprüfung: 24.10.2026.** Neue Vorgabe: lain soll viel anbieten,
aber der Entwickler soll die Integrationen selbst prüfen können, ohne überall
teure Accounts oder Abos zu kaufen. Bewertet sind alle **39 zusätzlichen
Konto-/Produktkandidaten und fünf lokalen Backends** aus
[Hauptrecherche](provider-discovery-2026-10-24.md) und
[Erweiterung](provider-discovery-expansion-2026-10-24.md).
Vorhandene Anschlüsse werden nicht als neue Käufe eingeplant.

Fortführung: [priorisierte Arbeitspakete](provider-integration-work-packages-2026-10-24.md)
und [Referral-/Affiliateprüfung](provider-referrals-expansion-2026-10-24.md).
Der Nutzer möchte mit Empfehlungen selbst Geld oder nutzbares Guthaben erhalten;
Nutzerboni sind zusätzlich erwünscht. NanoGPT gewinnt dadurch an Priorität.
Wichtige Ergänzung zu Novita unten: Das alte Credit-Referral ist beendet,
ein separates Affiliateprogramm ist öffentlich belegt; dessen Bedingungen sind
noch zu klären. Die Budgetbeträge bleiben Vorschläge.

Keine Accounts angelegt, keine vorhandenen Credentials verwendet, keine
Inferenzrequests oder Zahlungen ausgeführt. Preise sind dokumentierte Angebote,
keine garantierten individuellen Checkoutpreise; Steuern, Wechselkurs,
Zahlungsautorisierungen und regionale Verfügbarkeit können hinzukommen.

## Neue Empfehlung

**Keine Sammlung bezahlter Abos aufbauen.** Zuerst kostenlose Gatewayproben und
lokale Server, danach einzelne kleine PAYG-Guthaben. Bezahlte Abo-/Enterprise-
Monitore nur mit bereits vorhandenem passenden Account oder konkretem Bedarf.

- **Sofort sinnvoll:** SambaNova, Fireworks, Cohere, Upstage, Kilo-Free-Modelle,
  Ollama und LM Studio. Gratis-/Trialbedingungen jeweils vor Start bestätigen.
- **Kleiner Kauf kann sich lohnen:** NanoGPT-PAYG ab $1 per Karte, OpenAI API
  mit dokumentiertem $5-Mindesteinstieg. Dies ist ein Budgetvorschlag, keine
  Zahlungsfreigabe. DeepSeek zusätzlich als einfacher Balance-Monitor, zunächst
  ohne Kauf und nur falls Keyerstellung/Abfrage ohne Guthaben möglich sind.
- **Gut, aber bedingt:** Cerebras-Trial mit Karte; Novita und SiliconFlow erst
  nach Bestätigung des tatsächlichen Gratis-/Kleineinstiegs; Scaleway mit
  Zahlungsvalidierung und ohne harten Kostendeckel.
- **Nicht eigens kaufen:** Featherless-Developer, Cursor-Team oder
  Windsurf/Devin-Enterprise nur für Integrationstests. Together/Friendli bieten
  gegenüber bereits vorhandenen Hosts vorerst zu wenig Mehrwert für neue Guthaben.

Diese Budgetreihenfolge ersetzt die rein technische Priorisierung der beiden
älteren Berichte. Ein fachlich guter Monitor kann bei teurem Testzugang trotzdem
weiter hinten stehen.

## Was die Bewertung bedeutet

**5/5** = zuerst anbinden, viel Nutzen für wenig Geld/Aufwand.
**4/5** = sinnvoller nächster Anschluss, begrenzter Zusatzaufwand oder Zugangshürde.
**3/5** = bedingt sinnvoll, erst Zugang/Preis/Nachfrage klären.
**2/5** = vorerst zurückstellen, für eigenen Test zu wenig Zusatznutzen oder zu viel Aufwand.
**1/5** = aktuell nicht als eigenen Integrationskauf verfolgen.
Das sind unsere Produkturteile, keine objektiven Anbieter-Rankings.

Der **Testzugang** ist nicht der Preis eines Requests. Einige kurze Requests
können nur Centbruchteile kosten, während der Anbieter $10/$15/$50 vorab bindet.
Ein $1-Gatewaytest beweist außerdem kein bezahltes Abo-Fenster, einen Reset oder
eine History mit echten Daten. Ein Nullguthaben-Monitor beweist nur den lesenden
Grundanschluss, nicht positive Guthaben, Überlauf oder belastbare Frische.

## 1. Erste acht Kandidaten

| Anbieter | Urteil | Eigenen Test bezahlen? | Warum / kleinster sinnvoller Nachweis |
| --- | --- | --- | --- |
| **DeepSeek** | **5/5 Monitor, 4/5 Gateway** | **Zunächst $0 versuchen**, aber Key ohne Aufladung nicht verifiziert. Mindestaufladung öffentlich nicht belastbar ermittelt. | Sehr kleiner offizieller Balanceanschluss, eigener relevanter Anbieter. Null-/Fehlerschema lesen; erst später günstiges Modell testen. Keine Aufladung auf Verdacht. [Balance](https://api-docs.deepseek.com/api/get-user-balance) |
| **Fireworks** | **5/5** | FAQ nennt **$1 Startercredit**, ohne Zahlungsmethode Stopp nach Verbrauch. Individuelle Gewährung/Ablauf prüfen. | Besonders gutes Verhältnis: Gateway plus Quotas plus externe Usage-/Kostenabfragen. Trial erst nutzen, wenn Adapter/Tests vorbereitet sind; Serverless statt dedizierter GPU. [Starter](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit) |
| **SambaNova** | **5/5 Gateway** | **$0**, dokumentierter Free Tier ohne Zahlungsmethode. | Einfach selbst wiederholt testbar, eigener Host. Kein unabhängiger Monitor belegt; kein Aufwand in einen Header-only-Monitor stecken. [Limits](https://docs.sambanova.ai/docs/en/models/rate-limits) |
| **Cerebras** | **4/5** | **$0 Kauf**, aber **verifizierte Zahlungsmethode nötig**. $5 Trialcredit für 30 Tage; danach Stopp ohne Guthabenkauf. | Interessante Inferenzperformance und gute Gatewayergänzung. Trial erst starten, wenn Implementierung testbereit ist. Kein dauerhafter Free Tier; automatisches Recharge nicht aktivieren. [Aktuelle Trialregeln](https://inference-docs.cerebras.ai/support/rate-limits) |
| **Scaleway** | **3/5**, später guter EU-Anschluss | 1 Mio. Freitokens dokumentiert, Zahlung/KYC nötig; **kein Hard Cap**. Kontoeröffnung kann €1-Autorisierung auslösen. | Mehrwert durch EU-Cloud und offizielle Cockpit-/Billingdaten. Für Nullrisiko-Testkorb ungeeignet; kleine manuell begrenzte Requests wären möglich, wenn der Cloudaccount ohnehin gewünscht ist. [FAQ](https://www.scaleway.com/en/docs/generative-apis/faq/) |
| **MiniMax** | **3/5**, bei eigenem Abo **5/5 Monitor** | Subscription-Key erfordert kostenpflichtigen Plan; kein belegter Gratis-Abo-Test. Aktuelle Planpreise/Keyberechtigung im Checkout prüfen. | Guter Kontingentmonitor, aber für einen einmaligen Test kein zusätzliches Dauerabo kaufen. PAYG/Credit-Pack ist kein Ersatz für den Nachweis der Abo-Fenster. [Plan/Keys](https://platform.minimax.io/subscribe/coding-plan) |
| **Chutes** | **3/5 PAYG**, Abo zunächst zurückstellen | Kein Free Tier. PAYG ohne Mindestbindung beworben; das ist **keine Zusage zur kleinsten Aufladung**. Optional Plus $10/Monat, Pro $20/Monat. | Interessanter Host, aber Abo-Überlauf und automatisierte Last erschweren Qualifikation. Wenn überhaupt kleiner PAYG-Test nach bestätigtem Kaufbetrag. Nicht das Abo für Worker-/Benchmarktests kaufen. [Preise und Nutzungsgrenzen](https://www.chutes.ai/pricing) |
| **Together AI** | **2/5** | **Mindestens $15 erster Kauf**, danach $5; kein Free Trial. | Technisch gut, offizielle History interessant. Gegenüber vorhandenen Hosts zunächst zu wenig zusätzlicher Nutzen für einen weiteren Guthabenrest. Bei konkreter Nutzernachfrage erneut bewerten. [Billing](https://docs.together.ai/docs/billing) |

## 2. Neue Balance-/History-Kandidaten und direkte APIs

| Anbieter | Urteil | Zugangskosten / Hürde | Konsequenz |
| --- | --- | --- | --- |
| **NanoGPT** | **4/5 Gateway**, **3/5 neuer Abo-Monitor** | **Ab $1 Karte oder $0,10 Krypto** für PAYG; Pro aktuell $12/Monat. Kostenloses Webmodell ist **nicht** kostenlos über API. Neue Abo-Verfügbarkeit öffentlich nicht abschließend bestätigt. | Einer der besten kleinen Gatewaytests. Abo-Monitor nur mit bestehendem/zugänglichem Abo testen; PAYG allein belegt dessen Quotas nicht. Inaktiver Management-Token-Test wäre allenfalls Grundqualifikation. [Preise](https://nano-gpt.com/pricing), [Management](https://docs.nano-gpt.com/api-reference/management-api) |
| **Featherless** | **2/5 für eigenen Neukauf**, technisch später **4/5** | Regulär beworben: **Developer ab $50 Credits/Monat**, Chat $25/Monat. Ein neuer Developer-Einstieg nur mit kleinem Einmaltop-up ist **nicht bestätigt**; daher keine Behauptung, jeder Account müsse zwingend $50 kosten. | Nicht eigens $50 binden. Chat-Tarif ist laut Produktseite für interaktive Nutzung und schließt App/API-Traffic, Hintergrundautomatisierung und Benchmarking aus; nicht als billigen Testtarif verwenden. Guthaben-/Historyadapter erst mit passendem vorhandenen Tarif oder gesichertem kleinen Developer-Zugang. [Aktuelle Tarife/Chatgrenzen](https://featherless.ai/platform/inference-api), [Credits](https://featherless.ai/docs/request-pricing-and-credits) |
| **Venice** | **3/5**, bei kleinem PAYG-Einstieg **4/5** | API auch für Free-Accounts, Requests bleiben **bezahlt**. Mindestaufladung unbestätigt. Weder Pro-Abo noch DIEM-Staking als Testvoraussetzung kaufen. | Gute Datenabdeckung, aber Kontotypen/Währungen erhöhen Aufwand. Zuerst Admin-/Inference-Lesepfad bei Nullsaldo; positiver USD-Test nur bei kleinem bestätigten Kaufbetrag. DIEM-Prozent/Reset bleiben separat ungetestet. [Preise](https://docs.venice.ai/overview/pricing) |
| **Novita** | **4/5 bedingt** | Ältere offizielle Blogs nennen $0,50 Startcredit, aktuelle allgemeine Vergabe nicht gesichert. $10-Referral-Seite zeigt **„Referral Ended“**. Mindestaufladung offen. | Balanceadapter klein und nützlich. Kostenlosen/kleinen Zugang erst bestätigen; nicht mit dem beendeten Referral kalkulieren. [Referralstatus](https://novita.ai/referral/), [Balance](https://docs.novita.ai/api-reference/basic-get-user-balance) |
| **SiliconFlow** | **4/5 bedingt** | Für den relevanten internationalen Account keine belastbare Gratis-/Mindestaufladungszusage gefunden. China-Angebote nicht übertragen. | Sinnvoller Balanceanschluss, wenn ohne Kauf lesbar oder kleiner Einstieg möglich. Bis dahin kein Gratisversprechen. [Internationaler Einstieg](https://docs.siliconflow.com/en/userguide/quickstart) |
| **DeepInfra** | **4/5 bedingt** | PAYG dokumentiert, kein verifizierter Mindestkauf/individueller Trial. Startupförderung ist kein allgemeiner Free Tier. | Guter Kandidat durch Gateway und offizielle Billing-/Keylimitdaten. Vor Kauf kleinsten Zahlbetrag prüfen; bei >$5 zunächst weiter hinten. [Preise](https://deepinfra.com/pricing), [Billingindex](https://docs.deepinfra.com/llms.txt) |
| **OpenAI API direkt** | **5/5 Gateway** | Offizielle OpenAI Docs nennen **$5 Mindestkauf**, gekaufte Credits 1 Jahr gültig. Kein allgemeiner Gratiszugang zugrunde gelegt. | Eigenständiger Kernanschluss mit hohem Nutzen. Ein kleiner Prepaidbetrag kann viele kurze Funktionstests abdecken; Modellpreis vorher wählen, Auto-Recharge aus, keinen absichtlichen Guthabenüberlauf testen. Dokumentierter Abschaltverzug bedeutet keinen centgenauen Hard Cap. [Offizielle Billingbeschreibung im Cookbook](https://developers.openai.com/cookbook/examples/o1/using_reasoning_for_routine_generation) |
| **Anthropic API direkt** | **5/5 mit vorhandenen Credits**, sonst **4/5** | Prepaid, Kaufminimum hier nicht eindeutig belegt. **Neue relevante Option:** vorhandene Max-/Team-Abos können monatliche API-Credits beanspruchen; nicht Pro/Free. | Vor jedem Neukauf vorhandene berechtigte Credits prüfen. Max/Team **nicht erst dafür abonnieren**. API-Credits und Coding-Aboquotas bleiben getrennt; Org-History braucht passende Rechte. [API-Zahlung](https://support.claude.com/en/articles/8977456-how-do-i-pay-for-my-claude-api-usage), [Plancredits](https://support.claude.com/en/articles/17154008-monthly-api-credits-for-max-and-team-plans) |

Bei Anthropic nennt der aktuelle Helpartikel mindestens sieben Tage auf dem
berechtigten Plan und keinen zusätzlichen Kartenzwang zum Beanspruchen/Nutzen
dieser Plancredits. Unverbrauchte Credits verfallen am Zyklusende; vorhandene
gekaufte Credits oder Auto-Reload können anschließend weiter verbraucht werden.
Zugang und Rollout im eigenen Konto prüfen, keinen vorhandenen Anspruch annehmen.

## 3. Weitere Gatewayhosts und Aggregatoren

| Anbieter | Urteil | Testkosten / Mehrwert | Empfehlung |
| --- | --- | --- | --- |
| **Cohere** | **4/5** | Kostenloser Evaluation-Key, aktuell 1.000 Calls/Monat laut Limits. Eigene Modellfamilie statt nur zusätzlichem Host derselben Modelle. Karten-/Länder-/Laufzeitdetails nicht vollständig belegt. | Kostenlosen Prototypingzugang nutzen, Produktionsfreigabe separat. Gatewaytest lohnt sich; kein belegter Quota-Monitor. [Limits](https://docs.cohere.com/v2/docs/rate-limits) |
| **Upstage Solar** | **4/5** | Offizieller Einstieg verspricht **$10 Startcredit**; individuelle Gewährung, Ablauf und Kartenpflicht nicht vollständig belegt. Studio-Trial ist ein anderes Produkt. | Guter neuer Modellanbieter; nur Startcredits verwenden, zunächst keine postpaid Zahlungsmethode hinterlegen. [API-Einstieg](https://console.upstage.ai/docs/getting-started) |
| **Kilo AI Gateway** | **4/5** | Dokumentierte `:free`-Modelle kosten null; aktuelle Verfügbarkeit/Keyzugang erst bestätigen. Bezahlte Credits zusätzlich mit 5% Zahlungsgebühr. | Gateway zunächst auf ein explizites Free-Modell festlegen, kein automatischer kostenpflichtiger Fallback. Ähnliche Aggregation wie OpenRouter, daher kleiner Anschluss ohne großen Spezialausbau. [Billing](https://kilo.ai/docs/gateway/usage-and-billing) |
| **Hugging Face Inference Providers** | **3/5** | Aktuell **keine monatlichen Credits für Free Users**. Mindestcreditkauf offen; PRO kostet zusätzlich und ist für reinen Test unnötig. | Nützlich für bestehende HF-Nutzer, aber Routenauswahl überschneidet sich stark mit vorhandenen Hosts. Erst kleiner bestätigter Kauf oder bestehende HF-Credits. [Preise](https://huggingface.co/docs/inference-providers/pricing) |
| **Tinfoil** | **3/5** | API PAYG, Mindestkauf/Trial nicht belegt. Zusätzlicher SDK-/Attestierungsaufwand, dafür echter Schwerpunkt auf vertraulicher Inferenz. | Nach vorne bei konkretem Datenschutzbedarf oder günstigem Testzugang. Aktuelle Doku enthält inzwischen auch **offizielle Admin-Billing-/Usage-/Request-APIs**; kein reiner Gatewaykandidat. [Preise](https://tinfoil.sh/pricing), [Admin API](https://docs.tinfoil.sh/admin/admin-api.md) |
| **Parasail** | **3/5** | **Karte schon für Modellnutzung und API-Keyerstellung erforderlich**. Standardmäßig nachträgliche Abrechnung: automatische Belastung bei $25 aufgelaufenem Verbrauch, Rest am Zyklusende. **$25 ist keine Mindestaufladung.** | Kleiner Serverlesstest kann wenig kosten, aber kein bestätigter harter Kontodeckel. Keine Dedicated-GPU anlegen. Offizielle Billing-API mit MTD-Kosten/History erhöht den Mehrwert; noch kein Rest-/Reset-Monitor. [Preise](https://docs.parasail.io/parasail-docs/billing/pricing.md), [Billing API](https://docs.parasail.io/parasail-docs/api-reference/billing-api.md) |
| **FriendliAI** | **2/5** | **$10 Mindestaufladung** laut eigener Start-FAQ. Promo-Code-Einlösung belegt, allgemein erhältlicher Gratiscode nicht belegt. | Wegen Überschneidung mit anderen Hosts zunächst sparen. Bei kostenlosem gültigen Promo-Zugang oder konkreter Nutzernachfrage wieder interessant. [Mindesteinstieg](https://learn.friendli.ai/articles/4198394149-q4-2-how-do-i-get-started-and-how-much-do-i-need-to-load-first) |
| **Perplexity** | **3/5 für Recherche**, **2/5 für Coding** | Allgemeiner Gratis-API-/Mindesteinstieg hier nicht belastbar belegt. Webabo nicht als API-Zugang einplanen. | Nur wenn lain bewusst Recherche-/Suchprofile anbieten soll. Suchkosten und Zitier-/Toolantworten sind zusätzlicher Qualifikationsumfang. [Produktabgrenzung](https://docs.perplexity.ai/docs/agent-api/migrate-from-sonar/overview) |

## 4. Europäische und große Cloudplattformen

**Keiner dieser Einträge ist pauschal ein $50-Account.** Der einzelne Request kann
sehr günstig sein. Zurückstellung wegen Konto-/IAM-/Billingaufwand und noch
ungeklärter Kostenbegrenzung, nicht wegen einer erfundenen Mindestzahlung.
Keine konkreten Trialbeträge einkalkulieren, solange Land, Vertrag und Produkt
nicht nachweislich passen. Technische Primärquellen stehen im Erweiterungsbericht.

| Anbieter | Urteil | Nutzen gegenüber vorhandenen Anschlüssen | Eigener Test / Entscheidung |
| --- | --- | --- | --- |
| **IONOS AI Model Hub** | **3/5** | Sinnvolle zusätzliche deutsche/europäische Cloudoption. | Kleinen Token-PAYG-Test erst bei ohnehin gewünschtem Account; Mindestbindung/Trial/Hard Cap offen. Einer der sinnvolleren EU-Kandidaten, kein Pflichtkauf jetzt. |
| **STACKIT AI Model Serving** | **3/5** | Deutsche Cloudoption für Nutzer mit entsprechendem Bedarf. | Cloud-/Kundenonboarding und konkreten Vertrag zuerst klären. Nicht eigens einen größeren Vertrag abschließen. |
| **OVHcloud AI Endpoints** | **3/5** | EU-Host und Public-Cloud-Nutzerbasis. | Aktuellen passenden Trial/Token-PAYG prüfen. Frühere freie Beta und eine freie **Batch**-Beta belegen keinen gratis interaktiven Gatewayzugang. |
| **AWS Bedrock** | **2/5 jetzt**, **4/5 bei AWS-Nachfrage** | Hoher praktischer Nutzen für bestehende AWS-Kunden, andere IAM-/Regions-/Abrechnungsvorgaben. | Nur mit bestehendem AWS-Testkonto oder explizit gewünschtem Cloudtest; keine GPUdeployments nötig. Budgets nicht als vollständigen Abschaltmechanismus behandeln. |
| **Azure / Foundry** | **2/5 jetzt**, **4/5 bei Azure-Nachfrage** | Unternehmenszugänge, Deploymentnamen, eigene Ressourcen. | Bestehende Ressource bevorzugen; nicht für einen zusätzlichen Katalogeintrag allein Azure aufsetzen. Trial/Modelldeployment/Billing prüfen. |
| **Google Vertex AI / Agent Platform** | **2/5 jetzt**, **4/5 bei GCP-Nachfrage** | GCP-IAM-/Projektzugänge zusätzlich zum vorhandenen Gemini-API-Anschluss. | Kein weiteres Modell allein durch den Providernamen gewonnen. Eigener Account nur bei realem GCP-Ziel, Tokenrefresh und Billing gesondert qualifizieren. |
| **Oracle OCI GenAI** | **2/5** | Eigene Cloudkonten, Regionen und Enterprise-Anwendungen. | Für erste Zielgruppe geringerer Zusatznutzen; GenAI-Verfügbarkeit/Trial nicht aus allgemeinem Always Free ableiten. Zurückstellen. |

## 5. Coding-Abos, regionale Anbieter und reine Monitore

| Anbieter | Urteil | Kosten-/Zugangshürde | Empfehlung |
| --- | --- | --- | --- |
| **GitHub Copilot** | **4/5 mit eigenem bezahltem Abo**, sonst **3/5** | Aktuelle User-Billing-API gilt ausdrücklich für selbst **gekaufte** Copilot-Pläne. Copilot Free ist kein belegter Zugang zu genau diesem Report. | Monitor lohnt bei vorhandenem Abo. Kein Abo allein für den Report bestellen. Usagebericht belegt nicht automatisch vollständige Restquote. [Billing API](https://docs.github.com/en/rest/billing/usage) |
| **Cursor Teams/Organizations** | **2/5** | Dokumentierter Adminanschluss braucht passenden Team-/Orgzugang. Persönliches Pro-Abo ist kein ausreichender Testaccount für diese API. | Kein Teamabo nur für Integration kaufen. Bei vorhandenem Nutzer/Testpartner mit passendem Plan später vorziehen. [Admin API](https://cursor.com/docs/account/teams/admin-api.md) |
| **Windsurf / Devin Desktop Enterprise** | **1/5 für eigenen Neukauf** | Offizielle API **Enterprise only**, Service-Key/Billingrechte nötig. | Zurückstellen. Kein Enterprise-Vertrag für den Monitor; möglicher späterer Anschluss bei bestehendem geeigneten Zugang. [Aktuelle API](https://docs.devin.ai/desktop/accounts/api-reference/api-introduction) |
| **BytePlus ModelArk Coding Plan** | **2/5** | Preisreferenz nennt Lite **$10/Monat**, Pro $50/Monat. Das ist ein Codingabo mit Nutzungseinschränkungen, kein beliebiger Gatewaytarif. | Selbst Lite erst nach Klärung der lokalen Proxyrolle; keine $50-Stufe nötig oder empfohlen. Separater PAYG-Zugang wäre eine andere Qualifikation. [Preise](https://docs.byteplus.com/en/docs/modelark/model-pricing), [Grenzen](https://docs.byteplus.com/en/docs/modelark/coding-plan-personal-plan-overview) |
| **Xiaomi MiMo** | **3/5 bedingt** | Internationaler Accountzugang, aktueller Trial, Mindestzahlung und stabile Detaildoku noch offen. | Erst kostenlose Zugangs-/Dokuklärung; kein Kauf auf Basis von Werbeaktionen. [Plattform](https://platform.xiaomimimo.com/) |
| **GMI Cloud** | **2/5** | Gratis-/Mindesteinstieg nicht qualifiziert; wieder ein weiterer Host ähnlicher Open-Weight-Modelle. | Nur bei gesichertem kostenlosem/kleinem Zugang oder Nutzernachfrage. [Serverless](https://docs.gmicloud.ai/inference-engine/marketplace/serverless) |
| **Baidu Qianfan / Token Plan** | **2/5** | Verifikation, Deutschlandzugang, Kaufbetrag und Vertragsabgrenzung offen. | Zu viel Onboarding-/Regionalarbeit für frühe Breite; zunächst Katalog/Recherche. [Produkt](https://cloud.baidu.com/product/codingplan) |
| **Tencent Hunyuan / TokenHub** | **2/5** | Migration zu TokenHub, regionaler Zugang und Billing noch zu klären. | Erst aktuelle Plattform stabil qualifizieren, keinen alten Account nur für Tests kaufen. [Migrationshinweis/API](https://cloud.tencent.com/document/product/1729/111007) |

## 6. Lokale Backends: viel Nutzen ohne Anbieterabo

| Backend | Urteil | Geld / Aufwand | Empfehlung |
| --- | --- | --- | --- |
| **Ollama** | **5/5** | Lokaler Server ohne kostenpflichtigen Cloudaccount; vorhandene Hardware und kleines geeignetes Modell nötig. | Einer der besten ersten Anschlüsse. Kleine Modelle reichen für Protokolltests, nicht zur Bewertung großer Codingmodelle. |
| **LM Studio** | **4/5** | Lokaler OpenAI-kompatibler Server; kein kostenpflichtiges Inferenzkonto für den lokalen Test erforderlich. | Weit verbreiteter Desktopfall. Nach Ollama als zweite Laufzeitqualifikation sinnvoll. |
| **llama.cpp server** | **4/5** | Ohne Anbieterabo, auch kleine CPU-/GGUF-Konfigurationen möglich. | Gute zusätzliche unabhängige Serverprobe, allerdings mehr Template-/Parserdetails. |
| **vLLM** | **3/5** | Kein API-Abo; geeignete unterstützte Hardware-/Serverkonfiguration nötig. GPUmiete wäre ein eigener Testkostenpunkt. | Vorziehen, wenn passende Hardware schon verfügbar ist; keine GPU nur für diesen Katalogeintrag mieten. |
| **SGLang** | **2/5 jetzt**, **3/5 mit passendem Server** | Kein API-Abo; weiterer Laufzeit-/Modell-/Hardwareaufwand. | Für erste Desktopzielgruppe nachrangig; nur mit zugänglichem Server selbst qualifizieren. |

Primärquellen: [Ollama](https://docs.ollama.com/api/openai-compatibility),
[LM Studio](https://lmstudio.ai/docs/developer/openai-compat),
[llama.cpp](https://github.com/ggml-org/llama.cpp/tree/master/tools/server),
[vLLM](https://docs.vllm.ai/en/latest/serving/online_serving/),
[SGLang](https://docs.sglang.io/docs/basic_usage/openai_api.md).
Modell-/Softwarelizenzen und kommerzielle Bündelung bleiben getrennte Fragen.

## Konkreter Testkorb und Ausgabenregel als Vorschlag

**Welle 1: Ziel $0 für neue Anbieterzugänge.** Ollama/LM Studio mit vorhandener
Hardware; SambaNova Free; Fireworks-Starter, Cohere Evaluation, Upstage-Startcredit
und ein verfügbares Kilo-`:free`-Modell. Falls Angebot/Region/Keyzugang nicht passt,
Kandidat zurückstellen. Keine Karte nur wegen einer unbekannten Gratisbehauptung
hinterlegen. Cerebras als gesonderte $0-Kaufoption, falls Zahlungsvalidierung
für dich akzeptabel ist; der Trial läuft ab.

**Welle 2: optional $6 vor Steuern/Gebühren für neue Guthaben.** $1 NanoGPT-PAYG
plus $5 OpenAI API. Nur planen, wenn tatsächlich gebraucht und Checkoutbeträge
passen. Der Gesamtbetrag ist gebundenes Guthaben, nicht erwarteter Verbrauch;
keine Gebühren-/Wechselkursgarantie. Vorhandene Anthropic-API-/Plancredits ohne
zusätzliches Abo bevorzugen. DeepSeek/Novita/SiliconFlow nur lesend kostenlos
vorprüfen; zusätzliche Aufladung nicht in die $6 einrechnen.

**Danach:** Vorschlag pro weiterem Provider maximal $5 neue Vorleistung,
einmalig bis $10 bei klarem Alleinstellungsmerkmal. Darüber oder bei laufendem
Abo/Enterprisezugang zunächst kein eigener Kauf. Beträge sind eine empfohlene
Arbeitsregel und weder vom Nutzer festgelegtes Budget noch Kaufautorisierung.

Vor einem später autorisierten Live-Test den Adapter anhand offizieller Schemas
und lokaler Fixtures fertig vorbereiten. Dann sequenziell: Auth/Modellliste,
ein kurzer Text, ein Stream, ein Toolroundtrip, Abbruch und Usagevergleich.
Kein Lastbenchmark, keine parallelen Worker und kein absichtlicher Guthabenüberlauf
nötig, um den ersten Anschluss zu qualifizieren. Ein Testsatz mit kleinem Kontext
und begrenzter Ausgabe kann günstig sein; jede Modell-/Zusatztoolgebühr vorher prüfen.

Für Abo-/Enterpriseadapter ohne passenden eigenen Zugang kann ein recherchierter
Eintrag erhalten bleiben. Vollständig live geprüft wird er erst mit passendem
Account. Null-/Fixturetests oder ein generischer OpenAI-Anschluss ersetzen diesen
Nachweis nicht. Technische Testbarkeit bleibt außerdem getrennt von vertraglicher
Erlaubnis und kommerzieller Distribution von lain.
