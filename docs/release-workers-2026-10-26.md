# Worker-v1 und Codex-Reparaturen, 26.10.2026

**Abgeschlossen:** Projektregeln und begrenzte Worker sind implementiert und
geprüft; Lain-Host/CLI/Desktop daraus noch nicht installiert. Der separat
reparierte Codexlauncher ist lokal installiert: `codex` und `codex --profile lain`
zeigen beide die Modellgruppen. Alle 15 nativen Menüfälle, zwei native Starts
durch den tatsächlich installierten Launcher und elf abschließende
Offlineprüfkommandos bestanden. Keine öffentliche Veröffentlichung.

Nachfolgende Quellerweiterung mit eigener Abnahme:
[expliziter Worker-Kontext und Providerreparaturen](provider-tool-context-repair-2026-10-26.md).
Sie ergänzt diesen Stand um `contextMode:native` und getrennte Ergebnisprüfung;
eine neue produktive Host-/CLI-/Coreinstallation ist daraus nicht erfolgt.

## Vorschlag für das nächste lokale Update

Auftrag dieser Fortsetzung: Architektur-Sparring „was müssen wir ready machen und
installieren“. Die folgende Lieferreihenfolge ist ein Vorschlag für den nächsten
gemeinsamen Stand, kein bereits ausgeführter Installationslauf und keine neue
öffentliche Releasefreigabe. Die bisherigen Implementierungsaufträge und ihre
Belege bleiben gültig. Neue Produktentscheidungen werden daraus nicht abgeleitet.

Ausgangsaufnahme: `main` bei `fef478f601f46b315939fa53f02caa9791086f1d`,
umfangreiches bestehendes WIP (34 veränderte und 53 unversionierte Statuseinträge,
teilweise ganze Verzeichnisse). Produktiv läuft weiterhin `ff5771c`, aktives
Profil `lain`, Revision/applied 57/57, Gateway bereit. Einzelne geprüfte Dateien
oder Kandidaten sind noch kein gemeinsamer ausgelieferter Stand.

| Paket | Ergebnis und Abnahme | Abhängigkeit |
| --- | --- | --- |
| A: Lieferstand festlegen | Bewusst abgegrenzter, eingecheckter Kandidat mit Worker-/Kontext-/Reasoningexport und Cohere-Coreänderung sowie benötigten gemeinsamen Änderungen. Vorhandenes WIP inventarisieren und erhalten; kein pauschales Committen, Stashen oder Bereinigen. Quelle, Locks, Coremanifest und Clientpins stimmen überein | Vor dem endgültigen Paketbuild |
| B: Updateprüfung klären | Ursache von `checks.ownServiceIdentity` am nicht lesbaren externen Docker-ssh-agent aufklären. Eigene Dienstidentität im vorgesehenen Testaufbau positiv nachweisen; fremde/unbekannte Prozesse nicht pauschal als irrelevant überspringen. Neue vollständige gültige Qualifikation statt Umbenennen von `NOT-VALID` | Analyse unabhängig von A; finaler Nachweis am Kandidaten aus A |
| C: Gemeinsames Paket qualifizieren | Frisches Gatewaypaket mit Host, CLI, Desktop und neuem Core bauen. Passende Offlinechecks und echte native Abnahme gegen exakt die ausgelieferten Bytes: Modell-/Reasoningwechsel, Auto-Router, begrenzte Worker mit inherit/native, Read-only und Schreibworktree. Install-/Update-/Bestanderhaltung nachweisen | A und gültiger Updateweg aus B |
| D: Lokal installieren | Frischen Bestand und externe Clientartefakte erfassen, nötigen Dienststopp samt vollständiger privater Sicherung und regulären eigenen Updateweg ausführen. Danach Versionen, Corebereitschaft, Accounts, Profile, Routen und History überprüfen. Rückfall-/Recoveryweg berücksichtigen; `app.previous` allein ist keine Daten-/OAuth-Rücknahme | C bestanden; tatsächlicher Dienstwechsel braucht ein geeignetes Runtimefenster |
| E: Clients und Projektworker aktivieren | Kataloge mit dem neuen Host regulär neu exportieren und die tatsächlich eingebundenen Standard-/Lain-/VS-Code-Bindungen prüfen. Gültige bestehende Modell-/Effortwahl erhalten. Skill und Regeln nur in den vorgesehenen Projekten installieren bzw. bestehende Anpassungen erhalten; konkrete Routen und Kontextwahl validieren | D, da alter Export den Fehler wieder erzeugt |
| F: Aktivierten Stand abnehmen | Neuer CLI-/IDE-Testthread zeigt Auto-Stufen und überträgt gewählten Effort korrekt. Echte kleine Toolfortsetzung und ein begrenzter Workerbatch liefern beobachtbar passende Route/Account/Resultate. Bestehende Threads behalten ihre eigenen Snapshots. Lokales Update und öffentliche Releasefreigabe getrennt berichten | E; IDE-Aktivierung nach bewusstem Reload/Neustart |

