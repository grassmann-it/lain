# AC-08 – Consoleverbindungen und Cohere-Pairing

Stand 24.10.2026, `5dc916f`. **Nur Arbeitspaketplanung.** Grundlage:
[Redesign](../account-connect-redesign-2026-10-24.md),
[MiMo](../account-connect-research/mimo-nvidia-fireworks.md) und
[SambaNova/Upstage/Cohere](../account-connect-research/samba-upstage-cohere.md).
Eigene Sessions bleiben Architekturvorschläge; belegte öffentliche Verträge gezielt schließen.

## Ergebnis, Reihenfolge und Zuständigkeit

AC-08a liefert zuerst einen durchgängigen MiMo-Erkenntnisversuch:
bewusst eingerichtete eigene Session → richtige UID/Region/Produktbindung →
echter lesender Usageabruf → Accountanzeige ohne Core, Modellroute oder Keypflicht.
Ein Gatewaykey ist optional. Erst der Versuch bestimmt Übertragbarkeit auf AC-08b/c;
AC-08d prüft Cohere-Pairing unabhängig von einer möglichen Consolebrücke.
Ein gescheiterter Sessionansatz blockiert weder vorhandene Keywege noch passive Reads.
AC-00 besitzt gemeinsame Contracts/`Program.cs`/Migrationen, AC-01 Account-/Credential-/
Operationbasis samt Monitor, AC-02 UX und AC-10 Abnahme. AC-08 liefert Methodenvertrag,
providerlokale Adapter/Reader und Evidenz; AC-03 stimmt Normalisierung/Readergrenzen ab.

## Vor AC-08a zu entscheidender Linux-Browserweg

Aktuell öffnet `src/Lain.Desktop/MainWindow.Login.cs` über `Launcher.LaunchUriAsync`
den Systembrowser; `Lain.Desktop.csproj` enthält Avalonia 12.1.4, keine WebView.
`src/Lain.Host/Accounts/AccountService.cs` besitzt Loginoperationen, keine Consolesession;
`Accounts/Monitoring/MonitorAccessService.cs` fordert einen bestehenden API-Keyaccount.
`HostQuotaSource` in `src/Lain.Host/Program.cs` projiziert bisher Inferenzcredentials.
Vor einer Implementierung ist eine kurze Entscheidung mit Probeausgang erforderlich:

- Kandidat A: separater lokaler Browserhelper mit eigenem Profil-/Sessionkontext;
  Linuxruntime, Distribution, IdP-/MFA-Kompatibilität und Sessionpersistenz nachweisen.
- Kandidat B: bewusst eingerichtete Browserbrücke, etwa eigene Erweiterung mit enger
  lokaler Übergabe; Installationsaufwand, Hostauthentisierung und Origins nachweisen.
- Eine eingebettete Avalonia-WebView ist nur ein zu prüfender Kandidat, keine vorhandene
  Fähigkeit; kein Browserpaket oder Linux-WebKit/CEF-Unterbau wird hier vorgegeben.
- Reiner Systembrowserstart reicht nicht zur Sessionrückgabe. Lehnt der Provider einen
  Kandidaten ab, wird kein Cookieexport, Profilscan oder Kontrollbypass ergänzt.

Die Variante hält Cookies/Sessionmaterial im qualifizierten Browserkontext.
Eine private Brücke bietet benannte Provideroperationen mit festen Origins,
Pfaden, Parametern und Antwortschemas; keine beliebigen Fetch-/JS-/Cookieoperationen.
AC-01 verwaltet Connectabsicht, Zielaccount/Revision und private API-Keyablage;
Der gewählte Browserkontext besitzt Session/Erneuerung. AC-08 liefert Lifetime-/Brückenvertrag
an AC-01, keinen zweiten Credentialstore; Pairingowner und Refreshbesitz bleiben eindeutig.
Vorgeschlagener neuer Ordner: `src/Lain.Host/Accounts/ConsoleConnections/` mit
providerlokalen Unterordnern; Browserhelperort/-projekt erst nach Variantenentscheidung.
Brückenvertrag/private Authentisierung gehen an AC-01, gemeinsame DTOs an AC-00.

## Gemeinsame Abnahmegrenzen für alle Unterpakete

