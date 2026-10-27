# Native VS-Code-Abnahme

Die Offlineprüfungen laufen ohne VS Code oder Provider:

```sh
python3 -m unittest discover -s tests/vscode-groups -p 'test_*.py'
node --test tests/vscode-groups/test_host.cjs tests/vscode-groups/test_query.mjs tests/vscode-groups/test_ui.mjs
```

`native_backend.py` startet die unveränderte Erweiterungsbinary in einem privaten
Bubblewrap-Kontext ohne externes Netzwerk. HOME bleibt derselbe Pfad; dort liegt
ein synthetisches Homeverzeichnis. `--no-project-override` prüft den globalen
Katalog, ohne dieses Flag wird die abweichende Projektbindung geprüft.
`--catalog /absolut/lain.models.json --no-project-override` prüft zusätzlich Astras
fünf Reasoningwerte an den exakten vorbereiteten Katalogbytes. Es findet keine
Inferenz statt.

`native_vscode.py --extension ORIGINAL --bundle BUNDLE --output NEUES_VERZEICHNIS`
startet tatsächliches VS Code mit privatem Xvfb und dem Originalpaket. Alle
nativen Läufe halten exklusiv `~/.cache/lain-verification-lane.lock`. Benötigt
werden die im Harness ausdrücklich gepinnten lokalen VS-Code-/Playwrightpfade,
Bubblewrap, Xvfb, D-Bus, Node und Python. CODEX_HOME muss ungesetzt sein und wird
nicht überschrieben. Das optionale `--bundle` aktiviert den echten
Installationsablauf; ohne dieses Flag ist es ein Entwicklungsdurchstich.
`--catalog /absolut/lain.models.json` verwendet den exakten reparierten Katalog
und Astra/low als privaten Ausgangszustand für einen zusätzlichen UI-Test ohne
Turn. Ein natives Modellempfehlungsmodal wird mit „Continue with current model“
geschlossen, damit kein anderes Modell gewählt wird. Der private Prozess hebt
sein eigenes Dateideskriptor-Softlimit auf mindestens 8192 an; Produktionslimits
werden nicht geändert.

Der private Controller schreibt `activation.json` mit tatsächlich aktivierter
Version und Pfad. `native_ui_driver.cjs` nimmt in `command.json` eine monoton
steigende `id` und asynchronen JavaScripttext `code` entgegen. Er stellt
`browser, context, page, fs, snapshot, require` bereit und protokolliert jeden
Befehl samt Ergebnis unter `commands/`. Das ist ausschließlich ein lokaler
Testtreiber für diese private Umgebung. `stop` beendet ihn; der Supervisor
beendet seine eigenen Code-/Xvfbprozesse. Ein Lauf ist auf 15 Minuten begrenzt.

Die fachlichen Fälle stehen in `native_ui_cases.cjs`. Der Modellauswahldialog
kann beim erneuten Öffnen seine erweiterte Ansicht behalten. Fokus wird nach
der tatsächlichen Animation geprüft. Der synthetische Anbieter akzeptiert nur
Fixturemodelle, `low/high` und den festen Testtext. Eine zusätzliche native
Chattitelanfrage ist zulässig; Antwortzählung und Anfragenzählung sind deshalb
getrennt. Kein echter Provider ist aus dem Netzwerknamespace erreichbar.

Für die Paketabnahme werden Originalaktivierung und native Liste erfasst, dann
über `vscode_groups.py install --isolation-root /mnt --apply` die exakten
Paketbytes installiert. Vor dem bewussten privaten Fensterreload müssen aktive
Originalversion, Webviewpfad und Originalressourcen unverändert bleiben. Erst
nach dem Reload wird `groups(page)` am aktivierten Kandidaten geprüft. Vor der
Rücknahme werden Konfiguration, synthetische Auth, Settings und Threaddateien
gehasht. Nach sauberem Stopp startet derselbe Zustand mit
`--resume --rollback-first`; der normale CLI-Rücknahmeweg muss das Original
aktivieren und diese Dateien erhalten. Paketversion und eine einzelne
ausgewählte Registryzeile werden gesondert geprüft.

Eine Qualifikationsdatei wird erst aus diesen tatsächlich beobachteten
Ergebnissen erstellt und bindet Manifest, VSIX und komplettes Kandidateninventar.
Schema und produktiver Installations-/Rücknahmeweg:
[maintenance.md](../../clients/vscode/maintenance.md). Frühere Fehleraufnahmen
bleiben erhalten und sind keine bestandene Abnahme.
