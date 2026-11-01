# Einheitliche Anbieter- und Accountnamen

Stand: 25.10.2026. Analyse von HEAD `cbf083f`; installierte CLI laut
Lieferkontext `b49096f`, Desktop `67fb26c`. Screenshot
`/tmp/codex-clipboard-SabJLv.png` selbst angesehen. Keine Builds, Tests,
Host-/Provideraufrufe, Installation oder Produktänderungen durchgeführt.
Dieses Dokument ist ein Entwurf, keine Implementierungsfreigabe.

## Verbindlicher Auftrag und Empfehlungen

**Nutzervorgaben:** Die langen Namen wie `My Google account` und
`My NVIDIA Build account` sollen aus der gewöhnlichen Darstellung verschwinden.
Bei genau einem Account eines Anbieters soll dessen kurzer Name erscheinen,
konkret etwa `Gemini`, `NVIDIA` und `Cloudflare`. Mehrere Accounts müssen
unterscheidbar bleiben. Bewusst gewählte Namen, bestehende Konfiguration und
Identität dürfen nicht still verändert werden. Gefordert ist hier Analyse.

**Präzisierte Nutzerpräferenz:** Bei nur einem Anbieteraccount steht standardmäßig
nur die kurze Anbieterüberschrift. Der eigene gespeicherte Name bleibt im Detail
und bei mehreren Accounts zur Unterscheidung erhalten.

**Empfehlung:** Anbieterüberschrift und gespeicherte Accountbezeichnung als zwei
unterschiedliche Dinge behandeln. Das erfüllt den Wunsch nach kurzen Namen
sofort und verlangt für die erste Darstellung weder Bestandsmigration noch eine
neue Host-API. Eine spätere explizite Namensherkunft verbessert Onboarding und
Grenzfälle; sie ist keine Voraussetzung für eine kurze Anbieterüberschrift.

## Belegter Ausgangsstand

| Quelle | Befund und Wirkung |
| --- | --- |
| `src/Lain.Client/AccountNameSuggestions.cs:4` | Gilt ausdrücklich nur für neue Accounts. Liefert `Gemini`, aber noch `NVIDIA Build` und `Cloudflare Workers AI`; vergibt bei Namenskollisionen numerische Suffixe. Ändert keine bestehenden Namen. |
| `src/Lain.Client/ProviderDisplayNames.cs:5` | Zweiter Katalog, etwa `Gemini API`, `NVIDIA Build`, `Cloudflare Workers AI`; enthält andere Aliasse als die Desktopfunktion. |
| `src/Lain.Cli/CompactAccountUsage.cs:35` | Zeigt gespeichertes `Name` und zusätzlich die rohe Provider-ID, außer beide Strings sind gleich. Erklärt im Screenshot `My Google Gemini API account · gemini-api`. |
| `src/Lain.Cli/AccountUsage.cs:35` | Full zeigt ebenfalls gespeicherten Namen und rohe Provider-ID. |
| `src/Lain.Cli/HumanLists.cs:23` | Accountliste verwendet gespeicherte Namen, IDs und rohe Providerwerte. Providerkatalog verwendet dagegen den Namen aus dem Hostkatalog. |
| `src/Lain.Desktop/MainWindow.Budgets.cs:13` | Eigene Anbietertabelle. `AccountDisplayName` ersetzt nur Namen, die exakt der Provider-ID entsprechen. Eine Reihe neuer Anbieter fällt auf rohe IDs zurück. |
| `src/Lain.Desktop/MainWindow.AccountChooser.cs:234`, `src/Lain.Cli/AccountWizard.cs:46` | Beide verwenden denselben Vorschlagshelfer, aber unterschiedliche Quellen für den übergebenen Providernamen. Reconnect erhält den gespeicherten Namen. |
| `src/Lain.Desktop/Usage/UsageView.cs:563`, `src/Lain.Desktop/Tray/DesktopSignalMonitor.cs:207` | Usage und Tray verwenden wiederum gespeicherte Namen. Eine reine Kachelkorrektur löst „überall“ nicht. |
| `src/Lain.Core/Contracts.cs:4` | `AccountDto` hat `Id`, `Name`, `Provider`, aber keine Kennzeichnung automatisch/bewusst vergeben. |
| `src/Lain.Host/Accounts/LegacyImportService.cs:89` | Reimport bewahrt vorherigen Namen. `displayNameIsExplicit` existiert beim API-Import in privaten Credentialdaten, nicht als allgemeiner Clientvertrag. Namen und E-Mails gelten dort ausdrücklich nicht als Identitätsbeweis. |

