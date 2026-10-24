# AC-01 – Accountbasis, eigenständige Monitorzugänge und Loginoperationen

Planungsstand: 24.10.2026, Main `5dc916f`; keine Implementierung oder ausgeführten Prüfungen.
Grundlage: [Account-Connect-Entwurf](../account-connect-redesign-2026-10-24.md): Accounts/History erhalten, optionale Modelle, Monitor ohne Proxy.
Die unten konkretisierte technische Lösung ist ein Vorschlag; gemeinsame Verträge verantwortet Root.

## Prüffähiges Ergebnis und Grenze

Ein Account kann mit einem qualifizierten Monitorzugang entstehen, ohne Inferenzsecret, Modell,
Tariftemplate, Route, angewandtes Profil oder installierten Core. Er erscheint sofort mit getrenntem
Zugangs-/Monitoring-/Inferenzstatus. Spätere Inferenz ergänzt denselben Account; ID, Name und History bleiben.
Vorhandene Same-Key-Reader sowie separate Monitortokens benutzen denselben Resolver mit echten Rechten.
Monitorverbinden, Monitor-Reconnect und direkte Quota-Reads starten keinen Proxy oder Inferenzrequest.
Vorhandener Codex-Core-Login bleibt im Gatewaybetrieb funktionsfähig und explizit coreabhängig.
Neue Providerreader, Loginprotokolle, CLI-/Browserconnectoren und deren Qualifikation gehören in Folgepakete.
Ein coreunabhängiger Codex-Refresh ist dadurch ausdrücklich noch nicht geliefert.

## Gelesener Iststand: wiederverwenden und gezielt ändern

- `Accounts/AccountService.cs`: Revisiongate, Namenerhalt, Keyrotation und Duplikatschutz existieren;
  `AddApi` koppelt Connection an Billingplan und mindestens ein erlaubtes Modelltemplate, selbst bei null Routen.
  Neue IDs entstehen aus Provider+Key; `Check` verlangt immer ein Inferenzsecret. `Remove` behält Secrets privat.
- `Operations/OwnStatePaths.cs`: `SecretStore` liest aktuelle Core-owned Access-Tokens über Referenzen;
  Refreshmaterial verlässt den Core nicht. Private Dateirechte/atomarer Einzeldateischreibweg bleiben Grundlage.
- `Accounts/Monitoring/{MonitorAccessService,MonitorCredentialStore}.cs`: separater privater Speicher vorhanden,
  aber nur für vorhandene `api-key`-Accounts. Cloudflare-Scopeabgleich verlangt lesbare Inferenzbindung.
- `Program.cs`: eingebettete `HostQuotaSource` verlangt `HasAccount`; `QuotaCredential.FromSecretJson` und
  `QuotaService` verlangen nichtleeres Inferenztoken. Alle drei Stellen verhindern eigenständige Monitorcredentials.
- `LoginOperationService` in `AccountService.cs`: Poll/Cancel pro Operation serialisiert, bisher nur Providerkontext;
  keine Host-TTL, Kapazität oder Bereinigung, bei Beginfehler bleibt bereits angelegtes Semaphore zurück.
  Codex besitzt zusätzlich seinen begrenzten Core-Flow; dessen Grenzen gelten nicht für den allgemeinen Host.
- Startpfad ist `Program.cs` plus `Operations/OwnedProxyLifecycle.cs` (kein separater `ProxySupervisor`).
  Coremetadaten werden beim Boot gelesen; beschädigte optionale JSON-Dateien können den Hoststart verhindern.
  Gespeichertes aktives Profil startet Core vor APIstart; jeder Login startet Core oder schreibt Leer-Konfiguration.
  `Usage/JournalUsageCollector.cs` pollt unabhängig davon sekündlich und markiert bei fehlendem Core Coveragefehler.

## Umsetzungsschritte und Dateigrenzen

