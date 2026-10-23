# NVIDIA Build und StepFun / Step Plan

Stand der offiziellen Recherche: **24.10.2026**. Keine Konten, Schlüssel oder
Abonnements vorhanden; keine authentifizierten Provider- oder Inferenzaufrufe.
Die folgenden Angaben stammen aus öffentlich abrufbarer Herstellerdokumentation.
Die Preishinweise der Nutzerliste sind keine Konfigurationsvorgaben.

## Verifizierte Routen

| Route | Hersteller / Inferenz / Gateway | Protokoll und Basis | Modell-ID | Budget |
| --- | --- | --- | --- | --- |
| NVIDIA Build | StepFun / NVIDIA / NVIDIA | OpenAI Chat Completions, `https://integrate.api.nvidia.com/v1` | `stepfun-ai/step-5-preview` | kostenloser API-Prototypingzugang im Developer Program; kontospezifische Grenzen unbekannt |
| StepFun PAYG | StepFun / StepFun / keiner | OpenAI Chat Completions, `https://api.stepfun.com/v1` | `step-5-preview` | kostenpflichtige API, getrennt von Step Plan |
| Step Plan | StepFun / StepFun / keiner | OpenAI Chat Completions, `https://api.stepfun.com/step_plan/v1`; Anthropic Messages SDK-Basis `https://api.stepfun.com/step_plan` | `step-5-preview` | Abo-Credits, keine Tokens |

Die API-Key-Dokumentation verlangt eigene Zugangsdaten. Ein Consumer-Abo ist
keine Quelle allgemeiner API-Credits. Dieselbe Modellfamilie hat hier verschiedene
Routen, Inferenzanbieter und abrechnende Konten.

## NVIDIA Build

Die [Build-Modellseite][nv-build] nennt die vollständige Modell-ID, den
`POST /v1/chat/completions`-Aufruf, Bearer-Authentifizierung, `stream` und SSE,
Text-/Bildeingaben, Tool Calling, Reasoning und **1M Kontext**. Die [Inference-API-Referenz][nv-infer] belegt `max_tokens` von 1 bis
1.048.576 (Standard 4096), einschließlich Reasoning und Antwort. Prompt plus
Ausgabe müssen ins Kontextfenster passen; dies ist keine unabhängige zusätzliche
Ausgabezuteilung. Die Route unterstützt höchstens acht Bilder und ausdrücklich
kein Video/Audio. `tool_choice` unterstützt `auto`, `none`, `required` und eine
benannte Funktion. Bei Fortsetzungen sind `reasoning_content` und passende
`tool_call_id` zu erhalten. Reasoning bleibt auf diesem Checkpoint aktiv;
`enable_thinking=false` darf nicht als funktionierende Abschaltung behandelt
werden. SSE läuft bis `data: [DONE]`; Reasoning, Inhalt und Toolaufrufe kommen
als Deltas. Konkrete Reasoning-Effort-Stufen der NVIDIA-Route bleiben unbekannt.
Die [Modellkarte][nv-card] relativiert nutzbaren Kontext und Parallelität nach
Hostingkonfiguration.

[NVIDIAs FAQ][nv-faq] bestätigt kostenlosen Zugang zu NIM-API-Endpunkten für
Prototyping über das NVIDIA Developer Program. Entwicklung, Tests, Forschung und
Evaluation sind vom Produktionseinsatz zu unterscheiden; für Produktion nennt
NVIDIA eine AI-Enterprise-Lizenz. Die 90-tägige Enterprise-Evaluation und die
16-GPU-Grenze für selbst gehostete NIMs sind **kein** kostenloses Tokenbudget der
Build-API. Kein belastbarer fester RPM-/TPM-/Tages-/Parallelitätswert und kein
öffentlicher API-Key-Quota-Endpunkt für diese Route wurde verifiziert. Solche
Werte bleiben unbekannt, niemals „unbegrenzt“ oder ein ausgedachtes Restbudget.

