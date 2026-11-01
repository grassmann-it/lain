# Anbieter, Sammlungen und Autorouter in der Modellauswahl

Stand 25.10.2026; lain-Quellen `cbf083f`. Analyse/Entwurf, keine Umsetzung.
Nutzerscreenshot `/tmp/codex-clipboard-EP5AWt.png` selbst angesehen.
OpenAI Docs verwendet: erst offizielle Suche und Seitenabruf, danach lokale Analyse.
Keine Credentials/privaten Konfigurationen gelesen, keine Clients gestartet,
keine Provider-/Inferenzaufrufe, Builds, Installation oder Produktänderungen.

## Gewünscht und vorgeschlagen

**Expliziter Nutzerwunsch:** Modelle nach Anbietern sowie eigenen Sammlungen wie
`Free` und `IT Sec` auswählen; innerhalb einer Sammlung oben den Autorouter,
darunter die einzelnen Modelle. Die genaue Umsetzung und Routingregel sind offen.
**Präzisierung mit zwei weiteren Screenshots am 25.10.2026:** Gemeint ist
ausdrücklich die zweistufige Auswahl in **Codex CLI `/model`**. Das erste Menü
mit `codex-auto-review` und `All models` soll die Gruppenauswahl ermöglichen;
das nächste Menü zeigt Autorouter und zugehörige Einzelmodelle. Eine Gruppierung
nur in der lain-Oberfläche oder lediglich schönere flache Namen erfüllt diesen
Wunsch nicht. Die früher empfohlene Reihenfolge „lain-Sammlungen zuerst“ wird
deshalb für diesen Anwendungsfall ersetzt: zuerst Codexfähigkeit nachweisen.
**Empfehlung:** Zwei Sichten auf dieselben zentralen Routen, keine Modellkopien.
Die Sammlungsmitgliedschaft ist Darstellung/Organisation; ein Autorouter ist ein
gesondertes ausführbares Ziel und benötigt eine tatsächliche Routingentscheidung.

```text
Codex /model                         [gewünschtes Verhalten]
  Free                         → gruppenspezifische Auswahl
  IT Sec                       → gruppenspezifische Auswahl
  OpenRouter                   → Auswahl dieses Anbieters
  NVIDIA                       → Auswahl dieses Anbieters
  Alle Modelle

Select Model and Effort · Free        [nach Auswahl von Free]
  Automatisch · Free           [nur ausführbar, wenn Router qualifiziert]
  Modell A · Kilo
  Modell B · OpenRouter
```

Eine Route darf in `NVIDIA` und `IT Sec` vorkommen: gleiche Route-ID, gleicher
Accountzugang, gleiche Aktivierung. Auswahl/Änderung in einer Sicht wirkt auf
dasselbe Objekt. „Anbieter“ bezeichnet den Zugangspunkt, nicht automatisch den
Modellhersteller; Hersteller bleibt ein separater Filter. `IT Sec` ist ein frei
vergebener Zweckname, kein Nachweis spezieller Eignung oder zusätzlicher Rechte.

## Belegter Iststand und Codexgrenze

