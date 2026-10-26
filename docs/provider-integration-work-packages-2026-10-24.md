# Weitere Provider: priorisierte Arbeitspakete

**Stand: 24.10.2026. Status: Vorschlag für den nächsten Ausbau.** Recherche und
Planung sind beauftragt; diese Pakete sind noch kein Implementierungs-, Account-,
Bewerbungs- oder Kaufauftrag. Die laufende CLI-/Tray-Abnahme bleibt unabhängig.

**Konkretisiert nach aktuellem Codeabgleich:** [Anbieterpakete mit 63 einzelnen
Einträgen](provider-work-packages/README.md), [modularer Ausbau](provider-work-packages/modularity.md)
und [fehlende Voraussetzungen](provider-work-packages/prerequisites.md).
Diese Fortführung ist für die nächste Übergabe maßgeblich; die folgende
Wellenplanung bleibt die Begründung der Priorisierung.

## Ziel und Grundlage

Kompakte gemeinsame Auswahlgrundlage: [Entscheidungsmatrix mit Nutzen,
Testkosten und Referralertrag](provider-decision-matrix-2026-10-24.md).

Bestätigt: lain soll viele sinnvolle Anbieter anbieten; der Entwickler möchte
sie persönlich testen können, ohne dafür überall teure Guthaben oder Abos zu
kaufen. Empfehlungen sollen ihm möglichst Geld oder nutzbares Guthaben bringen;
ein günstigerer Nutzereinstieg ist zusätzlich erwünscht. Die genaue Lizenz und
Veröffentlichungsform von lain sowie konkrete Referralplatzierungen sind offen.

Grundlagen: [acht Hauptkandidaten](provider-discovery-2026-10-24.md),
[31 weitere Kandidaten und fünf lokale Backends](provider-discovery-expansion-2026-10-24.md),
[Bewertung aller 39 + fünf mit Testkosten](provider-priorities-budget-2026-10-24.md),
[neue Referralprüfung](provider-referrals-expansion-2026-10-24.md),
[Produktziele](product-scope.md), [Verträge](service-contracts.md),
[Monitorentwurf](monitor-mode.md).

**Empfehlung:** Zuerst kostenlose durchgängige Integrationen, daneben wenige
wirtschaftlich passende Anschlüsse. Provision darf bei ähnlichem Nutzen den
Ausschlag geben; sie ersetzt weder Testbarkeit noch Nutzungsrechte. Kein Kauf
einer Abosammlung. Kein neuer Transportstack nur für zusätzliche Providernamen.

## Reihenfolge und Zuschnitt

Aufwand ist eine relative Schätzung vor Codeprüfung: **S** = bestehendes Protokoll
und kleiner Adapter; **M** = mehrere Daten-/Authpfade; **L** = Betriebs-/Installations-
oder Quellenvertrag betroffen. Keine Tages- oder Fertigstellungszusage.