1. **Verträge vorziehen (Root):** Account-ID von Credential-ID lösen; Account enthält deklarierte/erkannte
   Provider-, Produkt-, Regions- und Ressourcenbindung mit Herkunft sowie getrennte Fähigkeiten/Status.
   Ein Credentialhandle nennt Zweckrollen, Generation, Ablauf und Owner; öffentliche Projektion enthält keine Secrets.
   Loginstart bindet Methode, Zweck, Neuanlage oder Ziel-ID, Startrevision und erwarteten Scope/Accountgeneration.
   Methode deklariert Coreabhängigkeit/Qualifikation; Status liefert zulässige Aktion, Ablauf und Zielaccount-ID.
   Scope-Discovery und Bestätigung haben eigene geheimnisfreie Schritte. DTO-Namen legt Root verbindlich fest.
2. **Accountservice entkoppeln:** `AccountService.cs` bekommt Create/Attach/Reconnect ohne Route-/Tarifpflicht.
   Zugangsmethode und echte unbekannte Ressourcenparameter validieren; Routentemplates nur bei optionaler
   Modellwahl prüfen. Bestehende `AddApi`-Signatur über Kompatibilitätsadapter weiterführen; kein neues Pflichtfeld.
   `Check` bewertet vorhandene Fähigkeiten statt fehlendes Inferenzfile als globalen Accountfehler auszugeben.
   Inferenz später über gezielten Zielaccount ergänzen; gleiches Credential kann mehrere Rollen referenzieren.
3. **Credentialzugriff vereinheitlichen:** `SecretStore` für Legacy/Core-owned erhalten; neue private Verwaltung
   unter `Accounts/Credentials/` liefert zweckgebundene aktuelle Access-Credentials und versionierte Referenzen.
   Same-Key-Monitoring referenziert den vorhandenen Key statt eine zweite Kopie zu speichern; separate Reads
   funktionieren mit ausschließlich eigenem Monitorcredential. Kein leerer/fiktiver Inferenzkey als Platzhalter.
   Owner unterscheidet statischen Key, externen Snapshot, Core, delegierte CLI und eigenen Sessionowner;
   ein Reader erhält nur nötigen Access-Zugriff, niemals Refreshmaterial oder Provider-Schreiboperationen.
4. **Monitorbasis umbauen:** `MonitorAccessService.cs`, `MonitorCredentialStore.cs`, `MonitorAccessCatalog.cs`
   erlauben Attach und Neuanlage über Accountservice ohne `api-key`-Voraussetzung. `inference-key` bleibt nur
   eine echte Referenz auf vorhandene passende Rechte. Bekannte Account-/Projektbindung vergleichen;
   unbekannte Inferenzbindung blockiert kein eigenständiges Monitoring, macht aber keine Bindung verifiziert.
   Unterschiedliche bestätigte Ressourcen nicht am selben Scope zusammenziehen; Produkt/Region mitprüfen.
5. **Quotaquelle lösen:** `HostQuotaSource` in eigene Datei `Quota/HostQuotaSource.cs` verschieben (Root entfernt
   die Definition aus `Program.cs`). Resolver erhält Account, Quellzweck und passende Credentialreferenz.
   `QuotaService.cs` prüft die vom Reader verlangte Rolle, nicht generell `BearerToken` eines Inferenzzugangs.
   Für vorhandene Adapter schmalen Legacyadapter erhalten; gemeinsame `QuotaContracts.cs` ändert Root.
   Passive Reader bleiben credentialfrei und im Monitorbetrieb „Beobachtung über Gateway erforderlich“;
   direkte Reader benötigen keinen Core. Kein Transport-/Inferenzfallback bei fehlendem Monitorreadrecht.
   Laufende Reads und Cache binden Credentialgeneration/Scope; nach Remove/Scopewechsel Ergebnisse verwerfen.
   Ablauf betrifft nur den gewählten Zugang; frischer Monitor bleibt trotz abgelaufener Inferenz nutzbar.
   Letzte Werte nur bei unveränderter Quelle/Scope mit ursprünglichem Beobachtungszeitpunkt bewahren.
