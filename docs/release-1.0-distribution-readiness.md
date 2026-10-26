# lain 1.0.0 – öffentliche Lieferform und Linux-Betrieb

Stand: 26.10.2026. Analyse der gemeinsamen Arbeitskopie, Ausgangs-HEAD
`fef478f601f46b315939fa53f02caa9791086f1d` mit umfangreichem parallelem WIP.
Kein Veröffentlichungs-, Installations- oder Abnahmebeleg. In diesem Auftrag
wurden ausschließlich Quellcode, Paketmetadaten und Dokumente gelesen; keine
Credentials, Datenbanken oder privaten Laufzeitbestände. Keine Builds, schweren
Tests, Diensteingriffe oder Commits. Diese Datei ist die einzige eigene Änderung.

## Ergebnis und konkrete Blocker

Die vorhandene Liefertechnik trägt ein Linux-x64-Archiv mit Installer,
Prüfsummen, Komponentenprovenienz und Drittanbieter-Notices. Eine öffentliche
1.0.0-Lieferung ist durch die vorliegenden Nachweise allein noch nicht gedeckt:

1. **Eigene Lizenz inzwischen entschieden:** Der Nutzer hat Artistic License2.0
   gewählt. Root-`LICENSE` enthält den unveränderten SPDX-Referenztext,
   `NOTICE` grenzt eigene Bestandteile von Drittmaterial ab. Der Publisher
   nimmt beide Texte und ein Gitarchiv der exakten Source auf. Die bereits
   gebauten r4-Pakete enthalten diese Ergänzung noch nicht; final neu bauen.
2. **Artwork inzwischen bestätigt:** Der Nutzer bestätigt öffentliche Nutzung
   des Fotos seines Freundes Alexander Huxel · Webentwicklung Huxel. Attribution
   mit <https://flickr.com/people/204852984@N05/> steht in `NOTICE`,
   [Brand-Herkunft](../src/Lain.Desktop/Assets/Brand/README.md) und der neuen
   About-Source. Das dokumentiert die übermittelte Freigabe für Lain, keine
   allgemeine Artistic-Lizenz des Fotos oder separate Kontaktaufnahme zum Urheber.
3. **Exaktes finales Notice-Paket noch zu prüfen:** Die Collector-Skripte liefern
   Belege, ausdrücklich keine abgeschlossene Complianceprüfung. Native und
   verschachtelte Drittanteile sowie die Übereinstimmung mit den finalen Bytes
   bleiben Prüfgegenstand. Ein leerer Go-`missingEvidence`-Wert deckt nur den
   vom Collector beschriebenen Umfang ab.
4. **Öffentliche Client-Lieferung offen:** Codex-Patchbuild und veränderte
   VS-Code-Erweiterung sind separate Artefakte. Ihre lokale Qualifikation und
   erhaltene Lizenzdateien belegen keine abgeschlossene öffentliche
   Redistributionsprüfung. Insbesondere Codex-Runtime-Ressourcen enthalten
   LGPL-2.1-Texte; die betroffenen Bibliotheken und Erfüllung ihrer konkreten
   Bedingungen müssen vor einer Binärweitergabe zugeordnet werden.
5. **Finaler Releasebeleg fehlt in diesem Auftrag:** Der Publisher setzt
   `releaseQualified: false` auch mit positivem Backendbeleg. Installierte
   Gesamtversion, reale Modellmatrix, Client-/Skillsabnahme und abschließende
   Releasechecks liegen bei den anderen Arbeitspaketen. Umfangreiches WIP in
   eingefrorenen Buildpfaden blockiert derzeit einen Publish aus genau dieser
   Arbeitskopie; es wird hier weder bereinigt noch committed.

Das sind Nachweis- und Produktentscheidungen, keine neu eingeführten
Pflichtgenehmigungen. Öffentlicher Downloadort, unterstützte Distributionen,
Vertrauensanker für Downloads und Umfang optionaler Clients sind noch festzulegen.

## Belegte Komponenten und Weitergabebedingungen

