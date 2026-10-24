# UXD-00 – Integrationsstand, Verträge und Entscheidungen

Ziel: Ein späterer Implementierer kennt die echte technische Basis, vorhandene
Owner und die von offenen Designentscheidungen abhängigen Teile. Einstieg und
gemeinsame Grenzen stehen in der [Paketübersicht](README.md).

## Befund und Zuständigkeit

Die 14 Designs beschreiben breite Gatewayansichten. Der gemeinsame Quellbaum
wird parallel in AC-/ST-Paketen verändert. Vorhandene DTOs sind kein Nachweis
für einen nutzbaren Host-/Clientpfad. Dieses Paket besitzt den Abgleich und
gemeinsame Clientanschlüsse; neue fachliche Backendarbeit bleibt bei AC/ST.

Gezielt lesen: `src/Lain.Core/Contracts.cs`, `ConnectionContracts.cs`,
`ProfileApplicationContracts.cs`, `src/Lain.Client/LainApiClient.cs`,
`ObservabilityClient.cs`, `src/Lain.Host/Program.cs`,
`Providers/ProviderCatalogEndpoints.cs`, `Models/ModelEndpoints.cs` und
`Models/ProfileApplicationService.cs`. Weitere Reads nur für betroffene Anschlüsse.

## Aufgaben

1. Aktuellen HEAD, lokale Änderungen und zuständige AC-/ST-Owner erfassen.
   Bereits integrierte Arbeit als erfüllt übernehmen; die alte Offen-Tabelle im
   Gesamtausführungsplan ist kein verlässlicher Nachweis fehlenden Codes.
2. Folgende Tabelle mit realem Vertrag, Fehlern und synthetischem Beispiel
   vervollständigen, bevor eine Seite den Anschluss benutzt:

   | Anschluss | Beim Planen beobachtet | Nächster Nachweis |
   | --- | --- | --- |
   | Betriebsabsicht / Fähigkeiten | `StatusDto.RuntimeMode`, `Capabilities`; `PUT /api/v1/runtime-mode` vorhanden, Clientwrapper fehlt | Revision, Wechselwirkung und Verhalten alter Hosts klären; Monitor nicht aus `ProxyReady` ableiten |
   | Verfügbare Methoden / Connect | Core-DTOs vorhanden; bisheriger Provider-/Onboardingkatalog und bestehender Login-/Keypfad verdrahtet | Neue Methodenkatalog-/Connectendpunkte gegen AC-00/01/02/09 prüfen; keine URL oder Fähigkeit aus DTOexistenz erfinden |
   | Applyvorschau / Recovery | `ProfileApplyPreviewDto`, `ProfileApplicationStatusDto`, Previewservice vorhanden; im gelesenen Endpoint-/Clientpfad noch kein Vorschauanschluss | ST-02 liefert erreichbaren, revisionsgebundenen Vorschau-/Statuspfad; bestehenden Apply nicht mit erfundener Vorschau beschriften |
   | Teilmessungen / Attribution | Usage-/Observabilityclient und neue DTOfelder vorhanden | Vorhandene Felder mit ST-03 abgleichen, Missing/null und Abdeckung in Fixtures festschreiben |

3. Vorläufige Layoutannahmen aus der Übersicht als Entscheidungen oder weiter
   offene Vorschläge dokumentieren: reservierte Accountsbreite, Simple-Live,
   schmaler Detailwechsel, Barebonesnavigation. Bei offenem Punkt nur abhängige
   Produktgestaltung zurückhalten; technische Vorarbeit weiterführen.
4. Mit UXD-01 den Seitenvertrag festlegen: eigenständige Scrollflächen,
   Seiten-/Objekt-ID, Detailzustand, Filterdraft, Backziel, Fokusanker,
   Präsentationswechsel in place und Verhalten verschwundener Ziele.
   Vorhandene `WorkspaceNavigationRequest`, `EditorSnapshot`,
   `WorkspacePreferencesStore` erweitern, wo nötig; keine zweite Zustandsverwaltung.
5. Zuständigkeit für jede gemeinsame Datei bestätigen. Clientwrapper hier;
   Shell und Commands in UXD-01; Quotahelpers UXD-02; Route-/Policyhelpers UXD-04;
   zentrale Tests UXD-09. Neue gemeinsame Datei ebenso eindeutig zuordnen.
6. Einfache verfügbare Anschlüsse zuerst integrieren. Eine nicht qualifizierte
   Provider-/Loginmethode blockiert nur ihre Darstellung als ausführbare Methode.
   Kein vorgetäuschter Erfolg und keine stille Umschaltung auf einen anderen Login.

## Abnahme

- Für jeden verwendeten Anschluss gibt es reale Methode/URL, Eingabe-/
  Antwortbeispiel, Revision und Fehlerverhalten oder einen benannten Blocker
  samt zuständigem bestehendem Paket. Alle Beispiele sind synthetisch.
- Host offline, bewusster Monitor, Gateway ohne Setup und gestörtes eingerichtetes
  Gateway werden separat beschrieben. Fehlende neue Felder eines alten Hosts
  erzeugen keine erfundenen Fähigkeiten und keinen Moduswechsel.
- Geänderte Revision während Menüöffnung/Applyvorschau ist als Konfliktfall
  enthalten; es wird kein veralteter Stand automatisch erneut geschrieben.
- Die Abhängigkeiten haben keinen Kreis. UXD-09 kann Fälle früh ausarbeiten,
  seine abschließende Ausführung wartet auf die jeweiligen Seiten.
- Jede offene Layoutentscheidung benennt Nachteil, Alternative und betroffene
  Pakete. Keine allgemeine Designzustimmung wird aus dem Paketauftrag abgeleitet.

## Grenzen und Übergabe

Keine neue Credential-, Provider-, OAuth-, Persistenz- oder Applyengine. Keine
Installation oder echte Anmeldung als Voraussetzung des lesenden Abgleichs.
Vertragsänderungen am Core/Host nur durch den bestehenden AC-/ST-Owner.

Liefern: kompakte aktuelle Anschlussmatrix mit Commitbezug, vereinbarte
Dateieigentümer, synthetische Beispiele und Entscheidungs-/Blockerliste an die
betroffenen UXD-Pakete. Änderungen gegenüber diesem Plan ausdrücklich benennen.
