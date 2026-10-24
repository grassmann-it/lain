# AC-00: Integration und gemeinsame Verträge

Planungsbasis `5dc916f`, [Arbeitsauftrag und Grenzen](README.md).
Verantwortlich W0; Zusammenarbeit W1 für Persistenz/Lifecycle, W2 für Client/UI,
W10 für freigegebene Paket-/Installeränderungen. Die folgenden Verträge sind
Vorschläge zur Konkretisierung, keine bereits implementierten DTOs.

## Ergebnis

Alle Fachpakete können auf demselben Account-, Methoden-, Credential- und
Quotamodell arbeiten. Monitorbetrieb wird in Host, Startlogik und Auslieferung
tatsächlich unabhängig vom Core. Der bestehende Gatewayvertrag behält seine
Identitäts-, Journal-, Kosten- und Snapshotgarantien.

## Ausgangslage und erste Änderungen

- `AccountDto` hat eine globale Credentialart, `LoginDto` nur Provider und Zustand.
  Die neuen Fähigkeiten benötigen getrennte Beobachtungen, Ressourcen und Owner.
- `ProviderCatalogRegistry` leitet Connections aus Routen/Tarifen ab. Ein Account
  ohne Template darf künftig trotzdem eine gültige Zugangsmethode haben.
- `Program.cs` liest optionale Coremetadaten beim Boot, startet Core vor jedem
  Login und bei aktivem Profil; `HostQuotaSource` liegt ebenfalls darin.
- `QuotaCredential.FromSecretJson` und `QuotaService` erwarten einen Inferenz-
  Bearer. Separate Monitorcredentials existieren, lösen die Kopplung aber nicht.
- `JournalUsageCollector` läuft auch ohne Core. Das darf im Monitor weder Poll-
  Fehler erzeugen noch historische Coverage-/Cursorstände verändern.
- Publisher und Installer verlangen heute das Corebinary. Ein nur ruhender
  Prozess erfüllt „ohne installierten Core“ deshalb nicht.

## Gemeinsamer fachlicher Vertrag

Die konkrete C#-Benennung ist delegierbar; Informationen und Grenzen bleiben
verbindlicher Input für die spätere Implementierung. API bleibt lokal unter
`/api/v1`; additive DTO-Erweiterungen und Rückwärtskompatibilität zuerst prüfen.

| Bereich | Mindestens benötigte Information / Verhalten |
| --- | --- |
| Zugangsmethode | Stabile Anbieter-/Methoden-ID, Produkt/Region, Voraussetzungen, unterstützte Zwecke, benötigte Ressourcen, Herkunft und methodenspezifische Reife |
| Account | Stabile lokale ID/Name; bestätigte Provideridentität und Org/Projekt/Workspace soweit verfügbar; mehrere Capability-/Credentialbindungen ohne Duplikat |
| Fähigkeit | Inferenz, Quota, Kosten, Modelle jeweils getrennt: verfügbar, noch ungeprüft, fehlende Rechte, abgelaufen, nicht unterstützt; Erfolg einer Fähigkeit beweist keine andere |
| Credentialhandle | Private Referenz, Ownerart, Generation, Zweck, Scopebindung und eigene Ablaufzeit; keine Token-/Cookie-/Refreshwerte im öffentlichen DTO |
| Connectoperation | Methode/Zweck, Zielaccount oder bewusste Neuanlage, erwartete Revision, bekannte Ressourcenbindung, Ablauf/Abbruch, nächste Nutzeraktion und geheimnisfreier Ergebnisstatus |
| Ressourcenauswahl | Verifizierte Auswahl oder ausdrücklich manuelle unbestätigte Angabe; Provider, Produkt, Region und Accountscope erhalten; Reconnect bleibt an vorhandene Identität gebunden |
| Tokenprojektion | Zeitlich gültiger Zugang für genau den qualifizierten Endpunkt/Zweck; Rotation nur beim Owner; Monitor sieht keinen Refresh-Token; statische Keys erhalten keinen fiktiven Refresh |
| Quota | Stabile Fenster-/Quellen-/Scopeidentität, Messzeit/Frische, native Einheit, nullable Werte, letzte erfolgreiche Beobachtung; neue Generation darf kein Altscope-Ergebnis übernehmen |
| Accountdetails | Kilo-Passstatus/Gutschrift/Fälligkeit und ähnliche Sachverhalte als benannte fachliche Details; kein erfundener Prozentnenner und kein ungeprüfter beliebiger JSON-Beutel |
| Hoststatus | Hostbereitschaft, gewählter Betriebsmodus, Coreverfügbarkeit/-zustand, tatsächliches aktives Profil sowie ausstehende Gatewayänderung getrennt |

