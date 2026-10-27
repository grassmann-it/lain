# Quota-Erklärungen und nächste Schritte

Review vom 25.10.2026, Quellstand `cbf083f`. Screenshot: `/tmp/codex-clipboard-SabJLv.png`; laut Auftrag installierte CLI `b49096f`, Desktop `67fb26c`. Quellbefund ist kein Nachweis, dass jeder Anschluss auf dem installierten Host verfügbar oder am Nutzerkonto erfolgreich ist.

**Ergebnis:** Die kompakte Ausgabe löscht heute die vorhandene Erklärung. Das ist die erste zu behebende Ursache. Anschließend müssen Grund und nächste Aktion zusammenpassen: „kein Anschluss in lain“, „anderer Lesezugang nötig“, „noch keine Beobachtung“ und „Abruf fehlgeschlagen“ sind vier unterschiedliche Situationen. Ein pauschaler Setup- oder Console-Hinweis hilft dabei nicht.

## Auftrag, Grundlage und Grenzen

Nutzervorgaben: kürzere, lesbare CLI; verständliches Warum; bestmöglicher belegter Onboardingweg; kein geratenes „Pro erforderlich“; unbekannte Werte, gemeinsame Budgets und unterschiedliche Messarten korrekt erhalten. Autorisiert sind Review, Mockups und Arbeitspakete. Es gab keine Produktänderungen, Builds, Tests, Hostabfragen, Anbieterrequests, Installation oder Commits.

Verbindliche bestehende Grundlagen: `docs/product-scope.md:38` verlangt benannte Fenster, Restmenge und unbekannte Werte; `docs/product-scope.md:166` verlangt die Trennung fehlender Unterstützung von Abruffehlern sowie Prüfung geeigneter OAuth-/Leserechte-/Managementzugänge. `docs/service-contracts.md:59` trennt Beobachtung, Frische und Scope; `:65` sagt ausdrücklich, dass `unsupported` kein Verbindungsfehler ist. Empfehlungen dieses Reviews sind noch keine angenommenen Architekturentscheidungen. Architektur-Sparring-Skill und vorhandener Projektindex wurden als Reviewkontext gelesen; keine Projektentscheidung wurde umgeschrieben.

## Befunde am tatsächlichen Code

| Befund | Quelle | Konsequenz |
| --- | --- | --- |
| Kompakt ersetzt `Support.Message` durch `Quota setup required`, `Quota: check provider console` usw.; URL/Handlung entfallen vollständig. | `src/Lain.Cli/CompactAccountUsage.cs:71–85` | Gute Erklärungen aus dem Host erreichen den Standardaufruf nicht. |
| Vollansicht zeigt Message und URL, empfiehlt bei **jedem** `stale`/`error` jedoch „Set up or reconnect“. | `src/Lain.Cli/HumanQuotas.cs:99–106` | Netzwerkfehler, Datenlücke oder alte Header werden als Zugangsproblem dargestellt. |
| Desktop bietet bei `setup-required` zunächst Refresh und bei jedem Fehler/alten Wert Setup an. | `src/Lain.Desktop/MainWindow.Monitor.cs:11–36` | Mehrere gleichrangige Aktionen ohne belegte Priorität; Wiederholen kann wirkungslos sein. |
| CLI-Wizard präsentiert eine Liste generischer Operationen; Refresh steht zuerst, sobald überhaupt eine Methode existiert. Lokales Speichern prüft den Zugang noch nicht. | `src/Lain.Cli/MonitorWizard.cs:23–44, 71–78` | Auch ohne gespeicherten Lesezugang wirkt Refresh wie der empfohlene Start. |
| Desktop enthält bereits verfügbare Methoden, Beschreibungen, Anforderungsansicht, Save sowie Save-and-refresh. | `src/Lain.Desktop/MainWindow.Monitor.cs:56–95, 157–174` | Bestehenden Ablauf gezielt priorisieren; kein neues Onboardingsystem nötig. |
| `QuotaSupportDto` hat nur Kind, Message, ActionUrl, CredentialPurpose. | `src/Lain.Core/Contracts.cs:30` | Ein einziges Kind mischt Kanal, Implementierungsstand und aktuellen Handlungsbedarf. Kein belastbarer kurzer Grund oder Aktionsbezug. |
| Katalog enthält Quellen, Abrechnungspläne, Restrictions, Monitoringoptionen. Connection-Methoden haben Availability, Purposes, Requirements/Guidance, Docs und Recommended. | `src/Lain.Core/ProviderContracts.cs`; `ConnectionContracts.cs:36–43`; `MonitorContracts.cs` | Umfangreiche vorhandene Basis; Availability ist ausdrücklich **kein** Kontoberechtigungsnachweis. |
| Synthetic behandelt eine erfolgreiche Antwort ohne erkanntes Quotafeld als Fehlertext. | `src/Lain.Host/Quota/SyntheticQuotaAdapter.cs:21–23` | Screenshot beweist weder fehlendes Abo noch Pro-Pflicht. Möglich sind Produkt-/Schema-/Kontounterschiede; diese Ungewissheit benennen. |
| Zahlreiche reine Supportzustände werden vom Host schon ohne Error ausgegeben. | `src/Lain.Host/Quota/QuotaService.cs:110–119, 148–152, 223–243` | Bestehende korrekte Unterscheidung erhalten; keine rote Fehlermeldung allein wegen fehlender automatischer Quelle. |

