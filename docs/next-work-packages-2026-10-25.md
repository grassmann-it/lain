# Aktueller Stand und nächste Arbeitspakete

**Folgeauftrag:** Der Nutzer hat die Umsetzung als Goal mit parallelen Workern
beauftragt. Durchführung und Nachweise stehen im
[Ausführungsbericht](next-work-packages-execution-2026-10-25.md).
Die folgenden Planungsaussagen beschreiben den Stand vor dieser Freigabe.

Stand: **25.10.2026**, Quellenabgleich auf Main
`af69241283c1e549a103aca188d27114bad0aade`. Vier lesende Bereichsaudits haben
Provider/Connect, Desktop, Routing/Betrieb und erhaltene Worktrees abgeglichen.
In dieser Runde keine Builds, Produktänderungen, Installation oder Provideraufrufe.
Die Pakete unten sind eine konkrete nächste Übergabe, noch kein gestarteter
Implementierungsauftrag. Ihre Prüfungen sind geplant.

## Was fertig ist und woran gerade gearbeitet wird

Die letzte Lieferwelle ist beendet. Aktuell läuft keine neue Produktimplementierung.
Maßgeblich ist der [Lieferbericht zu Modellgruppen und CLI](codex-model-groups-delivery-2026-10-25.md):

| Bereich | Tatsächlich geliefert | Grenze |
| --- | --- | --- |
| Lokaler Betrieb | Host/CLI `13519ef`, Desktop `67fb26c`, Core `191f4f17…`; bereit bei Revision 54/54 | Quellen-HEAD und installierte Komponenten sind verschiedene Stände. Kein allgemeines öffentliches Release. |
| Bestand | 15 Accounts, 58 ursprüngliche Routen, drei ursprüngliche Profile erhalten | Frühere Liveproben qualifizieren nicht jedes Modell oder jede neue Loginmethode. |
| Codex `/model` | Im separaten lain-Profil: Anbieter-/Accountgruppen, eigene Gruppen Free/IT Sec/1M, 57 manuelle Modelle, 16 qualifizierte Automatikziele | Gepatchter, gepinnter Codex 0.164.0. Grafische Gruppenpflege fehlt. Free hat vier manuelle Modelle, keine aktivierte Automatik. |
| Routing | Modellreihenfolge, feste Route und Accountbindung pro Gespräch; CLI-CRUD für Collections/Router | Kein Fehlerfallback. Zweiter Anbieteraccount kann einen bisher eindeutigen Anbieterrouter absichtlich ungültig machen. |
| UI und CLI | Accounts-Einstieg, Monitor/Gateway, Detailstufen, Settings/Tray, kompakte CLI-Tabelle und gemeinsame Quota-Hierarchie | UXD-17–22 sind installiert. Breite native Bedien-/Modusabnahme und Nutzerbewertung sind damit nicht vollständig erledigt. |
| Providerzugänge | Vorhandene Reader, Account-/Credentialbasis und mehrere vorbereitete Loginwege | Implementierung, freigegebene Methode und erfolgreicher realer Abruf sind getrennte Zustände. MiMo und Cloudflare haben konkrete offene Blocker. |

Die Desktopquellen sind seit dem installierten `67fb26c` unverändert. Für die
hier geprüften MiMo-/Cloudflare-/Synthetic-/OpenRouter-/Kimi-/Requestydateien ist
der Diff zum Hostpin `13519ef` leer. Das ist ein Quellenabgleich, keine neue
Laufzeit- oder Anbieterprüfung. Weitere Lieferbelege:
[Kontingente](quota-state-delivery-2026-10-25.md),
[UXD-17–22](ux-state-implementation-2026-10-25.md),
[kumulative Lieferung](cumulative-implementation-2026-10-25.md),
[Quota-Hierarchie](quota-display-delivery-2026-10-25.md).

