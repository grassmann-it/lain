# UX Pilot: Startauftrag für eine neue Codex-Sitzung

Vorbereitung vom 24.10.2026 auf lain `5dc916f`. Zwei GPT-6.1-Sol-Worker haben
318 aktuelle PNGs der Produktionsoberfläche mit synthetischen Daten auf privaten
Xvfb-Displays aufgenommen. Ein Astra-Max-Worker hat das UX-Review und konkrete
Designprompts erstellt. Die Account-Connect-Arbeitspakete sind separat ausgearbeitet und
unabhängig gegengeprüft. **In dieser Vorbereitungsrunde wurde kein Screenshot an
UX Pilot übertragen und keine Designgenerierung gestartet.**

UX Pilot ist lokal als OAuth-MCP unter `https://mcp.uxpilot.net/mcp` eingetragen.
Seine Tools waren in der vorbereitenden Sitzung nicht verfügbar. Der Nutzer
möchte die eigentliche Arbeit bewusst in einer neuen Codex-Sitzung starten.
Eine Konfiguration oder lokale Authstatusanzeige ersetzt keinen erfolgreichen
Toolaufruf; die nächste Sitzung prüft ihre tatsächliche Verbindung.

## Kopierfertiger Startprompt

```text
Übernimm die vorbereitete UX/UI-Überarbeitung von lain in /home/tomka/Desktop/lain.
Nutze architecture-sparring und die UX-Pilot-MCP-Verbindung. Ziel sind konkrete
Designentwürfe in UX Pilot; noch keine Änderungen am Avalonia-Produktcode.

Lies zuerst AGENTS.md und diese vorbereiteten Unterlagen:
- docs/ux-review-2026-10-24.md
- docs/uxpilot-design-prompts-2026-10-24.md
- docs/brand-reference.md
- docs/account-connect-redesign-2026-10-24.md
- docs/account-connect-work-packages/README.md

Aktuelle Referenzen liegen unter artifacts/ux-review-20261024/:
index.html ist die Gesamtgalerie, review-pack.json nennt zwölf Einstiegsbilder
und alle 318 Screenshots. Hauptseiten, Barebones/Simple/Advanced, schmale und
breite Fenster sowie Vorher-/Nachherzustände sind bereits frisch aufgenommen.
Verwende die synthetischen Originalbilder; wiederhole die Aufnahme nicht pauschal.
Betrachte die zum jeweiligen Prompt passenden Bilder vor dem Generieren.

Prüfe die tatsächlich verfügbaren UX-Pilot-MCP-Tools und ihre Schemas. Lege für
lain eine eigene Entwurfsseite an; bestehende Designs weder überschreiben noch
löschen. Lade nur ausgewählte synthetische Referenzen und das vorhandene Logo
hoch, wenn die Tools das unterstützen. Keine privaten Kontodaten/Credentials.

Beginne mit Accounts und dem Accountdetail: ruhige, scanbare Kontingentkacheln
mit Rest/Reset und ein fokussiertes Detailpanel, das andere Kacheln nicht
verschiebt. Prüfe den Entwurf gegen das Astra-Review, korrigiere erkennbare
Abweichungen und leite daraus eine konsistente Komponentensprache ab.
Danach bearbeite Connect, Models/Profiles, Usage/Live und Settings mit den
vorbereiteten Detailprompts. Keine ungezielte Serie unabhängiger Gesamtlooks.

Behalte den Kern: sehr dunkles Grün/Schwarz, Mintakzent, vorhandene Katze links,
native Desktopanmutung und realistisch in Avalonia mit Semi/Ursa umsetzbare
Komponenten. Verbessere Hierarchie, Typografie, Dichte, Beschriftungen und
Aufklappverhalten. Keine generische SaaS-Sidebar und keine neue Markenidentität.

Accounts bleiben Startseite; Profiles bleiben direkt erreichbar. Die einzige
Barebones/Simple/Advanced-Auswahl bleibt rechts unten. Informationsmodus und
Monitor/Gatewaybetrieb sind getrennt. Die kleine Monitoransicht zeigt keine
irrelevante Proxykonfiguration. Relevante Kontingentfenster sind ohne Öffnen
erkennbar; unbekannte Werte, Guthaben ohne Limit und vergangene Resets bekommen
keine erfundenen Prozentwerte. Rechtsklick und sichtbares ⋯ teilen dieselben
Aktionen. Account Connect folgt Anbieter → Zugang → Kontingente → optionale
Modelle; noch ungeklärte Loginmethoden nicht als bereits unterstützt ausgeben.

Dokumentiere UX-Pilot-Seiten-/Design-IDs, Links, verwendete Prompts/Referenzen,
beibehaltene Elemente, Änderungen und offene Punkte lokal. Prüfe die erzeugten
Bilder selbst. Keine Produktimplementierung, Installation, Dienstneustarts,
echten Providerlogins, Käufe oder Veröffentlichung öffentlicher Previews.
Falls MCP-Zugriff fehlt, benenne den konkreten Verbindungsblocker und erhalte
die vorbereitete Übergabe; gib fremde oder lokal erzeugte Mockups nicht als
UX-Pilot-Ergebnis aus.
```

## Quellen und Prüfgrenzen

- [Gesamtgalerie](../artifacts/ux-review-20261024/index.html)
- [Referenzpaket und Bildhashes](../artifacts/ux-review-20261024/review-pack.json)
- [Seitenmanifest](../artifacts/ux-review-20261024/pages/manifest.json)
- [Interaktionsmanifest](../artifacts/ux-review-20261024/interactions/manifest.json)
- [Astra-Review](ux-review-2026-10-24.md)
- [Englische UX-Pilot-Prompts](uxpilot-design-prompts-2026-10-24.md)
- [Vorhandenes Katzenlogo](../src/Lain.Desktop/Assets/Brand/lain-cat.png)
- [Account-Connect-Arbeitspakete](account-connect-work-packages/README.md)

Screenshots zeigen den tatsächlichen Quellstand mit künstlichen Daten; sie
beweisen keine Livequotas oder bereits implementierten Zielabläufe. Es gab
gezielte Screenshot-Harnessbuilds und lokale synthetische UI-Prüfungen, keine
Produktinstallation. Drei ausschließlich dadurch veränderte Lockfiles wurden
nach Prüfung der konkreten Restore-Differenz auf ihre ursprünglichen Bytes
zurückgesetzt. Produktcode, echte Accounts, Dienste und Nutzerdisplay bleiben
unverändert. Die Gallery- und Bilddateien liegen unter dem ignorierten
Artefaktverzeichnis; für eine andere Maschine müssen sie ausdrücklich mitgegeben
werden. Eine neue Sitzung auf demselben Rechner kann die angegebenen Pfade lesen.
