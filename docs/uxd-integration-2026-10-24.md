# UXD-00: integrierte Anschlüsse und Seitengrenzen

Bezugsstand: `a890903`, ergänzt durch die nachfolgende integrierte Abnahme.
Der Umsetzungsauftrag umfasst AC-00–10, ST-01–05 und UXD-00–09.
Die früheren Planungsgrenzen der Paketdateien sind historische Auftragsstände.
Ausführungsnachweise stehen im [gemeinsamen Ledger](work-packages-execution-2026-10-24.md).

## Tatsächliche API-Verträge

Alle Pfade liegen unter `/api/v1`, sind lokal authentifiziert und werden durch
`LainApiClient` beziehungsweise `ObservabilityClient` verwendet. Credentials
werden privat übermittelt; öffentliche Antworten enthalten ausschließlich
Credential-IDs, Generationen und Fähigkeiten. Die folgenden Beispiele sind
synthetisch, keine echten Zugangsdaten.

| Anschluss | Eingabe und Ergebnis | Konflikt-/Fehlerverhalten |
| --- | --- | --- |
| `GET /status` | `runtimeMode`, `capabilities`, gewünschte/angewendete Revision, `application`; aktive Profilidentität kann unbekannt sein | Offline ist ein Transportfehler. Fehlende Felder eines alten Hosts beweisen keine neue Fähigkeit. |
| `PUT /runtime-mode` | `{"revision":4,"mode":"monitor"}` → `StatusDto` | Veraltete Revision: 409. Monitor wartet auf das Ende eigener Coreaktivität; Gatewayauswahl führt kein Apply aus. |
| `GET /connection-methods?provider=kilo` | `ConnectionMethodCatalogDto`, installierte Methoden mit Zweck, Voraussetzung und Verfügbarkeit | Ein vorhandener Katalogeintrag ist kein erfolgreicher Login oder Anbieterzugriff. |
| `POST /accounts/connect` | `RevisionRequest<ConnectCredentialRequest>` → `ConfigurationDto`; Account ohne Modelle möglich | Revision und Accountgeneration werden geprüft; falsche Ressource/Identität ersetzt keinen bestehenden Zugang. |
| `POST /accounts/login` | Revision, Provider, Methode, Zwecke und optional Zielaccount/Generation → `LoginDto` | Nicht verfügbare Methode: 409 vor Anbieteraktion. Wartende Operation bleibt über ihre ID erreichbar. |
| `GET` / `DELETE /accounts/login/{id}` | Status lesen beziehungsweise abbrechen → `LoginDto` | Cancel und Poll warten nicht auf den globalen Apply-Lock. Terminale Operationen werden begrenzt aufbewahrt. |
| `POST /accounts/login/{id}/resource` | Revision und `ResourceBindingDto` | Nur entdeckte autorisierte Ressourcen; genau eine passende Ressource wird automatisch gewählt, Mehrdeutigkeit erfordert Auswahl. |
| `POST /accounts/{id}/models` | Revision, Generation, `connectionId`, `modelRouteIds[]`, optionale Settings | Host verwendet ausschließlich den privaten Zugang des exakten Zielaccounts; kein Secret-Roundtrip durch die UI. |
| `GET /profiles/{id}/preview?revision=4` | `ProfileApplyPreviewDto`, globale Routenänderungen und Verhalten laufender Streams | Veraltete Vorschau: 409; unbekannte aktive Basis bleibt unbekannt. |
| `POST /profiles/{id}/apply` | `{"revision":4}` → Konfiguration | Explizite globale Anwendung, Drain und atomare Veröffentlichung; unklare Wiederherstellung blockiert einen ungeprüften Neustart. |
| `POST /profiles/{id}/codex/export` | Revision sowie absolute Helfer-/Katalogpfade → Clientartefakte | Vorbereitung, atomare lokale Installation und tatsächlicher Clientstart bleiben getrennt. |
| `GET /quotas` | `QuotaDto[]`, unabhängige Fenster/Scopes und optionale Metadaten-Aktualität | Unbekannt bleibt null; alter Pass-/Keymetadatenstand wird durch ein frisches Wallet nicht frisch. |
| `GET /usage`, `/usage/analysis`, `/attempts` | UTC-Zeiten, explizite Zeitzone, begrenzte Seiten/Gruppen | Bekannt gemeldete Teilsumme und Messabdeckung getrennt von Collectorvollständigkeit; ungültige Abfragen: 400. |

