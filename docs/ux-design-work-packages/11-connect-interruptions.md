# UXD-11 – Connect-Unterbrechungen und Reconnect derselben Identität

Ergebnis: Ablauf, Abbruch und Wiederverbinden lassen keinen doppelten oder
fälschlich verbundenen Account zurück. Ergänzt UXD-03 und AC-02/09. Native
Dateien bleiben beim Connectowner; Credential-/Login-Lifecycle bei AC.
Voraussetzungen: UXD-00/01/03 und tatsächlich qualifizierte Methode je Anbieter.

## Aufgaben

1. Start, Warten, Cancel, expired, Fehler und completed auf reale Operationszustände
   abbilden. Wiederöffnen des Browsers verwendet die vorhandene Operation.
   Nach Ablauf startet erst die ausdrückliche Aktion einen neuen Versuch.
2. Back/Cancel bewahren Anbieter und ungefährliche Eingaben; keine Token-/Keywerte
   in Screenshot, Clipboard oder Logs. Verspätete erfolgreiche Antworten nach
   Cancel dürfen nicht wie eine bewusst bestätigte neue Verbindung erscheinen.
3. Reconnect vor dem Start an bestehende Account-ID binden. Name, lokale History,
   Modellbindungen und Auswahl gehören weiter zu dieser Identität; keine neue ID
   durch den Umweg über Add account erzeugen. Fehlende Quotaquelle ist kein
   Credentialfehler. Tatsächlichen aktuellen Vertragsstand von AC verwenden.
4. Abschluss trennt Credential gespeichert, Monitoring verfügbar und tatsächliche
   Inferenzprobe. Modellwahl bleibt optional. Ohne qualifizierte Methode konkrete
   Voraussetzung oder bestehenden Weg nennen; keine Erfolgsschablone anzeigen.

## Abnahme

- `CONNECT-EXPIRED`: „No account was added“, bewahrter Name, Start new sign-in
  und Back/Cancel bei 800×560 erreichbar. Keine erfundene Ablaufzeit.
- `CONNECT-RECONNECT`: ausdrücklich dieselbe Identität, kein vorgelagerter Delete,
  kein erfundener Quota-/Capabilitystand; nach Antwort genau eine Account-ID.
- Cancel unmittelbar vor completed sowie wiederholtes Reopen/Doppelklick und
  Hostverlust synthetisch prüfen. Keine unbeabsichtigte Discovery oder Inferenz.
- Monitor ohne Codex-Corevoraussetzung bietet diesen Weg nicht als ausführbar an.
  Die Bilder zeigen eine qualifizierte Gatewayfixture, keinen Monitorbeleg.

Neue Statusdarstellung bleibt ein [Entwurf](../uxpilot-followup-2026-10-24.html).
Lifecycle-/Identitätsschutz folgt bereits bestehenden Anforderungen. Lieferung:
API-Zustandsmatrix, native Zustände beider Größen, synthetischer Race-/Fehlernachweis;
echte Providerqualifikation separat benennen. Keine Logins in diesem Planauftrag.
