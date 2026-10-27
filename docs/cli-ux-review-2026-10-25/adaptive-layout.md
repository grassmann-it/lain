# Adaptive CLI-Layouts: Fensterzeilen und zwei Inhaltsbereiche

Review vom 25.10.2026. Quellstand `cbf083f`; laut Auftrag installierte CLI
`b49096f`, Desktop `67fb26c`. Analyse und synthetische Entwürfe, keine
Implementierungsfreigabe. Keine Produktänderungen, Builds, Produktprüfungen,
Host-/Provideraufrufe, Installation oder Commits.

**Empfehlung:** Eine ruhige Tabelle mit einer Zeile je tatsächlich angezeigtem
Quotafenster. Verfügbare Restmenge steht vor Verbrauch und Balken. Bei ungefähr
160 Spalten kann derselbe Bestand in zwei inhaltliche Bereiche wechseln:
Kontingente links, fehlende Messwerte mit konkretem Grund rechts. Die notwendige
Mindestbreite ergibt sich aus dem Inhalt; 160 ist kein bedingungsloser Schalter.
Pipes bleiben einspaltig. Ein Account mit zwei Fenstern bleibt ein Account.

[Offline-Vorschau: A/B, 80/120/160 Spalten, Hell/Dunkel und Langnamen](layout-preview.html).
Alle Ansichten sind monochrom und ohne Netzabhängigkeiten. Die Hintergrundfarben
der Galerie sind Beispiele, keine festgelegte CLI-Palette. Darstellung und
Sprache der Mockups sind Vorschläge; Zahlen und Zustände sind synthetisch.

## Anforderungen und Vorschläge getrennt

| Grundlage | Verbindlich im Review zu erhalten | Noch vorgeschlagene Lösung |
| --- | --- | --- |
| Aktueller Nutzerauftrag | Erkennbare Tabellen, sinnvoll genutzte Breite, weniger Textwand; brauchbar ohne schwer erkennbare dünne grüne Balken | A als Grunddarstellung; B als breite Alternative |
| Aktuelle Namenspräzisierung | Kurze Anbieterüberschriften; bewusst gewählte Namen und verschiedene Accounts bleiben unterscheidbar | Gemeinsame Namenspolicy aus [Namen und Struktur](names-and-structure.md); keine rohe Provider-ID neben dem Kurznamen |
| `product-scope.md:127` | Zur Terminalbreite passende CLI-Tabellen und Kontingentbalken | Balken erst bei ausreichendem Platz, sonst numerischer Verbrauch |
| `cli-usage.md:12` und gemeinsame Quota-Hierarchie | Höchstens zwei primäre kurze Fenster, Monatswerte und Zusatzcredits sekundär, wirkliche Nullsalden sichtbar, unbekannt bleibt unbekannt | Fenster als Zeilen; keine feste Matrix aus 5h/7d/Monat/Wallet für jeden Account |
| `cli-usage.md:29` | Native Restmenge, Prozent, Reset samt Zeitzone/Offset, Shared- und Frischehinweise | Restmenge vor dem Verbrauch; Zustand möglichst in eigener Spalte |
| Aktueller Analyseauftrag | Keine Account-/Anbieter-PAYG-Summierung, keine Änderung von Daten/Identität | Zwei visuelle Bereiche teilen denselben unveränderten Präsentationsbestand |

Die älteren Desktopentscheidungen zu Accountkacheln sind kein Verbot von
CLI-Tabellen. Ihr Geltungsbereich ist die Desktopoberfläche; der CLI-Auftrag nennt
Tabellen ausdrücklich. Das Review ändert weder diese Entscheidung noch die
gemeinsame Auswahl von Haupt- und Nebenfenstern.

## Was am heutigen Layout belegbar ist

