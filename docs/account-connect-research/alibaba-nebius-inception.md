# Account Connect: Alibaba, Nebius und Inception

Stand **24.10.2026**, lain-Basis `5dc916f`. Vorschlag im Architektur-Sparring,
keine Implementierungsfreigabe oder bereits getroffene Produktentscheidung.
Vorige Quellenqualifikation in `docs/quota-auth-research/alibaba-nebius-inception.*`
wiederverwendet. Für diesen Auftrag nur offizielle öffentliche CLI-Quellen zu
Speicherort, Profilauswahl, Redaction, Workspaceauswahl und Refresh nachgelesen.
Keine Logins, Registrierungen, privaten Daten, Anbieterreads, Inferenz, Käufe,
Runtime, Builds oder Produktcodeänderungen. Die alten Reports bleiben bytegleich.

| Produkt | Bevorzugter Einstieg | Was der Benutzer eingibt | Danach verfügbar |
| --- | --- | --- | --- |
| Alibaba gewöhnliche Model-Studio-API | „Mit Alibaba anmelden“ über offizielle CLI | Browseranmeldung; Region/Workspace nur bei tatsächlicher Unklarheit | Gewöhnlicher Inferenzkey und getrennte Console-Session für Usage; kein Cloud-AK nötig |
| Alibaba Coding Plan | Eigener Subscription-Eintrag, zunächst nur Nutzung | Consoleanmeldung; kein Subscriptionkey fürs reine Lesen | Drei Callbudgetfenster; bestehender lain-Inferenzausschluss bleibt |
| Nebius Token Factory Serverless | „Mit API-Schlüssel verbinden“ | Einmal Projektkey | Inferenztransport; passiv beobachtete RPM/TPM ohne weiteren Monitorlogin |
| Inception Mercury | „Mit API-Schlüssel verbinden“ | Einmal API-Key | Inferenztransport; allenfalls lokal beobachtete Tokenusage, keine belegte Kontobalance |

## Was am bisherigen Connect diese Produkte unnötig erschwert

`MainWindow.AccountChooser.cs` trennt Codexlogin von Discovery und manueller
API-Verbindung. `MainWindow.Providers.cs` und der CLI-`AccountWizard.cs` verlangen
vor dem Speichern eine explizite Tarifauswahl, ein Secret und sämtliche als
Required beschriebenen Scopefelder. Alibaba verlangt so Key, Region, Workspace
und Service Deployment Scope. Für Nebius/Inception wird ebenfalls zwischen
Trial und PAYG gewählt, obwohl deren realer Kontostatus damit nicht ermittelt
wird. Die vorhandene `AccountService.AddApi`-Schnittstelle erwartet genau einen
API-Key; `MonitorAccessCatalog` bietet für diese drei Anbieter keine Zugänge.

**Vorschlag:** Der Benutzer wählt zuerst das Produkt und dessen besten belegten
Connectweg. Modelle und Anzeigename sind optionale spätere Einstellungen.
Trial versus PAYG ist hier kein notwendiger Authschritt: Ein normaler
Alibaba-Key kann automatisch Trialtokens und danach andere Abrechnungspfade
verwenden, Nebius und Inception haben ebenfalls keinen separaten Trialkey als
belegten Standard. Kontotarif und Restguthaben bleiben ohne Beobachtung unbekannt.
Ein unbestätigter Tarifklick darf keine Berechtigung und keine Kostensicherheit
schaffen. Die fachliche Kostenrichtlinie muss bei dieser Änderung mit angepasst
werden; lediglich das Tarif-Pflichtfeld zu verstecken würde den bestehenden
Hostvertrag verletzen.

## Alibaba Model Studio: praktikabler Anschluss ohne Keykopieren

### Minimaler Benutzerablauf

1. In lain „Alibaba Model Studio“ → **„Mit Alibaba anmelden“**. Der kurze Text
   kündigt Browseranmeldung, einen gewöhnlichen Inferenzkey und Nutzungszugang an.
2. Die offizielle Alibaba-Seite öffnet sich. Benutzer meldet sich an und erteilt
   die dort angebotene Autorisierung. Fehlende Kontoaktivierung oder Rechte
   werden auf dieser Seite erledigt; lain verspricht dafür keine Anmeldung mit
   einem Klick. Keine Cloud-AccessKeys abfragen.
