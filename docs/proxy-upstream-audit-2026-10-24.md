# CLIProxyAPI: Anpassungen und Abstand zum Upstream

Lesende Gegenprüfung auf lain `416fe13`, offizielle Releases und Quellarchive
geprüft am **24.10.2026**. Keine Builds, Dienste, Zugangsdaten oder bestehenden
Proxyqueues wurden für diese Untersuchung verwendet. CLIProxyAPI bleibt
Drittanbietersoftware; der folgende Umfang ist keine Proxy-Neuentwicklung.

## Herkunft und Umfang

| Vergleich | Verändert | Hinzugefügt | Weggelassen |
| --- | ---: | ---: | ---: |
| Offizielles v7.3.17 (`9bdde54…`) → übernommene lokale Basis | 19 | 23 | 1 |
| Lokale Basis → lain-Patchstand 2 | 15 | 28 | 215 |
| Offizielles v7.3.17 → heutiger lain-Baum | 32 | 51 | 216 |

1.393 offizielle Dateien wurden unverändert übernommen. Zwei geänderte Dateien
überlappen zwischen den Patchstufen, weshalb die Zeilen nicht einfach addiert
werden dürfen. Alle 1.476 aktuellen Dateien stimmen mit
[source-manifest.json](../proxy/source-manifest.json) überein; der Baselinehash
stimmt ebenfalls. Der offizielle Tag v7.3.17 zeigt auf den manifestierten Commit.

Die 215 nach der lokalen Basis weggelassenen Dateien sind 164 Beispiele bzw.
Plugin-Demodateien, 28 Assets, zehn Dokumentationsdateien, neun GitHub-/CI-Dateien
und vier Repositorydateien. **Keine produktiven Provider-, Executor- oder
Übersetzungsdateien fehlen durch dieses Weglassen.** Gegen das offizielle Archiv
kommt die bereits zuvor entfernte `auths/.gitkeep` hinzu.

Bereits die übernommene Basis enthielt ParentRequest-/Versuchszuordnung,
Abbruchbeobachtung mit unbekannter Usage, Gemini-/Muse-/MiMo-Schemakompatibilität
und verbesserte Startbereitschaft. Diese Änderungen sind keine neu von lain
entwickelten Funktionen und implizieren nicht, dass der Nutzer sie selbst
geschrieben hätte. Siehe [Übernahme](proxy-backend.md) und
[Core-Vertrag](backend-core.md).

## Was heute vorhanden, angepasst oder nicht erreichbar ist

| Funktion | Tatsächlicher Zustand in lain |
| --- | --- |
| Chat Completions, Responses, Responses Compact, Anthropic Messages | Exponiert unter lain-Routen-/Accountbindung; originale Executor- und Übersetzungsarbeit bleibt erhalten. |
| WebSocket Responses, Realtime/Audio-Live | Im übernommenen Core vorhanden, durch die lain-HTTP-Allowlist nicht exponiert. |
| Bilder, Video, Legacy Completions, Claude Count Tokens, native Gemini-Endpunkte | Im Core vorhanden, über den lain-Eingang nicht exponiert. |
| Anbieter-Executoren | Nicht pauschal entfernt. Lains Account-/Protokollangebot ist enger als der vorhandene Executorbestand. |
| OAuth | Eigene Codex-Anmeldung und externe Codex-Access-Snapshots. Andere originale Loginabläufe sind über den lain-Launcher nicht angeboten. Externe Snapshots werden nicht refreshed/revoked. |
| Management-API/Weboberfläche | Originales Management/UI deaktiviert bzw. nicht exponiert; eigene begrenzte `/lain/*`-Schnittstellen. |
| Konfigurations-Hotreload | Durch festen Startup-Snapshot ersetzt. Profil-Apply stoppt/startet den eigenen Proxy. |
| Plugins, Discovery, pprof, Requestlogs | Implementierungen vorhanden, im lain-Launcher ausdrücklich deaktiviert. |
| Accountwahl, Pools, Retries | Auf gebundene Credentials eingeschränkt; strikte Bindung darf beim Retry nicht still wechseln. Pools bleiben ausdrücklich konfigurierte Mitgliedschaften. |
| Usage und Ausfälle | Dauerhaftes Start-/Endjournal, stabile lain-IDs und wiederholbares Lesen statt entnehmender Queue. Journalfehler sperren weiteren Dispatch. |
| Providerprotokolle und Kostenregeln | Zusätzliche Transportwahl, Provider-Sessionanforderungen und Free-only-Prüfung am tatsächlichen ausgehenden Request. |
| Requestgröße | Am lain-Eingang derzeit auf 32 MiB begrenzt. |

Zentrale Quellbelege:

- [Originale API-Routen](../proxy/core/internal/api/server_routes.go) gegenüber
  [lain-Allowlist und Bindungen](../proxy/core/internal/lain/http.go).
- [Launcher](../proxy/core/cmd/lain-proxy/main.go), insbesondere deaktivierte
  Management-/Plugin-/Discovery-/Log-Funktionen und Refresh-Verhalten.
