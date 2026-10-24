# Account Connect: MiMo, NVIDIA Build und Fireworks

Stand: 2026-10-24, Bezugsstand `5dc916f`. **Architekturvorschlag, keine beschlossene Implementierung.** Aufbauend auf den fertigen Berichten `docs/quota-auth-research/mimo-nvidia.{md,json}` und dem zentralen `auth-review`. Neue öffentliche Gegenprüfung betrifft Fireworks’ CLI-Profil-, Account- und Schlüsselausgabe. Keine Anmeldung, Credentialdatei, Provider-API-Abfrage, Schlüsselerzeugung oder CLI-Ausführung wurde vorgenommen. Die alten Berichte bleiben unverändert.

## Empfehlung und belastbare Grenze

| Produkt | Bevorzugter Einstieg | Inferenzcredential | Usagecredential / Grenze |
| --- | --- | --- | --- |
| MiMo Individual Token Plan | Eigene, bewusst gestartete Console-Browsersitzung; darin Herstellerautorisierung und anschließende Usage-Reads, nach Qualifikation | Verschlüsselt zurückgegebener `tp-`-API-Key | Dieselbe Browsersitzung, separat gespeichert und erneuert; kein OAuth-Usage-Token |
| MiMo Team Token Plan | Eigene Consolesitzung für Projekt und Seat; Team-Key über belegten Herstellerweg | `ttp-`-API-Key; CLI-Teambootstrap noch unbelegt | Sitzung mit Projekt-/Seatbindung; Mitgliedschaft und Rollen entscheiden über sichtbare Werte |
| MiMo PAYG | Gemeinsame eigene Consolesitzung wie Individual, nach Qualifikation | `sk-`-API-Key | Sitzung für monetäres Guthaben, getrennt von Token-Plan-Credits |
| NVIDIA Build | Offizielle Schlüsselseite öffnen, passenden Key einmal einfügen | Build-Key mit Public-API-Endpoints-Berechtigung | Keine belegte eigene Usage-/Remaining-/Resetquelle; keine OAuth-Gesamtzusage |
| Fireworks Serverless | Offizielle `firectl`-Anmeldung in nachgewiesen isoliertem Profil; Account entdecken, benannten Userkey erzeugen | Ein neuer User-API-Key, Ausgabe privat übernehmen | Derselbe Key zunächst für Accountquoten; fehlende Quotarechte separat anzeigen |

Ein erfolgreicher Login bedeutet weder erfolgreiche Inferenz noch lesbare Quoten. Beim Connect wird keine bezahlte Testinferenz ausgelöst. Der Abschluss zeigt unabhängig „Inferenzschlüssel verbunden“, „Nutzung verifiziert“, „Nutzung erneut anmelden“ oder „Nutzung nicht verfügbar“. Eine Quelle mit bloßen allgemeinen Limits wird als allgemeine Information geführt.

## MiMo: ein Login, zwei unabhängig lebende Zugänge

### Was der Hersteller tatsächlich liefert

MiMo Code bietet `mimo auth login` mit Xiaomi-Anmeldung und Wahl PAYG/Token Plan. Der öffentlich belegte Ablauf ist ein eigener API-Key-Bootstrap: `/authorize` erhält X25519-Public-Key, Loopback-Redirect und Keyname; der Callback enthält verschlüsseltes `{sk,uid,url}`. Der offizielle Client speichert danach einen API-Key mit UID und Basis-URL. Es gibt dabei keinen nachgewiesenen OAuth-Access-/Refresh-Token und keine Usage-Scopes. Die Bezeichnung OAuth im Plugin ändert diese Credentialart nicht. Die Providerseite kann dabei einen Key erzeugen und markiert ihn über `POST /api/v1/apiKeys/codeCli`; das ist eine Autorisierungsaktion, kein lesender Monitoringcheck. [M1–M4]

