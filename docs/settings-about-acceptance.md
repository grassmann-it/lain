# Settings, About und Fotovariante

Der Clientstand `416fe139` ist am 24.10.2026 installiert. Der bisherige obere
View-Bereich ist durch einen Settings-Einstieg ersetzt. Barebones/Simple/Advanced
ist ausschließlich unten rechts auswählbar.

Settings enthält Dichte pro Hauptbereich und getrennte Einstellungen für
Minimieren ins Tray, Schließen ins Tray und Meldungen. About zeigt die tatsächlich
geladene Version, den Projektlink und belegte Lizenzinformationen. Das angepasste
Katzenfoto besitzt kräftigeren Kontrast; Originaldatei und historische Belege
bleiben erhalten. Die fünf Hauptbereiche und ihre bestehenden Funktionen bleiben
in dieser Lieferung erhalten.

Geprüft wurden 611 synthetische Desktop-Checks mit 104 Bildern, 98 Tray-Checks
gegen den exakt veröffentlichten Desktop und 166 Checks gegen die installierte
Oberfläche. Alle GUI-Prüfungen liefen auf privaten Xvfb-Sitzungen. Die tatsächliche
Launcher-/Fenstericon-Integration wurde separat geprüft. Tests betreffen unter
anderem Settings/Zurück, erhaltene Entwürfe und Fokus, unabhängige Dichten,
Speicherfehler, Tray-Verhalten und About-Version bei 800 und 1120 Pixeln Breite.

Der private Installertest und der echte Clientaustausch behalten Host/Core,
Accounts, Modelle, Profile, Präferenzen, den historischen Datenstand und die
Revisionen bei. CLI-Kurzansicht und `--full` bleiben vorhanden. Der Host/Proxy
wurde für diese Clientlieferung nicht neu gestartet. Das neue Icon und die
zugehörige Eigentumszuordnung wurden gemeinsam aktualisiert.

Nachweise: [installierte Clients](../tests/acceptance/evidence/settings-installed.json),
[installierte UI](../tests/acceptance/evidence/settings-installed-host.json),
[Tray](../tests/acceptance/evidence/settings-frozen-tray.json),
[nativer Launcher](../tests/acceptance/evidence/settings-native-launcher.json),
[Installer](../tests/acceptance/evidence/settings-installer.json),
[finales Paket](../tests/acceptance/evidence/settings-release-final.json).

[Settings](screenshots/settings.png) · [About](screenshots/about.png).

Die anschließend diskutierte stärkere Trennung der Informationsmodi und neue
Kontextmenüs stehen im [UX-Entwurf](ui-modes-and-actions.md). Diese Lieferung
implementiert weder den Monitorbetrieb ohne Core noch neue Routensperren.
Das separat autorisierte Upgrade auf CLIProxyAPI v8.0.41 wird isoliert aufgebaut;
diese Clientabnahme qualifiziert weiterhin den vorhandenen Core, kein ungeprüftes
Upstream-Upgrade.
