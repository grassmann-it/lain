# Projektregeln und gezielte Worker v1

Stand: **26.10.2026**. CLI und gebündelter Skill liegen im Quellstand vor;
dieses Dokument beschreibt den v1-Vertrag. Es ist kein Nachweis einer installierten Version oder
bestandener Laufzeittests. Den konkreten Lieferstand und die noch auszufüllenden
Belege führt der [Releasebericht](release-workers-2026-10-26.md).

## Zuschnitt und Grundlage

Der aktuelle Quellumfang umfasst Projektregeln, zweckgebundene Routenwahl und
gezielte, begrenzte Workerbatches mit einem nativen Codex-Client. Der aufrufende
Agent liefert konkrete Aufgaben. Automatische Aufgabenzerlegung, ein
Quota-/Kostenoptimierer und ein globaler Hostscheduler gehören in spätere
Vorhaben. Ein optionaler Classifier oder eigener Approver ist hier nicht Teil
des Lieferumfangs.

Grundlage sind die [Produktziele](product-scope.md), insbesondere die allgemeine,
anbieter-/modellneutrale Zuordnung nach Zweck, sowie die Entscheidungen im lokalen
Architektur-Sparring (`.architecture-sparring/design.md`, Abschnitte
„Skill-Bibliothek und Projektarbeitsweise“ und „Aufruffamilie“, und `roles.md`).
Die lokalen Unterlagen enthalten auch ältere Vorschläge; dieser ausdrücklich
beauftragte v1-Zuschnitt konkretisiert sie, ohne deren gesamte Roadmap zu liefern.
Der [Profil-/Revisionsvertrag](service-contracts.md#implementierter-wirevertrag-v1)
bleibt maßgeblich.

`lain:workers` ist der gewünschte Produktname. Der neue
[Skillquelltext](../skills/lain-workers/SKILL.md) trägt den gültigen eigenständigen
Codex-Namen `lain-workers`; nach tatsächlicher Installation ist `$lain-workers`
der entsprechende Skillname. Das Quellverzeichnis registriert keinen
`lain:…`-Namespace. Skillanweisung und ausführbarer Befehl `lain workers …` sind
unterschiedliche Schnittstellen. Der Skillquelltext wird als Ressource in der
CLI-Assembly gebündelt. Eine Projektinstallation erfolgt optional und ausdrücklich:

```sh
lain workers install-skill --project DIR
```

Optionales `--json` liefert strukturierte Ausgabe. Der lokale Befehl kopiert den
gebündelten Skill nach `DIR/.agents/skills/lain-workers`. Existiert das Ziel
bereits, verweigert er die Installation und erhält lokale Änderungen. Kein
erzwungenes Überschreiben oder Löschen als Updateverfahren. Anschließend den
eigenständigen Skill `$lain-workers` im Client entdecken, gegebenenfalls in einer
neuen Sitzung. Bundling und Projektinstallation registrieren keinen Namespace,
initialisieren keine Regeln und wenden kein Modellprofil an. AP6 dokumentiert
diesen Weg, ohne selbst eine Installation auszuführen.

## Projektregeln

`lain workers init --project DIR --profile ID` richtet
`DIR/.lain/workers.json` ein: `schemaVersion: 1`, gewählte `profileId`,
`maxWorkers: 2`, leere `purposes` und `costPolicy: "configured"`. Das leere
Template erlaubt keine Routen und erteilt keine Berechtigungen. Eine bestehende
Datei wird nicht überschrieben; Init verweigert sie. Zweck-/Routenreferenzen
werden innerhalb des Einrichtungsauftrags ergänzt und danach validiert.
Ein einmaliger Aufgabenwunsch wird nicht still zum Projektstandard.
Eine Einrichtungsanweisung und vorhandene Autorisierung werden genutzt, ohne ein
zusätzliches Freigabeverfahren durch den Skill einzuführen. Das Lesen von Regeln
oder Status allein autorisiert keine Profilanwendung oder Credentialänderung.

Beispiel nach Ergänzung von Zwecken mit zu ersetzenden vorhandenen stabilen IDs.
`2` entspricht hier dem Initdefault; die Zwecke sind im Inittemplate noch leer:

```json
{
  "schemaVersion": 1,
  "profileId": "PROFILE_ID",
  "maxWorkers": 2,
  "purposes": [
    {
      "name": "review",
      "routeIds": ["ROUTE_ID_REVIEW", "ROUTE_ID_SECOND_CHOICE"],
      "contextMode": "native"
    },
    {
      "name": "docs",
      "routeIds": ["ROUTE_ID_DOCS"]
    }
  ],
  "costPolicy": "configured"
}
```

| Feld | Vertrag |
| --- | --- |
| `schemaVersion` | `1` |
| `profileId` | Referenz auf ein vorhandenes Profil |
| `maxWorkers` | Ganzzahl von 1 bis 12; der vorgelegte Batch darf diese Anzahl nicht überschreiten |
| `purposes` | Frei benannte Zwecke mit `name`, explizit geordneten `routeIds`, optionalem `reasoningEffort` und `contextMode` |
| `costPolicy` | `configured` oder `free_only`; ersetzt keine Laufzeitkontrolle |

Die Zwecknamen werden nicht im Programm auf E-Mail, Security oder einzelne
Hersteller festgelegt. Routen referenzieren den zentralen Bestand; es entsteht
keine zweite Anbieter-, Modell- oder Capabilityliste im Skill. Die Reihenfolge
ist eine ausdrückliche Präferenz für die Auflösung vor dem Start, keine
Optimalitäts- oder Kostenlosigkeitsbehauptung und keine Ausführungsfallbackkette.
Optionales `reasoningEffort` muss für die konkrete Route qualifiziert sein;
ein fehlender Wert beweist keinen deaktivierten Reasoningmodus. Unbekannte
Fähigkeiten werden nicht aus einem Modellnamen als unterstützt abgeleitet.

### Kontext pro Zweck

`contextMode` ist optional: fehlend/null oder `"inherit"` übernimmt den bisherigen
Clientkontext. `"native"` eignet sich für abgegrenzte Datei-/Shellaufgaben und
behält native Werkzeuge sowie `AGENTS.md`. Der Runner schaltet für diese Sitzung
Plugins, Apps, orchestrierte MCPs, Cloudskills und automatische Skillanweisungen
samt Katalog ab. Direkte MCP-Server werden über öffentliche native `config/read`-
Abfragen entdeckt und einzeln deaktiviert. Eine zweite Probe prüft den wirksamen
Zustand; echte Ausführung und Probe erhalten dieselben aufgelösten Overrides.
Globale Clientkonfiguration wird nicht geändert. Verwaltete Konflikte und vom
Overrideparser nicht unterstützte MCP-Servernamen werden ausdrücklich abgewiesen.
Die Probe prüft einen Zeitpunkt; sie sperrt andere Schreiber der nativen
Konfiguration zwischen Prüfung und Exec nicht.

Benötigte Arbeitsanweisungen gehören in den konkreten Taskprompt. Aufgaben mit
benötigten Integrationen oder Skills verwenden `inherit`; kein stilles Abschalten
als Fehlerfallback. Der Plan enthält die aufgelöste `contextMode`, das Receipt
die geprüften Kontext-Overrides. Der Modus gilt für den Lain-CLI-Runner, nicht
automatisch für native eingebaute Subagenten oder bestehende VS-Code-Threads.
Er begrenzt keinen Gesprächsverlauf und garantiert weder eine Tokenobergrenze
noch fachliche Antwortqualität. Der tatsächliche native Vergleich steht im
[Kontext-/Providerbericht](provider-tool-context-repair-2026-10-26.md).

## Aufgaben und CLI-Ablauf

Die Aufgaben stehen in einer separaten JSON-Datei. Beispiel für eine lesende
Prüfung und eine ausdrücklich beauftragte Dokumentationsänderung:

```json
{
  "schemaVersion": 1,
  "tasks": [
    {
      "id": "review-contract",
      "purpose": "review",
      "prompt": "Lies den API-Vertrag und berichte konkrete Widersprüche. Keine Dateien ändern."
    },
    {
      "id": "update-guide",
      "purpose": "docs",
      "prompt": "Ergänze die beauftragte Bedienungsanleitung in docs/guide.md und berichte die Änderung.",
      "writePaths": ["docs/guide.md"]
    }
  ]
}
```

Jede Aufgabe hat eine eindeutige `id`, einen vorhandenen `purpose`, einen
konkreten `prompt` und optional `writePaths`. Ohne Schreibpfade ist sie lesend;
explizite Schreibaufträge deklarieren ihre vorgesehenen relativen Pfade.
Schreibbereiche dienen Planung und Integration, **nicht einer ACL pro Pfad**.
Die Berechtigung zur Änderung folgt aus dem Auftrag und den geltenden
Projektregeln, nicht aus dem JSON-Feld.

Der vereinbarte CLI-Vertrag lautet:

```sh
lain workers help
lain workers install-skill --project DIR
lain workers init --project DIR --profile ID
lain workers validate --project DIR
lain workers plan --project DIR --tasks FILE --revision N
lain workers run --project DIR --tasks FILE --revision N --client /absolute/codex --timeout-seconds 900
lain workers status --project DIR
```

`DIR`, `ID`, `FILE` und `N` durch tatsächliche Werte ersetzen. Für `plan` und `run`
ist `--revision N` optional; für einen überprüften Plan den aktuellen numerischen
Stand verwenden. `install-skill` unterstützt optional `--json`. Für `run` einen
expliziten absoluten Pfad zur vollständigen nativen Programmdatei von
**Codex CLI 0.164.0** verwenden; der Runner unterstützt nur diese Version.
`--timeout-seconds` ist optional, Standard **900 je Kindlauf**,
gültig **1–3600**. `lain workers help` beschreibt den Workerbefehlsvertrag; prüfen, ob die
verwendete Version diesen Vertrag bereits bereitstellt. Fehlender Support ist
kein Anlass, eine Installation oder einen Dienstzustand still zu verändern.

`validate` prüft die Projektregeln. `plan` löst die vorgegebenen Aufgaben gegen
den Host auf und liefert den prüfbaren Profil-/Revisions-, Routen-/Modell-,
Reasoning-, Kontext- und Schreibbereichsbezug. Es startet keine Worker, reserviert keine
Quota und nimmt keine Profilanwendung vor. `run` verwendet den begrenzten Runner
für den konkreten Batch. `status` liest den lokalen Laufstand; ein Plan oder
vorhandenes Ergebnis beweist keinen noch laufenden Prozess.

## Host, Client und Kosten

Die Hostauflösung muss aktives Profil und Revision, die benötigten Fähigkeiten
und die bestehende Runtime-Kostenpolicy berücksichtigen. Gewünschte und angewandte
Konfiguration bleiben unterscheidbar. Ein passender Profilname allein reicht
nicht. Auch das separat installierte Codexpaar `lain.config.toml` und
`lain.models.json` muss zum selben Profil und Revisionsstand passen. Ein
vorhandener normaler Codexstandard ersetzt diese Prüfung nicht. Auf Konflikte
folgen erneutes Lesen und Überprüfen, kein blindes Wiederholen mit höherer
Revision und kein automatisches Apply/Install.

Der vereinbarte native Startvertrag verzichtet sowohl für den tatsächlichen
`exec`-Lauf als auch für die Konfigurationsprobe auf ein natives `--profile`:
Der Appserver von Codex 0.164 akzeptiert diesen Parameter nicht. Die Prüfung des
aktiven Lain-Hostprofils, seiner Revision und des separaten installierten
Artefaktbestands bleibt trotzdem erforderlich. Beim Einrichten mit
`lain workers init --profile ID` bezeichnet die Option weiterhin das Lain-Profil,
nicht einen nativen Startparameter.

Aus dem validierten Binding werden vollständige CLI-Overrides abgeleitet, die
für echten Lauf und Probe übereinstimmen: eine frische, je Lauf eindeutige
Provider-ID samt Provider-/Authkonfiguration und Katalog sowie das gewählte
Modell und dessen Effort. Gespeicherte Benutzerkonfiguration wird dadurch nicht
umgeschrieben. Ein geerbter Standardprovider oder ein Profilname allein belegt
diese effektive Bindung nicht.

Vor einem Turn müssen die nativen Appserverabfragen `config/read` und
`configRequirements/read` die wirksame Auswahl von Provider, Katalog, Auth,
Modell/Effort, Sandbox und allen drei unten genannten Delegationsflags prüfen.
Diese Konfigurationsgates laufen nacheinander vor den parallelen Workerturns:
Codex 0.164 initialisiert beim Start gemeinsam genutzten Zustand und Skills,
was bei gleichzeitigen ersten Appserverstarts kollidieren kann. Sie führen
keinen Turn aus; Codex darf dabei seinen normalen lokalen Zustand initialisieren.
Widersprechende verwaltete
Anforderungen werden abgewiesen; sie werden weder abgeschwächt noch durch eine
stille Ausführung mit anderen Defaults umgangen. Das beschreibt den
Integrationsvertrag, keinen bereits abgeschlossenen Implementierungs- oder
Qualifikationsnachweis.

Die geordnete Routenauswahl geschieht vor der Ausführung; die gewählte Route
bleibt gepinnt. Der Runner startet eine fehlgeschlagene Aufgabe nicht erneut
und wechselt ihre Route nicht. Er setzt die Request-/Streamretry-Einstellungen
des Clients auf null. Daraus folgt keine Aussage über sämtliche internen
Codex- oder Providerwiederholungen. Der Workerpfad darf vorhandene Kostenkontrollen
nicht umgehen; damit wird keine allgemeine Neuregelung anderer Proxyfunktionen
behauptet.

`configured` nutzt die bestehende Policy und erteilt keine zusätzlichen
Ausgabenrechte. `free_only` braucht wirksame Runtimekontrollen am tatsächlichen
Dispatch. Ein Dateiflag, „free“ im Namen, eine Preisliste oder ein Quotasnapshot
belegt diese Wirkung nicht. Nicht belegte Preise, Restmengen und tatsächliche
Kosten bleiben unbekannt; Aboquota, Guthaben und kostenlose Nutzung werden nicht
gleichgesetzt. Ein Plan ist weder Credential noch Budgetreservierung oder
harte Ausgabengrenze.

## Begrenzter nativer Runner

| Grenze | v1-Vertrag und Reichweite |
| --- | --- |
| Batchgröße | Anzahl der Tasks höchstens `maxWorkers`, dessen gültiger Bereich 1–12 ist; keine Zusage über alle Agents auf dem Rechner |
| Gleichzeitige Läufe | CLI-Laufsperre je Projekt; kein globaler Hostscheduler oder projektübergreifendes Quotalimit |
| Laufzeit | `--timeout-seconds` begrenzt den einzelnen Kindlauf; Terminierung und Ergebniszustand müssen am tatsächlichen Runner belegt werden |
| Lesende Aufgaben | Nativer Codexstart mit `--sandbox read-only` |
| Schreibende Aufgaben | Nativer Codexstart mit `--worktree --sandbox workspace-write`, isolierte native Worktrees; `writePaths` bleibt eine Bereichsdeklaration |
| Unterdelegation | Kindprozesse setzen `agents.enabled=false`, den Legacywert `features.multi_agent=false` und `features.multi_agent_v2=false`; kein Start verschachtelter Worker über einen anderen Weg |
| Fehler | Kein erneuter Aufgabenstart durch den Runner, Route bleibt gepinnt, Clientrequest-/Streamretries auf null; keine Abschwächung von Approval- oder Sandboxregeln |
| Integration | Keine automatische Zusammenführung; Worktree, Diff und Ergebnis zur bewussten Integration erhalten |

Schreibquellen müssen bereits in der wirksamen nativen Codexkonfiguration als
`trusted` eingetragen sein. Fehlendes oder ausdrücklich verweigertes Vertrauen
wird mit `native_config_source_trust_required` abgewiesen. Der Runner setzt
keine Vertrauensflags: Native `--worktree`-Starts könnten sonst ein bisher
uneingetragenes Projekt selbständig als vertrauenswürdig speichern.

Schreibaufträge laufen in nativen isolierten Worktrees vom sauberen **HEAD** des
Quellrepositorys. Sobald ein Batch Schreibaufgaben enthält, muss das gesamte
Repository frei von vorgemerkten, nicht vorgemerkten und unversionierten
Änderungen sein. Das gilt auch außerhalb eines gewählten Projektunterordners und
der deklarierten `writePaths`. Der Runner erfasst den Quellcommit und prüft
Sauberkeit, Repositoryidentität und denselben HEAD unmittelbar vor jedem
Schreibstart erneut. Diese Prüfungen sperren Git nicht gegen andere Schreiber
zwischen Prüfung und Start; sie sind keine atomare Quellsicherung.

Ignorierte Dateien werden nicht in den nativen Worktree kopiert. Es gibt weder
eine Übernahme offener Änderungen noch einen WIP-Snapshot; benötigte ignorierte
Dateien sind dort nicht automatisch verfügbar. Nutzeränderungen nicht allein zur
Erfüllung der Startbedingung committen, stashen, bereinigen oder verwerfen.
Ein blockierter Schreibbatch wird mit dem tatsächlichen Zustand berichtet.
Native Worktrees lösen auch nicht die fachliche Integration voneinander
abhängiger Änderungen. Ergebnisprüfung und eine Zusammenführung innerhalb des
beauftragten Umfangs bleiben beim aufrufenden Agenten bzw. Integrator.

Das Batchreceipt enthält den aus Git erfassten Quellcommit als `SourceHead`.
`VerifiedWorktree` eines Tasks stammt ausschließlich aus einer eindeutig
registrierten Git-Worktreeidentität mit passender nativer Threadzuordnung. Pfad,
HEAD und Detachedzustand werden aus Git und den nativen Registrierungsmetadaten
gewonnen. Modelltext, behauptete Pfade/Branches oder erfolgreicher Prozess-/Turn-
Abschluss sind dafür kein Nachweis. Fehlen eindeutige, gültige Metadaten, bleibt
die Worktreeidentität unbekannt/null; ein nicht erfasster Quell-HEAD bleibt
ebenfalls unbekannt. Ein rein lesender Batch erfasst keinen Schreibquellcommit.

Den tatsächlichen Qualifikationsstand einschließlich nativer Konfigurationsgates
und Helperkompatibilität führt der
[Releasebericht mit den Integrationsbelegen](release-workers-2026-10-26.md#tatsächlich-vorliegende-nachweise).

Ein optionaler direkter nativer Subagentenweg kommt nur bei qualifizierten
Fähigkeiten und belegter effektiver Routenwahl infrage; er übernimmt nicht
automatisch die Batch-, Timeout- und Sperrwirkung des CLI-Runners.

## Lokaler Zustand und Nachweise

Runtimezustand liegt unter `LocalDiscovery.StateDirectory/workers/<project hash>`.
`LocalDiscovery` berücksichtigt `LAIN_HOME`; ohne diese Auswahl ist der Pfad
`LocalApplicationData/lain/workers/<project hash>`. `LocalApplicationData`
bezeichnet das vom Betriebssystem aufgelöste Anwendungsdatenverzeichnis. Die
gewählte Runtimeablage bleibt privat außerhalb des Checkouts.
Diese Ablage ist kein globaler Scheduler und keine zweite Credentialverwaltung.
Projektregeln in `.lain/workers.json` sind bewusst versionierbar. Taskdateien
und Prompts bleiben privat und gemäß Projektkonvention ignoriert; keine Tokens
oder privaten Prompts in Releasebelege übernehmen. Persönliche Auth,
Clientkonfiguration, bestehende Accounts und Dienste werden erhalten.

Status und Ergebnisse mit ihrem tatsächlichen Stand berichten: abgeschlossene,
fehlgeschlagene oder unterbrochene Aufgaben nicht gleichsetzen, Teilergebnisse
nicht als Gesamterfolg ausgeben. Nach einer Unterbrechung zuerst Status prüfen;
keinen automatischen Wiederholungs- oder Cleanupversuch daraus ableiten.
Erhaltene Worktrees werden bewusst integriert bzw. später bewusst entfernt.
`succeeded` bezeichnet einen erfolgreich abgeschlossenen Prozess-/Turnablauf.
Das separate Taskfeld `ResultReviewState` bleibt `"not_reviewed"`: Der Runner
prüft nicht die fachliche Richtigkeit einer Antwort oder Änderung. Diese Prüfung
erfolgt anhand der Aufgabenanforderungen durch den aufrufenden Agenten/Integrator.
Die Ergebnisfelder `RequestedModel` und `ExpectedAccountIds` beschreiben die
beabsichtigte Routenwahl. Weder Prozess-/Turnerfolg noch eine erwartete Accountliste
belegen den tatsächlich verwendeten Upstreamaccount. Dieser bleibt unbekannt,
solange kein passender Join mit tatsächlich beobachteter History bzw. dem
Inspector vorliegt. Usage und Kosten nur mit tatsächlich beobachtetem Bezug zuordnen. Skilltext
allein erzwingt weder Workerzahl, Quota noch Kosten. Geplante Prüfungen und
tatsächliche Ergebnisse bleiben im [Releasebericht](release-workers-2026-10-26.md)
getrennt.
