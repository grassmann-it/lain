# UXD-19 – Modellaktionen, Verwendungen und stabile Identität

Ergebnis: Menü, Editor und Entfernen betreffen immer den gemeinten Modellzugang.
Filterleere und verschwundene Auswahl sind verständlich. Ergänzt UXD-04;
[gemeinsame Grundlage](16-state-continuity.md).

Owner: UXD-04, exklusiv `src/Lain.Desktop/MainWindow.Routes.cs` einschließlich
gemeinsamer Route-/Policyhelpers. Shellcommands über UXD-01, zentrale Tests UXD-09,
Client-/Hostreferenzvertrag UXD-00 und bestehende Fachowner.
Voraussetzung: UXD-01/04 und vorhandene revisionsgebundene Route-/Profilverträge.

## Bestandsabgleich und Aufgaben

1. Vorhandene Modelcommands, Detailansicht bei fehlender ID, Referenzsperre und
   Editor-Rebuild weiterverwenden. Delta vor allem bei Menü-/Pollingrennen und
   erhaltenem Draft prüfen; keine zweite Entfernungspipeline bauen.
2. ⋯, Rechtsklick und Tastatur führen denselben Commandsatz aus. Stabile Route-ID
   beim Öffnen binden, Verfügbarkeit/Revision vor Ausführung erneut prüfen.
   Änderungen an Auswahl oder Reihenfolge hängen die Aktion nicht um. Alias,
   Upstreammodell und Account getrennt zeigen; Copy kopiert den bezeichneten Alias.
3. Enabled, Visible und Applied unterscheiden. Hidden blendet einen Menüeintrag
   aus und ist kein Zugriffsverbot. Gespeicherte Laufzeitänderung bleibt bis zum
   globalen Apply ausstehend; keine Probeinferenz zur Zustandsbestimmung starten.
4. Entfernen nennt tatsächliche Profilmitgliedschaften und Fallbackverweise,
   sperrt bei vorhandenen Referenzen und lässt den Host erneut validieren.
   Defaultwechsel allein beseitigt keine Mitgliedschaft. Kein Quick detach,
   Force remove oder Entfernen historischer Usage.
5. Bei keinem Filtertreffer Query und Clear filters zeigen. Bei verschwundener
   ausgewählter ID kein Ersatz nach Alias/Zeile: sichere Inventarrückkehr,
   keine Edit-/Saveaktion auf Ersatzroute. Vorhandenen Draft zur Prüfung behalten,
   ohne die gelöschte Route still neu anzulegen.

## Geplante Abnahme und Lieferung

- Menü für `gpt-lain-01` öffnen, dann Auswahl, Reihenfolge und Konfiguration
  ändern: Aktion bleibt an dieselbe ID gebunden oder ist begründet nicht mehr
  ausführbar. Alle drei Menüzugänge und Escape/Fokusrückgabe nativ prüfen.
- Enabled/Visible einzeln ändern: richtiger Save an richtige ID/Revision,
  verständlicher Pending-Apply-Status und kein automatischer Apply-/Inferencecall.
- Referenzfixture Daily, Workspace 2, Workspace 3; zusätzlich echte synthetische
  Fallbackreferenz prüfen. Entfernen gesperrt, benannte Verwendungen korrekt.
  Nach bewusstem Auflösen und erneuter Prüfung nur aktuelle ID entfernen;
  historische Usage weiter lesbar.
- `no-match-demo` liefert Filterleere mit Rückweg. Entfernte stabile ID bei
  offenem Editor bewahrt Eingaben und verhindert Save auf eine andere Route.
  Tabelle/Detail bei 1120×780 und 800×560, lange Namen und Tastatur prüfen.
- Lieferung: erfüllte Deltas, Command-/ID-/Revisionsmatrix, native Evidenz und
  fehlende Hostanschlüsse an UXD-22; keine Providerfähigkeit aus Fixtures ableiten.

Designs: `MODEL-MENU`, `REMOVE-REFERENCED`, `MODELS-NO-MATCH`,
`MODEL-SELECTION-GONE` im [Manifest](../uxpilot-flows-2026-10-25.json).
