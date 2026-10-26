# Provider-, Tool- und Kontextnachschärfung

Stand **26.10.2026**. Folgeauftrag zur [ersten Modellprüfung](model-live-qualification-2026-10-26.md):
Kimi mit auto, Beam mit passendem Toolumfang, Cohere lauffähig machen und
unbrauchbare Antworten trotz HTTP 200 untersuchen. Auf die Kontextfrage wurde
der begrenzte Workerumfang um eine explizite Kontextwahl je Zweck ergänzt.
**Neue Host-/CLI-/Coreänderungen sind nicht produktiv installiert.**

## Ergebnis

| Route/Umfang | Tatsächlicher Nachweis | Grenze |
| --- | --- | --- |
| Kimi `k3`, `k3-256k`, `kimi-for-coding`, `kimi-for-coding-highspeed` | Alle vier mit auto, Datei-/Toolfortsetzung und fachlichen Grenzfällen bestanden | Kleiner synthetischer Task, sieben Funktionstools; keine Langkontextqualifikation |
| Beam `cmd-beam-free` | Auto-Toolablauf und fachliche Aufgabe bestanden | Separater historischer Grenztest: 128 Top-Leveldefinitionen akzeptiert, 129 abgelehnt |
| Z.ai `glm-5.3`, `glm-5.3-flash` | Beide mit auto und erlaubten neutralen Toolmetadaten bestanden | Vorheriger Testabbruch durch zu strenge Fixtureargumente ist kein Providerfehler |
| Cohere `command-a-plus-05-2026` | Neuer Corekandidat: zwei echte Streamingrequests, Toolaufruf und Fortsetzung funktionieren | Fachprüfung fehlgeschlagen: vorgeschlagener Guard verwirft weder null Mengen noch negative Mengen |
| Expliziter Worker-Modus `native` | Echter isolierter Codex-/Helper-/C#-Runnerablauf bestanden, AGENTS erhalten | Gilt für den CLI-Workerpfad; keine automatische Wirkung auf bestehende IDEthreads oder eingebaute Subagenten |

Die zusätzlichen Liveprüfungen dieser Runde umfassen **18 Clientaufrufe**:
12 im ersten Auto-/Qualitätslauf, vier im gezielten Z.ai-Metadatenfall und zwei
für den Cohere-Kandidaten. Alle 18 wurden unabhängig über die öffentliche
`lain attempts`-Schnittstelle mit Conversation/Turn, Account, Provider, Alias und
Profilroute verbunden; bei den Auto-Läufen zusätzlich mit Response-IDs.
Es wurden genau 18 passende erfolgreiche Attempts beobachtet. Clientzählung
allein wäre kein Beleg für sämtliche internen Provideraufrufe.
Der historische 59-Alias-Lauf und die fünf Grenzdiagnostikaufrufe bleiben
unverändert, ebenso ihre Fehler. Kein unverändertes Wiederholen bis zum Erfolg.

Sanitisierte Belege: [maschinelle Zusammenfassung](../tests/live/reports/provider-tools-context-20261026.json).
Private vollständige Pläne, ausgeführte Sourcekopien und öffentliche Attempts
liegen unter `~/.cache/lain-provider-tools-repair-20261026/`.

## Was Kontext belegt

Toolschemas, Skill-Katalog und geladene Anweisungen beanspruchen Kontext neben
Aufgabe und Verlauf. Frühere öffentliche Z.ai-Attempts der gemeldeten Recherche
enthielten 110.908/137.428 bzw. 111.789/138.159 Eingabetokens bei HTTP 200.
Die kurzen erfolgreichen Folgetests liegen bei rund 5.000–6.000 Eingabetokens.
Sie belegen kleine funktionierende Aufgaben; sie isolieren nicht die alleinige
Ursache der früheren unbrauchbaren Antworten. Keine dieser großen fremden
Recherceanfragen wird als eigener Testaufruf gezählt.

Der neue echte native Vergleich verwendet ein eigenes MCP mit 160 synthetischen
Werkzeugen und je einen Skill-/Projektregelsentinel:

| Übertragene Tooldefinitionen | `inherit` | `native` |
| --- | ---: | ---: |
| Top-Leveleinträge | 11 | 7 |
| Aufrufbare Definitionen einschließlich Namespacemitgliedern | 170 | 7 |
| Serialisierte Toolbytes | 118.760 | 7.562 |
| Projektregeln aus AGENTS | enthalten | enthalten |
| Optionaler Skill-Katalog | enthalten | ausgelassen |

Das sind rund **94 % weniger Toolbytes**, keine gemessene Tokenersparnis.
Codex gruppiert die 160 MCP-Werkzeuge in einen Namespace; elf Top-Leveleinträge
überschreiten die separat beobachtete Beamgrenze von 128 nicht. Der Vergleich
belegt Kontextverkleinerung und einen kleinen Toolumfang, keine allgemeine
Namespace-Kompatibilität von Beam und keinen Test sämtlicher persönlicher Plugins.

