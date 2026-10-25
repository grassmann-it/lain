# Bestätigte Designauswahl und vertiefte UX-Pilot-Prüfung

Stand 24.10.2026. Quelle: ausdrückliche A/B/C-Auswahl des Nutzers im Chat.
Diese Entscheidung bestätigt die genannten Layoutmuster. Der anschließende
Auftrag umfasst weitere UX-Pilot-Entwürfe, eigene Bewertung und Arbeitspakete;
keine Produktimplementierung, Installation oder Dienstneustarts in dieser Runde.

## Bestätigte Auswahl

| Bereich | Entscheidung | Verbindliche Folge |
| --- | --- | --- |
| Accounts breit | A, `KHVTKTujolE1ODNri20R` | Eine Accountspalte mit dauerhaft reserviertem 400-px-Detail bei 1120×780. Öffnen und Schließen packt das Inventar nicht neu. |
| Accounts schmal | A, `2acYFlKqIOMTxJllD6A2` | Bei 800×560 Liste und Detail nebeneinander; Referenz 340 px Inventar, restliche Breite Detail. Die frühere Backseite ist als Standard ersetzt. |
| Kompakter Monitor | B, `5saYPjGTtD3ddI1gMcN6`, ergänzt | Tabelle aller Quotafenster, ergänzt um platzsparend integrierte Verbrauchsbalken ähnlich A. Zahlen und Reset bleiben sichtbar. |
| Models | A, `yWFvRTiVdekXpNLsmnhR` | Tabellarisches Inventar mit benachbartem Detailpanel. |
| Usage | A, `CO4rai9QEKy08yaYNjbT` | Ergebnistabelle mit benachbartem Seitendetail. Die Empfehlung für B aus der vorherigen Runde ist nicht übernommen. |

### Bestätigte Folgeauswahl

Quelle: ausdrückliche Nachricht „Weitere Auswahl für lain“ am 24.10.2026.
Die drei Detailfragen sind damit entschieden; die vorherigen fünf Grundmuster
bleiben bestehen.

| Detailfrage | Entscheidung | Verbindliche Folge / dokumentierte Abwägung |
| --- | --- | --- |
| Balken innerhalb der gewählten Monitortabelle B | **A**, `MON-BARS-A`, `trX82PWMypys2YJz9zSv` | Lange, gleichmäßig breite Verbrauchsbalken unter den Zahlen. Zahlen, Restwert und Reset bleiben sichtbar. Mehr Zeilenhöhe als Inline-B ist akzeptiert. |
| Prüfung vor globalem Apply | **B**, `PROFILE-REVIEW-B`, `4kaeLSeKtOfdE3VRdvRZ` | Feste Reviewfläche im Profildetail, Profilliste bleibt sichtbar. Ausdrückliche Bestätigung und Revisionsschutz gelten auch ohne Bestätigungsdialog. Profil-/Kontextwechsel dürfen keine alte Vorschau auf ein anderes Ziel umhängen. |
| Live schmal bei 800×560 | **A**, `LIVE-NARROW-A`, `afOjxQu7vvu7dl2k2yUN` | Dauerhafte Requestliste links, Referenz 200 px, Ausgabe daneben. Kein automatischer Ersatz durch einen alleinigen Selector oder Listen-/Outputseitenwechsel. Weniger Ausgabebreite ist akzeptiert. |

Die früheren Empfehlungen Apply A und Live B sind nicht übernommen. Die
Alternativen bleiben als Vergleich erhalten. Diese Auswahl bestätigt die
genannten Präsentationen; weitere Fehlerbilder, Simple-Live in More und die
native Produktabnahme werden dadurch nicht pauschal freigegeben. Die Auswahl
führt zu keinen weiteren Generierungen oder Credits und startet keine Umsetzung.

[Bisherige Galerie](uxpilot-comparison-2026-10-24.html) und
[Bewertung mit Quellen](uxpilot-comparison-2026-10-24.md) bleiben als Historie
erhalten. Assistentenempfehlungen und andere Varianten ersetzen diese Auswahl nicht.
Die Monitortabelle gilt für den bewusst gewählten kompakten Monitor. Im Gateway
bleiben Accountzusammenfassungen die Grundlage; kein pauschaler Tabellenzwang.

Verbrauchsbalken zeigen einheitlich verbrauchten Anteil. Reine Geld-/Punktguthaben
ohne Nenner sowie unbekannte Mengen erhalten keinen Prozentbalken. Numerische
Werte, lokale Aktualität und unbestätigte vergangene Resets bleiben unabhängig
von der Darstellungsdichte erhalten. Ein leeres Einzelbudget sperrt nicht
behauptetermaßen den gesamten Account. Gemeinsame Budgets werden nicht addiert.

