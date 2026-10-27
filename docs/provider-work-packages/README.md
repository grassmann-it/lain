# Anbieter-Arbeitspakete und fehlende Voraussetzungen

Stand 24.10.2026. Auftrag: aktuelle Umsetzung lesend prüfen, Anbieterpakete
anlegen, fehlende Zugänge/Referrals sichtbar machen und modularen Ausbau planen.
**Hier wurden keine Adapter implementiert, Accounts geöffnet oder Live-Tests
gestartet.** Die parallele Lieferung und Repositoryarbeiten bleiben unabhängig.

Bestätigtes Ziel: Neue Anbieter sollen einfach ergänzt werden können. Der
konkrete [modulare Zuschnitt](modularity.md) ist ein am Code geprüfter Vorschlag,
keine durch die Detailtiefe automatisch angenommene Architektur.

## Einstieg für die laufende Umsetzung

**Aktuelle Übergabe nach den ersten Nutzeranmeldungen:**
[Parallelauftrag für Main mit Sol-/Astra-Workerzuschnitt](parallel-handoff.md).
Er ergänzt die konkrete fehlende Fünfergruppe, Quotaquellen/alternative Auth,
brauchbare Statusanzeigen, Standardnamen und privaten CLI-Wizard. Der Nutzer
meldet inzwischen Accounts und teilweise lokale Verbindungen; die folgende
Bedarfsliste ist kein Beleg, dass ein Account weiterhin fehlt. Individuelle
Scopes, Gratisbudgets und Liveproben bleiben gesondert zu prüfen.

1. [M0: vorhandene Anbieterstruktur gezielt modularisieren](modularity.md).
   Kleine kompatible Erweiterungen dürfen gleichzeitig mit einer ersten
   Gatewayintegration erfolgen; kein großer Frameworkumbau vor allen Providern.
2. [Konkrete erste Anbieterpakete](first-wave.md): SambaNova, Fireworks, NanoGPT,
   DeepSeek, lokale Backends und weitere kostenlose/kleine Einstiege.
3. [Pakete für bestehende Referrals](existing-referrals.md): Z.ai zuerst;
   Synthetic/Alibaba-Bedingungen und Kimi-/ChatGPT-/Command-Code-Angebote klären.
4. [Was wir vom Entwickler noch brauchen](prerequisites.md): Zugänge und eigene
   Links getrennt, ohne Credentials in dieser Dokumentation.

IDs in diesem Register sind Arbeitskennungen, **keine neuen Runtime-Provider-IDs**.
Bestehende IDs, Accountbezüge und Historie dürfen daraus nicht umbenannt werden.
Es gibt einen Eintrag je Anbieterziel, jeweils unabhängig abschließbar. Spätere
Einträge sind ein priorisierter Backlog, noch keine vollständige Codeübergabe.

## Wie „fehlt“ und „fertig“ zu lesen sind

„Noch nötig“ bedeutet für dieses Paket **noch nicht belegt/bereitgestellt**.
Wir haben keine persönlichen Konten oder Credentialdateien inspiziert; ein
genannter Account kann bereits vorhanden sein. Ein vorhandener Codeanschluss
ist kein Live-Nachweis. Allein ein fehlender Account blockiert keine offizielle
Recherche, Manifestvorbereitung oder isolierten HTTP-Fixtures.

- **Gateway:** Katalog/Setup, konkreter Transport, Modellroute, begrenzter Text-,
  Stream-, Tool- und Abbruchnachweis soweit unterstützt; lokale persistente Usage.
- **Monitor:** qualifizierter lesender Abruf ohne Inferenz, Einheit/Scope/
  Beobachtungszeit, echte Nullwerte, unbekannt/stale/error und gemeldeter Reset.
- **Referral:** aktuelle produktbezogene Regeln, eigene Berechtigung/eigener Link,
  erlaubter lain-Kanal, Entwicklernutzen und Nutzerbonus getrennt. Fehlender Link
  blockiert **nur** Referralaktivierung, nicht Gateway/Monitor.
- **Externes Historyimportieren:** späteres eigenes Quellenpaket; nicht Teil eines
  Balanceadapters. Standalone-Monitor benötigt zusätzlich sein Betriebs-/Installerpaket.