Der erste native Versuch scheiterte ausschließlich an der falschen
Harnessannahme, 160 MCP-Mitglieder müssten 160 Top-Leveleinträge sein. Seine
unveränderten Fehlerbelege bleiben in `native-context-r1`. Der korrigierte
Zählvertrag trennt beide Größen; `native-context-r2` ist bestanden.

## Implementierte Änderungen

### Expliziter kompakter Worker

In `.lain/workers.json` ist pro Zweck optional möglich:

```json
{
  "name": "review",
  "routeIds": ["EXISTING_ROUTE_ID"],
  "contextMode": "native"
}
```

Fehlend/null oder `inherit` erhält den bisherigen Kontext. `native` behält
Datei-/Shellwerkzeuge und AGENTS; automatische Skillanweisungen und Katalog,
Plugins, Apps, Cloudskills und orchestrierte MCPs werden mit Sitzungsflags
abgeschaltet. Direkte MCP-Namen werden turnfrei über `config/read` entdeckt,
einzeln deaktiviert und abschließend wirksam geprüft. Kein leeres MCP-Objekt
als vermeintliche Löschung: TOML-Tabellen werden rekursiv zusammengeführt.
Die echte Ausführung verwendet exakt dieselben geprüften Overrides.
Unvereinbare verwaltete Vorgaben oder vom CLI-Overrideparser nicht unterstützte
MCP-Namen stoppen die Ausführung ausdrücklich. Globale Konfiguration bleibt erhalten.
Die Konfigurationsprobe ist eine Zeitpunktprüfung; gleichzeitige Änderungen
zwischen Probe und Exec werden dadurch nicht atomar ausgeschlossen.

Der echte native Nachweis umfasst auch Shellausführung, verweigertes Schreiben
im Read-only-Worker, erfolgreichen Schreibworker im registrierten Detached-
Worktree, Toolfortsetzungen und unveränderte Basiskonfiguration. Er verwendet
isolierte Netzwerk-/PID-Namespaces und private Mounts an unveränderten
HOME-/CODEX_HOME-Pfaden; sämtliche Anbieterantworten sind synthetisch.
Der [Workervertrag](workers-v1.md) und der gebündelte
[Skill](../skills/lain-workers/SKILL.md) beschreiben die Kontextwahl.

### Kimi und Z.ai

Die erfassten nativen Codexrequests verwenden bereits `tool_choice:auto`.
Eine globale Umschreibung von forced/none zu auto wäre unnötig und würde
Aufruferabsichten verändern. Neue Testpläne pinnen auto auf beiden Schritten
und eine strukturierte fachliche Bewertung. Ein beschränkter AST-Interpreter
prüft den vorgeschlagenen Mengen-Guard auf positive Ganzzahlen, große Zahlen,
null, negative Zahlen, Boolesche Werte, Bruchzahlen, integrale Floats, Strings,
null-Werte und Container. Beliebiger Modellcode wird nicht ausgeführt.

Der gezielte Z.ai-Fall erlaubt zusätzlich neutrale native Execmetadaten;
Command/Workdir und die ausgeführte feste `/usr/bin/cat`-Fixture bleiben exakt.
Keine vom Modell erzeugten Shellbefehle werden ausgeführt. Der erste strengere
Fall bleibt als Testabbruch dokumentiert und wird nicht rückwirkend umgewertet.
Die beiden unterschiedlichen Requestverträge und Sourcepins stehen im Bericht.

### Cohere

Die Responses-to-Chat-Brücke akzeptiert ausschließlich die nachgewiesenen
defaultgleichen Controls `tool_choice:auto` und `parallel_tool_calls:true` und
überträgt sie durch Auslassen dieser Felder auf Cohere Chat. `none`, `required`,
erzwungene Tools und `parallel_tool_calls:false` bleiben explizit abgewiesen;
keine stillschweigende Abschwächung dieser Einschränkungen. Doppelte Controls
und nachträgliche unzulässige Wirefelder werden weiterhin geprüft.

Der neue Kandidat wurde gegen die vorhandene Cohere-Route getestet, über einen
eigenen Loopbackforwarder mit lokalem Inferenztoken nur im Speicher. Der
Kandidat erhielt einen synthetischen Schlüssel. Kein Lesen von Providerstores,
kein Produktivneustart und keine Account-/Profiländerung. Der finale Chatwire
enthielt nur `messages`, `model`, `reasoning_effort`, `stream`, `tools`; kein
erfundenes Outputbudget. Usage bleibt für diese Streamingantworten unbekannt.