## Umfang der vertieften Runde

Zusätzlich bis zu **400 UX-Pilot-Credits** ausdrücklich freigegeben. Bisherige
Runden verbrauchten 138 von 500 Credits. Die neue Runde wird separat abgerechnet;
die 400 zusätzlichen Credits sind eine Obergrenze, kein Verbrauchsziel.

Neue [UX-Pilot-Seite](https://uxpilot.ai/a/ui-design?page=zYyjjmz0kLDn1DsLhld0).
Entwürfe wurden selbst geprüft. Die oben ausdrücklich gewählten Präsentationen
sind bestätigt; übrige Alternativen und neue Zustandsdarstellungen bleiben Vorschläge.

Schwerpunkte: Monitorbalken; schmale Accounts-/Modellexperten; Profiles mit
globalem Apply, Revisionskonflikt und unbekanntem Recoveryzustand; Connect bei
Ablauf und Reconnect; partielle Usage; Live bei Pause/Retention/Sourcewechsel;
Settings bei engem Fenster und Speicherfehler. Gleiche Zustände werden nur dort
als Alternativen gegenübergestellt, wo eine echte Layoutentscheidung offen ist.

## Galerie und Ergebnis

- [Offlinegalerie mit bestätigter Folgeauswahl A/B/A](uxpilot-followup-2026-10-24.html).
- [Manifest mit allen 29 Design-IDs, echten Bild-URLs und Einzelbewertungen](uxpilot-followup-2026-10-24.json).
- [UX-Pilot-Canvas mit den geprüften neuen Fassungen](https://uxpilot.ai/a/ui-design?page=zYyjjmz0kLDn1DsLhld0),
  Gruppe `6HYcOoDHlTToIRz3SvsE`.
- [Sechs Folgepakete UXD-10–15](ux-design-work-packages/README.md#pakete-und-reihenfolge).

Die HTML-Datei enthält 29 Bilder direkt: sechs auswählbare Entwürfe, 18 weitere
Zustands-/Scrollansichten und die fünf bestätigten Grundmuster als Referenz.
Sie ist etwa 2,7 MB groß und funktioniert ohne Server oder Artefaktordner.
Die Zustandsbilder sind nach Themen aufklappbar. Bilder anklicken zum Vergrößern,
Varianten vergleichen, optional eine lokale Auswahl/Notiz eingeben und Antworttext
kopieren. Die Galerie nennt die inzwischen bestätigte Folgeauswahl A/B/A oben;
die Vergleichsbedienung und früheren Bewertungen bleiben erhalten. Lokale Favoriten
ändern keine dokumentierte Entscheidung. Die fünf Grundmuster werden nicht erneut
abgefragt. Lokale Eingaben bleiben im Browser, bis der Nutzer den Text selbst teilt.

```sh
firefox /home/tomka/Desktop/lain/docs/uxpilot-followup-2026-10-24.html
```

## Frühere Bewertung der drei inzwischen entschiedenen Detailfragen

Die folgenden Empfehlungen dokumentieren die Prüfung vor der Folgeauswahl.
Maßgeblich ist jetzt die bestätigte Folgeauswahl A/B/A oben; insbesondere wurden
die Empfehlungen Apply A und Live B nicht übernommen.
Sie setzen voraus, dass im Monitor Quotafenster verglichen werden, Apply eine
bewusste globale Aktion bleibt und Live im schmalen Fenster viel Text zeigen muss.

| Frage | A | B | Eigene Bewertung |
| --- | --- | --- | --- |
| Balken innerhalb der bereits gewählten Monitortabelle B | Lange Balken unter den Zahlen | 76-px-Balken neben den Zahlen | **A empfohlen:** gleiche lange Skala erleichtert Vergleiche. B spart beim vollständigen Fixturebestand 91 px Höhe: 781 statt 872 px Inhalt bei jeweils 436 px Scrollfenster. Beide behalten dieselben Werte und lokalen Fehlerhinweise. |
| Prüfung vor globalem Apply | Eigener Bestätigungsdialog | Feste Reviewfläche im Profildetail | **A empfohlen:** globale Wirkung und letzte Bestätigung sind deutlich. Nachteil: unterbricht Navigation und verlangt sauberen Dialogfokus. B hält die Profilliste sichtbar, braucht aber einen zusätzlichen Seitenzustand samt Navigationsschutz. |
| Live bei 800×560 | Permanente 200-px-Requestliste | Requestselector und bei Bedarf lokale Liste | **B empfohlen:** volle Ausgabebreite für Code. A eignet sich besser für häufigen Requestwechsel, kostet aber ein Viertel der Breite. Die bestätigte Accounts-Doppelspalte entscheidet diese separate Frage nicht. |

Die jeweiligen Präsentationen sind entschieden. Quotasemantik,
Revisionsschutz und Sourceidentität hängen nicht von einer A/B-Wahl ab.
Die schon früher vorgeschlagene Verlagerung von Live in More im Simple-Modus
bleibt eine eigene, bisher nicht bestätigte Navigationsfrage. Diese Runde
ändert sie nicht stillschweigend.

## Zusätzliche Gegenprüfungen und abgeleitete Änderungen

| Bereich / Bilder im Manifest | Ergebnis der eigenen Prüfung | Folge |
| --- | --- | --- |
| `MON-BARS-A/B` und `-LOWER` | Alle Fenster einschließlich Purchased/Free/Unknown, Kimi Monthly total/coding und Z.ai Wochenwert bleiben erhalten. Guthaben ohne Nenner haben keinen Balken. Fälliger Reset ist unbestätigt, kein automatisch bestätigtes Auffüllen. | UXD-10: einheitlicher verbrauchter Anteil, vollständige Scrollansichten, lokale Aktualität und Fehlerstatus. |
| `ACC-NARROW-EXPERT`, `-LOWER`, `ACC-NARROW-LONGNAME` | Nebeneinander bleibt auch schmal brauchbar. Lange Namen kosten zusätzliche Höhe; getrennte Scrollflächen und feste Detailaktionen sind nötig. Ein Quota-Refreshfehler belegt keinen OAuthfehler. | UXD-10: Scrollanker, lange Namen, Experteninformationen und unveränderte Inventarbreite nativ prüfen. |
| `MODEL-NARROW-EDITOR` und `-LOWER` | Route/Katalog, Bindung, Enabled/Visible, Policycheck und Fallbackfelder benötigen einen eigenen Editor-Scrollbereich mit festen Save/Cancel-Aktionen. Das breite gewählte Tabelleninventar bleibt gesetzt. | UXD-10: vollständige Feldzuordnung und Erreichbarkeit; keinen vereinfachten Screenshot als vollständigen Editor übernehmen. |
| `CONNECT-EXPIRED`, `CONNECT-RECONNECT` | Ablauf braucht einen ausdrücklichen neuen Versuch. Reconnect muss dieselbe Accountidentität erhalten; letzte Quotas werden nicht als aktuell ausgegeben. | UXD-11: Cancel/completed-Races, Back/Reopen und Identitätserhalt gegen reale Methodenverträge prüfen. Kein neuer Providerfähigkeitsbeleg aus einem Bild. |
| `PROFILE-CONFLICT`, `PROFILE-RECOVERY`, `PROFILE-NARROW-EDITOR` und `-LOWER` | Viewing, gespeichert und Applied bleiben getrennt. Nach Revisionskonflikt neu prüfen; bei Verbindungsverlust ist der Applyausgang unbekannt. Kein Force apply, Auto-Reapply oder behaupteter Rollback. | UXD-12: reale Vorschau-/Commit-/Recoveryzuordnung, feste Editoraktionen und aktuelle Fallbackgrenze. |
| `USAGE-PARTIAL`, `USAGE-NARROW` | Eine unbekannte Tokenkomponente macht die bekannte Summe zur Teilsumme. Accountauswahl macht globale First-output-Werte nicht gruppenlokal; Filterdraft ist keine angewendete Query. Schmale Spalten bleiben ein Lesbarkeitsrisiko. | UXD-13: Komponenten-/Scopezuordnung und Draftschutz. Native Kernspalten und Beschriftungen bei 800×560 qualifizieren. |
| `LIVE-NARROW-A/B`, `LIVE-SOURCE-CHANGED` | Pause stoppt nur die Anzeige. Retention kann eine Lücke verursachen; die Anzahl fehlender Ereignisse ist unbekannt. Eine gleiche Request-ID aus neuer Quelle belegt keine Kontinuität. | UXD-14: Sourcegebundene Auswahl, lokaler alter Ausschnitt, Follow-Verhalten und Pickerfokus. |
| `SETTINGS-NARROW-ERROR` und `-LOWER` | Fehler steht direkt bei Close to tray, vorheriger gespeicherter Wert wird wiederhergestellt. Traypräferenz und tatsächliche Trayverfügbarkeit bleiben getrennt; Hilfe ist durch Scroll erreichbar. | UXD-15: lokaler Speicherfehler, Retry, Back-Kontext und native gemeinsame Bedienprüfung. |

Ein gezielter lesender Quellabgleich bestätigt die Fallbackgrenze:
[MainWindow.Profiles.cs](../src/Lain.Desktop/MainWindow.Profiles.cs) erklärt beim
Fallbackeditor, dass gespeicherte Fallbackreihenfolgen aktuell keine ausführbare
Fallbackruntime sind und Apply Fallbackgraphen ablehnt. Bezugsstand des erneuten
Abgleichs: HEAD `c1e49bace17becc9b4dabc1b50b1c728163d3235` mit parallelen Änderungen.
Die Entwürfe zeigen deshalb lokal den verständlichen Hinweis „Fallback selections
can be saved. Applying a fallback configuration is currently unavailable.“
Das ist ein belegter aktueller Grenzfall, keine neue Architekturentscheidung.

Die partielle Usagefixture ist bewusst von der vollständigen getrennt:
128 Requests, 132 Attempts, vier historische Fehler = 3,0 % der Attempts;
43.200 + 21.000 = **64.200 bekannte Tokens**, Kimi Unknown. Die vollständige
schmale Fixture hat 81.986 Tokens einschließlich Kimi 17.786. Historische Fehler
und Completion errors sind verschiedene Messgrößen. Globales First output:
P50 240 ms/P95 510 ms bei 120/132 gemessenen geeigneten Attempts. Alle Werte
sind synthetische Designfixtures, keine Messung des installierten Systems.

## Durchgeführte UX-Pilot-Läufe und Kosten

| Lauf | Job-ID | Erzeugte Ausgangsentwürfe | Credits |
| --- | --- | ---: | ---: |
| Monitorbalken, schmale Accounts-/Modelldetails | `S44GyF7W8tIKX65IObBs` | 4 | 24 |
| Profiles/Apply/Recovery und Connect | `qngnB1RKU4n3TWXCDJtH` | 7 | 24 |
| Live, partielle/schmale Usage und Settingsfehler | `SIjM3nKFUvOZOMhT6FDy` | 5 | 21 |
| UX-Pilot-Review des bestätigten schmalen Accounts A | `0N1p2lrGkBQhk6u9aQlv` | Review ohne Kommentare | 3 |
| **Diese Zusatzrunde** | | **16 Ausgangsentwürfe** | **72 / 400** |
| Frühere Runden | | | 138 |
| **Kumulativ** | | | **210** |

Die kostenlosen HTML-Versionen erzeugten acht zusätzliche Zustands-/Scrollbilder
und korrigierten alle finalen Fassungen; sie meldeten jeweils `creditsUsed: 0`.
328 der zusätzlichen 400 Credits bleiben ungenutzt. Weitere kostenpflichtige
Varianten versprachen vor der inzwischen getroffenen Detailauswahl wenig neuen Nutzen.
Keine Heatmap- oder Reactgenerierung.

## Eigene Bewertung des automatischen Reviews

UX Pilot bestätigt den Kontextvorteil der Doppelspalte und nennt kleine
Actions, dichte Metadaten und Scrollsichtbarkeit als Risiken. Diese Hinweise
sind brauchbar und in UXD-10/15 aufgenommen. Der Bericht wird nicht ungeprüft
als fachliche oder Accessibilityfreigabe übernommen:

- Die behauptete Kontrastschwäche unter 3:1 ist für die genannten Vollfarben
  widerlegt: `#89938E` auf `#08100D` ergibt etwa **6,079:1**, auf `#0C1511`
  etwa **5,860:1**. Tatsächliche Kombinationen, Opazität und Disabledzustände
  brauchen trotzdem eine eigene Messung.
- Ein gemeldeter Prozentwert braucht keine erfundene absolute Menge. Der
  relative 72-%-Verbrauch ist gültig; Guthaben ohne Nenner und Unknown bleiben
  davon verschiedene Fälle.
- Vorgeschlagene Warnschwellen 70/90 % und roter „Fehler“ allein bei vergangenem
  Reset sind keine bestätigten Produktregeln und wurden nicht übernommen.
- 12-px-Metadaten dieser Runde sind eine Designempfehlung, keine erfundene
  WCAG-Mindestschriftgröße. Kontrastzahlen zertifizieren nicht die ganze App.

## Korrekturen, tatsächliche Prüfung und Grenzen

Alle Generatororiginale wurden gelesen und als PNG angesehen. Die eigene Prüfung
fand falsche Gatewaynavigation und Appliedstatus im Monitor, fehlende Wortmarke,
falsche Footersteuerungen, verkürzte Quotas, erfundene OAuthdiagnosen, Modellnamen,
Organisation/Region, Zeiten, Version/Diagnosefunktion, unpassende Fehlermetriken,
fehlende Attempts und erfundene Liveereignisse. Diese Angaben wurden entfernt
oder durch die gelieferten synthetischen Werte ersetzt. Katalog-/Routenfelder,
Policycheck und der Hinweis zur Fallbackgrenze wurden ergänzt. Die Überlappung
von Profile-Policyüberschrift und Check-Button wurde ebenfalls korrigiert.

Ein zusätzlicher Exportfehler war nur in den echten UX-Pilot-PNGs sichtbar:
Der Renderer verlängerte trotz Bodyhöhe die App bis zur gesamten Inhaltshöhe.
Deshalb wurde die Workspacehöhe ausdrücklich auf 472 bzw. 692 px begrenzt;
Inhalte scrollen intern. Die alten Korrekturversionen wurden durch neue ersetzt.
Die **24 finalen echten UX-Pilot-PNGs wurden danach selbst angesehen**, die vier
zuletzt korrigierten Editorbilder erneut. Die Galerie zeigt den Appausschnitt
per CSS; die heruntergeladenen PNGs bleiben unverändert.

Auf der neuen Seite liegen 24 finale Ansichten bei y=0 nebeneinander;
16 Generatororiginale bei y=1600 und 28 überholte Zwischenversionen bei y=3200.
Alle mit 1560 px horizontalem Abstand, alte Versionen ausdrücklich als überholt
benannt. Erneute Prüfung der tatsächlichen Seitenkoordinaten nach
`move_design_to_page`: **68 Frames, keine Überschneidung**, selbst bei
konservativen 1440×1024-PNG-Flächen. Frühere Seiten wurden nicht umgebaut.

Lokale Galerieprüfung mit vorhandenem Chromium/Playwright: **29 Bilder, sechs
Optionen**, keine Kartenüberschneidung/Überbreite bei 1600, 1120, 800 und 390 px.
Auswahl und Notizen bleiben nach Reload erhalten, Reset leert beides. Bildzoom
funktioniert auch schmal, blockiertes Clipboard öffnet markierbaren Antworttext,
gesperrter lokaler Speicher lässt die Auswahl und den Antworttext weiter nutzen.
Offline **null externe Requests und keine JS-Fehler**. Zwei Galeriefenster wurden
zusätzlich visuell geprüft. Alle 24 Design-HTMLs haben korrekte App-/Footermaße,
genau einen Informationsmodusdropdown im Footer und in Gatewayframes direkten
Profileszugang. Lokale Rohberichte liegen ignoriert unter
`artifacts/uxpilot-deepening-20261024/`.

**Keine native Produktabnahme:** Statische Bilder belegen keine tatsächliche
Tastatur-/Fokus-/Kontextmenübedienung, Polling-/Scrollgarantie, vollständige
Expertenfeldabdeckung oder Host-/Providerfähigkeit. Diese Nachweise bleiben in
UXD-09/15 und den jeweiligen Fachpaketen offen. In dieser Designrunde nur
Design-/Galerieartefakte und Dokumentation geändert; keine Produktcodeänderungen,
Produktbuilds, Installation, Dienstneustarts, echten Logins, Provideraktionen
oder Commits. Parallele Änderungen wurden erhalten.

## Übergabe in sechs Ergänzungspaketen

Die Pakete sind Deltas für die bestehenden Owner. Bestätigte Muster sind
übernahmebereit, einschließlich der Folgeauswahl Monitor A, Apply B und Live A.
Die früheren Empfehlungen sind keine verbleibenden Entscheidungsblocker. Fachliche
Verträge und reale AC-/ST-Anschlüsse sind vor der jeweils abhängigen Arbeit
nachzuweisen. Keine neue technische Schnittstelle aus einem Design erfinden.

1. [UXD-10: gewählte Panels, Monitorbalken und vollständige schmale Editoren](ux-design-work-packages/10-selected-layouts-monitor.md).
2. [UXD-11: Connect bei Ablauf/Cancel und Reconnect derselben Identität](ux-design-work-packages/11-connect-interruptions.md).
3. [UXD-12: Applyvorschau, Revisionskonflikt und unbekannter Ausgang](ux-design-work-packages/12-profiles-apply-recovery.md).
4. [UXD-13: Usage-Teilmessung, globaler Scope und Filterdraft](ux-design-work-packages/13-usage-partial-scope.md).
5. [UXD-14: schmale Liveausgabe, Retentionlücke und Sourceidentität](ux-design-work-packages/14-live-pause-source.md).
6. [UXD-15: Settingsfehler und gemeinsame native Abnahme](ux-design-work-packages/15-settings-and-native-gates.md).
