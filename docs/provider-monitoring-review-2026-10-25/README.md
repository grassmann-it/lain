# Monitoring und Anmeldung: Anbieterübersicht

Stand **25.10.2026**, Quellstand lain `cbf083f`. Zwei GPT-6 Astra und vier
GPT-6.1 Sol haben offizielle öffentliche Dokumentation und Herstellerquellen
gegen den vorhandenen Code geprüft. 20 Anbieter des laufenden Katalogs plus
Codex, Command Code, Kimi, MiMo und Z.ai ergeben **25 Anbieter**; DeepSeek und
MiniMax sind zusätzliche Kandidaten. Die 15 aktuell eingerichteten Anbieter
sind darin enthalten. [Abdeckung](coverage.json).

Die folgenden Wege sind unterschiedlich weit: implementierter Reader,
freigegebene Verbindungsmethode, öffentlicher Herstellervertrag und erfolgreicher
Accountabruf sind **getrennte Nachweise**. Diese Runde enthält keine neuen
Accountabfragen, Logins, Inferenz, Käufe, Keyanlage oder Installation.
Aus einem erfolgreichen Dokumentationsabruf folgt kein Zugriff dieses Accounts.

## Ergebnis für die Gestaltung

Ein fehlender Wert braucht **einen konkreten Grund und einen passenden Schritt**.
Kein allgemeines „Free hat keine Usage“, „Pro kaufen“ oder „OAuth nötig“.
Ein Login kann einen Key liefern, einen OAuthgrant behalten oder eine Hersteller-
CLI/Sitzung zuständig lassen. Nur wenn der jeweilige Reportingweg tatsächlich
freigegeben ist, wird daraus ein ausführbares Angebot in lain.

## Alle vorhandenen Anbieter

Die Gründe in der letzten Spalte sind Vorschläge für die jeweils beschriebene
Situation, keine Diagnose aller realen Konten. Vollständige Endpunkte, Rechte,
Produktgrenzen und datierte Belegstellen stehen im verlinkten Fachbericht.

