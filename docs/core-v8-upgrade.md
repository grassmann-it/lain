# CLIProxyAPI v8.0.41: Upgrade und externe Abhängigkeit

Der Nutzer hat am 24.10.2026 das direkte Upgrade beauftragt: möglichst unveränderte
CLIProxyAPI-Software verwenden und bestehende lain-Funktionen erhalten. Damit ist
die v7-Pinnung der ersten Lieferung für diesen Folgeauftrag aufgehoben.

## Implementierung

CLIProxyAPI wird aus dem offiziellen, gehashten v8.0.41-Modularchiv mit Commit
`8289ec4c71ad914d0612c2776352b76ba3d355a6` bezogen. Der vollständige vendorte
Quellbaum entfällt. `proxy/adapter` enthält den eigenen Launcher, Accountbindungen,
das dauerhafte Journal, den begrenzten Inspector und die Login-Eigentumsregeln.
Providerimplementierungen und Protokollübersetzung kommen weiter aus CLIProxyAPI.

Ein unverändertes offizielles Binary kann die vorhandenen Garantien nicht liefern:
Usage gelangt zunächst in eine RAM-Queue; Executorregistrierung, Feldanwesenheit
und einige bestehende Transportparameter benötigen zusätzliche Hooks. Diese
Erweiterungen werden offen als Patchdateien geführt. Das Buildmanifest trägt
`upstreamSourceModified: true`; der Upstream-Modulcache bleibt unverändert.
Die genaue Abgrenzung steht in [proxy/README.md](../proxy/README.md), Herkunft und
Dateihashes in [proxy/upstream.json](../proxy/upstream.json).

Die Host-Produktionsquellen brauchen für dieses Upgrade keine Änderung. Seine
Startflags, `/lain/*`-Schnittstellen und das Journalformat bleiben erhalten.
Die Auslieferung ersetzt ausschließlich Core, Go-Lizenznachweise und begrenzte
Core-Provenienz/Metadaten. Host, CLI, Desktop, Konfiguration, Secrets und offene
Profiländerungen behalten ihren Stand.

Eine bewusst geschlossene Nebenfunktion ist native serverseitige Bildgenerierung:
Ihr zusätzlicher modellbezogener Usage-Datensatz passte schon im v7-Stand nicht in
den qualifizierten Ein-Versuch-/Primärusage-Vertrag. Explizite native Bildtools
werden deshalb vor Versand klar abgewiesen, automatische Injektion deaktiviert
und Payload-Overrides am letzten HTTP-Sendepunkt geprüft. Normale Function-Tools
und Bildeingaben zur Analyse bleiben erhalten. Das ist eine transparente
Einschränkung des zuvor ungeprüften Pfads, keine behauptete neue Bildunterstützung.

## Abnahme

Die laufende Qualifikation ist noch kein Installationsnachweis. Der finale
Nachweis muss den exakten Binary- und Source-Manifesthash verbinden und folgende
getrennte Gates enthalten:

- Adapter-/SDK-Regressionen für Dauerhaftigkeit, Accountbindung, Refreshgrenzen,
  Feldanwesenheit, Inspectorgrenzen und Free-only-Sendeprüfung.
- Synthetische Protokoll-, Retry-, Abbruch- und Ausfalltests im privaten
  Netzwerknamespace; zehnfache Übernahme durch den echten SQLite-Consumer.
- Vom echten Hostwriter erzeugte Chat-/Responses-Konfiguration und Geminiheader.
- Synthetischer v7→v8→v7-Journalübergang einschließlich alter Bytes und Source-ID.
- Frischer Schema-2-Paketbau und Core-only-Installation mit geschützten
  Anwendungskomponenten, privater Sicherung und Fehlerrollback.
- Kontrollierter lain-Neustart mit unveränderter gewünschter/aktiver Revision,
  Accounts, Modellen, Profilen und historischer Usage. UI-Prüfung nur privat in Xvfb.

Ausführbare Matrix: [Backendqualifikation](backend-upgrade-qualification.md).
Staging-/Installationsvertrag: [Core-only-Auslieferung](core-upgrade-delivery.md).

Neue Upstreamfeatures sind durch einen Versionswechsel nicht automatisch in lain
freigeschaltet oder live qualifiziert. Der bestehende Endpointumfang bleibt
bestehen. Reale OAuth-Rotation, neue Anbieterzugänge und neue Upstreamfähigkeiten
brauchen gesonderte Nachweise. Der persönliche Bestandsproxy und seine Queue
gehören nicht zu den Upgradeprüfungen.
