# Nächste Arbeitspakete aus dem Review

Planungsstand **25.10.2026**, Code `cbf083f`. Anforderungen und Entwurfsgrenzen
stehen im [Review](README.md), Anbieterquellen in der
[Monitoringmatrix](../provider-monitoring-review-2026-10-25/README.md).
Dies ist die vorgeschlagene Übergabe, kein bereits ausgeführter Auftrag.
Vorhandene AC-Pakete und ihr heutiger Code werden fortgesetzt, nicht neu gebaut.

## 1. Gemeinsame kurze Namen

**Ergebnis:** Derselbe Account erscheint als `Gemini`, `NVIDIA`, `Cloudflare`
usw. in CLI, Übersicht, Auswahl, Usage, Tray und Benachrichtigungen.
Mehrere Accounts erhalten verständliche Zusätze. Eigene Namen bleiben erreichbar.

Gemeinsamer Owner für `Lain.Client` und die kurze Namensliste; Desktop-/CLI-Owner
ersetzen ihre lokalen Tabellen. Einstieg: `ProviderDisplayNames`,
`AccountNameSuggestions`, `MainWindow.Budgets`, CLI-Accountansichten und Tray.
Bestand nicht per Regex umbenennen; gespeicherte `Name`-/ID-/JSON-Werte bewahren.
Nur bekannte Standardtexte einklappen. Eine optionale spätere Namensherkunft
ist nicht Voraussetzung für Anbieterüberschriften.

**Abnahme:** Einzelaccount, zwei Accounts, gleiche eigene Namen, vorhandene
lange Standards, Emailname, Filter und Reimport. Filter verändern keine
Kollisionsbezeichnung. Die Auswahl und Mutation trifft immer dieselbe ID.
Details zeigen die vollständige gespeicherte Bezeichnung.

## 2. Warum und nächster Schritt in CLI und Desktop

**Ergebnis:** Aus `Quota setup required` wird eine belegte Aussage wie
„Google-Projektmonitor noch nicht verbunden“ mit passender erreichbarer Aktion.
Fehlende Implementierung wird nicht als Accountfehler ausgegeben.

Gemeinsamer Clientowner für Darstellung; Host-/Adapterowner liefern die bekannte
Ursache. Vorhandene `Support.Message`, `ActionUrl`, Methodenverfügbarkeit und
Monitorstatus zuerst nutzen. Stabile Gründe/kurze Texte/Aktionsreferenzen nur
additiv ergänzen, soweit diese Informationen heute fehlen. Den tatsächlich
verfügbaren Anschluss aus bestehendem Methodenkatalog wählen, keine eigene
Provider-OAuthlogik im Frontend einführen.

**Abnahme:** fehlender Zugang, abgelaufener Zugang, 401, 403 ohne bekannte
Tarifursache, unklarer GraphQL-Fehler, erfolgreiche leere Antwort, unbekanntes
Schema, passiver Wert, alte erfolgreiche Messung und nicht freigegebene Methode.
Timeout fordert keinen neuen Login. Fehlender Maintainerclient fordert keinen
Kauf. Konkrete alte Werte bleiben alt. `usage` bleibt bei gecachten GETs.

Ein kontobezogener **lesender** Erklärungsweg ist Teil dieses Entwurfs. Seine
Syntax vor Umsetzung festlegen; bestehende historische `usage`-Filter erhalten.
Der Monitorwizard bleibt eine separate bewusste Einrichtungsaktion.

## 3. Adaptive CLI-Ausgabe

**Ergebnis:** Das gewählte Tabellen-/Zweispaltenlayout löst die Textwand ab.
Kurze Fensternamen, native Restmengen und Resets sind direkt vergleichbar;
Erklärungen verdrängen die wichtigsten Quotas nicht.

Die Spalte „verfügbar“ setzt einen tatsächlich bekannten Restwert voraus.
Reine Usage, beobachtete Raten und konfigurierte Obergrenzen behalten ihre
eigene Bezeichnung; aus ihnen wird kein Restbetrag oder Reset erfunden.

CLI-Owner bearbeitet Renderer und gezielte Fixtures; gemeinsame Quotaauswahl
bleibt beim Clientowner. [Vorschau](layout-preview.html) und
[Layoutreview](adaptive-layout.md) sind Entscheidungsgrundlage. Die konkrete
Variante und angenommene Mindestbreite sind noch Vorschlag. Keine allgemeine
TUI-/Frameworkmigration notwendig. Keine neue Pflichtauswahl vor `lain usage`.

