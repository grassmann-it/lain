# Reale Modellprüfung vor dem Release, 26.10.2026

Aktueller Folgeprüfstand: [Provider-, Tool- und Kontextnachschärfung](provider-tool-context-repair-2026-10-26.md).
Die historischen Ergebnisse dieses Berichts bleiben unverändert.

Alle **59 konfigurierten konkreten Modellaliase** wurden über das aktive
Lain-Gateway angefragt. Der erste kontrollierte Durchlauf ergab **31 bestandene
Toolabläufe und 28 nicht bestandene Tests bei 94 Client-HTTP-Aufrufen**. Ein
separater Diagnoseplan benötigte fünf weitere Aufrufe: Kimi `k3` bestand mit
automatischer Tool-Auswahl, Beam akzeptierte 128 Definitionen und lehnte 129 ab,
Cohere wurde durch die Kompatibilitätsprüfung blockiert. Damit haben **32
verschiedene Aliase in mindestens einem Szenario einen Toolablauf abgeschlossen**.
Das ist keine vollständige Freigabe der VS-Code-/Pluginoberfläche.

Der Nutzer hat reale Requests je Modell unter realistischen Bedingungen
ausdrücklich als Arbeit vor dem Release verlangt. Die bestätigte Anforderung
steht in [product-scope.md](product-scope.md#reale-modellprüfung-vor-dem-release-26102026).
Die 28 ersten Fehlschläge sind **keine 28 bewiesenen defekten Modelle**:
Testbeschränkungen, Requestformat, Modellverfügbarkeit, Zugänge und Kapazität
müssen getrennt ausgewertet werden. Der Kimi-Folgeversuch zeigt diesen
Unterschied konkret.

## Bezugsstand und Prüfumfang

- Sourcebasis `fef478f601f46b315939fa53f02caa9791086f1d` mit umfangreichem WIP;
  installierter Host `1.0.0+ff5771c47fd61ead38d82d294b053c0dee21c67d`.
- Aktives Profil `lain`, Revision und angewandte Revision **57**. Der erste
  Durchlauf begann 15:24:36Z und endete 15:28:36Z; Diagnose 15:33:10Z–15:33:21Z.
  Die Bindung blieb an den geprüften Snapshots erhalten.
- 59 konkrete Aliase einschließlich versteckter Katalogeinträge; 16 Routeraliase
  sind keine zusätzlichen Modelle. Gleiche Upstreamnamen über Codex und Command
  bleiben getrennt. Jeder Aufruf pinnt den bestehenden Account seiner Route.
- 22.638 Bytes sinnvoller synthetischer Projektregeln zur Rechnungsverarbeitung,
  eine kleine Pythondatei mit fehlender Mengenvalidierung, sieben tatsächlich aus
  Codex 0.164.0 erfasste Funktionstools, Streaming, fester Dateilesebefehl und
  Toolresultat-Fortsetzung. Keine privaten Projektdateien oder Gespräche versandt.
- Der erste Request erzwingt `exec_command`, der zweite verwendet
  `tool_choice: none`; beide reservieren 2.048 Ausgabetokens. Native Codexdefaults
  mit `auto` oder ausgelassenem Ausgabelimit sind dadurch nicht allgemein geprüft.
  Die exportierte Reasoningvoreinstellung wurde unverändert verwendet.
- Nur `/usr/bin/cat` auf die eigene Fixture wurde ausgeführt, niemals vom Modell
  gelieferter Shelltext. Der Test verlangt exakt `cmd` und `workdir`; zusätzliche,
  auch harmlose native Argumente führen zu `tool_arguments_outside_fixture`.
  Solche Fälle bleiben für die Routenkompatibilität unentschieden.
- Höchstens zwei Aufrufe pro Alias, drei Testthreads, höchstens ein laufender
  Request pro Provider. Keine Harnesswiederholung oder Ersatzroute. Konten,
  Profile, Modelle, Tools der Nutzersitzung und Dienste wurden nicht verändert.

Die sieben Tools schließen MCP, Plugins, native Namespace-/Customtools und
Delegation aus. Die vollständige IDE-Konfiguration und lange echte Gespräche
bleiben offen. Ein bestandener Test belegt seinen Payload; die Antwortprüfung
auf `FINDING:`/`FIX:` und Mengenvalidierung ist keine umfassende Qualitätsbewertung.

## Gezielte Grenzfälle

| Szenario | Beobachtung | Aussage und Grenze |
| --- | --- | --- |
| Kimi `k3`, `tool_choice: auto`, 2.048 Ausgabetokens | Toolaufruf und Fortsetzung jeweils HTTP 200, finale Antwort vorhanden | Der erste erzwungene Toolstart ist kein Beleg, dass Kimi generell keine Tools unterstützt. Die drei anderen Kimi-Aliase wurden mit `auto` nicht nachqualifiziert. |
| Beam, genau 128 Tools | HTTP 200, vollständiger Stream | Sieben native Funktionen plus 121 explizit synthetische ungenutzte Funktionen akzeptiert. Keine Qualifikation aller echten Plugins. |
| Beam, genau 129 Tools | HTTP 400: `Invalid value for 'tools': maximum number of items is 128.` | Grenze dieser konkreten Command-/Beam-Route reproduziert. Die installierte Route lieferte diese Upstreamdiagnose; ein lokaler Guard wurde hier nicht als wirksam nachgewiesen. |
| Cohere, `auto`, Ausgabelimit ausgelassen | HTTP 400: `request contains a field outside the supported Cohere compatibility subset` | Bereits die Kompatibilitätsprüfung blockiert. Der Sourcevalidator lehnt explizite `tool_choice`- und `parallel_tool_calls`-Felder ab, auch bei `auto` bzw. `false`. Dieser Fall misst keine Kontextkapazität. |

Der alte Diagnoseklassifikator führte Beam/129 als `upstream_rejected`. Die
gesicherte Meldung belegt das Toolanzahllimit; spätere Klassifikatorversionen
erkennen diese Formulierung. Der ursprüngliche Bericht bleibt unverändert.

Die Kontextmeldung aus dem Nutzerscreenshot ist eine **separate Grenze**.
Lains [Kontextfehlernormalisierung](../tests/model-compatibility/context-boundary.md)
verwendet genau diesen Text für einen eindeutigen Upstreamcode
`context_length_exceeded`. Ohne zugehörigen Request, Modell, Kontext und
Ausgabereservierung ist seine konkrete Ursache noch nicht zugeordnet. Der
begrenzte Durchlauf hat weder maximale Kontextfenster gemessen noch die
Screenshotsituation reproduziert. 128 Tools sind kein allgemeines Codexlimit.

## Vollständige erste Modellmatrix

`Bestanden` bedeutet vollständiger Toolablauf und bestandene kleine
Antwortmarkerprüfung. Fehlerkategorien stammen zunächst aus dem begrenzten
Testklassifikator; insbesondere `tool_schema` kann die erzwungene Auswahl betreffen.
`upstream_rejected` ist eine Sammelkategorie und beweist nicht, dass der Provider
erreicht wurde. HTTP 200 allein genügt nicht für einen bestandenen Ablauf.

| Provider | Alias | Erster Test | HTTP |
| --- | --- | --- | --- |
| codex | `gpt-6-astra` | Bestanden | 200 / 200 |
| codex | `gpt-6.1-sol` | Bestanden | 200 / 200 |
| kimi | `k3` | `tool_schema`; auto später abgeschlossen | 400 |
| kimi | `k3-256k` | `tool_schema` | 400 |
| kimi | `kimi-for-coding` | `tool_schema` | 400 |
| kimi | `kimi-for-coding-highspeed` | `tool_schema` | 400 |
| zai | `glm-5.3` | `tool_arguments_outside_fixture` (Testbeschränkung) | 200 |
| zai | `glm-5.3-flash` | Bestanden | 200 / 200 |
| xiaomi-mimo | `mimo-v2.6-pro` | Bestanden | 200 / 200 |
| xiaomi-mimo | `mimo-v2.6-flash` | Bestanden | 200 / 200 |
| commandcode | `cmd-gpt-6-sol` | Bestanden | 200 / 200 |
| commandcode | `cmd-gpt-6-luna` | Bestanden | 200 / 200 |
| commandcode | `cmd-gpt-5.6-sol` | Bestanden | 200 / 200 |
| commandcode | `cmd-deepseek-v4-pro` | `tool_schema` | 400 |
| commandcode | `cmd-deepseek-v4.1-flash` | Bestanden | 200 / 200 |
| commandcode | `cmd-kimi-k3` | Bestanden | 200 / 200 |
| commandcode | `cmd-glm-5.3` | Bestanden | 200 / 200 |
| commandcode | `cmd-minimax-m3` | Bestanden | 200 / 200 |
| commandcode | `cmd-mimo-v2.6-pro` | Bestanden | 200 / 200 |
| commandcode | `cmd-mimo-v2.6-pro-ultraspeed` | Bestanden | 200 / 200 |
| commandcode | `cmd-step-5-preview` | Bestanden | 200 / 200 |
| commandcode | `cmd-gemini-4-argon` | Bestanden | 200 / 200 |
| commandcode | `cmd-nemotron-3-ultra-550b-a55b` | `unexpected_continuation_tool` | 200 / 200 |
| commandcode | `cmd-nemotron-3.5-super-vl` | Bestanden | 200 / 200 |
| commandcode | `cmd-inkling` | `upstream_rejected` | 400 |
| commandcode | `cmd-inkling-small` | `upstream_rejected` | 400 |
| commandcode | `cmd-muse-spark-1.3` | `tool_schema` | 400 |
| commandcode | `cmd-grok-4.7` | Bestanden | 200 / 200 |
| commandcode | `cmd-mistral-large-4` | Bestanden | 200 / 200 |
| commandcode | `cmd-fugu-ultra` | `authentication` | 403 |
| commandcode | `cmd-muse-wonder-1.0` | `tool_schema` | 400 |
| commandcode | `cmd-beam-free` | Bestanden | 200 / 200 |
| commandcode | `cmd-laguna-s-2.1-free` | `account_or_capacity` | 429 |
| commandcode | `cmd-glyph-cluster-free` | `upstream_rejected` | 400 |
| commandcode | `cmd-claude-sonnet-5-5` | `tool_schema` | 400 |
| commandcode | `cmd-claude-haiku-5-5` | Bestanden | 200 / 200 |
| commandcode | `cmd-claude-opus-5-5` | `tool_schema` | 400 |
| commandcode | `cmd-claude-fable-5-1` | `tool_schema` | 400 |
| commandcode | `cmd-qwen3.8-max-0902` | Bestanden | 200 / 200 |
| commandcode | `cmd-ling-3.1-flash` | Bestanden | 200 / 200 |
| codex | `gpt-6-luna` | Bestanden | 200 / 200 |
| codex | `gpt-5.5` | `model_unavailable` | 400 |
| codex | `gpt-5.6-terra` | Bestanden | 200 / 200 |
| codex | `gpt-5.6-luna` | Bestanden | 200 / 200 |
| codex | `gpt-6-sol` | Bestanden | 200 / 200 |
| codex | `gpt-5.6-sol` | Bestanden | 200 / 200 |
| kilo | `google/gemma-4-26b-a4b-it:free` | `upstream_rejected` | 503 |
| nvidia-build | `nvidia-step-5-preview` | `transport_or_timeout` | keine Antwort |
| codex | `codex-auto-review` | Bestanden | 200 / 200 |
| cohere | `command-a-plus-05-2026` | `upstream_rejected` | 400 |
| upstage | `solar-mini4-260922` | Bestanden | 200 / 200 |
| cloudflare-workers-ai | `@cf/google/gemma-4-26b-a4b-it` | `upstream_rejected` | 200 / 200 |
| cloudflare-workers-ai | `@cf/zai-org/glm-4.7-flash` | `tool_arguments_outside_fixture` (Testbeschränkung) | 200 |
| sambanova | `Meta-Llama-3.3-70B-Instruct` | `upstream_rejected` | 429 |
| groq | `groq-gpt-oss-20b` | `upstream_rejected` | 200 / 400 |
| gemini-api | `gemini-3.5-flash-lite` | `upstream_rejected` | 200 / 400 |
| openrouter | `openrouter-llama-3.3-free` | `upstream_rejected` | 404 |
| openrouter | `openrouter/free` | Bestanden | 200 / 200 |
| kilo | `kilo-liquid-lfm-free` | `account_or_capacity` | 429 |

## Belege und Nachvollziehbarkeit

Die [gemeinsam nutzbare JSON-Zusammenfassung](../tests/live/reports/realistic-models-20261026.json)
enthält jede Route, Requesthashes, HTTP-/Streamzustände, beobachtete Usage,
Quellpins und Grenzen. Keine Bearertokens oder rohen Modellantworten.

Der erste Lauf besitzt keine explizite Gesprächskennung. Parallel liefen echte
Luna-/Sol-Recherchen; ein breites Zeitfenster darf deren Verbrauch nicht dem Test
zurechnen. Der Inspector meldete verworfene Events und unvollständige Abdeckung.
**94 Clientaufrufe sind kein unabhängig belegter Zähler aller Upstreamversuche.**

Die fünf Diagnoseaufrufe tragen eigene Conversation-/Turnheader. Eine begrenzte,
vollständige öffentliche `lain attempts`-Seite enthält genau fünf passende
Datensätze; die drei erfolgreichen Antworten stimmen auch über Response-IDs
überein. Die Coherezeile ist eine lokale Ablehnung, keine nachgewiesene
Providerinferenz. Interne Versuche weiterer Gateways bleiben getrennte Evidenz.

Private Originalbelege:

- `~/.cache/lain-realistic-models-20261026-run1/`: eingefrorener Plan, Bericht,
  Fixture und exakt ausgeführte frühere Source-/Testkopien.
- `~/.cache/lain-realistic-models-20261026-boundaries1/`: separater Plan, Bericht,
  korrelierte öffentliche Attempts und exakt ausgeführte Sources samt Hashes.
- Erster Plan-SHA256:
  `5609b4e7e91258b1d077e23238b4b2259344226b3ca42767e372ab91300b3050`.
- Diagnoseplan-SHA256:
  `0b0a4e26548ea10cf9ebba19cf3b62d865f5dda6eb383620d2375accecff74c4`.

Die aktuelle Harnesssource enthält nachträgliche Diagnoseverbesserungen; sie
wurde nicht rückwirkend zur ausgeführten Source erklärt. Ursprüngliche Fehler
und deren eingeschränkte Diagnostik bleiben erhalten. Reproduktion und
Budgetgrenzen stehen in [tests/live/README.md](../tests/live/README.md).
Elf fokussierte Offlinechecks für Routebindung, SSE, feste Befehlsausführung,
redigierte Fehler und Szenariengrenzen bestanden. Keine erneute Liveinferenz
allein wegen späterer Diagnose-/Dokumentationsänderungen.

## Nächste Releasearbeit aus diesen Ergebnissen

Die folgenden Arbeitspakete sind Empfehlungen aus dem Befund, keine bereits
gelieferten Providerfixes oder weitere Freigaben zu Konfigurationsänderungen.

1. **Native Requestformate klären.** Automatische und erzwungene Toolauswahl
   unterscheiden; Kimi nicht pauschal als defekt markieren. Cohere mit seinen
   tatsächlich unterstützten Steuerelementen abgleichen und Clientdarstellung
   dazu prüfen. Keine angeforderte Semantik still entfernen. Die übrigen
   `tool_schema`-Fälle benötigen ebenfalls eine Diagnose vor einem Providerfix.
2. **Die verbleibenden Fehler einzeln auflösen.** Groq/Gemini scheitern erst bei
   Fortsetzung; Cloudflare/Gemma scheitert im zweiten Stream. Nicht als erfolgreiche
   Toolmodelle freigeben, nur weil ihr erster HTTP-Status 200 ist. GLM und
   Cloudflare/GLM mit einem sicheren, weniger künstlichen Argumentvalidator
   nachqualifizieren. Verfügbarkeit, Zugang, 429/503 und Timeout getrennt behandeln;
   keine ausgedachte Quotaerschöpfung oder bezahlte Ausweichroute.
3. **Echte Clientoberfläche und konkrete Grenzen prüfen.** Die tatsächlich
   aktivierten VS-Code-/CLI-Tools zählen und deren Typen erfassen; bei Beam
   zulässige Toolauswahl bewusst konfigurieren. Den konkreten Kontextfehler mit
   Requestzuordnung und beobachteter Eingabe-/Ausgabereservierung reproduzieren.
   Keine willkürliche Tool-/Kontextkürzung oder erfundene Kontextfenster.
4. **Am finalen Releasekandidaten abnehmen.** Nach Fixes nur betroffene Fälle
   gezielt erneut prüfen, Quellen und Artefakte pinnen und angebotene Fähigkeiten
   ehrlich dokumentieren. Der aktuelle Teststand ersetzt weder die vollständige
   Paketabnahme noch den noch offenen eigenen Updatenachweis aus der
   [bisherigen Releaseeinschätzung](release-workers-2026-10-26.md).

Die Modellmatrix ist damit erhoben. Eine allgemeine Aussage „alle Modelle
funktionieren in Codex/VS Code“ oder „releasebereit“ ist durch diesen Stand
nicht gedeckt.
