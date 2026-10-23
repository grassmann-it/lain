# Umsetzung 23.10.2026

Der Nutzer hat die Umsetzung von A–E autorisiert: .NET 10, SQLite, Avalonia und separater CLIProxyAPI-Core auf dem manifestgeprüften Snapshot 7.3.17-parent-request.3-model-compat.3-startup-readiness.1. Maximal zwölf aktive Agenten einschließlich Integrator, getrennte Worktrees. W0 Verträge/Integration; W1 Core; W2 Qualifikation; W3 Accounts/Betrieb; W4 Datenbank; W5 Modelle; W6 Quota; W7 Client/CLI; W8 Desktop; W9 Usage-UI; W10 Auslieferung; W11 Abnahme.

A ist Freigabegate für produktiven Fachbetrieb. Gerüste und Vertragsfixtures dürfen parallel entstehen. Persistente Dispatchregistrierung, synchron dauerhafte Usage, Replay und transaktionale Cursorbestätigung sind Pflicht. Strikte HTTP-Accountbindung darf nicht auf andere Accounts ausweichen. Unbekannte Mengen sind JSON null.

Eigene Ports 18417/18418 und eigene Ablage. Bestandsproxy, Queue, Secrets und Konfiguration bleiben unverändert. Lesender Import übernimmt API-Konfiguration und Codex-Access-Token ohne Refresh-Token; externen Snapshot weder refreshen noch revoken. Weitere OAuthanbieter bleiben deaktiviert bis qualifiziert.

Codex-Anschluss über eigene lain.config.toml, lokalen Authhelfer und --no-daemon --profile lain. Echte Anbieterproben kurz und harmlos; Last-/Fehlertests ausschließlich synthetisch. Fertigmeldung erst nach dokumentierter Abnahme einschließlich echter Nutzung und unabhängiger Prüfung.
