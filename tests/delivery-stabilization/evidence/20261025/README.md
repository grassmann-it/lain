# Lieferbelege: Kontingente und Zustandsfolgen, 25.10.2026

Quellfreeze: `d7200d7d700bd2d23abf8bf028d40526eb235bf7`.

| Datei | Ausgeführter Prüfumfang |
| --- | --- |
| `publish.json` | Getrenntes Monitor- und Gatewaypaket, Manifest- und Archivhashes |
| `v4-runtime.json` | Isolierter synthetischer v4-Update-/App-Rückkehrversuch beider Modi; keine allgemeine Downgradegarantie |
| `packaged-ui-identities.json` | Exakte ausgelieferte Produktions-DLLs und nativer Harness |
| `packaged-ui-scale-1.json` | 18/18 native Paketfälle, 377 Assertions, 46 Bilder |
| `packaged-ui-scale-1.25.json` | 4/4 native Paketfälle, 70 Assertions, zwölf Bilder bei tatsächlicher Skalierung 1,25 |
| `installation.json` | Verifiziertes reales Update, Revisionen 19/2, 15 Accounts und 14 historische Attempts erhalten |
| `installed-ui.json` | 100 gezielte Read-only-Checks, acht gerenderte Bilder; tatsächlich besucht: Accounts, Live, Profiles; übrige Navigation sichtbar geprüft |

`SHA256SUMS` sichert diese sieben JSON-Dateien. Ursprüngliche private
Installationsantworten und echte Accountbilder bleiben im privaten Backup;
die installierten UI-Belege hier enthalten nur kuratierte Ergebnisse und
Bildhashes. Paket-UI-Belege verwenden ausschließlich synthetische Daten.
Persönlich gesichtete Bilder sind in den getrennten Reviewberichten benannt;
ein erzeugtes Bild gilt allein noch nicht als visuell geprüft.

Live meldete verbunden, Cursor 0, keine Streams, keine Drops und keinen Fehler.
Historische Attempts werden nicht nach Neustart als Ausgabe wiedergegeben.
Der installierte Lauf qualifiziert den Leerhinweis und Host-/Historieerhalt
über Fenster-Schließen/Wiederöffnen, keine neue Inferenz und keinen zusätzlichen
Hostneustart. Seine Eingaben sind geroutete Controlereignisse; die Paketläufe
prüfen separat echte native Eingaben. Der vorherige breite installierte
Legacytest bleibt wegen seiner veralteten Navigationserwartung fehlgeschlagen.

Zehn der 14 historischen Attempts haben unbekannte Usage. Die bekannte
Teilsumme von 346.981 Tokens aus vier Messungen ist kein vollständiger
Gesamttokenwert. Neue reale FREE-Modellproben wurden nicht gesendet.