Die eigene Console verwendet eine Browser-Session für `/api/v1/balance`, `/tokenPlan/detail`, `/tokenPlan/usage` und Teamrouten. Ein über den normalen Browser gestarteter CLI-Login überträgt diese Cookies **nicht** an lain. Die Cookies haben keine belegte lesende Teilberechtigung; sie können breitere Kontobefugnisse tragen. Die veröffentlichte Cookiepolicy nennt 24 Stunden, was als Hinweis auf kurze Lebensdauer dient und keinen garantierten Refreshvertrag darstellt. [M5–M7]

### Bevorzugter kombinierter Ablauf, unter klarer Annahme

**Tragende Annahme:** Eine ausdrücklich für lain gestartete lokale, isolierte Browsersitzung kann die echte Provider-Anmeldung, die Herstellerautorisierung und danach erlaubte Console-Reads im selben Kontext durchführen. Zusätzlich muss geklärt werden, ob MiMo den eigenen Drittclient-Bootstrap unter eigenem Appnamen und eigener Keyidentität unterstützt. Die öffentliche Implementierung zeigt technische Machbarkeit, aber noch keinen dokumentierten Drittclientvertrag. Bis dahin ist dies ein konkreter Qualifikationskandidat, kein auslieferbarer OAuth-Connector.

1. Nutzer wählt MiMo und „Inferenz und Nutzung verbinden“; Produktwahl nur soweit nicht bereits aus dem vorhandenen Account bekannt. „Nur Nutzung“ überspringt die Keyautorisierung vollständig.
2. lain öffnet eine **eigene** MiMo-Anmeldesitzung. Die Oberfläche erklärt knapp, dass bei der Inferenzverbindung ein Key autorisiert/gegebenenfalls erstellt und für Usage eine Consolesitzung behalten wird. Anmeldung und Herstellerzustimmung erfolgen im Providerfenster. Keine Cookieübernahme aus vorhandenen Browserprofilen.
3. Nach der Keyrückgabe verbindet der Host nur bei passender UID, Produktart und erlaubter Regions-URL den Key mit der Sitzung. Die Sitzung liest die passende Usagequelle. Nur bei mehreren Projekten/Seats oder nicht eindeutiger Region folgt eine Auswahl.
4. Abschluss zeigt die unabhängig ermittelten Fähigkeiten. Eine Sitzung ohne aktiven Plan wird nicht zu einem Nullverbrauch oder zu einem vermeintlich funktionierenden Key umgedeutet.

Das kann für den Nutzer **eine Anmeldung** sein, bleibt technisch eine API-Key-Capability plus eine Session-Capability. Der Host hält die Session privat und führt nur die festgelegten Reads aus. Das Frontend erhält keine Cookies und keine Keyausgabe. Der eigene Client darf sich nicht still als `mimocode`, `MiMo Code` oder mit `X-Mimo-Source:mimocode-cli` ausgeben. Der öffentliche Authorizer liest unter anderem `app`, `pk`, `redirect_uri`, `key_name`; die CLI sendet zusätzlich `kn=mimocode`, das der untersuchte Authorizer nicht ausliest. Daraus folgt keine Freigabe für die Nutzung des MiMo-Code-Kanals. [M2, M7]

### Produkt- und Kontobindung

- **Individual:** `tp-`-Key, Regionen CN/SGP/AMS, genaue zurückgegebene und erlaubte Base-URL übernehmen. `/tokenPlan/usage` liefert aktuell `data.usage.items[]` mit `name`, `used`, `limit`, `percent` als Anteil 0…1. Alle Buckets berücksichtigen, einschließlich Kompensation. Monatlicher oder jährlicher Gesamt-Creditpool; `currentPeriodEnd` ist nicht pauschal ein monatlicher Refilltermin. [M6, M8–M10]
- **Team:** `ttp-`-Key, Projekt-ID und Seat-/Mitgliedsidentität zusätzlich zur UID. `/projects`, `/project/{projectId}/teamTokenPlan/detail`, `/usage` und `/my/seat` verwenden. Team-`usedPercent` ist 0…100; Seat enthält aktuelle und historische Credits sowie `nextResetTime`. Historische Seatwerte können geerbt sein. CLI/Authorizer-Unterstützung für **Teamkey-Ausstellung** ist noch nicht hinreichend belegt: keinen Individualkey still als Teamschlüssel verwenden. Ein belegter manueller Teamkeyweg plus dieselbe Sitzung ist eine sinnvolle erste Ausbaustufe. [M5, M6, M8, M10]
- **PAYG:** `sk-`-Key, dokumentierte/gelieferte PAYG-Base-URL, monetäres `/balance` mit Währung und Cash/Gift-Bestand. Region und Kontozuordnung getrennt prüfen; Plancredits und Guthaben niemals zusammenrechnen. Ein erschöpfter Plan wechselt nicht automatisch zu kostenpflichtigem PAYG. [M8, M9, M11, M12]

