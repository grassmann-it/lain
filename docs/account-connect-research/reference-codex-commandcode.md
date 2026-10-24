# Account Connect: Referenzen Codex und Command Code

Stand: 24.10.2026. Gezielte Ergänzung für den gemeinsamen Provider-Chooser;
keine vollständige erneute Bestandsprüfung. Codequelle ist die aktuelle
Arbeitskopie `/home/tomka/Desktop/lain`, Basis `5dc916f` plus dortige Änderungen.
Die JSON-Datei enthält Dateihashes, Quellen und zwei vollständige Profile.
Keine Logins, privaten Kontenabfragen, Installationen, Laufzeitversuche oder
Produktänderungen. Bestehende Reports bleiben unverändert.

## Entscheidung für den Chooser

| Referenz | Heutiger Anschluss | Minimaler Zielablauf | Zusätzliche Monitoring-Credentials |
|---|---|---|---|
| Codex | Funktionierender eigener Browser-Login über Host/Core; eigener Refresh-Besitz | Codex wählen → Browser autorisieren → zurück zu Lain; Quote automatisch anhängen | Keine: aktueller eigener Access-Token + Account-ID |
| Command Code | API-Key-Anschluss und Same-Key-Quota-Reader vorhanden | Gespeicherten Key nutzen; sonst offizieller CLI-Browserbootstrap und eng begrenzte Key-Übernahme qualifizieren; Studio-Key als sofortiger Fallback | Keine: derselbe Command-Code-Key |

Ein gespeicherter Login beweist keine erfolgreiche Modellanfrage. Ebenso muss
Reporting getrennt erfolgreich sein dürfen: Ein reines Monitorkonto benötigt
keine Inferenzroute und keine kostenpflichtige Testanfrage. Für Codex verlangt
der heutige Core allerdings aktive Routenbindung für Refresh; ein dauerhaft
alleinstehender Monitor braucht dafür eine ausdrückliche Lifecycle-Erweiterung.

## Codex: bestehenden eigenen Login erhalten

Der Account-Chooser bietet bereits **Sign in to Codex (browser)** an.
`CodexOwnedLoginAdapter` nutzt den schmalen authentifizierten
`HttpCodexOwnedLoginBackend`, nicht Legacy-Management. Der Core führt PKCE und
Loopback-Callback an `127.0.0.1:1455` aus; er prüft State, begrenzt Laufzeit und
persistiert erst nach erfolgreichem Austausch. Der Host übernimmt nur die
explizit zurückgegebene neue Owned-Datei. Der bestehende Account wird anhand
der Provider-Account-ID wiedererkannt; Modelle/Routen beginnen unverified.
Das ist vorhandene Implementierung und kein noch zu bauender OAuth-Stub.

Der Nutzer wählt Codex, öffnet die Autorisierung, wählt die beabsichtigte
Identität/Workspace und kehrt zu Lain zurück. Zusätzliche Client-ID, Cookie,
Monitoring-Key oder CLI-Einrichtung gehören nicht in diesen Ablauf.
Der Host speichert eine Owned-Dateireferenz. Refresh-Token bleiben beim Core;
`SecretStore` liest für Quota jeweils den aktuellen Access-Token und Ablauf aus
der Owned-Datei. Registry und angewandte Routenbindung begrenzen Refresh auf
wirklich eigene, aktive Credentials. Eigene Credentials ohne aktive Auswahl,
deaktivierte oder entfernte Credentials bleiben im Core inert.

`CodexQuotaAdapter` liest heute direkt
`https://chatgpt.com/backend-api/wham/usage` mit Bearer und
`ChatGPT-Account-Id`. Er zeigt vorhandene Primary-/Secondary-Prozentfenster mit
Reset und tatsächlicher Fensterdauer; unbekannte Angaben bleiben unbekannt.
API-Keys sind dafür ausdrücklich nicht qualifiziert. Das sind Account-Limits,
keine aus lokalen Tokenzählern errechneten USD-Budgets. Der Endpunkt ist als
bestehendes Verhalten belegt; diese Recherche macht ihn nicht zu einem
öffentlichen stabilen Drittanbieter-API-Vertrag.

