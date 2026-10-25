# Lokale Lieferung N01–N09

Stand 25.10.2026. Neun Fachworker arbeiteten in getrennten Cacheworktrees;
Integration und eine begrenzte serielle Build-/Runtime-Lane lagen beim Root.
Dies ist eine private Linux-Lieferung, keine öffentliche Releasefreigabe.

## Installierter Stand

Host, CLI und Desktop sind aus
`f3d485121a2b89d95a2cdec25aee3163913889a7` installiert, jeweils mit
Informationsversion `1.0.0+f3d485121a2b89d95a2cdec25aee3163913889a7`.
Gatewaymanifest: `c943a6972e5240e2b2ad4b1691dfd2e35225ec1a1a3f5229593eacb670c05d49`.
Separat gebautes und frisch geprüftes Monitorpaket:
`817a1a19638cda780df2f1757ca16b10855f0e4bcd40c1ba38fd0dd5bb2875a0`.
Core bleibt bytegleich
`191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`.
Der spätere Commit `fe813fc` ändert ausschließlich den privaten MiMo-Testrunner
und Dokumentation; er ist kein weiterer installierter Produktstand.
Die Abschlussdokumentation korrigiert zusätzlich eine veraltete Beschreibung
des kompakten CLI-Filters im Quell- und lokal verfügbaren Usage-Skill;
Paketbytes und installierte Produktassemblies bleiben dabei unverändert.

Bestätigt erhalten: **15 Accounts, 58 Routen, drei Profile, Revision 54/54**.
Accounts, Modelle und Profile wurden nach Installation und nach einem weiteren
Dienstneustart exakt mit dem Vorzustand verglichen. Die History im festen
UTC-Zeitraum 22.10.2026 00:00 bis 25.10.2026 19:00 ist unverändert.
Ausgewählte Credentials und übrige geschützte Dateien, normale Codexkonfiguration,
Auth, separate Profil-/Katalogdateien und Legacykonfiguration wurden über private
Fingerprints geprüft. Keine Rücknahme oder Wiederherstellung alter Grants.

## Ergebnisse

| Paket | Geliefert und geprüft | Verbleibende Grenze |
| --- | --- | --- |
| N01 | Gemeinsame kurze Accountanzeige ohne Umbenennen gespeicherter Daten; sichtbare tatsächliche Refreshgründe. Unter 800 px volle Listen-/Detailansicht mit Rückkehr, Fokus und Scrollposition. | Kein neuer Gesamtentwurf; größere Ansichten bleiben zweispaltig. |
| N02 | Synthetic-Ratio korrekt skaliert; Wochenprozent unverändert. OpenRouter-Free hat UTC-Tagesgrenze und nach Ablauf unbekannten aktuellen Rest. | Kein Inferenzrequest zur Erzeugung eines Messwerts. USD-Keycap bleibt separat. |
| N03 | Browserlifecycle, bereinigte Startgründe und Cleanup. Tatsächlicher Produktions-Open/Close mit frischem privaten Profil bestanden. | Login/MFA, UID/Region und echter Individual-Quota-Read bleiben ein interaktives Gate. |
| N04 | Accountgebundene Cloudflare-Reportingdiagnose mit getrennten Fakefällen für Rechte, Dataset, Binding und leere Antworten. | Tatsächliche aktuelle Reportinggrenze wird unten separat festgehalten; keine Restquota aus Analytics ableiten. |
| N05/N06 | Gruppen und Accountrouter in Profiles, globale Reorder-CAS-API, Draft-/Referenzschutz; zwei explizite Codexziele mit getrenntem Prepare, Install und Apply. | Free-Automatik bleibt ohne qualifizierten Zero-Cap-Zugang deaktiviert. |
| N07 | Portable Operatoren, exakte Pakete, atomarer Appwechsel, vollständige gestoppte Sicherung und sichere Fehlerbehandlung. Lokaler Stand nach expliziter Reparatur vollständig nachgeprüft. | Der erste automatische Start nach Appwechsel überschritt die Core-Bereitschaftsfrist; siehe Betriebsbefund. Kein automatischer Own-Update-Pass behauptet. |
| N08 | Qualifizierte Versionspflege, Erkennung geänderter Originalinstallation, Rebind und Rücknahme; reales Wartungsformat 2→3. Original/Patch 0.164.0 unverändert. | Echtes `/model` geöffnet/geschlossen und erste acht Gruppen geprüft; weitere Gruppen unter Scroll wurden dabei nicht bedient. |
| N09 | Native Abnahme, private CLI-PTY-Aufnahmen, zwei echte gesprächsfeste Routerturns mit Inspector/History-Abgleich und tatsächlich installierte UI geschlossen, während Host/Core bereit blieben. | OpenRouter-Browserdurchstich und authentifizierter MiMo-Read brauchen weiter Nutzerinteraktion; kein pauschaler Pass aller Provider. |

## Nachweise und tatsächliche Grenzen

