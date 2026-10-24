# AC-05 – Offizielle CLI-Verbindungen einzeln qualifizieren

Stand: 24.10.2026; Plan auf `5dc916f`, keine Implementierung oder Livequalifikation.
Grundlage: [Redesign](../account-connect-redesign-2026-10-24.md), Abschnitt „Gemeinsamer Anschluss im Host“.
Die folgenden Freigaben sind spätere Qualifikationsergebnisse, keine jetzt erteilte Ausführungsfreigabe.

## Ergebnis und Grenzen

Fünf unabhängig schaltbare Methoden: Requesty, Alibaba Model Studio, Command Code,
Fireworks Serverless und Mistral Vibe/gegebenenfalls Studio. Keine gemeinsame Freigabe „CLI unterstützt“.
Jede Methode liefert bestätigte Identitäts-/Ressourcenbindung, private Credentialreferenzen,
getrennte Fähigkeitszustände und einen konkreten Reconnectweg an den gemeinsamen Connect-Lifecycle.
Ein Keybootstrap ist eine sichtbare schreibende Autorisierung; ein Monitorstart erzeugt keinen vermeidbaren Key.
Keine Modellroute, Proxyaktivierung, Profilanwendung, Testinferenz, Käufe oder neue Serviceaccounts.
Bestehende Keys bleiben nutzbar; fehlende CLI oder offene Qualifikation führt zum benannten Keyfallback.
Dieser Plan führt weder Installer, CLI/Login, Build noch Providerrequests aus.

## Bestand, Abhängigkeiten und Zuständigkeit

`Accounts/AccountService.cs` enthält `IQualifiedLoginAdapter` und `LoginOperationService`:
heute nur Provider als Startkontext, flüchtige Operationen und keine Capability-Teilergebnisse.
`CodexOwnedLoginAdapter` zeigt eine begrenzte Owned-Dateiübernahme, ist kein allgemeiner CLI-Cacheimport.
`Operations/OwnStatePaths.cs`/`SecretStore` liefern private Dateien; deren Codex-Refreshreferenz bleibt spezifisch.
`MonitorCredentialStore` kennt noch keine delegierte Sitzung; `CommandCodeQuotaAdapter` und
`FireworksQuotaAdapter` bestehen bereits. AC-03 liefert den Requesty-Reader; der Alibaba-Reader fehlt im Istcode.
AC-00 liefert Verträge/Hostwiring; AC-01 Accountbasis, Credentialresolver, Loginoperationen und standalone Monitor.
Nötig sind Methode, Produkt, Zweck, Zielaccount, erwarteter Scope, Revision, Credentialowner und
Capabilityresultat sowie stabile Accountbindung/atomare Übernahme; keine parallelen DTOs oder zweiter Wizard.
AC-02 konsumiert den Ablauf in UI/CLI, AC-09 schaltet qualifizierte Methoden, AC-10 nimmt unabhängig ab.

AC-05 besitzt künftig neue Dateien unter `src/Lain.Host/Accounts/CliConnections/`:
`OfficialCliProcess.cs`, `CliConnectionStateStore.cs`, `CliQualificationManifest.cs`,
`RequestyCliLoginAdapter.cs`, `AlibabaCliLoginAdapter.cs`, `AlibabaOwnedProfileReader.cs`,
`CommandCodeCliLoginAdapter.cs`, `FireworksCliLoginAdapter.cs`, `MistralVibeCliLoginAdapter.cs`.
Zusätzlich besitzt AC-05 `src/Lain.Host/Quota/AlibabaConsoleQuotaAdapter.cs` samt enger Produkt-/Regionsnormalisierung;
zugehörige Schema-/Scope-/Expiry-/Teilresultatfixtures und Prüfungen liegen im eigenen Testordner.
Eigene lokale Prüfungen: neue Dateien unter `tests/account-connect-cli/`; keine bestehenden Testprogramme ändern.
Gemeinsame Account-/Secret-/Lifecycle-Dateien gehören AC-01; DTOs, `Program.cs`, Builddateien und Gesamtbuild
AC-00, Katalogintegration AC-09. Adapter, die einen Blocker behalten, bleiben unregistriert.

## AC-05-L – Lokale, ohne Providerkonto ausführbare Arbeit nach Implementierungsauftrag

1. Mit AC-00/AC-01 Start-/Resultatvertrag festziehen: `methodId`, `product`, `purpose`,
   `targetAccountId`, erwartete Providerressource, `revision`, `operationId` und Abbruchtoken.
   Ausgabe: Operationstatus, bereinigte Browseraktion, belegte Ressource, Capabilityzustände,
   private Referenz und Provenienz; niemals Token, Cookie, PKCE-Verifier oder komplettes CLI-JSON.
