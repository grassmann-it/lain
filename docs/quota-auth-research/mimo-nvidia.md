# Xiaomi MiMo und NVIDIA Build: Auth und Quota

Recherche vom **24.10.2026**, Basis lain `main` **5dc916f**. Architektur-Sparring,
keine Implementierungsfreigabe. Ausschließlich öffentliche Dokumentation,
öffentlicher Herstellerquellcode und unangemeldet ausgelieferte Websiteassets
gelesen; keine Accountsitzung, Credentials, Anmeldung, Inferenz, Clientregistrierung
oder Änderung von Produktcode/Diensten. Angegebene API-Routen wurden aus diesen
Quellen gelesen, nicht mit einem echten Account aufgerufen.

## Ergebnis und empfohlener Anschluss

**MiMo hat inzwischen einen offiziellen Login ohne Keykopieren.** MiMo Code
öffnet die Plattform, lässt PAYG oder Token Plan auswählen und erhält verschlüsselt
einen Inferenzkey plus Nutzer-ID und Base URL. Das Plugin nennt dies OAuth;
technisch ist es ein proprietärer Browserflow zur API-Key-Provisionierung, ohne
OAuth-Access-/Refresh-Token oder standardisierte Scopes. Der aktuelle öffentliche
Plattformcode verarbeitet sogar einen eigenen Anzeigenamen `app`, aber daraus
folgt noch kein dokumentierter Drittclientvertrag für lain. Der unmittelbar
qualifizierbare Anschluss ist daher **offiziellen MiMo-Code-Login delegieren und
den ausdrücklich ausgewählten xiaomi-Zugang privat übernehmen**, zunächst als
Inferenzanschluss. Ein nativer lain-Flow ist eine gute nächste Option, sobald
Kompatibilität und erlaubte Drittclientnutzung dieses Verfahrens geklärt sind.

**MiMo-Quota ist ein gesonderter Browsermonitor.** Die offizielle Website verwendet
konkrete lesende Endpunkte für Individualplan, Teamplan und PAYG-Balance. CodexBar
nutzt einen Teil davon mit Plattformcookies. Somit ist „keine API möglich“ falsch;
„kein öffentlich dokumentierter Inferenzkey-Quota-Vertrag qualifiziert“ bleibt
richtig. Eine eigene, nur für diesen Zweck angemeldete Browseroberfläche wäre die
ehrlich benannte Alternative. Sie benötigt nicht das Kopieren sämtlicher Cookies
aus bestehenden Browserprofilen. Der Bootstrap allein liefert diese Sitzung nicht.

**NVIDIA Build:** Weblogin und API-Key-Erzeugung sind belegt, ein delegierbarer
Drittclient-OAuth-/Deviceflow und ein Build-Kontoquota-Read dagegen nicht.
Neu gefunden wurde NVIDIAs öffentliches Runtime-Dokument mit **bis zu 40 RPM**
und **10.000 Requests/Tag**; dies sind Angebotslimits, keine beobachteten Restwerte.
Beste nächste begrenzte Evidenz: mit separat autorisiertem Build-Account die
eigene Konsole auf vorhandene Usage-/Rate-Limit-Daten und deren Requestvertrag
prüfen oder NVIDIA nach genau diesem Read-/Drittclientvertrag fragen. NGC-/NVCF-
Enterprise-Metriken lösen das nicht automatisch.

## Xiaomi MiMo: Produkte und bestehendes lain

lain kennt direkte MiMo-Inferenzaccounts und lokale Requesthistorie; `QuotaService`
hat für `xiaomi-mimo` bislang nur Console-Anleitung, keinen direkten Reader
(`src/Lain.Host/Quota/QuotaService.cs:157`). Das ist ein Implementierungsstand,
keine endgültige Aussage über Anbieterfähigkeiten. Command-Code-MiMo bleibt ein
anderer Anbieterzugang/Pool.