6. **Loginverwaltung extrahieren:** Implementierung aus `AccountService.cs` nach `Accounts/LoginOperationService.cs`;
   Adapter führen Auth/Discovery aus, zentraler Commitdienst übernimmt Account/Credential erst nach Prüfung.
   `CodexOwnedLoginAdapter.cs` auf denselben Commitvertrag anpassen, Owned-Dateireferenz/Refreshowner erhalten;
   keine automatisch erzeugten Modellrouten beim neuen Connect. Bestehende angewandte Routen bleiben erhalten.
   `HttpCodexOwnedLoginBackend.cs` behält sein enges authentifiziertes Interface und begrenzte Responses.
7. **Bootintegration an AC-00/Root übergeben:** sichtbare accountunabhängige Monitor-/Gateway-Einstellung;
   bestehende Gatewayinstallationen bleiben Gateway, laufende Requests werden nicht still gestoppt. Coremetadaten erst
   im Gateway-/Corepfad auflösen bzw. Fehler als Corestatus isolieren. Monitorstart bindet API/Account/Quota
   unabhängig von Binary, Manifest, Modellkatalog und Runtime; vorhandene Profildateien erzwingen keinen Start.
   Loginendpoint startet Core nur für die konkret ausgewählte coreabhängige Methode im passenden Modus;
   keine Leer-Runtime als allgemeine Authvoraussetzung. Coremethode im Monitorbetrieb meldet lokale Grenze.
   `OwnedProxyLifecycle` behält Integritäts-/Qualifikationsgates und eigene Prozesszuständigkeit.
   Collector bei ruhendem Core pausieren, History aus SQLite weiter lesen, keine künstlichen Coveragefehler
   oder Cursoränderungen; bei Gatewayfortsetzung vom vorhandenen Sourcecursor weiter sammeln.

**Bearbeitungsbesitz dieses Pakets:** obige Account-/Monitorservices, private Credentialverwaltung,
extrahierter Loginservice/Quotaquelle, `OwnStatePaths.cs` einschließlich `SecretStore`, minimaler
`QuotaService`-Resolveranschluss und lokale Tests. Root koordiniert Credential-/Configschema und Migration.
**AC-00/Root exklusiv:** `src/Lain.Core/*Contracts.cs`, `src/Lain.Client/ClientContracts.cs`, `QuotaContracts.cs`,
gemeinsame Interfaces/DTOs, `Program.cs`, `ConfigurationRepository.cs`-Schemaeinbindung, zentrale Migrationen,
Buildfiles, Integration und Bootfiles `OwnedProxyLifecycle.cs`/Collector; Anforderungen dafür aus Schritt 7.
AC-02 besitzt UI-/CLIablauf, AC-03 Readernormalisierung, AC-09 Providerkatalog/Modellwahl.
AC-07 besitzt den späteren coreunabhängigen Codexowner; bestehende Adapteränderungen über AC-01 abstimmen.

## Persistenz, Migration und Kompatibilität

- Alte Account-IDs unverändert übernehmen: keine neue Ableitung bei Migration oder Keyrotation; Route-/Profil-
  und Usage-Referenzen, Namen, BudgetPoolId, SharedWithLegacy und AppliedRevision bleiben erhalten.
  Neue Accounts erhalten opaque reservierte IDs; gleicher neuer Key allein definiert weder Budget noch Scope.
- Alte Inferenzfiles, `transports`, Monitorfiles und Core-owned Referenzen lesbar halten; vorhandene Keys
  als Fähigkeiten abbilden, ohne schon erfolgreiche Reads/Inferenz oder einen Anbieterplan zu behaupten.
  Billingplan aus altem Routing bleibt als bisherige Herkunft, kein erfundener erkannter Accounttarif.
- Identität: belegtes Provider-Subjekt + Produkt/Region/Ressource getrennt von UI-Name/E-Mail speichern.
  Nur überprüfte Gleichheit erlaubt gezielte Wiederverwendung; unbekannt bleibt unbekannt. Keine automatische
  Zusammenlegung bestehender Accounts/History. Neues Credential kann bekannte Identität ergänzen, nicht umdeuten.
