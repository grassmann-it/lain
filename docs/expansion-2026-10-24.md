# Erweiterung: Inspector, Auswertungen und Anbieter

Der Nutzer hat am 24.10.2026 die autonome Umsetzung ohne weitere Rückfragen
beauftragt. Bis zu 20 Worker sind erlaubt; mindestens zehn sollen sinnvoll
parallel arbeiten. Architektur-Sparring unterstützt Zuschnitt und Gegenprüfung.
Der installierte Kachel-/Quota-Stand bleibt während der Entwicklung nutzbar.

## Bestätigte Ergebnisse

- Look and feel der alten `~/.local/bin/codex-proxy-tui` weiter übertragen:
  verständliche Begriffe, Kontingentkacheln und direkt erkennbare Werte. Neueste
  Korrektur: unterschiedlich hohe Kacheln dürfen keine großen leeren Rasterzellen
  erzeugen; unabhängige Spalten bzw. vergleichbare dichte Anordnung verwenden.
- Laufende eigene Streams in einem Inspector ansehen. Zusammengehörige
  Unterhaltungen, Turns, Requests, Versuche und Toolfortsetzungen gruppieren,
  Worker/Subagenten anhand belegbarer Identitäten zuordnen. Nicht jeden Toolcall
  als neuen Chat darstellen und unbekannte Beziehungen nicht erfinden.
- Usage für alle eigenen Routen und Anbieter: Modellfamilie, konkrete Route,
  Hersteller/Inferenceanbieter/Gateway/abrechnendes Konto, Reasoning/Modus und
  verfügbare Worker-/Gruppeninformationen auswerten. Nutzung, Tokens und Kosten
  nur auf vorhandener Messgrundlage vergleichen, auch wenig genutzte Konten
  erkennbar machen. Keine Kündigungen oder Käufe automatisch ausführen.
- Performance mit klaren Messdefinitionen und Stichprobenzahlen auswerten:
  Antwortbeginn, Gesamtdauer, belegte Tokenraten; keine erfundenen Werte oder
  Mediane aus einer paginierten Stichprobe.
- Anbieter leicht ergänzbar machen: deklarative Fähigkeiten/Protokolle,
  Onboarding, benötigte Credentials, Accountlinks, Budgetarten, Rate-/Parallelitäts-
  limits, Modellmatrix je konkreter Route und ehrliche Zustände ohne Zugangsdaten.
- "Nur kostenlos" muss kostenpflichtige und unbekannt bepreiste Fallbacks
  ausschließen. Bezahlte Alternativen müssen bewusst konfiguriert sein.

## Anbieterpriorität und Recherchegrundlage

Die ausführliche Nutzerliste liegt lokal unter
`/home/tomka/.codex/attachments/30f7fbdc-9aae-42c7-b5ec-ee9107a486ae/pasted-text-1.txt`.
Ihre Preise/Modelle/Limits sind Recherchehinweise, keine verifizierten Fakten.
Offizielle Dokumentation vor Implementierung prüfen und Quellen/Prüfdatum erfassen.

1. NVIDIA Build / Step 5 Preview; StepFun direkt / Step Plan.
2. Poe und OpenCode Go / Go Plus.
3. Gemini API, Inception Mercury, OpenRouter, Requesty.
4. Cloudflare Workers AI, Groq (nicht Grok), Mistral.
5. Alibaba Model Studio, Nebius TokenFactory, Synthetic, xAI/Grok.

Abotypen, PAYG, dauerhaft kostenloses Budget und befristete Trials bleiben getrennt.
Credits, Punkte und Neurons sind keine Tokens. Account-/Projekt-/Workspace-Pools
werden nicht mit unabhängigen Modellbudgets verwechselt. Ohne Keys werden
Adapter/Onboarding mit Mocks geprüft; echte Verbindungen bleiben sichtbar ungeprüft.
Keine Accounts, Abos oder Käufe anlegen. Offizielle API-/OAuth-Wege verwenden;
Consumer-Abos nicht als universelles Proxybudget behandeln.

## Umsetzung und Grenzen

Integrator hält gemeinsame Verträge und Hostverdrahtung zusammen; ein
Vertragsworker darf konkrete DTO-/API-Vorschläge und Patches vorbereiten.
Ein Datenbankworker besitzt sämtliche Migrationen. Core-Instrumentierung,
Inspector, Usage, Desktop, CLI und Providerkatalog arbeiten in getrennten
Worktrees. Fachänderungen folgen den abgestimmten ausführbaren Verträgen;
offizielle Anbieterrecherche und unabhängige Gerüste laufen sofort parallel.

Nur der eigene lain-Core wird instrumentiert. Legacyproxy/Queue bleiben
unangetastet, normale Codex-Konfiguration wird nicht umgeschrieben. Authheader,
Keys und Refresh-Tokens gehören nicht in Inspector, Usage oder Logs. Laufende
Inhalte benötigen begrenzte lokale Puffer, klare Lebensdauer und explizite
Redaktion; die vorhandene dauerhafte Usage darf keine Payloadsammlung werden.
Belegte Identitäten und Herkunft jeder Gruppierungsdimension erhalten.

Alle GUI-Tests ausschließlich auf privatem Xvfb. Produktive Updates erst nach
passenden synthetischen Tests, unabhängiger Prüfung und kontrollierter Installation.
Neue Core-Binaries müssen die bestehende Identitäts-/Dauerhaftigkeitsqualifikation
weiter erfüllen; der bisher qualifizierte Build bleibt als Rückfall erhalten.
