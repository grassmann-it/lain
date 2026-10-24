# AC-07 – Eigene und verwaltete Logins: Kimi, Z.ai und Codex

Status: Arbeitspaketvorschlag vom 24.10.2026 auf `5dc916f`; keine Implementierung oder Ownerentscheidung vorweggenommen.
Grundlage: [Connectentwurf](../account-connect-redesign-2026-10-24.md), [Kimi/Z.ai](../account-connect-research/reference-kimi-zai.md), [Codex](../account-connect-research/reference-codex-commandcode.md), jeweils mit vollständigen Quellen im zugehörigen JSON.
Command Code gehört zum CLI-Paket und ist hier ausgeschlossen. Keine Logins, Providerreads, Runtime-, Buildaktionen oder Commits für dieses Dokument.

## AC-07.0 – Liefergrenze und gemeinsame Voraussetzungen

- Ergebnis: Anbieterfähigkeiten im gemeinsamen Connectablauf; kein zweiter Wizard und keine Pflichtmodellroute für Kontingente.
- Zuerst vorhandene passende Credentials nutzen; Kimi-/Z.ai-Keykonten und Codex-Browserlogin bleiben funktionsfähig, einschließlich Namen, IDs und History.
- Login, Monitorread, Inferenzkonfiguration und tatsächlich geprüfte Inferenz haben getrennte Zustände; Login/Modellliste beweisen keinen Modellrequest.
- Neue coreunabhängige Connects/Monitormethoden und Quota-Refresh starten keinen Proxy; die ausdrücklich erklärte bestehende Codex-Coremethode ist im Gateway die begrenzte Ausnahme. Kein Connect aktiviert neue Routen/Profile oder startet Modellrequest, Kauf, Planwechsel oder kostenpflichtigen Fallback.
- Credentialowner ist pro konkretem Credential eindeutig. Gleiche Identität darf getrennte Grants besitzen; dasselbe Refreshmaterial niemals parallel durch Host, Core und CLI erneuern.
- Delegation bedeutet Referenz auf einen isolierten Owner und begrenzte Operationen; kein Kopieren fremder Refreshfiles oder pauschaler Zugriff auf normale Nutzerprofile.
- Offene Architekturwahl je Provider vor Implementierung protokollieren: eigener Owner oder delegierter Owner, Transport, Audience, Ressourcenbindung und Disconnectwirkung.
- Reihenfolge: Qualifikationsspezifikation unabhängig vorziehen → kleiner Qualifikationsversuch/Ownerentscheidung → konkrete Adapter nach AC-00/AC-01 → AC-10-Abnahme.
- Qualifikationsversuche unten sind spätere, separat auszuführende Arbeit; Quellenprüfung/Fixtures und echte Herstellerqualifikation getrennt ausweisen.
- Unqualifizierte Buttons nicht als verfügbare Anmeldung ausliefern. Vorhandener Key-/Browserpfad ist währenddessen nutzbarer Fallback.

An **AC-01 Accountbasis** und **AC-00 Integration**, vor Providerimplementation zu vereinbaren:

- Connectkontext mit Methode/Zweck/Zielaccount/Revision, gebundener Ressource und Login-Generation; begrenzte Operationen, Ablauf und Abbruch.
- Atomare Credentialreferenz mit Ownerart, Credentialtyp, Product/Region/Subjekt/Org/Projekt, Gültigkeit und bestätigten Fähigkeiten; Geheimwerte bleiben privat.
- Kurzlebige Requestprojektion für Inferenz und Monitor: Audience, Host, notwendige Header und Ablauf; kein Refreshsecret im Reader oder statischen Profilexport.
- Owner-Generation gegen späte Callbacks/Refreshantworten; Reconnect derselben bestätigten Identität erhält ID/History, abweichende Identität überschreibt nichts.
- Getrennte Zustände für fehlende Rechte, abgelaufen, Quelle unbekannt, temporär nicht erreichbar und vorhandene letzte erfolgreiche Werte.
- Entfernen lokal, Pending abbrechen und gezielt Anbieterzugang widerrufen als verschiedene Aktionen; erfolgreiche Revokation nie allein aus lokalem Löschen ableiten.

