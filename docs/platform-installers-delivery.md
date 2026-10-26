# Plattforminstaller – getrennte Anschlusslieferung, 26.10.2026

Implementiert in neuen Dateien, ohne Publisher-, Core-, Host- oder bestehende
Linuxinstalleränderung. Kein Commit, Upload, öffentlicher CI-Lauf oder produktiver
Installationslauf. Der eingefrorene Kandidat unter
`/home/tomka/.cache/lain-release-1.0-source-20261026` und die gebauten
Monitor-/Gatewaypakete werden von dieser Lieferung nicht verändert.

## Aufruf und Verhalten

Voraussetzung: Python 3.10+, vertrauenswürdiges entpacktes natives Paket nach
[Vertrag](../packaging/platform-installers/README.md). Installer und
`packaging/platform-installers/install.py` gehören zusammen.

macOS, im Repository-/Lieferverzeichnis:

```sh
scripts/install-macos.sh check /absolute/native-package
# ~/.local/bin muss bereits im Shell-PATH stehen; sonst explizite Sperre.
scripts/install-macos.sh install /absolute/native-package
scripts/install-macos.sh uninstall
```

Windows, gewöhnliches Benutzerterminal mit PowerShell 5.1+ und Python:

```powershell
./scripts/install-windows.ps1 -Action check -Package C:\absolute\native-package
./scripts/install-windows.ps1 -Action install -Package C:\absolute\native-package
./scripts/install-windows.ps1 -Action uninstall
```

`check` ist lesend und kann plattformübergreifend die zur Rechnerarchitektur
passende Paketform prüfen. Installation/Deinstallation verlangen das Ziel-OS
und verweigern Root/Administrator. Keine Downloads oder automatischen Starts.
Linuxpakete verweigern bereits am getrennten Paketvertrag, nicht erst nach dem
Kopieren. Binärformat, Architektur, vollständige SHA-Inventur, Notices,
Qualifikationsbindung und Gateway-Corefreigabe sind Pflicht.

macOS: private Versionen und Daten unter
`~/Library/Application Support/lain-platform/`, CLI `~/.local/bin/lain`,
App-Bundle `~/Applications/Lain.app`, Benutzeragent
`~/Library/LaunchAgents/local.lain.platform.host.plist`. PATH-Voraussetzung wird
geprüft; Shellprofile werden nicht verändert. Hoststart explizit:

```sh
launchctl bootstrap gui/$(id -u) ~/Library/LaunchAgents/local.lain.platform.host.plist
# Vor Update/Deinstallation Agent entladen und Desktop schließen:
launchctl bootout gui/$(id -u)/local.lain.platform.host
```

Windows: `%LOCALAPPDATA%\lain-platform`, CLI `bin\lain.cmd`, Benutzer-PATH
unter HKCU (neues Terminal nach neuer Anmeldung), `Desktop\Lain.cmd` und
Benutzer-Autostart `Lain Host.cmd`. Der Host läuft beim nächsten Login als
gewöhnlicher Benutzer mit sichtbarer Konsole; kein Administratorservice und
kein versteckter Systemprozess. Für sofortigen Start Autostartdatei öffnen;
vor Wartung deren Konsole beenden und Desktop schließen. Umgeleitete/OneDrive-
Desktopordner sind noch nicht unterstützt; die Datei liegt unter `%USERPROFILE%\Desktop`.

Beide Plattformen setzen LAIN_HOME auf den separaten `data`-Unterordner,
Hostbindung auf Loopback `127.0.0.1:18418` und den paketlokalen Corepfad.
Der initiale Modus wird nur bei fehlender Modusdatei angelegt. Vorhandene
Konfiguration/Credentials/DB werden nicht gelesen. Laufende Prozesse mit den
Lain-Host-/Desktop-/Corenamen sperren Wartung konservativ. Das ist kein
vollständiger Prozess-/Port-Healthcheck und keine native Abnahme.