Methodenqualifikation belegt Verträge/zulässige Drittclientidentität, Browserübergabe,
Read-/Mutationsliste, Schemas und lokale Fehlerfälle.
Sie beweist weder Rechte noch Plan, Rollenabdeckung oder Daten eines echten Accounts.
Livequalifikation ist später ein ausdrücklich begrenzter Versuch für benannte Accounts,
Produkte/Regionen/Rollen: zuerst Identität und Reads, Keyreveal/-create getrennt davon.
Keine dieser Aktionen ist durch diesen Plan beauftragt; keine Testinferenz erforderlich.
Ein Resultat nennt Methode/Version und separat die tatsächlich live geprüften Scopes.
Vor Reads/Übernahme wird bekannte UID/Org/Region/Produktbindung geprüft; beim Reconnect
bleibt sie fest. Wrong account, unbekannte Identität oder neue Revision stoppt die
Übernahme, erhält vorhandene Daten und verlangt eine ausdrückliche neue Zuordnung.
Provider-CSRF-/SameSite-/Originregeln müssen im Originalkontext erfüllt bleiben;
lokale Operation-ID/Nonce, Brückenauthentisierung und Replayabwehr ersetzen sie nicht.
Abbruch/Disconnect invalidiert Rückwege; verspätete Antworten dürfen nichts neu anbinden.
Timeout, Ablauf, Neustart, Betrieb bei geschlossener UI und Helper-/Pollingfreigabe klären.
Monitor-only bekommt eine Read-Whitelist: kein Create, Reveal, Rotate, Join, Upgrade,
Defaultkey-Helper oder Ersatzkey, auch nicht nach 401/403. Keine Dummy-Inferenzcredential.
Teilerfolg bleibt nutzbar: Sessionablauf verlangt nur Usage-Reconnect, Keyfehler nur
Inferenzreparatur; keine stillen Keyrotationen, Accountduplikate oder Nullwerte bei Fehlern.
Providercreate und lokale Speicherung sind nicht atomar: vor Request Phase journalisieren,
bei Antwort bekannte Remote-Key-ID und Secret privat sichern, dann Account zuordnen.
Nach Timeout nicht blind neu erzeugen; verlorenes einmaliges Secret ist nicht aus ID
rekonstruierbar. Zustand „Erstellung ungewiss“/„Key verwaist“ erhalten; Bereinigung nur
gezielt über belegten Vertrag. Lokales Entfernen behauptet keinen Providerwiderruf.
Lokale Prüfungen nutzen Fixtures/Fakebrücke, Linux-GUI später nur privates Xvfb;
Secrets, Pairingsecret, Cookies und Keyantworten erscheinen nie in DTOs/Logs/Evidenz.

## AC-08a – MiMo als erster vollständiger Erkenntnisversuch

**Lieferung:** eigener bewusst gestarteter Login bis UID/Region/Produkt und Usage,
zuerst Individual Token Plan; PAYG und Team als ausdrücklich getrennte Nachweisscopes.
Vorschläge: `ConsoleConnections/MiMo/MiMoConsoleAdapter.cs`,
`MiMoReadOperations.cs`, optional `MiMoKeyBootstrap.cs` im selben Ordner;
`src/Lain.Host/Quota/MiMoQuotaAdapter.cs`; Evidenz unter
`tests/console-connections/fixtures/mimo/` und `docs/account-connect-qualification/mimo.md`.
Readvertrag: `/api/v1/tokenPlan/detail`, `/tokenPlan/usage`, PAYG `/balance`;
Team zusätzlich `/projects` und gebundene `/project/{id}/teamTokenPlan/...`-Reads.
Individual `usage.items[]` und `percent` 0…1, Teampercent 0…100 getrennt normalisieren;
Monats-/Jahrespool, Seatreset und Geldguthaben niemals als gleiche Quote darstellen.
UID muss aus belegter Consoleidentität kommen; ein Cookie-/E-Mailwert allein genügt
nicht als Nachweis. Erkennungsweg und Regionsbindung sind Teil des Erkenntnisversuchs.
Optionaler Gatewayteil nutzt nur nach eigener Authorizerqualifikation verschlüsseltes
`{sk,uid,url}` als API-Key; keine OAuthrefreshfiktion oder MiMo-Code-Impersonation.
Vorhandenen passenden lain-Key erhalten; Monitor-only besucht keinen Keyauthorizer.
CLI-Fallback mit eigenem `MIMOCODE_HOME` liefert Inferenz, keine Monitorsession.
**Abnahme:** Nutzersignin → gebundener Account → echte passende Usage ohne Key/Core;
Wrong-UID/-Region, Ablauf und Abbruch erhalten Account-ID/History und letzten Datenstand.
Optionaler Keyfehler erhält verifizierte Usage; Team-Keybootstrap ist kein Individualbeweis.
**Stop:** kein zulässiger funktionsfähiger Sessionweg oder keine belastbare Identitäts-/
Regionsbindung; dann Erkenntnisbericht und Portal-/Keyfallback, kein erfundener Loginbutton.

## AC-08b – SambaNova Cloud Free/Developer

