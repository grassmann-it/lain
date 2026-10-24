# UI-Struktur: installierte Abnahme

Stand 24.10.2026. **Implementiert, geprüft und installiert.** Anwendungscode und
alle drei frischen, eigenständigen Linux-x64-Publishes stammen aus
`a66cb2a19774d897702724f9f40755b74c82b2a6`, Informationsversion
`1.0.0+a66cb2a`. Nachfolgende Commits betreffen Tests und Lieferdokumentation.

## Ergebnis

Accounts ist der einzige vollständige Accountbestand und die Startseite.
Models, Profiles, Usage und Live bleiben direkt erreichbar; Anbieter entdecken
liegt innerhalb von Accounts. Simple ist Standard, Barebones und Advanced setzen
gemeinsame Ansichten anders auf. Dichte, Pins, Reihenfolge und Gruppierung werden
separat von der Hostkonfiguration gespeichert. Lokale Details, Entwürfe und
Navigationskontext überleben die geprüften Refresh-/Darstellungswechsel.

Alle gemeldeten Kontingentfenster bleiben in geschlossenen Kacheln zugänglich;
Usage zeigt zuerst Kennzahlen und eine Hauptaufschlüsselung. Profiles trennt
Auswahl, gespeicherten Stand, globale Anwendung und Clientexport. Live priorisiert
lesbare Ausgabe und erhält seine begrenzte Aufbewahrung. Die Benutzerkatze wird
unverändert als Quelle eingebettet; UI und Linux skalieren sie. Fenstertitel und
Launcher heißen **lain — Local AI Gateway**.

## Nachweise

| Prüfung | Ergebnis |
| --- | --- |
| Produktionsoberfläche gegen synthetische HTTP-Fixtures | 571 Checks, 104 Screenshots, 1120 × 780 und 800 × 560, ausschließlich privates Xvfb |
| Unabhängige UX-Abnahme | Astra: 25 finale Fixturebilder; Sol: 17 repräsentative Bilder; keine blockierenden Befunde |
| Host-HTTP-Grenzen in privatem Netzwerknamespace | 11 Gruppen, einschließlich Revisionen, Pending/Apply, Auth und fehlgeschlagener Anwendung; 0 Anbieteraufrufe |
| Installer mit temporären Nutzerdaten | Pfade/Argumente, Rechte, Desktopquoting/-Ownership, Updatebackup und Prüfsummen bestanden |
| Frische tatsächliche Paketinstallation | 7 Gruppen; mitgelieferter Core, CLI/API-Parität, ein synthetischer Request, Neustart, Update/Rollback desselben Artefakts und Offlineverhalten bestanden |
| Kontrolliertes Update der eigenen Installation | Zusammengehöriges gestopptes App-/Datenbackup; gewünschter/aktiver Stand 4/2, Credentials, geschützte externe Dateien und Legacydienst erhalten |
| Exakte installierte Oberfläche | 104 Checks, 20 Bildaufnahmen bei beiden Größen; nur erlaubte GET-Endpunkte, isolierte Darstellungspräferenzen, Usage vor/während/nach Fensterschließen unverändert |
| Native Linux-Integration | Tatsächlicher `gio launch` im privaten Xvfb; passender Titel, Prozess, normale Fensterklasse und transparentes 128 × 128 Fenstericon nachgewiesen |

Die installierten Bilder aller fünf Ziele bei 800 × 560 wurden zusätzlich von
Astra geprüft; keine neuen Befunde. Der installierte Desktop-Assembly-Hash lautet
`47e5f1815301668817f505f03f7371d46c67a602a67d78b006d731cba56b612d`.
Der unveränderte Core hat SHA-256
`bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`.

Im festen UTC-Zeitraum 01.10. bis ausschließlich 24.10.2026 blieben **1 Request,
1 Versuch, 81.986 Tokens und Cursor 2** erhalten. Der Hintergrunddienst war
während des Fensterschließens weiterhin bereit. Die normale Codexkonfiguration
und der alte Proxydienst wurden weder geändert noch neu gestartet; dessen
flüchtige Logs/Temporärdateien sind vom Fingerprintvergleich ausgenommen.

Backup: `~/.local/share/lain/upgrade-backups/20261024T110050Z-1b2b6bf2`.
Lieferpaket: `artifacts/lain-linux-x64-0.1.0-ui-structure-20261024.tar.gz`.
Der [Integritätsbeleg](../tests/acceptance/evidence/ui-structure-staged-integrity.json)
bindet den gestagten Stand; die unten genannten installierten Belege beschreiben
die tatsächlich geprüften Payloads. Der damalige Paket-Gate blieb vor der
anschließenden CLI-/Tray-Lieferung noch `releaseQualified=false`; ein separater
finaler Lieferbeleg für diesen historischen Stand wurde nicht geschrieben.
Detailberichte liegen unter `tests/acceptance/evidence/ui-structure-*.json`;
die [unabhängige UX-Prüfung](ui-structure-review.md) beschreibt die Bildabnahme.

## Geltungsbereich

Dies ist die Abnahme der UI-/Status-Erweiterung und ihres kontrollierten Updates.
Die frühere vollständige Expansion-Abnahme und Anbieterqualifikationen werden
mit ihrer damaligen Quelle unter `historical/expansion-baseline/` aufbewahrt;
die gesamte frühere Expansion-Suite wurde hierfür nicht wiederholt.
Die positiven Einzelprüfungen sind keine nachträglich gesetzte Paketfreigabe;
`expansionQualified=false` bedeutet hier „nicht erneut für diesen Stand ausgeführt“.

Es gab keine neuen echten Inferenz-, OAuth-Refresh- oder Revokeproben. Neue
Anbieter bleiben live `not_tested`. UI-Handlerprüfungen verwenden geroutete
Buttonereignisse, keine OS-Zeiger-Hit-Tests. Die native Probe ergänzt den echten
Launcher und X11-Iconnachweis; sie vergleicht nicht jedes skalierte Iconpixel
mit der Originalquelle.

Die alte Runtime benennt ihr angewendetes Profil noch nicht eindeutig. Der
aktuelle Host meldet deshalb `activeProfileId=null`; die UI zeigt diesen Zustand
ehrlich unbekannt. Offene Revision 4 wurde nicht angewendet, um diese Information
zu ergänzen. Eine spätere erfolgreiche, ausdrücklich ausgelöste Profilanwendung
veröffentlicht die neue eindeutige Metadatenkombination.

Der bestätigte [Monitor-Folgeauftrag](monitor-mode.md) umfasst zunächst
Kontingente und Resets ohne lokalen Core und ist außerhalb dieser Lieferung.