**Erhaltenes WIP:** In den 15 beim Desktopcleanup gesicherten Worktrees steckt
ein kleiner noch nicht integrierter Zusatz zur V4-Fehlerdiagnose
(`protected-state-delta.json`, gehashte Erhaltungsdiagnose, optionales Behalten
fehlgeschlagener synthetischer Fixtures). N07 prüft ihn gezielt. 38 Researchdateien
sind bereits bytegleich übernommen; alte Lockfiles und der frühe Desktopprototyp
sind kein nachgewiesener fehlender Funktionsumfang und werden nicht insgesamt
zurückgemergt. Die Sicherungen bleiben bis zu einer eigenen Aufräumentscheidung erhalten.

## Verbindliche Grenzen für die nächste Umsetzung

- Accounts, Credentials, stabile IDs, Routen, Profile und History erhalten.
  Bestehenden Proxy und normale Codexkonfiguration unangetastet lassen.
- Nur das **lain-Profil** erweitern. Eigene Modellgruppen werden gemäß ausdrücklicher
  Nutzerentscheidung dieser Runde **in Profiles** gepflegt; Anbietergruppen
  entstehen automatisch. Kein zusätzlicher Hauptnavigationspunkt.
- Speichern, globales Apply und Clientinstallation bleiben eigene bewusste Schritte.
  Konfigurationskonflikte behalten HTTP 409 und den vorhandenen Draft.
- Kontingente/Resets zuerst; unbekannt bleibt unbekannt. Credits, Usage,
  konfigurierte Limits und beobachtete Raten nicht zu vermeintlichem Rest addieren.
  Kurze Fenster prominent, Monat/Zusatzcredits in Details. `usage` bleibt lesend;
  `--full` und JSON behalten ihre vollständigen Daten.
- Neue Logins nur über konkret qualifizierte Methoden am bestehenden Account.
  Kein pauschales „OAuth liefert Usage“, keine Käufe oder bezahlten Fallbacks.
  Gespeicherter Zugang, verifizierte Identität und erfolgreicher Reportingread
  werden einzeln ausgewiesen.
- Bestätigte Layouts und Navigation weiterverwenden: Profiles direkt; Live in
  Simple direkt; ein Modusdropdown unten rechts; Draftwechsel mit Rückweg und
  Verwerfen. Keine neue Gesamtgestaltung als Nebenwirkung dieser Pakete.
- Maximal zwölf aktive Agenten einschließlich Integrator. Eine begrenzte serielle
  Build-/Runtime-Lane, GUI ausschließlich auf privatem Xvfb/D-Bus.
  Temporäres unter `~/.cache/lain-build-tmp`; unnötige Buildausgaben löschen.
  Neue Worktrees außerhalb des Desktops; keine neuen vollständigen Codekopien dort.

## Pakete und empfohlene Reihenfolge

Priorität ist eine Empfehlung aus den wiederholt genannten Nutzerproblemen.
N03/N04 betreffen die blockierten echten Monitorwege; N01/N02 liefern parallel
kleine sichtbare Verbesserungen. N05/N06 schließen die neue Gruppenfunktion in
der Oberfläche ab. N07/N08 bereiten wartbare weitere Lieferungen vor.

| ID | Ergebnis | Abhängigkeit / Übergabereife |
| --- | --- | --- |
| N01 | Quota-Fehler ohne Tooltipsuche; kurze verständliche Accountdarstellung | Lokal umsetzbar auf bestehenden Fakten; gemeinsame Namensdarstellung als unten benannte Empfehlung |
| N02 | Korrekte Synthetic-Regeneration und OpenRouter-Free-Tagesgrenze | Zwei unabhängig prüfbare Readerkorrekturen, lokal umsetzbar |
| N03 | MiMo-Browserblocker behoben; Individual-Monitor tatsächlich qualifiziert | Lokaler Fix umsetzbar; echter Login braucht Nutzerinteraktion und getrennten Livebeleg |
| N04 | Cloudflare nennt die konkrete Reportinggrenze und liest erlaubte Analytics | Diagnose umsetzbar; realer Erfolg abhängig von Account, Rechten und Dataset |
| N05 | Desktop-Codexsetup exportiert Gruppen und bietet den passenden Startweg | Vorhandenes Backend/CLI verwenden; Paketdateien mit N06 nacheinander |
| N06 | Eigene Gruppen und Accountrouter in Profiles bearbeiten | N05/shared Clientanschlüsse zuerst; kein neues Routingbackend |
| N07 | Gemischte Komponenten aus dem Repository sicher und wiederholbar ausliefern | Heutige Core-/Clientpins beibehalten; private Operatorannahmen entfernen |
| N08 | Gepatchten lain-Codex ohne Bindung an veralteten Originalfallback pflegen | Versions-/Updatepolitik vor produktivem Versionswechsel klären |
| N09 | Begrenzte integrierte native und reale Abnahme der ausgewählten Lieferung | Prüfkatalog sofort; Abschluss nach den tatsächlich ausgewählten Fachpaketen |

