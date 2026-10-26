# Plattforminstaller: synthetische CI auf nativen Runnern

Der Workflow `.github/workflows/platform-installer-contracts.yml` führt die
unveränderten `tests/platform-installers/test_contract.py` auf `ubuntu-latest`,
`macos-latest` und `windows-latest` mit Python 3.12 aus. Die Matrix läuft seriell;
ein Fehler verhindert nicht die Diagnose auf den anderen Betriebssystemen.
PR und Push lösen nur bei Änderungen an Workflow, dieser Dokumentation,
`packaging/platform-installers/**`, `tests/platform-installers/**` und den beiden
Mac-/Windows-Entrypoints aus. Manueller Start ist ebenfalls möglich. Es gibt
keinen Publish-, Installations- oder Releasejob.

## Prüfungen und Isolation

- Python-Engine und Contracttest werden mit `compile(..., 'exec')` im Speicher
  kompiliert. `PYTHONDONTWRITEBYTECODE=1` verhindert Import-Pycache im Quellbaum.
- Ubuntu und macOS prüfen `scripts/install-macos.sh` mit `bash -n`.
- Windows parst `scripts/install-windows.ps1` über
  `System.Management.Automation.Language.Parser.ParseFile`; Parserfehler sind
  Fehler des Jobs. Der Entrypoint wird dabei nicht ausgeführt. Der Runner nutzt
  PowerShell 7; dies ist kein eigener Nachweis für Windows PowerShell 5.1.
- Die gesamte bestehende Suite läuft ohne Teständerung, Überspringen oder
  erwartete Fehler. Leere Suites und übersprungene Tests lassen den Job scheitern.
- Der temporäre Fixturebereich liegt unter dem kanonisch aufgelösten
  `RUNNER_TEMP`. Das vermeidet macOS-Symlink-Vorfahren wie `/var`, ohne die
  Sicherheitsprüfung `safe()` zu umgehen. Windows muss Dateisymlinks anlegen
  können; eine vorgeschaltete Probe scheitert andernfalls explizit, ohne
  Administratorrechte anzufordern oder den Symlinktest zu deaktivieren.
- Die Lifecyclefälle ersetzen `layout`, `offline`, `architecture` und
  `integration`; ihre echten Dateioperationen bleiben im temporären Testbereich.
  Renderingfälle ersetzen `Path.home` und `APPDATA`. Kein Installerentrypoint,
  echtes Paket, Prozessstart, Registry-/PATH-Eingriff oder Schreiben ins
  Runnerhome ist Teil dieser Prüfung. Abschließend muss der Checkout sauber sein.

Die Suite konstruiert synthetische Mach-O-/PE-Header und temporäre
`native-qualification.json`-Fixtures mit positiven wie negativen Vertragswerten.
Diese unveränderte Testmechanik ist kein erzeugter nativer Abnahmebeleg: Der
Workflow stellt keinen eigenen `native-qualification`-Bericht aus, übernimmt
keine Fixture als Releasebeleg und lädt keine Artefakte hoch.

## Berechtigungen und Pins

Nur `contents: read`, keine Secrets oder persistenten Checkoutcredentials,
kein `pull_request_target`, keine Paketinstallation, Caches oder Artefakttransfers.
Die einzigen verwendeten Actions sind Checkout und Pythonsetup; deren reguläre
Source-/Python-Beschaffung ist die einzige vorgesehene Downloadfunktion.
Die Commit-Pins wurden am 26.10.2026 gegen die offiziellen GitHub-Repositories
über `releases/latest` und die jeweiligen Git-Tag-Refs geprüft:

| Action | Release | Commit |
| --- | --- | --- |
| [actions/checkout](https://github.com/actions/checkout/releases/tag/v7.0.1) | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| [actions/setup-python](https://github.com/actions/setup-python/releases/tag/v7.1.0) | v7.1.0 | `6d4fd22b20465ccc0f7354476588e8df7c9ca0e0` |

Runnerimages und die Python-3.12-Patchversion bleiben beweglich. Die Matrix
behauptet keine Abdeckung aller vier Zielarchitekturen durch echte Binärausführung;
die RID-/Modusmatrix ist synthetisch.

## Lieferstand und offene native Grenzen

Lokal unter Linux am 26.10.2026: 13 unveränderte Contracttests bestanden, in der
gemeinsamen `flock`-/systemd-Verifikationslane mit Speicher-/CPU-Limits. Python-
Kompilierung, Bashsyntax und YAML-Struktur wurden separat geprüft. Keine
Test-/Installerquelldatei wurde für diese Lieferung verändert.

Die Quellprüfung zeigt keine zwingende OS-Blockade bei kanonischem temporärem
Pfad und verfügbarer Symlinkfunktion. Insbesondere laufen Lifecyclefälle mit
dem Zielargument `macos` auch auf Windows nur gegen ersetzte Integration; sie
testen dort keinen echten macOS- oder Windows-Systemlebenszyklus. Fehlende
Windows-Symlinkrechte oder andere native Dateisystemabweichungen bleiben harte
CI-Fehler und müssen anhand des ersten echten Laufs untersucht werden.

Es wurde kein GitHub-Lauf gestartet und lokal weder macOS noch Windows oder
der PowerShellparser ausgeführt. Der erste öffentliche CI-Lauf steht aus.
Auch ein vollständig grüner Lauf bestätigt nur Contracttests und Syntax auf
den jeweiligen Runnern. Echte native Pakete, Host-/CLI-/GUI-Ausführung,
Autostart/LaunchAgents, Benutzer-PATH, Updates/Uninstall im Benutzerkonto,
Crash-Recovery, Signierung/Notarisierung und Windows-Coreportabilität bleiben
separate offene Abnahmen. Keine native Paket-, GUI- oder Corefreigabe folgt
aus diesem Workflow. Frühere Aussagen „kein Workflow“ im ursprünglichen
Installerlieferbericht beschreiben dessen damaligen Stand.
