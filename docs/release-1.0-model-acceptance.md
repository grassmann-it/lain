# Modellnachabnahme Release 1.0

Stand 26.10.2026: **vorbereitet, nicht ausgeführt**. Erst nach Installation des
neuen Gesamtstands durch den Nutzer. Das Update ist laut Nutzer zuletzt an
`quietDrain` gescheitert; eine neue Hostversion ist bisher nicht installiert.
Keine allgemeine Modell-/IDEfreigabe.
Ausgangs-HEAD `fef478f601f46b315939fa53f02caa9791086f1d`, umfangreiche parallele
Änderungen; HEAD allein identifiziert diese Source nicht. Historische Ergebnisse:
[erste Matrix](model-live-qualification-2026-10-26.md),
[Folgeprüfung](provider-tool-context-repair-2026-10-26.md).

## Verbindliche Einordnung der 59 Fälle

23 Aliase bleiben nach den dokumentierten Folgeprüfungen ungelöst. Sieben haben
Auto und die strukturierte Qualitätsprüfung bestanden. 29 weitere besitzen nur
den schwächeren historischen Forced-/Marker-Nachweis. Damit haben 36 verschiedene
Aliase mindestens einen Toolablauf bestanden; das bedeutet weder 36 aktuelle
Qualitätsfreigaben noch Freigabe des neu installierten Gesamtpakets. Für den neuen
Releasekandidaten ist noch keiner der 59 neu live abgenommen. Router sind keine
zusätzlichen Modelle, gleiche Namen verschiedener Provider bleiben getrennt.

Der Offlinehelfer `tests/live/release_1_0_model_plan.py` berechnet die drei disjunkten
Gruppen aus den beiden öffentlichen JSON-Berichten und druckt konkrete Befehle.
Er liest keine Konfiguration, Authdatei oder Stores, startet keine CLI und öffnet
keine Verbindung. Er implementiert keinen zweiten Liverunner. Sein älterer
Cohere-Hinweis auf den separaten Kandidatenrunner wird für die finale Abnahme
durch den direkten Cohere-Aufruf unten ersetzt; die 22 Nicht-Cohere-Aufrufe bleiben gültig.

```sh
python3 -B tests/live/release_1_0_model_plan.py --group open
python3 -B tests/live/release_1_0_model_plan.py --group auto_quality_passed
python3 -B tests/live/release_1_0_model_plan.py --group historical_only
```

Die gedruckten Executevorlagen enthalten absichtlich einen ungültigen
Hashplatzhalter. Pro Alias ein neues privates Ausgabeverzeichnis verwenden;
Vorbereitung und Prüfung eines einzelnen Falls, danach dessen Ausführung und
Bewertung, erst dann den nächsten Fall. Kein automatischer Retry über Executevorlagen.
`--tool-mode auto-v2` ist der tatsächliche Schalter; die ältere README-Schreibweise
`--payload-mode` wird vom aktuellen Parser nicht akzeptiert.

## Releasebindung vor dem ersten Request

1. Integrator hält die tatsächlich installierte Version und das freigegebene
   Paketmanifest mit SHA-256 fest: Host, CLI, Core, Codexbinary, Helper und relevante
   VS-Code-Erweiterung. Installations-/synthetischer Abnahmebeleg muss genau diese
   Bytes ausweisen. Kein historischer Kandidatenhash als aktueller Installationsbeleg.
2. Sourcebaum/Releasearchiv samt Diff, Dependencylocks und Source-Manifest einfrieren;
   Hashes der Liveharnesses, ihrer Abhängigkeiten und Toolfixture festhalten. Der
   Offlinehelfer druckt aktuelle Hashes der wesentlichen Quellen/Berichte. Diese
   Ausgabe mit dem freigegebenen Releasearchiv vergleichen, nicht blind übernehmen.
3. Öffentliche `lain status --json`, `lain models --json`, `lain profiles --json`
   sichern. Gateway bereit, aktive und angewandte Revision gleich, konkrete Route
   und bestehender Account eindeutig. Katalog nach regulärer Installation/Export
   binden. Kein Login, Apply, Routenersatz, Dienstneustart oder Credentiallesen
   durch die Modellabnahme. Lokales Inferenztoken erst im autorisierten Liverunner,
   ausschließlich über `lain auth token --audience codex`, nur im Speicher.