Bei Sessionablauf wird nur „Nutzung erneut anmelden“ verlangt; ein weiter gültiger Inferenzkey bleibt verbunden. Bei widerrufenem Key ist nur der Inferenzteil zu erneuern. Reauth mit anderer UID, Region, Projekt oder Seat wird als neue/anders gebundene Verbindung behandelt; Budgets und Historie werden nicht still umgehängt. Keine Refresh-Token-Logik erfinden.

### Hersteller-/Maintainerarbeit und sofortiger Fallback

Vor dem kombinierten Weg braucht es: belegte Drittclientnutzung des Authorizers unter eigener Identität; passende Redirect-/Antwortbindung, Key- und Hostvalidierung; qualifizierten eigenen Sessioncontainer samt Logout, Ablauf und privater Speicherung; Nachweis der UID-/Regionsbindung sowie ein gesonderter Teamtest. Console-Reads sind Frontendverträge, keine stabile öffentliche Monitoring-API. Diese Arbeit liegt beim Maintainer, nicht in zusätzlichen Nutzereingaben.

Fallback: offizielle MiMo-Code-Anmeldung mit dokumentiertem `MIMOCODE_HOME` in einem eigens für lain angelegten Verzeichnis; nur den bewusst gewählten privaten API-Key-Eintrag übernehmen. Ein bestehendes CLI-Profil wird nicht durchsucht oder überschrieben. Das liefert Inferenz, keine Consolesitzung. Usage bekommt bei Bedarf einen separat sichtbaren eigenen Console-Login. Für Team bleibt manuelle Keyeinfügung erhalten. Der untersuchte Client ruft den Systembrowser direkt auf; ein erfundener `BROWSER`-Override oder ein vermeintlicher Cookie-Callback ist kein Ersatz. [M1–M4]

## Fireworks: offizielle CLI als Delegat, ohne globalen Profilwechsel

Die aktuelle Integration verlangt API-Key und manuell eingetragenen Account-ID. Die offizielle CLI dokumentiert `firectl signin` für Google SSO und `firectl signin my-enterprise-account` für Custom SSO. Das ist eine gute Login-Delegation, kein belegter öffentlicher OAuth-Clientvertrag für lain. `account list -o json`, `api-key create --key-name … -o json` und `--account-id`/`--profile` sind dokumentiert. Die `signin`-Dokumentation belegt jedoch **nicht hinreichend**, in welches isolierte Profil sie schreibt. Ein Connector darf deshalb nicht einfach den globalen Login ersetzen. [F1–F5]

### Minimaler Zielablauf nach Profilqualifikation