„Nur kostenlos“ darf aus der kostenlosen Angebotsbeschreibung allein keine
Ausführungsfreigabe ableiten. Die Maschinenkennzeichnung bleibt `unsupported`,
bis Endpoint-, Account- und Requestkontrollen nachgewiesen Kosten ausschließen.
Unbekannte Kontoberechtigung oder ein erschöpfter Entwicklungszugang darf keinen
bezahlten Fallback aktivieren.

Einrichtung: NVIDIA-Konto und Developer-Program-Zugang selbst erstellen, Modellseite
öffnen, dort API-Key erzeugen und als eigenes Secret mit der offiziellen Basis
speichern. In lain ist ohne Schlüssel `missing_credentials`, mit Schlüssel bis
zur echten Prüfung `unverified` der richtige Zustand. Die Produktivfreigabe ist
nicht aus der kostenlosen Entwicklungsberechtigung ableitbar.

## StepFun direkt und Step Plan

Die [Step-5-Modellseite][step-model] bestätigt **1M Kontext / maximal 64k Ausgabe**,
Streaming, Tools, JSON Mode/JSON Schema sowie Reasoning `low`, `medium`, `high`.
Chat Completions verwendet `reasoning_effort`; Messages verwendet
`output_config.effort`. Text, Bilder und Videos sind beim direkten Hersteller
beschrieben; die NVIDIA-Route wird deshalb nicht automatisch um Video erweitert.

[PAYG-Schnellstart][step-payg] und [Step-Plan-API][step-plan-api] belegen getrennte
Pfade. Beim Anthropic-SDK darf die Basis kein abschließendes `/v1` enthalten,
weil das SDK `/v1/messages` ergänzt. Keine automatische Umschaltung von
`/step_plan/v1` auf `/v1`, wenn Plan-Credits aufgebraucht sind.

Die aktuelle [Plan-Preisseite][step-plan-pricing] nennt monatlich:

| Plan | monatlicher Preis | monatliche Credits |
| --- | --- | --- |
| Mini | ¥49 | 400M |
| Plus | ¥99 | 1.600M |
| Pro | ¥199 | 8.000M |
| Max | ¥699 | 40.000M |

M bedeutet eine Million Credits. Credits werden mit 1 Yuan = 1.000.000 Credits
beschrieben und sind **keine Tokens**. Monatsreste verfallen ohne Übertrag;
Quartals-/Jahreszahlung ändert die monatliche Zuteilung nicht. Step 5 Preview
verbraucht werktags 09–12 und 14–18 Uhr Beijing doppelte Credits gegenüber
Nebenzeiten; historische Abos können abweichen. Add-ons haben eine eigene
30-Tage-Gültigkeit. Ohne Kontodaten werden weder ein Plan noch Limits, Reset,
Ablauf oder Restbudget aus diesen Angebotszahlen als Kontobestand abgeleitet.

[PAYG-Limits][step-rate] unterscheiden Parallelität, RPM und TPM nach
kumulativen Bareinzahlungen und Kontotyp; Voucher zählen nicht zur Stufe.
Step Plan unterliegt dieser PAYG-Stufung ausdrücklich nicht. Der reale Kontotyp
und seine Stufe bleiben ohne Zugang unbekannt.

### Zulässiger Anwendungsumfang

[Plan-API-Dokumentation][step-plan-api] beschreibt ausdrücklich direkte API-Aufrufe
und Agent-/Coding-Tools. [Werkzeugintegrationen][step-tools] nennen u.a. Claude
Code, Codex CLI, OpenCode, OpenClaw, Goose, Hermes, Pi, Cline und Cursor. Das ist
mehr als eine pauschale Behauptung, das Abo sei nur im Webchat nutzbar.