Zusätzlich: `CompactUsageNote` schneidet lange Hinweise nach 88 Zeichen ab (`CompactAccountUsage.cs:133`). Im Screenshot endet gerade die entscheidende Cloudflare-Handlung hinter „Account Anal…“. Deshalb kurze, separat formulierte Ursache und separate Handlung statt einer gekürzten Ausnahmezeichenkette.

## Was eine Erklärung wissen darf

Vier Ebenen getrennt halten. Das ist eine Präsentations- und Datenherkunftsregel, kein Auftrag für vier neue Dienste.

| Ebene | Belastbare Aussage | Unzulässiger Schluss |
| --- | --- | --- |
| Anbieterwissen, mit Quelle/Prüfdatum | „Diese Quelle liefert X; benötigt Y; erfasst Scope Z.“ | Dokumentation eines Produkts beweist nicht, dass dieses Konto es besitzt. |
| Installierter Anschluss | „Diese lain-Version kann Methode M hier anbieten; Voraussetzung V fehlt.“ | Ein verfügbarer Button beweist keinen erfolgreichen Login oder Tarif. |
| Kontozugang/Bindung | „Monitoringzugang fehlt/ist abgelaufen; Projekt-ID ist bekannt.“ | Inferenz funktioniert ⇒ Monitoring erlaubt; OAuth vorhanden ⇒ alle Daten erlaubt. |
| Beobachtung | „Letzte Antwort enthielt Wert X / keine Serie / verweigerte Zugriff.“ | Keine Serie ⇒ null Verbrauch; 403 ⇒ falscher Tarif; fehlendes Feld ⇒ kein Abo. |

Planvoraussetzung nur nennen, wenn die konkrete Reportingquelle sie offiziell belegt. Form: „Diese Quelle setzt [Produkt] voraus. Ob dieses Konto dafür freigeschaltet ist, ist nicht bekannt.“ Erst eine passend gebundene Anbieterantwort darf „Dieses Konto hat …“ begründen. Kein Upgrade-CTA ohne belegte Möglichkeit, dass es den gewünschten Wert tatsächlich erschließt.

CLI/Harness ebenfalls getrennt erklären: Ein Werkzeug kann Login/Erneuerung bereitstellen, lokale Nutzung erfassen oder Providerheader beobachten. Keine dieser Eigenschaften beweist allein ein aktuelles Gesamtbudget. „Mit CLI verbinden“ braucht ein konkretes offizielles Werkzeug, isolierten Loginpfad, Scope und erwartetes Ergebnis. Kein Import fremder Sitzungen oder Cookies als beiläufiger Setupschritt.

## Zustände und passende Handlung

