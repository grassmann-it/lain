# Umsetzung der Account-, UX- und Stabilisierungspakete

## Auftrag und Ausgangspunkt

Der Nutzer hat am 24.10.2026 das Ziel **„setze die arbeitspakete um“** aktiviert.
Die vorherigen Planungsgrenzen sind damit für die Umsetzung aufgehoben;
inhaltliche Garantien, explizite Nichtziele und anbieterbezogene Nachweise bleiben.
Basis: `5dc916fcea702f2a196520903937570745e26dac`.

Maßgeblich sind [AC-00–10](account-connect-work-packages/README.md),
[ST-01–05 und Übergabereife](product-state-and-gaps-2026-10-24.md),
[UX-Review](ux-review-2026-10-24.md) und [Modi/Aktionen](ui-modes-and-actions.md).
Die [parallele UX-Pilot-Runde](uxpilot-design-results-2026-10-24.md) bleibt eigene
Entwurfsarbeit; konkrete Ergebnisse gegen Produktverträge prüfen und dann integrieren.

Ziel ist die durchgängig geprüfte Lieferung. Ein enger erster Durchstich mit
Kilo/Monitor reduziert Integrationsrisiko und ersetzt nicht die übrigen Pakete.
Vorhandene Accounts, Namen, Routen, Profile, Secrets und History erhalten.
Legacyproxy und normale Codexkonfiguration bleiben unberührt. Kein Kauf,
Paid-Fallback oder versteckter Inferenztest. GUI ausschließlich privat auf Xvfb.

## Koordination

Höchstens zwölf gleichzeitig arbeitende Agenten einschließlich Integrator,
eigene Worktrees ab gemeinsamem Ausgangscommit. Alle Builds, Publishes,
Installationen und Runtimeabnahmen werden durch den Integrator koordiniert.
Fachworker dürfen isolierte nichtbauende Quell-/Fixturearbeiten parallel ausführen;
Build-/Runtimefenster ausdrücklich absprechen. Commitadresse `tom@grassmann-it.de`.

Ein Eigentümer für gemeinsame Core-/Client-DTOs, Hostverdrahtung, Registry,
Projektdateien und Migrationen: Integrator. Fachworker melden nötige Änderungen
als konkrete Vertragsvorschläge. Fachliche Interfaces mit ausführbaren Beispielen
vor Integration festschreiben; keine konkurrierenden Account- oder Loginservices.
Accountbasis besitzt Secrets/Generation/Connect-Lifecycle und Quotaauflösung.
UI-Owner koordiniert Desktop, CLI, Clientwrapper und Katalogdarstellung;
Codex-Artefaktinstaller gehört ausdrücklich dem Profilpaket.

Unverwandte laufende Änderungen in `docs/product-scope.md`, Providerrecherche
und `docs/provider-work-packages/` nicht mitstagen oder überschreiben. Die
UX-Pilot-Ergebnisdateien gehören der parallelen Entwurfsrunde.

## Technische Festlegungen für die erste Integration

- Monitor/Gateway ist eine ausdrückliche Betriebswahl, unabhängig vom UI-Modus.
  Alte Konfiguration bleibt Gateway. Monitor lädt/startet keinen optionalen Core
  und erzeugt keine künstlichen Collectorfehler. Gespeicherte History bleibt lesbar.
- Neue Zugangsmethoden dürfen Accounts ohne Modelltemplate, Tarifzwang oder
  Inferenzkey erzeugen. Inferenz und Monitoring besitzen getrennte Fähigkeiten
  und gebundene Credentials; derselbe geeignete Key wird nur einmal gespeichert.
- Stabile Account-ID bei Reconnect; neue private Credentialgeneration zuerst
  sichern, dann Referenz atomar veröffentlichen. Rotation/recoverbare Remoteergebnisse
  nach AC-00/01; alte Refresh-Tokens nicht durch Rollback wiederverwenden.
- Bestehende Routenschalter bleiben gespeicherte Änderungen mit explizitem Apply.
  ST-02 ergänzt Vorschau und verlässliche Zustands-/Recoverysemantik. Eine neue
  sofortige temporäre Sperre oder mehrere gleichzeitige Clientruntimes ist kein
  stiller Nebeneffekt. Laufende Streams bei Apply sichtbar berücksichtigen.
- ST-01 erfindet keine Fähigkeiten und entfernt keine Tools unbemerkt. Bekannte
  Einschränkungen brauchen einen qualifizierten Anschluss am echten Versandpfad.
- ST-03 ergänzt bekannte Teilsummen pro Tokenkomponente samt Messabdeckung;
  vollständige Summen bleiben bei fehlender Messung null. HTTP 429 alleine wird
  nicht zu „Abokontingent erschöpft“. Nur feste bereinigte Fehlerkategorien speichern.
- ST-04 bewahrt bestehende Daten. Größenanalyse, segmentiertes Journal bzw.
  Archivierungsansatz müssen Source-/Cursor-/Replaygarantien erhalten. Keine
  automatische Löschung ohne festgelegte Aufbewahrung; Recovery zuerst qualifizieren.
- ST-05 liefert geprüfte private Linuxpakete. Öffentliche Veröffentlichung und
  Wahl einer Anwendungslizenz bleiben eigene Produktentscheidungen.

## Abnahmeverzeichnis

