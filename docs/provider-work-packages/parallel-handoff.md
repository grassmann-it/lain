# Übergabe: neue Anbieter, Quotas und private Accounteinrichtung

Stand 24.10.2026. Dieser Auftragstext ist für den Main-Implementor vorbereitet.
Die Workeraufteilung ist eine Empfehlung; hier wurden keine Worker gestartet und
keine Runtimeänderungen, Providerlogins oder echten Inferenztests ausgeführt.

## Bestätigtes Ergebnis und aktueller Befund

Der Nutzer möchte die fehlenden Anbieter nachziehen, mehr echte Quotaquellen,
verständliche Statusanzeigen und bessere Accountnamen. Keys sollen ausschließlich
lokal über lain eingegeben werden können, per UI oder CLI-Wizard. Wenn passende
Monitorwerte OAuth, andere Berechtigungen oder einen separaten Managementtoken
benötigen, soll dieser Zugang gezielt ergänzt werden. Ein fehlender einfacher
API-Key-Endpunkt ist kein Grund, die anderen offiziellen Zugangswege zu ignorieren.

Der Nutzer meldet Accounts bei der empfohlenen kostenlosen/Trial-Auswahl und
bereits teilweise verbundene Zugänge. Das bestätigt keine individuell gewährten
Credits, Scopes oder erfolgreiche Liveinferenz. Kilo ist laut Nutzer vorhanden,
aber ausgeschöpft; keine Aufladung oder bezahlte Ersatzroute voraussetzen.

Lesender Abgleich auf `main`, HEAD
`b5d3f815fb9ae5501bb5fe33bd0e21ca2915615e`: 15 JSON-Kataloganbieter. **SambaNova,
Fireworks, Cohere, Upstage und Kilo fehlen** sowohl dort als auch im installierten
CLI-Katalog. Die installierte Accountliste enthält unter anderem Groq, Gemini,
NVIDIA, Cloudflare und OpenRouter. Keine Secrets gelesen; keine Providerabfrage
durch diese Account-/Katalogabfragen ausgelöst.

Konkrete Lücken im gelesenen Code:

- `QuotaService.Project` stellt fehlende Adapter als `unsupported` plus Text in
  `Error` dar. `CompactAccountUsage` ergänzt dafür wiederholt fehlende Werte und
  eine Warnung. Eine fehlende Fähigkeit ist kein fehlgeschlagener Abruf.
- `GroqRateLimitHeaders` samt Parserfixtures existiert. Seine Existenz belegt noch
  keine durchgängige Erfassung/Anzeige der tatsächlichen Upstreamantwortheader.
- `MainWindow.Providers.cs` erzeugt `My {provider.Name} account` als Standardname.
- Installiertes `lain --help` hat `accounts add-api`, aber keinen interaktiven
  Account-Wizard. Desktopsetup verlangt genau ein Secret.
- `QuotaCredential` kennt einen Bearertoken, `AccountService` einen qualifizierten
  Loginadapter für Codex. Ein allgemeiner OAuth-/Mehrcredentialanschluss ist damit
  noch nicht umgesetzt. Bestehende Codexlogins nicht als generische Lösung ausgeben.

Dies ist eine gezielte Ausgangsprüfung, kein vollständiges Code- oder Liveaudit.
Main gleicht vor Start den dann aktuellen Stand ab und übernimmt bereits erledigte
Teile, statt sie erneut zu implementieren. Vorhandene uncommittete Providerdoku
gehört zu dieser Recherche; fremde Änderungen nicht zurücksetzen oder mitstagen.

## Parallelstart und Verantwortung

Empfehlung: **sieben Worker plus Main**, unter dem bisherigen Maximum von zwölf
aktiven Agenten. Sol für abgegrenzte Standardanschlüsse/Bedienung; Astra für
Auth, Quotas, Datenzuordnung und unabhängige Prüfung. Das ist eine Aufgabenwahl,
keine Festlegung kostenpflichtiger Modellzugänge.

Main reserviert zuerst gemeinsame Registrierungs-/Vertragspunkte und bestätigt
kurz das Datenformat für Quotaunterstützung, Quellen und Freshness. Danach können
Providerfixtures, offizielle Authrecherche und Oberflächenarbeit parallel laufen.
Kein großer Modulumbau vor der ersten Anbieterlieferung.

