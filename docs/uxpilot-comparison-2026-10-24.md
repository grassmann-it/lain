# UX-Pilot-Gegenprüfung und A/B/C-Vergleich

Stand 24.10.2026. Auf Wunsch nach weiteren Prüfungen und auswählbaren
Designalternativen erstellt. **Entwürfe zur Auswahl, keine neue Produktfreigabe.**
Die parallel laufende Implementierung ist davon getrennt und wurde hier nicht verändert.

**Nachträgliche ausdrückliche Auswahl:** Accounts breit A, Accounts schmal A,
Monitor B mit ergänzten Verbrauchsbalken, Models A und Usage A sind inzwischen
bestätigt. [Maßgebliche Auswahl und Folgeprüfung](uxpilot-selection-2026-10-24.md),
[Folgegalerie mit bestätigter Auswahl Monitor A / Apply B / Live A](uxpilot-followup-2026-10-24.html).
Die folgenden Empfehlungen und offenen Punkte protokollieren die frühere Runde;
insbesondere schmale Accounts B und Usage B wurden nicht übernommen.

## Direkt öffnen und auswählen

- [Offline-HTML mit Bildern, A/B/C-Auswahl und Antworttext](uxpilot-comparison-2026-10-24.html).
- [Neue UX-Pilot-Seite](https://uxpilot.ai/a/ui-design?page=RIYV8uJ5sbN92n08oTjz).
- [Manifest mit allen Design-IDs, echten PNG-URLs und Bewertungen](uxpilot-comparison-2026-10-24.json).
- [Bisherige 14 Entwürfe und Bewertung](uxpilot-design-results-2026-10-24.md).
- [Arbeitspakete UXD-00–09](ux-design-work-packages/README.md).

Die HTML-Datei ist etwa 3,5 MB groß und enthält alle 31 Screenshots direkt:
15 auswählbare Entwürfe, zwei zusätzliche Zustände und die 14 bisherigen
Referenzen. Zum Ansehen wird kein Server und kein Artefaktordner benötigt.
Die Datei im Browser öffnen, Bilder zum Vergrößern anklicken und je Bereich
A/B/C wählen. Kombinationen sind ausdrücklich möglich. Eigene Anmerkungen
ergänzen und „Auswahl kopieren“ drücken; der Antworttext kann dann in den Chat.
Falls die lokale Clipboard-API gesperrt ist, erscheint markierbarer Text.

Favoriten werden, soweit vom Browser erlaubt, lokal gespeichert. Es wird nichts
automatisch versendet oder auf das Produkt angewendet. Bei gesperrtem lokalem
Speicher bleibt der kopierbare Antworttext nutzbar. Es ist kein Favorit vorausgewählt.

## Vergleiche und eigene Bewertung

Für einen sinnvollen Vergleich bleiben Katze, Mint, dunkler Look, Daten und
fachliche Bedeutung gleich. Verglichen wird die Anordnung und Bedienidee.

| Bereich | A | B | C | Empfehlung und wesentliche Abwägung |
| --- | --- | --- | --- | --- |
| Accounts breit, 1120×780 | Eine Accountspalte + 400-px-Inspector | Zwei Kartenspalten + 320-px-Inspector | Quota-Zeilen + 400-px-Inspector | **A** als ruhige Basis. B zeigt mehr Accounts, hat engere Details und Leerraum bei ungleichen Kartenhöhen. C erleichtert zeilenweises Vergleichen. |
| Accounts schmal, 800×560 | Liste und Detail nebeneinander | Eigene Detailseite mit Back | Liste oben, Detail unten | **B** liest sich am besten. A bewahrt gleichzeitigen Kontext. C hat wenig Höhe in beiden Scrollbereichen und bleibt eher eine Vergleichsoption. |
| Monitor/Barebones, 800×560 | Kompakte Accountliste | Tabelle aller Quotafenster | Zwei Accountspalten | **B**, wenn der Hauptzweck das Vergleichen vieler Fenster ist. A ist für gelegentliches Prüfen ruhiger. C verliert bei langen Accounts Platz durch ungleiche Höhen. |
| Models, 1120×780 | Tabelle + Inspector | Nach bekannter Accountbindung gruppiert | Eigene Bearbeitungsseite | **A** für den 45er-Bestand. B passt zur Arbeit über Accounts. C ist ein zusätzliches Editiermuster; der Einstieg über ein Inventar wird weiterhin gebraucht. |
| Usage, 1120×780 | Tabelle + Seitendetail | Breite Tabelle + lokale Details | Ergebniskarten je Account | **B** für kompakte Auswertungen. A ist bei häufigem Drilldown nützlich. C ist bei wenigen Gruppen angenehm, skaliert schlechter bei vielen. |

Die Empfehlungen ersetzen keine Nutzerentscheidung. Die breite reservierte
Accountsfläche und die genaue Navigation bleiben die bereits dokumentierten
offenen Übernahmen. Diese Runde ergänzt keine pauschale neue Navigationsvariante.
Profiles ist im Gateway weiterhin direkt erreichbar; genau ein Informations-
modusdropdown bleibt rechts unten. Monitor enthält keine nicht vorhandenen
Gateway-/Profil-/Liveaktionen und behauptet keine Requesthistorie.

Zusätzlich geprüft und dargestellt:

- **Noch kein Account:** klarer Verbindungseinstieg, Modellwahl danach optional;
  keine erfundene Quota oder Erfolgsmeldung.
- **Gateway offline:** bekannter Aufbau und letzte Werte bleiben mit Warnung
  sichtbar, einschließlich zuletzt bekanntem Applied Workspace 2. Kein Wechsel
  zum Monitor, keine bestätigte Auffüllung und keine ausführbare Refreshbehauptung.

## Tatsächliche Generierungen und Korrekturen

Eigene neue Seite `RIYV8uJ5sbN92n08oTjz`, geprüfte Gruppe
`GQC52QsOd0Fj7G6HO784`. Drei bestehende geprüfte Frames wurden als A-Referenzen
kopiert; die ursprüngliche Seite blieb unverändert. Vierzehn neue Entwürfe
wurden über UX-Pilot-MCP erzeugt und anschließend dort als HTML-Versionen korrigiert.

| Lauf | Job-ID | Erzeugte Kandidaten | Credits |
| --- | --- | ---: | ---: |
| Accounts breit/schmal | `0i07SrliJEOY6SgmWoQP` | 5 | 18 |
| Standalone-Monitor | `cDkR38ojxa5ELSixcPcU` | 3 | 22 |
| Models, Usage, Leer/Offline | `T032xLCd69WRaySJAAay` | 6 | 28 |
| **Diese Runde** | | **14** | **68** |
| Vorherige Runde | | 14 finale Referenzen | 70 |
| **Gesamt** | | | **138 / 500** |

Kostenlose deterministische HTML-Edits meldeten jeweils `creditsUsed: 0`.
Keine weiteren Generierungsläufe und keine kostenpflichtigen Review-/Heatmap-/Reactläufe.

Eigene Sichtprüfung fand deutliche Generatorabweichungen: zuerst falsche blaue
Marke, fehlende Katze/Navigation und unvollständige Quotafenster; später doppelte
Navigation, zusätzlicher Profilselector, erfundene Performance-/Coverage-/
Offlinewerte sowie relative Zeitangaben. Sie wurden vor Aufnahme in die Auswahl
entfernt bzw. durch die gelieferten synthetischen Werte ersetzt.

Die geprüften Fassungen sind **bearbeitete UX-Pilot-Entwürfe mit echten neuen
UX-Pilot-PNGs**. Für Accounts und Monitor wurden die belegte Shell und vollständigen
Quotazeilen aus dem vorher geprüften Entwurf in die alternativen Anordnungen
übernommen. Models/Usage behalten ihre generierten Grundlayouts mit korrigiertem
Inhalt und gemeinsamer Shell. Offline verwendet den bekannten Accountbestand
statt der erfundenen Generatorquotas. Keine lokal nachgezeichneten Ersatzbilder.

Alle finalen Bilder wurden selbst angesehen, die sechs nachträglich verfeinerten
Fassungen erneut heruntergeladen. Auf der neuen Seite liegen die 17 finalen
Ansichten nebeneinander bei y=0; die 14 fehlerhaften Ausgangsfassungen separat
bei y=1600, ausdrücklich als überholt benannt. Abstand 1560 Pixel pro Frame.
Erneut gelesene seitenbezogene Koordinaten: **31 Frames ohne Überschneidung**,
auch mit konservativen 1440×1024-PNG-Flächen. Metadatenpositionen allein änderten
zunächst nur globale Fallbacks; `move_design_to_page` setzte die tatsächlichen
Seitenpositionen. Die erneute Prüfung benutzt diese Seitenpositionen.

## Prüfung und Grenzen

Die lokale HTML-Galerie wurde mit dem vorhandenen Chromium/Playwright geprüft:
15 Optionen, 31 eingebettete Bilder, kein horizontaler Überlauf und keine
Überlappung der Vergleichskarten bei 1600, 1120, 800 und 390 Pixel Fensterbreite.
Auswahl, Anmerkung, Erhalt nach Reload, Reset, Bildzoom und Clipboard-Fallback
funktionieren. Beim Offlineaufruf **null externe Requests und keine JS-Fehler**.
Vorschaubilder zeigen per CSS den Appbereich; Original-PNGs bleiben unverändert.

Ergänzende lokale Geometrieprüfung der 14 neuen Design-HTMLs: korrekte
1120×780 bzw. 800×560, Footer innerhalb des Fensters, ein Informationsdropdown
im Footer, originale Katze und direkter Profileszugang in allen Gatewayframes.
Diese technische Prüfung ergänzt die Sichtung der tatsächlichen UX-Pilot-PNGs.

**Keine native Produktabnahme:** Bilder belegen weder echte Tastatur-/Fokus-
und Kontextmenübedienung noch Polling-/Scrollgarantien, Host-/Loginfähigkeiten
oder vollständige Advancededitoren. Die in UXD-09 beschriebenen Nachweise bleiben
erforderlich. Die lokalen Prüfungen betreffen ausschließlich diese Galerie und
Designartefakte; kein Produktbuild, kein installiertes System, keine Installation,
Dienstneustarts, echten Logins oder Provideraktionen in dieser Runde.

Weitere gezielte UX-Pilot-Checks sind nach der A/B/C-Auswahl sinnvoll:

1. Gewählte Muster durchgängig auf Profiles und globales Apply übertragen:
   ausstehende Änderungen, inzwischen geänderte Revision, Fehler und Recovery.
2. Connect bei Abbruch, Ablauf, fehlender Voraussetzung und Reconnect zeigen;
   eine bestehende Accountidentität darf dabei nicht wie ein neuer Account wirken.
3. Usage mit Teilmessungen und Sourcefehler sowie Live mit Pause, abgelaufener
   Retention und Sourcewechsel prüfen. Keine Scheingenauigkeit aus fehlenden Werten.
4. Vollständige Expertenfelder und lange/mehrsprachige Namen in den gewählten
   schmalen Mustern prüfen. Abgeschnittene Controls nicht durch Weglassen lösen.

Diese Folgechecks sind gezielt zurückgestellt, bis die Grundmuster gewählt sind;
keine unüberschaubare Serie weiterer rein kosmetischer Varianten erzeugt.

Reproduzierbare lokale Zusatzartefakte liegen unter
`artifacts/uxpilot-alternatives-20261024/`: Aufträge, ursprüngliche/finale HTMLs
und PNGs, `verification.json`, `canvas-check.json` und Galeriebuilder. Dieser
Ordner ist ignoriert. Für die Auswahl genügt die einzelne selbständige HTML-Datei;
Manifest und dieses Protokoll enthalten die notwendigen dauerhaften Quelllinks.