Der Zustand bezieht sich auf **Monitoring**, nicht auf die Nutzbarkeit des Inferenzkontos. „Quota error“ neben dem Kontonamen ist dafür zu pauschal.

| Tatsächlicher Fall | Kurzer sichtbarer Grund | Erste Handlung / Grenze |
| --- | --- | --- |
| Kein geprüfter lain-Reader | „Automatischer Abruf in lain fehlt“ | Passende Console-/Dokumentationsseite. Kein Refresh/erneuter Login als Lösung. Nicht behaupten, der Anbieter habe keine API. |
| Existierender Reader, anderer Zugang fehlt | „Inferenzkey kann Monitoring nicht lesen“ | Konkrete verfügbare Monitoringmethode am bestehenden Konto starten. |
| Lokale Methode nicht verfügbar | „[Methode] benötigt [fehlende Voraussetzung]“ | Anforderungen zeigen. Maintainer-Clientregistrierung nicht dem Nutzer als Tokenproblem geben. |
| Zugang nachweislich abgelaufen | „Monitoringzugang abgelaufen“ | Genau diese Methode erneuern; bisherigen Wert mit Alter erhalten. |
| Gemeldeter Berechtigungs-/Datasetfehler ohne genauere Evidenz | „Analytics meldet einen Dataset-/Berechtigungsfehler“ | Richtigen Account/Scope und dokumentierte Berechtigung prüfen. Dataset und Berechtigung nicht willkürlich als Ursache auswählen. „Zugriff abgelehnt“ nur bei tatsächlich belegter Ablehnung. |
| Transport-/Serverfehler bei funktionierendem Anschluss | „Abruf fehlgeschlagen · letzter Wert [Alter]“ | Nochmals abrufen. Setup nur als zusätzliche Option, wenn andere Evidenz dafür spricht. |
| Erfolgreiche Antwort ohne exportierte Serie | „Quelle antwortet, meldet aber keine Werte“ | Erfassten Zeitraum/Projekt/Produkt erklären; keine rote Störung und kein Nullsaldo. |
| Antwort lässt sich nicht als erwartete Quota einordnen | „Antwort enthält keine erkennbare Quota“ | Ergebnis/Quelle prüfen; Kontoberechtigung unklar. Parser-/Vertragsproblem darf sichtbar bleiben, ohne erfundenen Abotarif. |
| Passive Quelle ohne Beobachtung | „Noch keine Limitheader beobachtet“ | Erklären, über welchen normalen Gatewaypfad Header entstehen. Refresh liest gespeicherte Daten und sendet keine Inferenz. Keine Testanfrage automatisch starten. |
| Nur Usage/Rate/Limit vorhanden | „Nutzung vorhanden · Restbudget unbekannt“ | Gemeldete Messart anzeigen; keine Setupstörung vortäuschen. |
| Unklarer Legacyzustand ohne hinreichende Metadaten | „Grund nicht näher bekannt“ | Originalnachricht vollständig in Details. Keine Ursache aus Providername oder Freitext raten. |

Bei `stale` zuerst den letzten bekannten Wert und Zeitpunkt zeigen, danach den **zusätzlichen** Grund, weshalb keine aktuelle Beobachtung vorliegt. „Stale“ allein ist keine Reparaturanleitung. `Reset due` ist kein Nachweis, dass ein neuer Zeitraum schon gelesen wurde.

## Adaptive Information: kurz heißt nicht inhaltsleer

Empfehlung für die Übersicht: weiterhin jeden Account auffindbar halten; je Account ohne Messwert **eine** Zeile aus Name + konkretem Grund + kurzem Aktionsverb. Nur neue oder tatsächlich blockierende Probleme erhalten eine Zusatzzeile. Vorhandene Messwerte behalten Fenster, Einheit, Restmenge und notwendige Frischehinweise. Unveränderte Supporterklärungen bekommen keine fünfzeilige Karte.

