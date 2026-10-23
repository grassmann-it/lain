# Direkte APIs: Gemini, Groq und Mistral

Geprüft am **24.10.2026** anhand der unten verlinkten offiziellen Dokumentation.
Keine Anmeldung, kein Kauf, keine Schlüssel und keine Inferenzanfragen wurden
für diese Vorbereitung verwendet. Dokumentierte Fähigkeiten sind keine erfolgreiche
Verbindungsprüfung mit einem konkreten Konto. Preise und garantierte Quoten sind
bewusst nicht im Katalog hinterlegt.

## Gemini Developer API

- Hersteller und Inferenzanbieter: Google; kein zusätzlicher Gateway.
- Offizieller OpenAI-Kompatibilitätsweg: Basis
  `https://generativelanguage.googleapis.com/v1beta/openai/`, Chat-Endpunkt
  `chat/completions`, Bearer-API-Key aus [Google AI Studio](https://aistudio.google.com/apikey).
  Dies ist ein anderer Protokollweg als das native `generateContent`-API.
- Die konkret vorbereiteten Modelle `gemini-3.5-flash-lite` und
  `gemini-3.8-flash` dokumentieren jeweils 1.048.576 Eingabetokens und 65.536
  Ausgabetokens, Textausgabe, Bild-/Audio-/Video-/PDF-Eingabe, Function Calling,
  Structured Output und Thinking. Das sind Eingabe-/Ausgabelimits, keine Zusage,
  dass beide Maxima gleichzeitig in einem Request verfügbar sind.
- Die Kompatibilitätsdokumentation zeigt Streaming, Tools, Bilder und strukturierte
  Ausgabe. Native Sonderfunktionen werden damit nicht automatisch für diese Route
  zugesagt. Für `gemini-3.8-flash` sind `low`, `medium` und `high` dokumentiert;
  `minimal` führt laut Modellseite zu einem Fehler. Die generische
  Kompatibilitätstabelle ist daher kein Grund, `minimal` auf jeder Route anzubieten.
- Standardinferenz beider vorbereiteten Modelle hat laut Preisseite einen Free Tier.
  Verfügbarkeit/Region und tatsächlicher Projekt-Tier müssen im eigenen Konto
  geprüft werden. Ein Projekt mit aktivierter kostenpflichtiger Abrechnung wird
  nicht allein durch die Modell-ID zu einer kostenlosen Route. Ein Google-
  Consumer-Abo ist kein Nachweis von API-Guthaben.
- RPM, **Eingabe**-TPM und RPD werden **pro Projekt**, nicht pro Key, angewandt.
  RPD wird um Mitternacht Pacific Time zurückgesetzt. Weitere modellabhängige
  Grenzen sind möglich; die aktive Quote steht in AI Studio und ist nicht garantiert.
  Mehrere Keys desselben Projekts ergeben keine unabhängigen Budgets. Ohne belegte
  Projekt-ID wird keine gemeinsame Poolidentität erfunden.
- Keine verifizierte allgemeine Restbudget-Abfrage mit dem gewöhnlichen API-Key
  vorbereitet. Quoten bleiben unbekannt; AI Studio ist der manuelle Prüfweg.
- Die aktuelle Modellübersicht beschränkt 2.5-Modelle auf bisher aktive Nutzer
  dieser Modelle und empfiehlt neuen Projekten 3.5 Flash-Lite oder 3.8 Flash.
  Alte 2.5-IDs werden deshalb nicht als garantiert zugänglicher Einstieg verwendet.

Quellen:
[Kompatibilität](https://ai.google.dev/gemini-api/docs/openai),
[3.5 Flash-Lite](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite),
[3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash),
[Modelle](https://ai.google.dev/gemini-api/docs/models),
[Preiskategorien](https://ai.google.dev/gemini-api/docs/pricing),
[Rate Limits](https://ai.google.dev/gemini-api/docs/rate-limits).

## Groq

- Groq ist der Inferenzanbieter; die vorbereitete GPT-OSS-Modellfamilie stammt von
  OpenAI. **Groq ist nicht xAI/Grok.** Kein zusätzlicher Gateway.
- Offizielle Basis `https://api.groq.com/openai/v1`, Chat
  `/chat/completions`, Modellliste `/models`, Bearer-Key aus der
  [Groq Console](https://console.groq.com/keys).
- Konkret vorbereitet: `openai/gpt-oss-20b` und `openai/gpt-oss-120b`.
  Beide sind in der offiziellen Modellübersicht als Produktionsmodelle mit 131.072
  Kontexttokens und höchstens 65.536 Ausgabetokens aufgeführt. Die jeweiligen
  Modellseiten dokumentieren Text-Eingabe/-Ausgabe, Tool Use, JSON-Object-/Schema-
  Modi und Reasoning. Bild-Eingabe ist auf diesen Routen nicht dokumentiert.
- Free Tier und Developer Tier sind getrennte Angebote. Developer ist PAYG und
  verlangt eine Zahlungsmethode. Werbecredits sind optional und kein garantiertes
  dauerhaftes Budget. Geschwindigkeitswerbung wird nicht als lain-Messung übernommen.
- RPM/RPD/TPM/TPD gelten organisationsweit; manche Organisationen haben zusätzliche
  getrennte Eingabe-/Ausgabetokenlimits. Die jeweils zuerst erreichte Grenze greift.
  Ein großes Kontextfenster bedeutet daher nicht, dass ein Free-Tier-Request in
  dieser Größe akzeptiert wird. Tatsächliche Grenzen stehen in der Console.
- Offiziell dokumentierte Antwortheader: `x-ratelimit-*-requests` beziehen sich
  auf **RPD**, `x-ratelimit-*-tokens` auf **TPM**. Resetwerte sind Dauern, keine
  Unix-Timestamps. `retry-after` ist bei HTTP 429 relevant. Der optionale Parser
  verarbeitet nur beobachtete Header; er sendet keine Inferenz-/Quota-Anfrage.
  Diese Fensterwerte sind keine Monatscredits oder Kosten und kein Nachweis eines
  konkreten Modellbudgets. Beobachtungen eines Requests bleiben dessen Route und
  Konto zugeordnet; ohne belegte Organisations-ID wird kein gemeinsamer Pool erfunden.
- Kein verifizierter eigener Quoten-Polling-Endpunkt vorbereitet. Vor einem echten
  Request ist das Restbudget unbekannt; Preise und Free-Tier-Limits werden nicht
  aus dokumentarischen Beispielwerten übernommen.
- OpenAI-Kompatibilität ist ausdrücklich unvollständig; nicht jede OpenAI-Option
  ist zulässig. Unter anderem werden `logprobs`, `logit_bias`, `top_logprobs` und
  `messages[].name` laut Kompatibilitätsseite nicht unterstützt.

Quellen:
[Kompatibilität](https://console.groq.com/docs/openai),
[Modelle](https://console.groq.com/docs/models),
[GPT-OSS 20B](https://console.groq.com/docs/model/openai/gpt-oss-20b),
[GPT-OSS 120B](https://console.groq.com/docs/model/openai/gpt-oss-120b),
[Rate Limits und Header](https://console.groq.com/docs/rate-limits),
[Abrechnung und Free Tier](https://console.groq.com/docs/billing-faqs).

## Mistral

- Direkter Mistral-Hersteller-/Inferenzzugang, kein zusätzlicher Gateway.
  Offizielle Basis `https://api.mistral.ai/v1`, `POST /chat/completions`,
  Bearer-API-Key aus [Mistral Studio](https://console.mistral.ai/).
  Das dokumentierte Chat-Format bietet Nachrichten, Tools, `stream` mit SSE und
  `[DONE]`, JSON-Schema-Ausgabe und modellabhängige Reasoning-Optionen.
- Konkret vorbereitet: `mistral-small-2603` (Mistral Small 4), statt eines sich
  verschiebenden `latest`-Aliases. Die Modellseite dokumentiert **256k** Kontext,
  Function Calling und Structured Outputs. Die genaue numerische Tokenzahl und
  maximale Ausgabe werden aus diesem gerundeten Wert nicht erfunden. Bilder und
  spezifische Reasoning-Stufen bleiben unbekannt, soweit die für die konkrete Route
  geprüften Quellen sie nicht hinreichend belegen.
- Free mode erlaubt API-Keys ohne Kreditkarte, innerhalb enthaltener Nutzung und
  Rate Limits. Das ist kein unbegrenztes API-Angebot.
- **Aktueller Dokumentationsstand:** Mistral beschreibt globale Free-/Pro-/Education-/
  Team-/Enterprise-Pläne für Vibe, Studio und API. Enthaltene monatliche Nutzung
  wird zwischen Studio, API und Vibe Code geteilt. Diese explizite Aussage ersetzt
  keine Prüfung des konkreten Kontos oder einer historischen Consumer-Mitgliedschaft.
- PAYG ist laut aktueller Dokumentation eine **optionale**, vorausbezahlte
  Erweiterung über das enthaltene Monatsbudget hinaus, kein eigener API-Plan.
  Ohne aktiviertes PAYG kann Nutzung beim Erreichen des Budgets stoppen. Daher
  müssen kostenloses enthaltenes Budget, Abo-Leistungen und bezahlte Überschreitung
  getrennt in der Konfiguration erscheinen. „Nur kostenlos“ darf PAYG nicht
  automatisch aktivieren oder einen unbekannten Tarif als kostenlos behandeln.
- Organisationsausgabenlimit und Workspace-Caps sind weitere, überlappende Grenzen.
  Workspace-Caps sind keine zusätzlichen unabhängigen Credits. Die konkreten
  modellabhängigen API-Limits umfassen TPM und Requests pro Sekunde; Werte sind
  im Admin Panel unter API → Limits zu prüfen.
- Programmgesteuerte Usage über das Admin API ist laut offizieller Dokumentation
  **Enterprise vorbehalten**. Ein gewöhnlicher API-Key begründet keine solche
  Berechtigung. Für diese Vorbereitung keine Quota-Abfrage, kein Scraping und
  kein Ausweichen auf undokumentierte Console-Endpunkte; Restbudget unbekannt.

Quellen:
[API-Key und Free mode](https://docs.mistral.ai/getting-started/quickstarts/studio/activate-and-generate-api-key),
[Chat-API](https://docs.mistral.ai/api/endpoint/chat),
[Modelllisten-API](https://docs.mistral.ai/api/endpoint/models),
[Small 4](https://docs.mistral.ai/models/mistral-small-4-0-26-03),
[Abos und gemeinsames Budget](https://docs.mistral.ai/admin/billing-usage/subscriptions),
[Usage, Limits und Enterprise-API](https://docs.mistral.ai/admin/billing-usage/usage-limits).

## Lokale Qualifikation

Alle Verbindungszustände starten ohne Credential als fehlend/unverifiziert.
Die vorbereiteten Mocks prüfen Endpunkt-/Credential-Zuordnung und
Headerinterpretation ohne Zugang zu einem Anbieter. Der zusätzliche Modelllisten-
Adapter verwendet ausschließlich die offiziell dokumentierten `GET /models`-
Endpunkte (Gemini über den OpenAI-Kompatibilitätsweg); ein erfolgreicher
Metadatenabruf ist weder Inferenztest noch Budget- oder Tarifnachweis. Er läuft
nicht automatisch beim Laden des Katalogs, verweigert Redirects und begrenzt
Antwortgröße und Modellkennungen. Die Python-Katalogtests sind bestanden; die
.NET-Mocks werden mit dem Integrator im gemeinsamen Buildfenster ausgeführt. Live-Modellverfügbarkeit, effektive Projekt-/Organisationslimits,
Abrechnung und End-to-end-Inferenz bleiben bis zu einem ausdrücklich möglichen
Verbindungstest mit eigenen Zugangsdaten ungeprüft.
