# lain — Account Monitor und Local AI Gateway

lain verwaltet lokale Provideraccounts, Kontingente und gespeicherte
Nutzungsdaten für Linux x64. Desktop und CLI verwenden denselben privaten
.NET-10-Host. Der Host kann unabhängig vom Fenster weiterlaufen.

Eigener Code: [Artistic License 2.0](LICENSE), mit getrennten
[Drittanbieter- und Bildhinweisen](NOTICE). Katzenfoto:
[Alexander Huxel · Webentwicklung Huxel](https://webentwicklung-huxel.de/)
[(Flickr)](https://flickr.com/people/204852984@N05/).

Im aktuellen **Quellstand** sind zwei ausdrückliche Betriebsarten implementiert:
**Monitor** liest qualifizierte Accountkontingente und vorhandene lokale History
ohne installierten oder gestarteten Proxy-Core. **Gateway** ergänzt Inferenz,
Modelle, Profile und den eigenen gepinnten CLIProxyAPI-Core. Ein Proxyfehler
schaltet einen Gateway nicht automatisch auf Monitor um. Informationsstufe
Barebones / Simple / Advanced, lokale Dichte und Betriebsart bleiben getrennt.

**Quellstand, geprüfte Pakete und installierter Dienst sind getrennte Nachweise.**
Die integrierten Account-/Stabilisierungs-/UXD-Änderungen sind durch den
Umsetzungsauftrag autorisiert. Sie sind damit noch kein neu installiertes oder
freigegebenes Release. Historische Installationsberichte und Screenshots gelten
nur für die dort genannten Binärhashes; alte Prüfzähler beweisen den aktuellen
Stand nicht. Die private Monitor-/Gateway-Paketabnahme und ein späterer
Dienstwechsel folgen erst nach den integrierten Prüfungen.

```sh
lain --help
lain status --json
lain mode --json
lain usage
lain history --today --json
lain accounts methods --json
```

Diese Befehle gehören zum aktuellen Quellvertrag. Mit `lain --help` zuerst die
Fähigkeiten der tatsächlich installierten CLI prüfen; Entwicklung aktualisiert
keine Installation. `usage` zeigt gespeicherte Kontingentsnapshots, `history`
zeichnet die vom Gateway erfassten Requests/Tokens aus. Unbekannte Werte bleiben
unbekannt. Ein Login oder gespeicherter Key beweist keine Inferenz-/Quotarechte;
öffentliche Recherche, synthetische Fixtures und echte Providerqualifikation
werden getrennt ausgewiesen. Console-/OAuth-/CLI-Methoden benötigen ihren eigenen
qualifizierten Client-/Sessionvertrag; reine Readerfixtures aktivieren keinen Login.

Accounts sind der Einstieg. Die native Oberfläche erhält gemeldete Quotafenster,
Accountidentitäten, Entwürfe, Kontextnavigation und unabhängige lokale
Fensterpräferenzen. Genau ein Informationsmodusdropdown steht unten rechts.
Settings enthält Dichte, Fenster/Benachrichtigungen und Hilfe/About; ein
Monitor-/Gatewaywechsel ist eine eigene revisionsgebundene Hostaktion.
About liest die echte Desktopassemblyversion und zeigt die gemeldete Hostversion
getrennt. Originalkatze, Mint-/Grün-/Schwarzfarben und gebündelte Schriften bleiben.

Gateway bindet CLIProxyAPI **v8.0.41** als verifizierte externe Abhängigkeit ein.
Eigener Adapter und explizite Hookpatches erhalten Accountbindungen, Journal und
Transportgarantien; dies ist kein unverändertes offizielles Releasebinary. Die
exakte Patch-/Quell-/Buildidentität steht im Paket, nicht allein in dieser
Upstreamversionsnummer. Monitorpakete enthalten diesen Core nicht.

```sh
scripts/publish-linux.sh --mode monitor /absolute/new/lain-monitor
scripts/publish-linux.sh --mode gateway /absolute/new/lain-gateway
```

Publish erzeugt private Stagingartefakte mit Prüfsummen, Komponentenprovenienz
und passenden Drittanbieternachweisen. `gate-status.json` trennt Backendprüfung
von vollständiger Releaseabnahme. Kein Publisher wählt eine Anwendungslizenz,
veröffentlicht extern oder aktiviert einen Dienst.

- [Referralprogramme und Bezugsstellen eigener Links](docs/provider-referrals-overview-2026-10-26.md)
- [Free-/Abovergleich aller Connectoren (Preisprüfung noch offen)](docs/provider-recommendations-2026-10-26.md)
- [OAuth-/Device-Logins: Bestand und nächste Integrationen](docs/oauth-provider-overview-2026-10-26.md)
- [1.0-Kandidat: Lieferumfang und Installation](docs/release-1.0-distribution-readiness.md)
- [Linuxinstallation](docs/install.md) und [Betriebsarten, Release und sichere Wiederherstellung](docs/linux-delivery-stabilization.md)
- [Aktuelle Arbeitspakete und Nachweisgrenzen](docs/work-packages-execution-2026-10-24.md)
- [UXD-Pakete](docs/ux-design-work-packages/README.md) und [Reviewreferenzen](docs/uxpilot-design-results-2026-10-24.md)
- [CLI-Befehle](docs/cli-usage.md), [Tray](docs/desktop-tray.md), [API-Verträge](docs/service-contracts.md)
- [Entwicklung](docs/development.md), [Backendqualifikation](docs/backend-qualification.md), [Architektur](docs/architecture.md)
- [Historisch installierter v8-Core und Grenzen](docs/core-v8-upgrade.md)
- [Isolierte Prüfung tatsächlicher Pakete](tests/delivery-stabilization/README.md)

Updates erhalten Accounts, eigene Namen, getrennte Credentials, History, Profile,
Routen und gewünschte/aktive Revisionen. Ein synthetischer Bestand mit gewünschter
Revision11 und angewandter Revision2 muss genau dieses Paar behalten; ein Update
ist kein implizites Apply. `app.previous` allein ist kein Daten-/OAuthrollback.
Konsistente gestoppte Snapshots werden explizit in einen neuen privaten Zustand
wiederhergestellt. Verbrauchte OAuth-Refreshgrants bleiben bis zur gezielten
Recovery bzw. zum Reconnect gesperrt; brauchbare statische Accounts und History
bleiben zugänglich. Alte Hosts ohne diese Schutzverträge dürfen nicht blind mit
wiederhergestellten Credentials gestartet werden.

Legacyproxy, fremde CLI-Profile und die normale Codexkonfiguration bleiben
außerhalb dieses Besitzes. Lain verwendet eigene Dienste, Daten und Secrets.
Codexintegration schreibt ihre separaten `lain.config.toml`/`lain.models.json`;
der Authhelper liefert nur den lokalen Inferenzschlüssel. Kauf, Paid-Fallback,
öffentliche Veröffentlichung und Anwendungslizenzwahl sind keine Nebenwirkung
dieser Arbeit. Die Anwendungslizenz ist Artistic-2.0; Repoöffentlichkeit ist beauftragt.
Ein öffentlicher Release mit Tag und Binärdownloads bleibt ein eigener Schritt.
