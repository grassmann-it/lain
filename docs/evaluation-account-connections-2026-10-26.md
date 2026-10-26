# Vorhandene Evaluierungsaccounts verbinden

Direkter Nutzerauftrag vom 26.10.2026: die neu vorhandenen Zugänge möglichst
über belegtes OAuth, sonst über private API-Key-Eingabe in lain einrichten.
Das ergänzt den abgeschlossenen OAuth-/Device-Lieferstand `ff5771c` und die
gezielte Requesty-Verbindung; kein neues automatisch fortgesetztes Goal.

## Bestand und Ziel

- Synthetic Go und OpenRouter sind bereits verbunden. Vorhandene Accounts,
  Routen, Profile und Historie erhalten; keine zweite Anmeldung allein dafür.
- Requesty ist über die offizielle isolierte CLI angemeldet und privat in lain
  gespeichert. Self-/Keyverbrauch ist lesbar, Organisationswallet nicht.
- Fireworks: vorhandenes API-/Serverless-Guthaben oder Trial, kein Fire Pass.
  Quotaadapter vorhanden, Anmeldung/Accountbindung noch einrichten.
- DeepSeek: Guthaben vorhanden; direkten Gateway-/Kataloganschluss und
  Balancereader ergänzen, vorhandenen Key ausschließlich privat eingeben.
- NanoGPT: Abo vorhanden, genauer Tarif/Preis unbekannt. Direkten Anschluss,
  veröffentlichtes PKCE für Inferenz und separaten `usage:read`-Monitor für
  Aboquoten ergänzen. OAuth-Inferenzkey ist kein Managementtoken.

Keine Käufe, Topups, kostenpflichtigen Fallbacks oder automatischen
Inferenzproben. Benutzer bestätigt tatsächliche Anbietergrants selbst;
API-/Managementkeys gehören ausschließlich in lokale private Eingabefelder.
Eine Anmeldung oder gespeicherter Key beweist noch keinen Live-Quotaabruf.

## Parallele Pakete und Dateigrenzen

| Paket | Verantwortung | Exklusive Dateien |
| --- | --- | --- |
| EC1 | DeepSeek: Katalog, bestehender OpenAI-kompatibler Transport, Balance, Fixtures | neue DeepSeek-Adapter-/Katalog-/Testdateien und Providerdoku |
| EC2 | NanoGPT: Katalog, veröffentlichter OAuthvertrag, separater Subscriptionreader, Fixtures | neue NanoGPT-Adapter-/Katalog-/Testdateien und Providerdoku |
| EC3 | Fireworks: gepinnte offizielle CLI und isolierter Signin-/Keyhandoff; vorhandene API-Key-Alternative erhalten | Fireworks-spezifische Login-/Operator-/Testdateien und Providerdoku |
| EC4 | Privater CLI-/Desktop-Setupweg für neue Tokenarten und ressourcenlosen Eigenaccountmonitor | zugewiesene CLI-/Desktopdateien nach gemeinsamem Vertrag |
| Integrator | gemeinsame additive Verträge, Registrierung, Monitorprojektion, Einbindung und Lieferung | Coreverträge, Hostverdrahtung und gemeinsame Builddateien |
| Abnahme | unabhängige Prüfung, begrenzte serielle Build-/Runtime-Lane | Nachweise und bereinigte Fixtures |

Arbeit in getrennten Cacheworktrees ab gemeinsamem Ausgangscommit, höchstens
zwölf aktive Agenten einschließlich Integrator. Kein Worker startet einen
großen Build oder Runtime ohne den seriellen Slot. GUI ausschließlich privates
Xvfb/D-Bus; große Temps im Cache. Fremder Proxy und model-usage unangetastet.

## Gemeinsame Grenzen und Abnahme

Vorhandene `/api/v1`- und Loginoperationen verwenden; zusätzliche Felder nur
additiv. Konfigurationsrevisionen und Credentialgenerationen respektieren.
Nutzerdefinierte Namen und stabile Account-IDs erhalten. Reconnect an denselben
Account bindet Identität und Ressource, statt einen neuen Zugang anzulegen.
Eigene CLI-Speicher sind tatsächlich zu isolieren; ein Profilname allein und
ein Availabilityflag genügen nicht. Der HOME-Umgebungswert bleibt unverändert.
Offizielle CLI-Grants dürfen Keys erzeugen; dieser Schritt bleibt sichtbar.

NanoGPTs Abomonitor besitzt keinen Accountselector: keine erfundene Account-ID
abfragen. Die notwendige additive Monitorfeldänderung legt der Integrator nach
Abgleich des Herstellervertrags fest. Managementtoken niemals für Inferenz
verwenden, Inferenzkey nicht als Kontingentzugang ausgeben. Fehlendes Setup ist
neutrale Anleitung, kein fehlgeschlagener Providerabruf.

DeepSeek-Geldsaldo ist keine Prozentquote: native Währungen getrennt halten,
genaue Dezimalwerte bewahren, kein künstliches Limit/Verbrauch/Reset. NanoGPT
verwendet tatsächliche Rest-/Limit-/Resetfelder mit dokumentierter Semantik;
fehlende oder degradierte Werte bleiben unbekannt. Fireworks-Ressourcenquotas,
genehmigte Obergrenzen und Geldsaldo bleiben unterschiedliche Messgrößen.

Pro Provider synthetische Vertrags-/Fehler-/Identitäts-/Credentialtests; danach
gezielte gemeinsame CLI-/Desktopprüfung und frisches vollständiges Paket.
Installation und tatsächliche Benutzerlogins getrennt dokumentieren. Lokale
Lieferung ist vom Nutzerauftrag umfasst; kein erneuter künstlicher
Nutzerfreigabeschritt. Echte Logins benötigen seine sichtbare Anbieterzustimmung
oder private Eingabe. Normale Codexkonfiguration und bisheriges Profil bleiben
erhalten; keine Profilanwendung allein zur Bereinigung einer Statusanzeige.