Die neun Accounts ohne Zahlen im Screenshot haben drei Arten von Bedarf: zwei Setupfälle, drei ungeklärte/fehlgeschlagene Reads, vier nicht angeschlossene Quellen. Als neue Übersichtskategorien sind „Einrichtung“, „Prüfen“ und „Kein automatischer Abruf“ hilfreicher als der eine Sammelblock. Diese Zuordnung ist eine Entwurfsanalyse des Screenshots, kein neu abgefragter Kontostand. Eine Kategorisierung darf Konten mit guten Werten **und** einem fehlenden Nebenfenster nicht aus der Werteübersicht entfernen.

Beispielauszug, englische Produktcopy, weder neue Kontoidentitäten noch neue Messergebnisse:

```text
Setup
  Xiaomi MiMo    Console sign-in needed        Connect monitoring
  Gemini        Inference key cannot monitor  Connect Google monitoring

Needs review
  Cloudflare    Dataset/permission error      Check account and permissions
  Kilo          Wallet balance unknown        Review last response
  Synthetic     No quota found in response    Review account/product scope

No automatic reading in lain
  NVIDIA Build  No verified account source    Provider reference
  Cohere        Reader not implemented        Usage console
  SambaNova     Reader not implemented        Usage console
  Upstage       Reader not implemented        Usage console
```

Die letzte Spalte ist im CLI-Entwurf eine Aktionsbeschreibung, kein behaupteter klickbarer Link. Auf schmalen Terminals gehört sie auf eine kurze eingerückte Zeile. Ein wiederholtes „Quota:“ und generische Fülltexte entfallen. Für CLI werden konkrete kopierbare Befehle nur für existierende Aktionen ausgegeben.

**Wichtige Commandgrenze:** Heute sind `lain usage --full` und `lain accounts monitor '<ACCOUNT_ID>'` echte Wege. Letzteres öffnet einen interaktiven Wizard mit auswählbaren Aktionen; es ist keine rein lesende Diagnose. Ein gefilterter Quota-Detailbefehl wäre eine neue Funktion. `lain usage --account ID` darf im Mockup nicht als vorhandene Quotaansicht erscheinen: dieser Filter fällt aktuell in die historische Usage-Semantik (`AccountUsage.cs:12`, `CliApplication.cs:258–265`). `lain quotas --account ID` ist derzeit ebenfalls nicht zulässig.

## Detailansicht: Warum, Ergebnis, ein nächster Schritt

Empfohlene feste Reihenfolge, jeweils adaptiv auf vorhandene Fakten gekürzt:

1. Erwartetes Ergebnis: Was genau wird lesbar? Restbudget, Wallet, Usage, Rate oder Limit; für welches Konto/Projekt/Modell und welchen Zeitraum?
2. Grund: Warum fehlen **hier** Werte? Konto-/Zugangsbeobachtung getrennt von allgemeiner Quellenbeschreibung.
3. Primärer Schritt: eine konkret benannte Methode oder Prüfung. Alternativen darunter.
4. Voraussetzungen vor dem Login: Tarif/Produkt nur belegt; zusätzlich Rechte, Ressource, lokale Werkzeuge und Lebensdauer des Zugangs.
5. Ergebnis nach Einrichtung: gespeichert / Identität bestätigt / Messwert gelesen sind getrennte Ergebnisse. „Verbunden“ garantiert noch keine exportierten Werte.
6. Quellen und vollständige Diagnostik ausklappbar bzw. in Vollansicht. Eine Support-URL darf Dokumentation sein; nicht jede `ActionUrl` als „Provider console“ beschriften.

Quellgestützte Textmockups; öffentliche Anbieterrecherche muss Produkt-/Tarifaussagen vor späterer Implementierung bestätigen:

```text
Gemini · Monitoring is not connected
Your inference API key cannot read Google Monitoring.
Available result: project/model usage and reported limits.
Remaining capacity may still be unknown.

Next: Connect Google monitoring for the same project.
  Google Cloud CLI: renewable access in a separate lain profile.
  Uses the CLI's broad standard Google grant; not read-only OAuth.
  Alternative: expiring Monitoring token; replace it after expiry.
```