| Prio / Paket | Prüffähiges Ergebnis | Eigener Testzugang | Aufwand / Voraussetzung |
| --- | --- | --- | --- |
| **P0 · AP0 Zugang und Referrals** | Nächste Accounts lassen sich mit geklärten Kosten-/Referralbedingungen gezielt eröffnen; bestehende wirtschaftliche Möglichkeiten sind erfasst. | Recherche $0; noch kein Signup. | S; zuerst, anschließend je Anbieter fortführen. |
| **P1 · AP1 SambaNova, danach Fireworks** | Je ein eigener qualifizierter Gatewayanschluss; Fireworks zusätzlich offizielle Quotas. | SambaNova Free ohne Karte; Fireworks $1 Startercredit, individuelle Gewährung/Ablauf prüfen. | S je Gateway, M mit Fireworksquota. Zwei getrennte Abschlüsse. |
| **P1 · AP2 NanoGPT** | Kleiner PAYG-Gatewayanschluss; OAuth-/Referralweg als separater Ausbau vorbereitet. | Optional ab $1 Karte; kein neues Pro-Abo. | S API-Key; M OAuth. Preis/Budget und Auszahlungs-/Kanalregeln noch offen. |
| **P1 · AP3 lokale Backends** | Ollama, dann LM Studio mit eigenem kleinen Modell nutzbar. | $0 Anbieterzugang bei passender eigener Hardware. | S–M; unabhängig von Cloudaccounts, bei vorhandener Hardware vorziehbar. |
| **P2 · AP4 kostenlose Breite** | Cohere, Upstage und Kilo-Free jeweils einzeln qualifiziert. | Evaluation/Starter/`:free`; nur tatsächlich gewährte kostenlose Nutzung. | S–M je Anbieter, aktuelle Bedingungen vorab prüfen. |
| **P2 · AP5 Balanceadapter** | DeepSeek zuerst; Novita, SiliconFlow, DeepInfra bedingt danach. | $0 lesend versuchen; keinen unbekannten Mindestkauf auslösen. | S je einfaches Guthaben, M bei Region/Keylimits. |
| **P2 · AP6 Monitor ohne Core** | Installation/Betrieb für Kontingente ohne Inferenzproxy. | Bereits geeignete eigene Zugänge verwenden. | L; Installations-/Fähigkeits-/Authentscheidungen aus Monitorentwurf klären. |
| **P2 · AP7 zulässige Empfehlungslinks** | Nutzer erkennt Entwicklernutzen und seinen eigenen Bonus; nur gültige eigene Links. | Kein Providerabo allein dafür; Affiliatezulassung ggf. nötig. | S–M; Platzierung/Verhalten vorgeschlagen, noch zu entscheiden. |
| **P3 · AP8 vorhandene Abos und externe History** | Erst passende Aboquotas; externe Request-/Kostenhistorie später getrennt. | Nur vorhandener passender Tarif oder später ausdrücklich gewählter Zugang. | M je Quote; L Quellenvertrag für History. |

**Erster Implementierungsvorschlag:** AP0 für SambaNova abschließen, dann genau
dessen Gateway durchgängig qualifizieren. Fireworks folgt als zweiter unabhängiger
Nachweis mit zusätzlicher Quotasemantik. NanoGPT ist danach der attraktivste kleine
Testkauf mit passend dokumentiertem App-Referralweg. AP3 kann mit vorhandener
Hardware vorgezogen werden. Eine ungeklärte Novita-Provision blockiert diese
kostenlosen Anschlüsse nicht.

## AP0 — Zugang und Einnahmemöglichkeiten vor Anmeldung klären

**Nachsteuerung: Vorhandene und schon geplante Anbieter gehören ausdrücklich
dazu.** Vor neuen Accountkäufen zuerst Z.ai wirtschaftlich vorbereiten, dann
aktuelle Synthetic-/Alibaba-Bedingungen sowie Kimi-/ChatGPT-/Command-Code-Angebote
mit vorhandenen Zugängen klären. Für die jetzige Recherche wurden nur öffentliche
Quellen gelesen. [Gezielte Nachprüfung und Übersicht aller 19 bestehenden
Anschlüsse](provider-referrals-existing-2026-10-24.md) enthält außerdem Claude:
das gefundene Gewinnspiel endete 2025; ein aktuelles Programm ist unbestätigt.

**Ergebnis:** Für den nächsten Anbieter liegt ein kleiner prüfbarer Steckbrief
vor. Bestehende Z.ai-/MiMo-/Kimi-/Synthetic-/Alibaba-Möglichkeiten und die neuen
NanoGPT-/Novita-/Kilo-Programme bleiben separat erfasst; Details im Referralbericht.

1. Persönlichen Test, Coding-Agent, parallele Worker, Abo/PAYG und kommerzielle
   Verteilung der BYOK-Software anhand aktueller Vertragsstellen unterscheiden.
   Lokaler Besitz des Keys oder OpenAI-Kompatibilität beweist keine Erlaubnis.