4. Frischen Plan mit dem gedruckten Prepareaufruf erstellen. Alias, Account,
   Reasoningeffort, Profilrevision, Kataloghash, sieben Tools, Fixturehash,
   `executionContract`, beide auto-Controls und maximal zwei Requests prüfen.
   Plan-SHA-256 in die Executevorlage einsetzen. `flock` und `--workers 1` verwenden.
5. Vor und nach jedem Fall installierte Artefakthashes/Version kontrollieren.
   Der bestehende Runner bindet Source, Payload, Profil/Revision, Inventory und
   Katalog, **prüft aber nicht selbst den Hash des laufenden Host/Coreprozesses**.
   Diese äußere Releasebindung ist daher Pflicht; Drift beendet die Abnahme.

Die aktuelle Installation wurde in diesem Auftrag nicht inspiziert. Neue Version,
Paketmanifest und deren Hashes bleiben bis zur Installation offen.

## Budget und Qualitätsentscheidung

Erste Welle: 22 offene Nicht-Cohere-Aliase, höchstens **44 Clientrequests** und
90.112 reservierte Ausgabetokens (44 × 2.048). Ein Alias pro Lauf, maximal zwei
Requests, sieben Top-Leveltools/sieben aufrufbare Funktionen, 22.638 Bytes
synthetische Projektregeln. Frühere Usage lag etwa bei 5.000–6.000 Eingabetokens
je Request; 6.000 ist **keine garantierte Obergrenze**. Vollständige Payloadbytes
und tatsächliche Usage protokollieren. Keine kostenpflichtige Ersatzroute.

Gezielte Installationsregression der sieben bereits stark geprüften Aliase:
maximal 14 Requests/28.672 reservierte Ausgabetokens, separat nach erster Welle.
Die 29 historischen Erfolgskandidaten benötigen für eine vollständige neue
Auto-/Qualitätsmatrix weitere maximal 58 Requests/118.784 Ausgabetokens. Die vollständige
Nachabnahme ist autorisiert; die Wellen dienen der Auswertung, nicht einer neuen Freigabe. Einschließlich
Cohere wären es höchstens 118 Clientrequests für alle 59 Fälle, ohne Kontextwellen.

Die vollständige Ausführung und nötige Harnessimplementierung sind autorisiert.
Es gilt die bestehende Subscription-/Free-/Routenpolitik: kein PAYGwechsel, keine
zusätzliche bezahlte Ersatzroute, kein Login oder Accountwechsel als Fallback.
Ein absoluter Geldrahmen ist nicht zusätzlich vorgeschrieben. Fehlendes hartes
Geldlimit blockiert nur, wenn ein solches tatsächlich verlangt wird. Unbekannte
Kosten/Usage ehrlich als unbekannt ausweisen, niemals als null. Request- und
Outputgrenzen sind keine harte Rechnungsgrenze; Gateway-/Providerinterne Retries
sind damit nicht ausgeschlossen. Nach jedem Fall Usage/Attempts prüfen; kein
unverändertes Wiederholen, auch bei 429/503/Timeout. Fachliche Modellfehler bleiben
als solche unverifiziert/fehlgeschlagen; kein Providerpatch zur Erzeugung eines
Grünergebnisses.

Bestanden verlangt beide vollständigen Streams, genau den erlaubten Fixture-Read,
eine finale strukturierte Antwort und **alle** Qualitätschecks: Mengenvalidierung
vor Subtotal, `ValueError`, positive ganze Zahlen einschließlich sehr großer
Werte akzeptieren; null Mengen, negative Zahlen, bool, Bruchzahlen, integrale
Floats, Strings, None und Container ablehnen. Der vorhandene beschränkte
AST-Interpreter bewertet ausschließlich den Prädikatausdruck; kein `eval`,
`exec`, Patchlauf oder Ausführen von Modellcode. Nur festes `/usr/bin/cat` auf
eigene Fixture. HTTP 200 und Runnerexit 0 allein bestehen nicht.