Technisch ist die Brücke repariert. Die Modellantwort besteht die fachliche
Abnahme **nicht**. `succeeded` eines Workerturns bedeutet deshalb nur
Ausführungsabschluss; das neue `ResultReviewState` bleibt `not_reviewed`.
Der aufrufende Agent muss Antwort/Diff anhand der Aufgabenanforderungen prüfen.
Es gibt keinen automatischen Retry oder Modellwechsel aus einem Qualitätsfehler.

Herstellergrundlagen der engen Controlabbildung:
[Cohere Compatibility API](https://docs.cohere.com/docs/compatibility-api#unsupported-parameters),
[Chat](https://docs.cohere.com/reference/chat),
[parallele Toolaufrufe](https://docs.cohere.com/v2/docs/tool-use-usage-patterns#parallel-tool-calling).
Für Codex ist `skills.max_context_tokens` ein Katalogbudget, keine allgemeine
Gesprächsgrenze: [Configreferenz](https://developers.openai.com/codex/config-reference/).

## Prüfung und verbleibende Releasearbeit

### Nachprüfung: Reasoning bei Automatisch

Die anschließende Nutzerbeobachtung ist am installierten Stand bestätigt:
Sowohl der durch `config.toml` referenzierte Katalog als auch der separate
`lain.config.toml`-Katalog enthalten bei allen 16 Auto-Aliasen
`supported_reasoning_levels: []` und `default_reasoning_level: "none"`.
Auch die Codex-Autogruppe ist betroffen. Das ist keine Aussage, dass sämtliche
Zielmodelle `none` unterstützen, und kein Nachweis des tatsächlich gesendeten
Efforts in einem konkreten Turn.

Im noch nicht installierten Quellstand berechnet `CodexProfileAdapter.RouterReasoning`
bereits die Schnittmenge der Reasoningstufen der tatsächlich kompilierten
Routermitglieder. Bei unbekannten Mitgliedern bleibt die Auswahl unqualifiziert
und der Default null; unbekannt wird nicht mehr als `none` ausgegeben.
`ReasoningExportCases` prüft eine bekannte Astra/Luna-Gruppe und eine Gruppe mit
unqualifiziertem Mitglied. Für die Lieferung müssen die aktualisierte
Modellqualifikation und der Routerexport gemeinsam paketiert, installiert und
die Kataloge über den regulären Clientexport erneuert werden. Den installierten
Katalog nicht isoliert um erfundene Optionen ergänzen. Native Auswahl und
tatsächliche Effortweitergabe durch den Router bleiben getrennte Abnahmepunkte.
Die erneute fokussierte Gateway-Exportsuite bestand mit **98 Assertions**, ohne
installierte Konfiguration oder Providerzugriffe.

### Ausgeführte Prüfungen

- Worker-Host: **299 Offlinechecks**, Worker-CLI: **224 Assertions** bestanden.
- Liveharness: **46 Offlinechecks**, davon 28 für die neuen Modell-/Kandidatfälle;
  native Outer-Harness: **6 Checks** bestanden.
- Fokussierte Go-Suite `TestCohere|TestOtherProviderStreamingUsage` bestanden;
  frischer source-manifest-gepinnter Corekandidat gebaut und live geprüft.
- Skillvalidator bestanden. Tatsächlicher nativer Kontextablauf separat bestanden;
  er ist kein Beleg eines bereits ausgelieferten Gesamtpakets.
- Unabhängiges statisches Review der Worker-Kontextbindung ohne konkrete Befunde;
  kein Ersatz für die getrennten tatsächlichen Laufzeitbelege.

Corekandidat SHA-256:
`f737b8726b845796f38bad6f9a881bddb96e9598ea8908f9849559d91a2164cd`.
Prepared-Source-/Manifestpin:
`09720a19fe45c465f4e9267845cdc6d79849d531564a0bdc6ab6dcd2db2d1676`.
Native Binary-/Helper-/Receiverpins stehen vollständig in der maschinellen
Zusammenfassung; sie bezeichnen exakt diese isolierte Abnahme.

Für den Release bleiben der gemeinsame festgelegte Quell-/Paketstand und dessen
native Paketabnahme aus dem [Releasebericht](release-workers-2026-10-26.md).
Weitere fehlerhafte Modelle der ersten Matrix sowie Cohere-Qualität sind offen;
kleine Folgefälle ersetzen keine vollständige neue Matrix. Langkontext und die
vollständige tatsächliche VS-Code-/Pluginoberfläche benötigen eigene Nachweise.
Ein neues kompaktes Workerfenster entfernt keinen bereits bestehenden langen
Verlauf aus einer anderen Sitzung. Automatische Zerlegung, Quotaoptimierung und
eine allgemeine dynamische Toolauswahl bleiben nachgelagert.
