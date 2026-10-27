# M0 — Anbieter mit kleinen Modulen ergänzen

Stand 24.10.2026. **Bestätigte Anforderung:** Neue Anbieter sollen einfach
hinzufügbar sein. **Vorschlag:** vorhandenen Manifest-/Adapteransatz ausbauen,
zunächst als mitgelieferte und beim Build registrierte Module. Kein neuer
Inferenzproxy und kein vorausgesetzter Loader für fremde DLLs/Skripte.

## Lesender Codebefund

Bezugsstand: HEAD `416fe139318a94939270aab7f6489518f4487b74`, Tree
`8c5650c622c7d368fb80250263983299f5432e76`, 24.10.2026; keine Builds/Tests in
dieser Planungsrunde. Parallel laufen Repository-/Lieferarbeiten; eine spätere
Historienumschreibung kann Commit-IDs ändern. Vor Umsetzung relevante Dateien
erneut abgleichen. Der Befund ist kein vollständiges Code-/Securityreview.

| Schon vorhanden | Fundstelle | Grenze für weitere Anbieter |
| --- | --- | --- |
| 15 eingebettete JSON-Anbieterbeschreibungen, Schema/Validierung und generische Darstellung | [Katalog](../../src/Lain.Host/Providers/ProviderCatalogRegistry.cs), [Verträge](../../src/Lain.Core/ProviderContracts.cs), `src/Lain.Host/Providers/Catalog/*.json` | Die vier ursprünglichen Zugänge Codex/Command Code/Kimi/Z.ai liegen nicht als diese 15 JSON-Dateien vor. Bestehende Identitäten erhalten, nicht blind vereinheitlichen. |
| Wiederverwendbarer Transport nach Protokoll | [Transportvalidierung](../../src/Lain.Host/Providers/ProviderTransportValidation.cs), [Compiler](../../src/Lain.Host/Operations/ProxyConfigurationWriter.cs) | Zusätzliche Regeln nach Anbietername im zentralen Validator; Registry wird auch im Compiler neu erzeugt. Erweiterte Regeln müssen dort dieselbe Quelle verwenden wie beim Anlegen. |
| Modelllistenadapter mit HTTP-/Parsergrenzen | [DirectApiModelAdapter](../../src/Lain.Host/Providers/DirectApiModelAdapter.cs) | Feste Endpunktliste nur für Gemini/Groq/Mistral. Klassenexistenz beweist keinen allgemeinen UI-/Host-Refreshpfad. |
| `IQuotaAdapter`, normalisierte Mengen und separater QuotaService | [QuotaContracts](../../src/Lain.Host/Quota/QuotaContracts.cs), [QuotaService](../../src/Lain.Host/Quota/QuotaService.cs) | Acht Adapter in [Program.cs](../../src/Lain.Host/Program.cs) explizit erzeugt; `FirstOrDefault(Supports)` löst Mehrdeutigkeit nicht. |
| Generisches API-Key-Setup in der Desktopoberfläche | [Einrichtung](../../src/Lain.Desktop/MainWindow.Providers.cs), [AccountService](../../src/Lain.Host/Accounts/AccountService.cs) | Formular verlangt genau ein Secret und HTTPS; AddApi verlangt nichtleeren API-Key. Kein fertiges No-Auth-Setup für lokale Server oder Monitor-only-Setup ohne Route. |
| Endpunktprüfung teilweise schon für Loopback ausgelegt | `AccountService.ValidateEndpoint`, `ProviderCatalogRegistry.EndpointUrl` | AccountService erlaubt HTTP nur Loopback, Katalogvalidierung und Providerformular verlangen HTTPS. Lokale Katalogintegration braucht eine abgestimmte explizite Regel. |
| Abbrechbarer qualifizierter Loginvertrag | `IQualifiedLoginAdapter` in AccountService | Registrierung aktuell Codex-eigen; NanoGPT-OAuth ist noch kein vorhandener Login. |
| Referralrecherche und Offline-HTML | `docs/referral-research-*.json`, `docs/provider-referrals.html` | Kein einheitlicher Referralteil in `ProviderDescriptorDto`; persönliche Links und Programminformation nicht mit API-Keys vermischen. |

Die vorhandene [Transportqualifikation](../providers/transport-qualification.md)
und [Serviceverträge](../service-contracts.md) bleiben die Basis. Daraus folgt
kein Bedarf, Streaming/Tools/History je Provider erneut zu erfinden.

## Vorgeschlagener Modulzuschnitt

Ein Anbieter bündelt Beschreibung und die **tatsächlich benötigten optionalen
Fähigkeiten**. Gateway, unabhängiger Monitor, externe History und Referral
sind unabhängig. Unterstützte Auth hängt zusätzlich von Produkt/Tarif/Region ab.

