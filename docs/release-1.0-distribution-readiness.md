# Lain 1.0.0 – Lieferform und Linux-Betrieb

Stand: 26.10.2026. Geprüfter Kandidat: `882bdadd09235b35ca74c0ebdcff59e00061b2fe`, eingefrorene Source unter `/home/tomka/.cache/lain-release-1.0-source-20261026`.
Die Main-Arbeitskopie enthält fremdes WIP und ist nicht die Buildquelle.
Die folgenden Belege gelten für die exakten r4-Pakete; spätere Begleitdokumente
ändern deren Sourcearchiv und Qualifikationsbindung nicht.

## Lieferumfang und Freigabestand

Öffentliche Lieferform: je ein Linux-x64-Tararchiv für **Monitor** (Host,
CLI, Desktop, Skills, ohne Inferenz-Core) und **Gateway** (zusätzlich eigener
CLIProxyAPI-Build), jeweils mit externer SHA-256-Datei. Enthalten sind
`LICENSE`, `NOTICE`, `notices/`, `SHA256SUMS`, Installer, Wartungshelfer,
Build-/Quellprovenienz und `source/lain-source.tar.gz` mit der exakten Source.
Kein modifizierter Codex-Binärclient und keine VSIX werden mitgeliefert.
Bestandskonfiguration hat Vorrang vor dem Initialmodus. Ein Gatewaybestand
kann nicht durch Installation des Monitorpakets stillschweigend reduziert werden.

Die neuen Pakete liegen unter `/home/tomka/.cache/lain-release-candidate-r4-20261026/`:

| Datei | SHA-256 |
| --- | --- |
| `package-monitor.tar.gz` | `9dabf8e1913f72c39bf337b50d42b9390479cf7f5ed25be72e78ba6bc5127e93` |
| `package-gateway.tar.gz` | `55a013bb1e35096e8cc2b8474182d24adf6c93cdb00008704428fa9c3ffce243` |

Die gleichnamigen `.sha256`-Dateien referenzieren portable Dateinamen.
Die Archive behalten beim Entpacken die Ordner `package-monitor` bzw.
`package-gateway`. Bei Umbenennung für einen Download muss die äußere
Hashdatei angepasst werden; Archivinhalt und innere Prüfsummen bleiben erhalten.

**Noch kein öffentlich freigegebener Download.** Core, elf isolierte
Vollpaketfälle, native Worker und Drain sowie die native Desktopmatrix sind
geprüft: acht gepflegte UI-Läufe und zwei vollständige Defaultläufe mit je
698 Checks. Noticekorrekturen sind im neu gebauten r4-Paket enthalten;
[Packagingreview](../packaging/licenses/review-20261026.md).
`releaseQualified: false` bleibt bestehen: Der lokale Dienst läuft weiterhin
auf `ff5771c`, Revision58/57. Der eigene Updateversuch brach vor Dienststopp
wegen aktiver Requests ab; Admission ist freigegeben. Der Nutzer möchte die
laufende Recherche erhalten. Update, Apply/Katalogexport, Skillaktivierung und
reale finale Modellmatrix warten auf ein ruhiges Zeitfenster.
[Aktueller Ausführungsstand](release-1.0-execution-2026-10-26.md).

## Download und Source

Bei Veröffentlichung gehören beide Archive mit ihren Hashdateien, diese
Anleitung, [Release Notes](release-1.0-release-notes.md) und Noticeprüfung in einen
versionierten Releaseeintrag. Ein bestätigter öffentlicher HTTPS-Downloadkanal
oder Signatur-Vertrauensanker ist noch nicht vorhanden. Keine Download-URL erfinden.
Hashdateien schützen Integrität; aus derselben Quelle bezogene Hashes allein
beweisen keine Herausgeberidentität. Die untenstehenden Befehle gelten nach
Bezug aus dem bestätigten Releasekanal.

```sh
cd "$HOME/Downloads"
sha256sum --check package-gateway.tar.gz.sha256
# Für Monitor stattdessen package-monitor.tar.gz.sha256 verwenden.
```