Transport, Toolvertrag, Fortsetzung, semantische Qualität, Verfügbarkeit,
Zugang/Kapazität und Releasebindung separat ausweisen. Unsupported AST oder
außerhalb der Fixture liegende Argumente sind unentschieden, kein bewiesener
Providerdefekt. Qualitätsfehler lösen weder Retry noch Modellwechsel aus.

Öffentliche unabhängige Zuordnung nach jedem Lauf:

```sh
lain attempts --conversation CONVERSATION_ID_AUS_PLAN --limit 100 --offset 0 --json
```

Conversation/Turn, Account, Provider, Alias, Profilroute, Response-ID soweit
vorhanden und Zeitfenster mit Report verbinden; vollständige gefilterte Seite
und Gesamtzahl prüfen, gegebenenfalls begrenzt paginieren. Clientcalls und
beobachtete Attempts getrennt zählen. Bei fehlender Abdeckung keine Aussage
über sämtliche Upstreamversuche. Report, Plan und redigierte Attempts hashen;
keine rohen Antworten, Tokens oder Providercredentials veröffentlichen.

## Cohere: finale Abnahme direkt am installierten Responsespfad

Der bestehende `realistic_models.py` unterstützt jetzt explizit
`--tool-mode cohere-auto-v2`. Der Vertrag gilt ausschließlich für Provider
`cohere`, den einzelnen exakten Alias `command-a-plus-05-2026` und Kontext K0.
Beide Requests verwenden `tool_choice:auto`, `parallel_tool_calls:true` und
**kein `max_output_tokens` oder anderes erfundenes Outputtokenfeld**. Die sieben
exakt erfassten nativen Funktionstools, der feste Fixture-Read und die
beschränkte fachliche Guardprüfung bleiben erhalten. Alle übrigen Modi behalten
ihre bisherigen Controls; insbesondere keine globale false→true-Umschreibung.

Nach erfolgreicher Installation und Bindung der tatsächlich laufenden Artefakte:

```sh
python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-cohere-direct --only-alias command-a-plus-05-2026 --tool-mode cohere-auto-v2 --context-mode k0
flock /home/tomka/.cache/lain-verification-lane.lock python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-cohere-direct --execute --expect-plan-sha256 GEPRUEFTER_COHERE_DIREKT_PLAN_SHA256 --workers 1
```

Vorbereitung sendet keine Inferenz. Ausführung sendet höchstens **zwei Requests**
auf den vorhandenen Gatewayendpunkt `/v1/responses`, mit dem bestehenden Account
und eigener Conversation-/Turnkorrelation. Kein eigener Core, kein Forwarder,
keine Chatdirektabkürzung, kein Routenersatz und kein Retry. Der erste Fehler
verhindert die Fortsetzung. Unbekannte Usage bleibt unbekannt.

Der Plan deklariert `maximumOutputTokens:null`, der Request enthält das Feld
überhaupt nicht. `executionContract.requestControls` bindet auto/parallel,
K0/Provider/Alias, absichtlich unbekanntes Outputbudget und vorhandene
Responsegrenzen: maximal 4.194.304 gelesene Responsebytes pro Request,
Eventgrenze 20.000 mit der bestehenden Streamingprüfung, 90 Sekunden
Streamdeadline und 45 Sekunden Sockettimeout. Diese sind Clientgrenzen, **kein
providerseitiges Outputtokenbudget oder hartes Geldlimit**. Ein Clientabbruch
beweist nicht, dass upstream sofort keine weitere Inferenz/Abrechnung stattfindet.
Das vorhandene autorisierte Abo bleibt nutzbar; kein PAYGwechsel oder zusätzlicher
bezahlter Fallback.

Sourcehash, eingefrorener Plan, erster Request und statische Fortsetzung werden
wie bei den übrigen Fällen verifiziert; tatsächlich gesendete Fortsetzungsbytes
werden zusätzlich gehasht. Falscher Provider/Alias, mehrere Aliase, K1/K2,
abweichende Tools, alter Source-/Legacyplan, veränderte Controls/Hashes oder ein
nachträglich behauptetes Outputtokenbudget werden vor Auth/Inferenz abgewiesen.
Die bekannte fachliche Schwäche bei null/negativen Mengen bleibt eine offene
Modellqualitätsfrage, bis eine neue tatsächliche Antwort alle Guardfälle besteht.
HTTP 200 allein besteht nicht; kein Providerpatch zur Korrektur eines Modellfehlers.