| Produkt | Inferenzcredential und Routing | Monitoring / Scope |
| --- | --- | --- |
| Individual Token Plan | eigener `tp-...`-Key; `https://token-plan-{cn,sgp,ams}.xiaomimimo.com/v1` für OpenAI oder `/anthropic` für Messages; konkrete Account-Base-URL maßgeblich | gemeinsame gewichtete **Credits**, über alle erlaubten Tools/Modelle; aktueller Planzeitraum; Geldbalance ist kein Ersatz |
| Team Token Plan | `ttp-...`; zugewiesener Seat und Teamprojekt; Route/Key aus dem Account | Teamgesamtverbrauch und eigener Seat getrennt; Seat kann bereits Verbrauch eines früheren Mitglieds enthalten |
| PAYG | `sk-...`; gewöhnlicher Open-Platform-Key, API `https://api.xiaomimimo.com/v1`; Region/Accountzuordnung beachten | Kontogeld in zurückgelieferter Währung, Cash/Granted getrennt; keine Plancredits |

[Schnellanschluss][m-quick] und [FAQ][m-api] erklären ausdrücklich, dass PAYG- und
Token-Plan-Keys nicht austauschbar sind. Die [Account-FAQ][m-account] erklärt
separat berechnete China-/Overseas-Nutzung, regionale Keys/Basen, Xiaomi-Account-
Login und Registrierung. China: Handynummer bzw. angebotene Drittidentitäten,
Identitätsprüfung vor Kauf/Aufladung. Overseas: E-Mail bzw. Google/Facebook als
Xiaomi-Account-Anmeldung, andere Zahlungswege. Dies sind Kontologins und noch
keine delegierten Inferenz-/Monitoringrechte für lain.

### Offizieller CLI-Login ohne manuelles API-Key-Kopieren

[Herstelleranleitung][m-cli-doc] nennt `mimo auth login`, alternativ `/login` oder
`/connect`, Browserautorisierung oder Plattformcode. Sie belegt:

- Auswahl PAYG **oder** Token Plan. PAYG legt einen Key mit Namen
  `mimo-code-cli-key...` an; gültiger Plan bzw. ausreichendes Kontoguthaben ist
  Voraussetzung. Ein bloßer Xiaomi-Login reicht nicht.
- Der Code ist öffentlich und MIT-lizenziert in [XiaomiMiMo/MiMo-Code][m-repo],
  zusätzlich über die offizielle Doku und npm-Paketmetadaten `@mimo-ai/cli`
  zugeordnet. Gelesener Pin **e37f2c2d01460df19b31d67b8116d6c66fe33db8**,
  Commitdatum 22.10.2026. Lizenz des CLI-Codes ist kein eigener Providervertrag.
- [Pluginquelle][m-cli-src]: `GET https://platform.xiaomimimo.com/authorize`
  mit `pk` (Base64url X25519 SPKI DER), `redirect_uri=http://localhost:<random>/`,
  `kn=mimocode`, `key_name=mimo-code-cli-key-<random>`. Fünf Minuten Timeout.
  Manueller Fallback verwendet die Plattform als Code-Anzeige statt Loopback.
- Callbackquery `u` enthält Base64url: 32 Byte ephemeral public key, 12 Byte
  Nonce, Ciphertext und 16 Byte GCM-Tag; X25519 → SHA-256(shared secret) →
  AES-256-GCM. Entschlüsselte Payload **`{sk, uid, url}`**. Keine Tokenexchange-API,
  kein Device Authorization Grant, kein Refresh-Token und keine Scopeliste.
- [Speicheradapter][m-cli-auth] speichert den Erfolg als **`type: api`**, `key`,
  `metadata.uid`, `metadata.base_url`; nicht als OAuthcredential. [Pfadauflösung][m-cli-path]
  ist `${MIMOCODE_HOME}/data/auth.json` oder XDG-Datenbasis `mimocode/auth.json`
  (Linux üblich `~/.local/share/mimocode/auth.json`), Modus 0600. Dies wurde nur
  aus Quellcode gelesen; keine private Datei geöffnet.

