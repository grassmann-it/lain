# N09 — unabhängige Abnahme

Stand der Vorbereitung: 25.10.2026, Ausgangscommit `7661886`.
Auftrag und Grenzen stammen aus [N01–N09](../next-work-packages-2026-10-25.md)
und dem [Ausführungsbericht](../next-work-packages-execution-2026-10-25.md).
Dieser Bericht enthält noch **keinen neuen GUI-, Paket-, Installations- oder
Providerpass**. Alte UXD-/Lieferbelege bleiben historische Nachweise ihrer Pins.

## Tatsächlicher Zustand

| Ebene | Zustand | Nachweis/Grenze |
| --- | --- | --- |
| Fokussierter Prüfkatalog | Vorbereitet | [n09-cases.json](../../tests/next-qualification/n09-cases.json): neun klar abgegrenzte Fälle, Verantwortliche, Fixtures und benötigte Evidenz |
| Privater Nativeoperator | Source vorbereitet | [run_native.py](../../tests/next-qualification/run_native.py), noch nicht mit GUI gestartet; verwendet bestehende Testassemblies |
| Kleine Pythonprüfungen | 5/5 bestanden | `python3 -B -m unittest discover -s tests/next-qualification -p test_runner.py -v`; nur Vorbereitung, Env-Allowlist, Pins, Symlinkablehnung und Plan-/Livegrenzen |
| N01-Fall | Owner-Source geliefert | `54ea3b8`, `RunN01DisplaySmoke`; Prüfung im integrierten Build und native Matrix offen |
| N05/N06-Fälle | Owner-Source in Arbeit | `RunN05N06ProfilesCases`, getrennte Fixturehandler; Programanschluss abgestimmt, kein N09-Lauf |
| Physische Models-/Fenster-/Modusabnahme | Offen | Bestehende NativeInput-/UXD-/Editoroperatoren sind Ausgangspunkt; Reflexions- und routed-Click-Prüfungen ersetzen keine physische Eingabe |
| Visuelle Prüfung | Nicht gestartet | Jedes neue PNG benötigt eigene Sichtung; Capture oder Exitcode 0 ist kein Layoutpass |
| Exaktes ausgewähltes Paket/Installation | Offen | Erst nach Sourceintegration, Componentpins und Root-Slot; keine Übertragung alter Binaryergebnisse |
| Reale zwei Gesprächsschritte/Provider | Nicht gestartet | Root führt die ausdrücklich autorisierte kleine Probe aus; N09 erzeugt keine Keys, Logins, Inferenzlast oder Providerreads |

`git diff --check` ohne Befund. Weder Build noch GUI, Host, Dienst, Browser oder
Provider gestartet. Bestehendes Benutzerdesktop, Legacyprojekt und normale
Codexkonfiguration wurden nicht gelesen oder verändert.

## Begrenzter Katalog und Wiederverwendung

| Fall | Nächste konkrete Prüfung | Wiederverwendeter Bestand |
| --- | --- | --- |
| Q01 | Quota-Timeout/403 ohne Snapshot sichtbar; vorhandene alte Messung und eindeutige kurze Namen bleiben erhalten | N01-Ownerfall, Desktop `Fixture`, `QuotaHierarchyCases` |
| Q02 | Accounts/Quota in erlaubter Größen-/Modusmatrix, echte Tastatur/Fokus, lange Namen und erreichbare Monitor-Details | `Uxd18MonitorCases`, `QuotaHierarchyCases`, XTEST `NativeInput` |
| M01 | Tatsächliche Modellposition, stable-ID-Bindung bei Sortierung/Reorder, Revision und benannte Referenzlocks | `Uxd19ModelsCases`, native Modellmenü-/Draftflüsse; gezielte physische Ergänzung noch offen |
| P01 | Prepare ohne Dateien, Cancel/Ziel-/Profilwechsel, 409/Draft, exakte vorbereitete Generation, Dispatchergrenze | N05-Ownerfall, vorhandener native Setup-/Browsepfad |
| P02 | Gruppen in Profiles, gespeicherte Gruppen-/Mitgliedreihenfolge, expliziter Accountrouter, Konflikte und ehrliches Free | N06-Ownerfall, Models-/Clientverträge, vorhandener Mixed-Paket-Harness |
| C01 | Gleicher Snapshot in schmaler/breiter TTY, Pipe und NO_COLOR; JSON/full unverändert | Bestehende gezielte CLI-Format-/Quotafixtures; Ownernachweis noch ausstehend |
| I01 | Exakte gewählte Paketkomponenten und nativer `/model`-Pfad mit erzeugter Reihenfolge | `host_model_groups.py`, `native_model_picker.py`, `router_sdk_probe.py`; keine neue Testarchitektur |
| R01 | Zwei kleine echte Autorouterturns, Ausgabe/Inspector/Account/Route/Persistenz, UI-close erhält Host | Root-Lane, separat von synthetischem Erfolg |
| R02 | Bewusst autorisierte OpenRouter-/MiMo-/Cloudflaremethoden mit Identität und Read oder belegter Grenze | Vorhandene Methodenoperatoren; Nutzerinteraktion/Rechte bleiben echte offene Gates |