### N01 — Quota-Gründe und Accountbezeichnungen

**Ist:** `MainWindow.Accounts.cs` führt den accountlokalen Refreshfehler bislang
im Tooltip; eine Kachel ohne Snapshot bleibt generisch. Supporttext,
Snapshotfehler, Refresh-/Setup-/Consoleaktionen existieren bereits. Neue Accounts
erhalten kurze Defaults über `AccountNameSuggestions`; die letzte CLI-Lieferung
hat bereits kompakte Anzeigenamen. Das ist kein fehlender Diagnosedialog und
kein Anlass, Bestandsnamen automatisch umzuschreiben.

**Änderung:** Den bekannten accountlokalen Fehler im Inspector lesbar zeigen,
auch ohne Snapshot. Ursachen und nächste Aktion aus vorhandenen Fakten wählen;
Timeout nicht in Loginbedarf und unbekanntes 403 nicht in „Pro nötig“ umdeuten.
**Darstellungsempfehlung:** dieselbe bestehende CLI-Regel für kurze bekannte
Standard-/Importlabels auch im Desktop nutzen. Eigene Namen erhalten und in
Details zeigen; Mehraccounts immer eindeutig. Kein neues persistentes ShortNamefeld
und keine Namensmigration als Voraussetzung. Falls eine neue Namenspolitik nötig
wird, diesen Teil getrennt klären; der Fehlerfix bleibt unabhängig ausführbar.

**Owner/Dateien:** Clientdarstellung mit `ProviderDisplayNames.cs`,
`AccountNameSuggestions.cs`, CLI-Labelregeln; Desktop
`MainWindow.Accounts.cs`, `MainWindow.AccountTiles.cs`, `MainWindow.Budgets.cs`,
`MainWindow.Quota.cs`, `MainWindow.Monitor.cs`. Hostursachen kommen aus N03/N04;
keine zweite Providerdiagnose im Frontend.

**Fertig:** Ohne Snapshot ist der tatsächliche Refreshfehler sichtbar. Ein
Timeout bietet keinen unnötigen Reconnect; frühere Messwerte bleiben mit Alter
erhalten. Einzel-/Mehraccount, eigener Name, bekanntes Standardlabel, Filter und
Reimport bleiben eindeutig derselben ID zugeordnet. CLI/Desktop zeigen dieselbe
Messbedeutung. Kein impliziter Provideraufruf, JSON und gespeicherte Namen unverändert.

### N02 — Zwei belegte Readerkorrekturen

**Ist/Änderung:** In `src/Lain.Host/Quota/SyntheticQuotaAdapter.cs` wird
`rollingFiveHourLimit.tickPercent` ungeändert als Prozent übernommen; die geprüfte
Herstellerquelle beschreibt eine Ratio. Ratio skalieren, vorhandenes
`nextRegenPercent` im Wochenblock weiterhin als Prozent behandeln. In
`OpenRouterQuotaAdapter.cs` fehlt der belegte UTC-Mitternachtsreset des expliziten
Free-Tageszählers. Beobachtungszeit mit dem Tagesfenster verbinden; abgelaufene
Beobachtungen nicht als aktuellen Rest behandeln. Accountweiter Free-Zähler und
USD-Keycap bleiben getrennt; unbekannte Accountidentitäten nicht zusammenführen.

