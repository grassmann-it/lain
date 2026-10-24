# Linux-Auslieferung

Erster Zielstand: Linux x64, .NET SDK **10.0.111**, Self-contained Host, CLI und
Avalonia Desktop. Der Build verwendet das `global.json`, explizite NuGet-Versionen
und eingecheckte `packages.lock.json`; Locked Restore verweigert Änderungen am
Abhängigkeitsgraph. Die Desktop-Laufzeit benötigt ein X11-/Wayland-Desktop-System
mit den für Avalonia üblichen nativen Bibliotheken (insbesondere fontconfig/X11).
Der gebündelte Proxy ist der verifizierte eigene Build aus dem externen, in
`proxy/upstream.json` gepinnten CLIProxyAPI-Quellarchiv, dem Anschluss in
`proxy/adapter` und den expliziten Hooks in `proxy/patches`, kein
installierter Bestandsproxy. Seine Herkunft und Hashes liegen im Paket.

## Monitor und Gateway im aktuellen Quellstand

Die folgenden historischen Gateway-/Desktopabläufe sind kein Nachweis für eine
neu installierte Version. Der aktuelle Publisher bietet explizite Pakete:

```sh
./scripts/publish-linux.sh --mode monitor /absolute/new/lain-monitor
./scripts/publish-linux.sh --mode gateway /absolute/new/lain-gateway
```

Monitor enthält Host/CLI/Desktop und Anwendungsnotices, aber keinen Core,
Corekatalog, Go-Build oder Go-Notice-Inventar. Gateway erhält sämtliche
Core-Integritäts-/Quell-/Abnahmegarantien. `package-metadata.json` und
`build-info.json` nennen die Betriebsart; `source-provenance/publish-properties`
zeichnet `LainRuntimeMode` pro Komponente auf. Ein frischer Installer setzt nur
den Initialwert, Bestandskonfiguration dominiert. Dienste werden nicht gestartet.

Der neue vollständige Ablauf einschließlich gesichertem Update, gestopptem
Snapshot, rotierenden Credentials und Grenzen alter Hosts steht in
[Linux-Delivery-Stabilisierung](linux-delivery-stabilization.md). Die tatsächliche
Paketprobe unter `tests/delivery-stabilization/actual_packages.py` ist standardmäßig
planend, benutzt bei expliziter Ausführung nur neue temporäre XDG-/LAIN_HOME-Pfade
und niemals systemd oder bestehende Daten. Alle Build-/Runtimeslots koordiniert
der Integrator. Erst die exakten geprüften Artefakte können eine neue Installation
begründen.

## Gatewaypaket bauen (bisheriger Ablauf)

Vom Checkout aus, mit .NET SDK und der vom Proxy-Build geforderten Go-Version:

```sh
./scripts/publish-linux.sh /absolute/path/lain-linux-x64
```

Der Zielordner muss neu sein. Das Skript veröffentlicht `cli/Lain.Cli`,
`host/Lain.Host`, `desktop/Lain.Desktop` und `proxy/lain-proxy`, Lizenz,
Quellmanifeste, ein `notices/`-Inventar mit exakten NuGet-Lizenzmetadaten und
verfügbaren Paket-/offiziellen Runtime-Lizenztexten sowie dem Inventar tatsächlich
verlinkter Go-Module samt gecachten Root-Lizenzen und Go-Toolchain-Lizenz, statischen `proxy/codex-model-catalog.json`, CLI-Skill und Installer. `SHA256SUMS` schützt die Paketdateien;
neben dem Ordner entstehen `.tar.gz` und dessen Prüfsumme. Das Archiv verwendet
sortierte Dateinamen, feste Eigentümer und Commitzeit (`SOURCE_DATE_EPOCH`
überschreibt diese). .NET baut deterministisch mit normalisierten Quellpfaden.
`build-info.json` zeichnet Commit, SDK und uncommittierte Änderungen auf.
Für einen vergleichbaren Releasebuild einen sauberen Commit und dieselben
Toolchains verwenden. Die Manifestprüfung allein ersetzt die Backend-Abnahme
nach `docs/execution.md` nicht. Ohne endgültigen Abnahmebeleg ist das Ergebnis
nur ein Staging-Paket: `gate-status.json` enthält `releaseQualified: false` und
es wird keine `qualification.json` erzeugt. Für ein backendabgenommenes Paket
`LAIN_QUALIFICATION_FILE=/absolute/approved/qualification.json` setzen. Dieser
Beleg muss `passed: true` und den exakten `sha256` des neu gebauten Proxys
enthalten; ein abweichender Beleg lässt den Build fehlschlagen. Der Publisher
setzt niemals selbst ein positives Abnahmeergebnis. Ein positiver Backendbeleg
setzt nur `backendQualified: true`; `releaseQualified` bleibt bis zur getrennten
Paket-, Desktop- und installierten Workflow-Abnahme `false`.

