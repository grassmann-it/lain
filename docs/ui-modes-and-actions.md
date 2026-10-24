# UI-Modi und Objektaktionen – Sparring vom 24.10.2026

Status: **Entwurf, keine bereits freigegebene Gesamtumsetzung.** Drei Worker haben
lain auf `416fe13` und das parallel bearbeitete MeowChan-Frontend lesend geprüft.
MeowChan wurde nicht gestartet; dessen aktuelle lokale Änderungen wurden nicht
verändert. Die installierten Settings/About-Änderungen sind davon getrennt.

## Bestätigter Bedarf und Befund

Der Nutzer möchte deutlich unterscheidbare Barebones/Simple/Advanced-Ansichten,
eine kleine App für Kontingente und Usage, weniger irrelevante Proxytechnik und
mehr Rechtsklickaktionen. Profiles wird im Gateway häufig benötigt und soll
direkt erreichbar bleiben. Für Routen braucht es einfaches reversibles
Deaktivieren, unter anderem für Kimi-Zugänge über Drittanbieter.

Heute ändern die Presets überwiegend Texte und geöffnete Details. Alle fünf
Hauptziele bleiben gleich; Fensterstart 1120×780 und Minimum 800×560 verhindern
eine wirklich kleine Ansicht. Account-/Routen-/Profil-Kontextmenüs fehlen.
MeowChan verändert dagegen auch Navigation, sichtbare Aktionsgruppen, Spalten
und Informationsdichte. Seine erzwungenen Fenstergrößen sollten nicht ungefragt
übernommen werden. Die frühere Festlegung gleicher fünf Hauptziele in allen
Presets wird durch den neuen Wunsch ausdrücklich erneut zur Diskussion gestellt.

## Vorgeschlagene Darstellung

| Modus | Hauptaufgabe | Darstellung |
| --- | --- | --- |
| Barebones | Kurz Kontingente prüfen | Kleine, ruhige Account-/Budgetansicht; vorhandene Usage erreichbar; Profiles im eingerichteten Gateway als direkter Kurzzugang; seltene Gatewaywerkzeuge im Menü |
| Simple | Alltag verwalten | Accounts, Modelle, Profile und Usage gut erreichbar; verständliche Zusammenfassungen; Technik lokal aufklappbar |
| Advanced | Betrieb und Fehler verstehen | Vollständige Gatewaynavigation mit Live; Attribution, Accountbindungen, Policies und Messgrundlagen unmittelbar sichtbar |

Alle gemeldeten Quota-Fenster, Rest/Reset, unbekannte Werte und relevante
Warnungen bleiben erhalten. Persönliche Detailentscheidungen und Filter sollen
Moduswechsel überleben. Dichte bleibt veränderbar; Fenstergeometrie springt beim
Umschalten nicht ungefragt. Eine kleinere Mindestgröße braucht eine eigene
Layoutabnahme, nicht nur eine geänderte Zahl.

## Fähigkeiten und Betriebszustand sind eine zweite Achse

Ein bewusst gewählter Monitor zeigt Kontingente und deren Aktualität. Ohne
Historienquelle verspricht er keine Request-/Tokenhistorie. Models, Profiles,
Live und Proxyaktionen gehören dort nicht in die normale Navigation.

Ein noch nicht eingerichtetes Gateway erhält einen zurückhaltenden
Einrichtungseinstieg. Ein eingerichtetes, vorübergehend gestörtes Gateway behält
seine Navigation und gespeicherten Profile; es wird nicht zum Monitor erklärt.
Bei unerreichbarem Host bleibt die zuletzt bekannte Struktur mit sichtbarem
Verbindungszustand bestehen. `ProxyReady=false` ist keine Betriebswahl.

Dafür fehlt heute ein ausdrücklicher Hostvertrag für Betriebsabsicht und
Fähigkeiten. Der bestätigte Monitor-Ausbau ohne Core bleibt ein eigenes Paket;
coreabhängiger Codex-Login darf darin nicht als funktionierende Anmeldung
erscheinen. Advanced schaltet keine technisch fehlende Fähigkeit frei.

## Objektaktionen und Routensperren

Rechtsklick, sichtbares `⋯` und Tastatur öffnen dieselben objektbezogenen
Commands. Das Ziel wird über stabile IDs erfasst und vor Ausführung gegen den
aktuellen Stand geprüft. Seltene Aktionen wandern aus langen Buttonreihen ins
Menü; grundlegende Aktionen bleiben erkennbar.

Für Routen sollten Aktivzustand und Aktivieren/Deaktivieren direkt sichtbar
werden. Das Menü bündelt Bearbeiten, Modellmenü-Sichtbarkeit, Profilverwendungen,
Usage, Account, Kopieren und nachrangig Entfernen. Vor dem Entfernen werden
benannte Profil-/Fallbackverweise gezeigt. Mehrfachauswahl kann explizit gewählte
Routen gemeinsam bearbeiten; eine automatische Einstufung als Firstparty darf
nicht aus Aliastext geraten werden.

Der aktuelle Vertrag unterscheidet:

- `Enabled=false`: Route bleibt gespeichert und in Profilen referenziert;
  der nächste Apply nimmt sie nicht in die Runtime auf.
- `Visible=false`: Alias verschwindet aus Modellmenüs; ein direkter Aufruf bleibt
  möglich. Das ist keine Sperre.
- Entfernen: löscht die gespeicherte Route, sofern keine Verweise bestehen.

Deaktivierte Profildefaults und Fallbackverweise müssen vorab sichtbar sein.
Gespeichert und aktiv bleiben ausdrücklich getrennt. Apply übernimmt den
gesamten aktuellen Profilstand, einschließlich anderer ausstehender Änderungen,
und startet den gemeinsamen Proxy neu. Eine isolierte sofortige Routensperre
gibt es heute nicht. Ebenso entsteht durch Deaktivieren keine automatische
Umleitung zu einem Firstparty-Alias.

## Noch zu entscheiden

1. Den konkreten Zuschnitt der kleinen Barebones-Navigation und die Bezeichnung
   der gemeinsamen Kontingent-/Accountseite bestätigen.
2. Für schnelle Routensperren den Umfang wählen: gespeicherte globale Freigabe
   mit separatem Apply, profilbezogene Auswahl oder sofortige temporäre Sperre.
   Letztere braucht einen neuen Host-/Runtimevertrag.

Ein erster schmaler Ausbau kann die gemeinsamen Objektcommands und sichtbaren
reversiblen Aktionen mit bestehenden APIs liefern. Atomare Sammeländerungen,
Sofortsperren und ein echter Monitorbetrieb erfordern getrennte Backendarbeit.