Konfigurationsmutationen bleiben revisionsgebunden; veraltete Änderungen liefern
409. Reine Monitoränderungen dürfen weder einen Proxyapply erzwingen noch eine
bestehende aktive Runtime heimlich ändern. W0 konkretisiert hierfür getrennte
Gatewaywirksamkeit innerhalb der Revisionierung und erhält die nachvollziehbare
Zuordnung der tatsächlich angewandten Runtime. `Revision != AppliedRevision`
allein ist nach der Entkopplung kein ausreichendes globales Gesundheitssignal.

Loginstart, Polling und Abschluss verwenden dieselben Hostoperationen für CLI
und Desktop. Netzwerk-/Browserwartezeiten halten nicht die globale
Mutationssperre. Nur lokale Commitphasen sind kurz serialisiert; ein Abschluss
prüft erneut Revision, Operationgeneration und Ressourcenbindung. Wiederholtes
Polling darf keine zweite Keyanlage oder zweite Credentialübernahme auslösen.

Secret-Eingabe bleibt über den vorhandenen privaten lokalen Mutationsweg möglich;
allgemeine Status-/Listen-/Fehler-/Login-DTOs bleiben geheimnisfrei. Devicecodes
oder Browseraktionen erhalten nur die für den vorgesehenen Vorgang erforderliche
Darstellung und dürfen nicht in Diagnosen oder dauerhafte Logs gelangen.

## Vor paralleler Fachintegration liefern

1. Aktuellen Stand und fremde Änderungen aufnehmen. Keine bestehende Forschung
   überschreiben; neue Lieferdateien explizit in den späteren Arbeitscommit nehmen.
2. Gemeinsame DTOs/Katalogschema und Zustandsübergänge mit W1/W2 festlegen.
   Quellenstatus und Methodenfreigabe sind getrennte Felder oder gleichwertig
   getrennte Projektionen, kein allgemeines `OAuthSupported=true`.
3. Kleine synthetische Vertragsbeispiele plus passende Serialisierungs-/
   Kompatibilitätsprüfungen liefern: bestehender Keyaccount; Monitor ohne
   Inferenzkey/Route; Quotaerfolg mit ungeprüfter Inferenz; verweigerter optionaler
   Scope; Reconnect derselben Identität; falscher Scope; abgebrochener Abschluss;
   unbekanntes Guthaben; Kilo-Wallet ohne Limit; reiner passiver Headeradapter.
4. Mit W1 ein crashfestes Schema für Accounts/Credentialreferenzen wählen.
   Vorschlag: neue private Credentialgeneration zuerst, atomarer Configverweis
   danach. Verwaiste Generationen gezielt behandeln; Provider-Keyanlage bleibt
   eine separate nichtatomare Aktion. Alte Secrets nicht vor sicherem Commit löschen.
   Rotation ist ein eigener Fall: Ein remote verbrauchter Refresh-Token bleibt
   nach lokalem Schreibfehler nicht gültig. Die neue Generation recoverbar halten;
   bei verlorenem/ungewissem Ergebnis gezielten Recovery-/Reconnectzustand statt
   Retry mit altem Material verwenden. Rollback reaktiviert keine verbrauchten
   Refreshdaten aus Backups; aktueller Owner bleibt erhalten oder wird neu verbunden.
5. Alle nötigen Konfigurationsmigrationen zentral schreiben. Existierende JSON-
   Konfiguration/Secrets und Usage-SQLite sind unterschiedliche Bestände; keine
   neue Datenbank oder Quota-Historisierung allein wegen dieses Plans einführen.
6. Vorhandene Monitorzugänge und Accounttypen verlustfrei abbilden, Legacyimport
   und API-Key-Fallback kompatibel halten. Daten einer unbekannten Identität
   nicht anhand einer E-Mail zusammenführen.

## Host- und Paketverdrahtung

Empfehlung: ausdrücklicher Monitor-/Gatewaybetriebsmodus, unabhängig vom
Darstellungsmodus. Bestehende Installationen behalten Gateway und aktive Profile.
Ein neuer Monitor kann ohne Coredateien starten. Ein Wechsel weg von einem
laufenden Gateway ist eine bewusste Betriebsaktion mit sichtbarer Wirkung auf
aktive Clients; Connect allein schaltet den Betrieb nicht um.