`cohere_candidate.py` bleibt historischer Diagnosebeleg des isolierten Cores.
Er ersetzt die finale direkte Abnahme nicht und wird nicht zusätzlich automatisch
ausgeführt. Die fehlende Installation und die tatsächliche Liveabnahme bleiben
offen; dieser Implementierungsauftrag hat weder Host noch Provider aufgerufen.

## Realistische Kontextstufen und Tools

| Stufe | Zweck und festgelegter Umfang | Status/Budget |
| --- | --- | --- |
| K0 kompakt | Vorhandene Rechnungsfixture, 22.638 Regelbytes, 7 Funktionen, auto auf beiden Schritten | Konkrete vorhandene Aufrufe oben; kein Langkontextbeleg |
| K1 Projektkontext | `--context-mode k1-32k`: exakt 128.000 generierte ASCII-/UTF-8-Bytes, 32.000 Inputtokens als beobachtetes Ziel; Regeln an Anfang/Mitte/Ende, vierte Evidenz nur in Datei | Z.ai glm-5.3 und Kimi k3-256k je maximal 2 Requests/4.096 reservierte Outputtokens, zusammen 4/8.192 |
| K2 gemeldete Größenordnung | `--context-mode k2-110k`: exakt 440.000 generierte Bytes, 110.000 Inputtokens als beobachtetes Ziel; gleiche Aufgabe und Regeln | Zunächst Z.ai glm-5.3, maximal 2 Requests/4.096 reservierte Outputtokens |
| T tatsächliche Clientoberfläche | Redigierter exakter Tool-/Skill-/Regelcapture der neuen CLI bzw. VS Code; native/inherit getrennt | Offline zählen und hashen; bisher kein vollständiger tatsächlicher Clientpayload live qualifiziert |

K1/K2 laufen im **bestehenden** `realistic_models.py`, mit genau einem konkreten
Alias pro Plan, sieben nativen Funktionen, auto auf beiden Requests und
`max_output_tokens:2048`. Die Fixture wird deterministisch erzeugt: nummerierte
synthetische Ledgerreviews zu Mengen, Preisen, Rundung, Rabatten, Revisionen,
Posting und Refunds. Drei relevante Regelkennungen stehen am Anfang, genau in
der Mitte und am Ende; die Dateikennung steht ausschließlich als Kommentar in
der gelesenen `src/invoice.py`. Die finale JSON-Antwort muss zusätzlich
`context_evidence` mit exakt `beginning`, `middle`, `end`, `file` liefern. Die
bestehende semantische Mengenprüfung bleibt unverändert erforderlich. Das ist
ein langer synthetischer Projektkontext plus Toolfortsetzung, kein vollständiger
nativer Gesprächsverlauf oder Nachweis beliebiger Codekorrektheit.

Feste Obergrenzen (serialisierte Requestbytes enthalten Tools, Instructions,
Input, JSON-Escaping und alle übrigen Bodyfelder):

| Grenze in UTF-8-Bytes | K1 | K2 |
| --- | ---: | ---: |
| Generierter Kontext exakt | 128.000 | 440.000 |
| Kontext maximal | 196.608 | 786.432 |
| Erster vollständiger Request maximal | 196.608 | 786.432 |
| Zweiter vollständiger Request maximal | 294.912 | 884.736 |
| Replay der ersten Outputitems maximal | 65.536 | 65.536 |

Überschreitungen werden ausdrücklich abgewiesen (`context_request_byte_limit`,
`context_replay_byte_limit`), niemals gekürzt. Response/SSE-Grenzen des bestehenden
Harness bleiben bestehen (4 MiB, 20.000 Events; Sockettimeout 45 Sekunden).
Keine automatische Wiederholung, Payloadvergrößerung oder Grenzsuche.

### Routebezogene Fenster-/Outputbelege

`--context-capacity /ABSOLUT/capacity.json` ist ein kleines, maximal 16.384 Bytes
großes **öffentliches Metadatenartefakt**, keine Authdatei. Format exakt:

```json
{
  "alias": "glm-5.3",
  "provider": "zai",
  "accountId": "ACCOUNT_ID_AUS_OEFFENTLICHER_ROUTE",
  "contextWindowTokens": null,
  "maximumOutputTokens": null,
  "source": "https://docs.z.ai/",
  "checkedAt": "2026-10-26T00:00:00Z"
}
```

