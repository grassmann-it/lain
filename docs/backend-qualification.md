# Backendqualifikation A

Stand 23.10.2026. **A noch offen.** Dieser Bericht trennt Fixture-Selbsttests,
synthetischen Core-Nachweis und echte Anbieterabnahme. Vorläufige Ergebnisse
sind kein Produktfreigabesignal.

## Reproduzierbarer Aufbau

Der Nachweis unter `tests/backend` verwendet Python-Standardbibliothek und die
von W1 gebaute `lain-proxy`-Binärdatei. Der Runner kopiert deren Inhalt einmal in
ein eigenes temporäres Verzeichnis und berichtet genau dessen SHA-256. Jeder
Prüffall erhält eigene YAML-/Binding-/Auth-/Journaldateien, zufällige freie
Loopbackports, zwei unterschiedliche synthetische Zugangsdaten und einen eigenen
Managementschlüssel. Es werden ausschließlich selbst gestartete Kinder beendet.
Der bestehende Proxy und dessen entnehmende Usagequeue werden nicht angesprochen.

Aufruf und Umfang stehen in [der Testanleitung](../tests/backend/README.md).
Der Runner erzeugt einen maschinenlesbaren Bericht und auf Wunsch bereinigte,
tatsächlich beobachtete Ereignisbeispiele mit identischem Artefakthash.

## Bisher tatsächlich beobachtet

Der erste integrierte Lauf auf SHA-256
`4681823fa46138f1355f89d2437bb94280b5f804856a71ef889f5a3104294c45`
hat diese Prüfungen bestanden:

| Prüfung | Beobachtung |
| --- | --- |
| Zwei Accounts, gleicher Modellalias | 100 Requests je verschiedenem synthetischem Credential, 200 logische Requests, 200 Versuche, 400 Ereignisse, 1.800 gemeldete Tokens je Account |
| Replay und Neustart | Wiederholte und paginierte Abholung liefern dieselben Ereignisse; Source-/Event-/Request-/Versuchsidentitäten und Zahlen bleiben nach Core-Neustart unverändert |
| Chat Completions SSE und Toolfortsetzung | Streaminhalt, Toolname/Argumente, harmloses Toolergebnis und nachfolgende Antwort passieren den Core; gemeldete Usage ist im Journal |
| Striktes Accountpinning | Synthetischer 429 bleibt auf Account A, ohne Request an B; fehlende Usage bleibt unbekannt |
| Bewusster Pool | Ein logischer Request mit zwei Versuchen: A liefert 429, B Erfolg; beide Account-/Versuchsidentitäten sind verbunden |
| Journalfehler | Zweiter Writer abgewiesen; Dateigrößenlimit verhindert Registrierung mit HTTP 503 vor jedem Upstreamrequest; Teilappend wird beim Neustart unverändert abgewiesen; explizite Recovery des nachweislich unbenutzten Testjournals funktioniert |

Der Abbruch lieferte bereits ein Ereignis mit `outcome=canceled`; der erste
Test prüfte den Upstreamdisconnect zu früh. Eine korrigierte Wartebedingung ist
vorhanden, aber noch nicht als bestanden ausgewiesen. Das Fehlen von
Cache-/Reasoningwerten wurde im ersten Lauf nicht geprüft; W11 hat dabei ein
fälschliches Null-zu-Nullwert-Mapping gefunden. W1 bearbeitet dies. Der erste
Hash ist damit **kein freigegebener Backendstand**.

Fixture-Selbsttests bestanden separat: 200 direkte Requests an den Fakeanbieter,
SSE/Toolfortsetzung, deterministische 429 und beobachtbarer Clientdisconnect.
Diese Selbsttests beweisen ausschließlich die Testfixture.

## Noch auszuführen oder extern zu belegen

Die erweiterte Suite prüft zusätzlich Responses-SSE samt Toolübersetzung zum
Chat-Completions-Fake, unbekannte Tokenkomponenten, synthetischen Keywechsel und
Accountentfernung/Wiederanlage über Bindingrevisionen, SIGKILL während eines
Streams, Fehlkonfiguration und anschließende Recovery. Ihr Ergebnis auf dem
korrigierten exakten Corehash steht noch aus.

Echte Codex-/ChatGPT- und Command-Code-Proben samt eigenem Zugang, realer
Providerübersetzung und harmloser Toolfortsetzung stehen unabhängig davon aus.
Synthetischer Keywechsel belegt keine OAutherneuerung. Anzeigenamen und
Host-Accountlebenszyklus gehören nicht zum Corefixturevertrag. Eine Prüfung von
Persistenz und transaktionaler Cursorbestätigung im SQLite-Consumer sowie der
Vorher-/Nachher-Nachweis zur Unverändertheit des bestehenden Diensts werden
separat integriert.

Ein hart beendeter Prozess kann bereits verbrauchte Anbieterusage nicht neu
erfinden: Ein dauerhaft gespeicherter `attempt_started` ohne terminales
Usageereignis ist eine erkennbare Lücke mit unbekannter Tokenmenge. Die erweiterte
Suite soll genau dies nachweisen. Die fachliche Tragbarkeit dieser gemessenen
Lücke benötigt die ausdrückliche Produktentscheidung; allein ihre Dokumentation
macht A nicht bestanden.