## AC-07.1 – Kimi Code

**Geplantes Ergebnis und Erhalt**

- Bestehender Coding-Key liest weiter `/usages` auf seinem qualifizierten `.com`- oder `.ai`-Codinghost ohne Zusatzlogin.
- Neuer Device-Login ermöglicht Kontingente; Modelle ergänzen dieselbe Verbindung erst nach eigener Transportqualifikation.
- Moonshot Open Platform/PAYG bleibt anderer Produktscope; kein Umleiten seines Keys auf Coding und keine implizite Regionskonvertierung.

**Offene Ownerentscheidung und kleinster entscheidender Versuch**

- A: eigener Hostowner mit veröffentlichtem Kimi-Device-Toolkit; B: isolierter offizieller `kimi-code`-Owner mit lokalem authentifiziertem Usagezugang.
- Empfehlung: A zuerst eng qualifizieren, weil Toolkit und gleicher Access-Token für Hersteller-Inferenz/Usage belegt sind und kein breiter Agentserver nötig wäre.
- Versuch: unterstützte eigene Clientverwendung/Admission anhand Herstellervertrag klären; dann ein isolierter Device-Flow mit einer Region, einem Account und einem reinen Quota-GET.
- Erforderlicher Lebenszyklusnachweis: Ablauf/Abbruch, erneuter Start, ein Renewal und ein paralleler Monitor-/Inferenzcredentialabruf bei genau einem Refreshowner.
- Vor Modell-Go zusätzlich den tatsächlich eingesetzten lain-Inferenztransport auf aktuelle Token, Host/Audience und benötigte Harness-/Versionsmerkmale qualifizieren.
- Modelllisten-GET/Requestfixture reicht für Form/Metadaten; echte Inferenz bleibt bis zu ihrem später beauftragten Einzelversuch `unverified`.
- A-Go: zulässiger eigener Client, sichere Tokenübernahme, lesbare native Fenster und erneuerbarer Zugang ohne CLI-Konfigurationsänderung.
- A-Abbruch: Clientannahme ungeklärt/abgelehnt, falsche Region, fehlende Identitätsbindung, unerklärte Rechte oder kein belastbarer Renewalpfad; keine offizielle Client-ID blind übernehmen.
- Dann B als getrennten Versuch qualifizieren: gepinnte CLI, eigenes Home, sichere lokale Bearerübergabe und `GET /api/v1/oauth/usage` inklusive Fehlerenvelope.
- B-Go erlaubt den belegten Monitorzugang; der lokale Agent-/ACP-/Sessionserver wird dadurch **kein OpenAI-kompatibler Inferenzproxy**.
- B-Abbruch bei Zugriff nur über fremdes Nutzerprofil, globaler Tokenrotation/Logout oder nicht begrenzbarem Connector; Fallback bleibt vorhandener Coding-Key.
- Vor Umsetzung Entscheidung A/B und getrenntes Modell-Go festhalten; der Implementierer darf die Ownerwahl nicht durch Bibliotheksauswahl entscheiden.

**Credential, Refresh, Scope und Transport**

- A hält OAuth Access/Refresh geschützt und serialisiert Renewal; Reader erhält Accessprojektion. B hält Refresh ausschließlich im isolierten Herstellerowner.
- Direkter Monitor: `GET {eigener Codingbase}/usages`, Bearer Access-Token/Key; alternative B-Projektion liest lokales Usage-API mit eigenem Serverbearer.
- Direkte Inferenz benötigt erneuerbare Tokenprojektion am qualifizierten Codingtransport; kein einmaliger Tokenexport als langlebiger API-Key.
- Native 5h-/7d-/Monatsfenster, Reset und nullable Booster getrennt erhalten; aktueller lain-Adapter deckt Booster noch nicht ab.
- Nur Kontingente: keine Modellroute, kein Modellrequest und keine Keyanlage. CLI-Login darf nicht das normale Default-Modell/Profil verändern.
- Scope bindet mindestens Subjekt, Codingprodukt, Region und erlaubten Host; lokale Detachaktion beendet keine fremde CLI-Session.

