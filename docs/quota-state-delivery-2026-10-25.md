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

Paket- und Installationsnachweise folgen nach dem Einfrieren der endgültigen
Quellen. Die installierte Ausgangsversion ist `793822c`, gewünschte/aktive
Revision zuletzt 19/2. Beim Update werden die dann aktuellen Revisionen erneut
erfasst; ein automatisches Apply gehört nicht dazu. Der eigene Dienst und der
alte Proxy bleiben getrennt. Neue Pakete, isolierter v4-Updateversuch und echte
Installation sind keine aus den obigen Offline-Suiten abgeleiteten Freigaben.