Sieben integrierte Backendsuiten bestanden: Models auf `e6d428b`, die übrigen
auf `b46b05e` (ClientCLI 380, Cloudflare 143, MiMo-Start 19, drei Consolegruppen,
Quota 104, AccountConnectReaders 157 Checks). Nachfolgende Quellreviews prüften
die tatsächlich geänderten Bereiche. Keine Behauptung, dass alle Backendsuiten
noch einmal gegen die letzte Assemblyversion ausgeführt wurden.

Die exakten `09cb8a5`-Desktopbytes bestanden Profiles jeweils 111 und N01 jeweils
15 Checks bei 100/125 % sowie die fokussierten Stateflows. Die neue exakte
`f3d4851`-Lieferung bestand **30 Quota-Zellen mit 1.434 Assertions** bei 100/125 %,
einschließlich aller erlaubten Größen und sechs Monitor-640-Fälle. Zuvor war dort
ein echter Schnittfehler gefunden und behoben worden. Ein unabhängiger Reviewer
prüfte 18 neue 640-X11-Bilder und 18 Renderpartner; Root prüfte Originalbilder.
Live-Resize mit bereits geöffnetem Inspector ist quellgeprüft, kein eigener
nativer Runtimefall. Eine ältere Popup-/Captureabweichung ohne verdeckte
Hauptaktion und generischer Stale-Text bleiben nicht blockierende Beobachtungen.

**71 private PTY-/Pipe-/JSON-Aufnahmen** nutzen die frische Paket-CLI/Client/Core,
Breiten 20–200, Farbe und `NO_COLOR`. Root sichtete schmale und breite Tabellen.
Die kompakte Anzeige verbirgt Accounts ohne Messwerte; `--full` und JSON behalten
die vollständigen Informationen.

Frische Gateway- und Monitorinstallationen, elf Paket-/Transaktionsprüfungen,
Core-Streaming/Drain und neun Host-Modellgruppenprüfungen bestanden auf `f3d4851`.
Der Core wurde erneut gebaut und auf die unveränderten qualifizierten Bytes
geprüft. Quelle, Locks, vollständige Pakete und tatsächliche PE-Metadaten sind
gebunden; ein unabhängiger bitgleicher Komplettneubau wird nicht behauptet.

MiMos erster isolierter Start scheiterte mit Exit 133. Ein begrenztes
Diagnosepaar belegte einen zu langen `TMPDIR` für Chromiums SingletonSocket.
Nur der Testrunner erhielt einen kurzen Alias für dasselbe Cache-Dateisystem;
Sandbox und Browserflags blieben erhalten. Der gezielte Retry bestand mit
unverändertem Produktions-Host, null benannten API-Reads und gelöschtem Profil.
Er ist kein Nachweis eines authentifizierten MiMo-Monitors.

Die installierte UI wurde auf privatem Xvfb mit privatem D-Bus und unverändertem
HOME geöffnet und über echtes `WM_DELETE_WINDOW` geschlossen. Host/Core-PIDs,
Bereitschaft, feste History, Konfiguration und UI-Einstellungen blieben erhalten.
Zwei vorherige Guards des privaten Helpers wurden vor UI-Start korrigiert:
hybride cgroup-Ausgabe und ein zu langer D-Bus-Socketpfad. Diese Verweigerungen
werden nicht als Produktfehler oder bestandene UI-Läufe gezählt.

## Betriebsbefund beim eigenen Update

Der erste portable Own-Update-Lauf installierte die exakten neuen Dateien, aber
der Core erreichte beim anschließenden Hoststart innerhalb von 20 Sekunden keine
Bereitschaft. Der Operator endete korrekt mit
`failed-poststart-recovery-required`, ohne automatischen Rollback, ohne Stop
eines nicht bewiesenen Backends und ohne Wiederherstellung alter Credentials.
Der ursprüngliche Fehlerbericht bleibt unverändert.

Nach unabhängiger Prüfung von eigener Unit, installiertem Host und fehlendem
Core wurde genau ein Start über die vorhandene Host-Reparatur-API ausgelöst.
Dieser gelang sofort. Der vollständige manuelle Postflight bestand; anschließend
bestand ein eigener systemd-Neustart hinter einer bestätigten ruhigen
Inferenzbarriere. Danach wurden Appinventar und Erhaltungsgrenzen erneut geprüft.
Die Anwendung läuft jetzt mit offenem Admissiongate und unverändertem Bestand.

**Folgearbeit:** Die Ursache des ersten Readiness-Timeouts ist nicht nachgewiesen.
Ein begrenztes Betriebspaket soll die fehlende Healthbedingung vor dem Cleanup
als feste Diagnose erfassen, ohne Core-stdout/Providercredentials zu speichern,
und den Kaltstart unter begrenzten Ressourcen reproduzieren. Kein stilles
Heraufsetzen des Timeouts und kein behaupteter erfolgreicher automatischer
Updateablauf aus der manuellen Reparatur.