**Owner:** je Reader isoliert; gemeinsame Quota-Zeit-/Scopeverträge beim Integrator.
Quellen: [Synthetic](provider-monitoring-review-2026-10-25/subscriptions.md),
[OpenRouter](provider-monitoring-review-2026-10-25/kilo-openrouter.md).

**Fertig:** `tickPercent: 0.02` wird 2 %, `nextRegenPercent: 2` bleibt 2 %;
Legacy-/moderne/koexistierende Blöcke funktionieren. OpenRouter zeigt bei frischer
Free-Beobachtung den nächsten UTC-Tageswechsel, auch über lokalen DST-Wechsel;
nach Ablauf bleibt der aktuelle Rest unbekannt, bis neu gemessen wurde.
Fehlend, null, echte Null und ungültige Werte bleiben unterscheidbar. Dieselben
Resultate sind in GUI, CLI und JSON nachvollziehbar. Fake-HTTP/Clock-Prüfungen
reichen für den Codefix; keine Inferenz zum Erzeugen eines Monitorwerts.

### N03 — MiMo Individual-Monitor abschließen

**Ist:** Eigener Chromium-/Consoleweg ist implementiert; die reale Probe brach
in `OpenAsync` vor abgeschlossener Browseröffnung ab. Ursache unbekannt.
[Methodenvertrag](account-connect-qualification/mimo.md) und Grenzen erhalten.

**Änderung/Owner:** `Accounts/ConsoleConnections/MiMo/*` samt eigenen Tests.
Startfehler reproduzierbar aufschlüsseln, lokalen Startpfad beheben und nur den
vorhandenen CN/EU-Individual-Weg qualifizieren. Account-/Credentiallifecycle nicht
neu bauen; Team, SGP und PAYG nicht nebenbei aktivieren.

**Fertig, lokal:** Eigenes privates Profil öffnet; Startfehler nennt einen
bereinigten überprüfbaren Grund. Cancel/Timeout/Fehlstart/Reconnect hinterlassen
keine unzugeordneten Prozesse/Profile. Hostneustart startet keinen Browser still neu.
**Fertig, real:** Nutzerlogin/MFA, gebundene UID+Region und erfolgreicher
Individual-Read mit Consolevergleich separat belegt. Ohne diese Interaktion
bleibt der reale Gate ausdrücklich offen. Inferenzkey, Account-ID und Routen
bleiben unverändert; keine Browsercookies aus fremden Profilen.

### N04 — Cloudflare-Analyticszugang qualifizieren

**Ist:** `CloudflareWorkersAiReader.cs` versucht bereits accountgebundene
Analytics; der aktuelle Fehler unterscheidet Dataset und Berechtigung nicht.
[Quellen-/Rechtegrenzen](provider-monitoring-review-2026-10-25/google-cloudflare.md).

**Änderung/Owner:** Cloudflare-Reader und dessen vorhandener Monitoranschluss.
Belegte Fehlersignale für falsche Accountbindung, fehlendes Analyticsrecht,
nicht verfügbares Dataset und erfolgreiche leere Antwort getrennt behandeln.
Wo die Antwort keine Unterscheidung erlaubt, Unsicherheit genau benennen und
einen gezielten Prüfschritt geben. Keine neue OAuth-/Wranglerlösung vorschalten,
bevor Scope, Account-/Datasetzugang und Speicherisolation nachgewiesen sind.

**Fertig:** Getrennte Fakefälle erreichen GUI/CLI mit passendem Grund. Ein
autorisierter echter Read liefert entweder die erlaubten Werte mit Ressource,
Zeitraum und Quelle oder einen belegten fehlenden Zugriff. Verzögerte Analytics
sind keine Live-Restquota. Keine Daten ergibt keine Nullauslastung; Rechteproblem
führt nicht zu wiederholten Logins. Nichtverfügbarkeit darf ein ehrlicher
Methodenabschluss sein, darf aber nicht als erfolgreiche Quotaunterstützung gelten.

### N05 — Desktop-Codexsetup an die gelieferte Funktion anschließen

