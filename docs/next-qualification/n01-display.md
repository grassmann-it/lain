# N01 Account-/Quota-Darstellung

Basis: `7661886`, Implementierung im isolierten N01-Worktree am 25.10.2026.

## Source bereit

- Accountlokale Refreshfehler erscheinen zusätzlich zum bisherigen Status als
  umbrochener Grund (höchstens drei Zeilen) in Kachel, Monitor und Inspector.
  Der vollständige Grund ist in den scrollbaren Inspectordetails selektierbar.
  Bestehende Messwerte, deren Alter und Controls bleiben während Fehlern erhalten.
- Ein bloßer Refreshfehler begründet keine neue Anmeldung oder Einrichtung.
  Explizites `setup-required`, Refresh, Console und manuelles Monitoring bleiben
  über bestehende Aktionen verfügbar. Keine Deutung eines generischen 403 als
  Abopflicht; keine eigene Providerdiagnose.
- `AccountDisplayNames` übernimmt die bestehenden kompakten CLI-Regeln gemeinsam
  für CLI/Desktop. Vollständige Inventarreihenfolge vor Filtern/Sortieren, eigene
  Namen und die Unterscheidung mehrerer Accounts bleiben erhalten. Zusätzliche
  Kollisionen eigener mit erzeugten Labels erhalten lesbare Anzeigesuffixe.
  Der gespeicherte Name steht sichtbar im Inspector. Keine Namensmigration,
  kein persistentes ShortNamefeld; `ProviderDisplayNames`/`AccountNameSuggestions`
  bleiben unverändert. Filter finden Anzeige, Originalnamen und stabile IDs.
- Integrationsanschluss N02: `QuotaDisplayFacts.IsElapsedFreeDay` verhindert nach
  Ablauf des expliziten accountweiten OpenRouter-Free-Tagesfensters eine erneute
  Prozent-/Restableitung aus historischen Used/Limit-Fakten. Desktop unterdrückt
  auch den alten Cache-Rest nach Zeitablauf; CLI zeigt im kompakten Modus dann
  `Remaining unknown`. Backendprojektion der Rohdaten liegt beim Integrator.

## Prüfungen

Ausgeführt: `git diff --check` ohne Befund. Source statisch gegen bestehende DTOs,
Refresh-Lifecycle, CLI-Regeln und Inspector-/Monitoranschlüsse geprüft.

Vorbereitet, **noch nicht ausgeführt** (serielle Build-Lane beim Integrator):

- `tests/client-cli`: bestehende Präsentations-/ANSI-/JSONprüfungen plus neue
  `AccountDisplayNameChecks` (Defaults, eigene Namen, Mehraccount, zusätzliche
  Kollision, Importmail, Reimport-Generation, genaue Free-Day-Grenze und Scope).
  `UsageStyleChecks` enthält zusätzlich einen gerenderten Compact-/Full-Fall:
  historische 10/50 Requests nach Tagesende ergeben keinen Rest/Prozentwert.
- `tests/desktop/N01DisplayCases.cs`: `RunN01DisplaySmoke` für kurze eindeutige
  Labels, ID-Filter, Timeout und 403 ohne Snapshot, vollständigen Inspectorgrund,
  keine implizite Setup-/Reconnectanforderung, erhaltene 37%-Controls und Alter,
  sowie die exakten Refresh-Account-IDs. Nur In-Memory-HTTPfixtures.
  Entrypoint `--n01-display-smoke` wird vom N09-Owner in `Program.cs` verbunden.
  Evidence erfasst Viewport, RenderScaling, Informationslevel und Betriebsmodus;
  visuelle native Bewertung liegt separat bei N09.

Kein Build/Restore/dotnet, GUI, Providerread, Dienstzugriff, Credential-/Bestands-
änderung oder Push aus diesem Worktree. Kein Runtime-/Visual-Pass behauptet.

## Integrationskorrektur

Der integrierte N05-Smoke fand CS0120 im bisherigen statischen `BudgetIdentity`:
Die gemeinsame Anzeige benötigt seit N01 das aktuelle vollständige Accountinventar.
`BudgetIdentity` ist deshalb ebenfalls eine Instanzmethode. Alle direkten sowie
Methodengruppen-Verwendungen von `AccountDisplayName` und `BudgetIdentity` wurden
statisch geprüft; alle liegen jetzt in Instanzmethoden. Kein zusätzlicher Build
aus diesem Worktree (Slot verbleibt bei N05). `git diff --check` ohne Befund.

## N09-Fix: Monitor bei 640×420

Basis `b74a398`, Source-only-Nachtrag nach unabhängigem Datei-/Screenshotreview.
Bei tatsächlichen 640×420 blieben mit dauerhafter 340px-Inventarspalte nur etwa
48px Inventarhöhe: der lange Detailbutton wurde abgeschnitten, drei native
Q02-Zellen scheiterten vor dem Öffnen des Inspectors.

Unter 800px belegen Accounts jetzt jeweils die volle Breite: ohne Auswahl das
Inventar, mit Auswahl der Inspector. Dadurch kann die Inventarüberschrift
wieder einzeilig neben den Aktionen liegen; lange Namen erhalten mehr Breite.
Die vorhandenen Controls bleiben montiert. Close/Escape öffnet das Inventar
mit gespeicherter Scrollposition und ursprünglichem Triggerfokus wieder.
Beim Verkleinern wird Fokus aus dem dann unsichtbaren Inventar nach Close
übergeben. Versteckte Scrollereignisse überschreiben die gespeicherte Position
nicht; Navigation/Präsentationswechsel behalten den gespeicherten Offset.
Ab 800px bleibt 340px/Rest, ab 1000px */400px unverändert nebeneinander.

Keine Änderungen an Quota-Fakten, Reihenfolge, Backend, zentralen Assertions oder
Fixturebedingungen. `git diff --check` bestanden; Build/Runtime hier ausdrücklich
nicht gestartet. Erneut zu prüfen: die drei 640-Zellen plus die zwölf übrigen
Matrixzellen je Skala, insbesondere Close/Escape, Fokus und Scrollrückkehr.
