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

UXD-17–22 sind integriert und mit den bestätigten Varianten installiert.
Der [native Quellenbericht](../tests/uxd-acceptance/state-flow-review.md)
dokumentiert zuletzt erfolgreiche Ergebnisse für alle 35 ausgewählten
Fallnamen über getrennte Läufe; der historische `native-final` bleibt 17/18,
der korrigierte Statusfolgelauf 2/2. Das ist kein einzelner 35/35-Lauf.
Accounts-/Monitor-/Models-Bereichsfixtures bestanden zusätzlich mit 34/28/18
Checks. Der [unabhängige Review](uxd17-22-independent-state-review-2026-10-25.md)
ordnet behobene Quellbefunde, persönlich gesichtete Bilder und Grenzen zu.

Auf den exakten ausgelieferten DLLs des Quellstands `d7200d7…` bestanden
18/18 ausgewählte native Fälle bei 100 % und 4/4 Layoutfälle bei tatsächlichen
125 %. Die [Paketbelege](../tests/delivery-stabilization/evidence/20261025/packaged-ui-identities.json)
halten die drei Produktions-DLL-Hashes fest. Ein unabhängiger Reviewer sichtete
sechs zugehörige X11-Bilder; Root drei. Verwendet wurden privates Xvfb und eigener
Session-D-Bus mit synthetischer API; das ist keine reale Providerprüfung.

Die [Lieferung](quota-state-delivery-2026-10-25.md) dokumentiert den tatsächlich
ausgeführten v4-Updateversuch und das verifizierte reale Update. Gewünschte und
angewandte Revision bleiben 19/2, Accounts und persistente Historie erhalten.
Es wurde kein automatisches Apply ausgeführt. Vollständige Accessibility,
breite Bildschirm-/Modusmatrix und reale zusätzliche FREE-Anbieterproben werden
aus diesen gezielten Nachweisen nicht abgeleitet.

Der gezielte anschließende [Read-only-Test der installierten UI](../tests/delivery-stabilization/evidence/20261025/installed-ui.json)
bestand **100 Checks** mit acht Bildern bei 1120×780 und 800×560. Exakte
installierte Assemblies, sichtbare Simple-Navigation mit fünf Bereichen plus More,
Live-Quellidentität/Leerhinweis, unbekanntes angewandtes Profil und Erhalt von
Host, Konfiguration und fester Historie über Schließen/Wiederöffnen wurden
geprüft. Alle 23 HTTP-Aufrufe waren GET; Preferences, Xvfb und Session-D-Bus
waren privat. Root sichtete beide Livebilder und das schmale Profilesbild.
Die vorherige breite installierte Legacyfixture scheiterte an ihrer veralteten
Fünf-Navigations-Erwartung; sie bleibt separat als Fehler erhalten. Der neue
gezielte Modus macht daraus keinen Gesamtsuiten-Pass. Keine neue Inferenz oder
reale Providerqualifikation wurde durch diese Beobachtung erzeugt. Reale
Accountbilder und vollständige Antworten bleiben ausschließlich im privaten Backup.

Nach Abschluss wurden zehn nicht mehr benötigte `bin`-/`obj`-Verzeichnisse
entfernt (2.272 MiB logische Dateigröße). Quellen, externe Harnessprojekte und
Locks, Pakete, Backups, Protokolle und Bildbelege bleiben erhalten. Die unten
bzw. im Quellenbericht genannten Harness-Buildpfade beschreiben den Prüflauf;
die gelöschten Assemblies lassen sich aus den festgehaltenen Inputs neu bauen.