3. Lain übernimmt nur die in diesem Connectvorgang erhaltenen Credentials und
   deren belegten Scope. Ist der Callback vollständig, gibt es keine zusätzliche
   Region-/Workspaceabfrage. Bei mehreren tatsächlich zugänglichen Workspaces
   oder fehlendem Scope erscheint genau die notwendige Auswahl. Ein Wechsel
   verlangt einen dazu passenden Key/Host; bloßes Auswählen darf dessen Rechte
   nicht umdeuten.
4. Ergebnis: „Alibaba verbunden“ mit getrennten Zuständen für Inferenzzugang und
   Nutzung. Eine zulässige reine Modelllistenprüfung kann Keyzugriff belegen;
   sie ist kein Inferenztest. Fehlende Monitoringrechte lassen den Inferenzteil
   erhalten und zeigen die konkret fehlende Berechtigung an.

Für den aktuellen internationalen Katalog ist `--console-site international`
passend; domestic ist eine explizite Alternative für Nutzer des chinesischen
Kontos, keine zusätzliche Pflichtfrage für alle. Das Standardziel ist das vom
Provider zurückgegebene Konto mit Scope, keine aus Sprache/IP geratene Region.
Die vorhandene internationale Singapore-Route darf nur übernommen werden, wenn
Callback/zulässiger Nachweis dazu passen.

### Wie der Anschluss technisch ohne Copy-Paste funktioniert

