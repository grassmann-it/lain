# AC-10 – Unabhängige Abnahme und Lieferung

Stand: 24.10.2026, Codebasis `5dc916f`. Dies ist ein **Planauftrag**, keine
Implementierungs-, Login-, Test-, Build- oder Installationsfreigabe. Grundlage:
[Account-Connect-Entwurf](../account-connect-redesign-2026-10-24.md),
[bestätigte Produktziele](../product-scope.md) und
[bisherige Abnahmegrenzen](../acceptance.md). Der Entwurf umfasst 25 Anbieter und
35 Produktvarianten; daraus folgt keine Freigabe aller vorgeschlagenen Methoden.

## Ziel und Besitz

Unabhängig nachweisen: Anbieter → qualifizierter Zugang → Account/Quota → optional
Modelle; ein Monitor funktioniert ohne installierten/gestarteten Core, Modellroute
oder Inferenzsecret. Bestehender Gatewaybetrieb, Accounts und Daten bleiben erhalten.
Abnahme prüft beobachtbares Verhalten der integrierten Anwendung, nicht nur DTO-
Beispiele oder das Verhalten eines selbst geschriebenen Fakes.

W9 als unabhängiger Reviewer besitzt ausschließlich **neue** Abnahmefixtures, Prüfskripte und Evidence unter
`tests/acceptance/account-connect/` und `tests/acceptance/evidence/account-connect/`.
Testharness-Projekte nur in diesem neuen Bereich; keine fremden Tests überschreiben.
Gemeinsame DTOs, `Program.cs`, Migrationen, Build-/Publish-/Installskripte und deren
Einbindung besitzt AC-00; W10 übernimmt die von W0 ausdrücklich zugewiesenen
Auslieferungsskripte. W9 verändert diese nicht parallel. Produktfehler gehen mit reproduzierbarem Befund an ihren
Paketowner zurück. AC-10 repariert nicht nebenbei die zu prüfende Implementierung.

## Eingang und Abhängigkeiten

| Paket | Vor der jeweiligen Abnahme benötigt |
| --- | --- |
| AC-00 | Versionierte gemeinsame Verträge, Migrations-/Rollbackkonzept, festgelegter Releaseumfang, exakt gebaute Artefakte und serialisierter Build-/Runtimeslot. |
| AC-01 | Stabile Account-/Ressourcenidentität, Credentialowner, Operationen/Revisionen, echter Betrieb ohne Core und unabhängiger Quotalifecycle. |
| AC-02 | Derselbe Ablauf in UI/CLI, geheime Eingabe und Projektionen, erkennbare Teil-/Fehlerzustände, optionale Modellwahl. |
| AC-03 | Readerfixtures mit nativen Einheiten/Fenstern, Freshness, partiellem Ergebnis und belegten Credentialanforderungen. |
| AC-04–08 | Pro ausgelieferter Methode Vertrag, Herkunft/Version, Credentialbesitz, Seiteneffekte und gezielte Fehler-/Lifecyclefixtures; offene Erkenntnisversuche ausdrücklich getrennt. |
| AC-09 | Vollständiges Methoden-/Fähigkeitsinventar je Produkt und Region; sichtbarer Fallback und Freigabestatus. |

Fixtures und Gegenprüfungen können nach Vertragsfreeze beginnen. Endabnahme wartet
nur auf die tatsächlich ausgelieferten Pakete; ein offener Google-/Sessionversuch
blockiert keinen unabhängig vollständigen Same-Key-Reader. AC-00 entscheidet den
Lieferumfang sichtbar; AC-10 darf fehlende Abnahme nicht durch Statusumbenennung lösen.

## Nachgewiesene Ausgangsgrenzen

- [Hoststart und Quotasource](../../src/Lain.Host/Program.cs) hängen heute an Core-
  Verdrahtung; `HostQuotaSource` liefert ohne Inferenzsecret kein Credential.
  Login kann den Core starten. Ein gesunder Monitor darf keinen solchen Pfad benutzen.
- [Publish](../../scripts/publish-linux.sh) baut/bündelt Core zwingend;
  [Installer](../../scripts/install-linux.sh) verlangt `proxy/lain-proxy`.
  Ein verstecktes oder ungestartetes Binary erfüllt das Core-freie Paket nicht.