| Bestandteil | Beleg im Repository | Feststellung / Konsequenz |
| --- | --- | --- |
| Eigener Host, CLI, Clientbibliothek, Coreverträge, Desktop und Skills | `src/*/*.csproj`, `skills/`, [LICENSE](../LICENSE), [NOTICE](../NOTICE) | Artistic-2.0 vom Nutzer gewählt. CLI bindet aktuell lain-usage, lain-workers, lain-connect und lain-doctor als Ressourcen ein. |
| CLIProxyAPI v8.0.41 plus eigener Adapter und Patches | [go.mod](../proxy/adapter/go.mod), [Quellmanifest](../proxy/source-manifest.json), [Upstream-Lizenz](../proxy/LICENSE.upstream) | Upstream MIT: Copyright- und Erlaubnistext bei Kopien/wesentlichen Teilen erhalten. Manifest bindet Archiv, Patches und Buildinputs. MIT des Upstreams ist keine Lizenzentscheidung für eigene Ergänzungen. |
| Avalonia.Desktop / Avalonia.Fonts.Inter 12.1.4, Semi 12.1.0.1, Ursa 2.2.0, Irihi.Shared 0.5.0 | [Desktopprojekt](../src/Lain.Desktop/Lain.Desktop.csproj), [Lockdatei](../src/Lain.Desktop/packages.lock.json), `packaging/licenses/` | Gepinnte Framework-Lizenztexte sind MIT, Copyright und Erlaubnistext erhalten. Der Avalonia-Frameworktext ersetzt nicht die Font- und Native-Notices. |
| Microsoft.Data.Sqlite 10.0.12, YamlDotNet 16.3.0; SQLitePCLRaw 2.1.12 transitiv | [Hostprojekt](../src/Lain.Host/Lain.Host.csproj), [Host-Lockdatei](../src/Lain.Host/packages.lock.json) | SQLitePCLRaw-Lizenztext ist Apache-2.0: Lizenz mitgeben, relevante Hinweise/NOTICE erhalten, Änderungen kennzeichnen. Wrapper, native SQLite-Bibliothek und weitere Pakete getrennt inventarisieren; keine pauschale Gleichsetzung mit SQLite public domain. Exakte weitere Lizenzmetadaten kommen aus den nuspecs. |
| SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3 und native Assets | Desktop-Lockdatei | Tatsächliche transitive Komponenten; C#-Paketlizenz allein deckt die Attribution nativer Unterkomponenten nicht nachweislich vollständig ab. Finale Notice-Dateien prüfen. Andere Plattformen im Restoregraph sind nicht automatisch ausgelieferte Linux-Binaries. |
| Self-contained .NET / ASP.NET Core | [Publisher](../scripts/publish-linux.sh), `global.json`, `packaging/licenses/sources.json` | SDK 10.0.111 gepinnt. Offizielle Runtime-Lizenz und Third-Party-Notices für 10.0.11 vorhanden; Hauptlizenz MIT, Drittanteile separat. Desktop-Restoremetadaten nennen hier 10.0.11-Runtimedownloads. Microsoft.Data.Sqlite 10.0.12 beweist kein Runtimeupgrade auf 10.0.12. Finale Runtimeversion je Publish prüfen. |
| Oxanium und IBM Plex Sans Condensed | `src/Lain.Desktop/Assets/Fonts/*/OFL.txt`, Desktop-Ressourcen | SIL OFL 1.1: Copyright/OFL erhalten, Fonts nicht für sich allein verkaufen; bei Änderungen Reserved Font Names beachten. Collector kopiert Familien-OFLs und inventarisiert Font-/Noticehashes. Inter zusätzlich über NuGet prüfen. |
| Go-Module und Go-Laufzeit im Gateway | [Go-Collector](../scripts/collect-go-notices.py), Adapter-go.mod | Tatsächlich verlinkte Module werden über `go version -m` ermittelt, Versionen/Checksummen und Root-Notices aus Cache bzw. geprüftem Upstream erfasst. Toolchain-Lizenz wird mitgenommen. go.mod allein ist kein endgültiges Binärinventar. |
| Separater Codex 0.164.0 mit drei Patches | [Upstream-Pin](../clients/codex/upstream.json), [Buildskript](../scripts/build-codex-picker.sh), `scripts/clients/install_codex_dispatch.py` | Build erzeugt codex und codex-code-mode-host; Dispatcher übernimmt zusätzliche Runtime-Dateien mit Hashbindung. Ressourcenliste enthält voice/NOTICE und LGPL-2.1, Opus, PCRE2, libffi, proxy-libintl, sljit und zlib. Kein vollständiger Codex-/Cargo-Lizenzsatz in den Repo-Lizenzdateien nachgewiesen; keine pauschale Freigabe behauptet. |
| Separat veränderte VS-Code-Erweiterung | [Wartungsvertrag](../clients/vscode/maintenance.md) | Vollständige fremde Bundles bleiben außerhalb des Repos; Ressourcen-/Lizenzhashes bleiben geschützt. Erhaltung ist Integritätsbeleg, keine bestätigte öffentliche Änderungs-/Weitergabeerlaubnis. |