| Worker / Vorschlag | Pakete und eigenständiges Ergebnis | Eigener Schreibbereich / Abhängigkeit |
| --- | --- | --- |
| W1 · Sol | **PV-SAMBA + PV-UPSTAGE:** zwei auswählbare Gatewayanbieter mit privatem Setup, konkreten Routen und ehrlichem Livezustand | Eigene Manifeste, eigene Providerfixtures und Kurzdocs; Standardtransport nutzen. Kein Quota- oder OAuthversprechen aus Kompatibilität ableiten. |
| W2 · Sol | **PV-KILO + PV-COHERE:** getrennt abnehmbare Gatewayanschlüsse | Eigene Manifeste/Tests/Docs. Kilo auf tatsächlich verfügbares Free-Modell vorbereiten, bezahlte Fallbacks ausschließen; erschöpftes Konto ist ein Zustand, kein Anlass zum Kauf. Cohere-Evaluation nicht zur Produktionsfreigabe erklären. |
| W3 · Astra | **PV-FIREWORKS:** Gateway und unabhängig abschließbarer offizieller Quotaadapter | Eigenes Manifest, Adapter, HTTP-Fixtures/Docs. Main integriert Registrierung; Accountkennung und Rechte explizit. Quotaarten, Einheiten und Herkunft erhalten; `updateTime` ist kein Reset. |
| W4 · Astra | **Q-SOURCES:** vorhandene Groq-/Gemini-/Cloudflare-/NVIDIA-Zugänge um belastbare Quota-/Verbrauchsquellen erweitern | Eigene Parser/Adapter/Quellenfixtures. Zunächst je Anbieter Quellenblatt; konkrete Erfassungsbrücke zum Core mit Main abstimmen. Zusätzliche Auth über W5, Anzeige über W6. |
| W5 · Astra | **AUTH-MONITOR:** offizielle weitere Zugänge für Q-SOURCES prüfen und belegte passende Authwege ergänzen | Zunächst Quellen-/Scope-/Flowvertrag; danach eigene Authmodule und Fixtures. Gemeinsame Credential-/Hostverträge ausschließlich mit Main. Keine spekulative Provider-OAuthimplementation. |
| W6 · Sol | **UX-SETUP:** brauchbare Quotaanzeige, kurze Standardnamen, privater CLI-Wizard | Alle betroffenen Desktop-/CLI-Dateien und Präsentationsfixtures gehören diesem Worker. Gemeinsame Hostregeln von Main; Quellen und Scopes aus W4/W5. Keine konkurrierenden CLI-/Desktopworker. |
| W7 · Astra | **REVIEW-PROVIDERS:** unabhängige Prüfung der integrierten Ergebnisse und Testlücken | Zunächst read-only Review, danach eigene Prüfberichte/gezielte unabhängige Fixtures; keine gemeinsamen Produktdateien ändern. Keine installierte GUI auf dem Nutzerdesktop automatisieren. |

Main besitzt `Program.cs`, gemeinsame Core-/Client-/Hostverträge, zentrale
Registrierung/Transportvalidierung, gegebenenfalls notwendige Core-Hooks und
Integration. Worker liefern begrenzte Änderungen aus getrennten Worktrees,
inklusive benötigter Quellen/Dateien; die ignorierten Sparringnotizen sind kein
erforderlicher Übergabebestandteil. Ein Worker meldet zusätzliche gemeinsame
Dateien, bevor er sie bearbeitet. Keine parallelen Schreibzugriffe auf dieselben
Dateien; Build-/Runtime-/Installationsslots koordiniert Main.

## Q-SOURCES: mehr Daten, ohne Quotas zu erfinden

Pro Anbieter dokumentieren: offizielle API, interne Web-API, Antwortheader oder
manueller Konsolenweg; benötigter Credentialtyp/Scope; Account-/Projekt-/Modell-
scope; Einheit; Beobachtungszeit; Verzögerung; verfügbare Mengen und Reset.
Eine Verbrauchsquelle ohne Limit ist keine Restquota. Lokale lain-History bleibt
als ausschließlich durch lain beobachtete Nutzung separat bezeichnet.

| Anbieter | Bereits belegte Basis / nächster Nachweis |
| --- | --- |
| **Groq** | Dokumentierte Antwortheader und vorhandenen Parser bis zum richtigen Account/Request anbinden. Requests beziehen sich auf RPD, Tokens auf TPM. Keine Inferenz nur zur Quotaaktualisierung senden. Console-/Enterprisewege haben andere Authrechte und sind nicht bereits durch den normalen Key abgedeckt. |
| **Gemini** | Projektbezogene Limits und AI-Studio-Konsolenweg sind dokumentiert. Offizielle alternative OAuth-/Monitoring-/Quotawege konkret prüfen; ein Consumer-Googlelogin beweist keine API-Reportingrechte. Projektidentität und modellabhängige Limits qualifizieren; bei reiner Usage keinen Rest erfinden. |
| **Cloudflare** | 10.000 Neurons/Tag im dokumentierten Workers Free ist eine Allokation, kein beobachteter Rest. Offizielle Account-/Analytics-/Usagewege sowie nötige Leserechte und etwaige OAuthalternativen prüfen. Nur bei passender gleicher Einheit, Zeitperiode, Abdeckung und bestätigtem Tarif einen Rest ableiten, sonst Usage separat anzeigen. Keine Workers-/Gateway-/Modellkosten vermischen. |
| **NVIDIA Build** | Kostenloses Prototyping ist belegt; fixer API-Key-Restquotaendpoint hier nicht qualifiziert. Offizielle Kontoverwaltung, Usage-/Quota-APIs, Rollen und unterstützten OAuthweg prüfen. Aus Enterprise-/Selfhost-NIM-Daten kein Build-Cloudbudget ableiten. Falls kein geeigneter offizieller Weg existiert, Grenze belegen und Konsolenlink anbieten. |
| **OpenRouter** | Bestehenden Adapter erhalten. Key-Spendcap als **Ausgabenlimit dieses Keys**, nicht als Accountguthaben darstellen. Gelieferte Reset-/Budgetfelder nutzen; fehlenden Reset nicht schätzen. Tatsächliche UTC-Tagesgrenze und aktuelle API-Antwort bei Free-Requests getrennt prüfen. |