Falls der Lockstand bewusst aktualisiert werden muss:

```sh
dotnet restore Lain.sln -r linux-x64 -p:SelfContained=true
```

Die Änderungen an allen Lockdateien prüfen und einchecken, dann erneut publizieren.
Die Release-Pipeline aktualisiert Lockdateien niemals automatisch.

## Nur den Desktop neu paketieren

Ein reines UI-Update verwendet den bereits qualifizierten Paketordner als
Basis und wird zuerst in einem neuen Staging-Ordner aufgebaut.
`publish-linux.sh` ist dafür ungeeignet: Es baut auch Host, CLI und Core neu.
Den Desktop mit dem aktuellen Lockstand separat veröffentlichen:

```sh
dotnet restore src/Lain.Desktop/Lain.Desktop.csproj --locked-mode -m:1 \
  -p:BuildInParallel=false -nr:false -r linux-x64 -p:SelfContained=true
dotnet publish src/Lain.Desktop/Lain.Desktop.csproj --no-restore -m:1 \
  -p:BuildInParallel=false -nr:false -c Release -r linux-x64 --self-contained true \
  -o /absolute/new/desktop-staging -p:ContinuousIntegrationBuild=true \
  -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false \
  -p:InformationalVersion=1.0.0+DESKTOP_COMMIT \
  -p:PathMap="$PWD=/_/lain"
```

Im kopierten Paket ausschließlich den gesamten `desktop/`-Ordner ersetzen;
ein Überschreiben einzelner Dateien würde entfernte Abhängigkeiten erhalten.
Host, CLI und `proxy/` vor und nach dem Kopieren anhand aller Dateihashes
vergleichen. Die Live-Installation und den Dienst bis zur Desktopprüfung
unverändert lassen.

Das neue NuGet-Inventar muss aus dem aktuellen Restoregraph stammen. Vor
`python3 scripts/collect-notices.py /absolute/new/package/notices` auch Host
und CLI mit ihren unveränderten Lockdateien wiederherstellen und prüfen, dass
deren inventarisierte Abhängigkeiten zum Basispaket passen. Einen frischen
`notices/`-Ordner verwenden, damit keine entfernten Pakete zurückbleiben. Das
Skript erfasst Semi, Ursa und transitive Pakete automatisch; `official/`
ergänzt die Lizenztexte aus den in `sources.json` festgehaltenen
Repository-Commits der NuGet-Pakete. Der alte Avalonia-11-Lizenztext bleibt als
historischer Zusatz erhalten; das aktuelle Inventar benennt die exakte Version.

In `build-info.json` ausschließlich Desktop-Provenienz und den neuen
Paketierungsstand aktualisieren, die bisherigen Komponentenstände erhalten.
Backendabnahme und ihre exakten Hashes bleiben bestehen; alte Desktopbelege
gelten nur für ihren aufgezeichneten Stand. `releaseQualified` bleibt bis zur
erneuten visuellen Abnahme `false`. Anschließend `SHA256SUMS` für alle
Paketdateien sowie Archiv und Archivprüfsumme neu erzeugen. Erst das geprüfte
Paket für eine getrennte Desktopaktualisierung der Installation verwenden;
der normale Installer ersetzt die ganze Anwendung.

Diese Schritte automatisiert der Staging-Helfer nach dem Restore aller drei
Projekte und dem separaten Desktop-Publish:

```sh
python3 scripts/stage-desktop-refresh.py /absolute/baseline-package \
  /absolute/new/desktop-staging /absolute/new/lain-desktop-refresh \
  --desktop-source-commit DESKTOP_COMMIT \
  --desktop-informational-version 1.0.0+DESKTOP_COMMIT
```

Er verweigert vorhandene Ausgaben, prüft das Basispaket, vergleicht die
Nicht-Desktop-Abhängigkeiten, erhält Host/CLI/Proxy bytegenau und erzeugt neue
Provenienz, Notices, Prüfsummen und Archiv. Fehler hinterlassen einen
unvollständigen Staging-Ordner zur Untersuchung; diesen nicht installieren.
Der Helfer startet weder Programme noch Dienste und schreibt in keine Installation.

Für die spätere Aktualisierung nur das GUI schließen; der Dienst bleibt aktiv.
Vor jeder Installationsänderung die kompletten `cli/`, `host/` und `proxy/`
Dateihashes von Installation und Staging vergleichen und bei einer Abweichung
abbrechen. `desktop/` und `notices/` zunächst in neue Nachbarordner der
Installation kopieren, die bisherigen Ordner in eindeutig benannte Backups
außerhalb von `app/` verschieben und die vorbereiteten Ordner als `app/desktop`
und `app/notices` einsetzen. Danach ausschließlich `build-info.json`,
`gate-status.json` und `SHA256SUMS` aus dem geprüften Paket übernehmen und im
installierten `app/` mit `sha256sum --check --quiet SHA256SUMS` prüfen. Launcher,
Service-Datei und private Daten werden bei diesem Desktopwechsel nicht berührt;
ein Dienstneustart ist nicht erforderlich.

## Installieren und starten

Archivprüfsumme vor dem Entpacken prüfen; danach den absoluten Paketpfad angeben:

```sh
/absolute/path/lain-linux-x64/install-linux.sh /absolute/path/lain-linux-x64
systemctl --user daemon-reload
systemctl --user start lain.service
~/.local/bin/lain status --json
~/.local/bin/lain ui
```

Der Installer startet oder aktiviert keinen Dienst. Optional aktiviert
`systemctl --user enable lain.service` den Start bei der Benutzeranmeldung.
Es wird kein Linger eingerichtet. Der Host läuft unabhängig vom GUI-Fenster,
jedoch nur innerhalb der Lebensdauer des Benutzer-Service-Managers.

Standardpfade:

| Zweck | Pfad |
| --- | --- |
| Launcher | `~/.local/bin/lain` |
| Desktop-Eintrag | `~/.local/share/applications/lain.desktop` |
| Originales Logo | `~/.local/share/icons/lain.png` |
| Private Daten | `~/.local/share/lain` (Modus 0700) |
| Programme | `~/.local/share/lain/app/{cli,host,desktop,proxy}` |
| Service | `~/.config/systemd/user/lain.service` |
| Host API | `http://127.0.0.1:18418` |
| Eigener Inference-Proxy | `http://127.0.0.1:18417` |

`lain` funktioniert aus jedem Arbeitsverzeichnis; `lain ui` startet die GUI,
alle anderen Argumente gehen unverändert an die CLI. `LAIN_HOME` überschreibt
das private Verzeichnis und muss absolut sein. Leerzeichen werden unterstützt;
Anführungszeichen, Backslashes, Prozentzeichen und Tabs in `LAIN_HOME` verweigert
der Installer vor jeder Änderung, da diese Pfade keine unterstützte systemd-
Programmpfadform sind. Beim Installieren wird der gewählte
Wert auch im Launcher und im Service festgehalten. Ein abweichendes
Installationspräfix als zweites Argument ändert den Ort von `bin/lain`.
Der aktuelle Installer legt zusätzlich `share/applications/lain.desktop` und
`share/icons/lain.png` unter diesem Präfix ab; `LAIN_DESKTOP_DATA_DIR` kann
den absoluten `share`-Pfad separat setzen. Der Desktop-Eintrag startet den
absoluten Launcher mit `ui`, unabhängig von `PATH` und Arbeitsverzeichnis.
Exec-Argumente und Desktop-Zeichenfolgen werden nach ihrer jeweiligen Syntax
maskiert. Das originale transparente Katzenbild wird unverändert kopiert;
der Desktop skaliert es für Menü und Taskleiste.
Im Anwendungsmenü erscheint **lain — Local AI Gateway**. Der CLI-Befehl heißt
weiterhin `lain`; bestehende Installationspfade ändern sich dadurch nicht.
`LAIN_HOME/desktop-integration.json` zeichnet die geschriebenen Desktop-/Icon-
Pfade und Hashes auf. Vorhandene fremde Dateien, Symlinks oder nachträglich
angepasste Desktop-Einträge/Icons werden vor jeder Installationsänderung
verweigert; eigene Anpassungen bleiben so erhalten.
`LAIN_SYSTEMD_DIR` wählt das Verzeichnis für die Service-Datei, ansonsten gilt
`$XDG_CONFIG_HOME/systemd/user` beziehungsweise `~/.config/systemd/user`.