Versionen sind nach Inventurhash unveränderlich adressiert und werden beim
Update erneut geprüft. Wiederholung verwendet dieselbe Version. Vorversionen
und sämtliche Nutzerdaten bleiben auch bei Deinstallation erhalten. Kein
Gateway→Monitor-Ersatz. Fremde oder modifizierte Integration wird verweigert.
Uninstall entfernt nur hashidentische verwaltete Dateien und einen selbst
hinzugefügten Windows-PATH-Eintrag. Leere App-Bundleverzeichnisse bleiben stehen.
Installation serialisiert sich mit einem lokalen Verzeichnislock. Bei Absturz
bleibt gegebenenfalls ein Lock oder `integration-pending.json`; dann explizite
Sperre mit manueller Prüfung statt falschem Erfolg. Mehrdateienintegration ist
nicht atomar; es gibt keinen behaupteten automatischen Crashrollback.

## Nachweise und Grenzen

`tests/platform-installers/test_contract.py` verwendet ausschließlich temporäre,
synthetische Mach-O-/PE-Header und Testtexte. Keine Fixture wird ausgeführt und
kein synthetischer Bericht gilt als native Qualifikation. Geprüft werden vier
RIDs × zwei Modi, Inventurangriffe, Manipulation, Symlink, Architektur,
Linuxformat, Secretpfade, fehlgeschlagene Qualifikation, Wiederinstallation,
Versionsupdate, Daten-/Versionserhaltung und fremde Integration. Der Lebenszyklus
testet echte Dateisystemoperationen mit isoliertem Layout; OSintegration und
Prozessabfrage sind dabei ersetzt. Keine produktiven Reads/Mutationen.

Ergebnis am 26.10.2026: **13 Tests bestanden**, einschließlich der acht
RID-/Modus-Unterfälle; Ausführung unter dem vorgegebenen flock-/systemd-/
Speicher-/CPU-Limit. Bash- und Python-Syntax sowie Whitespace der neuen Dateien
separat bestanden. PowerShell und echte native Runtime bleiben ungeprüft.

Reproduzierbarer Lauf (Runtimechecks nur in der gemeinsamen Lane):

```sh
flock /home/tomka/.cache/lain-verification-lane.lock systemd-run --user --scope --quiet -p MemoryHigh=1536M -p MemoryMax=2G -p MemorySwapMax=256M -p CPUQuota=150% nice -n10 python3 -B -m unittest discover -s tests/platform-installers -v
```

Offen vor Auslieferung:

- Echte vollständige macOS-/Windowspakete einschließlich nativer Dependencies,
  Notices und producerseitigem Secretscan; es existiert kein solcher Nachweis.
- Windows-Coreportierung für Dateisperren, sichere Dateiöffnung und Dateisystem-
  Abfragen (`Flock`, `Fstatfs`, `O_NOFOLLOW`); keine Freigabe durch diesen Installer.
- Native Host-/CLI-/Desktopprüfung einschließlich Modebootstrap, Loopbackport,
  Dienstlebenszyklus, Windows-Autostart/Console, Benutzer-PATH und Shellquoting.
- Native Update-/Uninstallprüfung, Crash-/Stromausfallwiederaufnahme, Windows-
  PATH-Konflikte und Desktopumleitung; Signierung/Notarisierung und Gatekeeper.
- PowerShellsyntax ist hier mangels `pwsh` nicht nativ geprüft. Python- und
  Bashsyntax separat geprüft. Keine Behauptung bestandener Windowsausführung.
- Native GitHubprüfungen erst nach öffentlichem Repository; keine Workflowdatei,
  kein Upload. Die zukünftigen Läufe müssen die im Paketvertrag genannten acht
  nativen Fälle mit exakter Payloadbindung nachweisen.

Die Lieferung ist ein implementierter, lokal fixturegeprüfter Installervertrag,
keine qualifizierte macOS-/Windowsproduktversion.