## Reale Methoden und Restgates

Die abschließende Probe nutzte den bestehenden Codex-CollectionRouter im
lain-Profil mit genau zwei Responses-SSE-Requests, `low` und maximal je 128
Ausgabetokens. Beide lieferten `ok1` beziehungsweise `ok2` über dieselbe
`gpt-6-luna`-Route und denselben Account; der dauerhafte Gesprächspin blieb
identisch. Inspector und Host-History belegen zwei Requests, zwei Versuche,
null Fehler, null Versuche mit unbekannter Usage und insgesamt 159 Tokens.
Die feste Historyabfrage war wiederholt stabil. Keine Inferenzretries,
Fallbacks oder Konfigurationsänderungen. Der vorherige R2-Guard hatte vor jedem
CLI-/HTTP-/Inferenzaufruf verweigert; in R2 und R3 wurden zusammen nur diese zwei
Inferenzrequests gesendet. Dies war eine direkte Probe des exportierten Routers und seiner
Profilbindung, kein zusätzlicher interaktiver Codex-CLI-Gesprächslauf.

Der passive `lain quotas --json`-Read beobachtete den automatischen
Cloudflare-Refresh von 21:09:15 UTC: Die Settingsantwort listete das angeforderte
Feld `sum.totalNeurons` für den ausgewählten Account und Requester nicht auf.
Das Dataset wurde dabei als aktiviert erkannt. Damit ist eine reale konkrete
Reportinggrenze belegt, aber weder eine fehlende bestimmte Berechtigung noch
ein nötiger Bezahltarif oder die vollständige Ursache des primären Queryfehlers.
Alle Mengen bleiben `null`; `support.kind=unavailable`, `state=error` und der
konkrete Grund bleiben sichtbar. Es gibt keinen erfolgreichen numerischen Read
oder gemessenen Provider-Datenzeitstempel. Der abschließende CLI-Read löste
keinen weiteren Providerabruf aus.

OpenRouter-PKCE mit sichtbarem Keybootstrap wurde nicht durchgeführt. MiMo braucht
weiter den bewussten Nutzerlogin/MFA und anschließende Identitäts-/Quota-Prüfung.
Fehlendes Reporting, erforderliche Rechte und ein fehlendes Dataset werden nicht
als bewiesene Abo- oder OAuthanforderung umgedeutet.

## Betrieb, Ressourcen und Evidenz

Alle GUIproben liefen außerhalb des Benutzerdesktops. Builds/Runtime blieben
seriell begrenzt, typischerweise MemoryHigh 1536 MiB, Max 2 GiB, Swap 256 MiB,
CPU 150 %, nice 10 und `ionice -c3`; der Benutzer-cgroup fehlt der IOcontroller.
Der frische volle Paketlauf erreichte 1.61 GB mit Soft-Throttling, ohne OOM;
die neue native Lane maximal 596 MB ohne Memoryevents. Der eigene Updateversuch
erreichte 1.5 GiB und 83.4 MiB Swap. Der externe Python-`model-usage` wurde nicht
verändert oder als lain bezeichnet. Neue große temporäre Dateien lagen im Cache,
nicht im RAM-Dateisystem `/tmp`.

Private Evidence liegt unter
`~/.cache/lain-qualification-20261025/next/`: `backend-e6d428b`,
`full-f3d485121a2b`, `native-responsive-f3d485121a2b`, `n08/real-menu-r3`,
`n09-two-turn-real-r3`, `final-passive-observation-r1`
und `installed-ui-close-prepared`. Teilberichte nennen exakte Quellen, Hashes,
Kommandos, tatsächliche Resultate und Grenzen. Frühere Fehlversuche bleiben
Fehlversuche. Vollständiger gestoppter Zustand und vorherige Anwendung bleiben
als Sicherungen erhalten; fremde Dokumente und 15 gesicherte schmutzige Worktrees
sind unangetastet. Genau sechs freigegebene alte Build-/Runtimeverzeichnisse
mit 2.097 generierten Dateien (4,457 GiB logische Dateigröße) wurden entfernt.
456 umliegende PNG-/JSON-/Log-Nachweise sind SHA-identisch erhalten, ebenso
frische Runtime und vollständige Pakete. Btrfs meldete danach netto nur
0,014 GiB mehr freien Speicher; 4,457 GiB physische Freigabe wird daher nicht
behauptet. Manifest: `cleanup-obsolete-ui-20261025T211128Z`.

Abschließender installierter Status um 21:12:33 UTC: `running`,
`proxyReady=true`, Revision 54/54, Profil `lain`, kein Statusfehler.

Ein zusätzlicher Hygienecheck über 49 Goalcommits und 98 Textdateien fand keine
Credentials, privaten Runtimekonfigurationen oder kopierten realen Stores in
den getrackten Änderungen. Keine öffentliche Veröffentlichung oder Push.