- [Startup-Snapshot](../proxy/core/sdk/cliproxy/lain_snapshot.go),
  [synchrone Usageübernahme](../proxy/core/sdk/cliproxy/usage/manager.go),
  [Dispatchhooks](../proxy/core/sdk/cliproxy/auth/lain_dispatch.go).
- [Kostenprüfung am Sendepunkt](../proxy/core/internal/runtime/executor/lain_free_only_transport.go).

## Neuerer offizieller Upstream

Neuester geprüfter Release: **v8.0.41**, veröffentlicht am 23.10.2026, 18:25 UTC.
Zwischen v7.3.17 und diesem Stand liegen 45 veröffentlichte Releases. Die frühere
Angabe v8.0.40 war ein älterer Recherchezeitpunkt. Der ursprüngliche lain-Auftrag
hatte v7.3.17 ausdrücklich gepinnt und ein v8-Upgrade zurückgestellt.

| Neuerung | Bedeutung für lain |
| --- | --- |
| Neuer v8-Konfigurations-/Managementvertrag; später ETag/If-Match | Ein Upgrade ist keine austauschbare Binaryaktualisierung. Lain hat zusätzlich eigene Revisionen. |
| Claude Responses Compaction, v8.0.22 | Konkreter Funktionsabstand: unser alter ClaudeExecutor beantwortet `responses/compact` mit „not supported“/501. |
| Claude/Codex `tool_search` und Deferred Tools, v8.0.24; weitere Gemini-Unterstützung v8.0.28 | Neue Übersetzungs-/Capabilitypfade fehlen im gepinnten Stand. |
| Sign in with ChatGPT/SIWC, v8.0.25 | Neue Upstreamfunktion; nicht mit lains vorhandenem Codex-OAuth-Login gleichsetzen. |
| Grok TTS `/v1/audio/speech`, v8.0.18 | Im alten Core nicht vorhanden; lains aktuelle Allowlist würde es auch nach einem bloßen Coreupdate blockieren. |
| Session-Affinity-Rebind, konfigurierbare Bodygrenze, Keepalive vor erster Ausgabe | Zusätzliche Betriebsoptionen neuerer Releases. |
| Native Vertex Interactions und weitere Thinking-/Signaturpfade | Neuere Protokollvarianten; ein vorhandener Gemini-Executor beweist diese Fähigkeiten nicht. |
| Usage-/Übersetzungsfixes in v8.0.36, .39 und .41 | Unter anderem Reasoningtokens, Service-Tier und Gemini/Vertex-Finishgründe. Eigene Usage-Patches ersetzen diese Fixes nicht pauschal. |
| Cluster-Probes und Rebalance | Versionsfortschritt mit geringerem unmittelbarem Nutzen für den lokalen Einzelhost. |

Diese Unterschiede beweisen keine Fehlfunktion jeder heutigen Anfrage. Eine
pauschale Liste „fehlender Modelle“ wäre ebenfalls unzuverlässig: Modelle können
zusätzlich konfiguriert und Kataloge dynamisch aktualisiert werden.

Offizielle Quellen: [v7.3.17](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v7.3.17),
[v8.0.0](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.0),
[v8.0.18](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.18),
[v8.0.22](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.22),
[v8.0.24](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.24),
[v8.0.25](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.25),
[v8.0.31](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.31),
[v8.0.36](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.36),
[v8.0.39](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.39),
[v8.0.41](https://github.com/router-for-me/CLIProxyAPI/releases/tag/v8.0.41).

## Sinnvolle Entkopplung – Vorschlag

Ein separates eigenes Core-Repository ist nicht zwingend nötig. Der vollständige
Fremdquellbaum kann durch einen gepinnten offiziellen Quellarchiv-Download mit
Prüfsumme und nachvollziehbare Patches ersetzt werden. Launcher, Adapter,
Qualifikation und Lizenznachweise bleiben erkennbar bei lain.

Zuerst sollte ausschließlich die Quell-/Buildorganisation geändert werden:
offizielles v7.3.17 pinnen, Basis-/lain-Patches konservieren, temporären Buildbaum
rekonstruieren und gegen das bisherige Dateimanifest vergleichen. Erst nach
diesem Nachweis den vendorten Baum entfernen. Für getrennte Basispatches sind
bei den zwei überlappenden Dateien `helps/usage_helpers.go` und
`usage/manager.go` die ursprünglichen Zwischenbytes aus der gesicherten
Basisquelle erforderlich; der Gesamtunterschied zum offiziellen Archiv lässt
sich bereits eindeutig abbilden. Neu gebaute Binarybytes werden separat
qualifiziert. Ein Versionsupgrade bleibt ein eigenes Vorhaben.

Ein unverändertes Standardbinary unterstützt die heutigen lain-Flags und
`/lain/*`-Verträge nicht. Die native entnehmende Usagequeue ersetzt außerdem
nicht die heutige dauerhafte Replay-Zusage. Welche Patches entfallen können und
welche bislang gesperrten Upstreamfunktionen exponiert werden sollen, ist deshalb
eine fachliche Folgeentscheidung, kein bloßer Austausch des Downloadlinks.
