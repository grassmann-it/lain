# Codex-Modellgruppen in VS Code

Lokale Lainanpassung für `openai.chatgpt` 26.1021.22027, Linux x64;
die lokale Paketversion ist 26.1021.22028. Keine öffentliche Paketverteilung.

Der bestehende Codexpicker erhält innerhalb seiner Modellauswahl Gruppen und
Modelle mit Zurück-Navigation. Die Power-/Reasoning-Steuerung sowie Modellwahl
und Threadupdates bleiben bei den nativen Codex-Callbacks. Gruppen, Reihenfolge,
Mehrfachmitgliedschaften und Router stammen aus `lain_model_picker` im wirksamen
Clientkatalog. „Alle“ enthält die nativ sichtbaren Optionen jeweils einmal.

`lain-groups-host.cjs` prüft den lokalen Appserverkontext, alle Workspacebindungen,
Katalogdateien und den vollständigen nativen Modellbestand. Er liefert nur
validierte Gruppendaten. Abweichende Provider/Profile, Remoteverbindungen,
eigene Executables und nicht belegbare Bindungen behalten die native Darstellung.
Die Oberfläche prüft beim Öffnen und während des geöffneten Menüs erneut;
abgebrochene oder veraltete Antworten dürfen alte Gruppen nicht übernehmen.

Die Patchquelle liegt in `scripts/clients/vscode_groups_patch.py`. Exakte
Eingabehashes und einmalige Strukturanker begrenzen die Transformationen.
Vollständige fremde Bundles werden nur im privaten Kandidaten gehalten.
Die gebündelte Alpha-Binary und ihr Helper werden nicht ersetzt.

[Wartung und Rücknahme](maintenance.md),
[Umsetzungsplan](../../docs/vscode-model-groups-plan-2026-10-26.md),
[lokaler Lieferstand](../../docs/vscode-model-groups-delivery-2026-10-26.md).

Die Reasoninganzeige folgt dem Katalog. Eine leere Liste kann keine auswählbaren
Werte anzeigen. Für den konkret gemeldeten alten Astraexport gibt es die
gesicherte Reparatur `scripts/clients/repair_codex_reasoning.py`: native Werte
`low` bis `max`, Standard `low`, neue unveränderliche Kataloggeneration und
bedingte Rücknahme. Sie führt weder Profilapply noch Dienstneustart aus.