Die [offizielle Authdokumentation](https://www.alibabacloud.com/help/en/model-studio/cli/config)
belegt `bl auth login --console --console-site international`. Die CLI kann bei
Bedarf einen gewöhnlichen API-Key erzeugen; Subscriptionkeys sind davon
getrennt. Der [Login-Sourcecode](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/auth/login-console.ts)
empfängt Console-Token und optional `api_key`, `base_url`, `workspace_id`,
`console_region`, `console_site` per Loopbackcallback mit Zufallsstate und
15-Minuten-Timeout. Das ist ein belegter providerbetriebener Login, keine
frei registrierbare neue OAuth-App von lain.

**Bevorzugte Integrationsvariante:** Lain startet die qualifizierte offizielle
CLI mit `BAILIAN_CONFIG_DIR` auf ein eigenes privates Verzeichnis pro Verbindung
und einem expliziten Profil, etwa `lain`. [Configpfade](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/config/paths.ts)
unterstützen dieses Override; [Loader](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/config/loader.ts)
und [Authstore](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/auth/store.ts)
legen Profilblöcke in **derselben `config.json`**, mit privaten Dateirechten,
an. Ein expliziter Profil-Login aktiviert dieses Profil innerhalb dieses
Configverzeichnisses; das private Verzeichnis verhindert Änderungen am normalen
CLI-Profil des Benutzers.

Nach dem bewusst gestarteten Login liest der Host ausschließlich den frisch
angelegten Profilblock in diesem eigenen Verzeichnis. Er übernimmt den
gewöhnlichen `api_key` mit Host und Scope in den bereits nötigen privaten
Inferenztransport; Consolezugang bleibt im zugehörigen Connector für eine feste
Read-Allowlist. So muss der Benutzer keinen Key kopieren. Keine Suche durch
`~/.bailian`, keine Übernahme eines zufällig aktiven fremden Profils, kein
Auslesen bestehender Browsercookies. Der [Befehl `config show`](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/config/show.ts)
maskiert Secrets und eignet sich **nicht** als Keyexport. Der eigene Dateiadapter
ist deshalb eine explizite Integrationsabhängigkeit mit qualifizierter Version,
kein dokumentierter allgemeiner Secret-Exportvertrag.

Für den CLI-Prozess darf der Connector keine zufällig geerbten
`DASHSCOPE_API_KEY`-/Base-URL-/Workspace- oder Cloud-AK-Overrides verwenden.
[Credentialauflösung](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/auth/resolver.ts)
gibt solchen Variablen teils Vorrang. Das eigene Verzeichnis enthält im
Standardpfad weder AK/SK noch STS. Rohsecrets gehen nicht an die UI, Kommandozeile,
Logs oder Exporte. Das sind Anforderungen an die spätere Umsetzung, hier wurde
kein CLI-Prozess ausgeführt.

### Rechte, Scope und echte Lebensdauer

- **Inferenz:** gewöhnlicher regionaler Key im passenden Workspace/API-Host.
  Erlaubte Modelle/IPs stammen aus Providerrechten. Für Bootstrap sind
  Administrator-/API-Key-Seitenrechte erforderlich. [Keydokumentation](https://www.alibabacloud.com/help/en/model-studio/get-api-key).
- **Nutzung:** eigener Console-Bearer im passenden Site-/Regionsscope;
  Model Monitoring braucht Aktivierung durch den Cloudkontoinhaber und
  `ModelObservation-FullAccess`. Kein Recht folgt allein aus der Anmeldung.
  [Berechtigungen](https://www.alibabacloud.com/help/en/model-studio/application-permission-management-overview).
- **Regionen:** Die geprüfte CLI-Gatewaytabelle qualifiziert Consolezugang nur
  für Beijing und Singapore. Frankfurt, Tokyo, Hongkong und Virginia haben
  Inferenzregionen, aber daraus folgt kein Console-Reportingpfad. Unbekannte
  Regionen ablehnen, insbesondere den CLI-Beijing-Fallback nicht übernehmen.
  [Gateway](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/console/gateway.ts),
  [Regionen](https://www.alibabacloud.com/help/en/model-studio/regions).
- **Erneuerung:** Der offizielle [Refreshcode](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/auth/refresh-token.ts)
  braucht gespeicherte AK/SK (optional STS), um `GenerateCLIAccessToken`
  aufzurufen. Ohne diese Credentials ist kein stiller Refresh belegt. Lain
  verlangt sie **nicht nachträglich als versteckte Connectvoraussetzung**.
  Stattdessen: „Nutzungszugang erneuern“ startet erneut den Browserlogin im
  selben eigenen Profil. Der Inferenzkey wird unabhängig behandelt; eine
  abgelaufene Console-Session macht ihn nicht automatisch ungültig. Exakte
  Console-Token-TTL ist unbekannt. [Client](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/core/src/client/client.ts)
  zeigt den getrennten Retry-/Refreshpfad.
- **Trennen:** Connector/Transport deaktivieren und eigene lokale Credentials
  entsprechend der lain-Aufbewahrung entfernen. CLI-Logout löscht lokale
  Credentials; damit ist kein serverseitiger Keywiderruf bewiesen. Eine
  Provider-Keylöschung wäre eine gesonderte ausdrücklich gewählte Aktion.

Ein vorhandener gewöhnlicher Key kann alternativ einmal eingefügt werden.
Optionaler Consolelogin ergänzt später Nutzung am gleichen Account, ohne einen
zweiten Inferenzaccount anzulegen. Einen bereits vorhandenen fremden
CLI-Profilimport nur als expliziten erweiterten Import anbieten, nicht als
Standarderkennung.

### Was Monitoring nach diesem Login lesen kann

Bevorzugt feste rohe Console-Leseoperationen über die offizielle CLI; sie erhalten
exakte Epoch-ms und Status. [Trial/Usage-Quellcode](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/shared.ts)
belegt Modellrest (`quotaTotal`), ursprüngliches Grant, Ablauf und Workspaceusage.
`usage free`/`usage stats` formatieren einige Zeitwerte als lokales Datum; diese
Ausgabe reicht nicht für präzisen Reset/Ablauf. Die [generische Consoleoperation](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/console/call.ts)
ermöglicht eine feste Read-Allowlist. [Workspace list](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/workspace/list.ts)
kann Namen und IDs liefern; sie beweist allein keine Bindung eines Keys an einen
beliebig gewählten Workspace.

Trialtokens bleiben Account + konkretes Modell/Snapshot, Input/Output gemeinsam;
Singapore + International Scope, einmaliger Ablauf statt monatlichem Reset.
Workspaceusage bleibt das angefragte Intervall mit tatsächlichen Units. Die
gewöhnliche API liefert auch konfigurierte Modell-/Workspacelimits unter
`GET /api/v1/models/limits`. Ein leeres Resultat ist kein Rest von Null. Ohne
Providerzeit bleibt DataAsOf unbekannt und die UI nennt die Aktualisierungslücke.

**Finanzsaldo nicht in den Standardlogin ziehen:** BSS-Balance/Bills benötigen
separate Finanzrechte und signierte Cloudcredentials. Optionaler späterer
Finanzconnector, kein stiller AK-Dialog nach erfolgreichem Browserconnect.
Auch der in der [FAQ](https://www.alibabacloud.com/help/en/model-studio/new-free-quota)
genannte eigenständige OAuth-Inferenzpool mit Calls/Tag ist ein anderes Produkt;
sein Consumptionvertrag ist nicht durch den ordinary-key CLI-Flow belegt.

### Voraussetzungen für Maintainer und gezielte nächste Probe

Offizielle CLI als qualifizierte, versionierte Abhängigkeit bereitstellen;
Linuxverteilung, Browsercallback und private Configdir-Unterstützung nachweisen.
Geprüfter Sourcecommit `3c8031f4d994c30820eb4d145d9404ba4a0acd39` enthält
[package version 2.5.1](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/cli/package.json),
während manche Doku 2.2.1 nennt. Daraus folgt keine Prüfung eines veröffentlichten
2.5.1-Binaries. Release/Integrität und Profil-/Callbacksemantik vor Auslieferung
abgleichen. Quellcode ist [Apache-2.0](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/LICENSE);
bei Bündelung erforderliche Lizenz-/Noticeangaben berücksichtigen. Eine eigene
Drittanbieter-OAuthregistrierung ist für den dokumentierten CLI-Connector nicht
belegt nötig; eine eigene lain-OAuthimplementierung bräuchte einen gesonderten
Anbietervertrag für Client-ID/Redirects/Scopes/Refresh.

Eine spätere explizit genehmigte Probe muss nur klären: liefert der gewählte
internationale Login ordinary key + Host + Region/Workspace ins eigene Profil;
welcher Readzugriff ist bei diesem Nutzer verfügbar; wie zeigt Sessionablauf
sich ohne AK/SK. Keine Inferenz, kein Serviceaccount oder Kauf zur Connectprobe
voraussetzen. Heute bleiben Missing-Scope-Callback und reale regionale
Telemetryverfügbarkeit offen.

## Alibaba Coding Plan: eigenständig, vorerst nur Nutzungsverbindung

Minimal: „Coding Plan-Nutzung verbinden“ → derselbe offizielle Consolelogin →
eigenständige Subscriptionkachel, wenn tatsächlich eine aktive Subscription
zurückkommt. Kein Subscriptionkauf, kein Erzwingen eines `sk-sp`-Keys für reines
Lesen. Gibt es keine Subscription, bleibt die Antwort „Kein aktiver Coding Plan
gefunden“, ohne impliziten Abschluss.

Der [offizielle Usagecommand](https://github.com/modelstudioai/cli/blob/3c8031f4d994c30820eb4d145d9404ba4a0acd39/packages/commands/src/commands/usage/coding-plan.ts)
liest per Consoleauth die drei `per5Hour/perWeek/perBillMonth`-Fenster in
Modellcalls. Fünfstundenfenster rollt; Wochenreset Montag 00:00 UTC+08 und
Abrechnungsmonat am Verlängerungstag. Exakte Instanzzuordnung nicht durch die
CLI-Auswahl der ersten VALID-Instanz ersetzen. Falls bereits ein eigener
Alibaba-Connector dasselbe nachgewiesene Konto/Site/Regionsziel enthält, kann er
für diese Lesekachel wiederverwendet werden; keine erneute Anmeldung erzwingen.

Subscription-Inferenz erfordert eigenen Key und eigenen Host. Der bestehende
lain-Backendpfad schließt sie aus; eine Nutzungsverbindung ändert das nicht.
Kein separater Inferenz-Account wird ohne Auftrag angelegt. Lebensdauer,
Reconnect und fehlende AK/SK entsprechen dem Consoleconnector. Token Plan Seat
Credits und der gewöhnliche Trial sind andere Budgets.

## Nebius Token Factory: heute ein Key, keine Cloud-IAM-Hürde

Minimaler Ablauf:

1. „Nebius Token Factory“ → „API-Schlüssel verbinden“; offizieller Link führt
   direkt zu [Project → API Keys](https://tokenfactory.nebius.com/project/api-keys).
2. Benutzer meldet sich bei Bedarf mit Google/GitHub oder seinem eingerichteten
   SSO an, wählt auf der Providerseite das gewünschte Projekt und erstellt einen
   benannten Key. Neue Konten können zusätzliche Abrechnungs-/Kartenschritte
   benötigen; vorhandene Konten müssen diese nicht in lain wiederholen.
3. Key einmal verdeckt einfügen → verbinden. Kein Pflichtfeld für Org-ID,
   Projekt-ID, Region, Trial/PAYG oder Modell. Der qualifizierte aktuelle
   Serverlesshost ist vorgegeben; andere Hosts gehören in eine explizite
   qualifizierte Erweiterung, nicht in freie Eingabe für jeden Benutzer.
4. Der gleiche Inferenzkey liefert im normalen Betrieb beobachtete RPM/TPM-
   Header. Vor der ersten Antwort zeigt lain „Noch keine Limits beobachtet“;
   kein weiterer Monitorzugang oder Testprompt für die Quotenanzeige.

[API-Auth](https://docs.tokenfactory.nebius.com/api-reference/introduction.md) und
[Quickstart](https://docs.tokenfactory.nebius.com/quickstart.md) belegen diesen
Keyweg. Eine eventuell angebotene reine Modelllistenprüfung bestätigt nur diesen
Readzugriff, keine Billing- oder erfolgreiche Inferenzberechtigung. Die
[Rate-Limit-Header](https://docs.tokenfactory.nebius.com/ai-models-inference/rate-limits.md)
geben dynamische RPM/TPM und Restkapazität, Reset in relativen Sekunden sowie ein
separates 15-Minuten-Anpassungsfenster; kein Kontoguthaben. Keyprojekt/Orgpool wird
nicht aus dem lokalen Accountnamen geraten. Headerbeobachtungen brauchen kein
separates Projekt-ID-Formular.

Der Key ist statisch, wird nur einmal angezeigt und hat keinen belegten
OAuthrefresh. Ersetzen ist eine lokale Aktualisierung derselben Verbindung;
Rotation/Providerwiderruf sind getrennte Lebenszyklen. [RBAC](https://docs.tokenfactory.nebius.com/team-access/groups.md)
trennt Project Admin/Member (eigene Keys/Projektressourcen) von Org Billing
Manager (Orgusage, kein Projektzugriff). Für Billing braucht der Benutzer
zusätzliche Orgrolle, die der Inferenzkey nicht beweist. Balance bleibt beim
[offiziellen Org-Billing-/Usagezugang](https://docs.tokenfactory.nebius.com/other-capabilities/billing-new.md).

**OAuth nicht als fertig verfügbaren Personenlogin anbieten:** Offizielles
[SDK-PKCE](https://github.com/nebius/pysdk/blob/main/src/nebius/aio/token/federation_bearer/auth.py)
und der in den [Token-Factory-SSOdocs](https://docs.tokenfactory.nebius.com/team-access/sso.md)
genannte CLI-Federationendpoint sind Cloud-/Föderationsprimitiven. Der konkrete
[AI_STUDIO StaticKey-Bootstrap](https://github.com/nebius/api/blob/e855cccf88420a80741b04075674528d75f3e238/nebius/iam/v1/static_key.proto)
unterstützt aktuell nur **Serviceaccounts**. Er darf nicht vor den einfachen
Personenkeyweg geschaltet werden. Die Nutzer müssten sonst neue Identitäten,
Gruppen und Rechte einrichten, ohne dass die heutige Serverless-/Billingannahme
belegt wäre.

Gezielte nächste Nachweisfrage: Kann ein berechtigter Token-Factory-Project-
Member sein vorhandenes Personenkey über einen unterstützten CLI-/API-Bootstrap
erzeugen, oder ist nur `StaticKeyService/Issue` für Serviceaccounts zulässig?
Welche minimalen Rollen, `aitenant`/Projektzuordnung, Clientregistrierung und
Schlüssellebensdauer gelten? Erst nach dieser öffentlichen Anbieterbestätigung
wäre eine kleine separat autorisierte Modelllisten-Readprobe mit dem erzeugten
Key sinnvoll; kein Serviceaccountsetup als Voraussetzung einer normalen
Connectprobe. Ein Nebius-Cloud-IAM-Token ist laut [offizieller Integration](https://github.com/nebius/nebius-physical-ai/blob/main/docs/workbench/token-factory.md)
ein anderes Credential und gehört nicht als vermuteter Key in die Inferenzroute.

Dedicated Observability wird nur dann separat eingerichtet, wenn der Nutzer
dieses Produkt auswählt: vorhandener geeigneter Key + konkretes Projekt,
[Dedicated-only-Metriken](https://docs.tokenfactory.nebius.com/ai-models-inference/observability.md).
Projekt-ID wird erst dann benötigt. Das ist keine Serverlessbalance; GPU-Cloud-
Billingexporte werden ebenfalls nicht in den Standardconnect gezogen.

## Inception: ehrlicher, schlanker Keyfallback

Minimal: „Inception Mercury“ → „API-Schlüssel verbinden“ → offizielle
[Platform-Keyseite](https://platform.inceptionlabs.ai/dashboard/api-keys) → dort
anmelden/Key erzeugen, falls noch keiner vorhanden → einmal verdeckt in lain
einfügen. Optionaler Anzeigename und Modellauswahl später. Keine Region-,
Projekt-, Org-, Trial/PAYG- oder neue Monitoringcredential-Pflichtabfrage.
Key und APIhost reichen für den dokumentierten Connector. Eine Kontoanmeldung
ist bei Inception selbst nötig, keine nachgewiesene delegierte OAuthanmeldung
von lain.

Die [Authseite](https://docs.inceptionlabs.ai/get-started/authentication) und
[offiziellen SDKs](https://docs.inceptionlabs.ai/get-started/sdks) verlangen API-Key;
öffentliche Doku/SDK qualifizieren keinen OAuth-, Device-, CLI-Keybootstrap- oder
Read-only-Balancetokenvertrag. Daher kein dekorativer „Mit Inception anmelden“-
Button, der am Ende trotzdem einen unvorhergesehenen Key verlangt.

Optionaler späterer Connectcheck kann `GET /v1/models` als reinen Lesezugriff
nutzen, ohne Inferenz/Billing zu bestätigen. Ansonsten „Key gespeichert;
Providerzugriff noch nicht geprüft“. Statische Keys werden bewusst ersetzt;
keine Refreshfähigkeit versprechen. Trennen ist kein bewiesener serverseitiger
Widerruf.

Monitoring braucht **kein weiteres Secret**, solange es ausschließlich die
[Tokenusage normaler Antworten](https://docs.inceptionlabs.ai/resources/faq)
zeigt: Input inklusive Cache, Output inklusive Reasoning, Unterfelder nicht
doppelt addieren. Diese Anzeige muss „Lokal beobachteter Verbrauch“ heißen;
Kontorest und übrige Clients fehlen. Das einmalige kontoweite Neukundenangebot
ist kein beobachteter verbleibender Grant. Die Usagekachel darf keine 100M
anzeigen und lokal davon herunterzählen. Billing erhält einen manuellen
Platformlink. Fehlende öffentliche Reports-/OAuthverträge werden in einem
späteren begrenzten Anbieterbeleg geklärt, nicht durch private Cookies ersetzt.

## Gemeinsame Folgerungen für die spätere Umsetzung

- Connectmethoden müssen Produkt und Fähigkeiten beschreiben können: offizieller
  CLI-Browserlogin, Keybootstrap, manuell eingegebener Key und reine
  Nutzungsverbindung. Der heutige Ein-Key-Formularvertrag reicht für Alibaba
  mit mehreren Credentialdomänen und für Coding-Plan-Monitoring ohne Inferenzkey
  nicht aus.
- Eine Verbindung kann Inferenzkey und Monitorzugang mit getrenntem Zustand,
  Herkunft, Scope und Ablauf besitzen. Nach fehlgeschlagenem Monitorrefresh
  nicht pauschal die funktionierende Inferenzverbindung ersetzen oder sperren.
- Gemeinsame UI-Zustände: Anmeldung läuft/abgebrochen, Key gespeichert,
  Modelllisten-Zugriff geprüft, Nutzung verfügbar, Nutzung muss erneut
  autorisiert werden, Nutzung nicht unterstützt/noch nicht beobachtet. Ein
  einzelnes „Connected“ darf keine Live-Inferenz oder Billingvollständigkeit
  behaupten.
- Die Desktop- und CLI-Wizards verwenden denselben Connectvertrag. Er enthält
  nur tatsächlich nötige Eingaben; Scopeauswahl folgt realer Mehrdeutigkeit.
  Tarif-/Entitlement- und Kostenpolitik bleiben von Authbeobachtung getrennt.
- Für die hier vorgeschlagenen Varianten fehlen Host-Operationen/Connector,
  sichere Providerprofiladoption, getrennte Credentiallebenszyklen,
  Monitoringadapter, abweichende Produkt-/Regionenscopes und angemessene
  Verifikation. Das ist ein konkreter Übergabeentwurf, kein bereits getesteter
  Account-Connect.