| Paket | Zu beweisendes Ergebnis | Stand |
| --- | --- | --- |
| AC-00/01 | Account-/Methoden-/Ownerverträge, verlustfreie Migration, Host ohne Core, Reconnect/Cancel/Rotation-Recovery | offen |
| AC-02/09 | Einheitlicher privater Desktop-/CLI-Connect, optionale Modelle, echte Fähigkeiten aller 25 Anbieter | offen |
| AC-03 | Kilo Wallet/Pass, Requesty Self, Synthetic-Erweiterung, Step-Erhalt, xAI-Metadaten | offen |
| AC-04 | OpenRouter/Poe-Flow und Callback mit Lifecycle; Produktionsvoraussetzungen ehrlich qualifiziert | offen |
| AC-05 | Isolierte offizielle CLI-Anschlüsse je Anbieter, gebundene Ergebnisse und passende Reader | offen |
| AC-06 | Google/Cloudflare-Monitorquelle, Scope/Rechte und OAuthtransport separat qualifiziert | offen |
| AC-07 | Kimi/Z.ai/Codex-Owner und Loginwege mit erhaltener Legacy-Snapshotgrenze | offen |
| AC-08 | Begrenzte Console-Sessionintegration, MiMo zuerst, jeder Folgeanbieter separat qualifiziert | offen |
| AC-10 | Unabhängige Offlineabnahme, Paketierung, erhaltene Installation, reale Prüfung gesondert | offen |
| ST-01 | Versionierte Client-/Modellfähigkeiten, tatsächliche Tool-/Streaming-/Kontextgrenzen, keine stillen Kürzungen | offen |
| ST-02 | Vorschau, gespeicherter/aktiver/exportierter Stand, Wiederherstellung und konsistente Clientartefakte | offen |
| ST-03 | Teilmessungen und Fehlerkategorien serverseitig, gleiche CLI/UI-Zahlen und erhaltene Replaysemantik | offen |
| ST-04 | Wachstum/Start/RAM gemessen; Datenpflege, konsistentes Backup/Restore, Fehler-/Recoveryfälle | offen |
| ST-05 | Aktuelle Build-/Supportidentität, wiederholbarer Release, Fresh install/Update/Rollback | offen |
| UX-01 | Stabile Accountkacheln/Details, alle Fenster und richtige Budgetzustände | offen |
| UX-02 | Unterschiedliche Modi, kompakte Monitoransicht und gemeinsame Objektcommands | offen |
| UX-03 | Übersichtliche Models/Profiles/Usage/Live, Settings und Navigation ohne Duplizierung | offen |

Ein Quellenbeleg, ein implementierter Adapter, lokale Fixtureprüfung, installiertes
Paket und reales Providerergebnis bleiben verschiedene Nachweise. Fehlt eine
externe Voraussetzung, den konkreten betroffenen Anschluss und die nächste Aktion
benennen; keine funktionierende Methode vortäuschen oder Gesamtpakete als fertig
markieren. Unabhängige Arbeit weiterführen.

## Erweiterung um UXD-00–09

Der Nutzer hat die neu hinzugekommenen Arbeitspakete ausdrücklich in den laufenden
Implementierungsauftrag aufgenommen. [UXD-00–09](ux-design-work-packages/README.md)
konkretisieren daher UX-01–03; vorhandene Umsetzung wird erhalten und anhand der
14 Referenzen sowie der schmalen/Monitor-/Fehlerfälle vervollständigt. Layoutannahmen
werden als überprüfbare Umsetzungsentscheidungen geführt, nicht als bereits
bestandene Designabnahme. Die Zwölfergrenze und private Xvfb-Prüfung gelten weiter.

UXD-00 Anschlussstand: `/connection-methods`, `/accounts/connect`, Login inklusive
Resourcewahl, `/runtime-mode`, Profil-Preview/Apply und ST-03-Teilmessungen sind
im gemeinsamen Host/Client angeschlossen. Erste Hostkompilation ist bestanden;
synthetische HTTP-Gesamtabnahme steht noch aus. Modelle werden über
`POST /accounts/{id}/models` mit Revision, Accountgeneration, exakter ConnectionId
und mehreren Template-IDs angehängt; der private Key bleibt beim Host.

Dateiaufteilung der Erweiterung: Shell+Accounts ein Owner; Connect+Models beim
bestehenden Desktopowner; Profiles, Usage, Live und Settings jeweils eigene
Seitenowner; zentrale visuelle Abnahme separat. Root besitzt gemeinsame DTOs,
Hostverdrahtung und Integration. Gemeinsame Shell-/Policyhelpers nur über Owner.

## Laufende Nachweise

- Ausgangsstand erneut gelesen; keine Produktänderungen seit `5dc916f` vor diesem
  Auftrag. Vorhandene Plan-/Recherchedateien werden gezielt für Worktrees übernommen.
- Integriert sind Accountgenerationen/Recovery, Reader, Browseradapter,
  Teilmessungen, Kompatibilität, Profiltransaktionen, Corejournal und die erste
  Desktopüberarbeitung. Isolierte Workerprüfungen sind bestanden, soweit ihre
  Lieferberichte dies ausweisen; neue AC-05–08-Prüfungen teilweise noch ausstehend.
- Erster integrierter Hostbuild: 0 Warnungen/Fehler. Weitere Integration seitdem
  wird erneut geprüft. Bisher keine Installation, Änderung des vorhandenen
  Dienstes oder echte Anbieteraktion in diesem Auftrag.
- AC-05–08 liefern lokale Qualifikation und explizite methodische Gates; fehlende
  eigene Clientregistrierung, stabile Identität oder Browser-/CLI-Isolation bleibt
  an der konkreten Methode sichtbar. Ein Parser ist kein funktionierender Login.
