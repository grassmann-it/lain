# Öffentliche Sourcevorbereitung, 26.10.2026

Produktbasis: `882bdadd09235b35ca74c0ebdcff59e00061b2fe`.
Begleitdokumente, qualifizierte Desktoptestkorrekturen und der neue
Plattform-Contractworkflow werden separat auf dieser Basis zusammengestellt.
Die Main-Arbeitskopie und ihre uncommittierten Änderungen bleiben erhalten.
Kein neuer Produktbuild oder Ersatz der an Source882 gebundenen r4-Archive.

## Prüfung vor Repoöffentlichkeit

Gitleaks8.30.1 wurde aus dem offiziellen Release geladen und gegen dessen
SHA-256-Inventar geprüft. Scans mit vollständiger Redaktion und ohne
`gitleaks:allow`-Kommentarunterdrückung:

| Umfang | Treffer | Einzeln geklärte Einordnung |
| --- | ---: | --- |
| Remote-Main-Historie,436 Commits bis5dc916f | 280 | 254 Manifestdigests,12 Account-Aggregations-IDs,1 synthetischer Teststring,13 identische öffentliche Upstreamkonstanten/-fixtures |
| Zusätzliche Historie bis Source882 | 6 | 3 Build-/Quelldigests,2 ausdrücklich synthetische Teststrings,1 OAuth-Designsatz |
| Vollständiger Source882-Arbeitsbaum | 150 | 134 Build-/Quelldigests,12 Account-IDs,3 synthetische Teststrings,1 Designsatz |

Account-IDs wurden anhand ihrer Verwendung als `AccountId` und Aggregations-
`Key` im selben Beleg zugeordnet. Die betreffenden Upstreamzeilen wurden exakt
mit dem öffentlichen CLIProxyAPI-Commit
`9bdde54b59d1af70ae0534a0ef61b2c3361a1257` verglichen. Öffentlich ausgelieferte
OAuth-Clientkonstanten sind keine privaten Lain-Accountcredentials; daraus folgt
keine Erlaubnis, andere Clientidentitäten für neue Loginadapter zu verwenden.
Keine ungeklärten Treffer dieser drei Scans. Der zusammengestellte
Veröffentlichungstree wurde erneut gescannt:153 Treffer, davon dieselben150
und drei zusätzliche reine Beschreibungen in Providerdokumenten. Lokale
Markdownverweise und Whitespace sind geprüft. Kein Nachweis, dass automatische
Scans jede denkbare Offenlegung erkennen. Die geprüften Trees enthalten keine
Treffer der zusätzlichen Dateinamensuche nach privaten Auth-/Credentialdateien,
Schlüsselcontainern, Datenbanken oder nicht als Beispiel benannten `.env`-Dateien.

Toolprovenienz, redaktierte Scannerberichte, Klassifikation und exakte öffentliche
Upstreamvergleiche liegen privat unter
`~/.cache/lain-publication-audit-20261026/`. Keine Accountzugänge wurden gelesen
oder ausprobiert. Die Veröffentlichungsvorbereitung benötigt keine Dienstpause.

## Getrennte Freigaben

Artistic-2.0 und die bestätigte Huxel-Attribution sind enthalten. Der Nutzer hat
Repoöffentlichkeit beauftragt. Ein erfolgreicher Visibilitywechsel und der
Remotecommit müssen anschließend ausdrücklich geprüft werden. Letzter
Remotezugriff scheiterte an GitHub-DNSauflösung; daraus folgt keine Änderung
der Visibility. Öffentlicher Release, Tag und Binärdownloads sind noch nicht
veröffentlicht. Aktive Recherche bleibt unangetastet; lokale Update- und
Modellabnahme warten auf ein ruhiges Zeitfenster.

Der Plattformworkflow prüft synthetische Installerverträge auf Linux/macOS/
Windows, keine echten nativen Pakete oder GUI. Referral-/OAuthübersichten
benennen reale Gates. Eigene Empfehlungslinks fehlen noch und werden nicht
erfunden. Der zusätzliche Providervergleich für3–5 Empfehlungen läuft separat.