Zusätzliche [offizielle Plattformquelle][m-auth-asset] bestätigt das Verhalten:
liest `app` (Default „MiMo Code“), `pk`, `redirect_uri`, `key_name`; `kn` wird dort
nicht verarbeitet. Sie liest/erstellt PAYG-Key bzw. Token-Plan-Key, erstellt die
gleiche verschlüsselte Payload und ruft beim Loopback `/auth?u=...` auf. Der
CLI-Listener verarbeitet alle Pfade; `/` in seiner Redirect-URI ist deshalb kein
Widerspruch. Das UI nennt Keygenerierung, Accountinformation und Datei-Upload als
Berechtigungstexte, bietet **keine OAuthscope-Verhandlung**. Eine separate
`POST /api/v1/apiKeys/codeCli`-Markierung findet während der Autorisierung statt.
Diese Mutation darf ein späterer Browserflow nicht unbemerkt als bloßen Read
bezeichnen.

**Fähigkeitstrennung:** Dieselbe Xiaomi-Websitzung kann die Konsole ansehen und
einen Inferenzkey autorisieren. Der CLI-Callback überträgt diese Sitzung jedoch
nicht an lain. Der CLI-Login liefert tatsächlich Inferenzcredential und
Regionbindung. Er liefert keine Plattformcookies und keinen Nachweis, dass dieser
Key Balance-/Plan-Reads darf. `uid` ist Bindungsinformation, kein Quotarecht.
Bestehenden CLI-Zugang später nur mit expliziter Accountauswahl privat einlesen;
keine Logs/CLI-Textausgaben als Secrettransport. Der vorliegende Auftrag testet
keinen Export und keinen Import. Ein lain-nativer Flow darf nicht durch
`X-Mimo-Source: mimocode-cli` oder `kn=mimocode` eine fremde Werkzeugidentität
behaupten. Ob ein eigener `app=lain`-Flow offiziell unterstützt/zulässig ist,
bleibt ein enger, konkreter Klärpunkt; technische Clientregistrierung wird im
gelesenen Frontend weder verlangt noch nachgewiesen.

### Konkrete Browsermonitor-Endpunkte

Die aktuelle öffentliche [Plattform-Hauptdatei][m-main-asset] definiert die
Read-Routen; Requests gehen mit same-origin Browsercredentials an
`https://platform.xiaomimimo.com/api/v1`. Die Anwendung setzt `x-timeZone` auf die
IANA-Zeitzone und `Accept-Language`. Kein Bearer-Key-Readvertrag ist damit belegt.

| GET-Pfad nach `/api/v1` | aus Quellen ablesbare Felder und Bedeutung |
| --- | --- |
| `/balance` | CodexBar: `code`, `data.balance`, `currency`, optional `cashBalance`, `giftBalance`, Geldbeträge als Strings. Offizielles UI zeigt außerdem eingefrorenen Betrag, Giftablauf und Credit-/Overdraft-Kontext; vollständiges Response-Schema live offen. |
| `/tokenPlan/detail` | `planCode`, `planName`, `expired`, `currentPeriodEnd`; aktuelle Website zusätzlich Auto-Renewal-/Clawflags; Ende ist UTC-Gültigkeitsende, nicht automatisch garantierter Refill. |
| `/tokenPlan/usage` | **aktuelle Herstellerwebsite: `data.usage.items[]` mit `name`, `used`, `limit`, `percent`**; `percent` ist Verhältnis 0..1 und wird für Anzeige ×100 gerechnet. Alle Items berücksichtigen; `compensation_total_token` ist ein separat behandelter Kompensationspool. |
| `/projects` | Projekt-/Rolleninventar für die explizite Auswahl des Teamkontexts. Keine automatische Addition aller Projekte. |
| `/project/{projectId}/teamTokenPlan/detail` | Teamplan/Settlement, `expired`, `currentPeriodEnd`, Seatanzahl/-limit etc.; Projekt-/Mitgliedsrechte maßgeblich. |
| `/project/{projectId}/teamTokenPlan/usage` | aktuelles offizielles UI: `usedPercent` bereits als 0..100, `nextResetTime`; Teamverbrauch, nicht automatisch eigener Memberverbrauch. |
| `/project/{projectId}/teamTokenPlan/my/seat` | `seat` mit `creditsUsed`, `creditsTotal`, `usedPercent`, `historyCreditsUsed`, `historyUsedPercent`, `nextResetTime`, `seatStatus`; aktuelle und historische Seatnutzung auseinanderhalten. |