Das Beispiel weist die Grenzen ausdrücklich als unbekannt aus und belegt keine
konkrete Z.ai-Kapazität. Der Integrator übernimmt bekannte Werte ausschließlich
aus geprüften **passenden Provider-/Modell-/Abo-/Transportquellen**, beispielsweise
dem öffentlichen Providerkatalog mit dessen überprüften Modellquellen. Keine
Katalog-Truncationpolicy, Aliasendung oder früher erfolgreiche Tokengröße als
Fensterbeleg verwenden. `source`/`checkedAt` und Identität müssen geprüft werden;
der Harness lädt die URL nicht und authentifiziert diese Angaben nicht selbst.
Unbekannte Werte bleiben `null`, statt ein Fenster zu erfinden. Alias, Provider
und Account müssen exakt zum bestehenden ausgewählten Eintrag passen.

Der vollständige Beleg wird im Source-/Planvertrag eingefroren. Bekannte maximale
Ausgabe unter 2.048 oder Zielinput plus 2.048 Reserve über bekanntem Kontextfenster
werden **vor Auth/Inferenz** abgewiesen. Das ist eine Ziel-/Reserveprüfung, keine
Tokenzählung des Payloads. `capacityStatus:target_fits_not_tokenized` sagt genau
das; `unknown` weist ein unbekanntes Fenster aus und sperrt den autorisierten
bounded Probe nicht künstlich. Die tatsächliche erste Tokenzahl ist ohne passenden
Providertokenizer vorab unbekannt. Eine Upstream-Kontextablehnung wird als Fehler
bewahrt, nicht durch Kürzung oder Ausweichroute umgangen. Wenn Usage ein bekanntes
Fenster einschließlich 2.048 Reserve überschreitet, folgt
`context_window_exceeded`; nach dem ersten Request wird dann keine Fortsetzung
mehr gesendet. Die Fortsetzung wird zusätzlich an ihrer Byteobergrenze geprüft;
ihr exakter Tokeneingang wird erst aus ihrer eigenen Usage bekannt. Es gibt keine
Behauptung einer exakten Vorab-Fitgarantie.

32k/110k sind **keine Tokenlimits oder Byteumrechnungen**. `contextObservation`
enthält die beobachteten Inputtokens beider Schritte und `targetReached` aus dem
ersten Request. Bei korrekter Antwort, aber zu kleinem beobachtetem Input heißt
der Zustand `context_target_not_reached`; bei fehlender Usage
`context_usage_unknown`. Beide sind keine erfolgreiche Zielkontextabnahme. Kein
unverändertes Wiederholen, um diese Zustände grün zu bekommen. Historische fremde
Z.ai-Recherchen mit 110k–138k Inputtokens sind Anlass, kein eigener Qualitätsbeleg.

### Konkrete Aufrufe nach Installation

Öffentliche Kapazitätsdateien je ausgewählter Route zuerst prüfen; keine
Providercredentials lesen. Für K1 Z.ai, K1 Kimi und K2 Z.ai jeweils ein neues
Ausgabeverzeichnis und den eigenen exakt gedruckten Planhash verwenden:

```sh
python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-k1-zai --only-alias glm-5.3 --tool-mode auto-v2 --context-mode k1-32k --context-capacity /ABSOLUT/zai-capacity.json
python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-k1-kimi --only-alias k3-256k --tool-mode auto-v2 --context-mode k1-32k --context-capacity /ABSOLUT/kimi-capacity.json
python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-k2-zai --only-alias glm-5.3 --tool-mode auto-v2 --context-mode k2-110k --context-capacity /ABSOLUT/zai-capacity.json
flock /home/tomka/.cache/lain-verification-lane.lock python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-k1-zai --execute --expect-plan-sha256 GEPRUEFTER_K1_ZAI_PLAN_SHA256 --workers 1
flock /home/tomka/.cache/lain-verification-lane.lock python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-k1-kimi --execute --expect-plan-sha256 GEPRUEFTER_K1_KIMI_PLAN_SHA256 --workers 1
flock /home/tomka/.cache/lain-verification-lane.lock python3 -B tests/live/realistic_models.py --output /home/tomka/.cache/lain-release-1.0-k2-zai --execute --expect-plan-sha256 GEPRUEFTER_K2_ZAI_PLAN_SHA256 --workers 1
```

