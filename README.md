# lain

Ein lokales Werkzeug für eigene AI-Accounts, Modelle und deren Nutzung über
mehrere Coding-Harnesse. Es soll die Einrichtung vereinfachen, Modellprofile
bereitstellen und Kontingente sowie tatsächlich gemessenen Verbrauch verständlich
zeigen. Konfigurierbare Fallbacks und Kontingentoptimierung gehören zur weiteren
Produktrichtung.

**Stand 23.10.2026:** Die erste Linux-Implementierung wird integriert und geprüft.
.NET-Host, SQLite-Usage, CLI, Avalonia-Verwaltung, Quotaadapter und Installer sind
implementiert. Die Freigabe des eigenen Proxy-Builds und die gesamte Abnahme
werden in [Abnahme](docs/acceptance.md) dokumentiert; ein Build allein ist keine
Produktfreigabe. Das bestehende persönliche Proxy-Setup bleibt unabhängig.

- [Installation und Betrieb](docs/install.md)
- [Aktueller Ausführungsplan](docs/execution.md)
- [Unabhängige Abnahme](docs/acceptance.md)
- [Produktziele und aktueller Zuschnitt](docs/product-scope.md)
- [Vorhandene Technik und Übernahmegrenzen](docs/technical-inventory.md)
- [Harness-Anschlüsse und Quellen](docs/harnesses.md)
- [Proxy-Abhängigkeit und geprüfter Bestand](docs/proxy-backend.md)
- [Gemeinsamer Kern, Hintergrunddienst und Clients](docs/architecture.md)
- [Gemeinsame fachliche Verträge](docs/service-contracts.md)
- [Arbeitspakete und ihre Reife](docs/implementation-plan.md)

Die Desktop-Oberfläche wird mit Avalonia umgesetzt. Linux ist die erste
Zielplattform; der Dienst soll unabhängig vom Fenster weiterlaufen.
Der erste benutzbare Stand verbindet eigenen verwalteten Proxy, Accountverwaltung
und persistente Usage, zunächst mit Codex-/ChatGPT-Abo und Command Code.
Ein gemeinsamer .NET-Kern mit Host und Clientbibliothek ist implementiert.
Profile und ein Modell-Wizard sollen die Auswahl
für Clients wie Codex, Claude Code und OpenCode zugänglich machen. Ein kleiner
KI-Konfigurationschat wird als ergänzende Bedienform erkundet. Bei Modelleignung
und Tarifkosten ist außerdem „Mit KI recherchieren“ mit Quellenlinks gewünscht.
Der Datenpfad soll auf bestehender Proxysoftware aufbauen; CLIProxyAPI ist der
bevorzugte Kandidat für eine mitgelieferte Abhängigkeit.

Name und gewünschte gestalterische Referenzen: **Serial Experiments Lain**, das
„Wired“ und **Ghost in the Shell**. Das Produkt soll trotz dieser Anklänge
minimalistisch bleiben und Nutzung/Kontingente klar lesbar machen.

Lokale Sparringnotizen liegen Git-ignoriert unter `.architecture-sparring/`.
Bestätigte, gemeinsam relevante Ergebnisse werden in die Dokumentation übernommen.