2. Prozessrunner mit Argumentliste ohne Shell und ohne Secrets in Argumenten bauen; aufgelösten
   Executablepfad, erwartete Version und Herkunft gegen ein Manifest prüfen, unbekannte Version ablehnen.
   Manifest enthält Herstellerquelle, Release, Quellcommit, Artefakt-SHA256, Lizenz, Plattform,
   getestete Befehle, Ausgabeformat, Isolation, Owner und Datum. Kein stilles Update/Auto-Installieren.
3. Je Methode nachgewiesene Isolation konfigurieren, geerbte Provider-/Profil-/Credentialoverrides
   entfernen. Eigene Verzeichnisse 0700, Dateien 0600, keine Symlinks oder Pfadflucht zulassen.
   Ein Profilflag beweist weder isoliertes Login noch isolierte Keyringablage; keine pauschale HOME-Umleitung.
   Kein Lesen/Ändern normaler CLI-Konfiguration, kein globaler Logout, kein Import fremder Refresh-Tokens.
4. Stdout/Stderr getrennt, größenbegrenzt und ausschließlich privat behandeln; Standardlimit 1 MiB,
   engeres methodisches Schema und Fehlercodeausgabe. Keine Rohdaten in Log, Inspector, Exception,
   Telemetrie, Export oder UI. Browseraktionen nur aus qualifizierter Ausgabe, HTTPS-/Hostallowlist;
   Callback-Codes nie als Status weiterreichen. Secrets nur im privaten Übernahmeschritt lesen.
5. Operationsjournal enthält Zustand, Profilreferenz, Bindung und gegebenenfalls bekannte neue Key-ID,
   keine Rohsecrets. Phasen: vorbereitet → Browser → Ergebnis privat verfügbar → übernommen/teilweise.
   Pro Verbindung Prozess und Übernahme serialisieren; Revision prüfen und alten Zugang bis Commit erhalten.
6. Abbruch/Deadline beendet nur den eigenen Prozessbaum und Loopbacklistener, drainiert Pipes und wartet
   begrenzt auf Exit. Bei Hostcrash beim Neustart eigene Prozesse/Operationsreferenzen abgleichen;
   kein altes Resultat als neuen Login übernehmen. Unklare Provider-Keyerzeugung verlangt Wiederaufnahme
   oder konkreten Aufräumhinweis, niemals automatisches erneutes Create oder behaupteten Widerruf.
7. Mit künstlichem CLI-Prozess/Fixtures Normalfall, Exitfehler, Timeout, Pipeüberlauf, kaputtes JSON,
   verspätete Antwort, Replay, falschen Scope, parallelen Reconnect und Crash vor/nach Commit prüfen.
   Output: lokale Prüfevidenz ohne Credentials; noch keine Freigabe eines echten Herstellertools.

## AC-05-R – Requesty: unterstützten Helper bevorzugen

