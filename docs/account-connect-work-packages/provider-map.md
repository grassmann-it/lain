# Anbieterzuordnung für Account Connect

Diese Matrix deckt die **20 Kataloganbieter, MiMo und vier Referenzanschlüsse** ab.
Die [Recherchecoverage](../account-connect-research/coverage.json) enthält die
35 getrennten Produktvarianten. Alle erhalten AC-02/09-Darstellung und AC-10-
Abnahme; die Tabelle benennt zusätzlich den fachlichen Owner. „Qualifizieren“
bedeutet keine bereits nutzbare oder zugesagte OAuthmethode.

| Provider-ID | Bestehender Zugang / sofort sinnvoller Umfang | Connectowner | Weitere Abhängigkeit / bewusst offene Grenze |
| --- | --- | --- | --- |
| alibaba-model-studio | Vorhandenen regionalen Key erhalten | AC-05 | Eigenes CLIprofil; Consolemonitor getrennt, Coding Plan eigener Produktscope |
| cloudflare-workers-ai | Accountgebundenen Token und vorhandene Account-ID erhalten | AC-06 | Eigener Public Client, Discovery, echte Neuronquelle und Inferenz getrennt qualifizieren |
| codex | Eigener Corelogin und externe Access-Snapshots erhalten | AC-07 | Unabhängigen Monitorowner separat prüfen; kein Snapshotrefresh und kein SIWC→WHAM-Schluss |
| cohere | Vorhandenen Produktkey erhalten | AC-08 | Pairing-/Sessionrechte prüfen; Evaluation, Trial und Production nicht gleichsetzen |
| commandcode | Vorhandener Key für Modelle/Quota | AC-05 | CLIbootstrap, begrenzte Übernahme und planabhängigen Providerzugang prüfen |
| fireworks | Vorhandener Key und Quotaadapter | AC-05 | firectl-Isolation/Keyresultat; vorhandenen Scope erkennen, keine neue Walletbehauptung |
| gemini-api | Vorhandener Key plus qualifizierte optionale Projektusage | AC-06 | Effektive Limits/Serienjoin und OAuth-Inferenz gesondert |
| groq | Key, passive Header über normalen Gatewaytraffic | AC-09 | Enterprise-Metrics nur bei belegtem Zugang; kein allgemeiner Standalonemonitor versprochen |
| inception | Key, lokal erfasste Inferenzhistory | AC-09 | Kein qualifizierter allgemeiner Browserbootstrap/Restquotaread; Portal und einfacher Keyfallback |
| kilo | Derselbe Key für Wallet/persönlichen Pass | AC-03 | Device-/CLI-Komfort als eigener späterer Methodennachweis; Free ist Modellfilter |
| kimi | Coding-Key für Modelle/Usage | AC-07 | Offiziellen OAuth-Owner/Transport qualifizieren; Moonshot Platform separat |
| mistral | Studio-/Vibe-Key; Adminmonitor gesonderter Zugang | AC-05 | Vibe-Produktscope/CLIisolation; Enterprise-Adminreader erst nach Header-/Rechtsqualifikation |
| nebius | Token-Factory-Key, passive Ratenwerte soweit angebunden | AC-09 | Dedicated Observability/Cloud-IAM nicht als allgemeines Serverlessbudget ausgeben |
| nvidia-build | Ein vorhandener Public-Endpoint-Key | AC-09 | Kein qualifizierter Kontorest-/Drittlogin; NGC/Enterprise nicht als Ersatz erzwingen |
| opencode-go | Workspace-Key, Go/Plus/PAYG getrennt | AC-09 | MCP-OAuth-Readtools als eigener Erkenntnisversuch; keine fremden rotierenden Tokens übernehmen |
| openrouter | Key und bestehender Keyquotaadapter | AC-04 | Public PKCE; Accountcredits haben gesondert dokumentierte Managementberechtigung |
| poe | Key, Punktequota und vorhandener PKCEhelper | AC-04 | Maintainerclient/Callback/Hostflow; kein erfundener Monatsreset |
| requesty | Vorhandener Key; Selfbudget/-usage ergänzen | AC-05 | AC-03 Reader; unterstützter CLI auth-token-Helper, Orgbalance nur mit passenden Rechten |
| sambanova | Vorhandener Key, Consolecredits als Sessionziel | AC-08 | Eigene Session/Keybootstrap qualifizieren; Monitor führt keine Keyoperation aus |
| stepfun | Vorhandenen PAYG-Key/Balancepfad erhalten | AC-03 | AC-09 Methodenstatus; Step Plan/Region separat, Drittclient und Rollout offen |
| synthetic | Vorhandener Same-Key-Aboquotareader | AC-03 | Wochenlimit-/Regenerationsschema ergänzen, PAYG-Geldsaldo bleibt getrennt |
| upstage | Vorhandener Key, Consoleusage/-credits als Sessionziel | AC-08 | Orgscope/Sessionbridge; reiner Monitor weder Create noch Reveal |
| xai | Inferenzkey mit Teamdiscovery | AC-03 | AC-09 optionaler Managementmonitor nach qualifizierten Rechten; Grok-Consumerlogin ist anderes Produkt |
| xiaomi-mimo | Vorhandenen Produktkey erhalten; Consolequota qualifizieren | AC-08 | CLI allein gibt keine Monitorsession; Individual/Team/PAYG unterscheiden |
| zai | Vorhandener internationaler Key für Modelle/Quota | AC-07 | ZCode-Tokenowner, Projekt/Team/CN getrennt; Key-ID ist kein Secret |

## Konkrete Restarbeit in AC-09

AC-09 ist kein Sammelauftrag, sämtliche noch offenen Anbieterloginverfahren
nebenbei zu implementieren. Es liefert jedem Anbieter einen ehrlichen,
bedienbaren Basiseinstieg und überprüfbare Status-/Fallbackfälle:

- **Groq/Nebius:** Beobachtung nach normalen Requests unterscheiden von
  eigenständig abrufbarer Quote. Fehlende Beobachtung ist weder Null noch Fehler.
- **NVIDIA/Inception:** kurzer Keyweg und Anbieterportal; fehlender Adapter
  beziehungsweise nicht qualifizierte Quelle ausdrücklich von Ablehnung trennen.
- **OpenCode:** vorhandener Workspace-Key; kleiner öffentlicher MCP-Readtool-
  Nachweis kann später eine zusätzliche Monitormethode begründen. Ohne belegten
  passenden Readtool kein OAuthbutton mit versprochener Quota.
- **Step Plan/Kilo Device:** konkrete offizielle Flows als Quellenstatus erhalten;
  bei fehlendem Admission-/Lifecycle-/Rolloutnachweis Keyfallback anbieten.
- **Mistral/Groq Enterprise, xAI Management, Alibaba Coding Plan:** optionale
  Monitoringprodukte benötigen passende Ressourcen/Rechte und eigene Reader-
  Qualifikation. Der normale Inferenzconnect verlangt sie nicht vorsorglich.

Falls für einen dieser optionalen Wege eine zusätzliche produktive Methode
beauftragt wird, benennt W0 vor Beginn einen eigenen Adapterowner und Abnahmeumfang.
Der neue Weg verwendet AC-01 und AC-02; es entsteht kein zweiter Authwizard.