- [Installerfixture](../../packaging/test-install.py) prüft ein Fakepaket,
  [Paketprobe](../../tests/acceptance/package_probe.py) reale Binärdateien mit
  synthetischer Inferenz. Letztere ist Gatewayprüfung, kein Monitor-Nachweis.
- [Upgradeprobe](../../tests/acceptance/upgrade_probe.py) bleibt ohne `--execute`
  planend; Ausführung gehört dem Integrator. Alte Datenpfadlisten müssen neue
  Credential-/Operationsspeicher berücksichtigen. `app.previous` allein belegt
  keine Rückkehr über eine geänderte Daten- oder Credentialschemaversion.
- Bestehende [Accounttests](../../tests/Lain.Accounts.Tests/Program.cs),
  [Host/Core-Verträge](../../tests/host-core-contract/README.md),
  [CLI-Fixtures](../../tests/client-cli/README.md) und Quota-/UI-Prüfungen ergänzen
  die neue Abnahme. Historische PASS-Ergebnisse gelten nur für ihre Artefakte.

## Abnahmematrix

| ID | Geplante Beobachtung und Fehlerfall |
| --- | --- |
| A01 – Erstconnect | Ohne Modell/Tarif/Dummykey speichern; Ressource nur bei echter Mehrdeutigkeit wählen. Eine bekannte Reconnectressource vorab binden, unbekannte beim Erstconnect vor Übernahme/eigener Keyanlage festlegen. |
| A02 – Bestand | Synthetischen Vorzustand mit zwei Accounts desselben Providers, eigenen Namen, getrennten Orgs/Regionen, Keys, Snapshots, History, Profilen und pending/applied Revisionen migrieren; IDs und historische Zuordnung erhalten, keine stillen Applies. |
| A03 – Reconnect | Gleiche verifizierte Identität aktualisiert den bisherigen Account; falscher Browserdefault/Orgwechsel und veraltete Revision ersetzen ihn nicht. Unbekannte Identität wird weder aus E-Mail erraten noch automatisch zusammengeführt. |
| A04 – Monitor | Frisches Paket ohne Core und ohne Inferenzcredential: Connect, Quota, Ablauf/Erneuerung und Neustart funktionieren; keine Coreprozesse, Inferenzlistener, Exporte oder Dummyroute. Nicht verfügbare Gatewayaktionen sind kein Gesundheitsfehler. |
| A05 – Wechsel | Monitor → Gateway ergänzt denselben Account erst auf bewusste Aktion; Gateway → Monitor erhält History/Routen/Profile und beendet nur eigene Coreaktivität. Kein Kauf oder automatisches Profil-Apply. |
| A06 – Ownership | Externer Snapshot bleibt ohne Refresh/Revoke und ohne Schreibzugriff auf seine Quelle; delegierte CLI bleibt Owner. Eigener OAuthowner hält genau eine Erneuerungszuständigkeit, keine konkurrierende Core-/Host-/CLI-Rotation. |
| A07 – Rotation | Gleichzeitige Quota-/Transportanforderungen, zwei Erneuerungen und alte 401-Antwort nach neuer Generation: nur zulässige Rotation, atomare Speicherung; alte Fehler löschen keine neue Session. Neustart an Speichergrenzen mit kontrollierter Fehlerinjektion. |
| A08 – Operationen | Cancel, Disconnect, Timeout, Doppelcallback, Callback nach Reconnect/Revisionwechsel und Refresh nach Entfernen mit steuerbaren Barrieren erzwingen. Kein Wiederbeleben oder Überschreiben; Listener/Prozessreferenzen und terminale Operationen werden begrenzt freigegeben. |
| A09 – Callback/Geheimnisse | Falscher State, Replay, fremde Operation, unerwartete Ressource/Redirect, abgelaufener Code und belegter Loopbackport scheitern sicher. Nur vereinbarte Rückwege; keine Bindung an öffentliche Interfaces. PKCE/Nonce dort prüfen, wo der Flow sie verlangt. |
| A10 – Teilanlage | Timeout nach Remote-Keyanlage, verlorene Antwort, Absturz vor/nach privater Persistenz: kein blindes Create-Retry. Remote-ID ersetzt kein einmaliges Secret; Wiederaufnahme, ungewisse Anlage oder gezielte Bereinigung korrekt unterscheiden. |
| A11 – Seiteneffekte | Anfragen/Datei-/Prozessaktionen protokollierende Fakes belegen: keine Inferenz, Käufe, Top-ups, Planwechsel oder kostenpflichtigen Fallbacks durch Connect/Monitor. Nur vorab offengelegte Credentialausstellung, getrennt von fachlichen Writes; Z.ai-Monitor legt keinen Keydatensatz an. |
| A12 – Resultate | Unsupported, fehlende Fähigkeit, fehlendes Readrecht, abgelaufen, temporärer Fehler, partial und stale bleiben unterscheidbar. Letzter Erfolg mit Originalzeit erhalten; unbekannt bedeutet weder aufgebrauchtes Budget noch 0; ein Quotaausfall widerruft keine Inferenzfreigabe. |
| A13 – Quotas | Kilo Free-Route ist kein Tarif; Wallet/Pass und Bonus/Periodenwerte nicht erfunden verrechnen. Native Fenster/Einheiten/Scopes erhalten; passive Header nur nach echtem Gatewayverkehr, kein erzeugter Request für Monitoring. |
| A14 – Projektionen | UI und CLI/API zeigen denselben Account, Scope, Zweck, Status und Zeitpunkt; keine zweite Credentialabfrage. Modellliste/Loginerfolg beweisen weder Inferenzrecht noch erfolgreiche Inferenz. Normale Nutzer brauchen keine eigene Clientregistrierung. |
| A15 – Gateway | Bestehende Key-/Codex-owned-/Snapshotkonten, aktive Routen und explizite Pools behalten Bindung/Transport, History und gewünschte/aktive Revision. Geänderte Grenzen mit echten Host/Core-Binaries gegen lokalen Fakeprovider prüfen. |