**Ist:** `MainWindow.Profiles.cs` exportiert ohne `enableLainModelPicker` und
zeigt nach Installation weiterhin den privaten `CODEX_HOME`-Startweg. Client/API
unterstützen Gruppenexport bereits. Der installierte Dispatcher funktioniert
für den interaktiven Start mit `--profile lain`.

**Vorgehen:** In Profiles den gruppierten Export ausdrücklich auswählbar machen.
Prepare zeigt Profil, Revision, Gruppenumfang, Ziel und benötigten Client, ohne
Dateien zu installieren oder Apply auszulösen. **Empfehlung:** zwei klar bezeichnete
Ziele weiter unterstützen: separate lain-Profildateien für den gelieferten Startweg
und bisherigen privaten Ordner. Nur beim passenden geprüften Ziel
`codex --no-daemon --profile lain` anzeigen. Fehlender/geänderter Dispatcher wird
als Voraussetzung erklärt; Vorbereitung installiert keinen Client und ersetzt
keine fremde Startdatei. Neue automatische Dispatcherinstallation ist nicht Teil
dieses Pakets.

**Owner:** Profiles/Clientsetup, `MainWindow.Profiles.cs` und gezielte Fixtures;
gemeinsame lokale Installationslogik aus `CodexArtifactInstaller` weiterverwenden.
Integrator besitzt `LainApiClient.cs`, gemeinsame Verträge und Builddateien.

**Fertig:** Prepare ist dateifrei; Cancel und Ziel-/Profilwechsel verwerfen keine
anderen Drafts und installieren nichts. Eine alte Revision beim Export liefert
HTTP 409. Explizite Installation verwendet genau die vorbereiteten Artefakte
samt Generation und Revision. Bei bereits erkanntem Revisionswechsel ist erneut
vorzubereiten; eine erst später erkannte Abweichung wird als veraltet angezeigt,
nicht als aktueller Applystand. Keine atomare Revisionsgarantie über Host und
lokale Dateien voraussetzen. Im nachgewiesenen
Ziel zeigt der echte `/model`-Pfad die Gruppen. Gewöhnlicher Codex bleibt erhalten.
Nicht qualifizierte Free-Automatik bleibt deaktiviert. Die Wahl der zwei Ziele
ist der benannte Umsetzungsvorschlag; ein Ersatz des bisherigen Installationsmodells
wäre vor einer solchen Änderung gesondert zu entscheiden.

### N06 — Gruppen und Accountrouter in Profiles verwalten

**Bestätigt:** In dieser Runde hat der Nutzer **Profiles** als Ort für eigene
Gruppen und `/model`-Konfiguration gewählt. Automatische Anbietergruppen bleiben
abgeleitet. Die APIs unter `/collections`, `/collection-routers` und
`/model-groups` existieren; keine neue Routingengine.

**Änderung/Owner:** Derselbe Profilesowner nach N05. Bestehende Route-IDs zuordnen,
Gruppen-/Modellreihenfolge sichtbar bearbeiten, Namen und vorhandene
Autorouterkonfiguration pflegen. Für die Reihenfolge ganzer Collections fehlt
noch ein Schreibvertrag: Der Integrator ergänzt eine kleine revisionierte
Reorderoperation in `ModelsService`, `ModelEndpoints` und dem typisierten Client.
Sie akzeptiert nur eine vollständige eindeutige Permutation der bestehenden
Collection-IDs und ändert weder Mitgliedschaften noch Routerreferenzen. Alte
Revision: HTTP 409; doppelte/fremde/fehlende IDs: keine Änderung.
Normale Buttons für Reihenfolge genügen;
Drag-and-drop ist keine Voraussetzung. Rechtsklick, sichtbare Aktionen und
Tastatur nutzen denselben Command. Bei globalen Collections ausdrücklich zeigen,
dass ihre Änderung auch andere verwendende Profile betreffen kann. Mitgliedschaft
wird durch Zuordnen/Entfernen gesteuert. Eine globale `Route.Visible`-Änderung darf
nicht als lokales Verbergen eines Gruppenmitglieds angeboten werden. Neues
gruppen- oder profilspezifisches Verbergen ist kein Teil dieses ersten Anschlusses;
dafür fehlen heute ein Vertrag und eine Entscheidung über den Geltungsbereich.