**Dateigrenze und Übergaben**

- Neue gemeinsame Providerdateien: `Accounts/Managed/KimiConnectAdapter.cs`, `Accounts/Managed/KimiUsageCredentialProvider.cs` unter `src/Lain.Host/`.
- Nur bei A-Go zusätzlich `Accounts/Managed/KimiOAuthOwner.cs`; nur bei B-Go `Accounts/Managed/KimiCliOwnerAdapter.cs` und `Quota/KimiCliUsageAdapter.cs`.
- Bestehenden `Quota/KimiQuotaAdapter.cs` gezielt um qualifizierte Projektion ergänzen; Same-Key- und Exact-Host-Verhalten nicht ersetzen.
- Request an AC-00/Root: `Program.cs`-Registrierung und Transportanschluss erst für gewählten Owner; bei B gepinnte optionale CLI-Lieferung/Prozessisolierung mit AC-05, kein Pflicht-Core für Monitor.
- Request an AC-01/Accountbasis: `.com`-Hostkanonisierung ohne ID-Neuberechnung, stabile Subjekt-/Regionsbindung, Refreshbroker und unabhängige Monitorfähigkeit.

**Beobachtbare Abnahme**

- Alter Key zeigt unveränderte Quote ohne Browser; neuer gewählter Login zeigt Fenster/Alter ohne Route, Proxy oder Modellrequest.
- Falscher Host/Region bekommt keinen Credential; verweigerter Consent/Abbruch überschreibt keinen Zugang; verspätetes Renewal belebt keinen entfernten Account.
- Genau ein Renewal bei parallelen Abrufen; derselbe Account bleibt nach Reconnect erhalten; CLI-Profil und normale Kimi-Konfiguration bleiben unberührt.
- Delegierter Monitor kann erfolgreich sein, während Inferenz ausdrücklich unqualifiziert bleibt; fehlende/fehlerhafte Fenster werden nicht Nullwerte.

## AC-07.2 – Z.ai / internationaler GLM Coding Plan

**Geplantes Ergebnis und Erhalt**

- Bestehender internationaler Key bleibt Inferenz- und Quotaquelle ohne Zusatzlogin; rohe `Authorization` bleibt erhalten.
- Neuer Browserconnect liefert getrennte OAuth-/Business-/kurzlebige Projektcredentials; persönlicher Scope zuerst, Team/CN gesondert qualifizieren.

**Offene Ownerentscheidung und kleinster entscheidender Versuch**

- A: eigener, unterstützter lain-Owner für ZCode-artiges Init/Poll/Exchange; B: delegierter isolierter ZCode-Owner mit belegter begrenzter Requestübergabe.
- Empfehlung: zuerst A-Zulässigkeit und reinen Readpfad mit vorhandener Key-ID prüfen; B ist keine bereits vorhandene sichere Tokenexport-Schnittstelle.
- Versuch: ein internationaler persönlicher Account, ein bestätigtes Projekt, bestehende passende Key-ID; Init/Poll → OAuth → Business-JWT → nicht anlegende Discovery → PAT → Quota.
- Keine Keyanlage im ersten Versuch. Fehlt passende ID, endet die Datenphase als `Zugang ergänzen`; Login darf separat erfolgreich bleiben.
- Entscheidender Nachweis: unterstützte eigene Client-/Versionsannahme, korrekte nicht geheime Key-ID, Tokenexpiry und erneuerbare Quota ohne verborgene Createoperation.
- Modell-Go separat für konkreten Codingtransport mit demselben aktuellen PAT; ein tatsächlicher Minimalrequest gehört erst zur späteren Inferenzqualifikation.
- Go: korrekte Identität/Projektbindung, gültige PAT-Antwort und echte Quota; bei Ablauf nur Ownererneuerung, keine Modellrequest-Wiederholung.
- Abbruch: ungeklärte `clientType`-Admission, fehlender nicht anlegender Pfad, falsches Projekt, `enable=false`, ungültige Tokenantwort oder notwendiger Zugriff auf fremdes ZCode-Profil.
- Fallback: vorhandenen Personal-Coding-Key einmal übernehmen/weiterverwenden, Console bei fehlendem Read; keine Umstellung auf Standard-API-Abrechnung.
- Ein späterer bewusster Modelle-Connect darf benannten Keydatensatz nur nach geklärter Semantik anlegen; Phasenstatus/Remote-ID verhindert blindes Create-Retry.
- Ist ein Key auch für reines Monitoring unvermeidlich, braucht dies einen gesondert belegten und vorab erklärten Connectpfad; kein stilles Durchlaufen des allgemeinen Resolvers.