| Anbieter | Was tatsächlich lesbar/belegt ist | Bester belegter Weg und derzeitige Grenze | Verständliche Aussage bei fehlenden Werten |
| --- | --- | --- | --- |
| **Codex** | Bestehender lain-Reader nutzt `wham/usage`; offizieller App-Server-RPC liefert ebenfalls Account-/Planlimits, ist aber separat anzubinden. | Bestehenden passenden Zugang nutzen; separat besessener Codex-CLI/App-Server ist ein Monitor-Kandidat. Öffentliches Sign-in-Protokoll allein beweist keine Quotarechte. [Belege](subscriptions.md) | „Zugang abgelaufen“ nur bei Ablaufnachweis; sonst „Kontingente derzeit nicht abrufbar“ mit tatsächlicher Ursache. |
| **Command Code** | Bestehender Alpha-Billingreader für rollende Credits und Zusatzwallets; öffentlicher Datenvertrag weiterhin begrenzt. | Passender bestehender Key zuerst. Offizielle CLI-Anmeldung separat qualifizieren; kein belegter pauschaler Pro-/OAuthzwang. [Belege](subscriptions.md) | „Billingquelle derzeit nicht lesbar“; vorhandene letzte Werte alt zeigen, kein neues Abo verlangen. |
| **Z.ai** | Coding-Monitor liefert die gemeldeten kurzen/längeren Limits und Resets; Keytypen sind produktbezogen. | Passenden bestehenden Coding-Key wiederverwenden; Standard-/Coding-/Teamkey nicht gleichsetzen. [Belege](subscriptions.md) | „Passender Coding-Zugang erforderlich“ nur wenn Produkt-/Keyabweichung belegt ist. |
| **Kimi** | Coding-Aboquotas; Open-Platform-PAYG-Balance als anderes Produkt offiziell mit demselben dortigen Key lesbar. | Bestehender Coding-Key für Abo. Open-Platform-Balancereader noch nicht angebunden; dortiger Key wäre für dessen USD-Balance geeignet. Neue Pläne haben 5h + Monat, alte können 7d behalten; Plus genügt laut aktueller Quelle. [Belege](subscriptions.md) | „Coding- und Plattformguthaben sind getrennt“; kein pauschales Pro- oder Wochenlimit voraussetzen. |
| **Xiaomi MiMo** | Individual-Token-Plan-Credits über vorhandenen eigenen Consolemonitor; Team/PAYG andere Scopes. | MiMo-Monitoring am vorhandenen Account verbinden; bestehender Browserstartblocker bleibt lokal zu klären. Inferenzlogin/Key ist keine Monitorsitzung. [Belege](subscriptions.md) | „Consolesitzung für Monitoring fehlt“ bzw. konkreter Browser-/Sitzungsfehler. |
| **Synthetic** | Aktuelle Quote-/Fensterschemas im bestehenden Reader; nächste Regeneration gesondert. | Bestehenden Key nutzen; Einheitenlücke `tickPercent` am Herstellerclient prüfen. Unbekannte Antwort beweist kein fehlendes Abo. [Belege](subscriptions.md) | „Antwort enthält kein erkanntes Kontingent“ mit Diagnose; keine erfundene Null oder Tarifdiagnose. |
| **OpenRouter** | Accountweiter Free-Tageszähler, eigener USD-Keycap; Accountcredits mit zusätzlicher Managementberechtigung. | Bestehender Key oder vorhandener öffentlicher PKCE-Keybootstrap. Free benötigt keinen Management-Key. UTC-Mitternachtsreset im Hersteller-SDK belegt; lain setzt ihn noch nicht explizit. [Belege](kilo-openrouter.md) | „Free-Rest wird nicht gemeldet“ bei fehlendem Feld; USD-Keycap weiterhin getrennt anzeigen. |
| **Kilo** | Persönliches Wallet und Pass mit bestehendem Key; Freepolicy 200/h pro IP, kein daraus gemessener Accountrest. | Vorhandenen persönlichen Key nutzen. Hersteller-CLI/Deviceflow ist Kandidat; `profile --json` kann fehlende Balance als 0 ausgeben. [Belege](kilo-openrouter.md) | „Walletabruf fehlgeschlagen“; weder Wallet 0 noch unbekannt sperrt nachweislich alle Freemodelle. |
| **Gemini** | Free-/Paid-Modellusage und Limitmetriken im richtigen Google-Projekt; Restkapazität nicht automatisch ableitbar. | Vorhandenen isolierten gcloud-Monitorweg samt Projekt/IAM verwenden. Inferenzkey reicht nicht für Monitoring; gcloud-Grant ist breiter als read-only. [Belege](google-cloudflare.md) | „Google-Projektmonitor nicht verbunden“ oder konkrete Projekt-/Rechte-/Serienursache; kein Gemini-Pro-Kaufhinweis. |
| **Cloudflare** | Freie tägliche Neuronallokation dokumentiert; Analyticsquelle und konkrete Account-/Datasetfreigabe unabhängig. | Accountbindung, Analyticsrecht und Datasetsettings prüfen. Wrangler exportiert erneuerbaren Token, passender Analyticsgrant/Isolation noch zu qualifizieren. [Belege](google-cloudflare.md) | „Analyticszugriff ungeklärt“ bei generischem Dataset-/Rechtefehler; nicht „nur Paid“ oder pauschal „neu anmelden“. |
| **Groq** | Vorhandene passive RPD-/TPM-Limits mit beobachtetem Rest und Reset; Enterprise-Prometheus-/Usagequelle separat. | Free-/normaler Key liefert Header bei normalen Anfragen, ohne neue Monitorinferenz. Enterprisequelle nur bei berechtigtem Account. [Belege](api-providers.md) | „Letzter beobachteter Limitstand“ oder „Noch keine Limits beobachtet“; keine universelle Accountrestquota. |
| **NVIDIA Build** | Öffentliche Produktlimits; kein verifizierter Build-Accountrest-/Resetvertrag in den geprüften Quellen. | Build-Konsole/Referenz. NGC, Dedicated/NIM und Cloud Functions nicht als Ersatz für Build-Quotarechte ausgeben. [Belege](api-providers.md) | „Build-Kontomonitor nicht belegt“; öffentliche Obergrenzen sind keine verbleibende Menge. |
| **Cohere** | Trial-/Productionlimits, Firstparty-Console-Usagekandidat. | Bestehender Inferenzkey für Inferenz; eigene Consolequelle erst qualifizieren. Kein nachgewiesener offener Restreader allein durch zusätzlichen Key. [Belege](api-providers.md) | „Console-Usage noch nicht angebunden“; nicht einen bestimmten Tarif als fehlend behaupten. |
| **SambaNova** | Normale Antwortlimits und Firstparty-Consolekandidat für Accountdaten. | Header/Consolequelle getrennt anschließen; veröffentlichter Free-Tier ist kein gemessener Rest. [Belege](api-providers.md) | „Account-Usage noch nicht angebunden“; vorhandene Anfragebeobachtungen separat bezeichnen. |
| **Upstage** | Firstparty-Console-Billing-/Creditskandidat, reguläre Inferenzkeys. | Gebundene eigene Consolequelle qualifizieren; Trialangebot nicht als aktuelles Guthaben setzen. [Belege](api-providers.md) | „Creditmonitor noch nicht angebunden“ und passende Usagekonsole. |
| **Fireworks** | Vorhandene offizielle Quotaquelle; Limitkonfiguration ist nicht automatisch Verbrauch oder Rest. | Bestehender geeigneter Key und exaktes Fireworks-Account nutzen. CLI-Login ist ein gesperrter Platzhalter mit konkreten Qualifikationslücken. [Belege](api-providers.md) | „Quotalimits verfügbar; Verbrauch nicht gemeldet“ bzw. tatsächlicher Rechte-/Ressourcenfehler. |
| **xAI** | Key-/Teamdiscovery existiert; Management-Billingquelle separat dokumentiert. | Passenden Team-/Managementread qualifizieren; Inferenz-Selfread beweist keine Billingrechte. [Belege](api-providers.md) | „Team-Billingzugang noch nicht angebunden“ statt allgemeinem OAuth-/Prozwang. |
| **Poe** | Aktuelle kombinierte Restpunkte, ohne daraus ableitbaren Reset/Anfangszuteilung. | Vorhandener Key oder implementierter PKCE-Keybootstrap nach Maintainerfreigabe. Nutzer registriert keine eigene App. [Belege](other-gateways.md) | „Browserverbindung noch nicht freigegeben; vorhandenen Key verwenden“, wenn genau diese Methode gesperrt ist. |
| **Requesty** | Key-Self, Caps und Usage bereits implementiert; Organisationswallet/Freepool nicht dasselbe. | Vorhandener Key zuerst; Hersteller-CLI kann Key erstellen, Artefakt-/Profilisolationsnachweis noch offen. [Belege](other-gateways.md) | „CLI-Anmeldung noch nicht qualifiziert“ nur für den CLIweg, nicht „keine Monitoring-API“. |
| **OpenCode Go / Go Plus** | Workspace-Inferenz; offizielles Console-MCP mit OAuth/DCR. Konkrete Go/Plus-Resttools nicht nachgewiesen. | Vorhandenen Workspace-Key behalten; DCR-MCP-Toolumfang gezielt prüfen. Kein erfundener eigener Registrierungsblocker. [Belege](other-gateways.md) | „Go-Rest und Reset noch unbekannt“; Console öffnen, weder Plan noch Rest aus Keyform ableiten. |
| **StepFun** | Persönlicher `.com`-PAYG-Balancereader existiert; Step-Plan-Login/Usage im Herstellerclient separat belegt. | Bestehender passender PAYG-Key reicht; Plan-Login-/CLI-Handoff separat qualifizieren. [Belege](other-gateways.md) | „Step-Plan-Monitor noch nicht qualifiziert“; PAYG, Org und Plan nicht zusammenführen. |
| **Alibaba Model Studio** | Regionale Trial-/Workspaceusage und Coding-Plan-Lesepfade im Herstellerclient; lokale Reader noch nicht produktiv verbunden. | Isolierten Console-/CLIweg fertig anbinden; BSS-Kontoguthaben getrennt mit passenden Rechten. Coding-Plan-Monitoring und zulässige interaktive Harnessnutzung getrennt von verbotener Backendnutzung behandeln. [Belege](cloud-regional.md) | „Nutzungsverbindung in lain noch nicht freigeschaltet“; kein weiterer Inferenzkey als vermeintliche Lösung. |
| **Mistral** | Dashboardwerte; Enterprise-Org-Reporting ausdrücklich Enterprise-/Adminzugang, dessen Authdoku widersprüchlich bleibt. | Vibe-Keybootstrap vereinfacht Inferenzzugang, belegt keinen Free-/Pro-Restzähler. Enterprise-Reader gezielt klären. [Belege](cloud-regional.md) | „Programmatische Restquelle für diesen Zugang nicht belegt“; Enterprisebedingung nur für Enterprise-API nennen. |
| **Nebius** | Serverless-RPM/TPM-Header; Dedicated-Monitoring eigenes Produkt, kein universelles Serverlesswallet. | Passive Quelle bzw. berechtigte Dedicated-Metriken gezielt ergänzen; Billingkonsole für Guthaben. [Belege](cloud-regional.md) | „Raten beobachtbar; Kontoguthaben unbekannt“; keine neue Dedicated-Ressource anlegen. |
| **Inception** | Veröffentlichte Tarifraten und normale Antwortusage; kein qualifizierter Accountrestreader. | Key für Inferenz und klar bezeichnete lokale Usage; Portal für Trial/Billing. Referenzraten im Katalog aktualisierbar. [Belege](cloud-regional.md) | „Kontorest nicht gemeldet“; beworbene Trialtokens niemals als verbleibenden Accountbestand setzen. |