Die Aufnahme zeigt außerdem eine persönliche E-Mail als Codexname, `groq`, `commandcode`,
`zai`, `Kilo Gateway` und neue `My … account`-Namen nebeneinander. Es geht sowohl
um redundante Wörter als auch uneinheitliche Großschreibung und Datenrollen.
Eine Umbenennung neuer Vorschläge allein behebt den sichtbaren Bestand nicht.

## Informationsarchitektur

Die Accountseite beschreibt **Zugänge zu einem Dienst**. Ihr Anbieter ist der
Vertragspartner beziehungsweise Zugangspunkt dieses Accounts, nicht automatisch
der Hersteller des aktuell benutzten Modells. Beispiel: Ein OpenRouter-Account
bleibt `OpenRouter`, auch wenn er ein OpenAI-Modell über einen anderen
Inferenzanbieter erreicht. Ein Groq-Account bleibt `Groq`, wenn das Modell von
OpenAI stammt. `GPT`, `Llama` und `DeepSeek` sind keine Ersatznamen dieser Accounts.

Die vorhandenen getrennten Felder in `ProviderRouteDescriptorDto`,
`UsageQuery` und `Attempt`-Metadaten bleiben getrennt:

| Rolle | Darstellung und Gruppierung |
| --- | --- |
| Accountanbieter | Kurzer Marken-/Dienstname auf Accountkarte und in Accountusage. Gruppenschlüssel ist der kanonische Accountdienst. |
| Modellhersteller (`manufacturer`) | Nur Modell-/Routen-/Verbrauchsauswertung; nicht aus Accountname oder Providertext ableiten. |
| Inferenzanbieter (`inferenceProvider`) | Tatsächlich gemeldeter ausführender Anbieter; unbekannt bleibt unbekannt. |
| Gateway (`gateway`) | Vermittler einer Route; nicht automatisch ein weiterer Account oder Hersteller. |
| Abrechnungsaccount (`billingAccountId`) | Kosten-/Zuordnungsidentität, kein Präsentationsname und keine Zusammenführung von Accounts. |
| Budgetpool | Gemeinsames Kontingent, kein Beweis identischer Logins und keine Erlaubnis, Werte mehrfach zu summieren. |

Die Nutzeraussage „vom Hersteller“ ist für diese Namensregel als
Accountanbieter zu verstehen. Google als Konzern darf nicht automatisch Gemini,
Vertex und ein künftiges anderes Produkt in dieselbe Accountgruppe zwingen.
Ebenso ist die bestehende Desktopgleichsetzung `moonshot` → `Kimi` zu prüfen:
Kimi Coding und Moonshot sind laut Account-Connect-Kontext getrennte Produkte.
Aliasse nur zusammenführen, wenn sie denselben Accountdienst bezeichnen.

## Empfohlenes Darstellungsmodell

Eine gemeinsame Clientpolicy berechnet aus dem **gesamten verfügbaren
Accountbestand** ein Präsentationsobjekt: kurzer Anbietername,
Accountbezeichnung, Unterscheidungszusatz und Detailwerte. Auswahl, Aktionen,
Routing und History bleiben immer an `Account.Id` gebunden.

### Ein Account

Primäre Überschrift ist der kurze Anbietername. Die gespeicherte Bezeichnung
bleibt im Detail unter `Account name` unverändert vorhanden. Das ist eine
Anbieterüberschrift, keine Behauptung, der Account sei umbenannt worden.