| Code-/Dokumentquelle | Befund | Auswirkung |
| --- | --- | --- |
| [`CompactAccountUsage.cs:17`](../../src/Lain.Cli/CompactAccountUsage.cs#L17) | Titel, Accounttags und Quoten entstehen als aufeinanderfolgende Textfelder. | Die Ausgabe hat keine accountübergreifenden Spaltenanker. |
| [`CompactAccountUsage.cs:35`](../../src/Lain.Cli/CompactAccountUsage.cs#L35) | Gespeicherter Accountname erhält ANSI `1;36`; rohe Provider-ID folgt, wenn sie nicht gleich lautet. | Lange Standardnamen und redundante technische Namen vergrößern die Zeilen. |
| [`CompactAccountUsage.cs:50`](../../src/Lain.Cli/CompactAccountUsage.cs#L50) | Fensterbreite wird nur innerhalb eines Accounts ausgerichtet, maximal 24 Zellen. | Verbrauch und Rest beginnen beim nächsten Account wieder an anderer Position. |
| [`CompactAccountUsage.cs:88`](../../src/Lain.Cli/CompactAccountUsage.cs#L88) | Verbrauch nutzt rote/gelbe/grüne SGR-Farben; Balken bestehen aus zehn Linien-/Punktzeichen. | Die Erkennbarkeit hängt von Schrift und Terminalpalette ab. |
| [`CompactAccountUsage.cs:149`](../../src/Lain.Cli/CompactAccountUsage.cs#L149) | Breite steuert Feldumbruch, nicht den Wechsel zwischen Layoutstrukturen. | 120 oder 200 Spalten machen primär dieselben Sätze länger, nicht die Übersicht effizienter. |
| [`HumanTable.cs:45`](../../src/Lain.Cli/HumanTable.cs#L45) | Spaltenbreiten richten sich nach dem längsten Inhalt; erst sekundäre Spalten entfernen, dann ganze Tabelle stapeln. | Ein einziger langer Name kann alle Accounts in eine Feldliste kippen. Einfaches Wiederverwenden des Renderers löst die Aufgabe daher nicht vollständig. |
| [`HumanTable.cs:16`](../../src/Lain.Cli/HumanTable.cs#L16) | Farbe nur am Terminal, ohne `NO_COLOR`, nicht bei `TERM=dumb`; Pipebreite fällt auf 100 zurück. | Gute vorhandene Plain-Grundlage, aber bisher kein eigener Begriff für räumliche Zweispaltigkeit. |
| [`QuotaPresentation.cs:77`](../../src/Lain.Client/QuotaPresentation.cs#L77) | Gemeinsame Auswahl kennt Hauptwerte, zusätzliche Werte, unbekannte Werte und echte Nullsalden. | Layout sollte diese Entscheidung konsumieren, nicht ein zweites fachliches Auswahlverfahren entwickeln. |

Der privat betrachtete Screenshot bestätigt wechselnde Textanker und geringe
visuelle Trennung. Dass dort fast alles cyan wirkt, beweist **nicht**, dass der
Renderer jede Zeichenfolge cyan färbt: unbemalte Felder erben den Vordergrund des
Terminals, viele andere sind lediglich gedimmt. Eine fest erzwungene weiße oder
schwarze Textfarbe wäre deshalb keine tragfähige Reparatur. Der Entwurf löst die
Hierarchie über Position, Labels, Ziffern und zurückhaltende Linien.

Die bestehenden synthetischen Exact-PTY-Captures unter dem privaten
`cli-style/exact-pty`-Nachweis wurden für 60/80/120 und Pipe gelesen. Der
Usage-Datensatz enthält sechs Accounts; `checks.json` enthält maximal 120 Spalten.
Die Captures belegen vorhandene Umbrüche und Plain-Ausgabe, nicht eine gute
Lesefolge für 15 heterogene Accounts bei 160/200 Spalten. Die neuen Dateien hier
sind manuelle Entwurfsartefakte, keine Captures eines neuen CLI-Binaries.

## A: Eine Account-/Fenstertabelle

Die Tabelle hat die Lesefolge **Account → Fenster → verfügbar → Verbrauch →
Reset → notwendiger Zustand**. Die Restmenge ist die primäre Nutzinformation:
Was steht in diesem Budget noch zur Verfügung? Prozent und Balken liefern den
schnellen Verhältnisvergleich; sie ersetzen die native Einheit nicht. Der Reset
bleibt als eigene, durchgehend ausgerichtete Spalte erkennbar. Diese Reihenfolge
ist ein Vorschlag; sie ändert die Bedeutung des Balkens nicht: gefüllt = verbraucht.

Ein zweites Fenster erhält eine Folgezeile mit leerem Accountfeld. Die Einheiten
bleiben je Fenster erhalten. Für einen Account ohne 5h-Fenster entsteht keine
leere 5h-Zelle. Monatskontingente bleiben als kurze Detailreferenz unter genau
diesem Account, nicht als globale zusätzliche Spalte. Nichtleere Zusatzcredits
können eine knappe eingerückte Mengenangabe behalten. Ein alleiniger Monatswert
oder Wallet bleibt weiterhin ein Hauptwert.

Bei 80 Spalten stehen kurze Accountüberschriften über den weiterhin ausgerichteten
Fensterzeilen. So kostet der Accountname nicht in jeder Zeile eine Spalte. Bei
120 Spalten passen Account, Fenster, Rest, kompakter Balken und Reset nebeneinander.
Eine schmale Hinweis-Spalte trägt `geteilt` oder `Letzter Stand`. Bei 160 Spalten
kann A längere Balken und etwas mehr Namen-/Hinweisbreite nutzen, ohne neue
sekundäre Daten nur wegen des freien Platzes nach oben zu ziehen.

Lange Namen werden nicht still gekürzt: Passt ein bewusster Name nicht in höchstens
zwei Zeilen der Accountzelle, erhält nur dieser Account eine vollständige
Überschriftszeile über seinen Werten. Die anderen Accounts bleiben in der Tabelle.
Sehr lange ungetrennte Zeichenfolgen dürfen an Terminalzellen umbrochen werden;
technische kopierbare IDs bleiben in der Detailansicht.

**Stärke:** Vergleich über alle messbaren Accounts, stabile Lesefolge, brauchbare
Kopie, robuste Pipeform. **Nachteil:** Zwei nacheinanderstehende Gruppen benötigen
mehr Zeilen; sehr breite Terminals verwenden nicht immer ihre gesamte Fläche.

## B: Zwei inhaltliche Tabellenbereiche

B verwendet bis zur inhaltlich ausreichenden Breite dieselbe Ausgabe wie A.
Danach stehen links die tatsächlichen Quotafenster und rechts Accounts ohne
Messwerte mit konkretem Grund/nächstem Schritt. Die beiden Überschriften und
ein einziger senkrechter Trenner machen die Trennung eindeutig. Die Lesefolge ist
jeweils von oben nach unten innerhalb des Bereichs. Eine rechte Zeile ist kein
Partner der auf derselben Höhe stehenden linken Quota.

Der 160-Spalten-Entwurf reserviert 98 Zellen links, drei für den Trenner und 59
rechts. Die tatsächliche linke Tabelle benötigt 94 Zellen:
14 Account + 17 Fenster + 18 Rest + 17 Verbrauch + 20 Reset + 8 Abstände.
Rechts stehen 12 Zellen Anbieter, zwei Abstand und 45 Zellen Erklärung. Das ist
eine durch den gezeigten Inhalt begründete Aufteilung, keine universelle Norm.
Einmalige lange Namen nutzen dieselbe lokale Ausweichregel wie A.

Im gleichen Hauptdatensatz hat A bei 160 Spalten 34 Zeilen und B 23. Beide zeigen
dieselben 15 Accounts, dieselben Quoten, Zustände und Nebenwertverweise. Die
Ersparnis von elf Zeilen gilt nur für diesen Entwurf. Sie ist keine gemessene
Produkteigenschaft und keine allgemeine Größenprognose.

**Stärke:** Gute räumliche Trennung von „verfügbar“ und „warum fehlt etwas“ bei
einem gemischten Bestand. **Nachteil:** Räumliche Lesefolge ist schlechter für
zeilenweises Kopieren, Logs und Screenreader. Bei zwölf messbaren und drei
unmessbaren Accounts bleibt die rechte Seite häufig leer, ohne viel Höhe zu
sparen. Deshalb B als optionale breite Darstellung, A als verlässliche Basis.

Nicht empfohlen: Accounts beliebig in zwei Kartenkolonnen füllen, zeilenweise
zwischen links und rechts springen oder Konten nach berechneter Kartenhöhe
umsortieren. Das verletzt Wiedererkennung und Fenstervergleich. Die Zugehörigkeit
zu den zwei Inhaltsbereichen darf keine Konten mit guten Werten und einem
fehlenden Nebenfenster aus der Kontingenttabelle entfernen.

## Breite ist ein Budget für Inhalte

| Terminal | Sinnvolles Layout | Begründung und Ausweichregel |
| --- | --- | --- |
| 60 | Accountüberschrift, anschließend kompakte Fensterzeilen; Reset/Zustand in eine zweite eingerückte Zeile | Bereits 17 Fenster + 7 Prozent + 18 Rest + 20 Reset + Abstände überschreiten 60. Numerische Werte und Einheiten zusammenhalten; Balken zuerst weglassen. |
| 80 | A/B identisch: Accountüberschrift mit mehreren ausgerichteten Fensterzeilen, kein Balken | 17 Fenster + 18 Rest + 7 Prozent + 20 Reset + 6 Abstände = 68. Es bleiben Reserven für andere Einheiten und längere Labels. |
| 120 | A/B identisch: gemeinsame Tabelle mit kurzen Anbieterüberschriften | Der konkrete Entwurf braucht 118 Zellen einschließlich Hinweis-Spalte. Ungewöhnlich lange Werte werden lokal umbrochen; bei dauerhaft größerem Mindestbedarf entfällt zuerst der Balken oder die Accountspalte wird wieder zur Überschrift. |
| 160 | A bleibt Gesamttabelle; B kann Kontingente und fehlende Messwerte nebeneinander stellen | Nur wenn beide Bereiche ihre notwendige Breite erhalten und die zweite Gruppe nennenswert Höhe spart. Im Entwurf 98 + 3 + 59. Ein langer Reset mit Jahreszahl, große native Mengen oder breitere Schriftzellen können B erst später zulassen. |
| 200 | Beide Varianten möglich; B kann links längere Balken und eine Zustands-Spalte aufnehmen | Zusatzbreite sinnvoll verteilen; keine drei Accounts nebeneinander und keine zusätzlichen Monats-/PAYG-Spalten. Bei wenigen Konten endet der Text dort, wo die Information endet. |

Eine mögliche Layoutentscheidung berücksichtigt berechnete Mindestbreiten und
vorab bestimmte Zeilenhöhen aus **demselben** Snapshot. Erst eine passende Breite
plus nützliche Höhenersparnis rechtfertigt B. Vier eingesparte Zeilen oder etwa
15 Prozent wären überprüfbare Startwerte für eine spätere Evaluation, keine
beschlossene Produktregel. Bei einem unsicheren Ergebnis bleibt A. Die verfügbaren
Metadaten müssen dafür Terminal/Pipe unabhängig von Farbe unterscheiden:
`NO_COLOR` bedeutet nicht automatisch eine nichtvisuelle Ausgabe.

## Unknown, Frische und Messarten bleiben eigenständig

- `0%` Verbrauch mit `100%` verfügbar ist ein voller gemeldeter Pool; `100%`
  Verbrauch mit `0%` verfügbar ist ein erschöpfter gemeldeter Pool. Der Stresstest
  zeigt den zweiten Fall zusätzlich deaktiviert und als letzten Stand.
- Kein Wert heißt `unbekannt`, niemals `0`, `100% frei` oder ein leerer Balken.
  Der Balken erscheint nur bei bekanntem Verbrauchsanteil. Ein bekanntes Guthaben
  ohne bekannten Nenner bekommt seinen Betrag, keinen erfundenen Prozentwert.
- `faellig` im Resetfeld bedeutet nicht, dass der neue Pool schon gelesen wurde.
  Betroffene Zahlen tragen zusätzlich `Letzter Stand`; ein Abruffehler bleibt
  auch bei vorhandenen Zahlen sichtbar.
- Native `USD-Credits`, `credits`, `requests`, `tokens` und `USD` werden weder
  ineinander umgerechnet noch über Accounts/Provider addiert. Ein Barvergleich
  zeigt verbrauchte Anteile verschiedener Grenzen, keine äquivalenten Kaufwerte.
- Reine Nutzung, Rate oder Limit ist keine Restmenge. Solche Werte benötigen
  ein ausdrückliches `Observed`-/`Limit`-Label beziehungsweise einen passenden
  Spaltenkopf; niemals einen gemeldeten Verbrauch in `verfügbar` umdeuten. Die
  gezeigten Hauptbeispiele sind tatsächliche Restmengen, keine Rate-only-Fälle.
- Die Kopfzeile nennt die lokale Zeitzone; jeder zukünftige Reset trägt seinen
  eigenen Offset. Das ist auch bei wiederholten Stunden und wechselndem Offset
  erforderlich. Ein Jahr außerhalb des laufenden Jahres bleibt sichtbar.
- `geteilt` ist ein Budgethinweis, keine zusätzliche Menge und keine Aussage,
  dass zwei Accounts dieselbe Identität haben. Bei konkreten Poolzuordnungen
  bleiben die vollständigen Erklärungen in Details erreichbar.

Die kurzen Gründe für fehlende Werte sind Beispielcopy und folgen
[Quota-Erklärungen](quota-explanations.md). Die tatsächliche spätere Ausgabe muss
belegten Snapshot-/Supportinformationen folgen. Sie darf weder allein aus dem
Anbieternamen eine Ursache ableiten noch ein nötiges Pro-Abo erfinden. Alle neun
Accounts bleiben auffindbar; gleiche Hinweise könnten später gruppiert werden,
aber nur bei wirklich gleichem Grund und ohne Verlust der Accountzuordnung.

## Plain, fremde Paletten und Pipe

Default-Vordergrund und höchstens Fettdruck reichen für Titel und Accountnamen.
Kritische Informationen werden nicht gedimmt. Farbe kann vorhandene Warnwörter
unterstützen; `FEHLER`, `Letzter Stand`, unbekannt und erschöpft stehen auch als
Text da. Kein Farbton wird als zuverlässig heller/dunkler angenommen. Die
Mockups nutzen `#` gegen `.` als gefüllte/leere Balkenzelle, damit die Unterscheidung
bereits in einem monochromen Terminal deutlich ist. Eine spätere Unicodevariante
braucht Prüfung mit echten Terminalfonts; dünne Linien sind keine Voraussetzung.

Bei `NO_COLOR` im echten Terminal kann die Tabelle ihre räumliche Struktur
behalten. Pipe/Dateiumleitung und eine bewusst lineare Darstellung verwenden A,
ohne ANSI und ohne nebeneinander ausgegebene unabhängige Listen. Wo keine
verlässliche Breite bekannt ist, bleibt die Ausgabe deterministisch; die
vorhandenen 100 Spalten sind ein möglicher Fallback, kein Anlass, B zu aktivieren.
Keine Kopie verliert die Bedeutung eines Werts, nur weil Farben wegfallen.

Die Galerie zeigt ausschließlich Plain-Text-Entwürfe. Sie demonstriert deshalb
Hierarchie ohne Farbe, qualifiziert aber weder Screenreader noch reales
Terminalrendering. Tabellen-/Zeichenbreiten müssen später weiterhin in
Terminalzellen gemessen werden, nicht in UTF-16-Zeichen. Der vorhandene
[`HumanTable.Width`](../../src/Lain.Cli/HumanTable.cs#L171) ist dafür die zu
prüfende Ausgangsbasis, nicht ein Nachweis für alle Graphemcluster.

## Artefakte und Grenzen

| Breite | A | B |
| --- | --- | --- |
| 80 | [Hauptentwurf](layout-a-80.txt), [Langnamen](layout-a-80-stress.txt) | [Hauptentwurf](layout-b-80.txt), [Langnamen](layout-b-80-stress.txt) |
| 120 | [Hauptentwurf](layout-a-120.txt), [Langnamen](layout-a-120-stress.txt) | [Hauptentwurf](layout-b-120.txt), [Langnamen](layout-b-120-stress.txt) |
| 160 | [Hauptentwurf](layout-a-160.txt), [Langnamen](layout-a-160-stress.txt) | [Hauptentwurf](layout-b-160.txt), [Langnamen](layout-b-160-stress.txt) |

Hauptentwurf: 15 kurze Anbieterüberschriften mit sechs messbaren und neun
unmessbaren Accounts. Der getrennte Stresstest behält 15 Accounts, ersetzt Groq
durch einen zweiten Codex-Account und ergänzt zwei bewusst lange eigene Namen.
Damit prägen außergewöhnliche Namen nicht den gewöhnlichen Hauptentwurf.
Der deaktivierte zweite Codex zeigt einen erschöpften letzten Stand. Alle
Bezeichnungen und Zahlen sind synthetisch; keine Nutzernamen oder Screenshots
wurden in diese Artefakte übernommen.

Die Artefakte wurden als Text durchgesehen und auf ihre vorgesehenen Zeilenbreiten
begrenzt. Es wurden keine Produktprüfungen oder Builds ausgeführt. Die Vorschau
ist noch keine native Terminalabnahme. Es werden keine neuen CLI-Flags als
bereits vorhandene Funktionen gezeigt: `lain usage --full` existiert; ein späterer
Layoutwahlschalter oder rein lesender Explain-Befehl wäre gesondert zu entwerfen.
`usage --account` ist heute ein Historyfilter und kein Quota-Detailbefehl.

## Konkrete Plain-Terminal-Ansichten

Die folgenden drei Ausschnitte enthalten jeweils den vollständigen synthetischen
15-Account-Bestand. Alle weiteren Vergleiche einschließlich Langnamen sind oben
als Text oder gemeinsam in der HTML-Vorschau erreichbar.

### 80 Spalten: gemeinsame schmale Form

```text
SYNTHETISCH | 15 Accounts | Europe/Berlin | 2026-10-25

KONTINGENTE (6)
FENSTER                    VERFUEGBAR   VERBR.  RESET
--------------------------------------------------------------------
Codex [geteilt]
5h                                66%      34%  10-25 19:00 +01:00
7d                                26%      74%  10-29 10:00 +01:00
  Monatskontingent: --full
Command Code [geteilt]
5h                     48 USD-Credits       0%  unbekannt
7d                      9 USD-Credits   81.25%  10-29 02:00 +01:00
  Extra: 6 USD-Credits; Monatscredits: --full
Z.ai [geteilt]
5h                      3,200 credits      80%  10-25 21:00 +01:00
7d                     18,000 credits      55%  10-30 19:00 +01:00
Kimi
5h                               100%       0%  10-25 20:00 +01:00
  Monatskontingent: --full
OpenRouter
Free models / day         30 requests      40%  10-26 01:00 +01:00
Key spend cap                  17 USD      15%  unbekannt
Groq
Tokens/min              17,600 tokens      12%  faellig
  Tokens/min: Letzter Stand
Requests/day             800 requests      20%  faellig
  Requests/day: Letzter Stand

OHNE MESSWERTE (9)
KONTO         GRUND / NAECHSTER SCHRITT
--------------------------------------------------------------------------------
NVIDIA        Keine verifizierte Kontoquelle; Anbieterreferenz
MiMo          Consolesitzung verbinden
Cohere        Reader fehlt in lain; Anbieter-Konsole
Kilo          FEHLER: Walletabruf; Rest unbekannt
SambaNova     Reader fehlt in lain; Anbieter-Konsole
Gemini        Projektmonitor verbinden
Cloudflare    FEHLER: Dataset/Berechtigung pruefen; Quota unbekannt
Synthetic     Antwort ohne erkennbare Quota; Produktscope pruefen
Upstage       Reader fehlt in lain; Anbieter-Konsole

Unbekannt ist nicht null. Geteilte Budgets nicht addieren. Details: lain usage
--full
```

### 120 Spalten: gemeinsame Tabelle

```text
SYNTHETISCH | 15 Accounts | Europe/Berlin | 2026-10-25

KONTINGENTE (6)
KONTO           FENSTER                    VERFUEGBAR          VERBRAUCH  RESET                 HINWEIS
----------------------------------------------------------------------------------------------------------------------
Codex           5h                                66%  [###.....]    34%  10-25 19:00 +01:00    geteilt
                7d                                26%  [######..]    74%  10-29 10:00 +01:00
  Monatskontingent: --full
Command Code    5h                     48 USD-Credits  [........]     0%  unbekannt             geteilt
                7d                      9 USD-Credits  [#######.] 81.25%  10-29 02:00 +01:00
  Extra: 6 USD-Credits; Monatscredits: --full
Z.ai            5h                      3,200 credits  [######..]    80%  10-25 21:00 +01:00    geteilt
                7d                     18,000 credits  [####....]    55%  10-30 19:00 +01:00
Kimi            5h                               100%  [........]     0%  10-25 20:00 +01:00
  Monatskontingent: --full
OpenRouter      Free models / day         30 requests  [###.....]    40%  10-26 01:00 +01:00
                Key spend cap                  17 USD  [#.......]    15%  unbekannt
Groq            Tokens/min              17,600 tokens  [#.......]    12%  faellig               Letzter Stand
                Requests/day             800 requests  [##......]    20%  faellig               Letzter Stand

OHNE MESSWERTE (9)
KONTO         GRUND / NAECHSTER SCHRITT
------------------------------------------------------------------------------------------------------------------------
NVIDIA        Keine verifizierte Kontoquelle; Anbieterreferenz
MiMo          Consolesitzung verbinden
Cohere        Reader fehlt in lain; Anbieter-Konsole
Kilo          FEHLER: Walletabruf; Rest unbekannt
SambaNova     Reader fehlt in lain; Anbieter-Konsole
Gemini        Projektmonitor verbinden
Cloudflare    FEHLER: Dataset/Berechtigung pruefen; Quota unbekannt
Synthetic     Antwort ohne erkennbare Quota; Produktscope pruefen
Upstage       Reader fehlt in lain; Anbieter-Konsole

Unbekannt ist nicht null. Geteilte Budgets nicht addieren. Details: lain usage --full
```

### 160 Spalten: B mit zwei Inhaltsbereichen

```text
SYNTHETISCH | 15 Accounts | Europe/Berlin | 2026-10-25

KONTINGENTE (6)                                                                                    | OHNE MESSWERTE (9)
KONTO           FENSTER                    VERFUEGBAR          VERBRAUCH  RESET                    | KONTO         GRUND / NAECHSTER SCHRITT
----------------------------------------------------------------------------------------------     | -----------------------------------------------------------
Codex           5h                                66%  [###.....]    34%  10-25 19:00 +01:00       | NVIDIA        Keine verifizierte Kontoquelle;
[geteilt]       7d                                26%  [######..]    74%  10-29 10:00 +01:00       |               Anbieterreferenz
  Monatskontingent: --full                                                                         | MiMo          Consolesitzung verbinden
Command Code    5h                     48 USD-Credits  [........]     0%  unbekannt                | Cohere        Reader fehlt in lain; Anbieter-Konsole
[geteilt]       7d                      9 USD-Credits  [#######.] 81.25%  10-29 02:00 +01:00       | Kilo          FEHLER: Walletabruf; Rest unbekannt
  Extra: 6 USD-Credits; Monatscredits: --full                                                      | SambaNova     Reader fehlt in lain; Anbieter-Konsole
Z.ai [geteilt]  5h                      3,200 credits  [######..]    80%  10-25 21:00 +01:00       | Gemini        Projektmonitor verbinden
                7d                     18,000 credits  [####....]    55%  10-30 19:00 +01:00       | Cloudflare    FEHLER: Dataset/Berechtigung pruefen; Quota
Kimi            5h                               100%  [........]     0%  10-25 20:00 +01:00       |               unbekannt
  Monatskontingent: --full                                                                         | Synthetic     Antwort ohne erkennbare Quota; Produktscope
OpenRouter      Free models / day         30 requests  [###.....]    40%  10-26 01:00 +01:00       |               pruefen
                Key spend cap                  17 USD  [#.......]    15%  unbekannt                | Upstage       Reader fehlt in lain; Anbieter-Konsole
Groq            Tokens/min              17,600 tokens  [#.......]    12%  faellig                  |
                Tokens/min: Letzter Stand                                                          |
                Requests/day             800 requests  [##......]    20%  faellig                  |
                Requests/day: Letzter Stand                                                        |

Unbekannt ist nicht null. Geteilte Budgets nicht addieren. Details: lain usage --full
```
