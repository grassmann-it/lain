# Backendqualifikation A

Stand 23.10.2026. **Der synthetische Core-Nachweis besteht alle 15 Prüfungen.**
Die echte Providerabnahme und die Gesamtfreigabe von A folgen getrennt; dieser
Bericht behauptet keine bereits erfolgten Liveaufrufe.

## Geprüfter Stand und Wiederholung

Geprüft wurde der eigene Launcher auf dem manifestierten Snapshot
`7.3.17-parent-request.3-model-compat.3-startup-readiness.1` einschließlich
lain-Erweiterungen. Finaler Core-SHA-256:

`8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0`

W1 bestätigt diesen Build als eingefrorenen konsolidierten Stand der Commits
`6292b90` und `50b095f`. Quell-/Toolchainprovenienz liegen in
`proxy/source-manifest.json`, `proxy/baseline.json`, `proxy/toolchain.json` und dem
vom Build erzeugten Artefaktmanifest. Die Builderprüfung geht der Kompilierung
voraus; der Testbericht ersetzt diese Provenienzprüfung nicht.

Der [maschinelle Prüfbericht](../tests/backend/fixtures/qualification-result.json)
und [tatsächlich beobachtete, bereinigte Ereignisse](../tests/backend/fixtures/event-examples.json)
sind versioniert. Die Beispiele enthalten Erfolg, zwei Accounts, 429/Retry,
Abbruch und erkennbare Lücken. Sie sind Auszüge verschiedener Quellen, kein
zusammenhängender Cursorbatch zum Einlesen. `--batch-fixture` exportiert dafür
einen vollständigen zusammenhängenden Batch.

```sh
./scripts/build-core.sh
python3 -m unittest discover -s tests/backend -p 'test_*.py' -v
python3 tests/backend/qualify.py --core artifacts/proxy/lain-proxy \
  --report /tmp/lain-qualification.json \
  --examples /tmp/lain-event-examples.json \
  --batch-fixture /tmp/lain-source-batch.json \
  --qualified-artifact /tmp/lain-qualified-core
```

Der Runner friert die Binarybytes einmal ein, sodass paralleles Neubauen keinen
Mischstand im Bericht erzeugt. Bei vollständig bestandenem Lauf kann er genau
dieses Artefakt für unabhängige Abnahme/Liveproben aufbewahren. Im ausgeführten
Lauf liegt es unter `/tmp/lain-w2-qualified-final15`; sein nachträglich geprüfter
Hash stimmt mit dem Bericht überein. Ein neu gebauter anderer Hash benötigt
seinen eigenen Testlauf. Anleitung und wiederverwendbarer `Run`-Aufbau stehen
in [tests/backend/README.md](../tests/backend/README.md).

## Isolation und Protokollumfang

Python dient ausschließlich als Testwerkzeug. Jeder Prüffall verwendet private
temporäre YAML-/Binding-/Auth-/Journaldateien, eigene Kindprozesse, zufällige
Loopbackports und getrennte synthetische Anbieter-/Inference-/Managementschlüssel.
Der Core erbt keine Providercredentials oder Proxy-/Helperkonfiguration aus der
Shellumgebung. Bereitschaft wird gegen die eigene Journal-Source-ID geprüft;
ein Portkonflikt darf keinen fremden Prozess als Testinstanz akzeptieren.
Cleanup beendet nur eigene Kinder und entfernt nur eigene temporäre Ablage.
Der bestehende Proxy und dessen entnehmende Queue werden nicht angesprochen.

Geprüft sind Client **OpenAI Chat Completions**, **OpenAI Responses SSE** über den
Chat-Completions-Upstreamadapter sowie **Anthropic Messages** mit nativem
Messages-Upstream. Alle Toolproben verwenden ausschließlich `fixture_echo` mit
dem Literal `safe`. Ein echter Codex-OAuth-/Command-Code-Transport, eine
Responses-zu-Claude-Liveübersetzung und ein tatsächlicher Harnesslauf sind
hierdurch nicht belegt. WebSocket wurde nicht geprüft.

## Tatsächlich bestandene Prüfungen

