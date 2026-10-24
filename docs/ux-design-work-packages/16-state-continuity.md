# UXD-16 – Übergabe der Zustandsrunde vom 25.10.

Stand 25.10.2026. Auf Nutzerauftrag „mach daraus arbeitspakete“ ist das bisherige
Sammeldelta in **sechs einzeln abarbeitbare Pakete UXD-17–22** aufgeteilt.
Diese Datei bleibt Einstieg und gemeinsame Grundlage; Aufgaben und Nachweise
stehen bei den einzelnen Paketen. Die bisherigen UXD-/AC-/ST-Owner bleiben zuständig.

Ziel: Navigation, Refresh und Konfigurationsänderungen bewahren Kontext und
Identität. Der Nutzer erkennt, welche Daten bekannt sind und welche Aktion
tatsächlich bestätigt wurde. Grundlage sind [18 geprüfte UX-Pilot-Entwürfe](../uxpilot-flows-2026-10-25.md),
[Designmanifest mit finalen IDs und PNGs](../uxpilot-flows-2026-10-25.json)
und [Auswahlgalerie](../uxpilot-flows-2026-10-25.html).

## Geltender Stand und Reife

Bestätigt bleiben [Accounts breit und schmal nebeneinander, Models/Usage Tabelle
plus Detail, Monitortabelle mit langen Balken, Applyreview im Profildetail und
die dauerhafte linke Requestliste in Live](../uxpilot-selection-2026-10-24.md).
Originalkatze, dunkler Look, Mint, native Controls, Profiles direkt im Gateway
und genau ein Informationsmodusdropdown rechts unten bleiben erhalten.
Betriebsabsicht Monitor/Gateway, Informationsumfang und Dichte sind getrennt.
Keine erfundenen Quotas, Preise, Anbieterfähigkeiten oder Backendgarantien.

**Zwei Präsentationsentscheidungen sind weiterhin offen:**

- Draftnavigation A: Nachfrage vor dem Wechsel; B: direkt wechseln mit bleibendem
  Hinweis, Rückweg und ausdrücklichem Verwerfen. Empfehlung B, weil der vorhandene
  Abschnittsentwurf bereits erhalten bleibt; der Hinweis beansprucht Platz.
- Simple-Live A: direkt; B: unter More. Empfehlung A bei regelmäßiger Nutzung,
  weil gleicher Footerplatz einen Schritt spart. B bleibt für seltene Nutzung sinnvoll.

UXD-17 trennt diese beiden abhängigen Gestaltungsteile vom unabhängigen
Bestandsabgleich. Keine Variante wird allein durch diesen Planauftrag ausgewählt;
keinen A/B-Umschalter als neue Produktfunktion bauen. Übrige Bilder konkretisieren
Zustandsfälle bestehender Verträge. Sie geben keine neue Startseite, Modusmatrix,
Fähigkeit oder zweite Runtime frei.

Die Designrunde las HEAD `e8dcac16e4a5dcfc2b309e0702faa3748c324d70`.
Für diesen Zuschnitt gezielt gegen **`5c6dfc7c1178a64b83ec4f852c1eb5b1662d9ec6`**
nachgelesen: Workspace/Drafts, Accounts/Quotadarstellung, Modellaktionen und
Profiles/Apply/Clientsetup. Keine allgemeine Codeabnahme. Seit dem Designabgleich
haben sich besonders Account-/Quotadateien und Desktopfixtures verändert;
vor Umsetzung aktuellen Stand erneut lesen. Fremde WIP-Dokumentation und
bestehende Implementierung erhalten, nichts pauschal neu aufbauen.

## Pakete und Reihenfolge

