# UXD-17 – Navigation und lokale Abschnittsentwürfe

Ergebnis: Zwischen Models und Profiles wechseln, ohne einen Entwurf unbemerkt
zu verlieren oder zu speichern. Live ist gemäß der noch offenen Simple-Auswahl
erreichbar. Ergänzt UXD-01; [gemeinsamer Stand, Grenzen und Entscheidungen](16-state-continuity.md).

Owner: UXD-01. Dateien: `src/Lain.Desktop/MainWindow.cs`,
`MainWindow.Workspace.cs` und erforderliche gemeinsame `Presentation/*`-Controls.
Seitenowner liefern Objekt-/Draftidentität; zentrale Tests über UXD-09.
Voraussetzung: aktueller UXD-00/01-Seiten-/Navigationsvertrag.

## Bestandsabgleich und Aufgaben

1. Vorhandene `SaveCurrentPage`-/`PageDraft`-Wiederherstellung weiterverwenden.
   Sie hält den montierten Editor je Abschnitt mit Aktionen, Revision und Fokus.
   Erhalt beim Abschnittswechsel und Revisionsprüfung getrennt nachweisen;
   keinen neuen Mehrfachentwurfsmanager oder Neustartspeicher bauen.
2. **Offen: Draft-A/B.** Nach der Nutzerwahl genau eine Präsentation anschließen:
   A fragt vor dem Wechsel, B wechselt direkt und zeigt einen bleibenden lokalen
   Hinweis. Beide nennen Models und das gemeinte Objekt, bewahren Eingaben,
   bieten Rückweg/Weiterbearbeiten und verwerfen nur ausdrücklich. Kein Save/Apply
   durch Navigation. Bei B Hinweis außerhalb wegscrollenden Inhalts halten;
   bei A Fokus im Dialog führen und zum Auslöser zurückgeben.
3. **Offen: Simple-Live-A/B.** A hat Live direkt, B unter More. Nur den gewählten
   Eintrag bauen, keine Duplikate und keinen neuen Variantenumschalter. Profiles
   bleibt direkt. Barebones-/Advanced-Bilder prüfen die vorgeschlagene Matrix;
   sie bestätigen keine weiteren Navigationsänderungen.
4. Informationsumfang und Dichte wechseln, während ein Entwurf oder ein nach
   More verschobenes Ziel offen ist: Inhalt, Auswahl und Rückweg erhalten.
   Monitor/Gateway bleibt eine eigene bewusste Entscheidung. Genau ein
   Informationsmodusdropdown rechts unten, kein automatischer Fensterwechsel.

## Geplante Abnahme und Lieferung

- Models `gpt-lain-01`, Enabled aus als lokaler Entwurf → Profiles Daily öffnen,
  Applied bleibt Workspace 2 → zurück: dieselben Eingaben, Revision und
  sinnvoller Fokus; kein Save-/Apply-Request. Verwerfen trifft nur diesen Draft.
- Konfiguration während des Drafts ändern: Eingaben bleiben, veralteter Save
  wird nicht unbemerkt auf die neue Revision übertragen. Nicht wiederherstellbare
  Felder konkret nennen; keine Speicherzusage über Appneustart.
- Gewählte Simplevariante bei 1120×780 und 800×560 per Maus und Tastatur prüfen;
  Escape/Fokusrückgabe, Settings/Back, Stufen-/Dichtewechsel, sichtbarer Footer.
- Lieferung: dokumentierte Nutzerwahl je Variante, Bestands-/Deltaabgleich,
  native Eingabefolge und Requestnachweis. Solange eine Wahl fehlt, deren
  Präsentation als offen melden; unabhängigen Erhalt bereits prüfen.

Designs: `DRAFT-LEAVE-A`, `DRAFT-LEAVE-B`, `SIMPLE-NAV-A`, `SIMPLE-NAV-B`,
`GATEWAY-BAREBONES`, `GATEWAY-ADVANCED-COMPACT` im
[Manifest](../uxpilot-flows-2026-10-25.json). Empfehlungen sind B für Drafts und
A für regelmäßige Live-Nutzung; sie sind keine Nutzerentscheidung.