[Detailansicht-Bundle][m-plan-asset] belegt Individual-/Teamfelder und
Prozentkonventionen. **Schemaabweichung gegenüber CodexBar:** dessen [Fetcher][cb-fetch]
liest noch `data.monthUsage.items.first`, setzt fehlgeschlagene Planreads auf
Nullwerte und wertet nur ein Item aus. Das ist kein geeigneter unveränderter
lain-Port. Das heutige Herstellerbundle und eine spätere bereinigte reale Antwort
müssen Vorrang erhalten. Fehlend/abgewiesen darf nicht zu „0 verbraucht“ werden;
Balance-Fehler dürfen einen funktionierenden Planread nicht vollständig verdecken.

[CodexBar-Doku][cb-doc] und [Cookieimporter][cb-cookie] sind Drittquellen, kein
offizielles SDK: benötigte Cookies `api-platform_serviceToken` und `userId`,
optional `api-platform_ph`, `api-platform_slh`. Offizieller Cookiepolicy-Text im
Hauptbundle nennt für diese Logincookies **24 Stunden**. Damit ist ein dauerhaft
unbeaufsichtigter Monitor ohne Wiederanmeldung nicht belegt. Keine separate
read-only Cookieberechtigung oder offizieller Monitor-Lesetoken wurde gefunden.

Die öffentliche Herstellerwebsite zeigt den gleichen Readvertrag wie CodexBar;
die Quelle der Routen ist damit zusätzlich offiziell überprüft. Das macht ihn
noch nicht zu einer stabil dokumentierten Drittanbieter-API. Empfehlung für einen
optionalen Browsermonitor: eigene explizite Anmeldung in einem abgegrenzten
Browserkontext, reine GET-Allowlist für die ausgewählten Pools, getrennte
Sitzungslebensdauer und Quelle „MiMo Konsole“. Ein eigener Browserlogin ist eine
bewertbare Alternative, keine bereits vereinbarte Architektur und keine heimliche
Cookieübernahme. Reiner Monitorlogin bedeutet noch keinen Inferenzkey in lain.

### Einheiten, Zeit, Grenzen und nächste Beweise

- [Individualplan][m-plan-doc]: **Credits**, nicht rohe Tokens und nicht Geld.
  Model-/Cachefaktoren sowie 0,8× nachts (00–08 Uhr Beijing / 16–24 UTC) verändern
  Verbrauch. ASR verbraucht Credits nach Audiodauer, TTS ist zeitweilig gratis.
  Lokale Tokenzählung bildet dieses Gesamtbudget nicht zuverlässig ab.
- **Monat oder Jahr:** aktuelle Angebotsseite nennt fixe monatliche **oder
  jährliche** Gesamtquota. [Expiry-FAQ][m-expiry] erklärt einen Kalendermonat/-jahr
  ab Kauf und UTC-Ende; Teamseats haben monatliche Quota. Kein pauschaler
  30-Tage-Monatsreset und keine jährliche Individualquota durch zwölf teilen.
- Individualplan endet bei Erschöpfung oder Ablauf; kein automatischer Verbrauch
  der PAYG-Geldbalance. Nicht verbrauchte Ressourcen werden nicht übertragen.
  Auto-Renewal ist ein Zahlungs-/Vertragsvorgang, kein lokal angenommener Refill.