Beleg: [Requesty-Bericht](../account-connect-research/kilo-opencode-requesty-openrouter.md#requesty),
offizielle CLI auf `21809398e99d888565178d718186b637f3de4566`; `auth token --profile …` ist expliziter Integrationshelper.
Input: eigene eindeutige Profilkennung, Zweck und bei Reconnect erwartete Gruppe/Router-/Keyidentität.
Zielbefehle: `requesty login --profile <eigenes-profil>`, privat `requesty auth token --profile <eigenes-profil>`;
Reconnect gezielt `login --profile <eigenes-profil> --force`, nie zufällig aktives Profil.
Provider-Versuch R: nach separatem Auftrag feste Version, Defaultprofil unverändert, Profilkollision,
Gruppenwahl, Loopbackabbruch und genau Key-plus-Newline-Ausgabe nachweisen; höchstens ein bewusster Bootstrap.
CLI hält ihr Profil; der kurzlebige Managementbearer (`manage:group:r manage:apikey:w`) wird verworfen.
Lain hält den übernommenen API-Key samt Profil-/Versionsherkunft; kein OAuthrefresh wird nachgebaut.
Mit autorisiertem Self-Read tatsächliche Keyrechte/-Identität prüfen; Selfcap/-usage ist keine Orgbalance.
Orgmonitor braucht getrennten berechtigten Readkey; fehlende Orgreads beeinträchtigen den Self-/Inferenzteil nicht.
Freigabe R erst mit Isolation, Helpervertrag und Bindungsnachweis; ohne stabile Kontoidentität beim Reconnect
keine automatische Zusammenlegung. SSH ohne lokalen Rückkanal: vorhandenen Key übernehmen.

## AC-05-A – Alibaba: eigenes Configformat als gepinnte Abhängigkeit

Beleg: [Alibaba-Bericht](../account-connect-research/alibaba-nebius-inception.md),
CLI-Source `3c8031f4d994c30820eb4d145d9404ba4a0acd39` (Paketquelle 2.5.1; Binary noch ungeprüft).
Input: eigenes `BAILIAN_CONFIG_DIR`, explizites Profil, Site sowie vorhandene erwartete Region/Workspacebindung.
`bl auth login --console --console-site international` mit qualifiziertem Profilargument delegieren.
Output: ausschließlich neu erzeugter Profilblock in eigener `config.json`; `config show` maskiert Keys.
Das ist schwächer als Requestys Exportvertrag: Dateischema, Profilaktivierung und Secretfelder müssen gepinnt werden.
Versuch A: separat qualifizieren, dass eigener Login Defaultconfig unberührt lässt, keine geerbten AK/Key-/Host-
Overrides wirken und Callback/API-Key/Console-Token dieselbe Site/Region/Workspacebindung zulassen.
Lain übernimmt gewöhnlichen Inferenzkey privat; CLI-Connector besitzt Console-Sitzung und allowlistete rohe Reads.
Nur Beijing/Singapore-Console freigeben; unbekannte Region ablehnen, keinen Beijing-Fallback übernehmen.
Ohne AK/SK/STS keine stille Erneuerung: „Nutzungszugang erneuern“ meldet im selben eigenen Profil erneut an.
Consoleablauf lässt gültigen Inferenzkey bestehen; fehlende Monitoringrechte bleiben Capability-Teilzustand.
QuotaRead: Der neue Adapter ruft über den CLI-owner ausschließlich die rohe Console-Read-Allowlist aus dem
[Quellreport](../quota-auth-research/alibaba-nebius-inception.md#konkrete-quellen-und-datenvertrag) ab:
`queryFreeTierQuota`, `queryFreeTierOnlyStatus`, `getModelUsageStatistic`/`listModelUsageStatisticData` mit
gebundenem Workspace, `Online` und explizitem Epoch-ms-Intervall; kein frei parametrisierbarer Consolecaller.
Consolecredential verbleibt beim CLI-owner; Reader erhält private Delegationsreferenz und normalisiert Rohresultate.
Abnahme QuotaRead: korrekte Envelopes, Modell/Snapshot, Grantstatus, `quotaTotal` als Rest, `quotaInitTotal` als
Ursprungsmenge, Epoch-ms-Ablauf, echte Usageunits und Intervall erhalten; Trial nur im belegten International/SG-Scope.
Falsche Site/Region/Workspace verweigern; abgelaufene Grants nicht verfügbar, Ablauf kein periodischer Reset.
Fixturetests für Schemafehler, leere Resultate, Sitzungsexpiry und einen fehlschlagenden Teilread: kein Nullrest,
erfolgreiche Teilwerte/letzte Werte samt Alter erhalten, `DataAsOf` ohne Providerzeit unbekannt; reale Reads separat qualifizieren.
Coding-Plan-Reader ist ausdrücklich später: eigene bedingte AC-05-Qualifikation und usage-only Instanzbindung,
bevor drei Callfenster gelesen werden; nicht Teil des ersten ordinary-Readers. Kein Abo-Key, Kauf oder BSS-/Cloud-AK-Zwang.

## AC-05-C – Command Code: Cacheübernahme erst nach Isolationstest

Beleg: [Referenzbericht](../account-connect-research/reference-codex-commandcode.md#command-code-offizieller-browserbootstrap-liefert-einen-key).
`cmd login`/`command-code login` liefert offiziell einen Key; Version, Export-/Ergebnisschema und Speicherisolation offen.
Input: Zielaccount und erwartete Org; Output nach Qualifikation nur frisch erzeugter Command-Code-Key, Herkunft und Org.
Versuch C beginnt mit öffentlicher Source-/Helpprüfung einer festen Version auf unterstützten eigenen Speicher.
Erst bei nachgewiesener Isolation einen gesondert autorisierten Login testen; andere Providercacheeinträge als Köder
verwenden und deren Unberührtheit nachweisen. Ohne Isolation/gezielte Übernahme keine CLI-Freigabe C.
Lain besitzt übernommenen Key; CLI behält eigenen Cache. Kein Refresh oder globaler Logout; Studio-Keyfallback bleibt.
Vorhandenen Reader `alpha/whoami?limits=1` für belegte Org nutzen; optionale Billingfehler partiell behandeln.
Go-/Planlogin belegt keinen Provider-API-Zugang; Modelle-/Inferenzberechtigung separat kennzeichnen, keinen Kauf starten.

## AC-05-F – Fireworks: Signin-Isolation und Standardkey getrennt beweisen

Beleg: [Fireworks-Bericht](../account-connect-research/mimo-nvidia-fireworks.md#fireworks-offizielle-cli-als-delegat-ohne-globalen-profilwechsel).
Input: erwarteter Account/Principal, optional vom Anbieter benötigte Enterprise-SSO-Kennung; keine allgemeine Orgpflicht.
Versuch F1: feste `firectl`-Version und nachweislich isoliertes `signin` qualifizieren; nachgelagertes `--profile`
allein genügt nicht. Ohne Beleg weder `~/.fireworks/auth.ini` ändern noch globales signin/signout ausführen.
Versuch F2 erst nach F1 und separater Keycreate-Autorisierung: `account list -o json`, Auswahl nur bei Mehrdeutigkeit,
höchstens ein `api-key create --key-name lain-… -o json` mit explizitem Account/Profil; kein `--all-users`.
Output privat: `key`, `keyId`, Account, Principal, Ablauf und bestätigter Standard-Serverlessprodukttyp.
JSON-Geheimwert und Nicht-Fire-Pass-Typ müssen belegt sein; API-Beispiel `isFirepass:true` nicht übernehmen.
Bestehenden passenden Key zuerst verwenden; Reconnect erzeugt ohne erklärten Grund keinen weiteren Key.
CLI besitzt SSO-Sitzung, Lain den übernommenen Key; SSOablauf invalidiert ihn nicht automatisch.
Quotarechte separat prüfen, keine Admineskalation; Kostenreports/Prepaidbalance sind keine Folge von Quotaerfolg.
Bei Create vor Crash bekannte Key-ID zur Wiederaufnahme behalten; nur eigene bekannte Keys gezielt aufräumen.

## AC-05-M – Mistral: Vibe nicht als universellen Studiozugang ausgeben

Beleg: [Mistral-Bericht](../account-connect-research/groq-mistral-google-cloudflare.md#mistral-drei-verschiedene-verbindungen),
Vibe-Source `63d0e8f17e2c1f0d9e1d6f4399ab2dc10fbdf477`; Browser-PKCE/Polling liefert API-Key, keinen OAuthrefresh.
Input: konkretes Vibe-/Studio-Produkt und erwarteter Workspace; Output nur dessen belegter Key und Scope.
Versuch M1: unterstützte CLI-Isolation und gezielte Ergebnisübergabe belegen; Keyring/`.env` können global wirken.
Kein stilles Keyring-/`.env`-Lesen; ein eigener Configpfad beweist allein keine isolierte Credentialpersistenz.
Versuch M2: nach eigener Autorisierung maximal ein Bootstrap, Workspace-Keyerstellungsrecht, Abbruch/Pollablauf,
externe Wiederverwendung und genaue Produkt-/Routeberechtigung klären. Modelllistenread beweist keine Inferenz.
Ohne diese Belege bleibt CLI-Methode gesperrt; vorhandener Studio-Key ist der sofortige Fallback.
CLI besitzt eigene Einrichtungssitzung, Lain nur gezielt übernommenen API-Key; bei Ablauf/Revoke neu verbinden.
Vibe-Whoami ist Identität/Plan, keine Restallowance. Enterprise-Adminmonitor bleibt eigener Key/Reader außerhalb AC-05.

## Gemeinsame Abnahme und Übergabe

| Fall | Prüffähiges Ergebnis je freigegebener Methode |
| --- | --- |
| Normal | Richtiger Account/Scope, private Übernahme, getrennte Fähigkeiten; keine Route/Inferenz und Defaultconfig unverändert. |
| Fehlende CLI/andere Version | Vor Login konkrete Installationshilfe/Keyfallback; kein Auto-Installer und keine teilweise Accountanlage. |
| Falsches Profil/Browserkonto | Ergebnis passt nicht zur erwarteten Bindung: kein Ersetzen, keine History-/Budgetverschiebung. |
| Abbruch/Crash | Eigener Prozess beendet/reconciled; alte Credentials nutzbar, keine doppelte Keyerstellung, Restzustand erklärt. |
| Same-account reconnect | Stabile Account-ID, Name/History/Budgetbindungen erhalten; nur passende Credentialgeneration atomar ersetzt. |
| Teilrechte/Sessionablauf | Funktionsfähige Capability erhalten, fehlende gezielt erneuerbar; kein Nullsaldo, kein stiller Scopewechsel. |

Je Anbieter getrennt liefern: Manifest, lokale Prüfevidenz, redigierter Qualifikationsbericht mit genehmigten
Aktionen/Reads, Schemas und Ergebnissen sowie Freigabe oder benanntem Blocker. Spätere Evidenzablage:
`docs/account-connect-qualification/cli/{requesty,alibaba,commandcode,fireworks,mistral}.md`.
Nicht ausgeführte Liveversuche und ungeklärte Inferenzrechte bleiben ausdrücklich offen; ein Anbieterblocker
hält die anderen Freigaben nicht auf. Integration/Registrierung erfolgt erst pro belegter Methode beim Integrator.