Privates Connectbeispiel:

```json
{
  "revision": 4,
  "value": {
    "connection": {
      "provider": "kilo",
      "method": "api-key",
      "purposes": ["quota"],
      "name": "Kilo"
    },
    "secret": "synthetic-fixture-only",
    "kind": "api-key"
  }
}
```

Der tatsächliche Methodenwert wird aus dem Methodenkatalog gewählt. Das Beispiel
ist kein hartcodierter Ersatz für dessen Verfügbarkeit. Modelattachment verwendet
zum Beispiel `{"revision":5,"value":{"expectedGeneration":1,
"connectionId":"fixture-connection","modelRouteIds":["fixture-model-a",
"fixture-model-b"]}}`; die IDs stammen aus dem installierten Providerkatalog.

## Betriebszustände

Monitor ist eine gespeicherte Absicht. Er läuft ohne Core und ohne Inferenzsecret,
hat eigene Quotaleser und bewahrt vorhandene History. Ein Gateway mit fehlendem
Core oder gestörter Anwendung bleibt als Gateway reparierbar. Ein neuer Gateway
ohne angewendetes Profil startet keine gespeicherte Wunschkonfiguration heimlich.

Nach Restore werden alte Coregrants zuerst quarantänisiert. Ein expliziter neuer
Codexlogin kann eine leere Managementruntime starten. Deren Konfiguration,
Bindings und Journal liegen separat; `login-only` bedeutet nicht Inferenzbereitschaft.
Das frühere aktive Profil und seine Beweisdateien bleiben dabei unverändert.

## Seitenvertrag und Zuständigkeit

`Ui.Viewport` / `WorkspacePage` geben Seiten eine begrenzte Arbeitsfläche mit
internen Scrollbereichen. `WorkspaceNavigationRequest` trägt Objekt- und
Profilidentität, Rückziel und vorhandenen Kontext. Präferenzen und Editorentwürfe
verwenden die bestehende Speicherung; es gibt keine zweite Zustandsverwaltung.

- Shell/Accounts besitzen Navigation, gemeinsame Commands, Scroll-/Fokusanker
  und den reservierten benachbarten Accountinspector. Schmal ersetzt Detail die Liste.
- Connect/Models besitzen Anbieterauswahl, Loginzustände und Modell-/Policyeditoren.
- Profiles, Usage, Live und Settings besitzen jeweils ihre eigene Seitendatei;
  gemeinsame Verträge und Clientanschlüsse gehören dem Integrator.
- UI-Abnahme besitzt synthetische Fixtures und private Xvfb-Harnesses. Ein grüner
  Geometriecheck ersetzt weder lesbare Ergebniszeilen noch Bildprüfung.

Überprüfbare Layoutentscheidungen: Accounts starten zuerst; Profiles bleiben im
Gateway direkt erreichbar. Barebones zeigt Accounts/Profiles/Usage, Simple ergänzt
Models und Live unter More, Advanced zeigt Live direkt. Monitor blendet reine
Gatewayaktionen aus. Genau ein Modusdropdown steht rechts unten. Breite
Accountdetails reservieren Inventarfläche; der Preis ist weniger gleichzeitiger
Überblick. Schmale Details verwenden Back mit erhaltenem Ursprung. Diese konkrete
Umsetzung wird anhand der 14 Referenzen und der 800×560-Fälle geprüft; historische
positive Rückmeldungen ersetzen diese Abnahme nicht.

## Nachweise und Grenzen

Die gemeinsamen Host-/Clientanschlüsse sind implementiert. Foundation-, Cloud-,
Profil-, Transport- und unabhängige Fixtures prüfen ihre Fachverträge. Die echte
Hostprozessprobe ergänzt Monitor ohne Core und Restore mit separater Loginruntime.
Methodenspezifische externe Registrierungen, Rechte und reale Anbieterprüfungen
bleiben getrennt; ein bestandener Fakeflow aktiviert keine unqualifizierte Methode.
Die laufende Bild-/Eingabeabnahme, exakte Pakete und Lieferung bleiben bis zu ihren
aufgezeichneten Ergebnissen offen.