**Credential, Refresh, Scope und Transport**

- OAuth-Token, ZCode-Session-JWT und Business-JWT sind verschiedene Credentials; Business-Exchange ist kein Beweis für vollständigen OAuth-Refresh.
- **`apiKey` aus Lookup/Create bezeichnet die Key-ID, kein Inferenzsecret.** Erst `/api_keys/{id}/access_tokens` liefert kurzlebiges `accessToken`.
- Owner prüft `tokenType`, `expiresIn`, `expiresAt`, `enable` und frühestes Ablaufdatum; pro Account/Familie/Origin/Org/Projekt/Key-ID/Generation begrenzter Cache.
- Genau ein Owner erneuert OAuth/Business/PAT; keine Speicherung des PAT als statischer Key und keine parallele Host-/ZCode-Refreshhoheit.
- Direkte Inferenz erhält aktuellen PAT am qualifizierten Codingtransport; Monitor sendet Credential roh an `/api/monitor/usage/quota/limit`.
- Team braucht eigene Rollen-/Scopeprüfung, Org-/Projectheader und belegtes `type=2`; CN braucht eigene Hosts/Region. Beides erweitert nicht still die jetzige Abnahme.
- Nur Kontingente nutzt ausdrücklich nicht anlegende Discovery; generischer Herstellerresolver kann einen Key erstellen und ist daher kein unveränderter Readadapter.

**Dateigrenze und Übergaben**

- Neue Providerdateien unter `src/Lain.Host/`: `Accounts/Managed/ZaiConnectAdapter.cs`, `Accounts/Managed/ZaiProjectTokenProvider.cs`, `Accounts/Managed/ZaiKeyDiscovery.cs`.
- Nur bei A-Go `Accounts/Managed/ZaiOAuthOwner.cs`; nur bei B-Go `Accounts/Managed/ZaiCliOwnerAdapter.cs` nach Nachweis eines begrenzten Delegationsvertrags.
- Keyanlage als getrennte `Accounts/Managed/ZaiKeyProvisioner.cs`; Quotaadapter erhält ausschließlich gültige Projektion, niemals Create-/Refreshzuständigkeit.
- Request an AC-00/Root: Contract-/`Program.cs`-Verdrahtung, PAT-fähiger Inferenztransport und gegebenenfalls optionale gepinnte CLI-Lieferung mit AC-05; keine globale Standard-Codingroute.
- Request an AC-01/Accountbasis: mehrere Credentialreferenzen pro Account, Projektwahl/Bindung, kurzfristige Projektion, Phasenjournal und getrennte Login-/Datenzustände.

**Beobachtbare Abnahme**

- Alter Key liest weiter; Nur-Kontingente-Flow enthält bei vorhandener ID keine Keydatensatzanlage, kein Secret-Reveal und keine Keyrotation und aktiviert keine Route; dokumentierte notwendige PAT-Ausgabe/-Erneuerung bleibt zulässig.
- Fehlende ID erzeugt keinen Key, kein Loginloop und keinen Accountzwilling; wechselndes Browserdefault überschreibt nicht das gebundene Projekt.
- Key-ID kann niemals als Modellsecret akzeptiert werden; falsche/abgelaufene PATs bleiben wirkungslos; alte 401 löscht keine neuere Generation.
- Parallele Modell-/Quotaabrufe teilen gültigen PAT und einen Renewalvorgang; Team/CN-UI bleibt bis gesondertem Go unqualifiziert.

## AC-07.3 – Codex: vorhandenen Browserlogin erhalten, Standalone separat

**Geplantes Ergebnis und Erhalt**