## Prüfablauf nach Implementierungsauftrag

1. AC-00 übergibt eingefrorenen Quellstand, geänderte Verträge, Paketinventar und
   Methodenmatrix. AC-10 gleicht Anforderungen und Readerannahmen unabhängig ab;
   jeweils nur tatsächlich unterstützte Produkte/Regionen als bereit ausweisen.
2. Neue Fixtures mit ausschließlich synthetischen Accounts/Secrets erstellen.
   Kontrollierte Uhr, Antwortfolgen und Barrieren reproduzieren A03/A07–A10;
   Fehler nicht durch Wartezeiten oder zufällige Rennläufe „testen“.
3. Integrator baut nötige Projekte seriell und koordiniert vorhandene Checks;
   AC-10 führt neue unabhängige Abnahme im zugewiesenen Slot aus. Bestehende
   [Verify-Skripte](../../scripts/verify-expansion.sh) bauen selbst: nicht parallel
   zu anderen Builds starten. Wiederholungen nur nach Änderung, Fehler oder
   offenem Befund; einen grünen unveränderten Core nicht beliebig neu qualifizieren.
4. Prozesse in neuen privaten State-/Configverzeichnissen und isoliertem Loopback
   starten; bei fehlender Isolation abbrechen, nie auf installierte Ports ausweichen.
   Bereits bekannte Fixtureports wie 18418 ebenfalls isolieren. Keine echte
   Provideradresse, Browseranmeldung oder fremde CLI-Session als Testvoraussetzung.
5. GUI ausschließlich über privaten Xvfb und isolierten Sessionbus; synthetische
   Browser-/CLI-Helfer statt persönlichem Browser. Screenshots visuell gegenprüfen.
   [Installierte UI-Harness](../../tests/desktop-installed/run-private-xvfb.sh)
   erhält bei synthetischer Abnahme explizites `--state`; ihr Default zeigt sonst
   auf bestehendes `LAIN_HOME`. Kein Test auf dem Nutzerscreen.