Vor Ausführung `generated-context.txt`, generierte Datei und `executionContract.context`
prüfen. Eingaben nicht von Hand ändern: der Runner vergleicht beide Dateien vor
CLI/Auth/Inferenz bytegenau mit dem deterministischen Generator. Harnesssource,
Tools, Plan, erster Payload und statische Fortsetzung sind gehasht; konkrete
Fortsetzungsbytes werden beim Senden zusätzlich protokolliert. Änderungen erfordern
einen neuen Plan; alte Belege bleiben unverändert. Kein Modelltext wird als
Shelltext oder Pythoncode ausgeführt.

Die drei Kontextfälle ergänzen höchstens **6 Requests und 12.288 reservierte
Ausgabetokens**. Mit der vollständigen 59er-Matrix sind das maximal **124
Clientrequests**. Für Cohere bleibt das Ausgabetokenbudget ausgelassen und daher
unbekannt; es gehört nicht in eine angeblich harte Gesamttokengrenze.

Toolzahlen immer als drei Felder melden: **Top-Leveleinträge**, **aufrufbare
Definitionen inklusive Namespacemitgliedern**, **serialisierte UTF-8-Toolbytes**.
Namespaces rekursiv zählen, Funktionen nicht doppelt zählen; Customtools separat
als Typ ausweisen. Historischer Capture: inherit 11/170/118.760 Bytes, native
7/7/7.562 Bytes. 160 MCP-Mitglieder in einem Namespace sind keine 160
Top-Leveltools. Bytes belegen keine Tokenzahl. Beam 128/129 bezieht sich auf
Top-Leveldefinitionen einer konkreten Route; kein allgemeines Codexlimit und
kein Namespacebeleg. Historische synthetische Beamgrenze nicht erneut abfragen;
`model_boundaries.py` würde zusätzlich alte Kimi-/Coherefälle ausführen und ist
für diese disjunkte Nachabnahme deshalb kein pauschaler Folgeaufruf.

## Offlineprüfung dieser Ergänzung

41 Checks in `test_realistic_models.py` und zwei Checks in
`test_cohere_candidate.py` bestanden. Darunter beide vollständig gemockten
K1/K2-Abläufe von generierter Vorbereitung bis zum Report, Sentinel-/Mengenfehler,
Source-/Payload-/Kapazitätsdrift, manuell veränderte Eingaben, Request-/Replaybytes,
bekannte Fenster-/Outputüberschreitungen und fehlende/zu kleine Usage. Zusätzlich
der gemockte direkte Cohere-Responsesablauf mit echten Request-/SSE-Harnesspfaden,
beiden Controls, unbekanntem Outputbudget, falscher fachlicher Antwort,
Provider-/Alias-/Kontext-/Toolgrenzen, Plan-/Payloaddrift, Erstfehlerstopp und
Responsebyte-/Event-/Zeitabbruch. CLI-Help
geprüft. Keine Liveanfrage, Providercredential, Produktionsänderung oder schwere
Build-/GUI-Lane; die tatsächliche Providerabnahme bleibt auszuführen.

```sh
python3 -B -m unittest discover -s tests/live -p 'test_realistic_models.py' -q
python3 -B -m unittest discover -s tests/live -p 'test_cohere_candidate.py' -q
```

## Vollständige disjunkte Bestandsliste

`auto_quality_passed` = stärkerer historischer Nachweis, `historical_only` = alter
Forced-/Markerpass, `open` = bisher ungelöst. Jede Zeile erfordert neue Bindung,
bevor sie als für den Release abgenommen gelten darf.