| Prüfung | Beobachtung |
| --- | --- |
| Zwei Accounts, gleicher Modellalias | 100 Requests je unterschiedlichem synthetischem Credential; 200 logische Dispatchrequests, 200 Versuche, 400 Ereignisse, 1.800 gemeldete Tokens je Account |
| Replay, Pagination und Neustart | Wiederholte und paginierte Abholung liefern dieselben Ereignisse; Source-/Event-/Request-/Versuchsidentitäten und Zahlen bleiben nach Core-Neustart gleich |
| Chat Completions SSE und Tools | Inhalt, Toolname/Argumente, Toolergebnis und Fortsetzungsantwort passieren den Core; gemeldete Usage ist dauerhaft; Finish-/Usageframes behalten ihre Reihenfolge |
| Responses SSE und Tools | Text-/Funktionsaufrufereignisse, `call_id`, Argumente, explizites Toolergebnis und Antwort werden über den Chat-Upstream übersetzt; gemeldete Usage ist dauerhaft |
| Anthropic Messages | Zwei verschiedene API-Credentials mit demselben Alias bleiben strikt zugeordnet; native Streams, Tool-JSON, `tool_result` und Fortsetzungsantwort funktionieren; vier Versuche sind dauerhaft zugeordnet |
| Unbekannt oder explizit nullwertig | Fehlende Cache-/Reasoningfelder und fehlendes Total bleiben JSON `null`; ausdrücklich gemeldete Nullwerte bleiben `0` und `usageReported=true` |
| Strikter Account und 429 | Der Request bleibt auf A; keine Anfrage an B; Fehlerusage hat unbekannte Tokenmenge und Status 429 |
| Bewusster Pool und Retry | Mindestens ein logischer Request enthält getrennte Versuche A/429 und B/Erfolg mit derselben Request-ID und verschiedenen Versuch-IDs |
| Keywechsel und Wiederanlage | Neue Credential-ID bleibt demselben stabilen Account zugeordnet; Entfernen/Wiederanlegen mit neuer Account-ID benennt alte Ereignisse nicht um |
| Deaktivierung und Recovery | Entfernen von A aus dem Konfigurationssnapshot bei erhaltener Route: strikt 503 ohne Upstreamersatz; ausdrücklicher Pool nutzt B; Wiederaktivierung stellt A wieder her |
| Clientabbruch | Terminaler Zustand `canceled`, unbekannte Tokens ohne Providerusage und beobachtbarer Abbruch des Upstreamstreams |
| SIGKILL während Stream | Vor erster Ausgabe dauerhaft registrierter Versuch überlebt; Neustart erfindet keine terminale Usage; eine spätere Anfrage funktioniert mit neuer Identität |
| Journalfehler und Startfehler | Zweiter Writer, Nicht-Loopbackkonfiguration und zwei Accountnamen desselben Credentials werden abgewiesen; Dateigrößenlimit verhindert Dispatch mit 503; Teilappend wird unverändert abgewiesen; explizite Recovery funktioniert |
| Terminaler Schreibfehler | Bei Chat und Responses werden erfolgreiche Completionmarker bis zur dauerhaften Usage zurückgehalten; Schreibfehler unterdrückt Erfolg, lässt genau einen offenen Versuch erkennbar und blockiert weitere Requests mit 503 |

Die vier Fixture-Selbsttests bestanden separat. Sie prüfen die Zuverlässigkeit
des Fakeanbieters; nur die 15 integrierten Prüfungen oben verwenden den Core.

Lokale Ablehnung vor Dispatch erzeugt keinen Upstreamversuch. Der
Deaktivierungstest weist deshalb drei tatsächlich dispatchte Requests und eine
zusätzliche lokal abgewiesene HTTP-Anfrage aus. Der Nachweis behauptet keine
vollständige Zugriffszählung sämtlicher abgewiesener HTTP-Anfragen.

## Ausfallgrenze und verbleibende Abnahme

Ein harter Prozessabbruch kann bereits verbrauchte Anbieterusage nicht neu
erzeugen. Gemessen wurde ein dauerhaft gespeicherter `attempt_started` ohne
terminales Usageereignis. Der Versuch und dessen Account bleiben rekonstruierbar;
die tatsächlich verbrauchte Tokenmenge bleibt unbekannt. Der gleiche sichtbare
Zustand entsteht bei einem terminalen Journalfehler; nach einem Teilappend
verweigert der Core den Neustart, bis die Ablage bewusst repariert wurde.
Die Suite kürzt kein Journal mit dispatchter Arbeit. Die explizite Recoveryprobe
stellt ausschließlich das nachweislich noch unbenutzte Testjournal wieder her.

Der Runner qualifiziert den dauerhaften Quellanschluss und nicht die spätere
SQLite-Transaktion. W4 hat den vollständigen 400-Ereignis-Batch des früheren
bestandenen Normalpfads auf SHA `683c6105…` über den unveränderten
`SqliteUsageStore` eingelesen und nach zehn Replay-/Reopenzyklen exakt 200
Requests, 200 Versuche, 3.600 Tokens, zwei Accounts und unbekannte
Cache-/Reasoningfelder nachgewiesen. Die Prüfung des abschließenden Hashs und
Consumerfehlerfälle werden im unabhängigen Integrationsnachweis geführt.

Für A bleiben getrennte Nachweise: echte Codex-/ChatGPT- und Command-Code-Proben
mit benanntem Protokoll und harmloser Toolfortsetzung, tatsächlicher
Harnessanschluss sowie unabhängige Vorher-/Nachher-Belege zur Unverändertheit des
bestehenden Diensts und seiner Dateien. Synthetischer Keywechsel belegt keine
OAuth-Erneuerung; Anzeigenamen und Host-Accountlebenszyklus sind eigene
Anwendungsfälle. Die fachliche Tragbarkeit der konkret gemessenen offenen
Versuche nach hartem Abbruch benötigt die Produktentscheidung. Allein die
Dokumentation der Lücke macht A nicht bestanden.