**Abnahme:** repräsentative 15 Anbieter plus Langnamen-/Mehraccountfall bei
60/80/100/120/160/200 Spalten, kleiner Fensterhöhe, heller/dunkler Palette,
konkreter Nutzerpalette, `NO_COLOR` und Pipe. Spalten sind nach Terminalzellen
ausgerichtet; keine horizontale Überlänge oder Bedeutungsabhängigkeit von Grün.
Zusätzlich echte Sichtprüfung und Lesefolge, nicht allein maximale Zeilenlänge.
`--full` und vollständiges JSON behalten ihre Verträge. Native UI nur auf Xvfb.
Ein Usage-only- und ein Limit-only-Fall müssen ohne erfundenen Restwert passen.

## 4. Bestehende Messungen richtig vervollständigen

**Ergebnis:** Vorhandene Reader liefern präzisere, richtig bezeichnete Daten.
API-Fähigkeit, laufender Code und echter Accountzustand werden einzeln abgenommen.

- OpenRouter: accountweiten Free-UTC-Tag ausdrücklich identifizieren; belegten
  Tagesreset samt abgelaufener Tageswerte behandeln. Key-USD-Cap bleibt getrennt.
- Kilo: Wallet-/Pass-Teilursachen sichtbar machen; fehlendes Feld/CLI-Nullfallback
  bleibt unbekannt. Kein OAuthzwang oder Freeausfall allein wegen Wallet 0.
- Synthetic: Einheiten und Schema gegen die aktuelle offizielle Quelle prüfen,
  insbesondere die gemeldete nächste Regeneration. Unbekannte Antwort nicht
  als bewiesenes fehlendes Abo verkaufen.
- Kimi: neue 5h-/Monatspläne und alte Wochenpläne anhand gemeldeter Fenster
  unterstützen; kein statisch erzwungenes 7d-Fenster.
- Groq: vorhandene Headerquelle durch bis zur Anzeige verfolgen; Enterprise-
  Reporting und normale Free-Keybeobachtung getrennt halten.

Je Reader ein eigener Owner; gemeinsame Quota-/Speicherverträge beim Integrator.
**Abnahme:** fehlend/null/echte Null, Tageswechsel und DST, gemischte/partielle
Antworten, Schemaabweichung, widersprüchliche Einheiten, Account-/Poolbezug und
zehnfach gleiche Beobachtung. Keine Inferenz zum Erzeugen eines Monitorwertes.
Die genaue Ursache eines aktuellen Accountfehlers bleibt ohne autorisierte
Accountprobe offen; öffentliche Quellen ersetzen diese Probe nicht.

## 5. Bereits vorbereitete Anschlusswege fertig qualifizieren

**Ergebnis:** „Verbinden“ führt je Anbieter zum besten heute verfügbaren
belegten Weg statt zu einer pauschalen Keyeingabe.

- Gemini: vorhandenen isolierten gcloud-Owner, Projektbindung und Modellreader
  vollständig durch die Oberfläche führen. Rechteumfang vor Login erklären;
  der CLI-Standardgrant ist breiter als reines Monitoring.
- Cloudflare: zuerst Account-/Dataset-/Rechteursache unterscheiden und Quelle
  qualifizieren. Wrangler-Tokenexport ist ein Kandidat; Scope und Speicher-
  isolation müssen tatsächlich passen. Neue OAuthanmeldung heilt kein fehlendes Dataset.
- OpenRouter/Poe: vorhandene Browser-/Keybootstrapadapter verwenden; bei Poe
  nötige Maintainerqualifikation abschließen. Nutzer müssen keine App registrieren.
- MiMo: bestehenden eigenen Consoleweg fertig qualifizieren, konkreten lokalen
  Browserstartblocker diagnostizieren; Individual-/Team-/PAYG-Umfang nicht vermischen.
- Requesty, Mistral, Alibaba, Command Code und Fireworks: vorhandene AC-05-
  Einzelqualifikationen für Toolartefakt, Isolation, Account-/Projektbindung und
  Übergabevertrag schließen. Ein Klassenname/Platzhalter ist kein verfügbarer Login.

**Abnahme:** Provideridentität, Abbruch, Timeout, Grantablauf, Refreshowner,
konkurrierender Reconnect, minimale Rechte und unveränderte fremde CLI-Profile.
Speicherung, erfolgreiche Authentisierung und erfolgreiches Reporting sind
getrennte Ergebnisse. Nur freigegebene Methoden als ausführbare Buttons anbieten.

## 6. Weitere Reportingquellen gezielt anschließen

**Ergebnis:** Pro Anbieter ein klar begrenzter zusätzlicher Monitor mit
eindeutiger Datenart und Ressource; keine pauschale Umsetzung aller Kandidaten.