Der [Step-Plan-Vertrag][step-terms] Abschnitt 3.1 untersagt Überlassung/Weiterverkauf
an Dritte ohne Erlaubnis; 3.3 beschränkt die Nutzung auf unterstützte oder explizit
als kompatibel dokumentierte Integrationen. Abschnitt 3.6 verbietet das Umgehen
von Limits mit Skripten, Proxies, Tokenrotation und anderen Mitteln. Daher wird
Step Plan nicht als universell freigegebenes Mehrnutzer-Proxybudget deklariert.
Ein lokaler Adapter behält diese Einschränkung sichtbar; er darf keine
Werkzeugidentität vortäuschen, Limits umgehen oder ein Nutzerabo teilen. Die
Freigabe einer konkreten lain-Nutzung ist anhand der dann geltenden Regeln zu
prüfen. PAYG bleibt eine gesondert bewusst konfigurierte Route.

### Kontingentabfrage

Der offizielle [Account-Endpunkt][step-account] ist
`GET https://api.stepfun.com/v1/accounts` mit Bearer-Key und Feldern `object`,
`type` (`prepaid`/`postpaid`), `balance`, `total_cash_balance`,
`total_voucher_balance`. Er belegt den **PAYG-Kontostand**, keine verbleibenden
Step-Plan-Credits oder Tokenkontingente. Die Antwort spezifiziert keine eigene
Währungskennung; die CNY-Einordnung folgt der separaten API-Preisdokumentation.
`total_cash_balance`/`total_voucher_balance` sind keine Limits für eine
prozentuale Restanzeige. Nur `balance` wird als Kontostand verwendet.

Kein offizieller öffentlich dokumentierter Step-Plan-Quota-Endpunkt wurde im
Dokumentationsindex verifiziert. Für Plan und NVIDIA bleibt die automatische
Restbudgetabfrage ausdrücklich unsupported; Konsole und Accountlink bleiben
zugänglich. Keine Verwendung privater Console-Endpunkte oder Browser-Cookies.

Einrichtung: [Step-Plattform][step-platform] öffnen, eigenes Konto und ggf.
notwendige Identitätsprüfung vornehmen, eigenen API-Key anlegen. PAYG und Plan
bewusst separat wählen; Plan nur mit vorhandenem berechtigtem Abo. Keine Käufe
oder Registrierungen durch lain. Schlüssel nie in Logs/Usage/Inspector speichern.

## Prüfgrenzen

Öffentliche Dokumentation wurde ohne Authentifizierung abgerufen. Geplante
synthetische HTTP-Fixtures prüfen Normalisierung und Routentrennung; sie
bestätigen weder reale Berechtigungen noch Performance, Limits oder Kompatibilität
eines echten Providerzugangs. Fehlende Zugangsdaten blockieren Onboarding und
Konfiguration nicht, bleiben aber als fehlend sichtbar.

[nv-build]: https://build.nvidia.com/stepfun-ai/step-5-preview
[nv-card]: https://build.nvidia.com/stepfun-ai/step-5-preview/modelcard
[nv-infer]: https://docs.api.nvidia.com/nim/reference/stepfun-ai-step-5-preview-infer
[nv-faq]: https://docs.api.nvidia.com/nim/docs/product
[step-model]: https://platform.stepfun.com/docs/zh/guides/models/step-5-preview.md
[step-payg]: https://platform.stepfun.com/docs/zh/quickstart/overview.md
[step-plan-api]: https://platform.stepfun.com/docs/zh/step-plan/integrations/reasoning-api.md
[step-plan-pricing]: https://platform.stepfun.com/docs/zh/step-plan/pricing.md
[step-rate]: https://platform.stepfun.com/docs/zh/guides/pricing/rate-limits.md
[step-tools]: https://platform.stepfun.com/docs/zh/step-plan/tool-integrations.md
[step-terms]: https://platform.stepfun.com/docs/zh/agreement/step-plan.md
[step-account]: https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md
[step-platform]: https://platform.stepfun.com
