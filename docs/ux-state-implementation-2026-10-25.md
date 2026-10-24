# Umsetzung UXD-17–22: Zustandsfolgen

Auftrag vom 25.10.2026: Die sechs Pakete aus
[UXD-16](ux-design-work-packages/16-state-continuity.md) sind ausdrücklich in
die laufende Implementierung aufgenommen. Der frühere reine Planungsauftrag
begrenzt diese Umsetzung nicht mehr. Bestehende AC-/ST-/UXD-Verträge und die
laufende Kontingentimplementierung bleiben erhalten.

## Bestätigte Präsentationsentscheidungen

Der Nutzer hat beide offenen Varianten am 25.10.2026 ausdrücklich gewählt:

- **Draftnavigation B:** direkt wechseln, Abschnittsentwurf erhalten; bleibender
  Hinweis außerhalb des wegscrollenden Inhalts mit Rückweg und ausdrücklichem
  Verwerfen. Navigation löst weder Save noch Apply aus.
- **Simple-Live A:** Live direkt in der Hauptnavigation. Profiles bleibt im
  Gateway direkt erreichbar; kein zweiter Eintrag unter More und kein A/B-Schalter.

Damit sind die beiden Darstellungsfragen aus UXD-16/17 für diesen Auftrag geklärt.
Andere Vorschläge werden dadurch nicht pauschal freigegeben. Bestätigte
Accounts-Panels, Monitortabelle mit langen Balken, Models-/Usagetabelle,
Applyreview im Profildetail und linke schmale Liveliste bleiben Grundlage.

## Durchführung und Zuständigkeiten

Bezugsstand der Aufnahme: Main `bba9804385f6e78ef77244c83dea99b3ddfb7bd5`.
Alle Owner gleichen vor ihrer Änderung den tatsächlichen Code und die vorhandenen
Tests ab; bereits erfüllte Fälle benötigen Evidenz statt Neuimplementierung.

| Paket | Zuständigkeit | Nächster Nachweis |
| --- | --- | --- |
| [UXD-17](ux-design-work-packages/17-navigation-drafts.md) | Shell/Workspace/Presentation; gemeinsame Shelländerungen anderer Owner | Draft B und Simple-Live A; Erhalt, Revisionsschutz, Fokus und Dichtewechsel |
| [UXD-18](ux-design-work-packages/18-account-snapshots.md) | Accounts/Tiles/Quota/Budgets; Busyvorschlag an Shellowner | Erster Snapshot, leer/Fehler, lokaler Refresh A→B, alle Fenster |
| [UXD-19](ux-design-work-packages/19-model-command-identity.md) | Routes und lokale Policyhelpers | Stabile Command-ID, Referenzsperre, Filter und verschwundene Auswahl |
| [UXD-20](ux-design-work-packages/20-connect-client-setup.md) | Connectseiten; Profiles-Owner für Clientsetup | Voraussetzungen, Prepare ohne Dateien, separate Installation |
| [UXD-21](ux-design-work-packages/21-apply-status-consistency.md) | Derselbe Profiles-Owner nach Setup; Shellstatus an UXD-17 | Revisionsgebundene feste Reviewfläche, unbekannter Ausgang, drei konsistente Anzeigen |
| [UXD-22](ux-design-work-packages/22-flow-acceptance.md) | Unabhängiger nativer Testowner | Synthetische Eingabe-/Request-/Bildmatrix in beiden Größen |

Private Worktrees, maximal zwölf aktive Agenten einschließlich Integrator.
Fachworker liefern Source und Bereichsfixtures. Gemeinsame Host-/Clientverträge,
Projektdateien und Migrationen koordiniert Root; zentrale UI-Fixtures gehören
UXD-22. Keine konkurrierenden Änderungen an gemeinsamen Shell-/Profilesdateien.

Builds, Publishes und Runtimeprüfungen bleiben seriell im zugeteilten Slot,
mit 1536 MiB MemoryHigh, 2 GiB MemoryMax, 256 MiB Swaplimit, CPUQuota150%,
niedriger CPU-/I/O-Priorität und eigenem SSD-TMPDIR. Unbenötigte Buildartefakte
werden gelöscht. GUI ausschließlich privates Xvfb, niemals Nutzerdisplay oder
Sitzungsbus. Keine echten Providerlogins, Käufe, Probeinferenz, automatisches
Apply oder Änderung bestehender Accounts/normaler Codexkonfiguration durch Fixtures.

## Nachweisstand

Aufnahme und Nutzerentscheidungen sind dokumentiert; die neuen Paketprüfungen
sind noch nicht ausgeführt. Source, lokale/native Prüfung, exaktes Paket,
Installation und reale Anbieterqualifikation werden getrennt fortgeschrieben.
Die laufenden Quotaänderungen ersetzen keinen dieser UX-Nachweise.