Der Zweitaccountfall zeigt betroffene Anbieterrouter und bietet ausdrücklich
gewählte Accountziele wie Codex (Privat)/(Work). Keine stille Vermischung. Backend
liefert bei Bedarf einen bereinigten konkreten Fehlergrund statt nur generischem
„invalid transport“; gemeinsame Endpointänderungen gehören dem Integrator.
[Geltender Routervertrag](collection-router-v1.md).

**Fertig:** Gruppe mit überlappenden Mitgliedern anlegen, Gruppen und Mitglieder
umordnen, speichern, neu laden und nach eigenem Apply/Export im nativen Menü in
derselben Reihenfolge wiederfinden. IDs/Usage werden nicht
dupliziert. Veraltete Revision, gelöschtes/deaktiviertes Mitglied, fremdes Profil,
Mehraccount-/Poolfall und fehlender Router sind verständlich und sicher.
Konfigurationsdraft bleibt bei Navigation erhalten. Anbieterautomatik folgt
weiter der gespeicherten Modellreihenfolge und Gesprächsbindung. Free erklärt
seine fehlenden Voraussetzungen; dieses Paket ändert keine Keycaps oder Kostenregeln.

### N07 — Wiederholbare Komponentenlieferung aus dem Repository

**Ist:** Mixed-Paket, exakte Komponentenpins, Erhaltung und eigenes Update sind
lokal nachgewiesen. Aktivierung und Teile der Orchestrierung liegen noch in
privaten Operatoren. Ein falscher Vorgänger wurde real vor Dienststop verweigert.
Grundlagen: [Linuxlieferung](linux-delivery-stabilization.md),
[Host/CLI/Core-Refresh](host-cli-core-refresh.md), aktueller Lieferbericht oben.

**Änderung/Owner:** Deliveryowner mit exklusiver Skript-/Paketgrenze vom Integrator.
Relevante private Operatorlogik als prüfbaren versionierten Ablauf übernehmen,
ohne Nutzerpfade oder Secrets zu kopieren. Komponenten, Baseline, Herkunft und
Evidence ausdrücklich als Eingaben; Planlauf ohne Mutation. Den erhaltenen
V4-Diagnosezusatz einzeln reviewen, nur bei echtem Nutzen übernehmen.

**Fertig:** Ein anderer sauberer Checkout kann das gleiche Komponentenpaket mit
expliziten Eingaben zusammenstellen und prüfen. Keine persönliche Cachedatei als
einzige erforderliche Quelle. Falsche Baseline/Tamper werden vor Stop erkannt;
Updatefehler behalten ihre Ursache. Exakte Paketprüfung, gestoppte vollständige
Sicherung, synthetischer Writefault und Rückkehr erhalten Identität/History.
Frische Monitor- und Gatewayinstallation getrennt nachweisen. Automatisches
Lingering, echtes Downgrade rotierender Grants, öffentliche Veröffentlichung und
bitgleicher unabhängiger Gesamtneubau werden daraus nicht behauptet.

### N08 — Versionspflege des lain-Codex

**Ist:** Patch und Dispatcher sind auf Codex 0.164.0 gepinnt. Der normale
Originalfallback ist an den damals aufgelösten Releasepfad gebunden.
[Clientvertrag](../clients/codex/README.md),
[Dispatchergrenzen](../clients/codex/dispatcher.md).

**Vorlauf/Empfehlung:** Nur ausdrücklich qualifizierte Versionen unterstützen;
geänderte Originalinstallation erkennen, verständlich melden und einen bewussten
Rebind/Update anbieten. Kein automatisches Nachbauen oder Austauschen eines
unbekannten Upstreams. Zielversion für einen echten Versionssprung erst nach
Prüfung des dann aktuellen Releases festlegen; keine erfundene Versionsnummer.
Diese Politik ist ein Vorschlag und vor produktiver Updatefunktion festzuhalten.

