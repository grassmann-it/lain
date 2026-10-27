# Native OpenAI-Usage in einer Lain-Sitzung

Nutzerauftrag am26.10.2026: originales `/usage`, ausschließlich OpenAI, direkt
in der Lain-Sitzung. Der Nutzer hat sich selbst mit ChatGPT angemeldet;
öffentlicher `codex login status` bestätigt dies. Ein frischer realer
`codex --no-daemon`-Start verweigert `/usage` trotzdem mit der Loginmeldung.

## Ursache und enger Quellfix

Codex0.164.0 ermittelt beim Start seine angezeigte Accountidentität aus dem
Inferenzprovider. Der command-basierte Lainprovider benötigt keine native
OpenAI-Anmeldung; seine `account_state()` ist deshalb leer. Die TUI setzt dadurch
ihre native Usage-Sperre trotz vorhandener separater ChatGPT-Anmeldung.

Patch `clients/codex/patches/0003-lain-native-openai-usage.patch` ergänzt nur die
Antwort auf `account/read`: Bei Provider-ID `lain`, konfigurierter eigener
Commandauth und tatsächlicher gültiger nativer ChatGPT-Identität wird diese
Identität für die Accountanzeige zurückgegeben. Die Berechnung erfolgt nach der
unveränderten Workspace-Routingentscheidung und wird bei tatsächlichen
Routingrequests nicht benutzt. `requiresOpenaiAuth` bleibt false. Das entspricht
der bereits nativen Accountidentität in `account/updated`-Benachrichtigungen.

Inferenzprovider, Endpoint, Commandauth und deren Auflösung sowie die originale
Usage-TUI bleiben unverändert. Ein OpenAI-API-Key ist kein ChatGPT-Abo und führt
zu keinem Fallback. Andere Provider und Lain ohne Commandauth behalten das
Upstreamverhalten. Es werden keine Credentials aus Lains Stores importiert.

## Prüfung und Installation

**Lokal installiert und mit echter Anmeldung geprüft, 26.10.2026.**
Quellen wurden aus dem gepinnten Originalarchiv mit allen drei Patches neu
rekonstruiert;9400+-Dateibaum bytegleich zur privaten Buildquelle. Die14
bestehenden Source-/Fixtureprüfungen bestehen.

Private Belege: `~/.cache/lain-codex-usage-20261026/`.
Der erste Build startete rustc nicht: Im bwrap-Aufbau fehlte ein nutzbares
`/dev/null`. Diagnose mit tatsächlichem EACCES erhalten; explizites privates
`--dev /dev` und `/proc` korrigiert nur den Buildaufbau. Kein Produktgate wurde
verändert. Der neue Build bestand in8m43s mit dem zuvor für Rust qualifizierten
3GiB-Hardcap, einer seriellen Lane und150%CPU. Gemessener Reclaim-Stall führte
zur Anpassung von MemoryHigh1536M über2560M auf3G bei unverändertem Hardcap.

`tests/codex-picker/native_openai_usage.py` prüft mit dem echten Client in privaten
Netz-/PIDnamespaces und synthetischen Credentials: Accountantwort,
Usagewert/Authorization, originale `/usage`-TUI, separaten Inferenzzugang sowie
negative Accountfälle. Alle sieben Fälle bestehen: Defaultstart, explizites
Lainprofil, frischer Daemonstart, signed-out, API-Key, anderer Provider und
Lain ohne Commandauth. Dazu bestehen15 native Modellmenüfälle, ein echter
Tool-/Shell-/Continuationablauf und92 Dispatcherprüfungen. Rust-Unit-Tests
wurden für diesen Patch nicht ausgeführt. Der negative Vergleich mit den alten
Clientbytes bleibt als fehlgeschlagener Lauf erhalten.

Qualifizierte Binary:
`dce2d889f7a0924894a8e2321f6c7101445cbc66a5baad821f725ce430899541`.
Record: `clients/codex/native-openai-usage-0.164.0.qualification.json`.
Der Helper und die übrigen Runtime-Dateien sind unverändert.

## Lokaler Austausch und echte Prüfung

Der private Austauschoperator benutzt den echten Installer zum Staging und
Linux `renameat2(RENAME_EXCHANGE)` für den atomaren Tausch kompletter States.
Neun Prüfungen einschließlich Rücknahme, Fehlern und konkurrierender Änderung
bestehen. Der erste reale Versuch stoppte vor Änderungen an einem historischen
Helper-Symlink. Die korrigierte Inventarisierung erfasst dessen Linktext ohne
Dereferenzierung; Linkrechte0777 sind unter Linux kein schreibbares Ziel.
Reguläre schreibbare Dateien werden weiterhin abgewiesen.

Operator-SHA:
`9be6f7200517b0295ac2fed053e43413a107320cd114ebaab1661eb20e95b3dc`.
Journalstatus `committed`:
`~/.local/share/lain/clients/codex-package-upgrades/9bc2e68f29304015bcf6f4ef7e6614cd/transaction.json`.
Der komplette Vorgänger liegt dort unter `client`; der Entry-Symlink blieb
einschließlich Inode unverändert. Installierter Maintenance-Status ist `ready`,
ohne Issues. Neues Manifest:
`0a2e7002b383de0b3d03fe38f43ec15a8e2eaf97e93745646c927bde167d79dc`.

`codex login status` bestätigt weiterhin ChatGPT. Ein echter frischer
`codex --no-daemon --no-alt-screen` öffnet `/usage` als originale Ansicht mit
„Account usage and resets.“, „View analytics“ und „Redeem reset“.
`/status` erkennt gleichzeitig Provider `lain` und das native Pro-Konto;
seine Limits zeigten zunächst „data not available yet“. Die separate native
`account/rateLimits/read`-Abfrage mit exakt der installierten neuen Binary
bestand:40% der Wochenquote verbraucht,60% verbleibend zum Prüfzeitpunkt.
`account/read` liefert ChatGPT/pro, `requiresOpenaiAuth=false`, kein
Workspace-Routing. Kein Modellrequest wurde für diese Liveprüfung gesendet.
Beleg: `live-patched-native-verification.json` im privaten Belegverzeichnis.
Der öffentliche Launcher leitet den Subcommand `app-server` absichtlich weiter
an die ursprüngliche Binary; dessen separate Liveabfrage ist kein Beleg für
die neue Accountanzeige.

Laufende Produktionsdaemon-Aufgaben wurden nicht beendet. Für die sofortige
Nutzung der neuen Bytes dient `codex --no-daemon`; ein vorhandener Daemon kann
bis zu seinem geordneten Neustart alte Bytes behalten. Die VS-Code-Binary wurde
hier nicht ersetzt. Das AP5-Host-/CLI-/Corepaket ist weiterhin separat vorbereitet,
nicht installiert; seine bisherigen Erhaltungs-SHA-Baselines sind durch den
bewussten Nutzerlogin und diesen Clientaustausch veraltet und müssen vor einer
tatsächlichen Lieferung frisch erfasst und gebunden werden.

Die vom Nutzer gezeigte Meldung „Disconnected from this task. Any running work
continues.“ ist im Quelltext die normale Daemon-Abmeldung bei `UserRequested`.
Ein fataler Abbruch hat einen anderen Wortlaut. Damit ist keine konkrete
Ursache für ein vom Nutzer nicht ausgelöstes sofortiges Beenden bewiesen;
keinen Authfehler oder nötigen erneuten Login daraus ableiten.
