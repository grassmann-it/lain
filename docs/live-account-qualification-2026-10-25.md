# Accountproben und sichtbarer Inspector, 25.10.2026

Der Nutzer autorisierte Aktivierung und kleine echte Proben aller eingerichteten
Accounts, ausdrücklich einschließlich OpenRouter Free. Drei Worker führten die
Anfragen mit festen Accountbindungen, synthetischem Text und ohne Tools aus.
Getestet wurde je ein Modellzugang pro Anbieter mit gezielten Nachproben;
**nicht alle 58 freigegebenen Modellzugänge sind qualifiziert**.

## Betriebsstand

Der eigene Gateway läuft mit Profil `lain`, Revision **35/35**, 15 erhaltenen
Accounts und 58 ausgewählten Modellzugängen. Die separaten Codexdateien sind
installiert. Vorher-/Nachher-SHA256 der gewöhnlichen `~/.codex/config.toml`
stimmen beim abschließenden Installationsschritt überein.

```sh
codex --no-daemon --profile lain
```

Groq, Gemini und OpenRouter erhielten Modellbindungen über den Host mit ihren
bereits gespeicherten Zugängen; neue Secrets wurden nicht eingegeben. NVIDIA,
Cloudflare, Cohere, Upstage, SambaNova und Kilo wurden im Profil ergänzt.
Vorhandene Accounts, bestehende Routen, andere Profile und Historie bleiben
erhalten. Eine ausschließlich für diese Probe vorbereitete, nicht kompilierbare
Synthetic-Route wurde abschließend wieder entfernt; der Account bleibt erhalten.
Der Nutzer kennt kein Synthetic-Abo, deshalb keine erfundene Tarifzuordnung und
kein PAYG-Test. Alter Proxy, dessen Queue und allgemeine Codexkonfiguration
wurden nicht verändert.

## Echte Ergebnisse

Das unabhängige Review prüfte **22 Clientcalls, 21 gespeicherte Attempts und
13 vollständige Antworten über zwölf Accounts**. Der erste Cohere-400 entstand
vor der bisherigen Versuchserfassung. Keine zusätzlichen doppelten Attempts
wurden gefunden. Die Testhelfer enthielten keine automatischen Wiederholungen
oder Ausweichrouten; es wurden keine Käufe oder Bezahlfallbacks eingerichtet.

| Account | Ergebnis und tatsächlich geprüfter Zugang |
| --- | --- |
| Codex | `gpt-6-luna`, Responses vollständig; `low` |
| Kimi | `k3`, Responses vollständig |
| Z.ai | `glm-5.3-flash`, Responses vollständig |
| Xiaomi MiMo | `mimo-v2.6-flash`, Responses vollständig |
| CommandCode | `cmd-claude-haiku-5-5`, Responses vollständig |
| NVIDIA Build | `nvidia-step-5-preview`, Responses vollständig; Always-on-Reasoning |
| Groq | `groq-gpt-oss-20b`, Responses vollständig; `low` |
| Gemini API | `gemini-3.5-flash-lite`, Responses vollständig |
| Upstage | `solar-mini4-260922`, Responses vollständig |
| OpenRouter | alter Llama-Free-Zugang 404; offizielles `openrouter/free` vollständig |
| Cloudflare | Gemma 4 mit 2.048 Ausgabetokens vollständig; kleine 512er-Proben erschöpften das Budget im Reasoning und endeten mit Streamfehler |
| Cohere | nativer Chat vollständig; Responses mit explizitem `none` noch 400 im installierten Core |
| Kilo | alter Gemma-Free-Zugang 404; aktuelles `liquid/lfm-2.5-2.6b:free` liefert Teiltext bei 512 Tokens; größere Nachprobe 429 |
| SambaNova | `Meta-Llama-3.3-70B-Instruct`, 429; keine Wiederholung |
| Synthetic | Account erhalten, ohne geklärtes Abo nicht angesprochen |

Bei unbekannter Reasoning-Unterstützung wurde kein erfundener `low`-Wert
gesendet. Das Weglassen ist keine belegte Abschaltung des Reasonings. Die
OpenRouter-/Kilo-Modelle wurden anhand ihrer öffentlichen Kataloge geprüft;
Modelle und Kataloge ändern sich. `openrouter/free` wählt selbst einen kostenlosen
Upstream. Kilos konkreter neuer Alias verwendet den belegten aktuellen Free-ID,
ohne qualifizierte Katalog-`free_only`-Bindung. Eine strikte Nullkosten-Garantie
wird für diese `configured`-Routen nicht behauptet.