SambaNova/Cohere/Upstage: belegte Firstparty-Consolequellen anhand AC-08 mit
eigener Accountbindung prüfen. Fireworks: Quota-API als Konfiguration oder
Usage entsprechend ihrem tatsächlichen Vertrag bezeichnen. Mistral Enterprise:
separaten Adminread und widersprüchliche Authdoku klären. OpenCode: konkrete
Go/Plus-Tools über das dokumentierte OAuth/DCR-MCP erst nachweisen.
Nebius: passive Serverlessraten bzw. separate Dedicated-Metriken. Inception:
veröffentlichte Raten als Referenz, keinen Trialrest erfinden. Step Plan bleibt
getrennt von vorhandenem persönlichem PAYG-Read.

DeepSeek-Balance und MiniMax-CLI sind ergänzende Kandidaten außerhalb des
25-Anbieterbestands; bei eigener Freigabe als kleine separate Erweiterung.
Keine neuen Abos, Käufe, API-Ressourcen oder Keys als versteckte Voraussetzung.
Eine ausdrücklich autorisierte Keyerzeugung ist nur mit belegtem Herstellerflow
und ausreichenden Rechten ein eigener Onboardingschritt.

## 7. Sammlungen und übersichtliche Modellwahl

**Ergebnis:** **Codex CLI `/model`** zeigt Anbieter und frei benannte Sammlungen
wie `Free` oder `IT Sec` in der ersten Auswahlstufe. Die folgende Liste enthält
oben die automatische Auswahl und darunter passende Einzelmodelle. Alle Sichten
referenzieren dieselben zentralen Routen. Ein nicht eingerichteter Router ist
klar erkennbar und nicht ausführbar. Eine bloße lain-interne Gruppierung oder
flache Exportkosmetik erfüllt den präzisierten Nutzerwunsch nicht.

Der [Zuschnitt C0–C4](model-collections.md) enthält Verträge, Clientgrenzen und
Abnahme. Zuerst den konkreten Codex-Menüanschluss nachweisen: Gruppenauswahl
muss eine entsprechend gefilterte zweite Liste öffnen. Eine Alias-Schnellwahl
genügt nicht. Falls die unveränderte CLI keinen solchen Anschluss besitzt,
eine begrenzte Clienterweiterung als Vorschlag ausarbeiten. Derselbe Routeneintrag
bleibt auch bei mehrfacher Zuordnung dieselbe ID; Aliasse, Profile und Usage
werden nicht dupliziert. Keine erfundenen Katalogfelder für native Untermenüs
ausliefern. Interne versteckte Reviewmodelle im Export ausdrücklich prüfen.

Der tatsächliche Sammlungsrouter bleibt ein eigenes Vorhaben: Reihenfolge oder
Classifier, Fähigkeitseignung, Kostenregel und Gesprächskontinuität vor seiner
Implementierung festlegen. Eine funktionierende `Free`-Auswahl erfordert
serverseitig qualifizierten Kostenschutz; kein bezahlter Fallback bei Erschöpfung
und keine Gratisgarantie durch den Namen einer Sammlung.

**Abnahme:** dieselbe Route unter Anbieter/Free/IT Sec, deaktiviertes oder nicht
im aktiven Profil enthaltenes Mitglied, stabile Einzelwahl, unveränderte Slugs
und kein zusätzlicher Account. Ein nicht qualifizierter Router wird nicht als
funktionierendes Modell exportiert. Der echte Codex-Menüablauf ist Teil der
Abnahme dieses Wunsches; Sammlungspflege in lain allein reicht dafür nicht.

## 8. Integrierte Abnahme und Lieferung

Nach gewünschter Umsetzung prüft ein unabhängiger Owner UI und CLI mit demselben
Datensatz: gleiche Namen, Messsemantik, Gründe und nächste Aktionen. Quellen-/
Tarifbehauptungen müssen auf die konkrete Methode passen. Keine „Pro-only“- oder
„OAuth-only“-Texte ohne Beleg. Gute Darstellung fehlender Daten zählt als
ehrlicher Abschluss, nicht als vorgetäuschte Monitoringunterstützung.

Vorbereitung, lokal geprüft, installiert und am echten Account geprüft getrennt
festhalten. Builds und native Tests seriell mit bisherigen Ressourcenlimits;
unnötige Buildkopien löschen. Accounts, stabile Identitäten, Routen, History,
normales Codexprofil und alter Proxy bleiben erhalten. Liveproben nur im
ausdrücklich autorisierten Umfang, keine Käufe oder bezahlten Ausweichrouten.
