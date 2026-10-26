# Quotaabhängige Auto-Routen: Umsetzung und Lieferstand

26.10.2026. Ausdrücklich beauftragt: frische vorhandene Quota-Messungen vor der
Modellwahl berücksichtigen und bei einem Limit vor Antwortbeginn zum nächsten
zulässigen Gruppenmitglied wechseln. Die bisherige No-Fallback-Regel ist damit
für diese begrenzten Fälle revidiert. Vertrag: [Collection Router](collection-router-v1.md).

## Verhalten

- Die konfigurierte Reihenfolge bleibt maßgeblich. Frische, eindeutig zuordenbare
  erschöpfte Fenster überspringen den jeweiligen Kandidaten. Mehrere Fenster
  werden gemeinsam berücksichtigt; ein freies Wochenfenster hebt ein erschöpftes
  Fünfstundenfenster nicht auf.
- Der Host übermittelt alle 15 Sekunden eine kleine Verfügbarkeitsprojektion aus
  seinem bestehenden Quota-Cache. Auswahl löst keinen Providerrefresh aus.
  Anfangs sind nachgewiesene Legacy-Kimi-Coding-/Z.ai-Coding-Verbindungen mit
  identischem Quota-/Inferenzschlüssel unterstützt. Andere Zuordnungen bleiben
  unbekannt. Die bestehende Quotaabfrage läuft weiterhin im Fünfminutentakt.
- Ablauf spätestens nach fünf Minuten ursprünglichem Messalter oder zum Reset.
  Wiederholtes Übermitteln erneuert die Messung nicht. Fehlende, fehlerhafte oder
  veraltete Daten sperren keine Route. Der zusätzliche Corecache ist im RAM;
  ein Neustart beginnt mit unbekannter Verfügbarkeit.
- Ein dennoch auftretendes 429/erkanntes Quotalimit kann vor jeder Ausgabe einen
  weiteren zulässigen Kandidaten versuchen. Je Mitglied höchstens ein echter
  Versand. Generische 429 sperren nur das Modell; nur erkannte accountweite
  Quotacodes betreffen dessen weitere Aliase. Kurze Cooldowns bleiben an die
  tatsächlich verwendeten Credentials gebunden.
- Kostenregeln und explizite Accountwahl bleiben verbindlich, auch bei einer
  Gruppe mit gemischten Kostenregeln. Nach Stream-/Toolausgabe kein Wechsel.
  Bei vollständiger Erschöpfung klarer Fehler statt Endlosschleife.
- Bestehende Threads bleiben gepinnt. Spätere Migration verlangt vollständige
  Messagehistorie und gepaarte Functionaufrufe/-resultate. Opaque Reasoning-/
  native-/Customtoolrecords oder bloße Fragmente führen beim nötigen Wechsel
  zu 409 `router_replay_required` mit Hinweis auf einen neuen Thread; sie werden
  nicht still entfernt. Same-Route-Continuations bleiben erhalten.

## Nachweise

Quellstand im separaten Cacheworktree:
`5fab859b8f603b7f7ce430d0d2fff748b05bd150`. Main-WIP bleibt unangetastet.
Corequellmanifest:
`1389394e9532150a2ba7c0422551ee376e1e3289360a8cac02a2d6e3675122b9`.

- 106 Hostchecks bestanden, einschließlich Ablauf/Reset, Credentialbindung,
 Konfigurations-/Coregenerationen, Grenzen und Monitor-/Stopped-Core ohne HTTP.
- Vollständige Go-Adaptersuite, gezielte Routertests und Router-Racetests
 bestanden. Exakte Befehle und Exitcodes:
 `~/.cache/lain-router-failover-20261026/verification.json`.
- Neuer Core gebaut, SHA256
 `90dfebb27ec7fd5184b54afa06e7105c279d12858f1948d5ed1e011413d80985`.
 Vollständige synthetische Prozess-/Backendqualifikation bestanden:
 Go 298 Testpasses ohne Skips, Python 22, Upgrade 8, Prozess 6, Provider 27,
 Kontext 14 und Archiv 12 Prüfungen. Offizieller Gesamtbeleg:
 `~/.cache/lain-router-failover-core-20261026/qualification-r2/qualification.json`
 (SHA256 `8e1ed8a4162bc2da88065cb46d4aaf8a9ca7ac1285b469be2148a918a8281971`).
- Monitor-Vollpaket gebaut; Archiv- und interne Prüfsummen sowie Lizenz, NOTICE
 und exakte mitgelieferte Source geprüft:
 `~/.cache/lain-quota-release-20261026/package-monitor.tar.gz`.
 Gatewaypaket ebenfalls gebaut, mit exakt passendem Core-/Quellmanifest und
 Originalqualifikation; Archiv-/Innenprüfsummen bestanden. Alle 11 isolierten
 Vollpaketprüfungen bestanden: frische Monitor-/Gatewaystarts, tatsächliche
 Assemblymetadaten, Upgrade, Backup/Recovery, App-Rückkehr, Fehlerumkehr und
 Prozess-/Integritätsguards. Bericht:
 `~/.cache/lain-quota-release-20261026/full-packages/runtime/report.json`.
 Artefakthashes: `~/.cache/lain-quota-release-20261026/delivery-artifacts.json`.

## Produktivstand und Aktivierung

Weiterhin installiert: `ff5771c`; gespeichert 58/angewandt 57. Luna ist per regulärer
CLI als vierter IT-Sec-Kandidat gespeichert. Echter Gruppenaufruf am alten Core
lieferte Kimi 429; separater direkter Luna-Aufruf lieferte 200 und eine sinnvolle
Antwort mit 183 Tokens. Dies ist kein Beleg für Fallback am neuen installierten Core.

Lesende Beobachtung des unveränderten eigenen Cores:
`~/.cache/lain-quota-release-20261026/activity-observation-r2/report.json`.
Acht Proben zeigten 7–9 aktive HTTP-Anfragen, Admission offen. Der Scannercheck
dauerte rund 0,89 Sekunden und verweigerte ein Update wegen offenem Lain-Desktop.
Keine Dienst-, Drain- oder Inferenzaktion. Die Anzahl beweist weder 7–9 unabhängige
Jobs noch einen Leak; die ursprüngliche Ursache des früheren Timeouts ist damit
nicht abschließend geklärt. Historische Updateinputs dienen in dieser Diagnose
nur der exakten Bestandsidentität und sind keine frische Updatefreigabe.

Vor Installation: exakte native/Paketabnahme und neuen eigenen Updateseal
abschließen;
frische 58/57-Erhaltungsinputs und eigenen Updatebeleg erzeugen. Offenes UI samt
Entwürfen und aktive Anfragen regulär abschließen. Keine laufenden Streams
abschießen. Danach regulär installieren, Revision 58 anwenden/Clientkatalog
exportieren und echten Gruppenaufruf samt Attempts gegen aktuelle Quotas prüfen.
Keine neue Installation, Veröffentlichung oder vollständige 1.0-Abnahme behauptet.
