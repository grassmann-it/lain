# Account Connect: xAI, StepFun, Poe und Synthetic

Vorschlag vom 24.10.2026 für `$architecture-sparring`, Basis `5dc916f`. Keine Implementierung oder neue Architekturentscheidung. Vorhandene Quota-Recherche wiederverwendet; ein gezielter öffentlicher Quellencheck ergänzt xAI-Keymetadaten. Keine Konten, Logins, Providerabfragen, Provisionierung, Runtime, Builds, Unteragenten oder Commits. Die bisherigen Reports bleiben unverändert. Maschinenlesbare Abläufe: gleichnamige JSON-Datei.

## Konkrete Zielabläufe

| Produkt | Sichtbarer Einstieg | Ergebnis | Zusätzliche Benutzereingaben |
| --- | --- | --- | --- |
| Poe | **Mit Poe verbinden** → Browser bestätigen | Ein ausgegebener API-Key für Inferenz und Restpunkte | Keine Client-ID und kein Monitor-Key. |
| Synthetic | **Synthetic verbinden** → API-Key einfügen | Inferenz und Aboquote automatisch | Ein Key; kein weiterer Key für Quota. |
| Step Plan | **Mit Step Plan anmelden** → Browser bestätigen | Login-Key für Inferenz und persönliche Usage, sofern Vertrag ausgerollt | Region nur bei Bedarf. Fremdclientqualifikation vor Freischaltung. |
| StepFun API | **StepFun API verbinden** → API-Key einfügen | Inferenz und geeigneter persönlicher PAYG-Kontostand | Region nur bei Bedarf; kein weiterer Monitor-Key. |
| xAI API-Team | **xAI API verbinden** → API-Key einfügen | Inferenz, automatisch erkannte API-Teamidentität | Optional **Abrechnung anzeigen** mit Management-Key am selben Account. |

Alle Abläufe erlauben anschließend Modelle hinzuzufügen. Ein Monitor-only-Interesse darf keine Modellroute verlangen. „Nur Kontingente anzeigen“ ist eine lokale Nutzungsentscheidung: Poe-, Synthetic- und Step-Keys behalten trotzdem ihre Ausgabeberechtigung. Keine Oberfläche darf daraus einen technisch lesebeschränkten Key machen.

Im heutigen Stand kann `SelectedModelRouteIds` leer bleiben: Dann wird keine tatsächliche Modellroute angelegt. Das entkoppelt den Account jedoch noch nicht von Inferenztemplates. `ProviderCatalogRegistry.cs:44–71` leitet Connections aus Routes ab und verlangt nichtleere `ProviderRouteIds`/`BillingPlanIds` mit exakter Mitgliedschaft. `AccountService.cs:42–81` benötigt auch ohne ausgewählte Modelle einen passenden Tarif und mindestens ein erlaubtes Template. Die folgenden Monitor-only-Abläufe sind deshalb Zielvorschläge; echte Verbindungen ohne Katalog-/Tarif-/Inferenztemplatekopplung sind damit heute nicht möglich.

Die Connect-Strecke sollte keine Abotarife abfragen, wenn deren Auswahl weder Login noch Endpoint bestimmt. Kontonamen automatisch vorschlagen und später ändern lassen. Providerzugang, Modellberechtigung und Kontingentzugriff getrennt rückmelden. Ein erfolgreicher Balance-GET benötigt keinen kostenpflichtigen Testprompt.

## Poe: drei Schritte mit bestehendem OAuth-Helper

1. Nutzer wählt Poe und **Mit Poe verbinden**; optional schon „Nur Kontingente anzeigen“.
2. Nutzer meldet sich im offiziellen Browserdialog an und bestätigt; Keyablauf optional dort wählen.
3. lain zeigt verfügbare Punkte und das verbundene Konto. Modelle später optional hinzufügen.