1. „Mit Fireworks anmelden“ erklärt einmal: offizielle Fireworks-Anmeldung und ein benannter persönlicher Key für lain. Der Host startet ausschließlich einen nachweislich isolierten CLI-Profil-/Konfigurationskontext. Bei Custom SSO ist nur die vom Hersteller benötigte Enterprisekennung zusätzlich nötig.
2. CLI entdeckt zugängliche Accounts. Bei einem geeigneten Account keine weitere ID-Eingabe; bei mehreren Auswahl mit Namen und ID. Der Account ist eine eigene Ressource, nicht der Modellowner `fireworks`, die User-ID oder die E-Mail-Adresse.
3. Der Host erzeugt **einen** Userkey mit eindeutigem `lain-…`-Namen, explizitem Account und Profil; keine Serviceaccounts, kein `--all-users`, keine Rollenerhöhung. Ablaufdatum ist eine optionale bewusste Wahl; ein nicht gesetztes Datum kann „läuft nicht ab“ bedeuten. Der Host übernimmt ausschließlich begrenzte private JSON-Ausgabe, speichert Key, `keyId`, Ablaufdatum, Account und Principal und zeigt keine Schlüssel im Status oder Log.
4. Derselbe API-Key wird zuerst für `GET /v1/accounts/{account_id}/quotas` verwendet. Fehlende Rechte ergeben „Inferenzschlüssel verbunden; Nutzung benötigt Berechtigung“ und keine erfundene Nutzung von null. Eine separate bereits autorisierte Monitorcredential ist bei Bedarf ein optionaler Folgeschritt. Keine Inferenzprobe.

Die Create-API liefert `keyId`, `displayName`, **`key`**, `expireTime`, `email`, `prefix`, `isFirepass` und weitere Metadaten; der Geheimwert ist beim Erstellen verfügbar. Der CLI-Vertrag muss für die qualifizierte Version nachweisen, dass `-o json` genau diesen Geheimwert privat liefert und tatsächlich einen Standard-Serverlesskey erzeugt. Das API-Dokubeispiel setzt `isFirepass:true`; dieses Beispiel darf **nicht** als Serverlesspayload kopiert werden. Die CLI-Doku nennt keinen entsprechenden Auswahlflag. Fire Pass ist ein eigenes Produkt und nicht still die hier betrachtete Verbindung. [F3, F6]

Wenn ein Key schon vorhanden ist, **keinen neuen erzeugen**: Accountdiscovery per `GET /v1/accounts` kann die manuelle Account-ID ersetzen, soweit der Key das zulässt. Scheitert Discovery an Berechtigungen, bleibt die gezielte manuelle Accountwahl/-ID als Fallback. `whoami` hat in der geöffneten Referenz keinen dokumentierten JSON-Outputflag; eine solche Ausgabe wird nicht angenommen. [F4, F5, F7]

### Profil-, Rechte- und Lebenszyklusdetails

Vor Freigabe muss eine feste CLI-Version durch öffentliche Source/Help und später einen begrenzten isolierten Test zeigen, dass `signin` ein benanntes Profil oder einen offiziell unterstützten getrennten Konfigurationspfad verwendet, dessen Defaultprofil unangetastet bleibt. `--profile` auf nachgelagerten Kommandos allein beweist dies nicht. Weder `firectl set-api-key …` noch globales `signin`, globales `signout` oder Überschreiben von `~/.fireworks/auth.ini` gehört in den Connector. Geheimnisse gehören nicht in `--api-key`-Argumente. Solange diese Isolation offen ist, ist der klare manuelle Keyimport der auslieferbare Weg.

CLI-Sitzung und erzeugter Key leben getrennt. Eine abgelaufene SSO-Sitzung macht einen gültigen API-Key nicht automatisch unbrauchbar. Reconnect bindet wieder an den gewählten Account/Principal; eine andere Auswahl ersetzt nicht still dessen Historie. Wenn Keyerzeugung gelingt, Import aber scheitert oder abgebrochen wird, kann ein verwaister Key bestehen: bekannte `keyId` für genaue Wiederaufnahme/Aufräumhinweise speichern; erfolgreichen Widerruf niemals behaupten. Nur eigens erzeugte bekannte Keys später gezielt widerrufen.

Quoten benötigen nicht automatisch Adminrechte; die minimale Rolle ist öffentlich noch nicht hinreichend dokumentiert. Inferenzfunktion beweist Quotarecht nicht. `monthly-spend-usd` bedeutet verbleibenden Raum unter einem Monatsausgabenlimit, kein Prepaidguthaben. Andere Einheiten sind nur bei belegter Zuordnung darstellbar. `updateTime` ist kein belegter Resettermin. Kostenreports sind eine spätere eigene Capability: `usageCosts:query` mit `ACCOUNT` ist anders berechtigt als `SELF` und wird nicht Teil der Standardverbindung. Für „Nur Nutzung“ kann ein isoliert delegiertes `firectl quota list -o json` eine eigene Variante sein, wenn dessen Konto-/Rechte-/Ausgabevertrag qualifiziert ist; dabei keinen unnötigen Inferenzkey erzeugen. [F8, bestehender auth-review]

