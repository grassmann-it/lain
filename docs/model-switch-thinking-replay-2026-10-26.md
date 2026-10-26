# Modellwechsel und gebundene Thinking-Blöcke

Nutzerauftrag vom 26.10.2026 mit höchster Priorität: Gespräche zwischen Claude
und GPT fortsetzen können, auch nach Compaction oder erneutem Verbinden.
Der bestehende Account-/Lieferauftrag bleibt erhalten, Installation und neue
Anbieteranmeldungen sind während dieser Fehlerbehebung noch nicht erfolgt.

## Nachgewiesener Fehlerpfad

Die gemeldete Request-ID `req_011CgQX8yQ9mtMSwbWXZWstP` gehört laut lokalem
Sessionmetadatensatz zum Provider `cpa`, zum separaten Proxy auf Port 8317.
Der Fehler wurde am 26.10.2026 um 08:18:46 UTC aufgezeichnet. Die Meldung nennt
einen Thinking-Block, dessen Signatur an einen anderen Gesprächspräfix gebunden
ist; konkret unterscheidet sich die Toolliste. Die signierte Historie bleibt
formal gültig und wird deshalb durch eine reine Provider-/Formatprüfung nicht
entfernt. Im globalen Katalog nutzen Sonnet und GPT außerdem unterschiedliche
Shelltools. Das allein ist kein Grund, sichtbaren Verlauf zu löschen.

Die aktive alte Proxybinary hat SHA256
`8b2e25d6546e2d84ab42d2f2a843fa1efc106adef27e2b9477d1cc2171bb0cb9`.
Ihre belegte Quelle liegt separat unter
`/home/tomka/Desktop/work/codex-proxy-tui/integrations/proxy-core/source`.
Kein Austausch dieser Binary und kein Neustart des alten Proxys aus diesem
Auftrag ohne belegtes sicheres Betriebsfenster.

## Gewählte Korrektur

Anthropics `thinking.block_binding.prefix_mismatch_behavior: drop_block` mit
`thinking-binding-controls-2026-08-01` erlaubt dem Anbieter, einen unpassenden
signierten Thinking-Block zu verwerfen. Text, Tools, Toolaufrufe und deren
Ergebnisse bleiben erhalten. Explizite Callerregeln bleiben wirksam; deaktiviertes
Thinking wird nicht wieder eingeschaltet. Keine neuen Retries oder Fallbacks.

Der von lain verwendete CLIProxyAPI-v8.0.41-Core enthält diese Korrektur bereits
für den ClaudeExecutor. Lains tatsächliche Command-Code-Claude-Konfiguration
verwendet diesen Executor. Ein älterer separater CPA-Core enthält den Fix noch
nicht. Dessen entsprechender schmaler Sourcebackport wird separat vorbereitet
und mit lokaler Fakegegenstelle geprüft; kein pauschales Coreupgrade.

Ein eng begrenzter, reversibler Konfigurationsweg für den laufenden alten Core
ist gegen seine tatsächliche Binary qualifiziert. Scope: eindeutige
Command-Code-Sonnet-/Opus-5.5-Aliase, Responses als Eingang, adaptive Thinking
und vorhandene signierte bzw. redigierte Assistanthistorie. Kein breiter
Providerdefault und kein Override einer bestehenden Callerpolicy.

Wichtiger Negativnachweis: atomarer Dateiaustausch über Rename lädt diese alte
Binary **nicht** zuverlässig nach. Ihr Watcher beobachtet den Datei-Inode.
Eine geänderte Datei auf Platte ist deshalb noch kein wirksamer Runtimefix.
Der ausdrücklich getrennte Schreibweg unter Erhalt des Inodes bestand drei
laufende Apply-/Rollbackzyklen mit derselben PID und demselben Inode; ein
offener Claude-SSE-Stream blieb dabei erhalten und schloss anschließend ab.
Insgesamt zwölf Offlinechecks bestanden. Code, Grenzen und Befehle:
[Wartungswerkzeug](../scripts/maintenance/thinking_replay_hotfix.md).