- Coremetadaten nur für tatsächlichen Corebedarf auswerten. Fehlende oder defekte
  optionale Dateien blockieren keinen Monitor; Gatewaydiagnose bleibt präzise.
- Corestart nur bei freigegebener coreabhängiger Methode oder ausdrücklich
  gewünschtem Gatewaybetrieb. Public-/CLI-/Sessionlogin startet keinen Core.
- Journalcollector und Inspector nur mit aktiver passender Runtime betreiben;
  im Monitor keine erfundenen Erfassungslücken. Gespeicherte History bleibt lesbar.
- `HostQuotaSource` aus Program in den von W1 verantworteten Resolver auslagern.
  Echte Monitorcredentials benötigen keinen Dummy-Bearer und keine `HasAccount`-
  Inferenzdatei. Abgelaufene Inferenz sperrt kein unabhängiges gültiges Monitoring.
- `QuotaService` veröffentlicht Ergebnisse nur noch für die aktuelle Bindungs-
  und Credentialgeneration. Alte gültige Werte bleiben nur beim passenden Scope.
- Corelose und volle Paketvariante sowie Installationsmetadaten mit W10 abstimmen.
  Ein Monitorupdate entfernt keinen vorhandenen Core oder Gatewayzustand heimlich.
  Der Installer erkennt den gewählten Umfang und bietet einen geprüften Rückweg.

## Exklusive Dateien und Übergaben

W0: `src/Lain.Core/Contracts.cs`, `ProviderContracts.cs`, `MonitorContracts.cs`
und erforderliche neue gemeinsame Vertragsdateien; `src/Lain.Host/Program.cs`,
`src/Lain.Host/Providers/ProviderCatalogRegistry.cs`, `src/Lain.Client/ClientContracts.cs`,
`src/Lain.Host/Quota/QuotaContracts.cs`, gemeinsame Projekt-/Build-
und Lockdateien sowie alle Migrationen. W1 liefert Services; W2 Clientwrapper
und Catalog-JSONs; Providerworker Adapterregistrierungen als Integrationsauftrag.

W10 erhält ausdrücklich die relevanten Publisher-/Installerskripte. W0 führt
kein paralleles Editieren derselben Datei ein. Methodenspezifische Quotaadapter
haben jeweils genau einen Paketowner; QuotaContracts bei W0, QuotaService bei W1.
`OwnStatePaths.cs` mit SecretStore gehört W1, Schemaänderungen werden mit W0
abgestimmt. `OwnedProxyLifecycle.cs` und Collector-Bootintegration liegen bei W0.
Zusätzliche HTTP-Inferenztransporte aus AC-06/07 benötigen einen gesondert
qualifizierten Anschluss; kein heimlicher neuer Corepatch innerhalb eines Readers.

## Abnahme und Grenzen

- Alte Konfiguration öffnet mit gleichen IDs, Routen, Profilen, Secrets und
  Usagecursor; neue optionale Felder zerstören keine alten Leser.
- Host startet ohne Corebinary, Manifest und Modellkatalog in Monitorbetrieb.
  Bestehender Gatewaystart und dessen Portkonfliktbehandlung bleiben erhalten.
- Monitor-only erzeugt keine Modellroute, Dummycredentials oder Coreanfrage.
  Pending-Gatewayänderungen bleiben sichtbar, erzwingen aber keinen Monitorfehler.
- Crash zwischen Secretwrite und Configcommit sowie Reconnect-/Disconnect-Races
  liefern keine verlorenen alten Zugänge und keine nachträglich wiederbelebten Tokens.
- Bereits veralteter Start: 409 vor Remoteaktion. Erst während Login/Keyausgabe
  veraltete Revision: keine lokale Adoption, aber privates Remote-/Recoveryergebnis
  erhalten; kein unmöglicher Remote-Rollback und kein blindes Create-Retry.
  Scopewechsel: keine fremden alten Quotas. Doppelte Events/Callbacks: keine
  doppelten Accounts/Providerwrites.
- Zentrale Fixtures sind Eingabe für AC-01–10. Lokale Prüfung, Installation und
  reale Providerabnahme sind getrennte Ergebnisse; dieser Plan führt keine davon aus.