Grundlage: `GeminiModelQuotaAdapter.cs:41`, `MonitorAccessCatalog.cs:34–38`, `ConnectionMethodCatalog.cs:71–75`. Die Empfehlung darf nicht allein aus `Recommended=true` kommen: verfügbarer Hostweg, erwartete Daten, Berechtigungsumfang und Erneuerungsaufwand bestimmen sie gemeinsam. Fehlt die qualifizierte CLI, Anforderungen zeigen; keine funktionierende Browser-OAuth-Methode behaupten. Browserweg ist nur bei qualifiziertem Maintainerclient verfügbar.

```text
Xiaomi MiMo · Console sign-in needed
The inference key cannot read console usage.
This lain connector reads Individual Token Plan credits
for a verified CN/EU identity. Your plan is not verified yet.

Next: Connect MiMo monitoring to this existing account.
Requires a desktop browser. Reconnect after its window closes,
the session expires, or lain restarts.
```

Grundlage: `MiMoQuotaAdapter.cs:26–29`, `ConnectionMethodCatalog.cs:76–80`. „Individual Token Plan“ beschreibt den Anschlussumfang; daraus folgt keine Aufforderung, ein Abo zu kaufen. Liegt konkrete Kontoevidenz vor, ersetzt sie „not verified yet“. Die im Projekt dokumentierte bisherige Browserstartstörung ist ein eigener lokaler Anschlussblocker, kein Tarifbeleg.

```text
Cloudflare · Analytics returned a dataset/permission error
The response does not identify the exact cause.
Account Analytics Read and dataset access are not yet confirmed.

Next: Check the saved account ID and analytics permission.
Use the existing token if it already has that permission.
Alternative: a separate token scoped to this account.
Available result: estimated UTC-day Workers AI neurons.
This does not establish an exact remaining allowance.
```

Grundlage: Screenshotdiagnostik, `CloudflareQuotaAdapter.cs:57–58` und `MonitorAccessCatalog.cs:40–50`. „Neu anmelden“ oder „Pro kaufen“ ist ohne weitere Evidenz keine passende Primäraktion. Der Adapter fasst GraphQL-Fehler zusammen; daraus ist nicht einmal eine bestimmte HTTP-Ablehnung abzulesen. Im echten UI den kürzesten **tatsächlich belegten** Fehler anzeigen; auch aus HTTP 403 allein keinen Tarif ableiten.

```text
NVIDIA Build · No verified account reporting source in lain
Account usage, remaining requests and reset time are unknown.
Published product limits are reference information, not a reading.

Next: Open the provider reference and account console.
No verified login or token change is known to unlock these values.
```

Grundlage: `NvidiaBuildPublicReference.cs:36–40`. Öffentlich beworbene Limits nicht zu Kontowerten, Balken oder verfügbaren Requests umformen. „Nicht verifiziert“ ist zeitgebundenes Wissen, keine Behauptung, NVIDIA habe generell keinen solchen Zugang.

## Bestehende Verträge nutzen, nur den fehlenden Teil ergänzen

**Sofort ohne Hostumbau möglich:** Kompakt die konkrete `Support.Message` wieder zugänglich machen; Handlungszeile aus vorhandener `ActionUrl`/Methode ableiten; allgemeines „reconnect“ bei jedem Fehler entfernen; Desktop/Wizard auf vorhandene `MonitorAccessOptions`, `ConnectionMethods`, `MonitorAccessDto`, Accountbindung und Capability ausrichten. Herkunft und Frische stehen schon im Quota-Datensatz. Katalogangaben dürfen keine Quota-Messwerte erzeugen. Den vorhandenen gemeinsamen Clientbereich für eine gemeinsame Erklärungspolitik erweitern, statt dieselbe Fallunterscheidung erneut in CLI und Desktop zu pflegen.

**Grenze:** Verlässlich kurze, lokalisierbare Gründe und aktionsgenaue Auswahl lassen sich nicht dauerhaft aus freien Fehlersätzen rekonstruieren. Das UI kennt aus `setup-required` allein nicht die fehlende Methode; aus `error` nicht die Ursache. Keine Stringsuche nach „permission“, „plan“ oder „expired“ als neue fachliche Wahrheit.