## Zwei zusätzliche Kandidaten

- **DeepSeek:** offizieller Same-Key-`/user/balance`-Read für native Guthabenwerte.
  Kleinere Monitorerweiterung, kein belegter OAuthzwang. Der neue Hersteller-
  Harness benötigt weiterhin einen API-Key und ersetzt den Balanceadapter nicht.
- **MiniMax:** offizieller Device-/PKCE-CLIweg, `mmx quota show --output json`
  und getrennte PAYG-Balancequelle. Hersteller warnt selbst vor wechselnder
  Bedeutung von `*_usage_count`; explizite Remainingprozente gegenprüfen.
  Eigene Client-ID nicht kopieren; isolierte Hersteller-CLI ist der Kandidat.

[Belege, Endpunkte und Grenzen beider Kandidaten](api-providers.md).

## Was „bester Onboardingweg“ hier bedeutet

1. Ergebnis vor Anmeldung benennen: Rest/Reset, Wallet, verzögerte Usage,
   Limitkonfiguration oder nur lokale Beobachtung.
2. Bereits geeigneten Zugang wiederverwenden. Kein erneuter Login nur weil
   eine Quelle unbekannte oder veraltete Daten liefert.
3. Belegten delegierten Login bevorzugen, wenn er den nötigen Zugang liefert.
   OpenRouter/Poe können automatisch einen Key ausgeben. Das ist keine
   allgemeine Fähigkeit jedes OAuthproviders.
