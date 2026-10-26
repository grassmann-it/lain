# VS-Code-Modellgruppen und Astra-Reasoning

Stand 26.10.2026. Umsetzung durch „gut implementiere“ beauftragt;
anschließend konkret gemeldeter Astrafehler in denselben Auftrag aufgenommen.
AP1–AP5 sind umgesetzt und nativ qualifiziert. Die lokale Variante
`26.1021.22028` ist über den normalen VSIX-Mechanismus installiert und ausgewählt.
**Die laufende Produktionsoberfläche wurde nicht neu geladen; ihre Aktivierung
ist nicht beobachtet.** Der Nutzer aktiviert sie mit `Developer: Reload Window`.

## Quellen und Grenze

`clients/vscode/` enthält eigene Host-, Abfrage- und UI-Bausteine.
`scripts/clients/vscode_groups_patch.py` transformiert ausschließlich den
gepinnten Build; `vscode_groups.py` stellt vollständige Kandidaten,
Qualifikationsbelege, Status, Installation und Rücknahme bereit.
CLI und Core bleiben dabei unangetastet. Die getrennte Workerlieferung wird
dadurch nicht installiert. Kein Providerlogin, Profilapply oder Dienstrestart.

Die Erweiterung nutzt unverändert `codex-cli 0.164.0-alpha.11.1`, SHA256
`e6d386e8822288f6d5f327e966a5a5e1de928de8795bb6149716005a410f38f2`.
Das ist nicht die separat reparierte Terminalbinary. Reale Alpha-Proben zeigen:
`model/list` bindet an den Startkontext; ein zusätzlicher `cwd`-Parameter ändert
diese Bindung nicht. Der Adapter vergleicht deshalb globale, Start- und
Workspacekonfiguration samt Providerdefinition und Katalog mit der nativen Liste.

## Prüfungen

- 87 Pythonprüfungen: Patchgrenzen, Paketwartung, Astra-Reparatur und Rücknahme.
- 28 Nodeprüfungen: Katalog-/Kontextbindung, Datei-/Schemaregeln, Pagination,
  Abbruch, verspätete Antworten und initiales Rendering.
- Reale Alpha-Proben mit und ohne Projektkatalogüberschreibung, ohne Inferenz.
- Native Entwicklungsdurchstiche in VS Code: Gruppen, Überschneidung, Alle,
  versteckte Modelle, deaktivierter fehlender Router, Modellkennung und
  Reasoningänderung persistent. Fehlerhafte erste UI-Stände bleiben als
  Fehlversuche erhalten; sie sind keine Abnahme des finalen Pakets.
- Exakte Paketinstallation Original → Kandidat → Original bestanden. Vor dem
  bewussten privaten Reload verwendete der laufende Host weiterhin Originalcode
  und Originalwebview. Danach war der Kandidat tatsächlich aktiv; nach Rücknahme
  und bewusstem Neustart wieder das Original. Jeweils eine ausgewählte
  Codex-Registryzeile, vollständige Originalressourcen erhalten.
- Zehn native UI-Abnahmepunkte am aktivierten Kandidaten: Escape/Fokus,
  Tastatur/Unicode/Überschneidung/versteckte Modelle/fehlender Router, eindeutiges
  Alle und schreibfreie Navigation, persistente Modell-/Reasoningstandards,
  neuer Testthread, Modell-/Reasoningwechsel im bestehenden Testthread, ungültige
  Metadaten mit nativer Liste, frische Kataloggeneration, schmale Seitenleiste,
  Router über seine tatsächliche native Modellkennung.
- Native Turns gingen ausschließlich an einen synthetischen Loopbackanbieter im
  Netzwerknamespace. Beim ersten Thread erstellt die Erweiterung zusätzlich einen
  Chattitel; der Harness zählt diese Anfrage getrennt von sichtbaren Antworten.
  Auth, Settings, Konfiguration und zwei Testthreaddateien blieben über die
  Rücknahme hashidentisch. Externe Provider waren nicht erreichbar.

Private Nachweise liegen unter `~/.cache/lain-qualification-20261026/vscode-*`.
Das vollständige finale Kandidatenpaket liegt unter
`~/.cache/lain-vscode-groups/final-r3/`; Manifest-SHA256
`f1f691b7eab8bc59c6f005bceeb85ad940ac99c295d7ad546c092ebd34e49804`.
Die eigenen Produktquellen stimmen mit den Sourcepins dieses Pakets überein.
Native Abnahme: `vscode-installed-r6/native-qualification.json`; Paketbeleg
`final-r3/qualification.json`, SHA256
`2e78e1dad3b4c25aaca9c4e41de2ab8d441651924c817633a171eb9c267993ba`.