Usage bleibt messwerttreu: die Serveraggregation enthält unbekannte Werte und
darf nicht durch Addition erfolgreicher Clientreports als vollständige Summe
ersetzt werden. Die zwei frühen Cloudflare-Streamfehler wurden vom installierten
Core fälschlich erfolgreich abgeschlossen. Auch Kilos Teilantwort steht dort als
Erfolg; das ist keine Bestätigung einer vollständigen Antwort. Historische
Datensätze wurden nicht nachträglich umgeschrieben.

## Inspector und Nutzerbeobachtung

Die anfänglich leere Ansicht war zeitlich erklärbar: Das aktuelle Fenster wurde
um 01:54:38 UTC geöffnet, nach der ersten Runde gegen 01:50–01:51 UTC. Der Host
hält abgeschlossene Streams 60 Sekunden, Inhalte höchstens 120 Sekunden; er
spielt beim Öffnen keine Ausgabe aus der Usagehistorie nach.

Eine neue Probe mit Worker-ID `lain-account-test-codex-visible` lieferte HTTP 200,
451 Zeichen und 182 gemeldete Tokens. Der Live-Endpunkt enthielt diese Anfrage.
Der Nutzer bestätigte anschließend ausdrücklich **„Ja, Request und Ausgabe
sichtbar“**. Das ist Nutzerbeobachtung, kein behaupteter unabhängiger Screenshot
seines Fensters. Alte Quellen, Cursorwechsel, abgelaufene Streams und entfernte
Events bleiben im Review getrennt; keine vollständige Liveaufzeichnung behauptet.

## Fehlerbehebung und abgegrenzte Lieferung

Ein anfänglicher Apply scheiterte am als String erzeugten `request-retry`.
Reproduktion in eigener Netzwerknamespace, mit eigenem Journal und Authkopien;
keine Anbieteraufrufe. Ein lesender Import derselben eigenen Importbasis mit
numerischem Wert stellte den Betrieb wieder her. Die Reproduktionskopien mit
Credentials wurden anschließend gelöscht. Keine Runtime-Metadaten wurden
manuell auf Erfolg gesetzt.

Im Repository sind folgende zusätzliche Fixes implementiert und geprüft:

- numerischer YAML-Typ für `request-retry` und erhaltene Startdiagnose;
- pure Transportprüfung vor Apply-Journal und Drain;
- Inspector-Polling nach Darstellungsfehlern und eindeutiger Ablaufhinweis;
- dauerhafter Fehlerabschluss bei fehlenden/fehlerhaften SSE-Abschlüssen;
- Cohere erhält validiertes explizites `none`/`high` trotz SDK-Normalisierung;
  lokale Cohere-Abweisungen erhalten einen Attempt.

Validierung: 101 Applyprüfungen, 75 Transportprüfungen, komplette Go-Paketsuite
und 106 Inspectorprüfungen mit zwölf Bildern auf privatem Xvfb/D-Bus bestanden.
Der anschließende reine UI-Wortlautnachtrag wurde nicht erneut gebaut.
Builds liefen seriell mit RAM-/CPU-Limits. Unnötige eigene `bin`/`obj`-Ausgaben
und temporäre Aufbereitungskopien werden gelöscht, Nachweise bleiben erhalten.

**Diese Sourcefixes sind committed, aber noch nicht installiert.** Die realen
Proben verwendeten weiterhin `d7200d7`. Neuer Core-/App-Build, Installation und
gezielte Liveabnahme der Fixes bleiben getrennte Lieferarbeit. Insbesondere ist
Cohere damit noch nicht als kompletter Codex-/Responses-Zugang qualifiziert;
`store:false` bleibt im bisherigen Cohere-Vertrag ausdrücklich nicht unterstützt.

[Sanitierter Reviewbeleg](../tests/live/reports/all-accounts-20261025.json).
Technische Details: [Streamabschluss und Cohere](providers/stream-outcome-and-cohere-2026-10-25.md).