## NVIDIA Build: den verbleibenden manuellen Schritt ehrlich verkürzen

NVIDIA Build benutzt für die gehostete Inferenz `https://integrate.api.nvidia.com/v1` mit API-Key. Es braucht den Dienstzugang **Public API Endpoints**; eine NVIDIA-Anmeldung, irgendein NGC-Key, Registryrecht oder NVCF-Zugang beweist ihn nicht. `ngc config set` verlangt bereits einen Key und vereinfacht die Erstverbindung daher nicht. `$oauthtoken` als Registrybenutzer ist ebenfalls API-Key-Nutzung, kein OAuth-Anmeldeverfahren. [N1, N2, N7–N9]

Minimaler heute belegbarer Ablauf:

1. „NVIDIA Build verbinden“ öffnet die offizielle Build-Schlüsselseite mit knapper Dienstangabe.
2. Nutzer meldet sich bei NVIDIA an, erstellt/wählt einen Key mit passender Berechtigung und fügt ihn einmal in lain ein. Account-/Orgzuordnung nur zusätzlich wählen, wenn wirklich relevant und belastbar ermittelbar; keine unbekannte Organisation erraten.
3. Host speichert den Key privat. Er kann bekannte Format-/Metadaten prüfen, meldet aber ohne unabhängigen Nachweis keine erfolgreiche Inferenz oder aktive Dienstberechtigung. Usage bleibt „keine unterstützte Kontoquelle“; keine zusätzliche leere OAuth-Anmeldung.

Die öffentliche Buildseite zeigt einen sessiongestützten Keycreate-/list-Mechanismus für `/v3/orgs/{orgName}/keys/type/AI_PLAYGROUNDS_KEY`, mit Invoke-Policy und typischer sechsmonatiger Vorgabe. Diese technische Möglichkeit qualifiziert weder einen eigenen öffentlichen OAuth-/Deviceclient noch eine Usageberechtigung. Die Login-URL mit `app=api-catalog` ist Herstelleridentität und wird nicht kopiert. Ein künftiger eigener Console-Sessionconnector könnte Keykopieren sparen, falls Nutzungs- und Integrationsvertrag geklärt werden; er löst die Usage-Lücke bislang nicht. [N4–N6]

Die öffentliche Konfiguration nennt „up to 40 rpm“ und „10,000 requests per day“, mit Modell-/Trafficabhängigkeit. Das sind keine kontospezifischen Werte für Verbrauch, Rest oder Reset. NGC-Registry-, Enterprise-, NVCF- und selbstgehostete NIM-Metriken werden nicht ersatzweise an das Buildkonto gehängt. Bestehender ausdrücklich gewählter NGC-Profilimport kann später einen Kopierschritt sparen, braucht aber eigene Dienstqualifikation und ist keine Browseranmeldung. Reconnect ersetzt nur den passenden Key; Konto-/Orgwechsel wird ausdrücklich neu gebunden. [N3, N7–N9]

## Folgen für den Account-Connect-Vertrag

Der heutige `IQualifiedLoginAdapter.BeginAsync(operationId)`/`LoginOperationService.BeginAsync(provider)` braucht einen expliziten Kontext für Produkt, gewünschte Fähigkeiten, Zielkonto/-ressource, Revision und Ausgabebesitz. Der Ergebnistyp muss API-Key-Bootstrap, delegierte CLI-Sitzung, eigene Browsersitzung und manuelle Credentialeingabe unterscheiden. API-Key-Erwerb wird nicht als `owned-oauth` gespeichert.