| Gespeicherter Name / Provider | Kompakte Überschrift | Gespeicherter Name im Detail |
| --- | --- | --- |
| `My Google Gemini API account` / `gemini-api` | `Gemini` | `My Google Gemini API account` |
| `My NVIDIA Build account` / `nvidia-build` | `NVIDIA` | `My NVIDIA Build account` |
| `My Cloudflare Workers AI account` / `cloudflare-workers-ai` | `Cloudflare` | `My Cloudflare Workers AI account` |
| `My OpenRouter account` / `openrouter` | `OpenRouter` | `My OpenRouter account` |
| `person@example.test` / `codex` (synthetisch) | `Codex` | `person@example.test` |
| `Forschung` / `gemini-api` | `Gemini` | `Forschung` |

Auch in den letzten beiden Zeilen bleibt standardmäßig die Anbieterüberschrift
allein sichtbar. Die eigene Accountbezeichnung ist vollständig im Detail und
bei mehreren Accounts als Zusatz verfügbar. Eine E-Mail ist kein wegwerfbarer
Standardname und wird im gespeicherten Namen nicht verändert. Eine optionale
Darstellungspräferenz kann sie auch beim Singleton als Zusatz einblenden.

Für alte Namen ohne Herkunft gibt es keine perfekte automatische Erkennung:
`My NVIDIA Build account` könnte auch bewusst eingegeben worden sein. Deshalb
**nicht** aus einem regulären Ausdruck eine Namensmutation oder angeblich sichere
Custom-Erkennung ableiten. Für den Singleton ist überhaupt keine Erkennung nötig:
Jeder gespeicherte Name liegt standardmäßig im Detail. Für mehrere Accounts kann
eine kleine, versionierte Liste nachweislich ausgelieferter Standardtexte je Dienst
helfen, kurze Zusätze vorzuschlagen; bei unbekannter Herkunft ist die gespeicherte
Bezeichnung konservativ zu erhalten, statt sie automatisch umzudeuten.

Eine explizite Anzeigepräferenz (`Show account name`) kann den Namen bei Bedarf
zusätzlich einblenden. Sie verändert nur Darstellung, niemals `Account.Name`.
Die bestätigte Singletonpräferenz benötigt weder diese Erweiterung noch eine
Bestandsmigration. Die Herkunftsfrage betrifft vor allem automatische Vorschläge
und Kürzungen bei mehreren Accounts.

### Mehrere Accounts und Kollisionen

Innerhalb eines Diensts erscheinen alle Zugänge mit einem Zusatz:
`Gemini · Privat`, `Gemini · Arbeit`; bei zwei automatisch benannten Zugängen
`Gemini 1`, `Gemini 2`. Bei Mischung: `Gemini · Forschung`, `Gemini 1`.
Die sichtbare Nummer ist ein Unterscheidungszusatz, keine Account-ID und kein
neuer gespeicherter Name. Für doppelte Customnamen: `Gemini · Arbeit 1` und
`Gemini · Arbeit 2`; die Originalbezeichnung bleibt jeweils `Arbeit`.

Ein bloßes Sortieren und Durchnummerieren bei jedem Rendern ist ungeeignet:
Filter, Refresh, deaktivierte Accounts oder eine zweite UI-Sitzung dürfen keine
Nummern tauschen. Für die erste Umsetzung ohne Persistenz empfiehlt sich ein
deterministischer Rang über die vollständige Accountgruppe, geordnet nach stabiler
ID, unabhängig von Anzeige-/Pinnreihenfolge und Locale. Damit bleiben Nummern
bei gleichem Bestand zwischen CLI und Desktop gleich. Nach Hinzufügen/Entfernen
können Ränge wechseln; das ist eine bekannte Einschränkung, keine dauerhafte
Identitätsgarantie. Nach einer solchen Änderung muss eine laufende Bestätigung
weiter das gebundene Accountobjekt verwenden und darf nicht nach Label auflösen.

Falls Nummernstabilität auch über Bestandsänderungen verbindlich sein soll,
ist ein persistenter, dienstweit geteilter Anzeigenordinal nötig. Eine rein
lokale Desktoppräferenz reicht für CLI-/Desktopgleichheit nicht. Dies ist ein
separates Folgepaket; eine stille Konfigurationsänderung beim Lesen wäre falsch.
Eindeutige bewusst gewählte Namen sind die stärkere langfristige Unterscheidung.

