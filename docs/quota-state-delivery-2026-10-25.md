# Kontingente und Zustandskorrekturen: Prüfstand 25.10.2026

Die zwölf aktuellen Offline-Suiten sind bestanden. Die
[Originalprotokolle und Prüfsummen](../tests/quota/evidence/20261025/summary.json)
trennen synthetische Prüfung von Anbieterzugriff, Paket und Installation.

| Bereich | Erfolgreich gemeldete Checks |
| --- | ---: |
| Accountgrundlage | 171 |
| Hostverdrahtung und Cleanup | 47 |
| Quotaquellen | 75 |
| Groq Enterprise | 66 |
| NVIDIA, ausschließlich datierte Referenz | Suite bestanden |
| Cloudflare | 52 |
| Google Cloud / isolierter offizieller CLI-Anschluss | 142 |
| MiMo, Identität und Profilbereinigung | Suite bestanden |
| Gemeinsamer Client und CLI | 223 |
| Kompakte und exakte Quotaformatierung | Suite bestanden |
| Erhalt von Messwerten und Fehlerzuständen | 98 |
| Accountreader und gewählte Zugangsart | 157 |

Der Lauf fand einen echten Startfehler: Ungültige MiMo-Ownerreceipts brachen
die Reconciliation ab. `f04bed1` erhält den betroffenen Kandidaten als fehlgeschlagen
und setzt den begrenzten Scan fort; die vorhandene Regression besteht danach.
Zusätzlich wurden veraltete Fixtureanschlüsse korrigiert. Der Reader-Test verwendet
jetzt den echten Host statt eines unvollständigen Quellauszugs. Produktionslocks
wurden zum Testen nicht verändert. Frühere fehlgeschlagene Läufe bleiben erhalten.

Die Google-Verbindung nutzt ein eigenes Profil des qualifizierten Google Cloud
CLI. Dessen tatsächliche OAuthrechte sind breit; ein Read-only-OAuthgrant wird
nicht behauptet. Cloudflare meldet beobachteten Neuronverbrauch ohne erfundenes
Freebudget. Groq unterscheidet passive Limits von Enterprise-Verbrauchsraten.
MiMo bietet eine eigene Browsersitzung an. NVIDIA bleibt eine datierte
Consolereferenz ohne gemessenen Accountrest. Diese Anschlussfähigkeit ist kein
Beleg für eine erfolgreiche reale Anmeldung oder Abfrage aller Nutzeraccounts.

UXD-17–22 samt bestätigter Monitortabelle und linker schmaler Live-Liste werden
[separat nativ geprüft](../tests/uxd-acceptance/state-flow-review.md).
Der [unabhängige Review](uxd17-22-independent-state-review-2026-10-25.md)
nennt Quellenbefunde, tatsächliche Bilder und Prüfgrenzen.

## Live und Codexprofil

Die installierte Source meldete vor dem Update Cursor 0, keine Streams, keine
Drops und keinen Fehler. Die gezeigten alten Attempts lagen vor dem Neustart.
Live ist ein begrenzter flüchtiger Ausschnitt eigener Inferenz; historische
Attempts werden nicht erneut eingespielt. Der neue Leerzustand erklärt dies.
Accounteinrichtung, Quotaabruf und Export erzeugen keine Inferenzereignisse.

Die zusätzlichen Free-Modellproben haben keine Inferenz gesendet: Eine aktive,
kostenqualifizierte neue Route war nicht belegt. Die alten 45 Codexaliases
entsprechen den 45 Routen im Profil `lain`; neue Accounts allein erweitern dieses
Profil nicht. Gewünschte Modellzugänge zuerst in Profiles → lain auswählen und
speichern. Laufzeit-Apply wird separat geprüft. Danach installiert
`lain profiles install-codex lain --revision N --json` die separaten Dateien unter
`~/.codex`; die normale Konfiguration bleibt erhalten. Der UI-Export nutzt einen
privaten Ordner und zeigt dafür den vollständigen `CODEX_HOME=… codex …`-Befehl.

## Lieferung

Quellstand `d7200d7d700bd2d23abf8bf028d40526eb235bf7` ist als Monitor- und
Gatewaypaket gebaut und als `1.0.0+d7200d7…` installiert. Die
[kuratierten Lieferbelege](../tests/delivery-stabilization/evidence/20261025/)
trennen Publish, synthetischen Updateversuch, exakte Paketoberfläche und reale
Installation. Spätere Dokumentations- und Harnesscommits verändern diese
ausgelieferten Produktionsassemblies nicht.