Ein Quellenblatt kann mit dem belegten Ergebnis „kein geeigneter offizieller
Zugang gefunden“ abgeschlossen werden; das ersetzt keine Quotaimplementation.
Offene Implementierungs-/Authentifizierungslücken ausdrücklich von dieser
Anbietergrenze trennen. Keine automatischen Browsercookieimporte, internen
Websession-Scraper oder neuen Dienstaccounts als stillen Ersatz einführen.

## AUTH-MONITOR: Anschluss abhängig vom belegten Zugangsweg

Der Ergebniswunsch eines zusätzlichen Monitorzugangs ist bestätigt. **Welche
Provider OAuth anbieten, welcher Flow und welches interne Credentialmodell
passen, ist noch zu prüfen.** Zuerst offizielle Herstellerquellen und Bedingungen:
native Desktopclients erlaubt, Clientregistrierung, Redirects, Scopes, Laufzeit,
Refresh/Logout und Erlaubnis zur kommerziellen Verteilung. Die API-Keyoberfläche
nicht einfach mit einem undokumentierten „Login“-Button versehen.

- Unterstützt der Anbieter passende Leserechte am bestehenden Token, führt der
  Wizard dorthin. Einen bereits ausreichenden Key nicht unnötig durch OAuth ersetzen.
- Braucht es ein separates Monitoring-/Managementsecret, privat am selben Account
  nach Fähigkeit zuordnen. Es darf kein zweiter erfundener Inferenzaccount nötig sein.
- Gibt es offiziellen Desktop-OAuth, Browserlogin mit geeignetem Flow, State/PKCE
  wo erforderlich, Abbruch/Timeout und sichere Callbackbindung qualifizieren.
  Kein Clientsecret in eine verteilte Desktop-App einbauen.
- Refreshrotation, konkurrierende Leser, abgelaufene/entzogene Tokens und
  Reconnect behandeln. Externe importierte Sessions niemals ungefragt refreshen
  oder widerrufen. Ein bloßer Nutzerinfo-Scope liefert keine Quota-Berechtigung.
- Kein geeigneter offizieller Weg: konkreten fehlenden Zugang nennen. Ein
  Account mit funktionsfähiger Inferenz bleibt trotz fehlendem Monitor benutzbar.

W5 kann Quellen/isolierte Module parallel liefern. Änderungen am gemeinsamen
Secretformat oder Accountvertrag brauchen vor der abhängigen Umsetzung einen
kleinen kompatiblen Zuschnitt mit Main; keine pauschale Accountmigration.

## UX-SETUP: beobachtbarer Abschluss

1. **Quotaansicht:** Ein fehlender unterstützter Abruf erzeugt eine kurze neutrale
   Erklärung und den passenden nächsten Schritt, keine fünf Fehlermeldungen.
   „Keine separate Quota-Abfrage; Limits nach der nächsten Nutzung“ ist nur für
   tatsächlich angebundene passive Headerquellen zulässig. Vorher klar „noch nicht
   implementiert“ oder „kein geeigneter Abruf bekannt“. Echte Auth-/Netzfehler,
   veraltete letzte Werte und erschöpftes Budget bleiben sichtbar unterscheidbar.
2. **Werte:** Nur beobachtete Werte samt Quelle/Frische anzeigen. Geldbetrag ohne
   belegten Nenner bekommt keinen Prozentbalken. Bei begrenztem Keybudget bleiben
   berechenbare Prozente zulässig, aber der Text benennt das Keylimit. Fehlende
   Resets bei Guthaben nicht ständig als Warnung wiederholen. Details bleiben in
   UI/CLI erreichbar; vorhandene JSON-Verträge kompatibel erhalten.
