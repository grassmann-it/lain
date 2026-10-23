# Linux-Auslieferung

Erster Zielstand: Linux x64, .NET SDK **10.0.111**, Self-contained Host, CLI und
Avalonia Desktop. Der Build verwendet das `global.json`, explizite NuGet-Versionen
und eingecheckte `packages.lock.json`; Locked Restore verweigert Änderungen am
Abhängigkeitsgraph. Die Desktop-Laufzeit benötigt ein X11-/Wayland-Desktop-System
mit den für Avalonia üblichen nativen Bibliotheken (insbesondere fontconfig/X11).
Der gebündelte Proxy ist der verifizierte eigene Build aus `proxy/core`, kein
installierter Bestandsproxy. Seine Herkunft und Hashes liegen im Paket.

## Paket bauen

Vom Checkout aus, mit .NET SDK und der vom Proxy-Build geforderten Go-Version:

```sh
./scripts/publish-linux.sh /absolute/path/lain-linux-x64
```

Der Zielordner muss neu sein. Das Skript veröffentlicht `cli/Lain.Cli`,
`host/Lain.Host`, `desktop/Lain.Desktop` und `proxy/lain-proxy`, Lizenz,
Quellmanifeste, CLI-Skill und Installer. `SHA256SUMS` schützt die Paketdateien;
neben dem Ordner entstehen `.tar.gz` und dessen Prüfsumme. Das Archiv verwendet
sortierte Dateinamen, feste Eigentümer und Commitzeit (`SOURCE_DATE_EPOCH`
überschreibt diese). .NET baut deterministisch mit normalisierten Quellpfaden.
`build-info.json` zeichnet Commit, SDK und uncommittierte Änderungen auf.
Für einen vergleichbaren Releasebuild einen sauberen Commit und dieselben
Toolchains verwenden. Die Manifestprüfung allein ersetzt die Backend-Abnahme
nach `docs/execution.md` nicht.

Falls der Lockstand bewusst aktualisiert werden muss:

```sh
dotnet restore Lain.sln -r linux-x64 -p:SelfContained=true
```

Die Änderungen an allen Lockdateien prüfen und einchecken, dann erneut publizieren.
Die Release-Pipeline aktualisiert Lockdateien niemals automatisch.

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
| Private Daten | `~/.local/share/lain` (Modus 0700) |
| Programme | `~/.local/share/lain/app/{cli,host,desktop,proxy}` |
| Service | `~/.config/systemd/user/lain.service` |
| Host API | `http://127.0.0.1:18418` |
| Eigener Inference-Proxy | `http://127.0.0.1:18417` |

`lain` funktioniert aus jedem Arbeitsverzeichnis; `lain ui` startet die GUI,
alle anderen Argumente gehen unverändert an die CLI. `LAIN_HOME` überschreibt
das private Verzeichnis und muss absolut sein. Beim Installieren wird der gewählte
Wert auch im Launcher und im Service festgehalten. Ein abweichendes
Installationspräfix als zweites Argument ändert nur den Ort von `bin/lain`.
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
den Launcher und den eigenen `app`-Ordner entfernen, anschließend
`systemctl --user daemon-reload`. Private Daten nur auf ausdrücklichen Wunsch
löschen.