Eigene Source steht unter Artistic-2.0, Fremdanteile unter ihren jeweiligen
Lizenzen. Die Fotofreigabe gilt für Lain einschließlich bearbeitetem Logo;
Attribution: Alexander Huxel · [Webentwicklung Huxel](https://webentwicklung-huxel.de/)
([Flickr](https://flickr.com/people/204852984@N05/)). Keine allgemeine Fotolizenz.
Das Sourcearchiv enthält gepinnte Fremdquellenreferenzen und Patchinputs,
keinen vollständigen Offlinecache aller NuGet-/Go-/Rust-Abhängigkeiten oder
separaten Upstreamarchive. Rebuild erfordert die jeweiligen Downloads.

## Optionale Clients: Source-/Patchlieferung

Der öffentliche Client-Lieferumfang besteht ausschließlich aus den im
Sourcearchiv enthaltenen eigenen Patches, Transformern, Pins und Anleitungen.
Lokale Clientqualifikation erteilt keine allgemeine Binär-Redistributionsfreigabe.
Unveränderte Clients beziehen Nutzer selbst über den jeweiligen offiziellen
Anbieterkanal. Gruppenmenü/native Usage-Erweiterungen benötigen den passenden
lokalen Patchstand; gewöhnliche API-Kompatibilität garantiert diese UI nicht.

Für Codex: Source in einen neuen Ordner entpacken, dort Pins in
`clients/codex/upstream.json` sowie `clients/codex/README.md` prüfen. Python 3,
Git und die Upstream-Buildabhängigkeiten sowie exakt Rust 1.99.1 bereitstellen.
Die nachfolgenden Schritte bauen lokal und installieren nichts:

```sh
python3 scripts/prepare-codex-picker.py --fetch --output /absolute/new/codex-source
scripts/build-codex-picker.sh /absolute/new/codex-source
```

Der Preparer bindet Upstream 0.164.0, Archiv-/Patchhashes und Lockdatei;
der Builder verwendet `--locked`, einen Job und baut **codex sowie
codex-code-mode-host**. Ein nackter CLI-Binarytausch ist kein vollständiges
Runtimepaket. Für flaglosen Daemonbetrieb sind zusätzlich die passenden,
selbst bezogenen Upstream-Helfer/Ressourcen nötig; Zusammenstellung und
Hashbindung nach `clients/codex/dispatcher.md` und `maintenance.md` prüfen.
Keine Ressourcen aus einer fremden lokalen Installation öffentlich hochladen.
Die darin vorhandenen LGPL-2.1-Texte sind kein Beleg für erfüllte Bedingungen
aller betroffenen Runtimebibliotheken. Native Abnahme gilt nur für die exakt
qualifizierten Bytes, nicht automatisch für einen eigenen Rebuild.

Für VS Code: Original `openai.chatgpt` 26.1021.22027 Linux x64 selbst vom
Anbieter beziehen. Nur eigener Transformer wird geliefert; kein Originalbundle
und keine modifizierte VSIX. Python 3.11+, vollständigen vertrauenswürdigen
Original-Hashmanifest und die festen Pins des Helpers verwenden:

```sh
python3 scripts/clients/vscode_groups.py prepare \
  --source /absolute/original-extension \
  --input-manifest /absolute/reviewed-input.json \
  --bundle /absolute/new-local-bundle
```

Ziel ist die lokale Version 26.1021.22028. Vollständiges Manifestformat,
Qualifikation, Installationsplan und Rollback stehen in
`clients/vscode/maintenance.md`. Ohne vollständiges passendes Original und
unabhängige Pins abbrechen; keine beliebige aktuelle Marketplaceversion anpassen.
Erzeugte Bundles/VSIX bleiben lokal. Installation verlangt exakte native
Qualifikation; Fensterreload erfolgt bewusst durch den Nutzer. Vor lokaler
Anpassung gelten weiterhin die Bedingungen des Originalanbieters; diese
Anleitung behauptet keine bestätigte Änderungs-/Weitergabeerlaubnis.

## Linux: Neuinstallation

Voraussetzungen: Linux x86_64 mit kompatiblen nativen Laufzeitbibliotheken,
Bash, Python 3, tar/gzip und sha256sum; für den dokumentierten Dienstbetrieb
systemd als Benutzer-Service-Manager. Desktop benötigt eine grafische Sitzung
und Avalonia-Native-Abhängigkeiten, unter anderem fontconfig/X11. Eine konkret
abgenommene Distributions-/Paketliste ist noch nicht belegt. .NET SDK und Go
sind für das fertige Self-contained-Paket nicht erforderlich.

Nach Erhalt eines final geprüften Archivs (Dateiname und Verzeichnis sind
Beispiele, nicht vorhandene öffentliche Downloads):

```sh
cd "$HOME/Downloads"
sha256sum package-gateway.tar.gz
# Mit dem bestätigten Releasehash vergleichen, dann entpacken.
tar -xzf package-gateway.tar.gz
package="$HOME/Downloads/package-gateway"
(cd "$package" && sha256sum --check SHA256SUMS)
bash "$package/install-linux.sh" "$package"
systemctl --user daemon-reload
systemctl --user start lain.service
"$HOME/.local/bin/lain" status --json
"$HOME/.local/bin/lain" ui
```

Der Ordnername muss dem tatsächlichen Archivinhalt entsprechen. Installer
prüft selbst sämtliche inventarisierten Paketdateien und verweigert zusätzliche
Dateien, Symlinks, Pfadtraversal und fremde Desktopintegration. Er startet oder
aktiviert keinen Dienst. Optional: `systemctl --user enable lain.service` für
den Start bei Benutzeranmeldung; Linger wird nicht eingerichtet.

Standard: Daten und App unter `~/.local/share/lain`, Launcher
`~/.local/bin/lain`, Unit `~/.config/systemd/user/lain.service`, Desktopdatei
`~/.local/share/applications/lain.desktop`, Icon `~/.local/share/icons/lain.png`.
`LAIN_HOME`, `LAIN_SYSTEMD_DIR`, `LAIN_DESKTOP_DATA_DIR` und das zweite
Installerargument für das Präfix erlauben andere absolute Pfade. Für Updates
dieselben Werte verwenden. Symlink-Pfade sind ausgeschlossen; LAIN_HOME darf
keine Anführungszeichen, Backslashes, Prozentzeichen oder Tabs enthalten.
Mitgelieferte Skills werden nicht automatisch in persönliche Skillverzeichnisse
installiert. Das Archiv enthält keinen automatisch installierten Codex-Patchclient.

## Linux: Update und Sicherung

Für ein vollständiges Offlineupdate zuerst Clients/Worker beenden, alle
Aufträge abschließen und Desktop/Tray schließen. Der Installer selbst drainiert
keine Anfragen und prüft keinen gestoppten Dienst. Erst dann:

```sh
systemctl --user stop lain.service
systemctl --user is-active lain.service
# Erwartet inactive; auch separat gestartete Host-/Coreprozesse müssen beendet sein.
```

Anschließend mit dem Wartungshelfer aus einem geprüften Paket sichern. Die
folgenden Variablen bezeichnen absolute, individuell zu wählende Pfade; der
Snapshot darf noch nicht existieren und muss außerhalb von LAIN_HOME liegen:

```sh
lain_state="$HOME/.local/share/lain"
old_package="$lain_state/app"
snapshot="$HOME/lain-backup-before-1.0.0"
python3 "$old_package/packaging/lain-maintenance.py" \
  backup "$lain_state" "$snapshot" --stopped
```

Das ist ein vollständiger privater Dateisnapshot einschließlich App und
Zugangsdaten, kein Export für Support oder öffentliche Releasebelege. Die
Befehle werden hier nur dokumentiert, nicht ausgeführt. Der Helfer prüft
Eigentum, Dateitypen, Hashes und unveränderte Bestände. Launcher, Unit und
Desktopintegration außerhalb LAIN_HOME gehören nicht zum Snapshot und müssen
für eine vollständige Wiederherstellung separat erhalten bleiben.

Ein vorhandenes `app.previous` vor dem nächsten Update außerhalb der Installation
archivieren; der Installer überschreibt es nicht. Das neue Archiv wie oben
prüfen und entpacken, `package` auf dessen absoluten Ordner setzen:

```sh
bash "$package/install-linux.sh" "$package"
systemctl --user daemon-reload
systemctl --user start lain.service
"$HOME/.local/bin/lain" status --json
```

Die vorherige App liegt danach in `app.previous`; Daten außerhalb `app` bleiben
bestehen. Nach dem Start Version/Modus und die für diesen Release vorgesehenen
Workflows prüfen. Ein erfolgreicher Installer ist kein Modell-/Clienttest.

Für kontrollierte Gatewayupdates mit belegter Drain-/Prozess-/Generationsprüfung
existiert zusätzlich [service-update.py](../scripts/service-update.py):

```sh
python3 scripts/service-update.py \
  --inputs /absolute/reviewed-update-inputs.json \
  --inputs-sha256 EXPECTED_INPUT_SHA256
# Nach erfolgreicher Planung dieselben gebundenen Inputs mit --execute verwenden.
```

Dieses Werkzeug benötigt installationsspezifische, hashgebundene Inputs und
Qualifikationsbelege. Es ist kein universeller öffentlicher Einzeiler; der
Publisher nimmt es derzeit nicht als eigenständigen Endnutzer-Updater ins Paket
auf. Ein generierter öffentlicher Update-Feed ist damit nicht nachgewiesen.

## Linux: Recovery

Ein Binarytausch über `app.previous` beweist weder Schemaverträglichkeit noch
Gültigkeit rotierender OAuth-Grants. Nach einem fehlgeschlagenen Update Dienst
stoppen und aus lassen, Desktop/Clients beenden und den Fehlerbestand erhalten.
Nicht blind alte Programme gegen bereits migrierte Daten starten.

Den vollständigen Snapshot ausschließlich in ein **neues** Verzeichnis
zurückspielen. Den Helfer aus dem separat behaltenen, geprüften Paket verwenden;
Elternverzeichnis des Recoveryziels muss existieren:

```sh
recovery="$HOME/lain-recovery-1.0.0"
python3 "$package/packaging/lain-maintenance.py" \
  restore "$snapshot" "$recovery" --stopped
```

Restore überschreibt keinen bestehenden Bestand und schreibt
`restore-requires-reconnect`. Den Marker nicht manuell löschen. Vor einem
Gatewaystart müssen rotierende Credentials ausdrücklich neu verbunden oder
über die implementierte Generations-Recovery mit einer verifizierten aktuellen
Generation abgeglichen werden. Alte Refresh-Tokens können serverseitig bereits
verbraucht sein. Der Host hat dafür eine Quarantäne; der Dateirestore allein
belegt keine erfolgreiche Konto-Recovery.

Der zusätzliche Repohelfer [service-recovery.py](../scripts/service-recovery.py)
verifiziert gepinnte Snapshot-/Helferhashes und plant standardmäßig:

```sh
python3 scripts/service-recovery.py \
  --snapshot /absolute/private-snapshot \
  --snapshot-manifest-sha256 EXPECTED_SNAPSHOT_MANIFEST_SHA256 \
  --helper-sha256 EXPECTED_MAINTENANCE_HELPER_SHA256 \
  --destination /absolute/new-recovery-home
# Erst für den bewusst gewählten Restore ergänzen: --execute --stopped
```

Er ersetzt keine Installation, schaltet keine Unit um und startet keinen
Backendprozess. Erst mit snapshotkompatibler App, abgeschlossener Credential-
Recovery und geprüften Konten/History/Routen auf das Recoveryhome umstellen.
Der konkrete produktionsreife Nutzerablauf für diese Konto-Recovery bleibt in
der Endabnahme nachzuweisen; hier wird kein erfundener CLI-Befehl angegeben.
Eine quarantänisierte Sicherung nicht mit einem historischen ungeschützten Host
starten. Siehe [Recoveryvertrag](linux-delivery-stabilization.md).

## Linux: Entfernen

Zuerst alle Aufträge abschließen, Clients/Worker und Desktop/Tray schließen.
Bei Standardinstallation anschließend:

```sh
systemctl --user disable --now lain.service
rm -- "$HOME/.config/systemd/user/lain.service"
systemctl --user daemon-reload
rm -- "$HOME/.local/bin/lain" \
  "$HOME/.local/share/applications/lain.desktop" \
  "$HOME/.local/share/icons/lain.png"
```

Vor dem Entfernen prüfen, dass diese Dateien zur eigenen Laininstallation
gehören. Bei abweichenden Installationspfaden die tatsächlich verwendeten Werte
für Präfix, LAIN_SYSTEMD_DIR und LAIN_DESKTOP_DATA_DIR verwenden. Separat
laufende Host-/Coreprozesse müssen ebenfalls geordnet beendet sein.
Daten unter `~/.local/share/lain` bleiben dabei erhalten. Nach gestoppter,
privater Sicherung kann dieses Verzeichnis samt `app`/`app.previous` bewusst
archiviert oder entfernt werden; es enthält private Accounts und History.
Kein automatischer Purge und kein angeblicher Uninstaller. Separat installierte
Skills, eigene Codexclients, VS-Code-Anpassungen und Providerzugänge werden
hierdurch nicht entfernt oder widerrufen. Für VS Code den dokumentierten
qualifizierten Rollback verwenden; für Codex den eigenen Dispatcher nach dessen
Installationsbeleg zurücknehmen. Eine Dateilöschung widerruft keine Providergrants.

## Vor öffentlichem Upload

Repoöffentlichkeit ist vom Nutzer beauftragt; ein öffentlicher 1.0-Release mit
Tag und Downloads ist dadurch noch nicht veröffentlicht. Getrackte Historie,
Kandidat und tatsächlicher Remotezustand werden vor dem Umschalten geprüft.
Neue Begleitdokumente und CI-Verträge müssen auf einem getrennten sauberen
Veröffentlichungsstand liegen; die laufende Main-Arbeitskopie bleibt erhalten.
Die r4-Archive bleiben an Source882 gebunden. Gesamtfreigabe und tatsächliche
Installation/Modellabnahme gesondert belegen; keine macOS-/Windowsproduktfreigabe
von Installerfixtures ableiten.
