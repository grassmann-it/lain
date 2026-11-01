# CLI, Namen und Monitoring: gemeinsamer Review

Stand **25.10.2026**, Ausgangscode `cbf083f`; installierte CLI `b49096f`,
Desktop `67fb26c`, Host `5f41fba`. Auftrag: drei GPT-6 Astra für konkrete
UX-Vorschläge, zwei Astra für Anbieterrecherche; vier GPT-6.1 Sol ergänzen die
vollständige Anbieterabdeckung. Ergebnisse sind Entwürfe und Quellenbefunde,
keine neue Produktimplementierung oder Freigabe bisher gesperrter Methoden.

## Was der Screenshot widerlegt

Die bisherigen technischen Prüfungen belegen korrekte Auswahl und Umbrüche,
aber keine überzeugende Informationshierarchie in der tatsächlichen
Terminalpalette. Die Ausgabe bleibt eine lange Textfolge. Grüne Balken sind
schwer lesbar, rohe Provider-IDs und lange Standardnamen verbrauchen Platz.
Die pauschalen kurzen Hinweise verstecken das entscheidende Warum.
Der neue Nutzerbefund öffnet deshalb die Gestaltung erneut.

**Bestätigte Anforderungen dieser Runde:**

- CLI soll Breite sinnvoll nutzen: vergleichbare Tabellen, bei genügend Platz
  auch eine sinnvolle zweite Spalte. Zweispaltigkeit ist eine Möglichkeit,
  kein Zwang für schmale Terminals oder Pipes.
- Ein einzelner Anbieteraccount bekommt eine kurze Anbieterüberschrift:
  etwa `Gemini`, `NVIDIA`, `Cloudflare`, `Kilo`. Lange erzeugte `My … account`-
  Namen und rohe Provider-IDs gehören nicht in jede Standardzeile.
- Mehrere Accounts bleiben eindeutig; eigene Namen und stabile IDs erhalten.
  Darstellung kürzen und gespeicherte Daten umbenennen sind getrennte Vorgänge.
- Quotas erklären konkret, warum Daten fehlen und welcher Schritt hilft.
  Tarif-, OAuth-, Rechte- oder Quellenbedingungen nur mit Beleg behaupten.
- Bestmöglichen Anschluss je Anbieter suchen: bestehenden Zugang wiederverwenden,
  belegten Browserlogin oder Hersteller-CLI nutzen, nötigenfalls gezielte
  Monitorrechte ergänzen. Eine neue Anmeldung allein garantiert keine Reportingquelle.
- Modellwahl zusätzlich nach Anbietern und eigenen Sammlungen wie `Free` und
  `IT Sec` organisieren. In der Sammlung soll oben der Autorouter und darunter
  die explizite Modellwahl stehen. Nach ausdrücklicher Screenshotpräzisierung
  ist das Hauptziel **Codex CLI `/model` mit zwei Auswahlstufen**. Die konkrete
  Routerregel bleibt zu entwerfen.

## Gestaltungsempfehlung

Die [offline öffnbare Vorschau](layout-preview.html) vergleicht eine gruppierte
Tabelle mit einer semantischen Zweispaltenansicht bei 80/120/160 Spalten.
Alle Daten sind synthetisch. Die Vorschau ist ein Lesefolgenentwurf und kein
Screenshot der installierten App.

Empfohlen ist eine ruhige Tabelle mit **Anbieter, Fenster, verfügbarem Betrag,
Reset und kurzem Zustand**. Verbrauch/Balken sind ergänzende Information, wenn
Platz vorhanden ist. Pro Account erscheinen die relevanten kurzen Fenster;
zusätzliche Monats-/Creditdetails bleiben über den vorhandenen Detailweg erreichbar.
Einheiten, unbekannte Werte, alte Messungen und geteilte Pools behalten ihre Bedeutung.

Auf sehr breiten Terminals kann links diese Messwertliste und rechts eine
eigenständige Liste mit **Grund und nächstem Schritt** stehen. Kein willkürliches
Verteilen von Accountkarten: Zeilen beider Listen sind keine zusammengehörigen
Datensätze. Die notwendige Mindestbreite ergibt sich aus Spalten und Inhalten;
160 Spalten ist ein Vorschlagsbeispiel, keine bereits akzeptierte feste Schwelle.
Schmal und umgeleitet bleibt die Ausgabe linear. Bedeutung muss ohne Farbe
verständlich sein; wichtige Gründe und Handlungen dürfen nicht abgedunkelt werden.

Die Vorschläge sind nachlesbar:

- [Adaptive Ausgabe und Vergleichsentwürfe](adaptive-layout.md).
- [Einheitliche Namen in Accounts, Pickern, CLI, Usage und Tray](names-and-structure.md).
- [Konkrete Gründe, passende Handlungen und vorhandene Verträge](quota-explanations.md).
- [Anbieter, Sammlungen, Autorouter und belegte Codexgrenzen](model-collections.md).
- [Anbieterfähigkeit und bester Anschlussweg für alle 25 Anbieter](../provider-monitoring-review-2026-10-25/README.md).