Beim Reconnect bleibt dieselbe überprüfte Account-ID erhalten; ein anderes
Konto darf nicht unbemerkt die vorhandene Verbindung ersetzen. Abbruch darf
einen bestehenden aktiven Zugang nicht überschreiben. Entfernen ist heute
lokale Konfigurationsentfernung nach Lösen aktiver Routen und behält private
Credentials. Pending-Login abbrechen ist kein Logout. Ein eigener gezielter
Cleanup-/Revocation-Lifecycle ist deshalb gesonderte Arbeit.

**Externe Snapshots bleiben extern.** Der explizite Import kopiert nur
Access-Token, Account-ID und Ablauf, ohne Refresh-Token oder Zugriff auf den
fremden Refresh-/Revoke-Lifecycle. Sie laufen ab und verlangen bewussten
Reconnect/Reimport. Ein kopiertes Authfile darf nicht allein durch Ablage im
Owned-Verzeichnis eigenen Refresh-Besitz erhalten. Auch ein zukünftiger
Monitoradapter darf nicht global `codex logout` ausführen oder die gemeinsam
von CLI/Extension genutzte Anmeldung verändern.

Die [offizielle Auth-Dokumentation](https://learn.chatgpt.com/docs/auth)
bestätigt Browser-Login und automatische Erneuerung sowie die getrennte
API-Key-Abrechnung. Keine neue öffentliche App-Registrierung ist notwendig,
um den vorhandenen Lain-Core-Pfad zu verwenden; dieser Bericht fügt jedoch
keinen Livebeleg für Browser/Refresh/Inference hinzu.

### Zwei klar getrennte öffentliche Kandidaten

Der offizielle [Codex app-server](https://learn.chatgpt.com/docs/app-server)
bietet managed Browser-/Device-Login und `account/rateLimits/read`, einschließlich
optionaler mehrerer Buckets und Credits. Das ist ein sinnvoller dokumentierter
Host-/Monitor-Kandidat, aber kein schon existierender Lain-Backendwechsel.
Extern übergebene Tokens sind dort ein gesonderter experimenteller Modus mit
Refresh-Verantwortung des Hosts. Ein eigener Adapter müsste Prozess und
Credential-Speicher isolieren und den Account-Lifecycle qualifizieren.

Neu beschreiben die offiziellen [SIWC-OSS-Preview-Dokumente](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
eine direkte dynamische Registrierung: initial `dynamic_agent_client`, danach
die ausgegebene accountgebundene Client-ID speichern. Für diesen konkreten
OSS-Pfad ist keine vorab beantragte Client-ID, kein Secret und kein Partner-Key
dokumentiert. Der Maintainer implementiert PKCE, State/Nonce, validierte
Identität und tatsächlich erteilte Planberechtigung; normale Nutzer geben
keine OAuth-Appdaten ein. Verfügbarkeit und Eignung der Preview bleiben separat
zu prüfen. Das hebt die vorhandene Core-Anbindung nicht auf.

Der Kandidat nutzt [öffentliche Responses-Inferenz](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
mit eigener Token-Audience, `store:false`, `stream:true` und bestätigtem
Abschluss. Er ist keine Freigabe für dieselben Tokens an Backend-API oder WHAM.
Die [Session-Dokumentation](https://developers.openai.com/siwc/token-sharing-open-source/profiles-and-sessions)
fordert getrennte Registrierungen, serialisierte Rotation und gezielte
Session-Revocation und verweist auf ChatGPT Settings → Usage. Ein öffentlicher
Quota-Reader für diese Tokens ist damit nicht belegt. Auch die
[app-server-Anleitung für diesen neuen Flow](https://developers.openai.com/siwc/token-sharing-open-source/codex-app-server)
nutzt einen eigenen Responses-Provider mit vom Host erneuertem Token; das
kann nicht mit dem managed Codex-Login gleichgesetzt werden.

## Command Code: offizieller Browserbootstrap liefert einen Key

Die [offizielle Quickstart-Anleitung](https://commandcode.ai/docs/quickstart)
belegt `command-code login` beziehungsweise `cmd login`: Browser öffnen,
autorisieren, anschließend API-Key im CLI-Authcache. Das ist ein echter
Login-zu-Key-Bootstrap. Er beweist keinen Lain-eigenen OAuth-Clientvertrag und
keinen von Lain zu rotierenden OAuth-Refresh-Token. Lain besitzt heute keinen
Command-Code-Loginadapter; vorhandene API-Key-Anbindung und Quota-Reader
müssen deshalb nicht neu gebaut werden.

Der minimale Zielablauf ist: Command Code wählen, vorhandenen Key
wiederverwenden oder offiziellen CLI-Bootstrap starten, dann nur dessen
überprüftes neues Command-Code-Key-Ergebnis übernehmen und Account/Organisation
anzeigen. Für diesen Helper sind Version, Resultatschema, Abbruch,
Altcache-Erkennung und gegebenenfalls offiziell unterstützte Speicherisolation
noch zu qualifizieren. Der CLI-Authcache kann weitere Provider-Credentials
enthalten; vollständiger automatischer Import oder globaler Logout wäre eine
falsche Ownership-Grenze. Es wird in dieser Recherche kein CLI ausgeführt.

Sofort verfügbar bleibt der kurze Fallback: [Studio](https://commandcode.ai/docs/studio)
öffnen, vorhandene Organisation wählen, API-Key erzeugen/kopieren und einmal
in Lain speichern. Studio dokumentiert die Wiederverwendung desselben Keys
für CLI und Provider API. Ein eigener zusätzlicher Billing-Key, Cookie oder
Browser-Sessionimport ist nicht nötig.

Die [Provider-Referenz](https://commandcode.ai/docs/provider) dokumentiert
`https://api.commandcode.ai/provider/v1` mit Chat Completions, Responses,
Messages und Modellliste. API- und Modellzugriff hängen vom Plan ab; die
heutige Seite schließt Go vom Provider-API-Zugriff aus. Erfolgreicher Login,
`whoami` oder Billing-Read sind daher kein Beleg für Inferenzberechtigung.
Ein reiner Monitor kann ohne Inferenzroute angeboten werden, wenn die
Reporting-Abfragen mit diesem Key tatsächlich erlaubt sind. Bestehende
Organisation und Abrechnung bleiben erhalten; kein automatischer Kauf.

Der vorhandene `CommandCodeQuotaAdapter` liest mit demselben Bearer-Key zuerst
`alpha/whoami?limits=1`. Nur die tatsächlich zurückgegebene `org.id` bestimmt
anschließendes `orgId` für optionale `billing/credits` und
`billing/subscriptions`. Optionaler Billing-Ausfall bleibt partiell. Unterstützt
werden gelieferte Monats-/Kauf-/Freicredits, 5h-/Wochenfenster und Org-/Modell-
Limits. Die zusätzliche monatliche Modellrestmenge bleibt ausdrücklich
unknown. Die historische `PlanCredits`-Tabelle ist kein bestätigtes aktuelles
Planangebot und keine per-Modell-Grenze. Der implementierte Alpha-Reader ist
kein hier belegter stabiler öffentlicher Billing-API-Vertrag.

Reconnect ersetzt den Key erst nach geprüftem Account-/Org-Abgleich; ein
Workspacewechsel ist eine bewusste Scopeänderung. API-Key-Erneuerung wird nicht
als OAuth-Refresh erfunden. Lokales Entfernen meldet die fremde CLI nicht ab
und widerruft keinen gemeinsam genutzten Key automatisch. Gezielt widerrufen
kann der Nutzer ihn in Studio. Fehlendes optionales Reporting blockiert keine
bereits qualifizierte Inferenz; vergangene Beobachtungen bleiben mit ihrem
ursprünglichen Zeitpunkt erkennbar.

Für den bestehenden Same-Key-Pfad fehlt keine eigene OAuth-Registrierung.
Nur eine unabhängige direkte Browser-Neuimplementierung würde einen belegten
Providervertrag und gegebenenfalls eigene Maintainer-Registrierung verlangen.
Das ist nicht durch Eingabe fremder Client-IDs oder durch mehr Setup für den
normalen Nutzer zu lösen.
