# Lain 1.0.0 – Release Notes für Linux x64

Kandidat vom 26.10.2026, Source `882bdadd09235b35ca74c0ebdcff59e00061b2fe`.
Vorbereitete Release Notes; noch keine öffentliche Veröffentlichung oder
Gesamtfreigabe. Die aktuellen Archive bleiben `releaseQualified: false`.

## Umfang

Lain verbindet lokale Konten-, Modell- und Routenverwaltung mit CLI und
Avalonia-Desktop. **Monitor** liefert diese Oberfläche ohne Inferenz-Core;
**Gateway** ergänzt den eigenen gepinnten CLIProxyAPI-8.0.41-Build.
Beide Pakete enthalten Self-contained .NET 10.0.11, Installer, Skills,
Wartungshelfer, Prüfsummen, Komponentenprovenienz, LICENSE/NOTICE und die
exakte getrackte Source. Eigene Bestandteile stehen unter Artistic-2.0;
Drittsoftware und das bestätigte Huxel-Foto behalten ihre eigenen Rechte.

Der Kandidat ergänzt quotaabhängige Verfügbarkeit für Router, begrenzten
Fallback und sichere Behandlung vorhandener Threadpins. Nachträgliche
Pinmigration unterstützt vollständige Message-/gepaarte Functionhistorie;
opaque Reasoning-/Customtoolhistorien verlangen einen neuen Thread.
CLI-/Host-Funktionen für begrenzte Worker sowie die mitgelieferten Skills
lain-usage, lain-workers, lain-connect und lain-doctor gehören zur Source
und Paketlieferung. Persönliche Skillverzeichnisse werden nicht automatisch
verändert.

## Belegter Prüfstand

Der neue Core ist qualifiziert. Lieferbelege nennen 106 Hostchecks,
298 Go-Test-Passes und elf bestandene isolierte Vollpaketchecks. Die exakten
Monitor-/Gatewayarchive wurden zusätzlich auf innere Prüfsummen,
Archivübereinstimmung und den Bezug ihres Sourcearchivs geprüft; Details in
der [Notice Review](release-1.0-notice-review.md).

Diese Nachweise sind keine neue Produktivinstallation und keine vollständige
Live-Abnahme aller Provider/Modelle. Der übergebene lokale Produktivstand ist
weiterhin `ff5771c`, Revision 58 gespeichert / 57 angewandt. Die neue eigene
Update-/Bestandserhaltungsabnahme wartet auf das vom Nutzer gewünschte Ende der
laufenden Recherche. Native Desktopprüfung besteht: acht gepflegte Läufe sowie
zwei vollständige Defaultläufe mit je698 Checks; alle228 Defaultbilder geprüft.

## Einschränkungen

- Ziel ist Linux x86_64; keine fertige macOS-/Windows-Lieferung, keine belegte
  allgemeine Distributionsmatrix. Desktop benötigt grafische Sitzung und native
  Avalonia-Abhängigkeiten; Benutzer-Servicebetrieb benötigt systemd.
- Providerzugang, Kontingente und Modellberechtigungen hängen vom jeweiligen
  Account ab. Statischer Katalog bedeutet keine Providerberechtigung.
- Keine modifizierten Codex-Binaries und keine modifizierte VSIX im öffentlichen
  Lainpaket. Optionale UI-Anpassungen werden als eigene Source/Patches mit Pins
  geliefert; Nutzer beziehen Originalclients selbst und qualifizieren lokale
  Builds. Keine pauschale Freigabe für deren Binärweitergabe.
- Die historischen Noticelücken sind in Source882 und den r4-Archiven geschlossen;
  [Packagingreview](../packaging/licenses/review-20261026.md). Frühere Archive
  und fehlgeschlagene Abnahmeläufe bleiben als historische Belege erhalten.
- Source liegt bei; alle externen Abhängigkeiten und Upstreamarchive für einen
  vollständigen Offline-Rebuild liegen nicht bei. Neue Begleitdokumente aus
  diesem Auftrag gehören noch nicht zur eingefrorenen Sourcerevision.

## Installation, Upgrade und Removal

[Downloadprüfung und Linux-Betriebsanleitung](release-1.0-distribution-readiness.md)
nennt die exakten Archivhashes, Voraussetzungen und Befehle. Installation
startet/aktiviert keinen Dienst. Vor Updates Aufträge abschließen, Desktop und
Clients schließen, Dienst geordnet stoppen und privat sichern. Installer erhält
Daten und bewahrt die vorherige App als `app.previous`; vor einem weiteren
Update diese Sicherung bewusst archivieren. Monitorinstallation ersetzt keinen
bestehenden Gatewaybestand als implizites Downgrade.

Rollback alter Binaries allein ist keine sichere Daten-/OAuth-Recovery.
Snapshots nur in ein neues Verzeichnis wiederherstellen; Quarantäne und
notwendige erneute Kontoverbindung beachten. Beim Entfernen zuerst Dienst und
Clients beenden und die verwalteten Integrationsdateien entfernen. Private
Daten bleiben erhalten, bis sie bewusst separat entfernt werden. Optionale
Clientanpassungen und Providergrants haben eigene Rücknahmeabläufe.

Das GitHub-Repo soll nach der Veröffentlichungsprüfung öffentlich werden.
Ein öffentlicher Release mit Tag/Downloads und die Gesamtfreigabe bleiben eigene
Schritte. Die korrigierten r4-Archive sind gebaut und geprüft; laufende Recherche
wird für das lokale Update nicht unterbrochen.