Kleine optionale Erweiterung als Empfehlung für den anschließenden Implementierungsentwurf: `QuotaSupportDto` behält alle bisherigen Felder und kann einen stabilen `ReasonCode`, eine kurze `Summary` und eine optionale `NextAction` mit Art/Label/Methoden-ID bzw. URL tragen. Die Aktion referenziert die bereits vorhandene Connection-Method-ID; sie dupliziert weder Secretfelder noch Loginlogik. Keine neue Quota-Entitlement-Engine, kein allgemeiner Workflowgraph und kein komplett neuer Providerkatalog sind nötig.

Reason nur am Host-/Adapterpunkt setzen, der ihn tatsächlich kennt. Zunächst wenige belegte Fälle abdecken; Rest bleibt `unknown` mit Originaldiagnose. Ein generischer „kein erkannter Wert“-Grund sagt nichts über ein Abonnement. Dynamische Voraussetzungen, zuletzt beobachteter Fehler und letzte erfolgreiche Werte bleiben unterschiedliche Fakten. Bei älteren Hosts ohne Zusatzfelder funktioniert die bisherige Message als ehrlicher Fallback.

Quellenherkunft braucht im ersten Schritt keine zusätzliche Laufzeitdatenbank: vorhandene `ProviderSourceDto`/Dokumentationslinks und datierte Researchdokumente nutzen. Für eine spätere in-app Aussage „Quelle geprüft am …“ muss der Bezug zur **konkreten** Methode/Behauptung eindeutig sein; ein beliebiger allgemeiner Providerlink genügt dafür nicht.

## Messsemantik und Farbe als harte Abnahmegrenzen

- Wallet-Balance, rollende Credits, Requests, Tokens, Usage-Schätzung, effektives Limit und Rate behalten native Einheit und Scope. Auch mehrere Prozentwerte sind nicht addierbar. `QuotaPresentation.HasReading` bedeutet „irgendeine Messung“, nicht „Restbudget bekannt“.
- Konto-/Poolidentität nicht mit „eigener Geldtopf“ gleichsetzen. `SharedWithLegacy` bedeutet dieselben Providerfonds; `BudgetPoolId` kann geteilte Mengen markieren. Nur belegte Beziehungen anzeigen; unbekannte Beziehungen nicht durch einen pauschalen Gesamtbetrag oder eine Anzahl „unabhängiger Budgets“ verstecken.
- Eine passive Headerbeobachtung ist zeitlich und nach Anfrage/Modell begrenzt. Sie kann eine echte gemeldete Grenze enthalten, deckt dadurch aber nicht automatisch die gesamte aktuelle Accountnutzung ab. Lokale History ersetzt keine Providerquota.
- Keine Werte ⇒ kein leerer Verbrauchsbalken und keine 0. Unbekannte Zusatzfenster bleiben auffindbar; echte Nullsalden und verbrauchte Hauptfenster bleiben sichtbar. Vorhandene `QuotaPresentation`-Regeln hierzu erhalten.
- Screenshot tatsächlich betrachten: Grundtext, Namen, Trennzeichen und viele Zahlen laufen im gleichen Cyan zusammen. ANSI-Default ist in dieser Nutzerpalette ebenfalls cyan; „einfach unbunt“ garantiert daher kein neutrales Weiß. Hierarchie muss durch Spalten, Abstand, sparsame Fettschrift und explizite Statuswörter ohne Farbe funktionieren.
- Entscheidende Gründe und nächste Aktionen nicht ANSI-dim (`2`) rendern. Orange/Rot nur auf die kleine Statusinformation, nicht auf ganze Diagnoseabsätze. Ein gesundes Budget braucht keinen grün gefärbten Volltext.
- Pixelstichprobe im Screenshot: Hintergrund `#1e2229`; häufige Text-/Glyphenfarben `#16a085` (~4,86:1), `#149678` (~4,30:1), `#008787` (~3,66:1), Grün `#44853a` (~3,54:1), Orange `#f67400` (~5,64:1). Das sind Screenshotwerte inklusive Antialiasing, keine formale Kontrastabnahme. Spätere Sichtprüfung muss genau diese Palette sowie `NO_COLOR`, schmale Breiten und Lesbarkeit der nicht hervorgehobenen Handlungen enthalten.