| Baustein | Gewöhnlicher OpenAI-kompatibler Anbieter | Sonderfall |
| --- | --- | --- |
| Beschreibung | JSON: stabile ID, Quellen, Zugang, Tarife, Modell-/Protokollrouten, Restriktionen | Quellenstatus und Livebeleg separat; keine Berechtigung aus Werbung ableiten. |
| Gateway/Transport | Vorhandenen Protokolladapter wählen, konkrete Endpunkte und Settings validieren | Kleine anbietereigene Policy für spezielle Header, Regionen oder Produktgrenzen. Beide Validierungsstellen nutzen dieselbe Policy. |
| Modellliste | Optionaler registrierter Reader mit geprüftem Endpunkt/Auth/Schemavariante | Fester geprüfter Katalog bleibt zulässig, wenn keine sichere Modellliste vorliegt. |
| Monitor | Optional vorhandenes `IQuotaAdapter` implementieren/registrieren | API-Key, separater Lesetoken oder Snapshot je nach Zugang; Refresh/Rotation bleibt beim Authpfad. |
| Anmeldung | API-Key wiederverwenden; kein neues Loginmodul nötig | OAuth/PKCE, IAM oder lokaler No-Auth-Server separat qualifizieren. |
| Referral | Öffentliche Programmregeln plus eigener freigegebener Link als getrennte Konfiguration | Abgelaufen, kontobezogen oder Kanal ungeklärt: kein aktiver generischer Referralbutton. |
| Historie | Bestehende lokale Proxyusage weiterverwenden | Optionaler externer Quellenadapter erst nach Quellen-/Deduplizierungsvertrag. |

Die konkrete C#-Form darf der Implementierer passend zum Bestand vorschlagen,
z.B. eine kleine Modulregistrierung mit optionalen Transport-/Modell-/Quota-
Adaptern. **Keine neue öffentliche Interfacefamilie gilt durch diese Namensidee
bereits als beschlossen.** Ziel ist eine nachvollziehbare Registrierungsstelle
statt wachsender Anbieterswitches in Host, UI und CLI.

## Umfang des ersten Pakets

1. Zuerst SambaNova anhand eines Manifests und vorhandenen Chat-Transports
   durchgängig vorbereiten. Nur die dabei belegten zentralen Kopplungen lösen.
2. Fireworks als zweiten unabhängig registrierten Anbieter ergänzen, mit eigener
   optionaler Quotaimplementierung. Das zeigt, ob Beschreibung/Transport und
   zusätzlicher Monitor wirklich getrennt erweiterbar sind.
3. Gemeinsame Registrierung validiert IDs und Adapterzuordnung. Überschneidende
   Supports-/Produktzuständigkeiten entweder explizit auflösen oder ablehnen,
   niemals von zufälliger Listenreihenfolge abhängig machen.
4. Anbieter- und Modellinformationen an GUI und CLI über denselben Hostvertrag
   liefern. Typische neue Anbieter benötigen keine neue Providerkarte oder
   anbieterspezifischen Switch in der UI. Echte neue Authart bleibt ein eigenes
   Paket, keine versteckte Manifestoption.
5. Zusatzfelder am öffentlichen DTO nur additiv und mit klaren Defaults; alte
   Accounts, Routen, Credential-/Journalidentitäten und Clientantworten erhalten.
   Keine pauschale Migration der ursprünglichen vier Anbieter erzwingen.

**Abnahme:** Zwei Anbieter lassen sich durch Manifest, optionale eigene Adapter
und Registrierung integrieren. Bei gemeinsamem Protokoll keine Kopie des
Inferenzexecutors und keine providerbezogene neue Desktop-/CLI-Seite. Tests zeigen
korrekte Zuordnung bei zwei Accounts, fehlendem optionalen Adapter, unbekannten
Feldern/Endpunkten und kollidierender Registrierung. Der bisherige
Transport-/Policy-/Account-/Quota-Vertrag besteht seine passenden Regressionen.
Keine bezahlte oder externe Inferenz nötig für diesen lokalen Strukturbeleg.

Die schon vorhandene Generik darf unverändert bleiben, wenn sie dieses Ziel
erfüllt. Nicht jedes zentrale Protokoll-Switch ist ein Problem; problematisch
ist eine neue **Anbieterspezialbehandlung** an vielen unabhängigen Stellen.

## Getrennte Folgepakete

- **M-LOCAL:** Kein-Auth- und optional authentifizierte lokale Server. Explizite
  erlaubte Loopbackziele, UI/Host/Katalog gleich; Cloudweitergabe bestehender Keys
  nicht versehentlich erweitern. Kein erfundener Pflicht-API-Key für den Nutzer.
- **M-MONITOR:** Einrichtung mit Lesetoken ohne Inferenzroute. Zugang/Scope je
  Fähigkeit, ggf. mehrere Credentialreferenzen am bestehenden Account; konkrete
  Schema-/Migrationswahl vorher zuschneiden. Standaloneinstallation bleibt
  [separater Monitorumfang](../monitor-mode.md).
- **M-REFERRAL:** Programm-/Status-/Prämienmetadaten und Linkkonfiguration, klare
  Kennzeichnung des Entwicklernutzens. Ein fehlender Code darf kein Accountsetup
  blockieren. Im ersten Schritt Link öffnen; NanoGPTs native OAuthattribution ist
  ein separates Loginpaket mit Zustimmung und ungeändertem bestehenden Referrer.
- **M-HISTORY:** Externe Historie erst mit Herkunft, Identität, Cursor, Scope und
  Deduplizierung. Weder Kontostandsdifferenzen noch Header werden zu Requests erklärt.

Offen vor entsprechender Umsetzung: konkrete Registrierungsform und DTO-Ergänzung,
Linkkonfigurationsort/Verteilung, erste Monitor-only-Zugangsarten, lokales Auth-
und Runtimeverhalten. Diese Fragen blockieren nicht die bestehenden Manifest-
oder Quota-Fixtures. Runtime-Drittplugins bleiben eine spätere Alternative mit
eigenem Vertrauens-/Updatekonzept, kein Ziel des ersten Pakets.