- Hersteller-UI-Text im Hauptbundle: Balance-/Usage-Daten nahe Echtzeit mit
  **bis zu fünf Minuten Verzögerung**, Tagesdaten final **07:00 UTC am Folgetag**.
  Das ist UI-Quelleninformation, kein zugesicherter API-SLA. Abrufzeit ist nicht
  automatisch fachliche Datenzeit. Reale Fenster/Serverzeit zunächst validieren.
- Plan darf laut Hersteller nur in kompatiblen interaktiven Coding-/Agenttools
  verwendet werden; offensichtliche Nicht-Coding-Skripte und eigene Appbackends
  sind ausgeschlossen. Eine lokale lain-Codingroute ist gesondert auf diesen
  Umfang zu beziehen, nicht als allgemeines weiterverkaufbares APIbudget freigeben.

**Nächster begrenzter Nachweis:** Einen vorhandenen berechtigten MiMo-Account
später explizit für (a) offiziellen CLI-Login mit bewusstem Produkttyp, danach
nur Metadaten-/Secretübernahme ohne Inferenz, und getrennt (b) eigenen
Browsermonitor verwenden. Je ausgewähltem Produkttyp einmal die obigen GETs
aufzeichnen, Secrets lokal schützen, nur redigierte Schemabeispiele sichern.
Erwartung: `{uid, base_url, key-prefix}` bindet den Inferenzzugang; separat
`usage.items`, Planende und ggf. Projekt/Seat identifizieren den Monitorpool.
Ist nur nativer lain-Browserbootstrap gewünscht, zuerst Unterstützung für eigenen
Appnamen/Keynamen und dessen Nutzungserlaubnis qualifizieren. Keine OAuth-
Clientregistrierung erfinden, wo noch gar kein standardisierter Clientvertrag
belegt ist.

## NVIDIA Build: eng vom übrigen NVIDIA-Angebot trennen

lain hat den Katalog `nvidia-build` mit OpenAI-Inferenzbasis
`https://integrate.api.nvidia.com/v1`, Bearer-Key und z.B.
`stepfun-ai/step-5-preview`. Kein Quotaadapter. [Quickstart][nv-quick] dokumentiert
Developer-Program-Anmeldung und Keyerzeugung im Build-Modellfenster. [FAQ][nv-faq]
bestätigt kostenlosen Zugang zu gehosteten NIM-Endpunkten für Prototyping;
Produktionsnutzung/Enterprise-Lizenz und selbst gehostete 16-GPU-/90-Tage-
Evaluierungen sind andere Produkte und kein Build-Tokenkonto.

### Was neu konkret belegt ist

1. **Öffentliche statische Limits:** [Runtime-JSON][nv-limits], am Recherchetag
   HTTP 200, enthält exakt `requestsPerMinute: "Up to 40 rpm"`,
   `requestsPerDay: "10,000 requests per day"` und den Hinweis auf Modellvariation
   und Drosselung durch andere Nutzer. [Build-UI-Quelle][nv-login-asset] liest das
   Dokument als `content/copy.yaml`; Anzeige heißt „Your API Rate Limit“.
   Es enthält **keine** Nutzung, Remainingwerte, Account-ID, Resetgrenze oder
   Aktualisierungszeit. Kein harter 10.000er Tagesbalken oder Refill ausrechnen.
2. **Sitzungsgebundene Keyprovisionierung existiert:** [offizielles Webasset][nv-key-asset]
   verwendet `GET` und `POST /v3/orgs/{orgName}/keys/type/AI_PLAYGROUNDS_KEY`,
   außerdem Update/Delete mit Key-ID. Defaultkey: sechs Monate Laufzeit,
   Policies `product: nv-cloud-functions`, `scopes: [invoke_function]`, Resource
   `{id:"*",type:"account-functions"}`. Antwort enthält `apiKey.value` und
   `expiryDate`. Das ist ein Beleg für Browser-Key-Provisionierung, kein
   Monitoring-/Billing-Readscope. Diese Mutationen wurden nicht ausgeführt.
