# Öffentliches Repository, 26.10.2026

[grassmann-it/lain](https://github.com/grassmann-it/lain) ist öffentlich.
Visibility per GitHub-CLI und anonymem API-GET mit HTTP200 bestätigt.
Source-/Recherche-/CI-Stand `8708650a1700d71a2cd7b3f4b95b3491a1cd479d`
wurde regulär per Fast-forward auf main hochgeladen. Die separate lokale
Main-Arbeitskopie wurde nicht committiert, zurückgesetzt oder gestasht.

Die vorangegangenen Netz- und OAuthberechtigungsfehler sind behoben.
Der CLI-Zugang tkoschka besitzt den erforderlichen workflow-Scope und Repo-
Adminrechte. Die Schreibverweigerung des separaten SSH-Zugangs bleibt ein
historischer Fehlversuch; keine Authentifizierungsdaten werden veröffentlicht.

## Erster Plattformworkflow

[Run40416133877](https://github.com/grassmann-it/lain/actions/runs/40416133877)
besteht auf ubuntu-latest, macos-latest und windows-latest. Alle drei Jobs
haben die synthetischen Contracttests und die Checkout-Erhaltungsprüfung
bestanden. Bashsyntax wurde unter Linux/macOS, PowerShellsyntax unter Windows
geprüft; die jeweils fremde Shellprüfung wurde erwartungsgemäß nicht ausgeführt.

Das ist keine Abnahme echter nativer Pakete, des Desktops, Dienstlebenszyklus
oder der Windows-Coreportierung. Einzelheiten im
[Workflowvertrag](platform-installer-ci.md).

## Verbleibende Releasegrenzen

Kein 1.0-Tag, GitHub-Release oder Binärdownload wurde veröffentlicht.
Die privaten r4-Archive bleiben an Produktbasis882bdad gebunden.
Das lokale Dienstupdate wartet auf das Ende der ausdrücklich weiterlaufenden
Recherche; Apply/Katalogexport, Skillaktivierung und finale reale Modellmatrix
bleiben offen. Providerempfehlungen, Referralbezugsstellen und OAuth-Inventar
sind im öffentlichen Quellstand enthalten. Eigene Referral-Links fehlen noch.