- Vorgeschlagener Crashschutz: Secrets privat als unveränderliche Generation vorbereiten, Referenz mit
  Account/Scope atomar publizieren. Nur bei statischer Key-/Account-Adoption lässt fehlgeschlagener Configsave
  den alten Zugang unverändert aktiv; unreferenzierte statische Vorbereitung gezielt bereinigen. OAuthrotation
  kann alten Refresh bereits verbraucht haben: neue Generation vor Publikation dauerhaft privat recoverbar sichern,
  niemals als gewöhnlichen Orphan löschen. Verlorenes/ungewisses Refreshresultat verlangt Recovery/Reconnect;
  kein Retry verbrauchten Materials oder rückgespielter Backuprefresh. Secretwrite innerhalb Update ist kein
  Mehrdatei-Commit; Root legt Format/Migration fest. Keine ungeprüfte In-place-Überschreibung als Ersatz.
- Abhängigkeitsprüfung statt pauschalem Löschen: Referenzen mehrerer Fähigkeiten und laufende Leases beachten.
  Remove entzieht lokal zuerst Zugriff, invalidiert Operationen/Generationen; Anbieterrevokation separat ausweisen.
  Alte Snapshots nie refreshen/revoken; Fremd-CLI-Caches nicht verändern. Keine automatische Kontoanlage beim Anbieter.
- Alte API-/Clientaufrufe behalten validierte Wirkung; neue Methoden/Fähigkeiten additiv bereitstellen.
  Root prüft Vertragsversion/Parserverträglichkeit und dass Account-only Änderungen keine Gatewayanwendung erzwingen.

## Login-Lifecycle und Nebenläufigkeit

Start reserviert Operation und gegebenenfalls neue Account-ID, bindet Methode/Zweck/Revision/Zielgeneration.
Vor eigener expliziter Keyerzeugung muss die Ressource eindeutig feststehen. Liefert der Providerlogin einen
Key bereits als Consentresultat, erwarteten Scope soweit bekannt vorab binden und gelieferten Scope vor lokaler
Adoption prüfen. Discoverymehrdeutigkeit verlangt Auswahl; Reconnect bleibt an den bestehenden Scope gebunden.
Vorgeschlagene Phasen: vorbereiten → autorisieren → Ressource wählen → privat vorbereiten → committen;
Endzustände completed, cancelled, expired, failed; Remoteausgang ungewiss bleibt eigener Recoveryzustand.
Start/Poll/Cancel sind pro Operation serialisiert und wiederholbar; zielaccountbezogene Commits/Disconnect und
pro Credentialowner Refresh serialisieren, unabhängige Accounts nicht während Browserwartezeit blockieren.
Globalen HTTP-Mutationslock nicht über Auth-/Providerwartezeiten halten; Root integriert kurze Commitgates.
Vor Publikation erneut Revision, Existenz, Generation, Scope und Cancel prüfen; CASkonflikt überschreibt nichts.
Unverwandte Configänderung verlangt erneute lokale Validierung; vorbereitetes Ergebnis ohne neuen Remotecreate nutzen.
Cancel/TTL/Disconnect invalidiert Commitrecht; verspäteter Callback/Refresh kann Account niemals wiederbeleben.
Beginfehler, Timeout und Shutdown räumen Listener, Helperprozess, Tasks und Semaphores auf; fertig gespeicherte
Credentials lassen sich nicht nachträglich durch „Cancel“ als nie erstellt behandeln. Nur eigene Prozesse beenden.
Ownerrefresh sichert Rotationsresultat dauerhaft vor Referenzpublikation; gültige Readerleases, keine Doppelrotation.
Remote-Keycreate ist nicht atomar mit lokalem Save: Phase/Remote-ID und sicher gespeichertes einmaliges Secret
privat erhalten; nach Timeout niemals blind erneut Create. Verlorenes Secret ist per Key-ID nicht rekonstruierbar.

