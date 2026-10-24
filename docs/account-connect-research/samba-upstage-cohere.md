# Account Connect: SambaNova, Upstage und Cohere

Vorschlag vom 2026-10-24, Arbeitsstand `5dc916f`. Ergänzt die fertige
[Quota-/Auth-Recherche](../quota-auth-research/samba-upstage-cohere.md), ohne sie
zu ändern. Keine Implementierung oder angenommene Architekturentscheidung.
Nur gezieltes Nachlesen bereits geladener öffentlicher Clients und zusätzliche
öffentliche statische Quellen: keine Konten, Logins, Provider-Accountabfragen,
Inferenz, Secrets, Serviceaktionen oder Builds.

## Gemeinsamer sichtbarer Vertrag

Ein Provider bekommt eine Aktion **Verbinden**, mit den Absichten **Modelle
und Nutzung** und **Nur Nutzung**. Bei bereits vorhandenem lain-Key kann der
Nutzer stattdessen die Nutzung an dieses Konto anbinden. Die Methode heißt in
der UI „Im Browser anmelden“ bzw. bei Cohere zunächst „Mit Cohere anmelden“.
Sie heißt erst dann OAuth, wenn tatsächlich ein qualifizierter OAuthvertrag
verwendet wird. In diesem Scope ist für keinen Anbieter ein öffentlicher
delegierter OAuthvertrag für beide Fähigkeiten belegt; Cohere hat einen
konkreten proprietären CLI-Pairingkandidaten.

Vor dem Start ist die Folge verständlich: „Verbindet dein Konto für Modelle
und Nutzung. Dabei wird ein persönlicher lain-API-Key eingerichtet.“ Diese
Erklärung gehört zum bewussten Connectabschluss. Danach kein separates
Keykopierformular, falls der qualifizierte Providerconnector den Key selbst
privat übernehmen kann. **Nur Nutzung** erzeugt, enthüllt oder rotiert keinen
Key. Ein reiner Monitor verwendet auch keinen bequemen
`GetOrCreateDefaultAPIKey`-Helper.

Sichtbar bleiben Providerlogin, nötige Konto-/Orgwahl und ein Ergebnis.
Intern dürfen getrennte Credentials entstehen: Session-/Pairingzugang für
Monitoring und ein API-Key für Inferenz. Ein Connectversuch ist keine Garantie
für ein einziges Secret. Bei einem einzigen zulässigen Konto wird dessen Name
bestätigt und keine sinnlose Auswahl angezeigt; bei mehreren wird genau eine
Organisation gewählt. Kein automatischer Beitritt, Wechsel der Abrechnung,
Upgrade oder Wechsel zu einem anderen Produkttyp.

Ergebniszustände sind unabhängig: **Nutzung verbunden**, **Modelle
eingerichtet**, **Anmeldung erneuern** oder ein konkreter Fehler. „Modelle
eingerichtet“ bedeutet Key/Transport lokal gespeichert, nicht getestete
Inferenz oder bestätigte Modellfreigabe. Ein fehlgeschlagener Monitoringread
darf einen bereits nutzbaren Inferenzkey nicht deaktivieren; ein fehlgeschlagener
Keybootstrap darf erfolgreiches Monitoring erhalten. Das Ergebnis benennt
Teilfortschritt, statt „Verbunden“ pauschal für beides zu verwenden.

## SambaNova: SambaCloud Free und Developer

### Beleg und bevorzugter Flow