6. Synthetische Secretmarker in stdout/stderr, Logs, HTTP-/CLI-Fehlern, öffentlichen
   DTOs, Exporten, Screenshots und Evidence suchen. Callbackcode, PKCE-Verifier,
   Refreshmaterial, Cookies und interne Credentialpfade dürfen dort nicht stehen;
   absichtlich angezeigte Autorisierungs-URL/Usercode nur im vorgesehenen privaten
   Connectkontext. Lokale API-Authentifizierung und private Dateirechte mitprüfen.
7. AC-10 bewertet die exakten Ergebnisse; Befund → zuständiger Owner → gezielter
   erneuter Check. Eigene Prüferwartung nicht an ein fehlerhaftes Produkt anpassen.

## Paket, Update und Rückkehr

AC-00 liefert getrennt identifizierbare Monitor-/Gatewayartefakte mit Prüfsummen,
Quell-/Buildstand und nötigen Lizenznachweisen. AC-10 prüft frische Installation
aus fremdem Arbeitsverzeichnis, Launcher, Host/CLI/Desktop, Neustart und unveränderte
Dateirechte. Monitor enthält keinen Core; Gateway enthält den qualifizierten Core.
Kein Installer aktiviert Dienste oder startet Credential-/Provideraktionen heimlich.

Upgradefixture vom bisherigen Gatewaystand sowie von leerem/gefülltem Monitor:
Accounts, eigene Namen, Credentials, Snapshotbesitz, History, UI-Präferenzen,
Profile/Routen und pending/applied Revisionen vergleichen. Beobachtungen nach
Neustart müssen denselben Accounts zugeordnet bleiben; kein doppelter Collector.
Fehler vor/nach Migration und fehlender Core beim Gateway müssen eine erklärte,
wiederherstellbare Lage ergeben, kein erfolgreich gemeldetes halb installiertes Paket.

Rollback braucht ein konsistentes gestopptes App-/Statebackup und einen getesteten
Rückweg für die konkrete Schemaversion. Binäres Zurückkopieren über migrierte Daten
genügt nicht. Bei OAuthrotation niemals verbrauchtes Refreshmaterial aus dem Backup
reaktivieren; AC-01/00 definieren vor Lieferung Erhalt des aktuellen Owners oder
bewussten Reconnect. Fehlende Rückkehrfähigkeit bleibt ein offener Lieferblocker.
Echte Installation/Dienstwechsel führt nur der Integrator im beauftragten Umfang aus;
Legacyproxy, fremde CLI-Profile und normale Codexkonfiguration bleiben unberührt.

## Evidence, Freigabe und Übergabe

Pro Check: Anforderung/ID, Paket/Methode/Produkt/Region, Quellrevision, Fixture- und
Artefakthashes, genauer Befehl, Umgebung, Exitcode, beobachtetes Ergebnis, Grenzen
und offene Befunde. Geplante Checks bleiben `not_run`; `unsupported` beschreibt
die Fähigkeit und ist kein Testfehlerersatz. Keine privaten Accountdaten oder
Credentialdateihashes in teilbarer Evidence; reale Schutzinventare bleiben privat.

Separat ausweisen: **Quellen-/Erkenntnisversuch**, **produktausgelieferte Methode**,
**lokale synthetische Abnahme**, **Paketprüfung**, **tatsächliche Installation** und
**reale Providerprüfung**. Keine dieser Kategorien ersetzt eine andere. Ein später
ausdrücklich beauftragter echter Login/Read/Inferenz-/Refreshversuch wird je Methode
und Fähigkeit protokolliert; Accountzugriff oder kostenpflichtige Inferenz folgen
nicht automatisch aus diesem Plan. Fehlender Livebeleg bleibt sichtbar `not_tested`.

Fertig ist AC-10, wenn der freigegebene Umfang alle zutreffenden Checks besteht,
offene Kandidaten ehrlich abgegrenzt sind und AC-00 einen reviewbaren Lieferbericht
mit Artefaktidentität, Migrations-/Rollbackanleitung und verbleibenden Grenzen hat.
Produktfreigabe einer neuen Methode benötigt deren eigene Qualifikation; eine
erfolgreiche Installation oder ein grüner Fakeflow schaltet sie nicht automatisch frei.
Der Abschluss nennt geliefert/installiert und tatsächlich geprüft jeweils getrennt.
