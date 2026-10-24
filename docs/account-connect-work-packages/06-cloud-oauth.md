# AC-06: Google und Cloudflare mit eigenem OAuthzugang

Planungsbasis `5dc916f`; Quellen und offene Detailbelege im
[Connectbericht](../account-connect-research/groq-mistral-google-cloudflare.md)
und [Quotabericht](../quota-auth-research/google-cloudflare.md).
Owner W6, Verträge/Wiring W0, Lifecycle W1, Katalog/UI W2, Abnahme W9.

## Ergebnis und Abhängigkeiten

Ein Nutzer verbindet ein vorhandenes Projekt beziehungsweise einen Cloudflare-
Account und erhält die tatsächlich lesbaren Nutzungsdaten. Bestehende Inferenzkeys
bleiben erhalten. OAuth-Inferenz wird unabhängig qualifiziert; ein erfolgreicher
Login und ein Metadatenread sind kein Nachweis dafür.

AC-00/01 liefern Connectoperation, Credentialowner, Ressourcenbindung und
Monitorstart ohne Core. AC-02 liefert Browseraktion/Picker/Teilergebnis;
AC-09 aktiviert nur qualifizierte Methoden. Öffentliche Dokumentation und
synthetische Antworten lassen sich vorher ohne Anmeldung prüfen. Noch keine
Appregistrierung, Rechteausweitung oder Kontoprüfung durch diesen Plan autorisiert.

## AC-06-G: Google-Projektmonitor zuerst

1. Vorhandenen `GeminiMonitoringQuotaAdapter` und `MonitorAccessCatalog` prüfen;
   bisherige Usage nicht als neue vollständige Restquote darstellen. Eigenen
   Desktop-PKCE-Login vorbereiten, ohne Nutzereingabe einer OAuth-Client-ID.
2. Maintainerclient, zulässigen Loopbackcallback, Produktionsconsent und Scope-
   umfang qualifizieren. Testingclient mit möglichem Sieben-Tage-Refreshablauf
   ist keine fertige öffentliche Anmeldung. Kein vertrauliches Appsecret verteilen.
3. Verfügbare vorhandene Projekte über den belegten Ressourcenread auswählen;
   Projekt-ID und -Nummer speichern. Browserkonto oder OAuthclientprojekt
   bestimmen nicht automatisch das Quota-/Inferenzprojekt. Reconnect pinnt es.
4. Monitoringrechten `monitoring.timeSeries.list` und Cloud-Quotas-Limitrechten
   den tatsächlichen Grant und Projekt-IAM gegenüberstellen. Kein pauschaler
   Owner-/Editorbedarf und keine erfundene Generative-Language-Userrolle.
5. Modellmetriken der Familie `generativelanguage.googleapis.com/quota/...`
   mit passenden effektiven Limits verknüpfen. Modell/Limitdimension, Einheit,
   Aggregation und Fenster müssen übereinstimmen; ungeklärter Join bleibt unbekannt.
   Bis zu 150 Sekunden Verzögerung mit `DataAsOf`/Freshness sichtbar halten.
6. RPM, TPM und Pacific-Tagesgrenzen getrennt abbilden. Keine kontoweite Balance
   aus generischen Requestzahlen; fehlende Serie ist nicht gemessener Nullverbrauch.
   Umfang außerhalb lain nur soweit durch Quelle belegt darstellen.
7. Reconnect bei fehlenden/erweiterten Scopes bewusst durchführen; bei Installed
   Apps keinen stillen inkrementellen Consent versprechen. IAMfehler durch
   eine konkrete Berechtigungsaktion beheben, nicht endlos erneut anmelden.

**Kleinster entscheidender Versuch:** Für genau ein beauftragtes bestehendes
Projekt ausschließlich erlaubte Discovery-/Monitoring-/Quota-Reads durchführen,
die korrekte Serien-/Limitzuordnung und verweigerte optionale Rechte nachweisen.
Ausgaben bereinigt: Feldschema, Scope, Zeitbasis und Ergebnis; keine Tokens oder
persönlichen Ressourcenlisten in Repositoryevidence. Ohne geeigneten Account
bleibt Livequalifikation offen; lokale Fixtures sind trotzdem möglich.

**Separates Inferenzgate:** Der aktuelle OpenAI-kompatible lain-Transport ist
mit API-Key belegt, seine konkrete OAuthakzeptanz nicht. Vor Freigabe genau
Transport, Audience, benötigte Headers einschließlich Quota-Projekt und
Tokenaktualisierung prüfen. Ein erforderlicher nativer Gemini-Transport wird als
eigene Vertragsänderung an W0 gegeben. Kein Serviceaccount, Key oder Billingprojekt
als automatischer Ausweg. Der vorhandene Key kann mit OAuthmonitor kombiniert bleiben.

## AC-06-C: Cloudflare ohne erfundene Restquota