Je Paket getrennt festhalten: Dokumentation vorbereitet, lokaler Adapter geprüft,
Liveprobe ausstehend/bestanden, kommerzielle Rechte und Referral offen/bestätigt.
Kein pauschales „fertig“, das einen fehlenden dieser Nachweise verdeckt.

**Priorität:** P1 jetzt vorbereiten; P2 nach passendem Zugang/ersten Nachweisen;
P3 bei Nachfrage oder bereits vorhandenem Zugang. Die Reihenfolge ist empfohlen.
Testkosten und Referralbeträge stehen vollständig in der
[Entscheidungsmatrix](../provider-decision-matrix-2026-10-24.md).

## Neue und bereits geplante Anbieter — 39 Einträge

| Paket-ID / Anbieter | Prio | Eigenständiges Ergebnis | Noch nötig für echten Nachweis / Empfehlung |
| --- | --- | --- | --- |
| **PV-SAMBA · SambaNova** | P1 | Gateway | Persönlicher Free-Account und Inferenzkey; konkretes verfügbares Modell. Kein Referral belegt. [Paket](first-wave.md#pv-samba--sambanova) |
| **PV-FIREWORKS · Fireworks** | P1 | Gateway, dann Quotas separat | Account/Key, Accountkennung und Berechtigung für Quotas; tatsächlich gewährte Startercredits samt Ablauf. [Paket](first-wave.md#pv-fireworks--fireworks) |
| **PV-NANO · NanoGPT** | P1 | PAYG-Gateway; Monitor und OAuth separat | Account/API-Key, optional kleiner Guthabenkauf erst nach Budgetwahl; separater Lesetoken, eigenes Abo nur für dessen Quotas; eigener Referralcode und Auszahlungsregeln. [Paket](first-wave.md#pv-nano--nanogpt) |
| **PV-DEEPSEEK · DeepSeek** | P1 | Balance zuerst, Gateway getrennt | Account/Key ohne Aufladung versuchen; wenn unmöglich Mindestkauf klären. Positiver Saldo separat. [Paket](first-wave.md#pv-deepseek--deepseek) |
| **PV-COHERE · Cohere** | P1 | Evaluation-Gateway | Evaluation-Key, aktuelle kostenlose Berechtigung; Produktion ist eigener Tarif-/Rechtenachweis. |
| **PV-UPSTAGE · Upstage** | P1 | Solar-Gateway | Account/Key, tatsächlich gewährte Credits, Ablauf und Kartenpflicht klären. Kein Kauf auf bloße Werbung. |
| **PV-KILO · Kilo** | P1 | Gateway auf explizitem Free-Modell | Account/API-Key, verfügbares Free-Modell; für Geld separat Affiliatezulassung, genehmigter Kanal und eigener Partnerlink. |
| **PV-NOVITA · Novita** | P2 | Balance, dann Gateway; Affiliate getrennt | Account/Key, kostenloser/kleiner Einstieg; Provisionsbasis, Auszahlung, Deutschland-/Kanalzulassung und eigener Link. |
| **PV-SILICONFLOW · SiliconFlow** | P2 | Regional korrekter Balance-/Gatewayanschluss | Internationaler Account/Key, Host/Währung, kostenloser/kleiner Einstieg; keine China-Aktion übertragen. |
| **PV-DEEPINFRA · DeepInfra** | P2 | Gateway und belegte Key-/Billinglimits | Account/Key, erforderliche Verwaltungsrechte, Mindestkauf/Trial; History später. |
| **PV-OPENAI · OpenAI API** | P2 | Direkter API-Gatewayanschluss | Geeigneter Projektkey/Modellzugang; vorhandene API-Credits oder ausdrücklich gewählter kleiner Prepaidkauf. ChatGPT-Abo ist kein API-Guthaben. |
| **PV-ANTHROPIC · Anthropic API / Claude** | P2 | Direkter API-Gatewayanschluss | Geeigneter API-Key/Workspace, vorhandene API-/berechtigte Plancredits; für Orgberichte ggf. Adminrechte. Aktives allgemeines Referral unbestätigt. |
| **PV-CEREBRAS · Cerebras** | P2 | Gateway | Account/Key, Zahlungsvalidierung und Trialbedingungen; erst bei testbereitem Adapter aktivieren. |
| **PV-MINIMAX · MiniMax** | P2 | Abo-Monitor; PAYG-/Abo-Gateway getrennt | Passender vorhandener Plan + Subscription-Key; PAYG-Key nicht gleichsetzen. Eigener Referralcode und vollständige aktuelle Voucherregeln. |
| **PV-MIMO · Xiaomi MiMo** | P2 | Passender internationaler Gatewayzugang | Persönlicher Account/Key, Region/Minimalzahlung; eigener Referralcode und genaue Token-Plan-Berechtigung. Code enthält bereits einzelne MiMo-Sonderbehandlung, kein Vollnachweis. |
| **PV-VENICE · Venice** | P2 | Gateway und Guthabenarten | Account, passender Inferenz-/Adminkey, minimale Aufladung; DIEM-/Pro-Nachweis nur bei vorhandenem Zugang. Eigener Link und aktuelle Pro-Referralregeln. |
| **PV-COPILOT · GitHub Copilot** | P2 | Belegter Billing-/Usage-Monitor | Vorhandener selbst gekaufter berechtigter Plan, GitHub-Auth/Scopes für konkreten Report; kein Kauf nur für Report. |
| **PV-SCALEWAY · Scaleway** | P2 | EU-Gateway; Billingdaten getrennt | Cloudprojekt/Key/IAM, Zahlungs-/KYC-Bereitschaft und gewählter Kostenrahmen; Hard Cap offen. |
| **PV-CHUTES · Chutes** | P2 | PAYG-Gateway, Quota separat | Account/Key, kleinster Kauf, passende Lastrechte und Überlaufregel; Abo nur mit geklärten Workergrenzen. |
| **PV-TINFOIL · Tinfoil** | P2 | Vertrauliche Inferenz, Adminlesepfad optional | Account/Key, kleiner Zugang, SDK-/Attestierungsanforderungen; Adminrechte gesondert. |
| **PV-PARASAIL · Parasail** | P3 | Gateway, Billingdaten getrennt | Account/Karte/Key, akzeptierter Postpaid-Kostenrahmen; kein bestätigter harter Stop. |
| **PV-HF · Hugging Face** | P3 | Inference-Provider-Gateway | HF-Token mit passenden Inference-Rechten, vorhandene Credits/kleiner Kauf, konkrete Upstreamroute. |
| **PV-PERPLEXITY · Perplexity** | P3 | Recherche-/Suchprofil | API-Key/Guthaben, gewünschter Suchumfang und Modell-/Toolgebühren; Webabo nicht gleichsetzen. |
| **PV-TOGETHER · Together AI** | P3 | Gateway; externe History später | Account/Key und vorhandenes Guthaben oder bewusster $15-Erstkauf; Billingberechtigung gesondert. |
| **PV-FRIENDLI · FriendliAI** | P3 | Gateway | Account/Key und vorhandenes Guthaben/Promo oder bewusster $10-Kauf. |
| **PV-FEATHERLESS · Featherless** | P3 | API-Gateway, Balance, später History | Passender Developerzugang, Key und Billingrechte; kein Chatabo als Automatisierungstarif. Keinen $50-Tarif nur für Test. |
| **PV-IONOS · IONOS** | P3 | EU-Gateway | Kundenvertrag/Projekt, passende Zugriffsrechte/Token, kleiner tatsächlich verfügbarer Tarifzugang. |
| **PV-STACKIT · STACKIT** | P3 | EU-Gateway | Cloudkonto/Projekt und Serviceaccount/IAM, freigeschalteter Model-Serving-Vertrag. |
| **PV-OVH · OVHcloud** | P3 | EU-Gateway | Passendes Public-Cloud-Projekt/Token, aktuelle interaktive Modellberechtigung und Billing. |
| **PV-BEDROCK · AWS Bedrock** | P3 | AWS-Gateway | Eigenes Testprojekt/IAM, konkrete Region und Modellzugang, gewähltes Billing; Signierung/Refresh qualifizieren. |
| **PV-AZURE · Azure/Foundry** | P3 | Deployment-Gateway | Vorhandene Ressource/Deployment, API-Version und passender Key oder Entra-Auth, Region/Billing. |
| **PV-VERTEX · Vertex AI** | P3 | GCP-Gateway | Projekt/Region, APIfreigabe, IAM/ADC und Tokenrefresh, passendes Billing. |
| **PV-OCI · OCI GenAI** | P3 | OCI-Gateway | Unterstützte Region/Modellzugang, Tenancy-/IAM-/Signaturdaten und Billing. |
| **PV-CURSOR · Cursor Teams** | P3 | Org-Monitor | Geeigneter vorhandener Team-/Orgplan und Adminkey; persönliches Pro reicht nicht. |
| **PV-DEVIN · Windsurf/Devin Desktop** | P3 | Enterprise-Monitor | Vorhandener Enterprisezugang, Service-Key und Billingrechte. |
| **PV-BYTEPLUS · BytePlus** | P3 | Tarifkonformer Coding-/PAYG-Anschluss | Account/Key/Region und gewünschter Tarif, erlaubte Proxyrolle; Coding-Referral-Verlängerung nicht belegt. Affiliate für andere Produkte separat. |
| **PV-GMI · GMI Cloud** | P3 | Weiterer Gatewayhost | Account/Key und gesicherter kleiner/free Zugang; andernfalls Nachfrage abwarten. |
| **PV-BAIDU · Baidu Qianfan** | P3 | Regionaler Gatewayanschluss | Deutschland-/Regionalzugang, Verifikation, Produkt-/Tarif-Key, Vertrag/Billing. |
| **PV-TENCENT · Tencent TokenHub** | P3 | Aktueller regionaler Gatewayanschluss | Verfügbarkeit/Migrationsstand, Account/Key/Region und Produkt-/Billingberechtigung. |

## Bestehende Anschlüsse — 19 Folgepakete

Hier bedeutet P1 zuerst **vorhandenen wirtschaftlichen Nutzen vorbereiten**, kein
ungefragtes Ändern bestehender Accounts. Weitere Gateway-/Monitornachweise folgen
den heutigen Fähigkeiten; eine besondere neue Quota-API wird nicht erfunden.

| Paket-ID / Anbieter | Prio | Ergebnis | Noch nötig |
| --- | --- | --- | --- |
| **PV-ZAI · Z.ai** | P1 | Aktuelles Referral am bestehenden Anschluss vorbereitet | Eigener Link/Code, zulässiger lain-Kanal und eigene Teilnahme; Credits erst ab drei gültigen zahlenden Empfehlungen. [Paket](existing-referrals.md#pv-zai--zai) |
| **PV-SYNTHETIC · Synthetic** | P1 | Aktuelle Referralregeln statt historischer Werte | Eigener Link, heute angezeigte Regeln/Prämie/Verfall und erlaubter Kanal. |
| **PV-ALIBABA · Alibaba** | P1 | Produktberechtigtes Coupon-Referral | Eigener Link, Teilnahmeberechtigung, konkreter Model-Studio-/Token-Plan-Umfang und Couponscope. |
| **PV-KIMI · Kimi** | P2 | Optionales Los-/Credit-Referral korrekt dargestellt | Eigener Link, aktuelle Kampagnenanzeige/Verfall und erlaubte Weitergabe; kein garantierter Betrag. |
| **PV-CODEX · ChatGPT/Codex** | P2 | Tatsächlich verfügbares Einladungsangebot erfasst | Eigene Kontoanzeige: Angebot, Bedingungen, Ablauf und Link- oder E-Mail-Verfahren; noch nichts versenden. |
| **PV-COMMAND · Command Code** | P2 | Vorhandenes Konto auf konkretes Programm geprüft | Eigene Studioanzeige oder offizielle aktuelle Regeln; bislang nur allgemeine Terms-Erwähnung. |
| **PV-CLOUDFLARE · Cloudflare** | P2 | Bestehenden Cloudanschluss/Transport nachqualifizieren | Persönlicher Key mit passenden Workers-AI-Rechten, Account-ID, Billingstatus; kein Referral bestätigt. |
| **PV-GEMINI · Gemini** | P2 | Bestehenden direkten Anschluss nachqualifizieren | Passender Projektkey, konkret verfügbarer Tarif/Region/Modell; kein API-Referral bestätigt. |
| **PV-GROQ · Groq** | P2 | Bestehenden Gateway nachqualifizieren | Eigener Key/Org-/Tarifstatus, Modell; Header-only-Limits nicht als M ausgeben. |
| **PV-INCEPTION · Inception** | P2 | Vorhandenen Gateway nachqualifizieren | Eigener Key/Modellzugang, tatsächlich geltende Startercredits/Verfall. |
| **PV-MISTRAL · Mistral** | P2 | Bestehenden direkten Anschluss nachqualifizieren | Eigener Key/Workspace/Free- oder Paid-Modus und Modell. |
| **PV-NEBIUS · Nebius** | P2 | Bestehenden Token-Factory-Anschluss nachqualifizieren | Eigener Key/Projekt, Region/Tarif und echtes Guthaben/Trial. |
| **PV-NVIDIA · NVIDIA** | P2 | Bestehenden NIM-/Build-Anschluss nachqualifizieren | Eigener passender Key, Modell-/Entwicklungsberechtigung und aktuelles Kontingent. |
| **PV-OPENROUTER · OpenRouter** | P2 | Bestehenden Gateway/Quota weiter qualifizieren | Eigener Key und korrekter Scope; für striktes Free-only eigens kontrollierter Zero-Cap-Zugang/Enforcementnachweis. |
| **PV-OPENCODE · OpenCode** | P2 | Bestehenden Zugang nachqualifizieren | Eigener passender Key/Tarif/Modellroute; Contributor-Datenfreigabe nur ausdrücklich, altes Referral beendet. |
| **PV-POE · Poe** | P2 | Bestehenden Gateway/Quotapfad nachqualifizieren | Geeigneter persönlicher Token oder qualifizierter OAuthweg, Berechtigung/Points; Creatorvergütung nicht Referral nennen. |
| **PV-REQUESTY · Requesty** | P2 | Bestehenden Gateway nachqualifizieren | Eigener Key/Workspace und aktuelle Route/Billing; kein Referral bestätigt. |
| **PV-STEPFUN · StepFun** | P2 | Bestehenden Gateway/Balancepfad nachqualifizieren | Passender Key/Region, PAYG und Step Plan unterscheiden; kein Referral bestätigt. |
| **PV-XAI · xAI** | P2 | Bestehenden API-Gateway nachqualifizieren | Eigener API-Key/Team mit Modellberechtigung und Guthaben; Webabo ist kein API-Nachweis. |

## Lokale Backends — fünf Einträge

| Paket-ID | Prio | Ergebnis | Noch nötig |
| --- | --- | --- | --- |
| **PV-OLLAMA** | P1 | Lokaler Gateway ohne Cloudaccount | Vorhandene geeignete Hardware, erlaubtes kleines Modell, Serveradresse; Einrichtung ohne erfundenen Pflichtkey und explizite Loopback-Policy. |
| **PV-LMSTUDIO** | P1 | Zweite lokale Desktoplaufzeit | Laufender lokaler Server, geeignetes Modell, tatsächlich gewählte Auth-/Serverkonfiguration. |
| **PV-LLAMACPP** | P2 | Unabhängiger GGUF-/CPU-Server | Kleines GGUF/Template mit passender Lizenz, Server, Toolparser soweit benötigt. |
| **PV-VLLM** | P3 | Eigener Server als Gateway | Bereits vorhandener unterstützter Server/Hardware und Modell, Endpoint/Auth. Keine GPUmiete eingeplant. |
| **PV-SGLANG** | P3 | Weitere eigene Serverlaufzeit | Geeigneter vorhandener Server/Modell, Endpoint/Auth und Templates. |

## Abhängigkeiten und Übergabegrenzen

Das nächste ausführbare Providerpaket verlangt einen aktuellen Codeabgleich:
parallele Änderungen können Teile bereits erledigt oder Verträge verändert
haben. M0 nur soweit umsetzen, wie das erste Paket es benötigt. Ein fehlender
Referrallink darf kostenlose Gatewayvorbereitung nicht aufhalten. Kauftarife,
Affiliatebewerbungen, Anmeldung und echte Inferenz sind gesonderte spätere
Handlungen; keine davon wurde durch das Anlegen dieser Arbeitspakete ausgeführt.

Bestehende API-/Account-/History-Identitäten, eigene Credentials und bereits
installierte Dienste erhalten. Keine fremden Arbeitskopieänderungen, Lockfiles,
Abnahmebelege, Git-History oder Releases in diesen Paketen nebenbei übernehmen.
