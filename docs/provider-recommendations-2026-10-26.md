# Providerempfehlungen für Deutschland — 26.10.2026

Die erneute öffentliche Webrecherche funktioniert. Alle **27 implementierten
Provideridentitäten** sind in den unten verlinkten drei Teilberichten abgedeckt.
Erfolgreich gelesene Seiten, Suchindexbelege, alte Angaben und verbleibende
Lücken sind dort getrennt. Die frühere vollständig blockierte Preisprüfung ist
überholt; deren Fehlversuche bleiben in der [Evidenzdatei](provider-recommendations-evidence-2026-10-26.json)
erhalten. Keine Anmeldung, kein Kauf und keine Inferenzanfrage.

## Drei günstige Aboempfehlungen

Das sind **Alternativen nach Bedarf**, kein empfohlenes Dreierpflichtpaket.
Preise sind USD-Listenpreise pro Monat; deutscher Checkout/Steuern ungeprüft.
Die Bewertung richtet sich nach nutzbarem Produktumfang, nicht Werbervergütung.

| Auswahl | Preis | Warum interessant | Entscheidende Grenze |
| --- | ---: | --- | --- |
| **[NanoGPT Pro](https://nano-gpt.com/pricing)** | **$12/Monat** | Breiter offener Modellpool, ausdrücklich auch über API; 60 Mio. Inputtokens/Woche, Output enthaltener Modelle ohne separate Berechnung | Exakte Modellvariante muss inklusive sein. Geschlossene Modelle, bestimmte Varianten, bezahlte Tools und feste Providerwahl können Guthaben verbrauchen. |
| **[Synthetic Go](https://synthetic.new/pricing)** | **$9/Monat** | Preiswerter offener Modellpool mit API und nachwachsender Wochenquota | Modellabhängig gewichteter gemeinsamer Pool; kein festes Tokenversprechen. Standard für $30 erst bei beobachtetem Engpass. |
| **[OpenCode Go](https://opencode.ai/go)** | **$10/Monat** | Kuratierter Codingmix, andere Agents ausdrücklich vorgesehen | Einzelworkspace und Codingtraffic; Session-/Clientkennung erhalten. Zeitfenster gleichzeitig, keine addierbaren Modellguthaben. |

**Für Lain zuerst einen davon auswählen und die gewünschten Modellrouten prüfen.**
NanoGPT ist der stärkste Startkandidat für einen großen offenen Pool. Synthetic
ist eine günstigere Alternative mit anderem Limitmodell; OpenCode lohnt sich
besonders für dessen konkreten Codingmix. Modellüberschneidung allein rechtfertigt
kein zweites Abo. Öffentlich bestätigte Tarifbedingungen sind keine neue
Livequalifikation der jeweils installierten Lainroute.

**Zusatzoption:** [Kilo Pass Starter](https://kilo.ai/pricing/kilo-pass/) ab
**$19/Monat** ist ein Guthabenabo mit Bonus, keine Flatrate. Der
[Gateway](https://kilo.ai/gateway) erlaubt externe API-Nutzung. Die aktuellen
Lain-Katalogrouten sind jedoch explizite Free-Routen; Pass-/Endpointabdeckung
vor Kauf gesondert prüfen. Nicht als heute nachgewiesen funktionierendes
Lain-Passabo empfehlen.

## Einfacher kostenlos anfangen

- **[Groq Free](https://console.groq.com/docs/rate-limits):** brauchbar für kleine,
  schnelle API-Aufgaben. Enge TPM-Limits können große Codingprompts ausschließen;
  Kartenfreiheit ist aus dem Upgradeablauf abgeleitet.
- **[Gemini API Free](https://ai.google.dev/gemini-api/docs/billing):** zusätzliche
  Modellfamilie ohne Paid-Billing; Modell und Projekt bestimmen die Limits.
- **[Requesty Free](https://www.requesty.ai/pricing):** ausdrücklich ohne Karte,
  [200 Requests/Tag und 20/min](https://docs.requesty.ai/features/free-models),
  organisationsweit geteilt. Lain-End-to-End-Abnahme separat.
- **Einmaliger Trial:** [Inception](https://docs.inceptionlabs.ai/get-started)
  nennt 100 Mio. Starttokens ohne Zahlungsdaten. Ablauf und konkrete Ratenlimits
  bleiben offen; kein regenerierender Gratispool.

SambaNova bleibt kartenfrei, ist mit 20 Requests/Tag aber enger. OpenRouter hat
ohne Guthabenkauf nur 50 Free-Requests/Tag. Alibaba/Nebius sind wegen Kostenübergang
bzw. Kartenpflicht keine erste Empfehlung für einen einfachen Gratisstart.
Details und Primärquellen im [Free-Bericht](provider-research-retry-free-2026-10-26.md).

## Referral: was sich tatsächlich einbauen lässt

**NanoGPT** hat ein belegtes Referral mit eigener Invitations-Seite. **Kilo**
hat ein separates Partnerprogramm mit Bewerbung; persönliches Pass-Referral
ist kein Ersatz für öffentliche automatische Verteilung. Bei **Synthetic** ist
der Linkbereich loginpflichtig, aktuelle Prämien bleiben ungeprüft. **OpenCode**
meldet sein früheres Referralprogramm als beendet. Eigene Links liegen weiterhin
nicht vor; keine fremden Codes oder konstruierten Trackinglinks einbauen.

[Bezugsstellen und Regeln](provider-referrals-overview-2026-10-26.md) sowie die
[erneute Abo-/Referralprüfung](provider-research-retry-subscriptions-2026-10-26.md)
enthalten die offiziellen Links. Geldprovision, Abo-/Nutzerbonus und bloßes
Kontoguthaben bleiben getrennt. Programmeignung für einen öffentlichen
Lain-Katalog ist je Anbieter zu prüfen.

## Alle 27 Connectoren und offene Kaufprüfungen

- [Synthetic/OpenCode: 2 Provider](provider-research-retry-core-2026-10-26.md).
- [Abos/Referrals: 10 Provider](provider-research-retry-subscriptions-2026-10-26.md):
  Kimi, Z.ai, NanoGPT, Kilo, Poe, Command Code, MiMo, StepFun, Mistral, Codex.
- [Free/Trial/PAYG: 15 Provider](provider-research-retry-free-2026-10-26.md):
  Alibaba, Cloudflare, Cohere, DeepSeek, Fireworks, Gemini, Groq, Inception,
  Nebius, NVIDIA, OpenRouter, Requesty, SambaNova, Upstage, xAI.

Kimi- und StepFun-Preise bleiben teilweise nicht auslesbar. Poe zeigt im Abruf
Jahrespreise, keine belegten Monatsangebote. Command Code GOAT hat widersprüchliche
Budgetangaben und eine Endpointabweichung zum Lain-Default; Mistrals API-Inklusivmenge
ist nicht eindeutig zugeordnet. Z.ai/MiMo-Codingpläne sind kein pauschaler
Zugang für beliebige Backendlast. Für vorhandenes Codexkontingent besteht aus
diesem Vergleich kein Anlass zum erneuten Abokauf. Diese Grenzen sind gezielte
Abnahmeaufgaben, keine pauschale Aussage gegen die Anbieter.