4. Nötige Ressource und Rechte gezielt führen: Projekt, Account, Workspace,
   Region und ggf. separates Reportingcredential. Ein Browserlogin schafft kein IAMrecht.
5. Ergebnisse unterscheiden: Zugang gespeichert, Identität bestätigt, Reporting
   gelesen. Leere/fehlende Serien bleiben unbekannt. Modelle erst optional danach.

Offizielle CLI-Subprozesse sind nur mit qualifiziertem Artefakt, eigener
Konfiguration, gebundener Identität, Zeit-/Outputgrenzen und klarer Token-/
Refreshzuständigkeit sinnvoll. TUI-Scraping ist kein bevorzugter Reportingvertrag.
Ein vorhandenes `--json` kann fehlen/null irreführend zu 0 normalisieren; Kilo
ist ein konkret belegtes Gegenbeispiel. Kein vorhandenes fremdes CLI-Profil
oder Browsercookiebestand wird als Integrationsschnittstelle angenommen.

Die Prioritäten stehen in den [gebündelten Umsetzungspaketen](../cli-ux-review-2026-10-25/work-packages.md).
Vorhandene Adapter nicht nochmals implementieren; fehlende Registrierung,
Maintainerfreigabe, Quellenqualifikation und Accountrechte gezielt schließen.