**Owner:** `clients/codex/*`, Prepare-/Buildskripte, `scripts/clients/*` und
eigene Tests. Paket-/Desktopanschlüsse gehen an N07/N05.

**Fertig:** Wechsel des originalen Releasepfads, fehlender Fallback, veränderte
CLI-Grammatik und Rücknahme sind mit klaren Ergebnissen geprüft. Nur eindeutig
interaktives `--profile lain` benutzt den Patch; andere Profile/Subcommands bleiben
beim gewählten Original. Gepinnte Quelle/Toolchain/Locks und realer Menüablauf
bleiben nachweisbar. Der frühere 3-GiB-OOM ist kein bestandener Volltest;
acht ungestartete TUI-Libtests werden weder als Pass noch pauschal als neuer
Releaseblocker gezählt. Zusätzliche Builds nur im begrenzten seriellen Slot.

### N09 — Begrenzte integrierte und reale Abnahme

**Owner:** unabhängiger Reviewer; Seitenowner beheben konkrete Befunde.
Vorhandene UXD-/CLI-/Providerfixtures wiederverwenden, nicht UXD-17–22 neu bauen.

**Geplante Abschlussfälle:**

1. Accounts/Quota bei 1120×780, 800×560 und dem erlaubten Monitorfenster 640×420;
   100/125 %, Barebones/Simple/Advanced, Tastatur/Fokus und lange Namen. Modi
   dürfen bestätigte gemeinsame Navigation haben; sie müssen sich nicht durch
   künstlich verschiedene Menüs unterscheiden. Fehlendes Reporting bleibt erklärbar.
2. Physische Modellreihenfolge, Revision/Locks, Prepare-Cancel/Zielwechsel und
   N05/N06-Gruppenpfad; CLI bei schmaler/breiter TTY, Pipe und `NO_COLOR` mit
   demselben Snapshot. Prüfbilder aus privatem Xvfb; konkrete Defekte statt
   pauschalem neuen Layoutauftrag melden.
3. Im installierten lain-Profil eine kleine echte Aufgabe über einen bestehenden
   qualifizierten Autorouter mit zwei Gesprächsschritten: Ausgabe, Inspector,
   Route/Account, feste Bindung und persistente Usage stimmen überein. Schließen
   der UI unterbricht den Host nicht. Getrennt protokollieren, welche Methoden
   und Modelle tatsächlich geprüft wurden; keine Vollabdeckung aus alten 22 Calls.
4. OpenRouter ist der nächste vorbereitete öffentliche Browserdurchstich ohne
   neue Maintainerregistrierung: nach bewusster Nutzerzustimmung/Anmeldung
   Keybootstrap und anschließenden Read am richtigen Account nachweisen.
   Eine mögliche neue Keyerzeugung ist ein sichtbarer Teil dieses Flows, kein
   stiller Nebeneffekt der Abnahme. Bereits vorhandenen Key nach Möglichkeit
   weiterverwenden. MiMo/Cloudflare-Livegates bleiben eigene Ergebnisse.

**Fertig:** Ein Bericht nennt Source-/Binarypins, lokal geprüft, installiert,
real geprüft, offene Methoden und konkrete Nutzerbeobachtung getrennt. Keine
unnötige Wiederholung großer Tests, keine Lasttests an echten Providern.
Eine weitere Installation oder reale Credentialänderung folgt dem dann
ausdrücklich beauftragten Umfang; dieser Plan startet sie nicht.

## Was vorerst offen bleibt und was es blockiert