3. **Namen:** Neue Defaults kurz und lesbar, etwa `Groq`, `Gemini`, `NVIDIA Build`,
   `Cloudflare Workers AI`, `OpenRouter`; bei mehreren Accounts eindeutigen
   Zusatz vorschlagen, etwa `Groq 2`. Keine ungeprüften Tarifbehauptungen im Namen.
   Gemeinsame Namensregel für UI und Wizard. Eigene Namen niemals automatisch
   überschreiben. Bestehende alte Defaults höchstens per sichtbarer, vom Nutzer
   ausgewählter Umbenennung bereinigen; IDs/Routen/Historybezüge bleiben gleich.
4. **CLI-Wizard:** Vorgeschlagener Einstieg `lain accounts add`, nach Abgleich mit
   der aktuellen CLI. Anbieter, benötigte Felder, Tarif und Route aus demselben
   Hostkatalog wie die UI. Offizielle Key-/Loginlinks zeigen, Secrets verdeckt im
   TTY abfragen und lokal speichern. Kein Secret in argv, Logs, Chat oder Report;
   `add-api --key-stdin/--key-env` für bestehende Automatisierung erhalten.
5. Wizard kann abbrechen, ohne halben Account zu hinterlassen; Konkurrenzrevision
   korrekt behandeln. Nichtinteraktiv gezielt auf bestehende Automationswege
   verweisen, nicht auf einem unsichtbaren Prompt hängen bleiben. Speichern,
   Metadatenabruf und echte Inferenzprobe als unterschiedliche Aktionen darstellen.
6. Jeder Anbieter erhält einen direkten Link zu den tatsächlich benötigten
   Credentials sowie eine kurze Feldanleitung. Zusätzliche Monitorrechte mit
   Zweck erklären; fehlender Referrallink blockiert kein Setup.

## Prüfung, Lieferung und Grenzen

Je Anbieter aus [first-wave.md](first-wave.md) Gateway-/Monitorabschluss separat
anwenden. Passende Fake-HTTP-Prüfungen: 401/403/429, Nullsaldo, fehlende Felder,
falscher Scope, stale/last-good, Accountverwechslung und Abbruch. Parserfixtures
allein sind kein Nachweis der angebundenen Runtimequelle. Neue OAuthpfade zusätzlich
gegen Callbackverwechslung, Widerruf und Refreshkonkurrenz prüfen.

W6 prüft die vom Nutzer gezeigten fünf Accounts als synthetische Präsentation:
keine Warnung allein wegen unsupported, sinnvolle Quellen-/Frischeanzeige,
OpenRouter-Keylimit korrekt, individuelle Namen erhalten. GUItests auf privatem
Xvfb, Terminaltests ohne echte Secrets. Main führt gemeinsame Build-/Regressionen
einmal am integrierten Stand aus; danach W7 unabhängig prüfen lassen.

Liveproben erst mit lokal in lain gespeicherten Accounts und bestätigten passenden
Berechtigungen/Gratisbudgets im bestehenden autorisierten Testumfang. Kein Kauf,
keine Autoaufladung und keine bezahlten Fallbacks. Fehlt der Nachweis eines
kostenlosen Testwegs, bleibt die Liveprobe offen und die übrige Arbeit läuft weiter.
Kein Worker soll Tokens anfordern, aus privaten Dateien auslesen oder weiterreichen.
Installieren, Dienstneustarts und Release koordiniert Main innerhalb des dafür
bestehenden Auftrags; diese Planungsrunde hat sie nicht durchgeführt.

Lieferstatus je Paket: vorbereitet / lokal geprüft / installiert / live geprüft
getrennt; echte externe Grenzen und noch fehlende Implementierung unterscheiden.
Keine Behauptung „alle Quotas funktionieren“ aus statischen Angebotslimits.
Referralaktivierung, Standalone-Monitor, externe Historyimporte, weitere Kauftarife
und lokale Server bleiben getrennte Folgepakete im [Register](README.md).

## Einstieg und Quellen

- [Modularitätsbefund und Vorschlag](modularity.md),
  [erste Providerpakete](first-wave.md), [Voraussetzungen](prerequisites.md).
- [Gemini/Groq/Mistral samt offiziellen Quellen](../providers/direct-apis.md),
  [NVIDIA/StepFun](../providers/nvidia-step.md),
  [Cloudflare/OpenRouter](../providers/gateways.md).
- [Fireworks/SambaNova und Vertragsgrenzen](../provider-discovery-2026-10-24.md),
  [Cohere/Upstage/Kilo](../provider-discovery-expansion-2026-10-24.md).
- [CodexBar-Inventar](../codexbar-provider-inventory.md) als Quellenhinweis für
  zusätzliche Monitorwege; dessen interner Webzugang ist keine offizielle API-
  oder Nutzungsrechtsfreigabe für lain.