2. Kosten des Zugangs erfassen: Vorleistung, laufendes Abo, Karten-/Identitätsprüfung,
   Region, Trialablauf und nachgewiesenes Verhalten nach Erschöpfung. Budgetalarm
   ist kein Hard Cap; Unknown bleibt Unknown.
3. Referral vor späterem Signup prüfen: Erstkonto-/Erstkaufregeln, eigener
   Anspruch, Nutzerbonus, erlaubter Kanal, Provisionsbasis und Geld/Guthaben.
   AP0 sammelt offizielle Programmlinks; persönliche Links erst aus dem eigenen
   später autorisierten Account. Keine Selbstempfehlung oder fremden Codes.
4. Gewünschten Nachweis auf den Tarif begrenzen: PAYG beweist kein Codingabo;
   persönliches Pro beweist keinen Team-/Adminreport.

**Abnahme:** Jede Aussage hat Prüfdatum und Quelle; offene Rechte/Kosten/Prämien
sind benannt. Bei Kaufzwang außerhalb eines später vereinbarten Budgets wird
der Kandidat zurückgestellt, ohne andere Integrationen aufzuhalten. Ein Referral
ist keine Voraussetzung für einen kostenlosen technischen Anschluss. Öffentlich
nicht geklärte kommerzielle Weitergabe bleibt vor dessen Auslieferung offen.

## AP1 — erster durchgängiger Gatewaynachweis