| Paket | Prüffähiges Ergebnis | Bestehender Owner | Voraussetzung |
| --- | --- | --- | --- |
| [UXD-17 – Navigation und Drafts](17-navigation-drafts.md) | Gewählte Navigation, bewahrter Abschnittsentwurf und lokaler Rückweg | UXD-01 | UXD-00/01; Darstellung der beiden Varianten erst nach jeweiliger Nutzerwahl |
| [UXD-18 – Accountdaten und Refresh](18-account-snapshots.md) | Erster Snapshot, leeres Inventar und lokaler Refresh klar getrennt | UXD-02, Shell UXD-01 | UXD-01/02/10 und tatsächlicher Quotaanschluss |
| [UXD-19 – Modellaktionen und Identität](19-model-command-identity.md) | Alle Commands treffen dieselbe stabile ID; Entfernen und Filterfälle verständlich | UXD-04 | UXD-01/04, vorhandene Policy-/Referenzverträge |
| [UXD-20 – Connect und Clientsetup](20-connect-client-setup.md) | Fehlende Voraussetzung vor Login; Prepare/Install/Apply nachvollziehbar getrennt | UXD-03/05, AC-/ST-Owner | UXD-03/05, bestehender AC-/ST-Anschluss |
| [UXD-21 – Applystatus](21-apply-status-consistency.md) | Stale/unknown in gewählter Reviewfläche; Liste, Detail und Shell widerspruchsfrei | UXD-05/12, ST-02 | Gewählte Reviewfläche UXD-05/12 und realer ST-02-/Clientanschluss |
| [UXD-22 – Native Abnahme](22-flow-acceptance.md) | Zustandsfolgen mit Eingabe-, Bild- und Requestnachweisen oder benanntem Blocker | UXD-09, jeweilige Seitenowner | Prüfkatalog früh; Ausführung nach jeweiligem Fachdelta und betroffener Nutzerwahl |

Zuerst je Paket klären: bereits erfüllt / fehlendes Delta / fehlender Anschluss.
UXD-18 zuerst als Accountdurchstich qualifizieren, danach Connect und Models/
Profiles anschließen. Die Grundlagen aus UXD-01 müssen verfügbar sein;
die beiden offenen UXD-17-Varianten blockieren keine unabhängigen Fachfälle.
UXD-22 kann früh Fixtures und Szenarien präzisieren; der Abschluss folgt nach
den betroffenen Fachpaketen. Fachpakete hängen nicht vom abgeschlossenen UXD-22 ab.

UXD-20 und UXD-21 berühren beide `MainWindow.Profiles.cs`: derselbe bestehende
Owner bearbeitet sie nacheinander, bevorzugt Setup vor Applystatus; das ist
Schreibkoordination, keine zusätzliche fachliche Abhängigkeit. Shelländerungen
aus UXD-18/21 integriert UXD-01. Zentrale Tests integriert UXD-09. Keine neuen
Schreibzuständigkeiten, Agenten oder konkurrierenden Vertragsimplementierungen.

## Gemeinsame Lieferung und Grenzen

Jedes Paket liefert den aktuellen Bezugscommit mit relevantem WIP, den
Bestandsabgleich, tatsächliche Änderungen, API-/ID-/Revisionszuordnung,
ausgeführte Prüfungen und offene Abweichungen. Ein bereits erfüllter Fall
braucht einen belastbaren Nachweis statt einer Neuimplementierung.
Native Controls verwenden; UX-Pilot-HTML und feste Exporthöhen sind keine
Produktimplementierung oder responsive Vorgabe.

Die Paketprüfungen sind **geplant, in diesem Planauftrag nicht ausgeführt**.
Die [bestandenen Galerie-/HTML-Prüfungen](../uxpilot-flows-2026-10-25.md#läufe-canvas-und-nachweise)
qualifizieren Designartefakte. Sie ersetzen keine native Produktabnahme.
Dieser Auftrag ändert ausschließlich Planung/Dokumentation; keine Produktcodeänderungen,
Builds, Installation, Neustarts, echten Provideraktionen oder Commits.
Keine weiteren UX-Pilot-Credits verbraucht.

Prüfung der Planübergabe: sechs neue Pakete mit eindeutigen IDs; alle 18 finalen
Designnamen zugeordnet, 75 lokale Dokumentlinks einschließlich Markdownanker
auflösbar, keine Kreise in den neuen Paketabhängigkeiten, Whitespace ohne Befund.
Die spätere HEAD-Fortschreibung auf `2c4bf7b7ab6d809aa679303edca026073e021f7d`
ändert gegenüber dem gelesenen Stand keine der berührten Desktop-/Client-/
Desktoptestdateien. Daraus folgt keine zusätzliche Produktqualifikation.