## Modellwahl nach Anbieter und Sammlung

Die neue Nutzeridee ergänzt den Review: `Free` und `IT Sec` sind eigene
Sammlungen über vorhandene Routen; die Anbieteransicht ist eine weitere Sicht
auf dieselben Objekte. Ein Modell darf in mehreren Sammlungen vorkommen, ohne
Alias, Zugang oder Usageidentität zu duplizieren. Profile bestimmen weiterhin,
welche Routen aktiviert und exportiert werden.

Gewünschter Ablauf in Codex: Sammlung wählen → zweite Liste mit `Automatisch`
oben und einzelnen Modellen darunter. Eine Gruppierung nur in lain erfüllt
diesen Auftrag nicht.
Der automatische Eintrag wird erst ausführbar, wenn eine echte Auswahlregel
qualifiziert ist. Ein Free-Router darf weder unbekannt bepreiste noch bezahlte
Ersatzrouten verwenden. Sammlung, kostenlose Ausführung und semantischer
Classifier sind getrennte Fähigkeiten.

Codex erhält heute einen flachen Katalog. Das sichtbare Menü mit „quick auto
mode“ belegt eine Modellschnellwahl, keine frei konfigurierbaren Untermenüs.
Der eingebettete Codexkatalog führt `codex-auto-review` als verstecktes
Freigabeprüfmodell; lains Export setzt ausgewählte Routen pauschal auf sichtbar.
Der gewünschte Gruppen-/Filterablauf muss deshalb zuerst am exakten Client
nachgewiesen werden. Gute Anzeigenamen und Reihenfolge bleiben sinnvolle
unabhängige Verbesserungen. Der korrigierte [Zuschnitt C0–C4](model-collections.md)
zieht die Prüfung des Codexanschlusses vor.

## Architekturfolgen, begrenzt auf das Problem

Kurznamen und Erklärungen sollen gemeinsame Clientregeln verwenden. Keine neue
Namensmutation, kein zweiter Credentialstore und keine neue allgemeine
Workflowengine sind nötig. Bestehende `QuotaPresentation`, Methoden,
Monitorzugänge, Accountbindungen und Credentialowner bleiben der Ausgangspunkt.

Der statische Katalog ist als Wahrheitsquelle unzureichend: Beim lesenden Abgleich
meldeten Gemini, Groq und Kilo dort `unsupported`, während aktuelle Quota-/
Methodenpfade existieren. Katalog, tatsächlich verfügbare Methode und letzte
Accountbeobachtung müssen getrennt, widerspruchsfrei dargestellt werden.
Ein bekannter Herstellervertrag ist weder eine freigeschaltete lain-Methode noch
ein erfolgreicher Read dieses Accounts.

`QuotaSupportDto` enthält bereits Message und Link. Diese Information kann
zuerst sinnvoll gezeigt werden. Für verlässliche knappe Gründe empfiehlt sich
später eine kleine additive Kennzeichnung am zuständigen Adapter, zusammen mit
einem Verweis auf die vorhandene nächste Aktion. Keine Tarif-/Authdiagnose aus
freiem Fehlertext erraten. Eine zusätzliche API-Erweiterung ist erst bei
konkret fehlenden Informationen nötig.

Kontobezogene CLI-Erklärung ist noch zu entwerfen: `lain usage --account ID`
ist heute ein Historyaufruf, **kein** Quota-Detailfilter. `lain accounts monitor '<ACCOUNT_ID>'`
öffnet einen Wizard und ist kein rein lesender Diagnosebefehl. Diesen Konflikt
darf ein Implementierer nicht still durch eine neue Bedeutung lösen.

## Nächste Umsetzung und offene Wahl

Die [gebündelten Arbeitspakete](work-packages.md) schließen an AC-02/03/05/06/08/09
an und ersetzen deren vorhandene Implementierung nicht. Zuerst Namen und
Erklärungen, dann das anhand der Vorschau gewählte Layout; parallel konkrete
Reader-/Anschlusslücken qualifizieren. Alle Layoutvarianten bleiben Vorschläge,
bis der Nutzer die Richtung bestätigt oder ausdrücklich die Umsetzung beauftragt.
Normale Implementierungsdetails können danach beim jeweiligen Owner bleiben.

Durchgeführt wurden Quellen-/Codeanalysen, öffentliche Dokumentationsabrufe und
ein lesender Hostabgleich von Providerkatalog sowie Provider-IDs der Accounts.
Keine Provideranmeldung, authentisierte Quotaabfrage, Inferenz, Schlüsselanlage,
Accountänderung, Installation oder Dienstoperation wurde in dieser Runde ausgeführt.
Private Accountnamen und der Nutzerscreenshot werden nicht in die Vorschau übernommen.
Die Offlinevorschau wurde per JavaScript-Syntaxprüfung und isoliertem
Headless-Chromium-Render geprüft; das ist keine Abnahme des späteren CLI-Renderers.