Fehlversuche bleiben erhalten: zunächst UI-Initialzustand/Escape, danach
Testtreiber-Zählung, Fokuswartebedingung, erhaltene erweiterte Menüansicht und
Kaltstart des privaten Fensters. Die Rücknahmeoberfläche stürzte einmal am
geerbten 1024-Dateideskriptorlimit ab; der private Harness hebt ausschließlich
sein eigenes Softlimit auf mindestens 8192 an. Erster Treiberfehler und Crashlog
sind unter `rollback-driver-first-error.txt` beziehungsweise
`rollback-fd-limit-*` erhalten. Die anschließende Originalabnahme bestand.
Diese Fehler sind keine bestandenen Zwischenläufe.

## Tatsächliche lokale Installation

Normaler VS-Code-CLI-Installationsweg, gleiche Erweiterungs-ID `openai.chatgpt`,
lokale Anzeige `Codex (Lain local groups)`. Ausgewählter Pfad:
`~/.vscode/extensions/openai.chatgpt-26.1021.22028`. Die Galleryoriginaldateien
unter `openai.chatgpt-26.1021.22027-linux-x64` blieben vollständig erhalten.
Transaktion im qualifizierten Bundle:
`transaction-5c603daea2db4ee899624d5f934de27a/completed.json`.
Ergebnis `reload-needed`, `activationQualified=true`, `reloadPerformed=false`.
Produktionsverzeichnisse: `~/.vscode/extensions` und `~/.config/Code`.

Status und Rücknahme stehen in [maintenance.md](../clients/vscode/maintenance.md).
Die lokale VSIX-Auswahl wird gepinnt; unbekannte Erweiterungsupdates werden
nicht übernommen oder blind gepatcht. Die Lieferung gilt für diesen Linux-x64-
Build. Remote, andere Provider, eigene Executables und nicht belegbare
Kontextbindungen verwenden die native Liste. Kein allgemeiner Marketplacefork
und keine öffentliche Veröffentlichung.

## Konkreter Astrafehler

Die installierte Clientkonfiguration verlangte `none`. Der alte Katalog
advertisierte für `gpt-6-astra` gleichzeitig Standard `none` und keine
Reasoningwerte. Deshalb fehlte die native Steuerung; der Provider lehnte den
Wert ab. Der bereits vorhandene Sourcefix im Export ist noch nicht als Host
installiert. Er repariert bestehende Clientartefakte nicht rückwirkend.

Die begrenzte Migration ist angewandt: nur Astras beide Reasoningfelder
ändern, fünf belegte Werte `low, medium, high, xhigh, max`, Standard `low`.
Alle übrigen Katalogbytes bleiben identisch. Neue unveränderliche Generation,
vollständige Sicherung, explizite Hashbindung vor dem atomaren Configwechsel
und bedingte Rücknahme. Die separate Lainkonfiguration mit Sol/high bleibt
erhalten. Die unveränderte Alpha-Binary hat den exakten neuen Katalog ohne Turn
eingelesen und alle fünf Werte sowie `low` als Standard bestätigt.
Native Aufnahme: `vscode-astra-final-r1/result.json`.
Zusätzlich tatsächliche VS-Code-Weboberfläche mit exakt diesem reparierten
Katalog geprüft: Astra startet mit `low`, die native Powersteuerung ist aktiv,
ein Tastaturwechsel auf `medium` wird in der privaten Konfiguration und am
Trigger gespeichert; Gruppendarstellung ebenfalls vorhanden. Keine Inferenz.
Beleg: `vscode-astra-ui-r1/astra-ui-checks.json`. Ein erster Wartefehler durch
das native Sol-Einführungsmodal bleibt als Fehlaufnahme erhalten; danach
ausdrücklich „Continue with current model“ gewählt und Astra beibehalten.

Aktive neue Generation:
`~/.codex/.lain-artifacts/f3f8af4dfc3545228d7cd6010b5d9457/lain.models.json`.
In der normalen `config.toml` änderten sich ausschließlich Katalogpointer und
Reasoningwert beim Reparaturapply. Bei der Abschlussprüfung stand die normale
Konfiguration inzwischen auf `command-a-plus-05-2026`/`none`; diese spätere Wahl
wurde ausdrücklich erhalten, der reparierte Katalog blieb eingebunden.
`low` ist Astras Katalogstandard, kein erzwungener Wert für andere Modelle.
Der bedingte Rücknahmebeleg darf eine solche spätere Konfigurationsänderung
nicht überschreiben. Alter Katalog, alle anderen Modellmetadaten, separate
`lain.config.toml` mit Sol/high, Auth und VS-Code-Settings blieben erhalten.
Die Migration betrifft den aktiven Standardkatalog, nicht jede historische
Profilgeneration. Vorbereitung, Apply, Verifikation und bedingter
Rücknahmebeleg: `~/.cache/lain-astra-reasoning-20261026/`.
Die Sourcekorrektur des allgemeinen Exports bleibt Teil der getrennten,
noch nicht installierten Host-/CLI-Lieferung.

Bestehende Chats behalten ihre nativen Threadsettings. Nach einem Fensterreload
kann dort ein unterstützter Reasoningwert ausdrücklich gewählt werden; die
Gruppennavigation allein schreibt keinen Wert und verändert keine alte Sitzung.
