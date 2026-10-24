# UXD-20 – Voraussetzungen, Exportvorbereitung und Clientsetup

Ergebnis: Vor einem Login ist erkennbar, welche lokale Voraussetzung fehlt.
Beim Codexsetup sind Vorbereitung, Installation und globales Apply getrennt.
Ergänzt UXD-03/05; [gemeinsamer Stand und Grenzen](16-state-continuity.md).

Owner: UXD-03 für `src/Lain.Desktop/MainWindow.AccountChooser.cs`,
`MainWindow.Login.cs`, `MainWindow.Providers.cs`, `MainWindow.Monitor.cs` und
Providerkatalogviews. UXD-05 besitzt `MainWindow.Profiles.cs`; Installationslogik
bleibt beim bestehenden ST-02-Owner. Shelländerungen über UXD-01, Client über UXD-00.
Voraussetzung: bestehende AC-Verbindungs-/Methodenverträge und ST-02-Artefaktfluss.
Zwei Teilstrecken mit getrenntem Anschlussnachweis; fehlender Loginanschluss
blockiert keine unabhängige Setupvalidierung und umgekehrt.

## Bestandsabgleich und Aufgaben

1. Bestehende bewusste Monitor-/Gatewaywahl und Methoden-/Corevoraussetzungen
   lesen. Fehlenden Core vor dem betroffenen Codex-Browserlogin erklären;
   Loginstart sperren, Rückweg zu Quellen oder bewusste Gatewayeinrichtung
   anbieten. Kein automatischer Betriebswechsel bei Verbindungsfehlern.
2. `FIRST-START` ist eine Zustandsstudie. Bestehenden Einstieg weiterverwenden;
   daraus weder eine neue Zweikarten-Startseite noch eine neue Modusmatrix
   ableiten. Keine Gatewayhistorie im Monitor und kein pauschales
   API-Key-Quotaversprechen; nur tatsächlich qualifizierte Methoden zeigen.
3. Existierenden Prepare-/Installfluss erhalten. Executable und bestehender
   Ziel-Elternordner werden vor Prepare validiert, Eingaben bleiben bei Fehlern
   erhalten. Fehlender Ordner bekommt Inlinehinweis; Prepare ist bis zu gültigen
   Voraussetzungen nicht ausführbar. Fehler ist keine behauptete Installation.
4. Prepare liefert die Beschreibung des gespeicherten Profils ohne Dateischreiben.
   Prepared zeigt Profil-/Artefaktidentität und Ziel, „noch keine Dateien
   installiert“ sowie einen eigenen Installauslöser. Keine erfundenen Pfade,
   Generationen oder Revisionen aus dem Design übernehmen.
5. Install verwendet genau das geprüfte Artefakt und den bestehenden Installer:
   neuer privater `lain-codex-…`-Ordner, separate Config, unveränderlicher Katalog.
   Normale Codexkonfiguration erhalten. Weder Prepare noch Install applizieren
   ein Profil; exportierter, installierter und tatsächlich bestätigter aktiver
   Stand bleiben getrennt. Reale Installergebnisse statt vorgreifender Erfolgstexte.

## Geplante Abnahme und Lieferung

- Monitorabsicht mit ausgewähltem Codexlogin und fehlendem Core: Erklärung vor
  Login, kein Loginstartrequest; Rückwege funktionieren. Gatewayausfall ersetzt
  keine bewusste Monitorwahl. Andere Methoden nur nach tatsächlichem Vertrag.
- Fehlender/ungültiger Zielordner und ungültiges Executable: kein Prepare,
  kein Dateischreiben; Eingaben und lokaler Fehler bleiben sichtbar. Danach
  korrigieren und denselben bewussten Schritt erneut auslösen.
- Daily betrachten, Workspace 2 bestätigt aktiv: Prepare liefert Prepared,
  ändert weder Appliedstatus noch Dateisystem. Cancel installiert nichts.
  Install separat in bestehender privater Testumgebung nachweisen; neue Dateien
  nur im neuen Ziel, gewöhnliche Konfiguration unverändert, kein Applyrequest.
- Echte Artefakt-ID/Revision am Übergang Prepared → Install bewahren;
  Profilwechsel hängt ein vorbereitetes Artefakt nicht unbemerkt auf anderes Ziel um.
  Fehler vor Prepare, bei Export und im Installer nach tatsächlichem Ergebnis trennen.
- Beide Fenstergrößen, Browse/Validierung/Fokusrückgabe und erreichbare Aktionen.
  Lieferung: zwei Teilstrecken mit Vertragszuordnung, Request-/Dateinachweis oder
  begrenztem Blocker. Keine echten Logins oder Installation im Planauftrag.

Designs: `FIRST-START`, `CONNECT-PREREQUISITE`, `CODEX-SETUP-VALIDATION`,
`CODEX-EXPORT-PREPARED` im [Manifest](../uxpilot-flows-2026-10-25.json).
Die Profilesdatei mit UXD-21 beim selben Owner nacheinander bearbeiten.