Die verlangte Matrix bleibt: Gateway 1120×780 und 800×560; Monitor zusätzlich
640×420; jeweils 100/125 %. Barebones, Simple und Advanced verwenden die
**bestehende** Navigation. Profiles bleibt direkt erreichbar, Simple hat Live
direkt, der einzelne Modusdropdown bleibt unten rechts. Gleichartige Menüs sind
kein Defekt. Normal ist der fokussierte Ausgangspunkt; Compact wird gezielt dort
nachgeprüft, wo Änderungen seine Geometrie berühren, ohne UXD-17–22 neu aufzubauen.
Die tatsächlich abgedeckten Kombinationen müssen aus Viewport/RenderScaling-
Evidenz kommen, nicht aus angeforderten Parametern.

## Operator für den späteren Root-Slot

Die bisherigen Shellrunner bleiben unverändert. Sie setzen `HOME` auf einen
Tempordner und nutzen teilweise feste `/tmp`-Pfade. N09 startet sie deshalb nicht
unverändert. Der neue kleine Operator übernimmt deren Xvfb-`-displayfd`,
Xauthority und eigenen D-Bus-Prozess, bindet jedoch ein synthetisches Verzeichnis
in eigener bwrap-PID-/Netz-Namespace am **unveränderten HOME-Pfad** ein. Es gibt
keine HOME-/CODEX_HOME-Zuweisung, keinen Nutzerdisplay-/Busfallback und keinen
Build. Runtime/TMPDIR/XDG/LAIN_HOME liegen im neuen privaten Cache-Evidenceordner.
Nur die eigenen gestarteten Kinder werden beendet.

Nach separat freigegebenem, gepinntem Build; erst ohne `--run` vorbereiten oder
im freigegebenen Runtime-Slot mit `--run` ausführen:

```sh
python3 tests/next-qualification/run_native.py \
  --assembly /absolute/approved/Lain.Desktop.Smoke.dll \
  --source-commit FULL_SOURCE_COMMIT \
  --output "$HOME/.cache/lain-qualification-20261025/next/n01-100" \
  --scale 1 --argument=--n01-display-smoke --run
```

Für 125 % einen **neuen** Ausgabeordner und `--scale 1.25` verwenden. N05/N06
verwendet dieselbe Desktopassembly mit `--argument=--n05n06-profiles-smoke`, sobald
der Ownerfall integriert ist. Die vorhandene UXD-Assembly kann gezielt mit
`--uxd-filter native-state-model-menu,native-state-draft` gestartet werden;
das ist zunächst Menü-/Draftabdeckung, **kein** vollständiger Reorderpass. Die
vorhandene Editorassembly erlaubt `--native-editor-filter models` bzw. `profiles`.
Keiner dieser Aufrufe ist hier bereits ausgeführt.

`plan.json` hält Sourcepin, vollständige Artefaktinventarhashes, Argumente und
angeforderte Skalierung fest. `run-result.json`/`summary.json` trennen nur
Vorbereitung, abgeschlossene Ausführung und Fehler; sie setzen bewusst keinen
automatischen Visual-/Accessibilitypass. Nativeberichte, Logs und Fehlerbilder
bleiben erhalten. Ein früherer fehlgeschlagener Lauf wird nicht überschrieben.

## Unabhängige Reviewgrenzen

Je integrierten Workercommit werden zunächst konkrete Quellenänderung,
Fixtureannahmen und Scope geprüft. N01: keine Namensmigration, keine erfundene
Reconnect-/Planursache. N02–N04: Null/Unbekannt, Freshness, Accountbindung und
bereinigte Diagnose; keine synthetische Antwort als echter Read. N05/N06:
Prepare/Install/Apply auseinanderhalten, Konfliktdrafts und Stable IDs,
globales Reorder nur vollständige Permutation, Account-/Pool-/Freegrenzen.
N07/N08: exakte Komponenten-/Originalpins, fremde Launcher erhalten, klare
Rücknahme und ausschließlich opt-in lain-Client. Sourcefreigabe, lokal geprüft,
Paket geprüft, installiert und real geprüft werden getrennt nachgetragen.

Noch keine dieser vorbereiteten Reviewlisten bedeutet, dass ein späterer
Workercommit unabhängig abgenommen wurde. Konkrete Findings gehen mit Quelle
und Reproduktion an den jeweiligen Owner; kein pauschaler neuer Layoutauftrag.

## Focused physical state-flow additions

`tests/uxd-acceptance/N09StateFlows.cs` registers these filters with the existing
UXD `Open`/`Case`/fixture/native-input mechanism (1120×780 and 800×560, Simple):

- `native-n09-model-identity`: actual row geometry after reversed Host inventory
  and native rename; exact ID/revision request; named removal locks; delayed
  snapshot while a real menu remains open and a stale native action; HTTP409
  draft retention; removed-ID Save with a same-alias replacement.
- `native-n09-prepared-cancel-target`: native Prepare, Change, shared/private
  target selection and parent A/B reprepare, then Tab/Shift+Tab/Enter Cancel.
  Both target trees remain empty and an unrelated Models draft survives. Shared
  target selection is checked without Prepare, so no dispatcher check is invoked.
- `native-n09-prepared-review`: regression for Shell Review latest from the
  prepared view. The original setup inputs are then absent from the logical
  subtree; the initial N05 implementation loses them during generic snapshot
  capture/rebuild. This case asserts preserved inputs and no implicit export,
  installation or Apply. Product fix and native execution remain pending.

These are source additions, **not executed passes**. Existing `NativePress`
uses BringIntoView only to position a target; actions and text use XTEST. Native
scrolling, the complete Q02 viewport/mode matrix, provider gates and any images
not individually reviewed remain outside these claims. Run the assembly with
`--uxd-filter=native-n09-` through the N09 runner after Root allocates a slot.
