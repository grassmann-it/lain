# UXD-10 – Gewählte Panels und Monitor mit Quota-Balken

Ergebnis: Die ausdrückliche [Designauswahl](../uxpilot-selection-2026-10-24.md)
ist durchgängig umgesetzt; Accounts behalten auch bei 800×560 ihr benachbartes
Detail und der kompakte Monitor zeigt alle Quotafenster in einer Tabelle mit
integrierten Verbrauchsbalken. Dies ist ein Delta zu UXD-01/02/04/06, kein zweiter
Neuaufbau. Bereits gelieferte Umsetzung zuerst prüfen und erhalten.

## Zuständigkeit, Voraussetzungen und Grenzen

UXD-01 besitzt Shell, Page-Mounting und gemeinsame Scroll-/Fokusmechanik;
UXD-02 Accounts, Tabellen-/Quotadarstellung; UXD-04 Models, UXD-06 Usage.
Exklusive Dateien bleiben gemäß [Ownerübersicht](README.md#dateieigentum).
Kein zusätzlicher konkurrierender Owner für `MainWindow.Workspace.cs`.
Voraussetzungen: aktueller Anschluss-/Fähigkeitsabgleich UXD-00 und bestehende
native Auswahl-/Detailgrundlage UXD-01. Keine neuen Providerreader oder Betriebsmodi.

## Aufgaben

1. Breite Accounts: eine Liste und dauerhaft reserviertes 400-px-Detail bei
   1120×780. Bei 800×560 weiterhin nebeneinander; 340 px Listenbreite ist die
   Referenz, keine neue Mindestfenstergröße. Keine automatische Backseite,
   kein Overlay und kein Neupacken beim Öffnen, Wechseln oder Schließen.
2. Models und Usage übernehmen Tabelle mit Seitendetail. Nicht die früheren
   Empfehlungen für Usage B oder schmale Accounts B implementieren.
3. Bewusster kompakter Monitor: Tabelle nach Account, jedes gemeldete Fenster
   mit Zahl/Reset/Scope und gültigem Verbrauchsbalken. Bestätigte Folgeauswahl A:
   lange, gleichmäßig breite Balken unter den Zahlen (`MON-BARS-A`). Die vorher
   gewählte Monitortabelle B bleibt die Grundlage; die Buchstaben betreffen
   unterschiedliche Fragen. Kurze Inlinebalken B bleiben eine nicht gewählte
   Alternative. Mehr Zeilenhöhe ist akzeptiert, Daten-/Quotasemantik bleibt gleich.
4. Guthaben ohne Nenner und unbekannte Werte ohne Prozentbalken. Gemeldeter
   Prozentanteil bleibt auch ohne absolute Menge gültig. Fälliger Reset bleibt
   unbestätigt; Refreshfehler darf nicht pauschal einen OAuthfehler behaupten.
   Gemeinsame Budgets nicht addieren und Free 0 nicht accountweit sperren.
5. Vollständige Editoren in begrenzten Scrollflächen mit erreichbarem Header
   und Save/Cancel. Models erhält auch Modellkatalog, dokumentierte Providerrouten,
   Bindung/Pool, Enabled/Visible, Policycheck und bestehende Fallbackfelder.
   Keine vollständige Feldabdeckung allein aus einem Entwurfsbild ableiten.

## Abnahme und Nachweise

- Gleiche Fixtures vor/nach Detailöffnung bei 1120×780 und 800×560: unveränderte
  Inventarbreite, Karten-/Zeilenpositionen und linker Scrollanker.
- Command Code mit sechs Fenstern, Z.ai mit gemeldetem relativen Wochenwert,
  Kimi mit unbestätigtem Reset, Guthaben ohne Nenner und unbekannte Quota.
  Obere und untere Scrollposition zeigen alle Daten ohne abgeschnittene Aktionen.
- Monitortabelle bei 800×560: normierbare Werte mit langen Balken unter den
  Zahlen und gleichbleibender Vergleichsskala; Zahlen/Restwert/Reset bleiben
  lesbar. Kein automatischer Wechsel auf kurze Inlinebalken.
- Langer Accountname und 35-Accountbestand: Titel darf umbrechen; Close/⋯,
  Refresh, Zahlen und Reset bleiben nutzbar. Moduswechsel ändert keine Fenstergröße.
- Models/Usage behalten Auswahl, Filter, Draft und Kontext über Polling und Back.
- Native Pointer-/Tastatur- und Scrollnachweise an UXD-09 liefern. UX-Pilot-HTML
  und Bilder sind Referenzen, keine übernehmbare Avalonia-Implementierung.

Referenzen: `MON-BARS-A/B`, deren `-LOWER`-Ansichten,
`ACC-NARROW-EXPERT`, `ACC-NARROW-LONGNAME`, `MODEL-NARROW-EDITOR`,
`USAGE-NARROW` im [Manifest](../uxpilot-followup-2026-10-24.json).

Übergabe: native Größen-/Geometrienachweise, Feldzuordnung und vorhandene
Commandsemantik. Bestätigte Layouts sind geklärt; weitere Reviewbilder bleiben
Vorschläge. Umsetzung oder Produktabnahme wurde in dieser Designrunde nicht ausgeführt.