| Provider | Alias | Stand | Historische Fehlerkategorie |
| --- | --- | --- | --- |
| codex | `gpt-6-astra` | historical_only | — |
| codex | `gpt-6.1-sol` | historical_only | — |
| kimi | `k3` | auto_quality_passed | tool_schema |
| kimi | `k3-256k` | auto_quality_passed | tool_schema |
| kimi | `kimi-for-coding` | auto_quality_passed | tool_schema |
| kimi | `kimi-for-coding-highspeed` | auto_quality_passed | tool_schema |
| zai | `glm-5.3` | auto_quality_passed | tool_arguments_outside_fixture |
| zai | `glm-5.3-flash` | auto_quality_passed | — |
| xiaomi-mimo | `mimo-v2.6-pro` | historical_only | — |
| xiaomi-mimo | `mimo-v2.6-flash` | historical_only | — |
| commandcode | `cmd-gpt-6-sol` | historical_only | — |
| commandcode | `cmd-gpt-6-luna` | historical_only | — |
| commandcode | `cmd-gpt-5.6-sol` | historical_only | — |
| commandcode | `cmd-deepseek-v4-pro` | open | tool_schema |
| commandcode | `cmd-deepseek-v4.1-flash` | historical_only | — |
| commandcode | `cmd-kimi-k3` | historical_only | — |
| commandcode | `cmd-glm-5.3` | historical_only | — |
| commandcode | `cmd-minimax-m3` | historical_only | — |
| commandcode | `cmd-mimo-v2.6-pro` | historical_only | — |
| commandcode | `cmd-mimo-v2.6-pro-ultraspeed` | historical_only | — |
| commandcode | `cmd-step-5-preview` | historical_only | — |
| commandcode | `cmd-gemini-4-argon` | historical_only | — |
| commandcode | `cmd-nemotron-3-ultra-550b-a55b` | open | unexpected_continuation_tool |
| commandcode | `cmd-nemotron-3.5-super-vl` | historical_only | — |
| commandcode | `cmd-inkling` | open | upstream_rejected |
| commandcode | `cmd-inkling-small` | open | upstream_rejected |
| commandcode | `cmd-muse-spark-1.3` | open | tool_schema |
| commandcode | `cmd-grok-4.7` | historical_only | — |
| commandcode | `cmd-mistral-large-4` | historical_only | — |
| commandcode | `cmd-fugu-ultra` | open | authentication |
| commandcode | `cmd-muse-wonder-1.0` | open | tool_schema |
| commandcode | `cmd-beam-free` | auto_quality_passed | — |
| commandcode | `cmd-laguna-s-2.1-free` | open | account_or_capacity |
| commandcode | `cmd-glyph-cluster-free` | open | upstream_rejected |
| commandcode | `cmd-claude-sonnet-5-5` | open | tool_schema |
| commandcode | `cmd-claude-haiku-5-5` | historical_only | — |
| commandcode | `cmd-claude-opus-5-5` | open | tool_schema |
| commandcode | `cmd-claude-fable-5-1` | open | tool_schema |
| commandcode | `cmd-qwen3.8-max-0902` | historical_only | — |
| commandcode | `cmd-ling-3.1-flash` | historical_only | — |
| codex | `gpt-6-luna` | historical_only | — |
| codex | `gpt-5.5` | open | model_unavailable |
| codex | `gpt-5.6-terra` | historical_only | — |
| codex | `gpt-5.6-luna` | historical_only | — |
| codex | `gpt-6-sol` | historical_only | — |
| codex | `gpt-5.6-sol` | historical_only | — |
| kilo | `google/gemma-4-26b-a4b-it:free` | open | upstream_rejected |
| nvidia-build | `nvidia-step-5-preview` | open | transport_or_timeout |
| codex | `codex-auto-review` | historical_only | — |
| cohere | `command-a-plus-05-2026` | open | upstream_rejected |
| upstage | `solar-mini4-260922` | historical_only | — |
| cloudflare-workers-ai | `@cf/google/gemma-4-26b-a4b-it` | open | upstream_rejected |
| cloudflare-workers-ai | `@cf/zai-org/glm-4.7-flash` | open | tool_arguments_outside_fixture |
| sambanova | `Meta-Llama-3.3-70B-Instruct` | open | upstream_rejected |
| groq | `groq-gpt-oss-20b` | open | upstream_rejected |
| gemini-api | `gemini-3.5-flash-lite` | open | upstream_rejected |
| openrouter | `openrouter-llama-3.3-free` | open | upstream_rejected |
| openrouter | `openrouter/free` | historical_only | — |
| kilo | `kilo-liquid-lfm-free` | open | account_or_capacity |