1. Eigenen öffentlichen Code+S256-PKCE-Client vorbereiten; Drittapp-Deviceflow
   ist nicht unterstützt. Keine Wrangler-Client-ID oder alten Scope-Strings kopieren.
   Publisher-/Registrierungsanforderungen sind Maintainerarbeit.
2. Tatsächliche OAuth-Scope-IDs und die Akzeptanz des Grants beim konkreten
   Accountdiscoveryread feststellen. Consent bietet Accountwahl; daraus folgt
   keine automatisch bekannte Account-ID im Callback. Nicht aus Tokens raten.
3. Genau einen autorisierten Account binden, mehrere im Picker unterscheiden.
   Bestehende Inferenzroute und Monitor dürfen nicht versehentlich verschiedene
   Accounts verwenden. Kein Global-Key als Discoveryfallback anfordern.
4. Den konkreten GraphQL-Kandidaten `aiInferenceAdaptiveGroups.sum.totalNeurons`
   mit aktuellem Schema, Accountentitlement und notwendigen Rechten qualifizieren.
   Sampling, Verzögerung und Abdeckung von REST/Bindings/Gateway getrennt belegen.
5. Nutzung und täglich freie Tarifgröße erst bei gleicher belegter Zeit-/
   Account-/Produktscope zu Rest zusammenführen. 10.000 Neurons sind kein aus
   Login abgelesener Saldo. Unbekannte Vollständigkeit erlaubt keinen exakten
   Restbalken; qualifizierte Nutzung kann dennoch sichtbar sein.
6. Billing Usage v2 Alpha/Restricted nicht als allgemein verfügbaren Monitor
   voraussetzen. Fehlender Datensatz oder Recht bleibt Teilergebnis mit Portal,
   keine kaputte Anmeldung und keine automatische Ausweitung auf Token-Create.

**Kleinster entscheidender Versuch:** Nach eigener Clientvorbereitung einen
beauftragten Account rein lesend entdecken, erlaubte Scopes und das relevante
Dataset abfragen. Erfolg verlangt nachvollziehbare Identität, Schema und Zeitbasis;
fehlende Neuronabdeckung hält Restquote ausdrücklich offen. Kein Inferenzrequest
zum Erzeugen von Testdaten und keine Workers-/Plan-/Tokenanlage als Voraussetzung.

**Separates Inferenzgate:** Workers AI „Read“ kann laut Runreferenz bereits
Inference erlauben. Rechte nicht allein nach ihrem Namen als read-only bezeichnen.
Konkreten REST-/Kompatibilitätsweg, Scope, Audience, Accountbindung und erneuerbare
Tokenprojektion prüfen. Ein reiner Monitor verlangt diese Fähigkeit nicht.

## Dateien und gemeinsame Grenzen

W6 besitzt Änderungen an `src/Lain.Host/Quota/GeminiMonitoringQuotaAdapter.cs`
und einem neuen `CloudflareQuotaAdapter.cs` sowie neue Google-/Cloudflare-
Login-/Discoveryadapter in den von AC-01 vorgegebenen Anbieterordnern. Neue
lokale Fixtures in `tests/account-connect-cloud/`; bestehende Quotasourcetests
nur nach Dateiabstimmung. Klassen-/Ordnernamen sind Vorschläge.

Keine Änderungen auf eigene Faust an Program.cs, gemeinsamen Contracts,
Credentialstores, Catalog-JSONs oder Desktop/CLI. W6 liefert benötigte Scopes,
Ressourcenfelder und Quotasemantik an W0/W1/W2. Besonders direkte Inferenz muss
aktuelle Tokens über den qualifizierten Owner erhalten; ein statischer Keyexport
oder Refresh im Quotaadapter ist kein entsprechender Anschluss.

## Abnahme und unabhängige Freigaben

- Zwei Projekte/Accounts, gleicher Modellname: keine vermischten Quotas;
  Reconnect erhält lokalen Account und verhindert unbemerkten Scopewechsel.
- Cancel, Callbackreplay, abgelehnter Scope, fehlendes IAM, widerrufener Grant
  und paralleler Refresh liefern begrenzte, konkrete Zustände ohne Secretleaks.
- Gültiger Monitor bei abgelaufener Inferenz bleibt nutzbar und umgekehrt.
- Fehlende, verspätete, gesampelte oder nicht passende Serien werden nicht zu
  Null beziehungsweise exakter Restquota. Source-/Windowidentität bleibt stabil.
- Ohne Core kann die qualifizierte Monitoringstrecke starten und aktualisieren;
  es entsteht keine Route, neue Cloudressource oder Inferenzanfrage.
- Googlemonitor, Cloudflaremonitor und beide Inferenzwege haben jeweils eigene
  Nachweise. Ein offenes Gate blockiert keine andere bereits qualifizierte Strecke.