Der Gruppenbestand umfasst deaktivierte und momentan herausgefilterte Accounts.
Ein Filter auf `Gemini 2` darf ihn nicht plötzlich `Gemini` nennen. Der Wechsel
von einem zu zwei Zugängen darf einen Singleton um einen Zusatz ergänzen.
Eine Ansicht mit nur unvollständigen Accountdaten muss diese Begrenzung kennen
und darf nicht aus einem einzelnen Treffer globale Einzigartigkeit folgern.

### Kurzname, Produktname und Aliasregister

Ein gemeinsamer Katalog ersetzt die verteilten Switches. Er führt mindestens
kanonischen Dienstschlüssel, geprüfte Aliasse, kurzen Namen und Produktnamen.
Die Namen sind Präsentation; bestehende Provider-IDs ändern sich nicht.

| Dienstschlüssel | Kurzname für Accounts | Produktkontext in Auswahl/Details |
| --- | --- | --- |
| `gemini-api` | `Gemini` | `Google Gemini API` |
| `nvidia-build` | `NVIDIA` | `NVIDIA Build` |
| `cloudflare-workers-ai` | `Cloudflare` | `Cloudflare Workers AI` |
| `openrouter` | `OpenRouter` | `OpenRouter` |
| `commandcode` | `Command Code` | `Command Code` |
| `zai` | `Z.ai` | `Z.ai` |
| `codex` | `Codex` | `OpenAI Codex` |
| `kimi` | `Kimi` | `Kimi Coding`, sofern dieser Dienst belegt ist |
| `kilo` | `Kilo` | `Kilo Gateway` |

Unbekannte/custom OpenAI-kompatible Dienste erhalten einen vorhandenen expliziten
Anbieternamen als Fallback; rohe IDs bleiben im Detail auffindbar. Keinen Hersteller
aus Endpoint-Substring, Account-E-Mail oder Modellnamen raten. `openai` ist nur
bei belegtem Codexzugang `Codex`, nicht generell. Aliasse wie `google` oder
`moonshot` sind vor einer Zusammenlegung anhand des tatsächlich vertretenen
Produkts zu qualifizieren. Markennamen bleiben localeunabhängig; übersetzt werden
Beschriftungen, nicht Identitätsschlüssel. Historische Standardtexte werden
explizit und providerbezogen katalogisiert, nicht sprachübergreifend erraten.

## Dieselbe Semantik an allen Stellen

- **Accountkarten, Monitor, Tray, CLI usage:** dieselbe Überschrift und derselbe
  Zusatz. Keine redundante rohe Provider-ID hinter `NVIDIA`. Zustand und geteilter
  Pool bleiben eigenständige Informationen.
- **Accountliste und Picker:** gleiche Namen. Vorhandene technische CLI-ID-Spalten
  bleiben für Befehlsbedienung erreichbar, vorzugsweise in Detail-/Fullansichten;
  kein Label wird ein neuer Befehlsselektor. Unterscheidung ist auch bei gekürzten
  Zeilen sichtbar, vollständiger Text per Detail beziehungsweise Screenreader.
- **Accountdetail:** Anbieter, Produkt, gespeicherte Accountbezeichnung und kopierbare
  Account-/Provider-ID getrennt. `Rename` bearbeitet ausschließlich den gespeicherten
  Namen; das Feld enthält niemals heimlich den berechneten Titel.
- **Usage/Live/Modelle/Profile:** Accountreferenzen nutzen dasselbe Objekt. Modellname,
  Hersteller und Route bleiben eigene Felder. Gelöschte historische Accounts brauchen
  einen belegten historischen Namen oder eine als solche erkennbare unbekannte
  Referenz; nicht die Nummer eines heutigen Accounts wiederverwenden.