## Konkrete Arbeitspakete für die spätere Umsetzung

| Paket | Ergebnis und betroffene Stellen | Abhängigkeit / Abnahme |
| --- | --- | --- |
| QE-1: Erklärung erhalten | `CompactAccountUsage`, `HumanQuotas`, gemeinsamer Client: konkreter Grund + separat erreichbarer nächster Schritt; keine sinnentstellende 88-Zeichen-Kürzung. | Kann mit bestehendem DTO beginnen. Screenshotfälle und ältere Hostantworten zeigen mindestens ein aussagekräftiges Warum. Keine zusätzlichen Providerrequests durch `usage`. |
| QE-2: Aktionswahl korrigieren | `MainWindow.Monitor`, `MonitorWizard`: gespeicherter Zugang, Methodenverfügbarkeit, Fehlergrund und gewünschter Messumfang bestimmen die erste Handlung. Gleicher Account/Projekt bleibt gebunden. | Verfügbare bestehende Methoden nutzen. Fehlendes Setup bietet nicht wirkungsloses Refresh als erste Wahl; Netzwerkfehler fordert nicht pauschal neuen Login. Setup bleibt explizit gestartet. |
| QE-3: Belegte Gründe stabil transportieren | Kleine additive Supporterweiterung nach Abstimmung; Host/Adapter setzen nur belegte Reasoncodes und referenzieren vorhandene Methoden. | Research muss spezielle Tarif-/Quellenbehauptungen tragen. Legacyfallback erhalten. 403/keine Serie/unkanntes Schema dürfen keinen Tarif erfinden. |
| QE-4: Read-only-Erklärung erreichbar machen | Kontobezogene Details in CLI entwerfen; neue Syntax ausdrücklich dokumentieren und gegen bestehende Historyalias-Semantik abgrenzen. Desktop nutzt seinen Accountinspector. | Naming-/CLI-Review integrieren. Wer „Warum?“ lesen will, muss weder 200 Zeilen durchsuchen noch einen Loginwizard bedienen. Keine automatische Auth- oder Refreshaktion. |
| QE-5: Gemeinsame fachliche und visuelle Abnahme | Offlinefälle für unsupported, setup, expiry, permissions/uncertain dataset, successful empty, unknown schema, passive, stale-with-values, only-rate, only-limit, shared pool. | Später angemessene Tests/Sichtprüfung, hier nicht ausgeführt. Reale Nutzerpalette; Farbe aus; schmal/breit; gleiche Ursache/Handlung in CLI/Desktop; keine erfundenen Quotas oder Summen. |

## Offene Rechercheabhängigkeiten

Providerrecherche verantwortet, **welche** Quelle mit **welchem** Zugang, Produkt/Tarif und Scope tatsächlich existiert. Dieser Review bestätigt nur, was die gelesenen Implementierungen und ihre hinterlegten Aussagen bereits transportieren. Besonders offen bleiben Ursache der Synthetic-Antwort ohne erkannte Quota, konkreter Cloudflare-Datasetzugang, mögliche zusätzliche Kontoquellen bei NVIDIA/Cohere/SambaNova/Upstage sowie Umfang und Lebenszyklus alternativer offizieller CLI-/OAuth-/Managementzugänge.

Researchergebnisse sollen je Anbieter beantworten: beste heute verfügbare Methode; erwartete Datenart; genaue Ressource; notwendige Rechte; belegte Produktgrenzen; erneuerbar oder kurzlebig; tatsächlich in lain verfügbar oder nur dokumentiert; datierte offizielle Quelle. „Nicht gefunden“ als Recherchegrenze kennzeichnen. Kontoentitlement bleibt ohne Kontoevidenz unbekannt. Diese Ergebnisse präzisieren Texte und Methodenpriorität, nicht rückwirkend die Beobachtungen im Screenshot.