[Sign in with Poe](https://creator.poe.com/docs/external-applications/sign-in-with-poe) ist ein offiziell vorgesehener PKCE-Codeflow mit `apikey:create`. Das Ergebnis ist ein API-Key samt optionalem Ablauf, kein separater Monitoringtoken. Derselbe Key liest [Balance](https://creator.poe.com/api-reference/getCurrentBalance) und [History](https://creator.poe.com/api-reference/getPointsHistory) und kann Modelle aufrufen. [Externe verteilte Apps](https://creator.poe.com/docs/external-applications/external-application-guide) sind ausdrücklich vorgesehen.

Clientregistrierung auf `poe.com/api/clients`, öffentlicher Client-ID-Wert und Releasecallback gehören **dem Maintainer**. Der Nutzer erhält dafür kein Formular. Die Doku beschreibt automatische `localhost`-Redirects für lokale Entwicklung; Produktionsfreigabe, tatsächlicher Loopbackhost und Portregeln vor Release prüfen. Kein privates Clientsecret in einer verteilten Desktopanwendung voraussetzen.

`PoeOAuthAttempt` implementiert bereits PKCE/State, Einmalverbrauch und Ablaufparser. Benötigt werden Hostbrowser/Callback-Lebenszeit, Abbruch/Deny, Austausch, SecretStore und Reconnect. `PoeQuotaAdapter` ist vorhanden; nach Consent automatisch anschließen. `PoeUsageHistory` existiert als begrenztes Primitiv und braucht Host-/UI-Anbindung, keine neue zweite Keyabfrage.

**Monitoring:** kombinierter User-Punktesaldo aus Plan und Add-ons, ohne belegte Allocation, Monatsreset oder Split. History kann API/Chat/Canvas App enthalten und ist keine lain-only-Abrechnung. Keine Resourcewahl nötig. Monitor-only legt keine Routen an.

**Reconnect:** bei Ablauf oder Widerruf neuer Consent mit frischem PKCE. Kein Refresh-Grant dokumentiert. API-Keyersatz aktualisiert das logische Konto; ein neuer Keyhash darf nicht automatisch einen zweiten Account erzeugen. Im geöffneten Token-/Balanceschema fehlt eine belegte stabile User-ID: deren Bindung bei Reconnect muss bewusst gelöst werden.

**Fallback:** **API-Key einfügen** mit offiziellem Keylink, danach derselbe automatische Quotaanschluss. Fehlende Maintainerkonfiguration als solche melden; Nutzer nicht zur eigenen Appregistrierung schicken.

## Synthetic: ein Key reicht heute für Inferenz und Quota

1. Nutzer wählt Synthetic.
2. Nutzer fügt einen API-Key ein; **Key öffnen** führt bei Bedarf zur [offiziellen Entwicklerseite](https://dev.synthetic.new/docs/api/getting-started).
3. lain verbindet, liest die Aboquote automatisch und zeigt nächste Teilregeneration; Modelle optional später.

Die [offizielle Quota-API](https://dev.synthetic.new/docs/synthetic/quotas) nutzt denselben Bearer-Key wie Inferenz. **Kein weiterer Key, keine Monitoringregistrierung, keine Region-/Projekt-/Client-ID-Eingabe.** Ein öffentlicher Synthetic-OAuth-/Device-/Create-Key-Vertrag wurde nicht verifiziert; deshalb keinen funktionslosen OAuthknopf entwerfen.

Der vorhandene `SyntheticQuotaAdapter` bleibt Grundlage. Die [aktuelle Herstellerquelle](https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/utils/quota.ts) ergänzt `planWeeklyLimit` sowie ältere `weeklyTokenLimit`/`rollingFiveHourLimit`. [Octofriend](https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/components/menu-quota-indicator.tsx) liest diese am selben Endpoint, [Antwortheader](https://github.com/synthetic-lab/octofriend/blob/87d7fa9ced654e61cc2b22b61e5211c958376165/source/state.ts) liefern `x-synthetic-quotas`. Diese Ergänzung wird automatisch Teil desselben Accounts, kein neues Setup.

**Monitoring:** aktuelle [Limitseite](https://synthetic.new/rate-limits) nennt nur Wochenkredite mit Teilregeneration. `nextRegenAt` als nächste Auffüllung, nicht als voller Reset darstellen. Alte Formen nur nach gemeldeten Daten anzeigen; keine addierten Pools. Konto-ID ist im geöffneten Quotaschema nicht belegt. Monitor-only speichert denselben Key ohne Routen.

**PAYG/Extra Usage:** dieselbe Connection kann bezahlte API-Nutzung und eine Aboquote tragen. Die [Preisseite](https://synthetic.new/pricing) belegt zusätzlich nutzungsbasierte Abrechnung, keinen öffentlichen Geldsaldo-/Historyreader. Dafür „Abrechnung in Synthetic öffnen“ am selben Account; keinen zweiten Key verlangen. Bezahlten Überlauf bewusst außerhalb der Loginhandlung konfigurieren, kein automatisches Umschalten bei erschöpfter Quote. Zwei Budgetdimensionen sind kein Grund für zwei künstliche Accounts.

**Reconnect:** neuen Key von offizieller Entwicklerseite einsetzen, Quote neu lesen, alte Snapshots invalidieren. Logische Identität bei fehlender Provider-ID sicher wiederbinden; nicht ungeprüft Kontopools über mehrere Keys zusammenlegen. Unbekanntes Quotaschema lässt Inferenzverbindung bestehen und zeigt Quota unbekannt, statt noch einen Key zu erfragen.

## Step Plan: Herstellerlogin und Usage gemeinsam, vor Release qualifizieren

1. Nutzer wählt **Step Plan**; Region China/Global nur bei unklarer vorhandener Auswahl.
2. Nutzer öffnet den offiziellen Browserflow und bestätigt die Anmeldung.
3. lain übernimmt persönlichen Login-Key/Identität und liest Usage. Modelle danach optional. Wenn Usage für dieses Backend nicht verfügbar ist, bleibt die Verbindung mit präziser Kontingentmeldung bestehen.

Die [offizielle Step-Code-Doku](https://platform.stepfun.com/docs/zh/step-code/configuration/models.md) trennt Planbrowserlogin und PAYG-Keyprofile. Die [Herstellerquelle](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/login-client.ts) verwendet einen Cloud-Pollingflow: `init` mit geheimem Polltoken im Authorizationheader, öffentlicher Browser-`flow_id`, danach `poll`. Ergebnis ist ein bestehender `credential.api_key`, optional UID und eine zusätzliche Accountsession. Ein lokaler Callbackport und eine Nutzer-Client-ID sind hierfür nicht nötig.

Clientmetadaten name/version/platform sind laut Quelle Auditdaten und keine Authentifizierung. Das belegt noch keine beliebige Drittclientzulassung. Vor Freischaltung benötigt lain eine unterstützte eigene Clientverteilung oder ein ausdrücklich qualifiziertes offizielles CLI-Handoff. Wahrheitsgemäße lain-Identität verwenden, Step Code nicht imitieren. Keine vollständige neue OAuthregistrierung erfinden.

Der [offizielle Usagecontroller](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/coding-agent/src/step/usage/account.ts) liest mit **dem Login-Key** `GET /step_plan/v1/usage` auf passender regionaler APIbasis. Manuelle `STEP_API_KEY`-/Gateway-/Plattformkeys sind im Herstellercontroller ausgeschlossen. Das Credential benötigt deshalb interne Herkunft `personal-plan-login`, Profil und UID; bloß `api-key` reicht zur Berechtigungsannahme nicht.

**Monitoring:** persönliches Schema1 mit Account-ID und Beobachtungszeit; Codingpläne melden separate 5h-/Wochenprozente, Tokenpläne Abo-/Topup-Creditbuckets mit Reset/Ablauf. Keine zusätzliche Resourcewahl neben Region/persönlichem Konto. Gleicher Login ermöglicht source-seitig Monitor-only ohne Routen, aber keinen read-only Key.

**Deployment:** die [Repositorydoku](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/coding-agent/docs/account-usage.md) macht Schema1-Backendverfügbarkeit zur Voraussetzung und behandelt 404 als noch nicht verfügbar. **Deployment ist hier ungeprüft; nicht definitiv undeployed.** Zuerst offline Fixtures, später separat autorisiert ein Login + ein Usage-GET je qualifiziertem Profil. Keine Inferenz/Registrierung/Käufe für diese Probe erforderlich.

**Credentials/Reconnect:** der [Providerwrapper](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/index.ts) behandelt den Modellkey lokal statisch. Bei Widerruf neu anmelden, Profil/Accountidentität prüfen und denselben logischen Account aktualisieren. Optional zurückgegebene Access-/Refresh-/Device-/Appdaten sind eine getrennte Accountsession; [Passportrefresh](https://github.com/stepfun-ai/Step-Code/blob/81e79bc4b699148d6823fedde9b5e70d9a80255f/packages/providers/src/step-provider/login-refresh.ts) ist nicht für den belegten Usage-GET nötig. Diese breitere Session nicht vorsorglich speichern. Falls später nötig, besitzt ein Credentialowner ihren Lifecycle, niemals der Quotaadapter.

**Fallback:** bis zur Fremdclientqualifikation Plan-API-Key für Inferenz einfügen und Quota/Consolefallback zeigen. Persönliche Browsercookies/Oasis-Token sind kein normales Connectformular. Kein Wechsel zu PAYG bei leeren Plancredits.

## StepFun PAYG: vorhandenen selben-Key-Reader automatisch nutzen

1. Nutzer wählt **StepFun API**, Region nur bei Bedarf.
2. Nutzer fügt eigenen Plattformkey ein.
3. lain verbindet, liest geeigneten Kontostand und bietet Modelle optional an.

Der vorhandene `StepFunQuotaAdapter` liest mit demselben persönlichen Key [GET `/v1/accounts`](https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md). Kein neuer OAuth-, Quota-Key- oder Tarifschritt. Preis-/Tarifauswahl ist kein Nachweis eines Accounts. `.ai` ist [offiziell als regionale Inferenzbasis](https://platform.stepfun.com/docs/zh/step-code/configuration/models.md) genannt, sein Accountreader aber noch gesondert zu qualifizieren.

**Scope:** persönliches Prepaid/Postpaid-Geldkonto ist keine Step-Planquote. Kumulierte Aufladungen/Geschenke sind keine Prozentlimits. [Projektkeys](https://platform.stepfun.com/docs/zh/guides/organization/api-keys.md) belasten stattdessen Organisationscredit ohne zusätzliche Inferenzheader; [Projekt-/Mitgliedcaps](https://platform.stepfun.com/docs/zh/guides/organization/credits.md) sind weitere Grenzen. Ohne belegten Reader kein künstliches Projektformular und keine automatische Deklaration eines Organisationskeys als persönlicher Cashpool. Bei unklarem Scope: Inferenz verbunden, Balance noch ungeklärt, Consolelink.

**Monitor-only/Reconnect:** persönlicher Key kann nur zur Balance gespeichert werden, ohne Route. Key bleibt ausgabefähig. Erneuerung über offiziellen Keydialog, regionale Produktbindung erhalten, Quota neu qualifizieren. Delegiertes PAYG-OAuth oder automatische Keyprovisionierung ist in den geprüften Quellen nicht belegt.

## xAI API-Team: Team automatisch erkennen, Readmanagement optional am selben Account

1. Nutzer wählt **xAI API** und fügt API-Key ein. lain liest offizielle Keymetadaten und erkennt das API-Team automatisch.
2. Optional **Abrechnung anzeigen** am selben Konto: passenden Management-Key einfügen; lain prüft dessen Team/Scope. Nur bei mehreren autorisierten Ressourcen eine echte Resourcewahl.
3. lain zeigt eigenständige Zustände für Inferenz und Abrechnung; Modelle optional später wählen.

**Gezielter neuer Primärbeleg:** [Accountreferenz](https://docs.x.ai/developers/rest-api-reference/inference/other), am 24.10.2026 geöffnet, dokumentiert **GET `https://api.x.ai/v1/api-key`** mit `team_id`, `api_key_id`, ACLs und Block-/Disabledstatus. Damit gehört die Team-ID nicht ins normale Benutzerformular. Das ist ein Metadatenread, kein Modellprompt. Berechtigungsfehler bleiben sichtbar, kein Rateteam.

[Managementvalidation](https://docs.x.ai/developers/rest-api-reference/management/auth) liefert mit dem separaten Management-Key `scope`, `scopeId`, `acls`. Bei `SCOPE_TEAM` automatisch gegen Inferenz-`team_id` vergleichen. `SCOPE_ORGANIZATION` ist keine Team-ID; benötigte Teamautorisierung/Discovery ist noch nicht belegt. Nur providerbelegt autorisierte Teams anbieten. Falls Discovery fehlt, allenfalls bewusstes erweitertes Team-ID-Feld mit noch ungeprüfter Bindung; keinen automatischen Organisations-ID→Team-ID-Tausch.

**Credentials:** Inferenz-Key für Modell-/Endpoint-ACLs, optional separat gespeicherter Management-Key für Billing. Die [Managementeinführung](https://docs.x.ai/developers/rest-api-reference/management) nennt `Management Keys` Read+Write als Benutzerberechtigung zum Erstellen eines Management-Keys; das ist keine bestätigte kleinste Billing-Read-ACL. Deren genaue Strings bleiben offen. Ein abgelaufener/widerrufener Monitorzugang ändert die gültige Inferenzverbindung nicht.

**Monitoring:** [Billing](https://docs.x.ai/developers/rest-api-reference/management/billing) liefert Prepaid-Centledger, getrennte laufende Postpaidrechnung/Spendinglimits und begrenzte historische USD-Nutzung. Nicht zu einem angeblichen Wochenpool addieren. Vorzeichen des Prepaidledgers qualifizieren; kein `abs()`. Ohne Management-Key sind [exakte Antwortkosten](https://docs.x.ai/developers/cost-tracking) optional nützlich, aber nur lain-beobachtete Requests statt kontoweites Restbudget.

**Monitoring allein:** der Providerbillingpfad benötigt Management-Key und autorisiertes Team, keinen Inferenz-Key und kein Modell. Für einen Teammanagement-Key kann ScopeId automatisch genügen. Zusätzlich zur Katalog-/Tarif-/Templatekopplung verlangt lain heute im `MonitorAccessService` bereits einen API-Key-Account: Zielarchitektur muss einen echten Monitor-only-Account erlauben, keinen Dummy-Inferenzkey. Das ist eine Accountcapability, kein zweiter fremder Account.

**Optionale fortgeschrittene Provisionierung:** ein ausreichend berechtigter Management-Key kann laut [offiziellem Keymanagement](https://docs.x.ai/developers/management-api-guide) einen Inferenz-Key erzeugen und dessen Team binden. Das könnte einen separaten „Inferenzzugang anlegen“-Pfad bilden. Keycreatorrecht ist breiter als Billingread; nicht voraussetzen, keinen Key bei reinem Monitorconnect erzeugen. Kein Topup oder Ausgabenlimitwechsel gehört dazu. Aktuelle Aufgabe entwirft nur den Ablauf.

**Reconnect:** nur betroffene Credentialcapability erneuern, Team erneut vergleichen, alten Snapshot invalidieren. Kein öffentlich belegtes API-Team-OAuth-Clientregistrierungs-/Refreshverfahren. Die Consumer-/Grok-Build-[Browser-/Deviceanmeldung](https://docs.x.ai/build/cli/reference) und deren Sitzcredits sind ein anderes Produkt und ersetzen diesen Ablauf nicht. Einen Grok-Build-CLI-/ACP-Pfad bei Bedarf gesondert benennen; feste Firstparty-Client-ID nicht als für lain freigegeben ausliefern.

## Gemeinsame Codefolgen ohne Implementierung

- Leere `SelectedModelRouteIds` erlauben bereits, keine tatsächliche Modellroute anzulegen. `ProviderConnectionDto` bleibt jedoch an Katalogrouten, passende Billingpläne und mindestens ein zulässiges Inferenztemplate gekoppelt (`ProviderCatalogRegistry.cs:44–71`, `AccountService.cs:42–81`). Echte Monitor-only-Verbindungen benötigen eine eigenständige Account-/Capabilityverbindung ohne diese Pflichtkopplung; Modellwahl und Routenanlage bleiben nachgelagert.
- Ein Flowdeskriptor muss öffentlich unterstützten Browserlogin, API-Key und optionale zusätzliche Monitoringcapability unterscheiden. OAuth-Maintainerwerte sind Produktkonfiguration, keine `ProviderCredentialFieldDto`-Nutzereingaben.
- Credentials und Berechtigungen intern getrennt führen: Inferenz, Quota, optionale Keyerstellung. „Monitor-only“ als Nutzungsabsicht von echten Readrechten trennen.
- `PoeOAuthAttempt`, vorhandene Quotaadapter und allgemeiner Monitorsecret-Speicher sind vorhanden. Fehlende Hostverdrahtung/Provideroptionen gezielt ergänzen, keine Neubeschaffung von funktionierenden Feldern behaupten.
- `AccountService` bildet IDs aktuell aus `provider + API-Key` und dedupliziert anhand des Keys. Reconnect/Rotation benötigt stabile logische Identität plus überprüfte Provideridentität, damit ein neues Secret nicht automatisch ein neues Konto erzeugt.
- `MonitorAccessService` koppelt Monitorzugang heute an einen vorhandenen API-Key-Account. Für xAI Management-only capability-basiert lösen; für Synthetic/Poe/Step-same-key überhaupt keine zusätzliche Monitorstrecke anzeigen.
- Erfolgreicher Connect löst nur ausdrücklich zu diesem Flow gehörende erlaubte Reads aus. Credentials speichern, Modellberechtigung und bezahlte Probe sind eigenständige Aussagen; weder Accountname noch geklickter Tarif sind Providerbeweis.

Begrenzte nächste Arbeit: zuerst Poe-Hostflow und Synthetic-Parser als konkretes Umsetzungspaket spezifizieren; Step-Plan-Clientverteilung/Rollout und xAI-Readrechte/Scope separat qualifizieren. Alle Abläufe und offenen Punkte pro Produkt sind in der JSON dokumentiert.