- **Onboarding:** Anbieterwahl zeigt kurz `Gemini`, darunter `Google Gemini API`.
  Namen optional anbieten, Standardvorschlag `Gemini` statt `My … account`.
  Vorschläge müssen sichtbare Namen anderer Accounts berücksichtigen; Änderungen
  durch den Nutzer erhalten. Reconnect und Rückkehr zu einem Draft ersetzen dessen
  Namen nicht. Eingabetext und berechnete Übersicht sind getrennt.
- **Accessibility und Suche:** dieselbe menschenlesbare Identität in sichtbarem Text
  und AccessibleName. Suche findet sowohl kurzen Anbieter als auch gespeicherten
  Namen, historische bekannte Standardtexte und technische ID. IDs nicht vorlesen,
  solange keine Detailaktion sie verlangt.
- **JSON/Export:** Rohfelder, IDs, Reihenfolge und gespeicherte Namen bleiben erhalten.
  Eine spätere optionale `displayName`-Erweiterung ist eine ausdrückliche
  Vertragsänderung, kein Teil einer stillen kosmetischen Anpassung.

## Migration und Herkunft

**Empfohlen: erste Lieferung ausschließlich als Präsentation.** Der Effekt ist
sofort rücknehmbar; Hashes/Revisionen von Konfiguration, Accounts, Profilen und
Routen ändern sich nicht. Bestehende Imports behalten Namen und Identität.
Grenzen bleiben die Legacyherkunft bei gewünschten Mehraccountkürzungen und
rangbasierte Nummern bei Bestandsänderungen.

**Nicht empfohlen: bestehende `Name`-Werte automatisch ersetzen.** Das zerstört
bewusste Namen, macht Wiedererkennung und Reimport konfliktanfällig und koppelt
eine reine Ansicht an Hostrevision/Schreibrechte. Auch eine exakte Standardtextliste
beweist historische Absicht nicht. Eine freiwillige spätere Bestandsbereinigung
müsste Vorher/Nachher pro Account zeigen und separat beauftragt werden.

**Sinnvolle spätere Erweiterung:** neue Accounts kennen `automatic`, `custom` oder
`imported/unknown` als Herkunft des gespeicherten Namens. Ein explizites Umbenennen
markiert `custom`, auch wenn das Ergebnis genauso wie ein Standardtext lautet.
Ältere Datensätze bleiben `unknown`; ein Schema-Default darf sie nicht nachträglich
als automatisch erklären. Login/Sessionrefresh und Reimport bewahren Herkunft
und bewussten Namen. Ein vom Identityprovider gelieferter Anzeigename oder eine
E-Mail ist eigener Herkunftstyp beziehungsweise konservativ erhaltener Name,
kein Beweis für einen generischen Standard. Diese Erweiterung verbessert die
Policy, ersetzt jedoch nicht den kurzen Anbieter-/Produktkatalog.

## Testbare Akzeptanzfälle für eine spätere Umsetzung

1. Je ein bestehender Google-/NVIDIA-/Cloudflare-Account mit langem Standardtext
   zeigt `Gemini`/`NVIDIA`/`Cloudflare` in CLI usage, Accountkarte und Monitor.
   Gespeicherter Name ist im Detail exakt lesbar; keine Schreibanforderung entsteht.
2. `person@example.test` und `Forschung` bleiben im Detail vollständig erhalten;
   beim Singleton steht standardmäßig nur der Anbieter, bei Mehraccounts der eigene
   Name als Zusatz. Kein gespeicherter Name wird anhand von `@` oder Domainteilen ersetzt.
3. Zwei automatisch benannte Gemini-Zugänge zeigen unterschiedliche Zusätze;
   CLI und Desktop ordnen sie denselben IDs zu. Gleiche eigene Namen werden ebenfalls
   unterscheidbar, ohne diese Namen zu ändern.
4. Filter, deaktivierter zweiter Account, Pinänderung, Quota-Refresh und Neuladen
   ändern die Zusätze eines unveränderten Bestands nicht. Gruppen sind dienstbezogen.
5. Ein neu hinzugefügter Account macht bestehende Bestätigungen nicht mehrdeutig:
   Ziel ist weiterhin die gebundene ID. Rangänderung ist kein Renaming und darf keine
   Such-/Aktionszuordnung nach der alten sichtbaren Nummer auslösen.