A und die Ursachenanalyse von B lassen sich unabhängig vorbereiten. Große
Builds, Paket-/Runtimeabnahmen und Installationen verwenden weiterhin eine
serielle Lane. C–F folgen in der angegebenen Reihenfolge. Frühe Einzeltests
werden nicht als Nachweis der später gebauten Gesamtpaketbytes umetikettiert.

### Reasoning und Auto als durchgängige Abnahme

Nicht nur das Dropdown prüfen: Katalog → native Clientauswahl → Routerbindung →
tatsächlich gesendeter Effort → öffentliche Attempts/History müssen zusammenpassen.
Bekannte Routergruppen bieten die gemeinsamen unterstützten Stufen an.
Unbekannte Unterstützung bleibt unqualifiziert, nicht erfundenes `none`.
Unzulässige geerbte Einstellungen dürfen keinen fehlerhaften Request erzeugen;
der konkrete Wechsel muss verständlich verweigert bzw. bewusst korrigiert werden.
Modellwahl und Effort bleiben bei einer bestehenden gepinnten Unterhaltung
nachvollziehbar; kein heimliches Umrouten für eine höhere Reasoningstufe.

Der aktuelle Befund aller 16 installierten Auto-Aliase und der bereits
bestandene Sourceexporttest stehen im
[Folgebericht](provider-tool-context-repair-2026-10-26.md#nachprüfung-reasoning-bei-automatisch).
Der dortige kompakte Modus gilt für den CLI-Workerpfad; er wird nicht allein durch
Installation auch zum Toolfilter eingebauter IDEsubagenten.

### Was bereits installiert ist und erhalten wird

Terminal-Codex 0.164.0 samt qualifiziertem Helper/Gruppenlauncher ist bereits
lokal repariert. Die lokale VS-Code-Variante `26.1021.22028` ist installiert;
ihre Aktivierung im laufenden Produktionsfenster wurde nicht beobachtet.
Diese Komponenten brauchen bei unveränderten Pins keine erneute Reparatur oder
Neuinstallation. Nach dem aktualisierten Katalogexport ihre wirksame Bindung und
Anzeige prüfen. Ein IDEreload ersetzt kein Host-/Coreupdate.

### Öffentliche Releasegrenzen bleiben offen

Die beauftragte reale Prüfung jedes konfigurierten Modells bleibt Releasearbeit.
Die kurzen erfolgreichen Kimi-/Z.ai-/Beam-Fälle sind keine Vollfreigabe großer
Recherchesitzungen oder sämtlicher Plugins. Cohere funktioniert im neuen
Kandidaten technisch, hat aber die fachliche Fixprüfung nicht bestanden.
Weitere offene Fälle der ursprünglichen Matrix sind getrennt aufzuarbeiten;
kein stilles Verkleinern des konfigurierten Angebots, um die Abnahme zu bestehen.
Für die öffentliche Verteilung bleiben Lizenz und Lieferform der lokalen
VS-Code-Anpassung zu entscheiden. Diese Fragen blockieren nicht die Vorbereitung
eines privaten lokalen Updatekandidaten. Automatische Aufgabenzerlegung und
Quota-/Kostenoptimierung bleiben nachgelagert.

## Lieferumfang

### Einordnung in den größeren ersten Produktrelease

Die auf anschließenden Nutzerwunsch ausgearbeitete
[Planübergabe für Connect und Doctor](connect-doctor-work-packages-2026-10-26.md)
konkretisiert AP0–AP5 einschließlich gemeinsamer Diagnose und Skillinstallation.
Neue Befehle und Skills darin sind Planverträge, noch keine vorhandene Lieferung.

Die Workerlieferung ist ein Teil des Produkts. Der bestätigte Einstieg umfasst
eigene Accountverwaltung und privaten Verbindungsweg, Modell-/Clientanschluss,
Profile und Gruppen, verständliche Kontingente/Resets und persistente History,
UI-unabhängigen Betrieb sowie eine tatsächlich geprüfte Linuxinstallation und
Updates mit Bestanderhaltung. Bereits gelieferte Funktionen werden nicht
pauschal als fehlende Implementierung behandelt; die finale Paketabnahme muss
ihren Zusammenhang erhalten. Monitor- und Gatewayumfang bleiben erkennbar.

Im früheren Sparring wurden `lain:accounts`, `lain:models`, `lain:connect`,
`lain:usage`, `lain:research`, `lain:doctor` und `lain:workers` vorgeschlagen.
Das war keine Festlegung auf sieben Skills im ersten Release. Aktuell sind
`lain-usage` installiert und `lain-workers` als Source vorhanden. Empfehlung in
der erneuten Umfangsdiskussion: `connect` für den geführten Clientanschluss und
`doctor` für Diagnose als nächste Skillabläufe priorisieren. Diese Empfehlung
ist noch keine angenommene zusätzliche Lieferanforderung. Account-/Modell-
Verwaltung ist schon über UI/CLI möglich; getrennte Skilldateien liefern nicht
automatisch zusätzliche Produktfähigkeit. KI-Recherche, Classifier/Approver,
automatische Zerlegung und Quotaoptimierung bleiben gesonderte Ausbauvorhaben.

Projektregeln in `.lain/workers.json` referenzieren ein existierendes Lainprofil
und stabile Routen je Zweck. `lain workers init` erzeugt höchstens zwei Worker
und zunächst keine erlaubten Zwecke. `validate` und `plan` prüfen Schema,
Profilrevision, angewandte Bindings, Transport, Modell-/Reasoningfähigkeiten und
bestehende Kostenkontrollen ohne Providerprobe oder Profilanwendung.
`free_only` muss im tatsächlichen Runtimebinding wirksam sein.

`run` startet ausschließlich den ausdrücklich gelieferten Batch (höchstens
`maxWorkers`, gültig 1–12). Es gibt eine Laufsperre je kanonischem Projekt,
private Status-/Event-/Ergebnisdateien, Kindlaufzeit 1–3600 Sekunden
(Standard 900), Prozessgruppenbereinigung und erhaltene Teilergebnisse.
Der Runner zerlegt Aufgaben nicht, startet sie nicht erneut und führt Worktrees
nicht automatisch zusammen. Quotaverteilung und ein globaler Scheduler folgen später.

Der gebündelte [Skill](../skills/lain-workers/SKILL.md) wird bei ausdrücklicher
Projekteinrichtung mit `lain workers install-skill --project DIR` nach
`.agents/skills/lain-workers` installiert; bestehende Anpassungen werden erhalten.
`lain-workers` ist der tatsächliche Skillname. Der gewünschte Produktname
`lain:workers` registriert keinen solchen Namespace. Regeln sind versionierbar,
Prompts und Runtimezustand bleiben außerhalb des Checkouts privat; `LAIN_HOME`
wird berücksichtigt. Der vollständige Vertrag steht in [workers-v1.md](workers-v1.md).

## Nachgewiesene Reparaturen

- Der Codexanschluss benötigt den separaten `codex-code-mode-host`. Build und
  Installer berücksichtigen ihn; qualifizierte Bytes werden als geschützte
  reguläre Kopie ausgeliefert und vor gepatchten Starts mitgeprüft.
- Modellkataloge exportieren Reasoning je belegtem Modell statt pauschalem
  `none` mit leeren Optionen. Astra und GPT-6.1 Sol verwenden im qualifizierten
  0.164-Clientstandard `low`, GPT-6 Sol/Luna `medium`; Luna bietet kein `ultra`.
  Unbekannte Steuerung bleibt unbekannt und blockiert Workerstarts. Explizit
  unzulässige Werte werden abgewiesen. Die konkrete historische Astrafehlermeldung
  ist damit nicht als live reproduziert oder abschließend erklärt ausgegeben.
- Native `config/read`- und `configRequirements/read`-Gates prüfen die wirksame
  Provider-/Auth-/Katalog-/Modell-/Effortbindung und die Sandbox. Probe und `exec`
  verwenden gleiche vollständige Overrides mit zufälliger Provider-ID, ohne
  natives `--profile`, das der 0.164-Appserver nicht akzeptiert. Verwaltete
  Widersprüche werden abgewiesen. Alle drei Delegationsflags sind deaktiviert.
- Echte parallele erste Appserverstarts konnten an der SQLiteinitialisierung
  scheitern. Die no-turn Gates laufen deshalb seriell vor parallelen Workerturns.
  Unveränderte Wiederholungen oder längere Blindtimeouts sind keine Reparatur.
- Schreibaufträge benötigen einen sauberen gesamten Gitstand und bereits
  vorhandenes natives Projektvertrauen. Native `--worktree`-Starts könnten sonst
  ein bisher uneingetragenes Projekt als `trusted` speichern; der Runner verweigert
  diesen Fall. HEAD wird erfasst und vor Schreibstarts erneut geprüft. Ignorierte
  Dateien/WIP werden nicht kopiert. Worktreeidentität stammt nur aus eindeutiger
  nativer Gitregistrierung und `codex-thread.json`, andernfalls bleibt sie unbekannt.
- FIFO-/Socket-/Gerätedateien werden abgewiesen. Linux liest JSON nichtblockierend
  und prüft den geöffneten Dateideskriptor, damit ein ausgetauschter FIFO die CLI
  nicht blockiert. Abbrüche bei der Worktreeidentifikation erhalten fertige Ergebnisse.

## Qualifizierter Codex-Helper

Der kombinierte Sourcebuild scheiterte mit Exit 101 am offiziellen Download der
rusty_v8-v152.2.0-Bindings (HTTP 404). Statt einer anderen V8-Version wurde der
**offizielle Helper aus Codex 0.164.0** authentifiziert und nativ geprüft:

- Upstreamcommit `6aaac694803004a63c6f393d153c96d22521a6b9`.
- Helper SHA256 `8ee4f472e92ac226a189e98998633b2d07f989f208515750764db660369f2926`.
- Sigstore: Artefaktsignatur, Fulcio-Kette, Rekor-Zeitstempel/Inklusionsbeweis und
  Checkpoint sowie exakte OpenAI-Workflow-, Tag- und Commitidentität geprüft;
  manipulierte Bytes und falscher Commit wurden abgewiesen.
- Mit dem gepinnten gepatchten CLI führte `functions.exec` tatsächlich
  `tools.exec_command` aus; echter Shell-Erfolg und dessen Tooloutput erreichten
  den zweiten lokalen Providerrequest und die abschließende Antwort.

Die portable [Qualifikation](../clients/codex/code-mode-host-0.164.0.qualification.json)
und die [Wartungsanleitung](../clients/codex/maintenance.md) halten Pins und Grenzen
fest. Vollbelege: `~/.cache/lain-qualification-20261026/ap1-official-helper-r1/`
und `ap1-official-helper-native-r1/`. Der ursprünglich verlinkte Systemhelper
funktionierte im konkreten Test ebenfalls, seine exakte Sourceversion blieb
unbelegt. Er wurde nicht als offizielles 0.164-Artefakt ausgegeben.

## Gleiche Modellgruppen beim normalen Codexstart

Zwei unabhängige Einschränkungen verursachten das flache Menü: Der Pythonlauncher
wählte den gepatchten Client nur für das explizite Profil, und die native
Erweiterung verlangte ebenfalls exakt `--profile lain`. Beide berücksichtigen
jetzt auch das Standardprofil bei Provider `lain` und gültigem Gruppenkatalog.
Andere explizite Profile und Provider behalten ihre bisherige Darstellung.
Es wird kein Profilparameter eingefügt; Modell, Reasoning und übrige Einstellungen
stammen weiterhin aus der tatsächlich gewählten Konfigurationsschicht.

Neuer Client SHA256
`0d45eb864e1773cdd692194f3d1e7b2056614560444df5ed48e027733aab349a`,
aus dem gepinnten 0.164.0-Source mit Rust 1.99.1 gebaut. Die Ergänzung
[default-menu-0.164.0.qualification.json](../clients/codex/default-menu-0.164.0.qualification.json)
qualifiziert diese Bytes mit dem offiziellen Helper. Native Nachweise unter
`~/.cache/lain-qualification-20261026/`:

- `codex-default-menu-native-r2/`: alle 15 PTY-Fälle, keine Providerrequests.
  Der parameterlose Start prüft Daemonsocket, PID-/Netzwerknamespace und exakt
  denselben CLIhash. Gruppenmitgliedschaft, Escape und Modellpersistenz bestanden.
- `codex-default-helper-native-r1/`: echte Shell über Code Mode und passende
  Fortsetzung bestanden, zwei synthetische Requests, Konfigurationssentinels erhalten.
- `workers-native-runner-r3-default-menu/`: derselbe Workerpfad mit den neuen
  Clientbytes bestanden; Lesen verweigert Schreiben, Schreibauftrag im verifizierten
  detached Worktree, Quellcheckout und Basiskonfiguration unverändert.

Die erste frische Daemonprüfung `codex-default-menu-native-r1` bestand 14/15;
der normale Start lehnte die übernommenen musl-Paketmetadaten beim GNU-Sourcebuild
korrekt ab. Der erfolgreiche zweite Lauf verwendet ein vollständiges Paket mit
passenden GNU-Metadaten. Neben Client und Helper sind 41 Begleitdateien gepinnt;
die Voicebegleitdateien wurden erhalten, Voicefunktionalität wurde nicht geprüft.
Rust-Unittests sind vorbereitet, wurden nicht ausgeführt; die native Abnahme
arbeitet mit dem tatsächlich gebauten Client.

## Tatsächlich vorliegende Nachweise

| Prüfung | Ergebnis / privater Beleg |
| --- | --- |
| Serielle Offlineabnahme | Final alle elf Kommandos bestanden: `workers-v1/offline-r2/report.json` unter `~/.cache/lain-qualification-20261026/`; 165 Sourcehashes vor/nach Lauf identisch, genaue Kommandos, Exitcodes und Logs. Erster Lauf mit zehn Kommandos separat erhalten |
| Dispatcher | 92 Pythonchecks einschließlich Defaultauswahl, vollständigem Paket, Austausch, Unterbrechung, Rücknahme und Gerätekennungswechsel |
| Sourcevorbereitung / Helperharness / Workerharness | 14 + 11 + 5 Pythonchecks |
| Worker-CLI | Final 194 Assertions bestanden; Fixture `~/.cache/lain-workers-tests/646c182908464953b3f0ec44c71f60df/` |
| Hostplaner | 271 Offlinechecks |
| Modellkompatibilität / Gatewaykatalog | 105 / 98 Assertions |
| Profilinstallation / Client-CLI / angewandte Profile | 150 / 476 / 104 Checks |
| Echter nativer Workerablauf | `workers-native-runner-r2/summary.json`: beide Worker erfolgreich; je zwei lokale Requests; read-only verweigert Schreibversuch; Schreibauftrag erzeugt Datei im verifizierten detached Worktree; Konfigurationssentinel unverändert |
| Startfehlerdiagnose | `workers-native-probe-contention.json` und Folgehandoff: SQLiteinitialisierung fehlgeschlagen; separate Skills-Kollision nur Warnung; erste fehlgeschlagene Läufe bleiben erhalten |
| Desktopprofile | Private Xvfb-N05/N06-Prüfung, 111 Checks bestanden: `workers-v1/desktop-r2/n05-n06-evidence.json`; echter Produktionscode mit In-Memory-API, keine volle GUI-/Visuellabnahme |
| Skill | System-Skillvalidator bestanden; Beispiele, Links und Whitespace geprüft |

Die Desktopvorprüfung `desktop-r1` scheiterte im eigens isolierten Harness an
fehlenden Geräte-/Proc-Mounts für die Zufallsquelle. Mit korrekten privaten
`/dev`-/`/proc`-Mounts bestand `desktop-r2`; keine Produktänderung zum Umgehen des
Fehlers. HOME und CODEX_HOME wurden nicht umbelegt, sondern das ursprüngliche
HOME im Namespace privat gemountet.

## Tatsächlich installierter Codexlauncher

Der bestehende Pfad `~/.local/share/lain/clients/codex-profile-lain` bleibt das
Wartungsziel; auch die installierte Lain-Desktopstatusprüfung kann ihn weiter
prüfen. Der Launcher startet für die Standardkonfiguration jetzt
`runtime/bin/codex`. Das vollständige qualifizierte Paket liegt unter `runtime/`;
sein Helper ist eine eigene geschützte reguläre Kopie. Der alte `codex-lain`
und der nutzerseitige Systemhelper-Symlink im Stammverzeichnis sind unverändert
für bestehende Sitzungen erhalten. Kein Profilparameter wird ergänzt.

Die alte Wartung speicherte Gerätekennung 30, aktuell ist es 50; alle drei
Inodes, ursprüngliche Programmbytes und das Linkziel stimmten überein. Die
ausdrückliche Operation `reaccept-identities` überprüfte diese enge Bedingung
und aktualisierte nur Manifest/Receipt mit privater Rohsicherung. Danach wurde
der qualifizierte Paketwechsel über `rebind` durchgeführt. Die erste private
Rücknahmeprüfung lehnte den alten Gerätebezug korrekt ab; der zweite private
Lauf mit dieser Wiederaufnahme bestand Austausch und Rücknahme vollständig.

Private Belege unter `~/.cache/lain-qualification-20261026/`:

- `codex-default-legacy-upgrade-stage-r2/acceptance.json`: Wiederaufnahme,
  Austausch, Status und Rücknahme erfolgreich; alte Runtime und Linkidentität erhalten.
- `codex-default-production-r1/acceptance.json`: lokale Installation erfolgreich,
  Konfigurations-/Kataloghashes unverändert, kein Dienstneustart und kein Profilapply.
  Upgrade-Transaktion `2bbf07e7563041a5bd0a9d9d51c9acab`, vollständige private
  Wartungssicherung im bestehenden `maintenance-history/`.
- `codex-default-production-native-r1/summary.json`: zwei reale Starts über den
  installierten Launcher mit synthetischem privaten HOME bestanden, einmal
  ohne Parameter und einmal mit `--profile lain`, keine Providerrequests.
- `codex-default-existing-daemon-native-r1/summary.json`: neuer TUI-Client mit
  privat vorab gestartetem Originaldaemon (`b93f6811…`) ebenfalls bestanden.
  Der bestehende produktive Daemon musste für die Menüreparatur nicht neu starten.

Installierter Status: `ready`, `readyForGroups: true`, keine Issues. Das
installierte Manifest hat SHA256
`fcb704289f860f623d51bafb3d98a2d7c4cb26a2448465e8fb675f7dd229713d`.
Die effektive Standardauswahl verwendet weiterhin das vorhandene Modell und
Reasoning. Insbesondere wurde ein vorhandenes `none` nicht still ersetzt.

## Liefergrenzen

Alle nativen Inferenzantworten kamen aus isolierten lokalen Testservern mit
synthetischen Credentials und privaten PID-/Netzwerk-/Home-Namespaces.
Es gab keinen echten Providerlauf, keine neue Anmeldung und keine Quotaqualifikation.
`RequestedModel` und `ExpectedAccountIds` beschreiben Absichten; tatsächliche
Upstreamaccounts benötigen einen belegten History-/Inspectorjoin.
Unbekannte Funktions-/Streaming-/Fortsetzungsfähigkeit wird nicht als bewiesen
angegeben; ausdrücklich nicht unterstützte Fähigkeiten werden abgewiesen.

Lain-Host/CLI/Desktop wurden aus diesen Sourceänderungen noch nicht produktiv
installiert, kein Profil angewandt und kein Dienst neu gestartet. Commit,
Paketlieferung und öffentliche Veröffentlichung sind damit nicht behauptet.
Vor einem Lainrelease bleiben die eigentliche Paketlieferung und ihre native
Installationsabnahme offen. Automatische Aufgabenzerlegung und Kosten-/Quotaverteilung
sind bewusst nachgelagert. Die historische Astrafehlermeldung ist ohne echten
Reproduktionslauf nicht als live behoben ausgegeben.