`MonitorAccessService` setzt aktuell einen bestehenden API-Key-Account voraus. Für MiMo „Nur Nutzung“ und einen eigenständigen CLI-Usageweg braucht es eine Session-/Delegationsreferenz ohne Dummy-Inferenzkey, getrennte private Credentialablage und getrennte Capabilityzustände. Bestehende Revisionsprüfung, kein stiller Credentialfallback und hostseitige Geheimhaltung bleiben erhalten. Sessionablauf, Keyrotation und Accountwechsel sind unterschiedliche Ereignisse. Der Host führt Profil-, Credential- und Tenantqualifikation durch; Nutzereingaben werden nicht zum Ersatz für diese Maintainerarbeit.

## Quellen

Alle Abrufstände 2026-10-24. MiMo/NVIDIA wurden aus dem abgeschlossenen Forschungsbericht übernommen; Fireworks-Quellen wurden für diesen Folgeauftrag gezielt öffentlich gegengeprüft. Öffentliche Frontendassets sind versionsgebundene Beobachtungen, keine zugesicherte API. Keine authentifizierte Funktionsvalidierung.

- M1: https://mimo.mi.com/static/docs/tokenplan/integration/mimo-code.md
- M2: https://github.com/XiaomiMiMo/MiMo-Code/blob/e37f2c2d01460df19b31d67b8116d6c66fe33db8/packages/cli/src/plugin/mimo.ts
- M3: https://github.com/XiaomiMiMo/MiMo-Code/blob/e37f2c2d01460df19b31d67b8116d6c66fe33db8/packages/cli/src/provider/auth.ts
- M4: https://github.com/XiaomiMiMo/MiMo-Code/blob/e37f2c2d01460df19b31d67b8116d6c66fe33db8/packages/shared/src/global.ts (Credentialablage außerdem `packages/cli/src/auth/index.ts` am selben Commit)
- M5: https://platform.xiaomimimo.com/static/main.569e3e15e1d2bea2.chunk.js
- M6: https://platform.xiaomimimo.com/static/279.17edd5ab4b3a18cd.chunk.js
- M7: https://platform.xiaomimimo.com/static/690.a73865333011cc6e.chunk.js
- M8: https://mimo.mi.com/static/docs/tokenplan/Token%20Plan/quick-access.md
- M9: https://mimo.mi.com/static/docs/tokenplan/Token%20Plan/subscription.md
- M10: https://mimo.mi.com/static/docs/quick-start/faq/token-plan/Validity&Expiry.md
- M11: https://mimo.mi.com/static/docs/quick-start/faq/account.md
- M12: https://mimo.mi.com/static/docs/price/pay-as-you-go.md
- F1: https://docs.fireworks.ai/tools-sdks/firectl/commands/authentication
- F2: https://docs.fireworks.ai/tools-sdks/firectl/firectl
- F3: https://docs.fireworks.ai/tools-sdks/firectl/commands/api-key-create
- F4: https://docs.fireworks.ai/tools-sdks/firectl/commands/account-list
- F5: https://docs.fireworks.ai/tools-sdks/firectl/commands/whoami
- F6: https://docs.fireworks.ai/api-reference/create-api-key
- F7: https://docs.fireworks.ai/api-reference/list-accounts
- F8: bestehend `docs/providers/fireworks.md`, `docs/quota-auth-research/auth-review.{md,json}` für Quoten-/Kostenquellen; Quoten-API https://docs.fireworks.ai/api-reference/list-quotas
- N1: https://docs.api.nvidia.com/nim/docs/api-quickstart.md
- N2: https://docs.api.nvidia.com/nim/docs/product
- N3: https://build.nvidia.com/api/runtime/content/copy.yaml
- N4: https://build.nvidia.com/_next/static/chunks/1ezdm36-wvhki.js
- N5: https://build.nvidia.com/_next/static/chunks/2xv_0hdneb8vt.js
- N6: https://build.nvidia.com/_next/static/chunks/2a1ymhcs400-z.js
- N7: https://docs.nvidia.com/ngc/latest/ngc-catalog-user-guide.html
- N8: https://docs.nvidia.com/ngc/latest/ngc-user-guide.html
- N9: https://docs.ngc.nvidia.com/cli/cmd.html