6. Unbekannter eigener Provider, getrennte Google-Produkte und Kimi/Moonshot bleiben
   getrennt, solange keine geprüfte Aliaszuordnung vorliegt. OpenRouter plus OpenAI-
   Modell wird auf der Accountseite weiterhin OpenRouter genannt.
7. `en`/`de`, Neustart und zweite Sitzung liefern bei gleichem Bestand dieselben
   Marken und Zusätze. Es wird nicht nach übersetztem sichtbarem Text gruppiert.
8. Reimport, Credentialrotation und Reconnect erhalten gespeicherten eigenen Namen.
   Das Namensfeld in Rename enthält den Rohwert und nicht die neue Überschrift.
9. Bewusst exakt `My NVIDIA Build account` gesetzter Name bleibt im Detail erhalten.
   Bei aktivem explizitem Anzeigenwunsch bleibt er auch als Zusatz sichtbar.
   Bei mehreren Accounts schützt Herkunft `custom` auch exakt gleiche Standardtexte;
   unbekannte Herkunft wird konservativ behandelt.
10. 20-Spalten-CLI, großer Text und Screenreader machen zwei gleichnamige Accounts
    weiter unterscheidbar. Keine ausschließlich in Tooltips zugängliche Pflichtidentität.
11. JSON-/Exportwerte sowie Account-/Profil-/Routenhashes und Revisionen sind vor/nach
    reiner Anzeige identisch. Weder Lesen noch Moduswechsel schreibt Konfiguration.
12. Accountsumme, Quota-Pools, Manufacturer-, InferenceProvider-, Gateway- und
    BillingAccount-Gruppierung sind vor/nach der Textänderung fachlich identisch.

## Kleine Arbeitspakete

| Paket | Ergebnis / Umfang | Abhängigkeit und Grenze |
| --- | --- | --- |
| N1 · Katalog | Gemeinsame kurze Namen, getrennte Produktnamen, geprüfte Aliasliste; Google/NVIDIA/Cloudflare zuerst. | Reine Clientdaten. Problematische Moonshot-/Google-Aliasse separat prüfen, keine Provider-ID-Migration. |
| N2 · Presentationpolicy | Ein gemeinsames Ergebnis für Anbieterüberschrift, gespeicherte Bezeichnung und kollisionssichere Zusätze; vollständiger Bestand als Eingabe. Legacytexte explizit. | N1. Herkunftsgrenze und Rangstabilität transparent spezifizieren. Keine Hostmutation. |
| N3 · Hauptansichten | CLI usage/accounts/full sowie Desktopkarten/Monitor/Inspector auf N2 umstellen; doppelte Provider-IDs entfernen, Details erhalten. | N2. Native/CLI-Offlinefixtures statt echter Provideraufrufe. |
| N4 · Referenzen und Onboarding | Tray, Usage, Model-/Profilpicker, Aktionen, AccessibleNames und Suche angleichen; Vorschläge kurz, Draft/Reconnect unverändert. | N2. Keine Onboarding-Neuarchitektur und kein Identitätsmatching anhand Namen. |
| N5 · Regression und Lieferbeleg | Obige Fälle mit synthetischem Mischbestand, Breiten, Locale, zwei Sitzungen und Rohdatenvergleich qualifizieren. | N3/N4. Keine reale Umbenennung oder Installation im Analyseauftrag. |
| N6 · Optionaler Herkunftsvertrag | Neue Namen explizit als automatisch/custom/importiert kennzeichnen; Legacy unknown; Reimport/Schemafragen beantworten. | Eigenständige Entscheidung. Kein Blocker für N1–N5; persistente globale Nummern nur bei bestätigter Anforderung ergänzen. |

Die bestehende Hauptnavigation mit Accounts, Models, Profiles, Usage und Live
bleibt die bestätigte Grundlage. Für diese Namenskorrektur ist weder ein neuer
Hersteller-Tab noch eine zweite Accountverwaltung erforderlich.