| Punkt | Nächste Entscheidung oder begrenzter Schritt | Blockiert |
| --- | --- | --- |
| Vollständig unveränderter CLIProxyAPI | Präferenz des Nutzers bleibt erhalten. Der heutige externe v8-Core benötigt explizite Hooks für zugesagte Dauerhaftigkeit/Bindung; siehe [Patchgrenze](../proxy/README.md). Empfehlung: bestehende begrenzte Patches weiterführen, Upstreamanschlüsse einzeln anbieten. Kein stilles Aufgeben der Garantien. | Wechsel auf offizielles unverändertes Binary; keine der obigen Quota-/GUIarbeiten |
| Free-Automatik | Manuelle Free-Auswahl funktioniert. Aktivierung erst mit qualifizierten effektiven `free_only`-Routen und frischem Same-Key-Zero-USD-Cap; aktueller gecachter OpenRouter-Cap 20 USD genügt nicht. Eigene Einrichtung nur bewusst beauftragen. | Nur Free-Automatik, nicht manuelle Free-Modelle/andere Router |
| Poe | Maintainerclient-/Releasecallbackfreigabe schließen. | Nur diesen Browserlogin, vorhandener Key bleibt nutzbar |
| Requesty/weitere Hersteller-CLIs | Je Methode gepinntes Artefakt, echte Speicher-/Keyringisolation, Identität, Refreshowner und Readvertrag qualifizieren; Requesty `--profile` isoliert den gemeinsamen Store nicht. | Jeweilige neue CLI-Anmeldung |
| Weitere Reportingquellen | SambaNova/Cohere/Upstage-Consoles, Step Plan, xAI-Billing, OpenCode-Tools usw. einzeln nach [25-Anbieter-Matrix](provider-monitoring-review-2026-10-25/README.md) auswählen. Herstellerlogin ist kein Quotanachweis. Kimi-Booster ist ein kleiner separater Readernachtrag mit präzisem Raw-Vertrag. | Nur jeweils zusätzliche Monitoringabdeckung |
| Öffentlicher Release | Produktlizenz, verbindliche Support-/Plattformmatrix und Veröffentlichungsumfang entscheiden; N07/N09 liefern technische Grundlage. | Öffentliche Releasefreigabe, nicht private Nutzung |
| Rollen/Classifier/Approver, automatische Quotaoptimierung, KI-Recherche/Skillbibliothek, weitere Harnesse | Nachgelagerte Vorhaben getrennt zuschneiden; kein impliziter Teil dieser Fertigstellungsrunde. | Nur jeweiliger Ausbau |

## Parallelisierung und Abschluss einer Lieferwelle

Bei beauftragter Umsetzung können N01, N02, N03, N04 und N07 unabhängig starten.
N05 und anschließend N06 haben **denselben Profilesowner**. N08 bearbeitet nur
seinen Clientbereich; N09 bereitet den Prüfkatalog parallel vor. Integrator besitzt
gemeinsame DTOs/Clientanschlüsse, `Program.cs`, Registry, Migrationen und
Builddateien. Providerowner ändern nur eigene Reader/Adapter; zentrale
Katalogfreigaben werden an den Integrator gegeben. Keine konkurrierenden Edits
an gemeinsamen UI-/CLIdateien oder gleichzeitigen großen Builds.

Eine erste kleine Lieferung darf N01/N02/N03/N04 abschließen, während gesperrte
Methoden explizit offen bleiben. N05/N06 können anschließend als eigener
Bedienablauf liefern. N07/N08 machen weitere Auslieferungen wartbar. N09 prüft
jeweils den tatsächlichen Umfang; ein offener Provider erzwingt weder erfundene
Unterstützung noch das Warten auf alle 25 Anbieter.

Die bestehende fremde Providerforschung und unversionierte Evidence bleiben
erhalten. Diese Planrunde erstellt keinen Commit/Push. Für frische Worktrees
oder einen neuen Implementierer die unversionierten Pläne und benötigten
Researchdateien ausdrücklich mitgeben; sie sind ohne diese Übergabe nicht in
einem frischen Checkout vorhanden.

Planprüfung dieser Runde: vier lesende Bereichsaudits, anschließend unabhängiger
Backend-/Übergabereview. Dessen drei Präzisierungen sind eingearbeitet:
Collection-Reordervertrag, klarer Geltungsbereich von Mitgliedschaft/Sichtbarkeit
und getrennte Revisionsaussagen für Hostexport und lokale Installation.
67 lokale Dokumentlinks über acht berührte Einstiegs-/Plandateien auflösbar;
Whitespaceprüfung ohne Befund. Keine Produktprüfung aus diesem Planreview ableiten.