3. **Login ist bislang eine Websiteintegration:** UI führt nach
   `https://api.ngc.nvidia.com/login?email=...&app=api-catalog&redirect_uri=...`.
   Gemeinsamer [Fetchcode][nv-fetch-asset] verwendet Browsercredentials und kann
   je Pfad an `api.ngc.nvidia.com` oder `buildapi.ngc.nvidia.com` routen. Keine
   eigene OAuth-Client-ID, öffentliche Registrierung, Devicegrant oder
   Drittclient-Callbackfreigabe für lain wurde in diesen Quellen belegt.

### Auth- und Quotaoptionen

| Zugang | Inferenz? | Monitor? / Bewertung |
| --- | --- | --- |
| Build-Key nach offizieller Browseranmeldung | Ja, bei tatsächlicher „Public API Endpoints“-Berechtigung; Accountlogin allein genügt nicht | Kein Build-Balance-/Quota-Read nachgewiesen. Derselbe Key hat nicht automatisch Monitoringrechte. |
| NGC Personal Key mit explizitem Dienst „NVIDIA Public API Endpoints“ | Offizielle [NGC-Keydoku][nv-ngc] ordnet diesen Dienst ausdrücklich den API-Catalog-NIM-Endpunkten zu. Ein beliebiger Registry-/NVCF-Key genügt nicht. | Andere Dienst-/Scopeberechtigungen und Enterprise-Metriken sind kein Beweis für das gehostete Build-Prototypingkonto. |
| NGC CLI | [Offizielle Anleitung][nv-cli] verlangt `ngc config set` und bereits vorhandenen API-Key; kein hier qualifizierter Browser-/Device-Login | CLI kann Org-/Registry-/Cloud-Function-Ressourcen verwalten, aber keine passende Build-Remainingquota wurde gefunden. Keyimport wäre höchstens eine Einrichtungserleichterung nach Dienstprüfung. |
| NGC/NCA Enterprise-SSO/OIDC | Authentifiziert Identität/Org; NGC-Rollen müssen separat vergeben werden | NCA-/SSO-Mitgliedschaft verleiht keine NGC-Rollen oder Build-Monitorrechte. Kein geeigneter Endnutzer-OAuthersatz. |
| Eigener Build-Browserkontext | Website kann nach erfolgreichem Login API-Key erzeugen | Technisch konkrete Alternative zu Copy/Paste, aber Keymutation/Browserintegration und zulässige Drittclientnutzung erst qualifizieren. Quotaquelle kann daraus noch nicht abgeleitet werden. |
| Self-hosted NIM-/NVCF-/Enterprise-Observability | Nur für jeweiliges Deployment/Produkt | Hier bewusst kein Ersatz für Build-Quota. Lokale Requests/Tokens bleiben lokale History. |

Die [NGC-Doku][nv-ngc] beschreibt bis zu acht Personal Keys, nutzer-/orggebundene
Lebensdauer und Diensteauswahl; Service Keys hängen an der Organisation und
benötigen Orgowner/Useradmin. `docker login ... --username '$oauthtoken'` ist laut
Doku ausdrücklich **API-Key-Authentifizierung**, kein OAuthlogin. Ein ähnlicher
Produktname oder OAuth-Bezeichner genügt nicht als Nachweis.

**Umsetzbare Empfehlung:** NVIDIA-Inferenz bleibt bis zu weiterem Nachweis der
offizielle Build-Keyweg mit expliziter Public-Endpointberechtigung; statische
Limits können als Herstellerhinweis mit Datum stehen. Für den gewünschten
vollautomatischen Anschluss ist die nächste Aufgabe begrenzt: Build-Unterstützung
für registrierte Drittclients/Keyprovisionierung erfragen oder in einem neu
autorisierten isolierten Browserlauf genau deren Session-/Callbackvertrag und
eventuelle Usage-/Rate-Limit-Reads belegen. Wenn die Konsole keine Accountnutzung
zeigt, kein Monitoring aus API-Key-Inventar konstruieren. Keine Inferenz zum
Refresh senden. Der Befund lautet **Quelle und Delegation noch nicht qualifiziert**,
nicht „NVIDIA kann keine Quota-API haben“.