Ein persönlicher **Browser-Sessionconnector mit anschließendem Keybootstrap**
ist der bevorzugte Kandidat. Die öffentlichen Cloudclients haben dieselbe
Origin für Auth-/Usage-/Keypfade. Das zusätzlich geöffnete
[API-Seitenbundle](https://cloud.sambanova.ai/_next/static/chunks/app/apis/page-26d3c835cd944ec2.js)
schließt die frühere Response-Lücke: Nach POST `/api/keys` mit `userName`,
`apiKeyName`, optional `expiresAt`, verarbeitet die UI `apiKey` und
`apiKeyInfo.name`. Die Rohübernahme eines neu erzeugten Keys ist damit als
First-Party-Clientvertrag belegt. Das ist kein öffentlicher OAuthvertrag und
noch keine qualifizierte Anmeldung für einen verteilten lain-Client.

Minimaler Ablauf für **Modelle und Nutzung**:

1. „SambaNova verbinden“ mit dem Hinweis auf einen persönlichen lain-Key.
2. Anmeldung in der qualifizierten Connector-Browsersession. Bestehendes
   SambaCloud-Konto bestätigen; keine SambaStudio-/SambaStack-Zugänge mischen.
   Eine Cloud-Orgwahl wird erst angeboten, wenn sie tatsächlich vom
   qualifizierten Vertrag geliefert wird; bisher belegt ist der Userkontext.
3. Connector liest `/api/auth:me`, bindet den Userkontext, richtet bei fehlendem
   passenden lain-Key genau einen benannten Key ein und speichert `apiKey`
   direkt privat. Vorhandenen eigenen lain-Key wiederverwenden, keine Rotation.
4. Credits/Usage lesen; beide Fähigkeiten und den Datenzeitpunkt zeigen.

Für **Nur Nutzung** entfallen Keyliste, Create und Reveal vollständig. Ein
Userkontext plus Session genügt dem Kandidatenreader. Beim späteren bewussten
„Modelle hinzufügen“ darf dieselbe noch gültige Session den Bootstrap tragen.
Das verlangt keinen zweiten Providerlogin, wohl aber die neue Connectabsicht.

Monitoring verwendet die belegten [Console-Datenpfade](https://cloud.sambanova.ai/_next/static/chunks/35360-10acd791f887f198.js):
GET `/api/usage?user_name=…&start_date=…&end_date=…`, optional Modell-/Keyfilter,
und Credits. GET `/resources/credits/balance` hat laut
[Billingclient](https://cloud.sambanova.ai/_next/static/chunks/59468-13418f0de96abfb8.js)
`amount`/`readAt`; Währung und Abwicklungsverzögerung vor Normalisierung
bestätigen. Der Inferenzkey ermöglicht zusätzlich passiv die offiziellen
[RPM-/RPD-Header](https://docs.sambanova.ai/docs/en/models/rate-limits).
Sie werden ohne Extra-Inferenz gesammelt, ersetzen aber keine Credits oder
TPD-Restwerte. Free/Developer bleiben getrennte Accountzustände. Connect
hinterlegt kein Zahlungsmittel und ändert keinen Tier.

### Voraussetzungen und belastbarer nächster Nachweis

Ein normaler Systembrowserlogin liefert lain ohne registrierten Callback oder
qualifizierte Brücke **keine** Cloudsession. Benötigt wird ein persönlich
autorisierter Sessionconnector, beispielsweise ein isolierter Browserkontext
mit enger Origin-/Operationsbrücke; Cookies bleiben dort, nicht in einem
manuellen Importformular. Ob der Providerlogin dort unterstützt wird, ob
externe IdP-Anmeldung eine Browsererweiterung/-brücke erfordert und wie die
Session erneuert wird, ist zu qualifizieren. Keine Umgehung von Browser- oder
Providerkontrollen als stillschweigende Lösung.

Maintainer klären Session-/CSRF-Bindung, eigene zulässige Drittclientverwendung,
Keyablauf/-limit und Wiederaufnahme nach unterbrochener einmaliger Keyausgabe.
Die [Keydokumentation](https://docs.sambanova.ai/docs/en/get-started/api-keys-urls)
nennt bis zu 25 Keys/User und einmalige Sichtbarkeit. Create nach Timeout
deshalb nicht blind wiederholen; ein lokales Connectjournal muss einen
eventuell angelegten Key als offenen Vorgang melden. Ob dessen Kennung sicher
aus Name/Metadaten rekonstruiert werden kann, ist noch offen.

Nächster begrenzter Nachweis: öffentliche Auth-/CSRF-/Keyresponse-Verträge
statisch vervollständigen und Connector-Browserübergabe spezifizieren. Erst ein
später gesondert autorisierter Versuch kann Readrechte und Bootstrap live
qualifizieren. Bis dahin API-Keyfallback mit normalem Privatfeld; passives
Monitoring bleibt dennoch lieferbar. Eine Consoleverknüpfung darf als
persönlicher, noch experimenteller Connector geprüft werden, ohne das gesamte
Providerangebot zu blockieren.

## Upstage: direkte Solar API

### Beleg und bevorzugter Flow

Bevorzugt ist derselbe sichtbare Ein-Connectflow mit **Console-Session,
Orgwahl und bewusstem API-Keybootstrap**. BFF und Keyresponse sind jetzt auf
Clientebene konkret: Das zusätzlich erneut geöffnete
[Mutationsbundle](https://console.upstage.ai/_next/static/chunks/850-7e74bc122f3ff630.js)
verwendet POST `/organizations/{organizationId}/api-keys` mit `{name,note}`
und übernimmt `key` sowie `api_key.key_id`. GET
`/organizations/{organizationId}/api-keys/{keyId}/reveal` liefert `key`.
Die Pfade laufen über den bereits belegten
[Console-BFF](https://console.upstage.ai/_next/static/chunks/7157-768d821a4d850f5c.js)
unter `https://console.upstage.ai/api/console/v3`, mit same-origin Session
und `x-console-request: 1`. Der Solar-Bearerkey ist kein Ersatz für diese
Session. Das sind First-Party-Clientbelege, keine zugesicherte Public API.

Minimaler Ablauf für **Modelle und Nutzung**:

1. „Upstage verbinden“; persönlicher lain-Key und direkte API als Zweck
   erkennbar, Studio-Abonnement nicht als Solar-Freimenge voraussetzen.
2. In der qualifizierten Connector-Browsersession anmelden. Bei mehreren
   bestehenden Mitgliedschaften Organisation wählen, sonst Namen bestätigen.
3. Org-/Rollenmetadaten lesen und fest binden. Vorhandenen eigenen lain-Key
   wiederverwenden; sonst genau einen `lain-…`-Key mit beschreibendem `note`
   anlegen und `key` direkt privat übernehmen. Kein automatisches Reveal des
   neuesten oder fremden Keys. Wiederverwendung eines anderen bestehenden Keys
   wäre eine bewusste Alternative, keine Heuristik.
4. Spend, Credits und Limits lesen. Ergebnis nennt Org und die wirklich
   erreichbare Usageabdeckung; keine automatische Inferenzprüfung.

**Nur Nutzung** führt nur Org-/Mitgliedschafts-, Spend-, Promotions-, Limit-
und bei Bedarf Usage-Reads aus. Kein Create oder Reveal. Eine neue
Orgmitgliedschaft wird im Connect nicht angelegt. Nach
[FAQ](https://console.upstage.ai/docs/resources/faq.md) sehen Member
Org-Saldo/Spend/Limits, per-Key-Verbrauch aber nur eigene Keys; Owner sehen
alle. Die UI muss deshalb etwa „Org-Guthaben · Verbrauch eigener Keys“
anzeigen können. Eine begrenzte Rolle ist nützlich, aber kein dedizierter
read-only Monitor-IAM-Key.

Intern werden Sessionreferenz, User-ID, Org-ID/Rolle und API-Key samt Key-ID
getrennt gespeichert. GET `/spend` liefert `month_to_date`/`period`,
`/promotions` Creditbestand und Coupons, `/usage-limit` Budget,
`/rate-limit` den Tier und `/usage` paginierten historischen Verbrauch.
Unterschiedliche Origins, Zeiträume, Granttypen und UTC-Monatsgrenzen bleiben
erhalten. Signupcredit, Prepaid und PAYG werden nicht durch eine synthetische
„Freequote“ vereinheitlicht; Credits können bei Erschöpfung auf registrierte
Zahlmittel übergehen. Im Connect keine Käufe, Zahlungsdaten, Budgets oder
Auto-Recharge ändern.

### Voraussetzungen und belastbarer nächster Nachweis

Auch hier fehlt der Sessionrückweg eines gewöhnlichen externen
Browserlogins. Für den persönlichen Connector sind isolierter Browserkontext
oder eine eigens qualifizierte Browserbrücke, Cookie-/CSRF-Vertrag,
Loginfortsetzung und Drittclient-/Verteilungsbedingungen Maintaineraufgaben.
Ein eigener OAuth-Client mit dokumentiertem Grant wäre künftig vorzuziehen,
ist in den vorhandenen Quellen aber nicht belegt. Dem Nutzer werden weder
Cognito-/IdP-Clientdaten noch BFF-Header als Setupfelder zugemutet.

Nächster Nachweis: den bereits konkreten BFF-/Bootstrapvertrag mit
Orgbindung, erlaubtem Browsertransport und Fehler-/Wiederaufnahmesemantik
statisch spezifizieren. Danach in einem gesondert autorisierten Versuch nur
Spend/Promotions lesen und Rollenabdeckung vergleichen; ein danach bewusst
beauftragter Connecttest kann genau einen benannten Key prüfen. Fehler beim
Create werden separat ausgewiesen, erfolgreiche Monitorreads erhalten.
Fallback: privater vorhandener API-Key für Modelle; dokumentierter CSVimport
oder Consoleansicht für Verbrauch. CSV ist kein Echtzeitsaldo.

## Cohere: Browser-Pairing zuerst qualifizieren

### Gemeinsamer Authkandidat und Produkttrennung

Der stärkste Ansatz für weniger Copy/Paste ist der tatsächlich ausgelieferte
**proprietäre CLI-Browser-Pairingflow**. Laut
[Appbundle](https://dashboard.cohere.com/_next/static/chunks/pages/_app-93016fb49a1d0e50.js)
unter `https://production.api.os.cohere.com/rpc/BlobheartAPI/`:
`CLIAuthBegin` → `browserURL,secret`; Browserbestätigung via
`CLIAuthConfirm(secret)`; `CLIAuthCheck` → `token`. Das
[CLI-Seitenbundle](https://dashboard.cohere.com/_next/static/chunks/pages/welcome/auth-cli-bd07116a0aa4d76a.js)
zeigt die angemeldete Session und Allow/Deny für `cliSession`. Ein eigener
lokaler Versuchskontext darf nach Qualifikation Begin/Check und die
freigegebene Browser-URL zusammenbinden. Pollingintervall, Timeout und
Tokenlifetime fehlen als öffentlicher Vertrag; keine Werte erfinden.

Das Token ist **nicht** als universeller Inferenz-/Billingtoken qualifiziert.
Präferiert werden ein User-/Monitorzugang und ein separat zugeordneter
Inferenzkey unter derselben Anmeldung. Ob das Pairingtoken überhaupt die
notwendigen Usage-/Keyoperationen darf, bleibt ein expliziter offener Beweis.
OAuth Device Grant, Scopes und Drittclientregistration sind daraus nicht
ableitbar. Vor Benutzung fremden CLI-Namens/Clientidentität ist eine eigene
zulässige Clientzulassung zu klären; alternativ qualifizierter persönlicher
Dashboard-Sessionconnector wie oben.

Orgwahl heißt zunächst **bereits bestehende Organisation bestätigen**. Die
zusätzlich geöffnete
[Select-Organization-Seite](https://dashboard.cohere.com/_next/static/chunks/pages/welcome/select-organization-49cf6466f8a4918d.js)
ist Onboarding: `GetUserVerifiedOrgs` und `JoinVerifiedOrg({orgID})`.
Sie beweist keinen allgemeinen Org-Switch. Connect übernimmt keine dieser
Membershipmutationen und behauptet keine freie Orgumschaltung. Ein später
qualifizierter Auswahlvertrag darf nur bestehende Berechtigungen auswählen.

### Evaluation: vorhandenen passenden Key privat übernehmen

Minimaler Ablauf:

1. „Cohere Evaluation verbinden“ mit Zweck „Modelle und Nutzung“ oder
   „Nur Nutzung“ starten.
2. Nach qualifiziertem Pairing: Cohere-Browser öffnen, anmelden und Allow
   bestätigen; vorhandenen Teamkontext bestätigen bzw. nach dokumentierter
   Auswahl genau eine bestehende Org wählen.
3. Für Modelle einen passenden **bestehenden Evaluationkey** aus Metadaten
   wählen, wenn mehrere vorhanden sind. Der
   [Keyseitenclient](https://dashboard.cohere.com/_next/static/chunks/pages/api-keys-f99a742f3c68c279.js)
   erlaubt Reveal für `EVALUATION` und ältere `TRIAL`, nicht für Production.
   Der ergänzte
   [Sharedclient](https://dashboard.cohere.com/_next/static/chunks/2287-f24b553c0a1e8c1a.js)
   ruft `ViewRawAPIKey` auf; der Appclient verarbeitet `rawKey`. Direkte
   geschützte Übernahme erspart Kopieren, **falls** das erhaltene Login die
   Rechte dafür hat. Vorhandenen lokalen lain-Key vorzugsweise wiederverwenden.
4. Usage-/Billingreads mit eigenem Loginzugang ausführen, Evaluationzustand
   und nur belegte Nutzung anzeigen. Keine restlichen Calls aus lokalem
   Verbrauch berechnen.

Automatisches Anlegen eines **Evaluationkeys** ist nicht hinreichend belegt:
die aktuelle Evaluationtabelle hat einen leeren `onCreate`-Handler. Der
Sharedclient belegt `CreateAPIKey({name,keyType})` und neue `key.raw_key`,
aber die sichtbare Modalsteuerung wählt `TRIAL` oder `PRODUCTION`, nicht
`EVALUATION`. Ein neuer Trialkey darf nicht still als identischer
Evaluationplan eingerichtet werden. Bei fehlendem passenden Key bleibt
Monitoring verbunden; die Modellfähigkeit wartet auf einen belegten Key oder
den privaten Keyfallback. Der Nutzer muss dafür keinen Token manuell aus dem
Pairing extrahieren.

Für **Nur Nutzung** entfallen Keyliste, Reveal, Create und
`GetOrCreateDefaultAPIKey` komplett. Die [Limitseite](https://docs.cohere.com/v2/docs/rate-limits)
nennt 1.000 Calls/Monat und A+ 20 RPM; Monatsgrenze/Timezone und aktueller
Accountreststand bleiben offen. Datasetstorage-Usage ist dafür ungeeignet.

### Production: bewusster benannter Key, kein Go-Live-Shortcut

Minimaler Ablauf bleibt Anmeldung → vorhandene Org bestätigen → Ergebnis.
Vor Start ist sichtbar: **Cohere Production**, kostenpflichtige Verwendung
gemäß bestehendem Account und Einrichtung eines persönlichen lain-Keys.
Nur bei bereits freigegebenem Productionzugang darf der qualifizierte
Connector `CreateAPIKey({name,keyType: PRODUCTION})` verwenden und
`key.raw_key` einmalig sicher übernehmen. Dieser Request-/Responsevertrag ist
im Sharedclient belegt. Existiert ein geeigneter lokaler Key, wird keiner neu
erzeugt. Ein vorhandener anderer Productionkey lässt sich laut Keyseite nicht
noch einmal enthüllen; hierfür keine automatische Reveal-/Rotatekette bauen.

Fehlende Productionfreigabe oder Adminrechte werden angezeigt. Connect nimmt
keine Verträge an und startet keinen Go-Live-Prozess. Für das konfigurierte
Command A+ beweist selbst ein Productionkey keine normale Productionquote:
neue Varianten können weiter Triallimits tragen. Native billed/raw Tokens,
Budgetauslastung und eventuelle Credits bleiben eigene Maße. **Nur Nutzung**
darf Produktionsabrechnung lesen, soweit die Rolle es zulässt, und keinen Key
erzeugen. Owner-/Userabdeckung separat qualifizieren.

### Gemeinsame Voraussetzungen und nächster Nachweis

Die Readerstubs `GetAPIUsage`, `GetAPIUsageV2`, `GetBillingActivity`,
`GetBillingHistory` sind belegt. Ein Get-Name ist bei diesen POST-RPCs kein
Mutationsbeweis; der Connector braucht eine konkrete Read-Operation-Whitelist.
Besonders ungeeignet als Monitorhelper ist der ebenfalls ausgelieferte
`GetOrCreateDefaultAPIKey({canReturnProductionKey:true})`: der kann einen Key
anlegen und einen Productionkey wählen. Lain verwendet ihn für diesen Flow
nicht. Benutzerabsicht, Keytyp und Konto sind explizit.

Maintainer klären eigene Pairing-/Drittclientzulassung, Session-/CSRF-
Fortsetzung, Tokenrechte, Lifetime/Refresh/Widerruf und Orgidentität, bevor
der Loginbutton auslieferbar wird. Nächster begrenzter Nachweis ist ein
offizieller oder öffentlich weiter nachgewiesener CLIvertrag für genau diese
Rechte; kein echter Pairing-Begin hier. Danach ein separat autorisierter
Read-only-Versuch von Usage/Orgmetadaten; Keyreveal/-create nur im danach
bewusst getesteten Modell-Connect. Eine dokumentierte freie Eingabe eines
vorhandenen privaten Keys bleibt Fallback, kein Anlass zur manuellen
Sessiontokenkopie.

## Konsequenzen für lain

`AccountService.AddApi` verlangt heute bereits `ApiKey` und bildet seine
Identität daraus. `MonitorAccessService` setzt außerdem ein vorhandenes
API-Keykonto voraus; er verwaltet nur Token-/Keyprojektionen. Damit lässt sich
ein zuerst erfolgreicher reiner Monitorlogin nicht ehrlich als normales
Account Connect darstellen. Vorgeschlagen wird ein Providerkonto mit stabiler
Identität plus unabhängig zugeordneten Auth-/Inferenz-/Monitorartefakten.
Keine künstlichen Dummykeys zum Befriedigen des bestehenden DTOs.

Ein Connectkoordinator sollte Zweck, Providerprodukt, Subject/Org und
Phasenfortschritt besitzen. Er öffnet den Browser-/Pairingflow, wählt bestehende
Ressourcen, führt nur bei Modellabsicht den qualifizierten Bootstrap aus und
persistiert Artefakte getrennt. Monitoringrefresh erhält keine Create-/Reveal-
Fähigkeit; Authrefresh liegt ebenfalls außerhalb des Quotareaders. Bei den
Sessionconnectors führt die enge Browserbrücke nur geprüfte Reads aus,
statt allgemeine Sessioncookies an jeden Adapter zu geben.

Verbindungsmetadaten brauchen Angaben wie `authMechanism`,
`monitoringAccess`, `inferenceAccess`, `keyBootstrap`, `productType`,
`qualification` und Org-/Rollenabdeckung. Das sind Entwurfsbegriffe, kein
vorgegebenes neues DTOschema. Browser-/Callback-/Session-/Pairingdaten und
Rohkeys erscheinen nicht in normalen Account-DTOs, URLs, Logs oder Fehlern.

Eine externe Keyerstellung und lokale Persistenz sind keine atomare
Transaktion. Ein lokales Journal verhindert doppelte Creates bei Timeout,
erhält Teilfortschritt und beschreibt einen etwaigen offenen Remote-Key.
Reconnect erneuert abgelaufene Auth, nicht automatisch Inferenzkeys. Lokales
Trennen löscht nur zugeordnete lokale Daten; optionaler Providerwiderruf
braucht einen belegten, bewusst gewählten Vertrag. Fehlendes Monitoring,
Entitlement und vermutete Quote bleiben offen, statt durch den Loginstatus
als bestätigt zu erscheinen.

Priorität: SambaNova passiv erfassbare Header sind sofort konkret. Für das
vereinfachte Setup sind Upstages vollständiger BFF-Keybootstrap und Cohere-
Pairing die stärksten nächsten Qualifikationen; SambaClouds Create-Response
ist jetzt ebenfalls belegt. Die fehlende Session-/Drittclientqualifikation
ist jeweils ein eng umrissenes Arbeitspaket und kein pauschaler Ausschluss.