Die Originaltexte und Herkunfts-URLs stehen in
[packaging/licenses/sources.json](../packaging/licenses/sources.json).
Dort liegt auch ein historischer Avalonia-11-Text: seine Anwesenheit bedeutet
nicht, dass Avalonia 11 aktuell verwendet wird. MIT erlaubt Weitergabe unter
Erhalt der Hinweise; Apache-2.0 enthält zusätzlich Patentregelungen und gewährt
keine allgemeine Markenlizenz. Providerzugang und API-Nutzung bleiben den
jeweiligen Dienstbedingungen unterworfen; ein Adapter verteilt keine
Modellgewichte und belegt keine Nutzungsrechte an fremden Accounts.

## Lieferform aus vorhandenen Skripten

[Publish](../scripts/publish-linux.sh) unterstützt `--mode monitor` und
`--mode gateway` (Standard). Beide enthalten CLI, Host, Desktop, Skills,
Installer und Notices; Monitor lässt Core/Go-Inventar weg. Gateway ergänzt
Corebinary, Lizenz, Katalog, Quell-/Buildmanifeste und Adapter-/Patchprovenienz.
Der Modus ist in `package-metadata.json` und `build-info.json` dokumentiert.
Eine bestehende Konfiguration dominiert den Initialmodus des frischen Pakets.

Der Publisher verlangt neue absolute Ausgabepfade, unveränderte eingefrorene
Inputs einschließlich `src`, `scripts`, `packaging`, `skills` und bei Gateway
`proxy`, exaktes SDK und Locked Restore. Er erzeugt `.tar.gz`,
`.tar.gz.sha256`, internes `SHA256SUMS`, NuGet-/Font- und gegebenenfalls
Go-Inventare. Ein Backendqualifikationsbeleg muss die exakten Binary- und
Quellmanifesthashes treffen; die Gesamtfreigabe wird nicht erfunden.

Geeignete öffentliche Lieferform als **Vorschlag**, noch keine Entscheidung:
versioniertes Linux-x64-Tararchiv pro angebotenem Modus samt Checksumme,
Versions-/Quellreferenz, finalem Abnahmebeleg, vollständigem Notice-Verzeichnis
und dieser Betriebsanleitung. Der aktualisierte Publisher kopiert `LICENSE`
und `NOTICE` und liefert `source/lain-source.tar.gz` aus dem exakten Commit mit;
dadurch ist Source direkt im neuen Paket erreichbar. Diese Änderung ist noch
nicht Bestandteil der historischen r4-Archive. Clients könnten separat oder als
Anleitung für einen eigenen Build geliefert werden; die Entscheidung bleibt offen.

Prüfsummen erkennen Beschädigungen, authentifizieren aber keinen Absender,
wenn Archiv und Hash aus derselben ungesicherten Quelle stammen. Einen
bestätigten Downloadkanal oder Signaturmechanismus festlegen; das Repo belegt
hier keinen fertigen öffentlichen Releasekanal. Der Publisher wurde anschließend
so korrigiert, dass neue Archiv-Hashdateien nur den Dateinamen enthalten.
Ein synthetischer Relokationscheck mit Leerzeichen in Build-/Downloadpfaden
bestand mit `sha256sum --check`; Shellsyntax geprüft. Bereits gebaute r4-Archive
wurden dadurch nicht verändert, ein neues Paket enthält die Änderung erst nach
seinem eigenen gebundenen Build.

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
sha256sum lain-1.0.0-linux-x64-gateway.tar.gz
# Mit dem bestätigten Releasehash vergleichen, dann entpacken.
tar -xzf lain-1.0.0-linux-x64-gateway.tar.gz
package="$HOME/Downloads/lain-1.0.0-linux-x64-gateway"
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

## Abschlusskriterien für den Integrator

- Artistic-2.0 samt Source und bestätigte Fotoattribution im finalen Paket
  prüfen; öffentlichen Client-Lieferumfang entscheiden.
- Finale NuGet-/Runtime-, Font-, native und tatsächlich verlinkte Go-Inventare
  auf die gelieferten Bytes beziehen; verschachtelte Notices und mögliche
  LGPL-Bestandteile eines separaten Clientbundles prüfen.
- Frischen eingefrorenen Stand, Paket-/Installations-/Update-/Recoveryabnahme,
  Client-/Skillsabnahme und reale Modellmatrix aus den zuständigen Arbeitspaketen
  zusammenführen; historische lokale Erfolge nicht zur öffentlichen 1.0.0 erklären.
- Öffentlichen Kanal, portable Hashdateinamen, Quellzugang, unterstützte
  Linuxumgebungen und konkrete Recoverybedienung dokumentieren. Dieser Bericht
  veröffentlicht nichts und verändert keine dieser Entscheidungen.
