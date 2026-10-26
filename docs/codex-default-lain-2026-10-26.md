# Lain als Codex-Standard, bisheriger Zugang als old-cpa

Am 26.10.2026 ausdrücklich vom Nutzer beauftragt und lokal umgesetzt. Diese
Entscheidung ersetzt die frühere Vorgabe, die normale Codexkonfiguration für
den Probebetrieb unverändert zu lassen.

- `~/.codex/config.toml` verwendet jetzt Provider `lain`, dessen lokalen
  Inferenzzugang per absolutem Auth-Helfer und den vorhandenen lain-Modellkatalog.
- `~/.codex/old-cpa.config.toml` ist eine **bytegenaue private Kopie** der vorherigen
  Standardkonfiguration. `codex --profile old-cpa` wählt den bisherigen CPA-Zugang
  mit dessen Modellkatalog und Einstellungen.
- Startmodell `gpt-6-astra` und Reasoning `ultra` bleiben erhalten. Das Modell
  steht im lain-Katalog zur Verfügung. MCPs, Plugins und allgemeine Einstellungen
  bleiben erhalten; die bestehenden alten Providerdefinitionen ebenfalls.
- `web_search = "disabled"` stammt aus dem bestehenden lain-Profil. Die
  explizite Datei `lain.config.toml` selbst ist unverändert.

Die verwendete Codexversion 0.164 unterstützt keinen globalen TOML-Selektor
`profile = "lain"` mehr. Der Standard entsteht deshalb durch die entsprechenden
Basiseinstellungen in `config.toml`. Separate Profile sind überlagerte Dateien;
siehe [offizielle Profilkonfiguration](https://learn.chatgpt.com/docs/config-file/config-advanced).
Der vorhandene Dispatcher und seine Binarypins wurden nicht geändert. Die
zusätzliche Gruppierung im gepatchten Terminalpicker bleibt an das ausdrückliche
`--profile lain` gebunden; das normale Standardrouting verwendet bereits lain.

## Prüfung und Betriebsgrenzen

Vor dem Schreiben: TOML-Semantikvergleich und native Konfigurations-/Profilprüfung
in einem privaten temporären CODEX_HOME. Danach dieselben Prüfungen an der echten
Installation: CLI-Startupvalidierung, Standard-MCP-Liste, `old-cpa`-MCP-Liste sowie
Startupvalidierung durch die installierte VSCode-Codexbinary bestanden. Beide
MCP-Listen enthalten dieselben fünf konfigurierten Server. Keine Modellausführung
oder Providerprobe daraus ableiten.

Alle vier betroffenen Konfigurations-/Sicherungsdateien sind privat (0600).
Eine unabhängige Gegenprüfung bestätigt die exakte Sicherung und ausschließlich
die beabsichtigten Änderungen an Provider, Katalog, Websearch und der additiven
lain-Providerdefinition. Keine Änderung an Accounts, Routen, Historie,
Proxyruntime oder Diensten; kein alter Thinking-Hotfix angewandt.

Neue Threads laden die aktuelle Konfiguration auch im bestehenden Codexdaemon
neu. Bestehende Threads behalten ihren Konfigurationsstand. Für den VSCode-Test
das Fenster neu laden und einen neuen Chat öffnen, damit auch die Clientauswahl
aktualisiert wird. Der erste isolierte Terminaltest kann weiterhin den gemeinsam
laufenden Appserver umgehen:

```sh
codex --no-daemon
codex --no-daemon --profile old-cpa
```

Privates Receipt, exakte Sicherungen und native Ausgaben:
`~/.cache/lain-default-profile-20261026/084553Z/`.
Ausgangs-SHA256 `876256378f480c1015426de6f9461ad2e18f33546702c8e9160075e0c11586c8`.
Für weitere Updateguards ist diese absichtliche globale Konfigurationsänderung
als neue Baseline zu behandeln. Die frühere Baseline bleibt als Beleg erhalten;
keine automatische Rücksetzung auf CPA.