| Paket unter `artifacts/` | SHA-256 des Archivs |
| --- | --- |
| `lain-linux-x64-monitor-quota-state-20261025.tar.gz` | `aff589164495a444e1d277d104f94a07f3746cca2beaf5f52e7d5883e0be3570` |
| `lain-linux-x64-gateway-quota-state-20261025.tar.gz` | `610a4d03a206f225a15c04e16b2e510120fec8821548ebe5bc7ccf2382c6b4db` |

Auf den exakten Gateway-Paket-DLLs bestanden 18/18 ausgewählte native Fälle bei
100 % (377 Assertions, 46 Bilder) und 4/4 bei tatsächlichen 125 % (70 Assertions,
zwölf Bilder). Der unabhängige Review prüfte sechs dieser X11-Bilder und die
Paketidentitäten; Root sichtete drei. Diese Auswahl ist keine vollständige
Accessibility- oder Modusmatrix. Der qualifizierte Core bleibt unverändert
`8.0.41-lain-adapter.3`.

Der ausgeführte isolierte v4-Update-/Rückkehrversuch bestand für Gateway und
Monitor mit synthetischer Historie und aktuellen synthetischen Grants. Er
erhielt 19/2 und den unbekannten aktiven Profilnamen; er verspricht keine
allgemeine Downgradefähigkeit. Die eigene loopback-only Netzwerknamespace
verhinderte reale Anbieterinferenz.

Die reale Installation ist separat `verified`: Proxy ready, gewünschte/angewandte
Revision weiterhin **19/2**, aktive Profilidentität weiterhin ausdrücklich
unbekannt. Alle **15 Accounts** und **14 historischen Attempts** im festen
UTC-Intervall 24.09.–25.10.2026 sind erhalten. Zehn Attempts haben unbekannte
Usage; 346.981 gemeldete Tokens aus vier Messungen bleiben deshalb eine bekannte
Teilsumme, kein vollständiger Gesamttokenwert. Ein frisches gestopptes Backup
liegt unter `~/.local/share/lain-backups/20261025-quota-state-d7200d7/`.
Ausgewählte aktuelle Credentials sowie eingefrorene Konfiguration, Runtime und
Journal blieben beim Appupdate unverändert. Kein automatisches Apply, kein
Codexexport und keine Änderung oder Neustart des alten Proxys wurden ausgeführt.

Neue reale FREE-Modellproben und erfolgreiche Anmeldungen/Quotaabrufe aller
Anbieter bleiben gesondert ausstehend; keine kostenpflichtige Probe wurde als
Ersatz gesendet. Eine Kontingentquelle und eine bestandene synthetische Suite
belegen allein noch keinen erfolgreichen Anbieterzugriff des Nutzeraccounts.

Der gezielte anschließende [Read-only-Test der installierten UI](../tests/delivery-stabilization/evidence/20261025/installed-ui.json)
bestand **100 Checks** mit acht Bildern bei 1120×780 und 800×560. Exakte
installierte Assemblies, sichtbare Simple-Navigation mit fünf Bereichen plus More,
Live-Quellidentität/Leerhinweis, unbekanntes angewandtes Profil und Erhalt von
Host, Konfiguration und fester Historie über Schließen/Wiederöffnen wurden
geprüft. Alle 23 HTTP-Aufrufe waren GET; Preferences, Xvfb und Session-D-Bus
waren privat. Root sichtete beide Livebilder und das schmale Profilesbild.
Die vorherige breite installierte Legacyfixture scheiterte an ihrer veralteten
Fünf-Navigations-Erwartung; sie bleibt separat als Fehler erhalten. Der neue
gezielte Modus macht daraus keinen Gesamtsuiten-Pass. Keine neue Inferenz oder
reale Providerqualifikation wurde durch diese Beobachtung erzeugt. Reale
Accountbilder und vollständige Antworten bleiben ausschließlich im privaten Backup.

Nach Abschluss wurden zehn nicht mehr benötigte `bin`-/`obj`-Verzeichnisse
entfernt (2.272 MiB logische Dateigröße). Quellen, externe Harnessprojekte und
Locks, Pakete, Backups, Protokolle und Bildbelege bleiben erhalten. Die unten
bzw. im Quellenbericht genannten Harness-Buildpfade beschreiben den Prüflauf;
die gelöschten Assemblies lassen sich aus den festgehaltenen Inputs neu bauen.