| Ebene | Nachweis und Konsequenz |
| --- | --- |
| lain-Verträge | `src/Lain.Core/Contracts.cs:6`: Route mit stabiler ID/Alias/Provider/AccountIds; Profil mit RouteIds und DefaultRouteId. Kein Collection-/Autoroutervertrag. |
| Modellbestand | `ModelsService.cs:90` aggregiert Modelle nach Upstreammodell und Provider; Sammlungsauswahl muss Routen referenzieren, weil diese Aggregation verschiedene Zugänge zusammenfassen kann. |
| Codexexport | `CodexProfileAdapter.cs:53` exportiert ausschließlich `models[]`; `slug` und `display_name` sind beide Route.Alias, Beschreibung enthält Route-ID, `priority` folgt Reihenfolge. Kein Gruppenbaum. |
| Profile | Export löst die Profilrouten auf und berücksichtigt enabled/visible. Sammlung darf weder Profil automatisch aktivieren noch eine nicht ausgewählte Route in den laufenden Proxy einschleusen. |
| Runtime | `ProviderCostPolicy.cs:48` weist explizite Cross-Route-Fallbacks weiterhin als nicht qualifiziert zurück. Ein gespeicherter Fallback ist kein verfügbarer Sammlungsrouter. |
| Installierter Codex | Symlink und Paketmanifest belegen CLI `0.164.0`; `ModelCompatibility.cs:16` nennt denselben qualifizierten Zielstand. Keine laufende Sitzung verändert. |
| Sichtbare Codex-UX | Screenshot zeigt `codex-auto-review` und `All models` unter „quick auto mode“. Das installierte Binary enthält denselben Menütext und bekannte `codex-auto-*`-Bezeichnungen. Dies belegt eine Auto-Modus-Vorstufe, keine beliebigen Benutzersammlungen. |
| Nachprüfung der beiden konkreten Menüs | Der rein lesend dekodierte eingebettete Katalog von Codex 0.164.0 hat 11 Modelle und als einziges Top-Level-Feld `models`. `codex-auto-review` heißt dort `Codex Auto Review`, beschreibt ein automatisches Freigabeprüfmodell und hat `visibility: hide`, `priority: 43`. Der lain-Export macht dagegen ausgewählte Routen pauschal `list` und zeigt Alias/lain-Beschreibung. Das ist eine plausible Erklärung des sichtbaren Revieweintrags, kein Nachweis frei konfigurierbarer Gruppen. Der genaue Auto-Menüfilter bleibt unbewiesen. |