Eigene Methodenqualifikation nach MiMo, keine automatische Sessionübertragbarkeit.
Vorschläge: `ConsoleConnections/SambaNova/SambaNovaConsoleAdapter.cs`,
`SambaNovaReadOperations.cs`, `SambaNovaKeyBootstrap.cs` sowie
`Quota/SambaNovaQuotaAdapter.cs`; eigene Fixtures und Qualifikationsdatei wie bei MiMo.
Bindung über `/api/auth:me` an User; keine unbelegte Cloud-Orgwahl oder SambaStudio-
Vermischung. Usage `/api/usage?user_name=…&start_date=…&end_date=…`,
Credits `/resources/credits/balance` mit `amount/readAt`; Währung/Verzug qualifizieren.
Modellabsicht darf genau einen benannten Key über `POST /api/keys` erzeugen;
`apiKey` und `apiKeyInfo.name` privat übernehmen. 25-Keylimit/einmalige Sichtbarkeit
und Wiederaufnahme berücksichtigen; kein Create bei vorhandener passender Credential.
**Abnahme:** richtiger User, Free/Developer unverändert, Credits/Zeitraum belegt;
Read-only führt keine Keyoperation aus, abgebrochener Create wird nicht wiederholt.
**Fallback/Stop:** ohne Session-/CSRF-/Drittclientnachweis nur Key + Portal;
passive RPM/RPD-Header nach normalen Requests separat nutzbar, keine Creditsimulation.

## AC-08c – Upstage direkte Solar API

Vorschläge: `ConsoleConnections/Upstage/UpstageConsoleAdapter.cs`,
`UpstageReadOperations.cs`, `UpstageKeyBootstrap.cs`, `Quota/UpstageQuotaAdapter.cs`;
eigene Fixtures/Evidenz. BFF `https://console.upstage.ai/api/console/v3`,
Session und `x-console-request: 1` im qualifizierten Kontext, keine Headerkopie als Auth.
Bestehende User-/Org-/Rollenbindung entdecken; Auswahl nur bei mehreren Mitgliedschaften.
Spend, Promotions, Usage-/Rate-Limit und paginierte Usage getrennt lesen; Member sehen
gegebenenfalls nur eigenen Keyverbrauch bei Org-Saldo, Owner mehr: Abdeckung benennen.
Modellabsicht: vorhandenen passenden lain-Key nutzen oder genau einmal
`POST /organizations/{orgId}/api-keys` `{name,note}`; `key`/`api_key.key_id` sichern.
Kein automatisches Reveal eines fremden/neuesten Keys; Monitor-only niemals Reveal.
**Abnahme:** richtige Org, unterschiedliche Rollensicht, UTC-/Zeitbereichssemantik,
Teilspeicherung/Wrong-Org und keine Änderung an Zahlungsdaten, Budgets oder Recharge.
**Fallback/Stop:** ohne BFF-Session-/CSRF-/Clientnachweis vorhandener Key + Portal/CSV;
CSV ist historisch, kein Echtzeitsaldo; Creditende nicht zu einer Freequote umdeuten.

## AC-08d – Cohere Pairing, Evaluation und Production

Zuerst eigener zulässiger Pairingclient: `CLIAuthBegin → browserURL,secret`,
`CLIAuthConfirm`, `CLIAuthCheck → token`; kein OAuth-Device-Grant behauptet.
Vorschläge: `ConsoleConnections/Cohere/CoherePairingAdapter.cs`,
`CohereReadOperations.cs`, `CohereKeyBootstrap.cs`, `Quota/CohereQuotaAdapter.cs`.
Pollinggrenzen/TTL, Tokenrechte, Refresh/Widerruf und erlaubte Orgidentität qualifizieren;
keine fremde CLI-Identität, `JoinVerifiedOrg` oder unbelegte Orgumschaltung übernehmen.
Nur belegte Read-RPCs für Usage/Billing freigeben, trotz POST-Transport;
`GetOrCreateDefaultAPIKey` ist ausgeschlossen. Pairingsecret bleibt operationsgebunden.
Evaluation: passenden vorhandenen Key nur bei Modellabsicht gezielt privat enthüllen;
TRIAL-Neuanlage ist kein Evaluationersatz. Production: vorhandenen Key bevorzugen,
sonst nur bei bestehender Freigabe bewusster `PRODUCTION`-Create mit `key.raw_key`.
**Abnahme:** Evaluation/Production, Rollenabdeckung und Command-A+-Sonderlimits getrennt;
Monitor-only weder Keyliste noch Reveal/Create; Ablehnung/Timeout hinterlässt keine Bindung.
**Fallback/Stop:** Pairingrechte/Lifecycle oder eigene Zulassung unbelegt → Key + Portal;
eigene Dashboard-Session wäre eine neue explizite Entscheidung, kein stiller Ausweichweg.