**Aktivierung noch ausstehend:** Der alte Core ersetzt bei jedem erfolgreichen
Konfigurationsneuladen auch seinen CodexExecutor und schließt dessen
WebSocket-Sitzungen. Daher ist die Nutzerfrage nach einem kurzen Ruhefenster
offen; bis zur tatsächlichen Antwort keine Runtimeanwendung. Kein Proxyrestart.
Der Inode-erhaltende Schreibweg ist ausdrücklich nicht atomar; bei Schreibfehlern
stoppt er mit dauerhaftem exaktem Backup und ohne automatische Wiederholung.

Die konkrete private Konfiguration wurde vorbereitet und semantisch verglichen:
ausschließlich drei eng begrenzte Payloaddefaultregeln kommen hinzu, sonst
unveränderter Konfigurationsinhalt. Beleg und privater vorbereiteter Stand:
`~/.cache/lain-thinking-replay-20261026/production-prepared-r1/prepared.json`.
Ausgangs-SHA256 `aa2dd29aa48752ba77caadf6e779834401d995ebcbbaaa482feefba0e4934804`,
vorbereiteter SHA256 `91f83c2cf10fa754967aa7b098cb3a23fcea037d80bb13631a342d90a0920936`.
Vor Anwendung beide Werte und die aktive Binary erneut prüfen.

Der schmale Sourcebackport bestand 74 gezielte Tests einschließlich
Crossprovider-/Toolverlauftests und den vollständigen Servercompile. Unabhängiges
Lese-Review abgeschlossen. Diff, Originale und Nachweise im privaten Cache;
Binarycandidate SHA256
`b4686828044a2e538578fe4a8efe41c04db6b11200d118a768401eb0b9dcba03`.
Dieser Candidate ist **nicht installiert** und keine pauschal qualifizierte
Legacy-Corelieferung; für den aktuellen Betriebsfix ist kein Binaryaustausch nötig.

## Lain nach Kaltstart wiederhergestellt

Parallel war der lain-Host aktiv, sein eigener Proxy aber nach der
20-Sekunden-Startfrist beendet. Ein einmaliger gezielter Start über die bestehende
Hostoperation `/api/v1/service/start` war nach etwa 0,4 Sekunden erfolgreich.
Der alte Proxy und der Host wurden dafür nicht neu gestartet, kein Profil
angewandt. Zustand weiterhin 16 Accounts, 58 Routen, drei Profile,
Revision **55 / angewandt 54**. Der ursprüngliche Kaltstartgrund ist rückwirkend
nicht belegt; diese Wiederherstellung ist kein Fix für die offene
Kaltstartdiagnostik.

Privater Nachweis:
`~/.cache/lain-thinking-replay-20261026/lain-recovery-r1/report.json`.
Weitere lokale Tests, Originaldateien, Backport und Prüfsummen liegen unter
`~/.cache/lain-thinking-replay-20261026/`. Keine Anbieterkeys, Prompts oder
echten Thinking-Blöcke in die Projektdokumentation übernehmen.

## Wiederaufnahme

1. Aktuellen Runtimefix-/Reviewstand in dieser Datei und den privaten Belegen
   abgleichen; keinen erfolgreichen Hotreload ohne Nachweis behaupten.
2. Nach dem Fehlerfix den qualifizierten EC-Lieferstand `dca5f94` fortsetzen;
   Paket-/Testbelege sind vorhanden, ein Installationsaufruf fehlt noch.
3. Vor Installation frische unveränderte Bestands-/Codexguards erfassen.
   Alte Guards behalten; unabhängige Modellwahl kann die normale Codexconfig
   seit dem alten Capture geändert haben. Niemals deshalb zurücksetzen.
4. Anschließend echte NanoGPT-, DeepSeek- und Fireworks-Anschlüsse nur über
   sichtbare Anbieterzustimmung bzw. private lokale Keyeingabe einrichten.

Frische EC-Deliveryaufnahme:
`~/.cache/lain-qualification-20261026/ec-final-r3/full/own-update-resume-r2/`.
Öffentlicher Bestands-/History-/Guardvergleich bestanden; beide Pakete vollständig
hashgeprüft. Der qualifizierende Prozessscanner verweigert aber
`checks.ownServiceIdentity` wegen eines nicht lesbaren externen `ssh-agent` im
Docker-Systemcgroup. `qualification.NOT-VALID.json` bleibt entsprechend ungültig.
Kein Installationsaufruf und keine Umgehung dieses Nachweises. Diese technische
Prüflücke ist unabhängig von der offenen Nutzerantwort zum Modellwechselfix.