**SambaNova zuerst.** [Free-Tier-/Limitsdoku](https://docs.sambanova.ai/docs/en/models/rate-limits)
und Vertragsgrenzen stehen im Hauptbericht. Bestehenden API-Key-/OpenAI-Pfad des
gewählten Cores wiederverwenden, soweit nach Codeprüfung passend. Kein neuer
Provider-Frameworkumbau als Vorbedingung. Modellfähigkeiten anhand eines konkreten
Modells qualifizieren; Modellliste ist kein Toolcallnachweis.

**Geplante Schritte und Abnahme:**

- Eigener Account/Key und verfügbare Modelle sind eindeutig zugeordnet; Secrets
  erscheinen weder in CLI/GUI-Status noch Logs/Belegen. Kein Account wird jetzt angelegt.
- Ein kurzer Textrequest, ein SSE-Stream und ein Toolroundtrip mit einem
  nachweislich geeigneten Modell funktionieren über lain. Abbruch beendet den
  Versuch sauber; keine stillen kostenpflichtigen Fallbacks oder Accountwechsel.
- Tatsächliches Upstreammodell, Tokenabdeckung, Fehler und Abbruch bleiben in
  der bestehenden lokalen History nachvollziehbar und nach Neustart erhalten.
  Kosten nur gemäß belegter Semantik; unbekannte Mengen nicht als Null erfassen.
- 401/403, 429, fehlendes Modell und gestörter Stream werden lokal simuliert.
  Ein echtes Limit wird nicht absichtlich ausgeschöpft. Begrenzte sequenzielle
  Liveprobe erst nach späterer Freigabe, kein Benchmark-/Workerlasttest.
- GUI und CLI zeigen dieselben normalisierten Daten. SambaNova-Responseheader
  werden nicht als unabhängig abrufbarer Monitor vermarktet. Externe Account-
  History ist damit nicht nachgewiesen.

**Abschluss:** Quellen-/Fixtureprüfung und echter Nachweis sind getrennt
ausgewiesen. Ohne echten Zugang bleibt `not_tested`; ein generischer erfolgreicher
Provider beweist nicht SambaNova. AP0-offene Vertragsfragen bleiben sichtbar und
werden nicht durch bestandene Protokolltests erledigt.

**Fireworks als eigener zweiter Abschluss:**
[Starterverhalten](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit)
prüfen, Serverless verwenden, keine dedizierte GPU anlegen. Dieselbe begrenzte
Gatewayabnahme; zusätzlich offizielle List/Get Quotas ohne Inferenz lesen.
Quota-Art, Einheit, Scope und Beobachtungszeit erhalten. `updateTime` ist kein
Reset. Billing-/Usage-API wird als mögliche spätere externe History dokumentiert,
nicht still in die lokale History summiert. Optionales
[AMD-Förderguthaben](https://fireworks.ai/partners/amd) ist ein getrenntes,
zulassungspflichtiges Angebot, kein Grund, den Anschluss aufzuschieben.

## AP2 — NanoGPT: günstiger Anschluss mit wirtschaftlicher Perspektive

**Erster Abschluss:** PAYG mit API-Key, bestehender Gatewayabnahme und bewusst
gewähltem Modell/Preis. [Preise](https://nano-gpt.com/pricing): dokumentierter
kleiner Einstieg $1 Karte bzw. $0,10 Krypto; Gebühren/Region vor Kauf prüfen.
Keine Zahlung ist hier freigegeben. Ein kostenloses Webmodell belegt keinen
kostenlosen API-Zugang. Pro-Abo und dessen Resets bleiben separat.

**Zweiter, unabhängiger Abschluss:**
[OAuth/PKCE mit nativer Referralzuordnung](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce).
Erst bestehende lain-Login-/Credentialverträge und erlaubte Scopes abgleichen;
Clientsecret gehört nicht in eine Desktop-App. Loginabbruch, State/PKCE,
Loopbackbindung, Widerruf und secretfreier Status gehören zur Abnahme. Referral
nur mit echtem eigenen Code und sichtbarer Zustimmung; Ablehnung/ungeeigneter
Account muss normalen Login erlauben. Bestehende Attribution bleibt erhalten.
Kein bezahlter Referralabschluss für die Prüfung nötig.

**Monitor separat:** [Management API](https://docs.nano-gpt.com/api-reference/management-api)
mit passenden Lesescopes prüfen. Ohne eigenes aktives Abo sind positive Quotas,
Reset und Überlauf nicht live belegt. Kein teures Abo nur für diesen Nachweis.
USD-/Nano-Gutschrift ist noch keine bestätigte Barauszahlung; vollständige
Auszahlungsregeln sind ein kommerzieller Blocker, kein API-Key-Gatewayblocker.

## AP3 bis AP5 — unabhängig lieferbare Breite

| Paket / Provider | Erster sinnvoller Test und Grenze | Abschluss / Zurückstellung |
| --- | --- | --- |
| AP3 **Ollama** | Kleines vorhandenes/lizenzgeeignetes Modell lokal, Modellliste, Text, Stream, Abbruch; Tools nur mit geeignetem Template/Modell. | Lokale Nutzung ohne Cloudkonto; kein Cloudquota-Monitor. Keine unbemerkte Modellinstallation oder GPUmiete. |
| AP3 **LM Studio**, danach **llama.cpp** | Dieselbe Gatewayprobe an eigenem Server. Modellverwaltung/Lizenz und Serverzugriff getrennt vom Protokoll prüfen. | Je Laufzeit eigener Nachweis; Ollama-Erfolg beweist die anderen nicht. vLLM/SGLang nur bei vorhandener geeigneter Hardware. |
| AP4 **Cohere** | Aktueller Evaluation-Key, dokumentiert 1.000 Calls/Monat; native/OpenAI-Route und Tools konkret qualifizieren. | Evaluationsstatus sichtbar, keine automatische Produktionsfreigabe oder erfundene Restquote. |
| AP4 **Upstage** | Tatsächlich gewährte Startercredits und deren Ablauf/Kartenbedingungen bestätigen; dann kurze Gatewayprobe. | Kein Neukauf, falls beworbene $10 nicht verfügbar sind. |
| AP4 **Kilo** | Ein aktuell verfügbares explizites `:free`-Modell; kein bezahlter Fallback. | Free-Gateway getrennt vom Kilo-Pass-Affiliate. Keine Provision auf Free-Traffic behaupten. |
| AP5 **DeepSeek** | Zuerst `GET /user/balance` bei Nullguthaben, sofern Key ohne Kauf möglich. Gesamt/Bonus/Aufladung je Währung. | Keine Verbrauchsprozente/Resets daraus ableiten. Ohne kostenlosen Keyzugang Mindestzahlung klären und vorerst zurückstellen. |
| AP5 **Novita** | Lesender Balancepfad; Einheit 1/10.000 USD und mögliches `creditLimit`/Schulden berücksichtigen. | Alter $10/$10-Referral beendet. Neuer Affiliate separat; aktueller kleiner Testzugang erst bestätigen. |
| AP5 **SiliconFlow** | `/user/info` auf passendem internationalem/regionalem Host; Währung und Berechtigung erhalten. | Keine China-Credits oder regionale Bedingungen übertragen. |
| AP5 **DeepInfra** | Offizielle Key-/Spend-/Historypfade einzeln auf Monitorumfang prüfen; kleinsten Zugang erst klären. | Keylimit, Monatsbudget und Kontoguthaben sind verschiedene Größen. History später separat. |

Für jeden Balanceadapter: Erfolg, Nullsaldo, fehlende Berechtigung, Timeout,
429 und veralteter letzter Erfolg; nachweislich ohne Inferenzrequest. Nur
erforderliche Leserechte nutzen. `unsupported`, `error`, `stale` und unbekannte
Felder gemäß bestehendem Vertrag. Ein Nullsaldo beweist keinen positiven
Saldo, Reset oder harten Kostenstopp. Adapter müssen nicht aufeinander warten.

## AP6 — eigenständiger Monitor bleibt eigenes Produktpaket

Ein neuer Quotaadapter allein liefert keinen Betrieb ohne Core. Der
[Monitorentwurf](monitor-mode.md) benennt noch offene Paket-/Installationswahl,
Hostfähigkeiten, Authpfade und Moduswechsel. Nächster Schritt ist deren begrenzter
Zuschnitt mit einem bereits geeigneten direkten Adapter, nicht die Übernahme
aller neuen Provider.

**Abnahmeziel:** Frische Installation und Neustart ohne Core-Binary, Coreprozess
oder Inferenzport; kein indirekter Corestart beim Login. Kontingente und gemeldete
Resets in GUI/CLI identisch, kein „Proxy offline“-Fehler im gesunden Monitor,
keine vermeintliche Requesthistorie. Bestehende Konfiguration und lokale History
werden beim Moduswechsel erhalten. Erst nach Entscheidung der genannten
Betriebsfragen als ausführbare Implementierungsübergabe behandeln.

## AP7 — Empfehlungslinks nutzbar machen

Zuerst vorhandene [Standaloneübersicht](provider-referrals.html) um verifizierte
neue Programme ergänzen; heutige Recherche bleibt als separate Quelle erhalten.
**Vorgeschlagen:** regulärer Anbieterlink plus kenntlich gemachter Empfehlungslink,
sichtbarer Entwicklernutzen/Nutzerbonus, Prüfdatum und Bedingungen. Eigener Link
fehlt oder Programm abgelaufen: kein aktiver Referralbutton mit erfundenem Vorteil.
Keine unsichtbare Attribution, keine automatische Änderung von Inferenzrouten.

Für Kilo/Novita zuerst zulässigen lain-Kanal und eigene Teilnahme klären;
automatische Anwendung eines persönlichen Kilo-Referrals ist keine freigegebene
Option. NanoGPT-OAuth ist AP2, keine bloße HTML-Linkänderung. Bewerbungen,
Accountzugriff und Veröffentlichung sind durch diesen Plan nicht beauftragt.
Abnahme der später gewählten UI: Ein Klickziel ist nachvollziehbar, Bonus und
Provision stimmen für das konkrete Produkt, Ablehnung blockiert keine Anmeldung.

## AP8 und übriger Backlog — erst bei passendem Zugang

- **Abo-Monitore:** MiniMax, NanoGPT, GitHub Copilot mit vorhandenem passenden Plan
  vorziehen. Chutes: Abo-Lastgrenzen/PAYG-Überlauf zuerst klären. BytePlus:
  Codingtoolbindung klären, historisches Referral nicht als aktiv einplanen.
- **Externe History später:** Fireworks als erster Quellenkandidat; getrennte
  Herkunft, Accountbezug, Zeitraum/Zeitzone, Cursor, Deduplizierung, Kosten-/
  Tokenabdeckung und Retention vor Import vereinbaren. Kein Addieren derselben
  Nutzung aus Proxyjournal und Anbieterreport. Ein Guthabenverlauf ist keine
  Requesthistorie. Folgeumfang des Monitors bleibt zunächst Quota/Reset.
- **Kleine weitere Käufe:** OpenAI API optional dokumentierter $5-Einstieg;
  Anthropic zuerst mit vorhandenen berechtigten Credits. Cerebras erst bei
  akzeptierter Zahlungsvalidierung ($5 Trial, 30 Tage; kein Dauerfree).
- **Mehrwert abhängig vom Einstieg:** Venice, Tinfoil, Parasail und MiMo bei
  kleinem bestätigtem Zugang bzw. konkretem Datenschutz-/Modellbedarf vorziehen.
  Perplexity erst bei gewünschtem Rechercheprofil. HF/GMI bei vorhandenem Zugang.
- **Zusätzliche Guthaben zurückstellen:** Together $15 erster Kauf, Friendli $10;
  Featherless nicht eigens für regulären $50-Developer-Einstieg kaufen. Chat-Tarif
  ist kein zulässiger billiger Ersatz für App-/Benchmarklast.
- **EU-/Cloudnachfrage:** Scaleway, IONOS, STACKIT, OVHcloud; danach Bedrock,
  Azure/Foundry, Vertex AI und OCI mit passenden vorhandenen Accounts. Kein
  pauschaler $50-Mindestpreis; IAM/Billing/Hard-Cap-Aufwand ist die Hürde.
- **Org-/Regionalhürden:** Cursor Team, Windsurf/Devin Enterprise, Baidu,
  Tencent erst mit geeigneten vorhandenen Zugängen/Regionen. Kein Team-/Enterprise-
  oder Regionalaccount allein für einen Katalogeintrag. GitHub Models ist bereits
  eingestellt und kein neuer Integrationskandidat.

## Vorgeschlagene Budget- und Lieferregel

Zuerst $0 neue Vorleistung. Optional danach zusammen $6 vor Gebühren/Steuern
für NanoGPT $1 und OpenAI $5; danach höchstens $5 je weiterem Anbieter als
Arbeitsvorschlag. **Kein akzeptiertes Budget und keine Zahlungsfreigabe.** Ein
Centbetrag an Inferenzverbrauch rechtfertigt nicht automatisch einen hohen
Mindestkauf. Starter-/Triallaufzeit erst nutzen, wenn der Adapter testbereit ist.

Jeder Anbieter bekommt einen eigenen Abschluss mit Quellen-/Fixture-/Livestatus,
Gateway-/Monitorfähigkeiten und offener Rechteprüfung. Katalogeintrag, vorbereiteter
Adapter und live qualifizierter Anschluss bleiben unterscheidbar. Das erlaubt
viel Auswahl, ohne ungetestete Provider als fertig auszugeben. Bestehende
Accounts, Credentials, Dienstprozesse, Referraldateien und CLI-/Tray-Abnahme
werden von dieser Planung nicht verändert.