## Abhängigkeiten und offene tragende Vorschläge

Zuerst AC-00-Verträge/Commit-/Migrationsformat, danach Schritte 2–6 mit Testadaptern; Schritt 7 und echte
Hostintegration müssen vor AC-10-Abnahme mitlaufen. AC-04–08 konsumieren Login-/Ownergrenzen;
AC-02/03/09 bauen auf Account-/Monitorverträgen auf. Protokolle selbst werden dort implementiert.
Offen zur Rootentscheidung: **A)** Persistenz/Übergang des sichtbaren Ausführungsmodus; empfohlen neue
Monitorwahl ohne Core, alte Gatewayabsicht erhalten. Beim bewussten Wechsel laufende Requests geordnet behandeln.
**B)** Credentialgenerationen/Operationrecovery privat als Dateien oder zentraler Store; empfohlen Dateien mit
atomarem Referenzcommit, keine zweite konkurrierende Datenbank. **C)** Operationgrenzen, empfohlen anfänglich
8 aktive/64 terminale, 10min interaktive TTL, 1h terminale Metadaten; Ressourcen exklusiv je Callbackport/Profil.
Crash beendet wartende Browseroperationen; sicher vorbereitete Remoteergebnisse bleiben privat recoverbar.
Diese Werte sind Vorschläge, nicht bestehende Garantien. Codex-Monitorowner ohne Core bleibt separates Gate.

## Lokale Abnahme – geplant, ohne echte Accounts

- Private temporäre Stateverzeichnisse, Fakeprovider/-owner, TimeProvider und deterministische Speicherfehler;
  keine echte Auth, Providerlast oder Änderungen am Nutzerscreen. UI-Verbrauch nur im privaten Xvfb-Folgepaket.
- Hostintegration ohne Corebinary/-katalog/-manifest und mit beschädigten optionalen Coremetadaten, leerem State
  sowie altem aktivem Profil im expliziten Monitormodus: API bereit, kein Corekind/Portzugriff, direkte Quota lesbar.
- Monitor-only mit keinem Inferenzfile/Route: separate gültige Reads möglich; Same-Key nur einmal speichern;
  später Inferenz ergänzen erhält ID/Name/History. Passive Quelle bleibt awaiting-observation ohne Corestart.
- Migration alter multi-transport/API-/Codex-/Snapshot-/Monitorfixtures; gleiche IDs, Routen, Profile und
  Historywerte, wiederholte Migration idempotent; Keyrotation ändert keine ID und verbreitert keinen Scope.
- Abgelaufener Snapshot, fehlendes Readrecht, ungültiges Monitorfile, falsches Projekt/Region, gemischte
  Ressourcen: lokaler fähigkeitsbezogener Fehler; kein Dummykey, kein Fallback, keine Inferenz, kein Duplikat.
- Beginfehler/TTL/Shutdown, doppeltes Begin, Callbackreplay, Cancel gegen Commit, Disconnect gegen Read/Refresh,
  gleichzeitiger Reconnect/Rotation und zwei unabhängige Accounts: keine Wiederbelebung, Doppelrotation oder Leaks.
- Statische Adoption mit Savefehler: alter Key unverändert; Remotecreate-Timeout: gespeichertes Ergebnis
  fortsetzbar, verlorenes Secret nicht behauptet, kein zweites Create. Rotierende Grants separat: Antwortverlust,
  Save-/Publishfehler nach Rotation und Crash vor/nach Sicherung ergeben recoverbare neue Generation oder
  Recovery/Reconnect, niemals Wiederverwendung alter Refresh-Tokens oder automatische Orphanbereinigung.
- Gatewayregression: vorhandener Core-owned Codexlogin/Refresh bleibt über seinen Owner nutzbar; externe
  Snapshots bleiben ohne Rotation. Collector setzt Cursor nach Pause fort und verändert im Monitor keine History.
