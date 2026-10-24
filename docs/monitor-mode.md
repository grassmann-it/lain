# Optionaler Monitor-Modus

Stand 24.10.2026. Dies ist ein Architekturentwurf für einen separaten Ausbau;
die laufende UI-Strukturlieferung bleibt der Gateway-Stand.

## Bestätigtes Ziel

Der Nutzer möchte lain auch für Menschen anbieten, die Kontingente sehen, aber
keinen lokalen CLIProxyAPI-Core installieren oder betreiben möchten. Auf die
Umfangsfrage hat er ausdrücklich gewählt: **„Kontingente und Resets zuerst;
Request-/Tokenhistorie später ergänzen“.** Die Produktbezeichnung lautet
**lain — Local AI Gateway**; ein neuer Produkt- oder Befehlsname wurde hierfür
nicht beschlossen.

## Vorgeschlagener Zuschnitt

Ein optionaler Betriebsmodus **Monitor** ergänzt **Gateway**. Das ist eine
bewusste Installations-/Betriebswahl, kein automatischer Failover bei Proxyfehlern.
Der kleine eigene .NET-Host übernimmt weiterhin Kontingentabfragen, lokale
Accounts und die API für Desktop/CLI. Ein Betrieb ohne jeden Hintergrunddienst
ist damit nicht vorausgesetzt.

| Fähigkeit | Gateway | Monitor, erster Stand |
| --- | --- | --- |
| Accounts, benannte Quota-Fenster, Prozent/Rest/Reset | Ja | Ja, mit qualifizierten Zugangsarten |
| CLIProxyAPI-Binary, Prozess, Inferenzport | Ja | Nicht installiert oder gestartet |
| Proxy-Routen, globale Profilanwendung, Clientexport | Ja | Keine Monitor-Funktion |
| Persistente Request-/Tokenhistorie | Eigenes qualifiziertes Journal | Nicht erfasst; später eigene Quellenintegration |
| Live-Inspector | Eigener begrenzter Puffer | Keine Monitor-Funktion |

Die Oberfläche richtet sich nach Fähigkeiten: Accounts/Kontingente als Einstieg,
Anbieter entdecken und passende Zugangseinrichtung. Proxy-Anleitungen und
„Proxy offline“ erscheinen nicht als Fehler eines gesunden Monitors. Gemeinsame
Kontingentkacheln, Presets, Dichte, Aktualität, unbekannte Werte und Shared-Budgets
werden wiederverwendet. Eine leere Usage-Seite darf keinen Nullverbrauch behaupten.

## Nachgewiesene Trennstellen und Grenzen

- `src/Lain.Host/Quota/QuotaService.cs` verwendet Accounts, Credentialquellen und
  direkte Anbieteradapter. Diese Abfragen benötigen keinen Inferenzproxy.
- `src/Lain.Host/Program.cs` registriert Journal/Inspector derzeit unbedingt und
  startet vorhandene aktive Proxykonfigurationen. Installation, Hostfähigkeiten,
  Endpunkte und UI brauchen deshalb eine ausdrückliche Monitor-Variante.
- `scripts/install-linux.sh` verlangt heute das Core-Binary. Ein unsichtbarer oder
  absichtlich fehlgeschlagener Proxy genügt nicht als „ohne lokalen Proxy“.
- Die eigene Codex-Anmeldung startet aktuell den Core; ihr Backend
  `Accounts/HttpCodexOwnedLoginBackend.cs` spricht dessen Login-Endpunkt an.
  Im Monitor muss diese Funktion fehlen oder separat ohne Core qualifiziert
  werden. API-Key-Zugänge und externe Access-Snapshots sind mögliche erste
  Zugangsarten, aber der vorhandene Legacyimport ist noch kein allgemeiner
  Importer für Codex-/Claude-Clientdateien. Externe Snapshots werden nicht
  refreshed oder revoked; Ablauf und erneuter lesender Import bleiben sichtbar.
- Quotas sind derzeit Momentaufnahmen im RAM. Eine historische Kurve von
  Kontingentständen wäre ein eigener Umfang; sie ist keine Tokenhistorie.

## Spätere Historie

Kontingentänderungen werden niemals in vermeintliche Requests oder Tokens
umgerechnet. Jede spätere Quelle benötigt dokumentiertes Format, Herkunft,
Einheiten, Identität, Abdeckung, Deduplizierung und quellenspezifischen Fortschritt.
Client und Proxy können dieselbe Nutzung sehen; solche Werte dürfen nicht
automatisch addiert werden. Unbelegte Account-/Credentialzuordnungen bleiben
unbekannt.

Der aktuelle `Usage/SqliteUsageStore.cs` besitzt einen globalen Collectorstand;
Quellenwechsel und Fehler werden nicht ausreichend pro paralleler Quelle
isoliert. Mehrquellenbetrieb erfordert deshalb eine gezielte Vertragserweiterung,
keinen zweiten Leser am bestehenden Collector. Fremde Proxyqueues werden nicht
ungefragt konsumiert. Zunächst wird genau ein Quellentyp qualifiziert, sobald
dieser Ausbau beauftragt ist.

## Vorgeschlagene Abnahme des ersten Monitor-Stands

1. Frische Installation und Neustart funktionieren ohne Core-Binary, Core-Prozess
   oder Inferenzport; Login und Hintergrundcollector starten ihn auch indirekt
   nicht.
2. Vorhandene qualifizierte Kontingentadapter zeigen dieselben belegten Werte und
   Reset-/Freshness-/Shared-Zustände wie im Gateway.
3. Nur verfügbare Zugangsarten und passende Aktionen werden angeboten; fehlende
   Requesthistorie wird als nicht erfasst bezeichnet.
4. Moduswahl oder späterer Wechsel verändert weder fremde Proxykonfigurationen
   noch vorhandene History und führt nicht still zum Start eines Gateways.

Noch zu entwerfen: konkrete Installationsauswahl und Paketaufteilung,
Fähigkeitsvertrag der API, erste Zugangsarten je Anbieter und ein nachvollziehbarer
Wechsel zwischen Modi. Diese Punkte sind kein Blocker der aktuellen UI-Lieferung.
