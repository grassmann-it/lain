# UXD-08 – Ruhige lokale Settings

Ziel: Darstellung, Fensterverhalten und Hilfe schnell erfassen, vorhandene
Speicherung erhalten. Abhängig von UXD-01; kleiner Layoutumfang, F11.
[Übersicht](README.md).

## Referenz, Befund und Dateien

Frame 14 `O63jRSAvsTNvo54maqIS`,
[Bild](../uxpilot-design-results-2026-10-24.md).
`MainWindow.Settings.cs` hat seitenbezogene Dichte mit vorbelegter Ausgangsseite,
persistierte Minimize-/Close-to-tray-/Notificationflags, Trayverfügbarkeit,
Speicherfehlerrollback und About mit echter Version/Lizenzen. Diesen Bestand
erhalten; dieses Paket besitzt nur diese Datei.

## Aufgaben

1. Drei Gruppen klar anordnen: Density for <Ausgangsseite>, Fenster und
   Benachrichtigungen, Hilfe/About. Normal/Compact mit dem lokalen View-Menü
   derselben Seite synchronisieren. Keine zweite Auswahl der Informationsstufe.
2. Native lesbare Checkboxen für die drei vorhandenen Flags verwenden. Abhängigkeit
   von verfügbarer Trayumgebung am betreffenden Control erklären.
   Bei Close/Minimize klar machen, dass der Host unabhängig weiterlaufen kann.
3. Echte Assemblyversion, Repository- und Lizenzinformationen sowie Originalkatze
   erhalten. Keine erfundene Version und keine dauerhafte Erfolgsmeldung.
4. Lokale Speicherung und vorhandenes Rollback bei Schreibfehler beibehalten.
   Fehlermeldung zeigt den tatsächlichen Zustand; Controls dürfen keine nicht
   gespeicherte Präferenz als erfolgreich persistiert ausgeben.
5. Back kehrt zur ursprünglichen Seite mit deren Auswahl/Draft/Scroll zurück.
   Änderungen sind nur lokale Präferenzen, kein Apply und kein Hostneustart.

## Abnahme

- Bei 1120×780 sind alle drei ruhigen Gruppen im ersten Viewport sichtbar;
  bei 800×560 bleiben alle Controls mit sinnvollem Scroll erreichbar.
- Settings aus Accounts/Models/Usage öffnen: „Density for“ bezeichnet jeweils
  die Ausgangsseite. Änderung wirkt dort und im lokalen View-Menü konsistent.
- Die drei Flags werden nach erneutem Öffnen und in einem neuen synthetischen
  Fenster korrekt geladen; simuliertes Speicherproblem führt zum vorhandenen
  Rollback mit Fehleranzeige.
- Tray verfügbar/nicht verfügbar sind eigene Fälle. UI-Schließen wird nicht
  mit Stoppen des Hosts verwechselt; keine zusätzliche Runtimeaktion durch Settings.
- Genau ein Modusdropdown bleibt im Shellfooter; Settings enthält keinen zweiten.
  Native Checkbox, Label, Tastaturfokus und Aktivierungsfläche sind lesbar/bedienbar.

## Grenzen und Übergabe

Keine neue Settingsarchitektur, Notificationengine, Trayimplementierung,
Fonts oder Previewquotadaten. Speichergrundlagenänderungen beim Shellowner,
native Trayänderungen beim bestehenden Trayowner anmelden.

Liefern: breite/schmale Gruppenansicht, Tray-/Speicherfehlerfall und Nachweis
unveränderter lokaler Persistenz sowie Backnavigation.