Die [offizielle Konfigurationsreferenz](https://learn.chatgpt.com/docs/config-file/config-reference)
belegt `model_catalog_json` und einen nativen Provider-Katalog-URL. Das abgerufene
[offizielle Konfigurationsschema](https://developers.openai.com/codex/config-schema.json)
beschreibt den Katalogpfad als Startoption, nicht als live pro Thread umschaltbare
Sammlung. In diesen Quellen ist keine frei definierbare Gruppen-/Untermenüoption
belegt. Das Konfigurationsschema ist nicht das vollständige Modellkatalogschema.
Die [offizielle Modelllistenbeschreibung](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
trennt sichtbaren `display_name` von dem zur Inferenz gesendeten `slug`.

**Belastbare Grenze:** Flache Katalogeinträge und menschenlesbare Namen sind
belegt; ein konfigurierbarer Baum `Anbieter → Sammlung → Modelle` in Codex 0.164.0
ist hier **nicht nachgewiesen**. Ein vollständiger lokaler Pickerquelltext oder
ein exportiertes installiertes Katalogschema lag nicht vor. Fehlende Binärstrings
sind kein Unmöglichkeitsbeweis. Echte Untermenüs verlangen einen separaten
Clientnachweis oder eine Änderung am Codexclient. Keine erfundenen `groups`,
`children` oder `auto_modes`-Felder als funktionierende Exportlösung versprechen.
Auch `codex-auto-review` im ersten Menü beweist weder lains Autorouting noch eine
Anbindung an „Approve for me“; der Name allein gibt einer Route keine solche Rolle.
Der aktuelle erste Menüeintrag ist eine Modellschnellwahl. Ihn durch weitere
Aliasse zu ergänzen wäre noch keine Gruppe mit gefilterter zweiter Modellliste.

## Kleiner fachlicher Zuschnitt

Sammlung: stabile eigene ID, frei gewählter Name, geordnete Referenzen auf Route-IDs,
optional eine Referenz auf ein späteres Routerziel. Das ist der minimale Entwurf,
kein beschlossenes DTO. Anbieteransicht wird aus Routen abgeleitet. Profil bleibt
die aktivierte/exportierte Modellauswahl; Sammlung bleibt eine überlappende Sicht.
Nicht im Profil enthaltene Mitglieder sind erkennbar und nicht ausführbar.

Einzelwahl sendet weiterhin genau den vorhandenen Alias. Ein Sammlungsautorouter
braucht später einen eigenen stabilen virtuellen Alias, der serverseitig nur auf
erlaubte Mitgliedsrouten aufgelöst wird. Der Menüname allein darf keine Auswahl
vortäuschen. Bis dahin: in lain `Automatisch · Noch nicht eingerichtet`, nicht als
funktionierendes Modell exportieren. Es ist kein Ersatz durch die erste Route.
Vor Ausführung mindestens festlegen: explizite Reihenfolge oder Classifier,
Fähigkeitsfilter, Kostenregel, Verhalten ohne geeignete Route und Kontinuität einer
Unterhaltung. Ein Classifier darf nur bekannte erlaubte IDs zurückgeben; sein
Aufwand ist eigener Verbrauch. Kein Modellwechsel mitten im Stream.

`Free` als Sammlungsname ist keine Gratisgarantie. Ein ausführbarer Free-Router
braucht separat eine durchgesetzte `free_only`-Policy und belegte zulässige Routen;
unbekannte Kosten oder erschöpfte Gratisroute dürfen keinen bezahlten Ersatz
auslösen. Ein lokales kostenloses Auswahlverfahren vermeidet zusätzlichen
Classifierverbrauch. Der bestätigte [Produktumfang](../product-scope.md)
verlangt allgemeine, anbieterneutrale Zweckzuordnungen; die
[Architektur](../architecture.md#erweiterungen-und-reife) erhält zentrale
Routenreferenzen und trennt die spätere Regelausführung. Approver und
Berechtigungen sind eine andere Aufgabe. Der neue Wunsch konkretisiert Sammlungen/Autorouter,
entscheidet aber weder Scheduler noch die bisher offene Approverintegration.

## Umsetzbare Pakete mit Abnahme

| Paket | Ergebnis und testbarer Abschluss |
| --- | --- |
| C0 · Gewünschten Codexablauf nachweisen | Priorität vor dem bisherigen UI-Zuschnitt: versionspezifische Picker-/Katalogverträge prüfen; falls nötig vollständig isolierter synthetischer Menüversuch ohne Anbieterzugang. Test: Auswahl von Free öffnet nur Free-Mitglieder, IT Sec eine andere Teilmenge; Autorouter steht jeweils oben, Zurück erhält Kontext, Alle Modelle bleibt erreichbar. Schnellwahl eines einzelnen Alias zählt nicht als Untermenü. Wenn die unveränderte CLI keinen solchen Anschluss anbietet, Einschränkung belegen und eine begrenzte Clienterweiterung als Vorschlag vorlegen; keine Umsetzbarkeit durch bloßes Umbenennen behaupten. |
| C1 · Sammlungen in lain | Kleine Sammlungsliste und Anbieteransicht über dieselben Route-IDs. Test: Route B in NVIDIA und IT Sec, nur einmal gespeichert/gezählt; Umbenennen/Entfernen einer Sammlung ändert weder Alias noch Profil/Accounts. Suche, Zurück und Auswahl bleiben erhalten. |
| C2 · Codex-Darstellung | Nach C0 den gewünschten zweistufigen Ablauf an den tatsächlich belegten Clientanschluss anbinden. Zusätzlich gute `display_name`-Werte, unveränderte `slug`-Werte und keine duplizierten Aliasse pro Sammlung. Test: richtige Slugs, gruppenspezifische zweite Liste, keine unbekannten Schemafelder, Profile/Visibility erhalten. Ein flacher Katalog ist eine unabhängige Verbesserung, erfüllt allein aber nicht den Auftrag. Behandlung interner versteckter Reviewmodelle ausdrücklich prüfen. |
| C3 · Sammlungsrouter separat | Vor Implementierung Auswahl-/Kosten-/Gesprächsregel festlegen. Danach eigener virtueller Alias, Mitglieds-/Profil-/Capability-Prüfung und nachvollziehbares gewähltes Ziel. Test: kein Mitglied passend → klarer Fehler; Free erschöpft/Preis unbekannt → kein Paidfallback; explizite Einzelwahl umgeht Autorouter. Bis Qualifikation nur nicht ausführbarer Entwurf. |
| C4 · Gemeinsame Abnahme | Synthetische Überschneidung Free/IT Sec/Anbieter, gelöschte/deaktivierte Route, ungespeicherter Stand, schmale CLI und Tastatur prüfen. Roh-IDs bleiben stabil; keine Aktivierung/Installation durch bloßes Browsen; bestehende Approver-/Workerregeln bleiben unverändert. |

Empfohlene Reihenfolge nach der Präzisierung: C0 zuerst, anschließend die davon
abhängige Codexdarstellung C2 mit den gemeinsamen Sammlungsdaten aus C1. C3 bleibt
ein eigenes Vorhaben; die vollständige Abnahme der gewünschten automatischen
Auswahl benötigt sowohl den echten Menüablauf als auch das tatsächliche Routing.
