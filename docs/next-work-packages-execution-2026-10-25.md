# Umsetzung N01–N09

Am 25.10.2026 hat der Nutzer die Umsetzung des
[konsolidierten Plans](next-work-packages-2026-10-25.md) als Goal mit parallelen
Workern beauftragt. Die frühere Beschränkung auf Planung ist damit für diesen
Auftrag aufgehoben. Kein Auftrag zur öffentlichen Veröffentlichung, zu Käufen,
bezahlten Fallbacks oder zum Wechsel der normalen Codexkonfiguration.

Die empfohlenen begrenzten Lösungen werden umgesetzt: bekannte kurze Anzeigenamen
ohne Bestandsmigration, zwei ausdrücklich bezeichnete Codexinstallationsziele und
bewusst qualifizierte Clientversionen mit überprüfbarem Rebind. Bestehende
Corepatches und Free-Kostenschutz bleiben erhalten. Neue providerseitige Zustimmung
und interaktive Anmeldung bleiben echte, separat ausgewiesene Schritte.

## Aufteilung

| Paket | Exklusive Zuständigkeit | Anfangsstatus |
| --- | --- | --- |
| N01 | Account-/Quota-Darstellung und gemeinsamer kurzer Anzeigename | Beauftragt |
| N02 | Synthetic- und OpenRouter-Readerkorrekturen | Beauftragt |
| N03 | MiMo-Browser und zugehörige Fixtures | Beauftragt |
| N04 | Cloudflare-Readerdiagnostik und zugehörige Fixtures | Beauftragt |
| N05/N06 | Profiles-UI, Codexsetup und danach Gruppenverwaltung | Beauftragt; ein gemeinsamer Owner |
| N06-Vertrag | Reorder-/Gruppen-API und Clientanschluss | Beauftragt; eigener Backendowner mit Integratorabstimmung |
| N07 | Portable Komponentenlieferung und begrenzte Diagnostik | Beauftragt |
| N08 | Codexdispatcher, Wartung und Rücknahme | Beauftragt |
| N09 | Unabhängige Integration, native Abnahme und genaue Evidenzgrenzen | Vorbereitung parallel |

Root koordiniert gemeinsame Verträge, Build-/Projektdateien, Integration und
Auslieferung. Worker arbeiten in eigenen Gitworktrees unter Benutzer-Cache,
nicht auf dem Desktop. Maximal zwölf aktive Agenten einschließlich Root.
Fremde schmutzige Dokumente und gesicherte Worktrees bleiben erhalten.

## Ausführung und Evidenz

Vor parallelen Änderungen gemeinsamer Ausgangscommit. Keine konkurrierenden
Builds/Restore-/Publishes: Root vergibt einen begrenzten seriellen Slot.
GUI ausschließlich eigenes Xvfb und D-Bus; TMPDIR unter
`~/.cache/lain-build-tmp`. Die aktuelle Maschine hat Speicherdruck, deshalb erst
Quellarbeit und kleine Python-/statische Checks, danach passende begrenzte Tests.
Keine Veränderung des externen `model-usage`-Diensts.

Pro Paket werden Source, lokale Prüfungen, Paket-/Installationsergebnis und reale
Providerqualifikation getrennt nachgetragen. Offen benötigte Nutzeranmeldung oder
Accountrechte verhindern keine unabhängige Quellimplementierung und dürfen nicht
als erfolgreicher Monitoringabruf ausgegeben werden.