## Quellen und Evidenzniveau

Alle verlinkten Kernquellen tatsächlich am **2026-10-24** öffentlich geöffnet.
Einzelne ReadMe-/NVIDIA-Seiten antworten je Client zeitweilig 403; erfolgreiche
HTML-/Markdownabrufe und öffentliche Buildassets tragen die obigen Aussagen.
Webassets sind offizielle technische Evidenz der aktuellen Website, keine
garantierte öffentliche API. CodexBar ist gesondert als Drittprojekt gekennzeichnet.
Die JSON-Datei neben diesem Bericht enthält Quellen, Authoptionen und offene
Implementierungs-/Qualifikationsarbeit maschinenlesbar.

[m-quick]: https://mimo.mi.com/static/docs/tokenplan/Token%20Plan/quick-access.md
[m-api]: https://mimo.mi.com/static/docs/quick-start/faq/api-integration.md
[m-account]: https://mimo.mi.com/static/docs/quick-start/faq/account.md
[m-cli-doc]: https://mimo.mi.com/static/docs/tokenplan/integration/mimo-code.md
[m-repo]: https://github.com/XiaomiMiMo/MiMo-Code/tree/e37f2c2d01460df19b31d67b8116d6c66fe33db8
[m-cli-src]: https://github.com/XiaomiMiMo/MiMo-Code/blob/e37f2c2d01460df19b31d67b8116d6c66fe33db8/packages/cli/src/plugin/mimo.ts
[m-cli-auth]: https://github.com/XiaomiMiMo/MiMo-Code/blob/e37f2c2d01460df19b31d67b8116d6c66fe33db8/packages/cli/src/provider/auth.ts
[m-cli-path]: https://github.com/XiaomiMiMo/MiMo-Code/blob/e37f2c2d01460df19b31d67b8116d6c66fe33db8/packages/shared/src/global.ts
[m-main-asset]: https://platform.xiaomimimo.com/static/main.569e3e15e1d2bea2.chunk.js
[m-auth-asset]: https://platform.xiaomimimo.com/static/690.a73865333011cc6e.chunk.js
[m-plan-asset]: https://platform.xiaomimimo.com/static/279.17edd5ab4b3a18cd.chunk.js
[m-plan-doc]: https://mimo.mi.com/static/docs/tokenplan/Token%20Plan/subscription.md
[m-expiry]: https://mimo.mi.com/static/docs/quick-start/faq/token-plan/Validity&Expiry.md
[cb-doc]: https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/docs/mimo.md
[cb-fetch]: https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Providers/MiMo/MiMoUsageFetcher.swift
[cb-cookie]: https://github.com/steipete/CodexBar/blob/d34fbb47afe73f81dd591bae28a60f72c39a9fe4/Sources/CodexBarCore/Providers/MiMo/MiMoCookieImporter.swift
[nv-quick]: https://docs.api.nvidia.com/nim/docs/api-quickstart.md
[nv-faq]: https://docs.api.nvidia.com/nim/docs/product
[nv-limits]: https://build.nvidia.com/api/runtime/content/copy.yaml
[nv-login-asset]: https://build.nvidia.com/_next/static/chunks/1ezdm36-wvhki.js
[nv-key-asset]: https://build.nvidia.com/_next/static/chunks/2xv_0hdneb8vt.js
[nv-fetch-asset]: https://build.nvidia.com/_next/static/chunks/2a1ymhcs400-z.js
[nv-ngc]: https://docs.nvidia.com/ngc/latest/ngc-catalog-user-guide.html
[nv-cli]: https://docs.ngc.nvidia.com/cli/cmd.html
