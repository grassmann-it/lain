# UXD-04 – Dichtes Modellinventar mit vollständigen Aktionen

Ziel: Im 45er-Bestand Zugang finden, Zustand verstehen und sofort bearbeiten.
Abhängig von UXD-01/02 und dem aktuellen Policyvertrag aus UXD-00; F05.
[Übersicht](README.md).

## Referenz, Befund und Dateien

Frame 9 `XjprbmkRcPW8vOyOPOEr`,
[PNG und Korrekturprotokoll](../uxpilot-design-results-2026-10-24.md).
Heute rendert `MainWindow.Routes.cs` hohe Karten; Filter, kontextuelle Route-ID,
Accountbindung, Pool-/Provenienzfelder sowie Policy-/Fallbackeditoren existieren.

Dieses Paket besitzt `MainWindow.Routes.cs` einschließlich der von Profiles
genutzten `CompactRouteButton`-/`SpendingPolicyEditor`-/Policyhelpers. Vor UXD-05
deren Aufrufvertrag stabilisieren; Auslagerung mit Shellowner abstimmen.
`MainWindow.Workspace.cs` bleibt bei UXD-01, Clientwrapper bei UXD-00.

## Aufgaben

1. Hohe Inventarkarten durch native tabellarische Zeilen ersetzen: Clientalias,
   Anbieter/Accountbindung, Enabled, Menüsichtbarkeit, Pending apply und Edit/⋯.
   Auswahl öffnet lokales Detail; Suche und Provider-/Visibility-/Statefilter bleiben.
2. Stabile Route-ID verwenden; bei Navigation über Route-ID hat diese Vorrang
   vor ähnlichem Modell-/Aliastext. Account, Route, angefragtes und effektives
   Modell nicht zusammenwerfen. Fehlende Werte als nicht geliefert kennzeichnen.
3. Enabled und Visible getrennt zeigen/bedienen. Disable speichert und wirkt
   beim nächsten globalen Apply. Hidden entfernt einen Alias aus Modellmenüs,
   sperrt keinen direkten Aufruf. Kein schneller Schalter suggeriert Sofortsperre.
4. Edit und vollständiges Menü ohne vorgeschaltetes technisches Aufklappen
   erreichen: Enable/Disable, Menüsichtbarkeit, Profilverwendungen, Usage,
   Account, Kopieren, Entfernen. Entfernen zeigt benannte Profil-/Fallbackverweise
   und erhält gespeicherte Historie.
5. Bestehende vollständige Bearbeitung erhalten: Upstreammodell, echte Account-
   oder Poolbindung, Providerroute, Policy/Spending/Fallbacks, Defaults und
   Provenienz. Lokale Expertbereiche verdichten, nicht funktionslos weglassen.
   Keine Ableitung von Firstparty oder unterstützten Tools aus Aliasnamen.
6. Breite Zeilen/Inspector und schmale Darstellung mit UXD-01 abstimmen.
   Lange Namen dürfen umbrechen/gezielt aufgeklappt werden; entscheidende Zustände
   und Aktionen bleiben zugänglich. Header und Zeilen müssen gleich ausgerichtet sein.

## Abnahme

- 45 synthetische Routen und lange Alias-/Accountnamen bleiben filterbar und
  bedienbar. Die acht Designbeispielzeilen werden exakt verwendet, sofern der
  Designvergleich sie braucht; zusätzliche Routen kommen aus qualifizierten Fixtures.
- `gpt-lain-01`: strikt Personal Codex gebunden, Verwendung in drei Profilen.
  `gpt-lain-02`: saved Disabled/Pending apply. `gpt-lain-04`: Enabled und Hidden.
  Diese Zustände sind ohne Detailöffnung unterscheidbar.
- Disable/Hidden ändern nur ihren gespeicherten Wert mit aktueller Revision;
  es entsteht kein Apply, automatischer Fallback oder Credentialwechsel.
- Vorhandene Policy-/Fallbackfelder lassen sich öffnen, ändern, abbrechen und
  wieder laden. Nicht unterstützte Optionen benennen ihre Einschränkung.
- Edit/⋯ ist in 1120×780 und 800×560 erreichbar. Menüs sind per Tastatur,
  Rechtsklick und sichtbarem Trigger gleich; Polling/Sortierung ändert ihr Ziel nicht.
- Models → Account/Usage/Profilverwendungen → Back erhält Auswahl, Filter,
  Scroll und Editorentwurf. Entfernte Route oder Revisionkonflikt führt zu
  verständlichem Zustand ohne Schreiben auf ein Ersatzziel.
- Nicht gelieferte Preise, Limits und Fähigkeiten bleiben unbekannt;
  keine neue kostenlose oder kostenpflichtige Fähigkeit aus dem Design ableiten.

## Grenzen und Übergabe

Keine neue Routingengine, automatische Modelloptimierung, atomare Sammeländerung
oder temporäre Sofortsperre. Solche Vertragsänderungen bleiben eigener Umfang.

Liefern: Inventar-/Edit-/Kontextzustände beider Größen, Commands samt Semantik,
Erhaltungsnachweis der Expertenfelder und stabilen Helpervertrag für UXD-05.