- Bestehende `CodexOwnedLoginAdapter`/`HttpCodexOwnedLoginBackend` und Core-PKCE bleiben eigener Browserlogin; keine neue Client-ID-, Key- oder CLI-Pflicht.
- Bestehender Core bleibt Owner seiner Owned-Credentials; heutiger Monitor liest aktuellen Access-Token plus Account-ID direkt für WHAM.
- AC-01/AC-02 entfernen automatische Routenerzeugung beim reinen Verbinden; bisherige Routen und aktive Core-Refreshfähigkeit bleiben erhalten.
- Externe `codex-access-snapshot` bleiben nicht erneuerbar: kein Refresh-/Revokezugriff, keine Ownership durch Kopieren in Owned-Verzeichnis; Ablauf verlangt Reconnect/Reimport.

**Offene Ownerentscheidung und kleinster entscheidender Versuch**

- Bestehender Core refresht nur aktive angewandte Owned-Bindungen; eine Dummyroute oder Lockerung der Snapshotgates löst Standalone nicht.
- A: neuer eigener routeunabhängiger Hostowner mit ausdrücklich geklärter exklusiver Übergabe; B: isolierter offizieller app-server als Owner eines eigenen Monitorgrants.
- Empfehlung: B zunächst für einen neuen Standalone-Monitor qualifizieren; bestehendes Core-Refreshmaterial niemals kopieren oder gleichzeitig durch B erneuern.
- Versuch: gepinnter isolierter app-server, neues eigenes Profil, managed Browserlogin, `account/rateLimits/read`, Prozessneustart und ein Renewal ohne Proxy-Core/Route.
- Go: stabile Account-/Scopebindung, echte Limits, erneuerbarer isolierter Owner und gezieltes lokales Trennen; normale CLI-/Extensionsession bleibt unberührt.
- Abbruch: nötige fremde Authcacheübernahme, keine Profilisolierung/Erneuerung oder nicht trennbare Logoutwirkung; alten Browserlogin erhalten, Standalone als noch unqualifiziert ausweisen.
- Vor Übernahme auf bestehenden Account Entscheidung treffen: getrennten Grant als zusätzliche Monitorreferenz binden oder exklusiv migrieren; kein stiller Ownerwechsel.
- A bleibt Alternative, wenn B ungeeignet ist; Ownership-Handoff, Persistenz und Authzulässigkeit sind dann eigener Go/No-Go-Nachweis vor Umsetzung.
- SIWC-OSS-Preview ist separate Audience/dynamische Registrierung/Responses-Inferenz und **kein belegter WHAM- oder app-server-Monitorzugang**; in diesem Paket kein Ersatzlogin.

**Transport, Dateien, Übergaben und Abnahme**

- Inferenz bleibt bestehender Core-Codextransport; heutiger WHAM-Reader bleibt eigener Zugang. B nutzt dokumentiertes Rate-Limit-RPC und eigenen Lifecycle, nicht denselben Refreshsecret.
- Neue Dateien bei B-Go: `Accounts/Managed/CodexAppServerOwnerAdapter.cs`, `Quota/CodexAppServerQuotaAdapter.cs`; bei A-Go stattdessen `Accounts/Managed/CodexStandaloneOwner.cs` unter `src/Lain.Host/`.
- Bestehende Codex-Dateien nur gezielt integrieren; keine generische OAuth-Neuschreibung. Request an AC-00/Root: app-server-Prozess-/Buildlieferung optional und unabhängig vom Proxy-Core sowie `Program.cs`/Contracts-Verdrahtung.
- Request an AC-01/Accountbasis: Monitoraccount ohne Routes/Core, unveränderliche Snapshot-/Ownedgrenze, Grants mit eindeutigen Ownerreferenzen und klare Migration/Detachzustände.
- Abnahme: bestehender Browserconnect/aktive Inferenz unverändert; neuer Monitor ohne Core/Route über Neustart/Ablauf nutzbar; Snapshotablauf führt zu Reconnect statt Refresh.
- Reconnect erhält Name/ID/History; falsche Account-ID ersetzt nichts; Abbruch/Detach verhindert späte Adoption; ein Owner je Refreshcredential, keine normale `codex logout`-Ausführung.
