# Accountnamen und Plattforminstaller

Nutzerentscheidung 26.10.2026, ergänzend zum laufenden 1.0-Auftrag.

## Accountnamen

- Genau ein konfigurierter Account pro Provider: kompakter Providername in
  Accountlisten, Usage, Filtern und Auswahlfeldern. Grundlage ist der gesamte
  Accountbestand einschließlich deaktivierter Accounts, nicht der aktuelle Filter.
- Mehrere Accounts desselben Providers: eigene Namen zur Unterscheidung;
  generische Namen werden nummeriert, Namenskollisionen aufgelöst.
- „Rename account…“ im Account-Menü, einfacher Editor mit Speichern/Abbrechen.
  CLI: `lain accounts rename ID --name "Work account" [--revision N]`.
- Umbenennen ändert nur den gespeicherten Namen unter Revisionsprüfung.
  Account-ID, Credentials, historische Zuordnung und Modellbindungen bleiben erhalten.
- Usage verwendet die gemeinsame Client-Anzeigenamenslogik statt roher Importnamen.

Implementierung im Quellstand; noch kein neuer Installationsnachweis.

Offline geprüft am26.10.2026:479 Client-/CLI-Vertragschecks und415
Account-Foundationchecks bestanden. Die Foundationfälle prüfen insbesondere
dauerhaftes Umbenennen, unveränderte Accountmetadaten, ungültige Namen und
Revisionskonflikte. Desktopbuild erfolgreich,0 Warnungen/0 Fehler.
Native Usageprüfung bestanden:30 Checks, private Xvfb-/D-Bus-Sitzung,
synthetischer HTTPbestand,1120×780 und800×560/beide Dichten. Accountlabels,
Route-Alias samt unveränderter Detail-/Navigationsidentität und unbekannte
Messwerte geprüft. Belege im privaten Cache
`~/.cache/lain-usage-names-20261026/native-r2/`; keine Provideranfragen oder
Produktionsfenster aus dieser UI-Prüfung. Die Account- und Route-Screenshots
wurden visuell geprüft.

## Lesbare Routennamen in Usage

Die Route-Gruppierung zeigt den konfigurierten Modell-Alias statt der internen
`route-…`-ID, ebenso die Überschrift der ausgewählten Gruppe. Die ID bleibt in
den Details erhalten; Filter und Navigation verwenden weiterhin diese stabile
Identität. Providergruppen verwenden die formatierten Providernamen.
Wenn die aktuelle Route unbekannt ist, bleibt ihre historische ID als ehrlicher
Fallback sichtbar. Keine rückwirkende Umbenennung oder Änderung der Messdaten.

## Installer für macOS und Windows

Gewünscht sind auch macOS- und Windows-Installer mit Desktop, Host und `lain`-CLI.
Nach öffentlicher Bereitstellung des Repositorys sollen native GitHub-Runner die
Installation prüfen. Aktuell existiert nur der Linux-Installer; diese Notiz ist
kein Mac-/Windows-Paket oder bestandener CI-Lauf.

Arbeitspakete:

1. Native Paketierung inklusive Core und Drittanbieter-Notices für die unterstützten
   Architekturen; ausführbare CLI nach Installation im Terminal erreichbar.
   Vorher Plattformstellen qualifizieren: Der Core verwendet derzeit direkt
   `syscall.Flock`, `Fstatfs` und `O_NOFOLLOW`; Windows braucht dafür passende
   Implementierungen von Sperren, Dateisicherheit und Speicherplatzabfrage.
   Die Host-Fireworks-Anbindung meldet außerdem Unix-Dateimodusstellen beim Build.
2. macOS: App-Bundle und Installer, Benutzer-`launchd`-Dienst, CLI-Pfad,
   saubere Deinstallation und Bestandserhaltung bei Updates.
3. Windows: Installer mit CLI-/PATH-Integration, Desktopstart und Benutzerbetrieb
   des Hosts ohne pauschale Administratorpflicht; Update-/Deinstallationspfad.
4. GitHub-Prüfungen auf Linux, macOS und Windows: frische Installation,
   `lain --help`, Hoststart, `lain status --json`, leeres `lain usage`,
   Wiederinstallation/Update und Deinstallation mit geprüfter Bestandserhaltung.
   Fixtures ohne echte Providercredentials; zunächst keine automatische Veröffentlichung.
5. Signierung/Notarisierung und echte Plattformabnahme vor öffentlicher
   Auslieferung klären; Crossbuilds ersetzen keine nativen Installationsprüfungen.