Isolierter Installationstest ohne Systemänderungen oder Dienststart:

```sh
LAIN_HOME=/tmp/lain-test/data LAIN_SYSTEMD_DIR=/tmp/lain-test/units \
  ./scripts/install-linux.sh /absolute/path/lain-linux-x64 /tmp/lain-test/prefix
cd /tmp
/tmp/lain-test/prefix/bin/lain --help
```

Bestandsproxy, vorhandene Proxy-Queues, OAuth-Credentials und die normale
Codex-Konfiguration werden durch die Installation nicht verändert. Ein
späterer lesender Import und der separate Codex-Anschluss sind explizite
CLI-Aktionen. Der Skill wird mitgeliefert und nicht automatisch in die persönliche
Skillbibliothek kopiert.

## Aktualisieren und zurückwechseln

`systemctl --user stop lain.service`, dann neuen Paketordner mit denselben Pfaden
installieren, `daemon-reload` und `start` ausführen. Daten bleiben außerhalb von
`app`; die vorherige Anwendung liegt anschließend unter `app.previous`.
Vor einer weiteren Aktualisierung diese Sicherung bewusst archivieren oder
entfernen. Der Installer verweigert das Überschreiben eines fremden `app`-Ordners.
Für einen Rückwechsel den Dienst stoppen, `app` archivieren und `app.previous`
als `app` zurücksetzen. Eine Programm-Sicherung ersetzt keine Sicherung des
Datenverzeichnisses; bei Datenbankschemaänderungen passende Daten sichern.

Deinstallation: Dienst stoppen und deaktivieren, die ausgewiesene Service-Datei,
den Launcher, den eigenen Desktop-Eintrag `share/applications/lain.desktop`,
das eigene Icon `share/icons/lain.png` und den eigenen `app`-Ordner entfernen, anschließend
`systemctl --user daemon-reload`. Private Daten nur auf ausdrücklichen Wunsch
löschen. Das übergeordnete `LAIN_HOME` und `app.previous` bleiben erhalten;
kein rekursives Löschen von `~/.local/share/lain` ausführen. Bei individuellen
Pfaden die vom Installer ausgegebenen konkreten Dateien verwenden.
Desktop-Eintrag und Icon nur entfernen, wenn Pfad und aktueller SHA-256 zu
`desktop-integration.json` gehören; angepasste oder fremde Dateien behalten.

## Synthetisch prüfen

`./scripts/verify.sh` baut seriell und prüft Accounts, CLI/Client, Modelle,
Quota, SQLite-Usage, Installer und vorhandene Python-Unittests. Es führt keine
realen Anbieterproben aus. Optional starten `--core /absolute/lain-proxy` und
`--host /absolute/Lain.Host` zusätzlich synthetische Prozessprüfungen; der
Hosttest benötigt den freien eigenen Port 18418. Backendberichte liegen im
ausgegebenen temporären Verzeichnis. Diese Prüfungen ersetzen keine endgültige
Abnahme für den exakten Lieferstand.

Der isolierte Installer-Test `python3 packaging/test-install.py` prüft außerdem,
dass nicht unterstützte Pfade keine Installationsänderungen hinterlassen.
`LAIN_VERIFY_SYSTEMD=1 python3 packaging/test-install.py` prüft zusätzlich die
erzeugten Units für Standard- und Leerzeichenpfade mit `systemd-analyze --user
verify`, ohne Dienste zu starten oder zu ändern.
