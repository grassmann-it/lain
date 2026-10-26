# Providerempfehlungen für Deutschland — 26.10.2026

**Rechercheergebnis mit Aktualitätslücke: keine abschließend aktuell verifizierte Kaufempfehlung.** Der implementierte Bestand umfasst **27 Provideridentitäten**, davon 22 eingebettete Katalogmanifeste und fünf zusätzliche Provider im `ConnectionMethodCatalog`. Nach den vorhandenen Belegen sind **Synthetic Go ($9/Monat)** und **OpenCode Go ($10/Monat)** die zwei interessantesten kleinen Abo-Kandidaten. Dies sind vorläufige Produkturteile, keine heute verifizierten Angebote. Nicht auf drei bis fünf auffüllen.

**Preisprüfung am 26.10.2026 versucht, nicht abgeschlossen.** Der Webtoolaufruf lieferte Fehler; der Aufruf mit serialisiertem `input` endete mit HTTP-Transportfehler zu `chatgpt.com/backend-api/ps/mcp`. Kein Browser verfügbar. Direkter öffentlicher Abruf von `https://synthetic.new/pricing` scheiterte an DNS (`Temporary failure in name resolution`). In diesem Lauf wurde keine externe Preisseite erfolgreich gelesen. Alle nachstehenden Angebotsangaben sind ausdrücklich **übernommene Bestandsbelege vom 23.–26.10.2026**, keine neue Webverifikation. Die Evidenzdatei trennt diese Herkunft vom Datum dieses Berichts.

## Entscheidung nach Nutzerwert

| Priorität | Vorläufiger Kandidat | Nutzen und Kosten | Entscheidende offene Prüfung |
| --- | --- | --- | --- |
| 1 | Synthetic **Go**, $9 monatlich | Preiswerter API-naher Zugang zu mehreren offenen Modellfamilien; Quotaadapter vorhanden. Standard $30 erst bei tatsächlich zu kleinem Go-Pool, Big $90 derzeit kein Budgettipp. | Heutige Go-Verfügbarkeit, konkrete Creditmenge/Gewichte, Steuer und DE-Zugang; gewünschte Modelle/Tools sowie Nutzungsregeln für persönliche Codingworker bestätigen. |
| 2 | OpenCode **Go**, $10 monatlich | Attraktiv, wenn gezielt die angebotenen Codingmodelle benötigt werden. Go Plus $40 zunächst zu teuer ohne belegten Engpass. | Aktuelle Modell-/Planabdeckung, Protokollroute, DE-Checkout und Zulässigkeit des gewünschten Lain-Einsatzes; parallele Fenster und Extra Usage prüfen. |

**Budgetentscheidung:** Zunächst nur einen dieser Kandidaten erwägen. $9 bzw. $10 sind Originalpreise aus dem Bestand, kein EUR-Endpreis. Ein kombinierter nomineller Grundpreis von $19/Monat wäre erst bei nachgewiesen komplementärer Modellabdeckung sinnvoll; identische GLM-/Kimi-/DeepSeek-Familien sind keine unabhängigen zusätzlichen Fähigkeiten. Keine Jahresbindung zur Erstbewertung eingehen. Monatspreise sind keine belegte Aussage zu Kündigungsfrist oder fehlender Mindestlaufzeit.

**Kostenloser Einstieg vor zusätzlichem Abo:** SambaNova Free ohne verknüpfte Zahlungsmethode ist nach Bestandsbeleg der klarste wiederholbare Kandidat. Für schnelle, unterschiedliche Modelle kommen Groq Free und berechtigte Gemini-Free-Projekte infrage; deren Kartenfreiheit und aktuelle Modelllimits bleiben hier offen. OpenRouter-, Requesty- und Kilo-Free-Routen ergänzen die Auswahl, sind jedoch wegen gemeinsamer Limits/Kapazität kein unbegrenztes Codingbudget. Inception ist ein besonders großzügig beworbener **einmaliger** 100-Mio.-Token-Trial ohne Zahlungsdaten, kein Dauerabo; Ablauf ungeklärt. Fireworks bietet laut Bestand einen kleinen $1-Einstieg, Cohere Evaluation eine zusätzliche Modellfamilie mit Produktionsgrenze. Das sind Prüfprioritäten, keine Garantie einer individuellen Freischaltung.

**Bewusst keine weiteren Kaufempfehlungen:** NanoGPT hat einen kleinen PAYG-Einstieg, aber $12 Pro aus der Vorrecherche und der implementierte Subscriptionreader beweisen weder heutige Neuabo-Verfügbarkeit noch API-Inklusivnutzung. Z.ai und Kimi sind fachlich plausible Codingoptionen, jedoch fehlen aktuell geprüfte Preise, Renewal-/Aktionsregeln und passende Entitlements. Kilo Pass hat keinen aus dem implementierten Gateway-Freeanschluss ableitbaren Abonutzen. Step Plan ist transparent im Katalog bepreist, aber DE-Zugang, CNY-Abrechnung und gewichtete Peaklimits machen ihn derzeit weniger einfach. Poe und Mistral bleiben ohne konkrete Punkt-/Inklusivmenge nicht vergleichbar. Kein zusätzlicher ChatGPT-/Command-Code-Kauf ohne aktuellen Preis und persönlichen Mehrbedarf.

## Umfang, Steuern und Qualifikationsgrenze

Gelesen: `AGENTS.md`, `src/Lain.Host/Providers/ProviderCatalogRegistry.cs`, sämtliche 22 JSON-Manifeste in `src/Lain.Host/Providers/Catalog/`, `src/Lain.Host/Accounts/ConnectionMethodCatalog.cs`, `src/Lain.Host/Accounts/Managed/ManagedLoginQualification.cs`, `docs/technical-inventory.md`, `docs/provider-discovery-2026-10-24.md`, `docs/provider-discovery-expansion-2026-10-24.md`, `docs/provider-priorities-budget-2026-10-24.md`, `docs/evaluation-account-connections-2026-10-26.md` und `docs/model-live-qualification-2026-10-26.md`. Referralbasis: `docs/provider-referrals-overview-2026-10-26.md`; abgeschlossene Programmbreitensuche nicht wiederholt.

Die ältere technische Inventur ist historischer Architekturkontext; der heutige Umfang folgt dem Code. Codex, Command Code, Kimi, Z.ai und Xiaomi MiMo ergänzen die 22 Manifeste. Aliase, mehrere Endpoints, Monitoringmethoden und verschiedene Abostufen sind keine zusätzlichen Anbieter. Discoverykandidaten wie MiniMax, Chutes, Venice, Claude direkt oder lokale Backends werden nicht als implementierte katalogisierte Provider dazugezählt. Ein generisches OpenAI-Profil ist kein eigenständig qualifizierter Anbieterconnector.

**Für jede Zeile:** Deutschland-/EU-Kaufberechtigung, konkreter Checkout, Währungsumrechnung/Kartengebühr und Steuerbehandlung sind ungeprüft, sofern nicht ausdrücklich anders angegeben. USD/CNY bleiben unverändert; kein fingierter EUR-Wechselkurs. Unklar ist jeweils, ob Listenpreise deutsche Umsatzsteuer enthalten oder hinzukommt. Auch fehlender Festpreis bedeutet **nicht kostenlos**. Jahrespreise/-rabatte, Jahresbindung und Promotionsende sind für sämtliche aktuellen Angebote offen; es wird kein Aktionspreis als dauerhaft oder „pro Monat“ umgerechneter Jahrespreis als monatlich kündbar dargestellt.

Implementiert heißt hier: lesbarer Sourceanschluss/Verbindungsmethode vorhanden. Es bedeutet weder lokal installierter Stand noch Accountzugang, Tarifberechtigung, zulässige Last oder live bestandener Toolroundtrip. Alle 22 Manifeste melden `liveVerification=not_tested`; die gesonderte Modellprüfung vom 26.10. enthält reale, konkrete Ergebnisse und ersetzt diese konservativen Katalogfelder nicht automatisch. Ihr Ergebnis gilt nicht pauschal für alle Provider/Pläne. In diesem Auftrag erfolgten keine Logins, Accountabfragen, Inferenz, Käufe, Anmeldungen, Kontakte, Commits oder schweren Tests.

## Alle 27 Connectoren: Free, Trial, Preis und Einsatz

Die folgenden Limits und Preise stammen aus dem lokalen Bestand. „Offen“ bezeichnet fehlenden Beleg und ist kein Negativbeweis. API-Abos, Coding-Abos und Chatprodukte sind getrennt zu bewerten. Fair Use, Regeneration und hohe beworbene Mengen sind begrenzt; keine Zeile bedeutet unbegrenzte Nutzung.

| Connector | Produkt | Free ohne Karte? Trial/Verfall/Limits | Monatspreis und Währung / Abrechnung | Lain-Nutzbarkeit und Grenze |
| --- | --- | --- | --- | --- |
| alibaba-model-studio | API: Trial/PAYG; Coding Plan ausdrücklich ausgeschlossen | Karte offen; typischerweise 1 Mio. Tokens je berechtigtem Modell, 90 Tage; nur Singapore/International, kein DE/EU-Trial abgeleitet | Kein Abo für diesen Connector; PAYG modell-/regionsabhängig | Workspace-/Regionskey; Free Quota Only standardmäßig aus, verzögert; ohne wirksamen Anbieterstop kein free-only |
| cloudflare-workers-ai | API: Free/PAYG | Karte offen; 10.000 Neurons/Tag, 00:00 UTC; keine Tokens; einige Modelle nur bezahlt | Kein belegtes pauschales Inferenzabo; Mehrverbrauch bezahlt | Account-ID + Token; Free-Billingzustand und Modell-Allowlist vor Gratisnutzung qualifizieren |
| cohere | API: Evaluation, keine Produktionsfreigabe | Karte offen; 1.000 Calls/Monat, Chat 20/min laut Bestand; Trialende offen | Produktionspreis/Vertrag offen | Kompatibilitäts-API nur Parametersubset; Consolelogin unavailable; kein Quota-Monitor |
| deepseek | API: PAYG; Free-Chat separat | Kein allgemeines Gratis-API-Guthaben belegt; Mindestaufladung offen | Kein belegtes API-Abo; USD/CNY-Salden getrennt | Same-key Balance; Betrag ist kein Hard Cap oder Abo-Prozent |
| fireworks | API: Trial/PAYG | $1 Startercredit falls gewährt; Ablauf offen; ohne Zahlungsmethode Stopp laut Vorrecherche | Serverless PAYG; kein empfohlenes Abo | Key + Quotas; offizielle CLI abhängig von qualifizierter Installation; Förderprogramm kein Referral |
| gemini-api | API: Free/PAYG; Google-Chatabo separat | Free Tier für berechtigte Projekte/Modelle; Kartenfreiheit hier nicht neu belegt; RPM/TPM/RPD projektabhängig, Tagesreset Pacific | PAYG pro Modell; kein aus Verbraucherabo abgeleitetes API-Budget | Key; GCloud-CLI nur Monitoring, separat qualifiziert; Free-only setzt passenden Billingzustand voraus |
| groq | API: Free/Developer PAYG | Free Tier dokumentiert; Karte offen; Modell-/Org-RPM/RPD/TPM/TPD | Kein empfohlenes Pauschalabo; Upgrade explizit | Key; Gateway kann Quota lesen; Gratislimits keine beobachtete Restmenge |
| inception | API: einmaliger Trial/PAYG | 100 Mio. Starttokens laut Bestand; keine Zahlungsdaten nötig; Verfall offen | PAYG, aktueller Tokenpreis offen | Vorbereiteter Adapter; Kontogewährung und Modellrechte ungetestet |
| kilo | Gateway Free/PAYG; Kilo Pass separat | Explizite :free-Modelle; 200 Requests/h/IP laut Bestand, gemeinsam; Karten-/Keyzugang offen | Pass-Preise nicht aus Affiliateprämien ableiten; bezahlte Gatewaycredits laut Vorrecherche +5% Zahlungsgebühr | Device/API-Key implementiert; Passabdeckung/Bonusverbrauch in Lain nicht belegt; keine bezahlte Fallbackroute |
| mistral | Free/API/Studio/Vibe; bezahlte Pläne mit gemeinsamer Nutzung | Free-Menge und Kartenpflicht offen; monatliche gemeinsame Nutzung, kein Tokenbetrag erfunden | Pro/Education/Team/Enterprise dokumentiert, aktueller Monatspreis offen; optional Prepaid-Überlauf | Org-/Workspace-Limits überlappen; offizielle CLI unavailable; Le-Chat-Preis allein belegt keine API-Menge |
| nanogpt | API PAYG und ggf. Abo; kostenlose Webmodelle getrennt | Kein freier API-Tarif belegt; PAYG ab $1 Karte bzw. $0,10 Krypto laut 24.10.; Abozugang offen | Historischer Pro-Hinweis $12/Monat; kein bestätigtes heutiges Kaufangebot oder API-Inklusivbudget | Key/OAuth und separater Subscriptionreader implementiert; Modellliste beweist Aboabdeckung nicht; keine Chatgratisübertragung |
| nebius | API: Trial/PAYG; Builder separat | Bankkarte erforderlich; $1 / 30 Tage; Builderförderung nicht allgemeiner Trial | PAYG; kein empfohlenes Abo | Vorbereiteter Anschluss; individuelle Limits/Balance nicht qualifiziert |
| nvidia-build | API: Entwickler-Prototyping | Gratis-Entwicklungszugang; Karte/Verfall/Restlimits offen; nicht pauschal 90 Tage Hosted API | Produktionskosten separat, kein empfohlenes Abo | Kein unbegrenzter Produktionsbetrieb; Enterprise-Evaluation und eigene GPU sind andere Produkte |
| opencode-go | Coding-Abo über konkrete API-Protokolle | Kein Gratis-Trial belegt; Kartendetails offen | Go $10/Monat, Go Plus $40/Monat laut Katalog; 5h/Woche/Monat gelten gleichzeitig | Einpersonenworkspace; 20%/5h, 50%/Woche, 100%/Monat; Modellreferenzbudgets nicht summieren; Extra Usage separat aus |
| openrouter | Gateway: Free/PAYG | 50 Free-Requests/Tag, 20/min; 1.000/Tag nach insgesamt $10 gekauften Credits; Fehlversuche zählen; Karte für Free nicht neu belegt | Kein empfohlenes Abo; Modellpreise und Aufladegebühren separat | Key/OAuth; explizite :free-Routen; Konto-/Keylimit unterscheiden; keine gratis Plugins/Fallbacks unterstellen |
| poe | Chatabo + externe API mit Punkten | Kostenloser Chat ist kein bestätigter kostenloser API-Pool; Karte/Trial offen | Aktueller Monatspreis und Punktmenge unbekannt | Key; Browserclient setup-required bis Registrierung qualifiziert; Abo-/Zusatzpunkte im Saldo nicht getrennt |
| requesty | Gateway: Free/PAYG | 200/Tag und 20/min pro Org; zahlende Org 1.000/Tag, 60/min; Karte offen | PAYG plus Gatewayaufschlag; kein empfohlenes Abo | Key; allgemeine CLI unavailable; separate private Einmalanmeldung laut AGENTS kein allgemeiner Loginbeleg |
| sambanova | API: Free/Developer | Free ohne verknüpfte Zahlungsmethode; konkrete Katalogroute 20/min, 20/Tag, 200.000 Tokens/Tag; kein globales Allmodellbudget | Karte ändert Tier zu Developer PAYG; kein empfohlenes Abo | Key; Consolemonitor unavailable, kein unabhängiger Restquotabeleg |
| stepfun | API PAYG und Step Plan getrennt | Kein Gratisanspruch belegt; Kartenpflicht offen | Mini CNY 49, Plus 99, Pro 199, Max 699/Monat; 0,4/1,6/8/40 Mrd. gewichtete Credits, keine Tokens | Monatscredits verfallen; Step-5-Peakverbrauch werktags 09–12/14–18 Beijing doppelt; Add-ons 30 Tage; CN/DE-Zulassung offen |
| synthetic | API-Abo und separates PAYG | Kein dauerhaft kostenloser API-Tarif belegt; Karte/Trial offen | Go $9, Standard $30, Big $90/Monat; gemeinsamer gewichteter Creditpool regeneriert über die Woche | Key + Quota; Modellrotation, Gewichtung und individuelle Toolfähigkeiten beachten; Big-Beta/Fair Use nicht unbegrenzt |
| upstage | API: einmaliger Trial/PAYG | $10 Startcredit beworben; Karte, Gewährung und Ablauf offen | Aktueller PAYG-Preis offen; kein belegtes empfehlenswertes Abo | Key/Org; mit Zahlungsmethode können nach Creditende Kosten anfallen; kein unabhängiger Quota-Monitor |
| xai | API PAYG; Grok/X-Chatabos separat | Kein allgemeines dauerhaftes Gratisbudget bestätigt; Karte offen | Aktueller Modellpreis offen; Verbraucherabo liefert keinen bestätigten API-Pool | Inferencekey; Monitoring benötigt separaten Managementkey/Team; Groq anderer Anbieter |
| codex | ChatGPT/Codex-Abokontingent; direkte OpenAI API separates Produkt | Heutiger Free-/Trialumfang, Karte und Ablauf nicht verifiziert | Aktueller DE-Monats-/Jahrespreis offen; kein historischer US-Preis als EUR-Checkoutwert | Gateway-owned Login abhängig von Core; Snapshot nur Quota; Standalone-Kandidat unavailable; bestehende lokale Qualifikation keine allgemeine Tarifgarantie |
| commandcode | Coding-/API-Produkt | Kein allgemeiner Gratis-/Trialanspruch belegt; Karte/Ablauf offen | Aktueller Abopreis und enthaltene Budgets offen | API-Key und direkte Quota; offizielle CLI unavailable; dokumentierte Modellobergrenze kein beobachteter Restwert |
| kimi | Kimi Code Abo, Webmitgliedschaft und Moonshot API unterscheiden | Karte/Trial/Gratis-Codeumfang offen; kostenloser Chat kein API-Anspruch | Aktueller internationaler Monatspreis und Code-Berechtigung offen | Code-Key für Inferenz; Kimi-Device liest nur Quota, beschafft keinen Inferenzzugang; Region separat binden |
| zai | GLM Coding Abo versus reguläre API | Kein dauerhafter Gratis-Codingplan belegt; Karte/Trial offen | Aktuelle Lite/Pro/Max-Preise, Aktionen und Verlängerungspreise offen | Coding-Key auf Coding-Endpoint; Managedlogin unavailable; API-Kompatibilität allein erlaubt keine allgemeine Worker-/Backendlast |
| xiaomi-mimo | API und Individual Token Plan | Karte, heutige Gratispläne, Grants und Laufzeit offen | Aktueller Monatspreis/Währung/Planumfang offen | Key; eigener Consolemonitor Linux + Chromium + Desktop, CN/EU-Identität, nach Browserschluss neu verbinden |

## Referral: getrennte wirtschaftliche Bewertung

Keine eigenen Links/Codes im gelesenen Bestand. Fachliches Ranking oben berücksichtigt keine Werbervergütung. Guthaben, Preisnachlass, Loschance und auszahlbare Provision sind verschiedene Vorteile. **Für alle Programme:** eigene Teilnahmeberechtigung, Deutschland, öffentliche Lain-Platzierung und heutige Regeln nicht in diesem Lauf verifiziert. „Neu geprüft 26.10.“ in der Ausgangsdatei ist deren Prüfbehauptung, keine hier wiederholte Verifikation.

Die nachfolgende Tabelle übernimmt die passenden Zeilen der bereits abgeschlossenen Übersicht unverändert; Quellen und damaliger Prüfstatus bleiben erhalten. Dadurch sind für alle 27 Provider Nutzen, eigener Linkeinstieg und bekannte Kanalrestriktionen nachvollziehbar, ohne eine neue Referralbreitensuche zu behaupten.

| Provider | Ref-/Affiliateprogramm | Nutzen / Restriktion | Eigener Linkeinstieg | Offizielle Quelle | Status in Ausgangsrecherche |
| --- | --- | --- | --- | --- | --- |
| OpenAI / Codex | Ja, kontobezogenes Referral | Kontobezogene Nutzungsprämie laut Einladungsdialog; Personal und Business getrennt, Enterprise ausgeschlossen. Keine allgemeine Cash- oder API-Provision belegt; frühere Juni-Resetaktion nicht fortschreiben. | Profilmenü → Invite a friend / Invite a coworker, falls freigeschaltet. Dokumentiert ist eine E-Mail-Einladung; öffentlicher übertragbarer Link nicht zugesagt. Nichts absenden. | [Invite friends and coworkers](https://learn.chatgpt.com/docs/pricing#invite-friends-and-coworkers) | Neu geprüft 26.10.2026 |
| Command Code | Offen / nicht bestätigt | Terms erwähnen nur mögliche zeitlich begrenzte Referralboni; keine aktuelle Aktion mit Prämie und Einstieg belegt. | Kein belegter Link-Einstieg. Später nur vorhandenes eigenes Studio auf ein konkretes Angebot prüfen. | [Command Code: Nutzungsbedingungen](https://commandcode.ai/terms); [Command Code: Preise](https://commandcode.ai/pricing) | Neu geprüft 26.10.2026 |
| Z.ai / GLM | Ja, kontobezogenes Referral | 10% der ersten tatsächlichen Coding-Zahlung als Plattformcredits; Nutzer 10% Erstrabatt. Erstzahlung binnen 72 h, letzter gültiger Verweis; Credits erst ab drei gültigen zahlenden Empfehlungen nutzbar. Weitere Bonusstufe je 30. Keine Barauszahlung; für Abos, Ressourcenpakete und API nutzbar. | Eigene Kampagnenansicht über den Join-Link der Regelseite; persönlichen Link/Code dort kopieren. Nicht aus der Subscribe-URL ableiten. | [GLM Coding: Credit campaign rules](https://docs.z.ai/devpack/credit-campaign-rules); [GLM Coding: Abonnement-Einstieg](https://z.ai/subscribe) | Neu geprüft 26.10.2026 |
| Kimi / Moonshot AI | Ja, kontobezogenes Referral | Beide erhalten bei gültigem Erstabo binnen 24 h eine Loschance auf Kimi-Guthaben, keinen festen Geldbetrag. Erste Zuordnung zählt; Registrierung allein, Verlängerung und Enterprise zählen nicht. Guthaben laut Regeln bis 31.12.2026, konkrete Prämienanzeige maßgeblich. Kein Moonshot-API-Referral daraus ableiten. | Seitenleiste → Invite & Earn Rewards → Invite now; eigenen Link oder QR-Code kopieren. | [Moon Companion Plan: offizielle Aktionsregeln](https://www.kimi.com/en/help/kimi-blackboard/moon-companion-plan) | Neu geprüft 26.10.2026 |
| OpenRouter | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Öffentliche Support- und Dokumentationsquellen belegen keine Referral-Prämie; Inferenzanbieter-Onboarding ist kein Kunden-Affiliateprogramm. | Kein offiziell belegter produktbezogener Link-Einstieg. | [OpenRouter: Support](https://openrouter.ai/support); [OpenRouter: offizieller Dokumentationsindex](https://openrouter.ai/docs/llms.txt) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Requesty | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Mitgliedereinladungen in der Teamverwaltung belegen kein vergütetes Empfehlungsprogramm. | Kein offiziell belegter produktbezogener Link-Einstieg. | [Requesty: Support](https://www.requesty.ai/support); [Requesty: offizieller Dokumentationsindex](https://docs.requesty.ai/llms.txt) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Poe | Offen / nicht bestätigt | Vergütung eigener veröffentlichter Bots pro Nachricht belegt; das ist kein allgemeiner Poe-Empfehlungslink. | Kein belegter allgemeiner Referral-Link-Einstieg; Creatorbereich wäre ein anderes Geschäftsmodell. | [Poe: Creator Monetization FAQ](https://help.poe.com/hc/en-us/articles/21921312368020-Poe-Creator-Monetization-FAQs); [Poe: Creator Monetization Terms](https://poe.com/pages/earnings-tos) | Neu geprüft 26.10.2026 |
| Synthetic | Ankündigung belegt; aktuelle Konditionen offen | Referral öffentlich angekündigt. Historische $5/$5 nach ersten $10 Umsatz sind keine heutige Zusage; aktuelle Prämien und Regeln hinter Login. | Eigener Bereich [Referrals](https://synthetic.new/referrals), Anmeldung nötig. | [Synthetic: öffentliche Referral-Ankündigung vom 14. März 2025](https://synthetic.new/blog/referrals-launch); [Synthetic: Referral-Bereich, Anmeldung erforderlich](https://synthetic.new/referrals) | Neu geprüft 26.10.2026; Ankündigung lesbar, Referralpanel Loginbarriere |
| OpenCode Go / Go Plus | Beendet | Offizieller Konsolenquelltext enthält Endehinweis: keine weiteren Credits für beide Seiten. Keine alten Links bewerben. | Kein aktiver Link zu beschaffen. | [OpenCode Go](https://opencode.ai/go); [Offizieller Wortlaut des Endehinweises](https://github.com/anomalyco/opencode/blob/dev/packages/console/app/src/i18n/en.ts) | Neu geprüft 26.10.2026 |
| Alibaba Cloud Model Studio / DashScope | Ja, Cloud-Referral; Modellprodukt offen | Cloudprogramm: 1 Couponcredit je erstem Trial (max. 50/Monat), 10 beim Erstkauf, 10% berechtigter Folgezahlungen im definierten Zeitraum; 1 Credit = $1 Couponwert, kein Bargeld. Internationales Einzelkonto, Zulassung und gültige Zahlungsmethode; Model-Studio-/DashScope-/Token-Plan-Abdeckung und Couponnutzung offen. | Nach eigener Zulassung im [Invite-&-Earn-Portal](https://www.alibabacloud.com/en/referral) den exklusiven Link kopieren. | [Alibaba Cloud Invite & Earn – Programm und Bedingungen](https://www.alibabacloud.com/en/referral); [Offizielle Model-Studio-/Token-Plan-Seite mit Invite-&-Earn-Verweis](https://www.alibabacloud.com/en/campaign/ai-landing-page-token) | Neu geprüft 26.10.2026; Regeln im offiziellen Suchindex, Direktseite eingeschränkt |
| Cloudflare Workers AI | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. PowerUP und Technology Alliances sind allgemeine Partnerprogramme. Startup-Credits und das reguläre Workers-AI-Freikontingent sind keine bestätigte Vergütung für geworbene API-Kunden. | Kein offiziell belegter produktbezogener Link-Einstieg. | [Workers AI – offizielle Preise und Freikontingente](https://developers.cloudflare.com/workers-ai/platform/pricing/); [Cloudflare – Partnerprogramme](https://www.cloudflare.com/partners/) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Google Gemini API | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Google-Cloud-Partnerschaften, Startup-Förderung, Free Tier sowie etwaige Programme für Workspace oder Verbraucherprodukte belegen keine Referral-Berechtigung für die Gemini API. Google-Cloud-Welcome-Credits sind laut API-Abrechnungsdokumentation nicht für Gemini API/AI Studio verwendbar. | Kein offiziell belegter produktbezogener Link-Einstieg. | [Gemini API – Abrechnung und Credit-Abgrenzung](https://ai.google.dev/gemini-api/docs/billing); [Gemini API – zusätzliche Nutzungsbedingungen](https://ai.google.dev/gemini-api/terms) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Groq | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Free Tier und beantragbare Hackathon-API-Credits sind keine nachgewiesenen Referral-Prämien. Programme fremder Anbieter namens GROX oder gro.co sind kein Groq-Programm. Groq und xAI/Grok sind getrennte Anbieter. | Kein offiziell belegter produktbezogener Link-Einstieg. | [GroqCloud – Billing FAQs](https://console.groq.com/docs/billing-faqs); [Groq – Vertragsrahmen für Cloud- und AI-Dienste](https://console.groq.com/docs/legal/services-agreement) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Inception Mercury | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Das im API-Quickstart beschriebene Startkontingent von 100 Millionen Tokens ist ein Neukundenangebot und kein belegter Werberbonus. Unternehmensangebote und das gleichnamige NVIDIA-Inception-Programm nicht mit Inception Mercury verwechseln. | Kein offiziell belegter produktbezogener Link-Einstieg. | [Inception Platform – offizieller API-Quickstart und Neukundenkontingent](https://docs.inceptionlabs.ai/get-started); [Inception – Mercury-3-Produktankündigung](https://www.inceptionlabs.ai/blog/introducing-mercury-3) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Mistral API | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Ein Partnerkontaktformular belegt keine veröffentlichte Affiliate-Provision. Free mode, im Plan enthaltene API-Nutzung und einlösbare Credit-Gutscheine sind nicht automatisch Referral-Boni. | Kein offiziell belegter produktbezogener Link-Einstieg. | [Mistral – offizielle Partner und Auswahlverfahren](https://mistral.ai/partners/); [Mistral – Pläne, API-Nutzung und Prepaid-Abrechnung](https://docs.mistral.ai/admin/billing-usage/subscriptions) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Nebius Token Factory | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Der allgemeine Nebius Partner Program, Builder-Förderung, Promo-Codes und das Token-Factory-Startguthaben sind kein belegtes öffentliches API-Referral. Die offizielle Y-Data/Nebius-Academy-Studentenempfehlung betrifft einen Kurs, nicht Token-Factory-Nutzung. | Kein offiziell belegter produktbezogener Link-Einstieg. | [Token Factory – Billing & Consumption](https://docs.tokenfactory.nebius.com/other-capabilities/billing-new); [Nebius – allgemeiner Partner Program](https://nebius.com/nebius-partner-program) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| NVIDIA Build | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Der NVIDIA Developer Program gewährt Entwicklungs-/Prototyping-Zugang; dieser Zugang ist keine Vermittlungsprämie. NPN, Cloud-Partner, NVIDIA Connect und Startup-Inception sind gesonderte Programme und bestätigen kein persönliches Build-API-Referral. | Kein offiziell belegter produktbezogener Link-Einstieg. | [NVIDIA NIM – General FAQ und Entwicklungszugang](https://docs.api.nvidia.com/nim/docs/product); [NVIDIA – Partner Network](https://www.nvidia.com/en-us/about-nvidia/partners/) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| StepFun | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. Der Builder Program belohnt qualifizierte Projektbeiträge beziehungsweise ausgewählte Demos; das ist kein belegtes Kunden-Referral. Ein invite_code-Parameter allein beweist weder eine Provision noch deren Geltung für PAYG oder Step Plan. Beide Produktbereiche getrennt prüfen. | Kein offiziell belegter produktbezogener Link-Einstieg. | [StepFun – offizielle API-Plattform](https://platform.stepfun.ai/); [StepFun Builder Program – Projektbelohnungen statt bestätigter Referral-Provision](https://platform.stepfun.ai/builder-program) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| xAI / Grok | Offen / nicht bestätigt | Kein produktbezogenes Referral belegt. API-Credits, Verbraucherkonten und X-/Grok-Abonnements sind getrennt zu betrachten. Fremde Dienste wie xAIcreator und der andere Anbieter Groq sind kein Nachweis eines xAI-Referral-Programms. Geteilte Chat-Inhalte auf grok.com sind keine offiziellen Programmbedingungen. | Kein offiziell belegter produktbezogener Link-Einstieg. | [xAI – offizieller Grok-API-Quickstart](https://docs.x.ai/developers/quickstart); [xAI – offizielle API-Produktseite](https://x.ai/) | Offen; Produkt-/Partnerquelle neu gelesen 26.10.2026, kein Negativbeweis; Screening 24.10. |
| Xiaomi MiMo | Ja, Token-Plan-Referral | 10% API-Credits auf ersten bezahlten Token Plan; bestimmte höhere Abos 20%. Nutzer 10% Erstrabatt. Bindung binnen drei Tagen nach Registrierung, Erstkauf binnen 30 Tagen; normale API-Aufladungen zählen nicht. Standardcap 30 bezahlte Empfehlungen; Credits nach drei Tagen, 40 Tage gültig, nur API, kein Bargeld. Frühere Gratispläne können ausschließen. | Eigener Refer-&-Earn-Bereich der Plattform; dort persönlichen Link/Code kopieren. Exakter aktueller Menüpfad ohne Konto nicht bestätigt. | [Promotions-FAQ](https://mimo.mi.com/docs/en-US/quick-start/faq/promotions); [Referral-Anleitung](https://mimo.mi.com/docs/en-US/quick-start/promotions/refer) | Neu geprüft 26.10.2026: FAQ lesbar; Referral-Anleitung per Webtool nicht erreichbar |
| NanoGPT | Ja, Referral einschließlich API | 10% berechtigter bezahlter Nutzung, Nutzer 5% Rabatt; separat 5% auf bezahlte Abokäufe/-verlängerungen. Tägliche USD-/Nano-Kontogutschrift. Keine Provision auf Einzahlungen, freie/aboabgedeckte Nutzung oder bestimmte Providerwahl-Aufschläge; Bild-/Video-API nur Rabatt. Bankauszahlung und Mindestbetrag nicht bestätigt. | Eigene [Invitations](https://nano-gpt.com/invitations); eigenen Link und dessen Code kopieren. | [Support-Regeln](https://nano-gpt.com/support); [Earn](https://nano-gpt.com/earn); [App-Attribution](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce#referral-attribution) | Neu geprüft 26.10.2026; Invitations führt zur Anmeldung |
| Kilo Partner-/Affiliateprogramm | Ja, separates öffentlich beworbenes Programm | Einmalig $9,50 / $24,50 / $99,50 für qualifizierte neue Starter-/Pro-/Expert-Passkunden, monatlich oder jährlich. Nur Kilo Pass, keine Gateway-PAYG-Nutzung, Verlängerungen oder späteren Planwechsel. 30-Tage-Last-Touch-Cookie, 30 Tage Prüfung; Bankauszahlung monatlich ab $100. Bezahlter/Suchmaschinen-/Coupon-/Cashback-Traffic eingeschränkt; lain-Kanal offen. | Auf [Partners](https://kilo.ai/partners) Apply for Kilo Pass; nach Annahme genehmigten eigenen Partnerlink/-code erhalten. Keine Bewerbung ausgeführt. | [Kilo Partner Program](https://kilo.ai/partners) | Neu geprüft 26.10.2026; Programm belegt, eigene Zulassung fehlt |
| Kilo persönliches Pass-Referral | Ja, persönlich; für öffentliche lain-Verteilung nicht zulässig einplanen | Beide erhalten 50% der Monatsstufe als Bonus auf nächste berechtigte Verlängerung; höchstens fünf Boni. Aktives persönliches Monatsabo, brandneuer Nutzer, Erstzahlung binnen 30 Tagen im Webcheckout. Kein Geld, keine Jahres-/Teampläne. Persönliche Kanäle; Massenmarketing und automatisierte Verteilung verboten. Persönliche Social-Media-Weitergabe ist genannt, daraus folgt keine Freigabe für einen öffentlichen Appkatalog. | Persönliches Referralwidget im eigenen Kilo-Konto (Impact Advocate). Diesen Link nicht als öffentlichen lain-Partnerlink übergeben. | [Persönliche Referralbedingungen, §§ 2–4](https://kilo.ai/refer/terms/pass) | Neu geprüft 26.10.2026; klar vom Partnerprogramm getrennt |
| Fireworks | Allgemeines Referral offen; Förderung separat belegt | AMD AI Developer Program: $50 Fireworkscredits, 90 Tage, nur nach Zulassung. Das ist Projekt-/Entwicklerförderung, keine Prämie für geworbene Kunden. | Kein belegter allgemeiner Referral-Link. AMD-Bewerbung würde einen Fördercode für eigene Nutzung liefern. | [AMD-/Fireworks-Förderung](https://fireworks.ai/partners/amd) | Neu geprüft 26.10.2026: Förderung; allgemeiner Referralstatus aus Screening 24.10. |
| SambaNova | Nicht bestätigt | Free Tier ist keine Referralprämie. | Kein offiziell belegter produktbezogener Einstieg. | [Limits](https://docs.sambanova.ai/docs/en/models/rate-limits) | Übernommen 24.10.2026; am 26.10. nur konsolidiert |
| Cohere | Nicht bestätigt | Evaluation-Key und Trial sind keine Referralprämie. | Kein offiziell belegter produktbezogener Einstieg. | [Limits](https://docs.cohere.com/v2/docs/rate-limits) | Übernommen 24.10.2026; am 26.10. nur konsolidiert |
| Upstage | Offen / nicht bestätigt | Kein gesicherter produktbezogener Vorteil im gelesenen Bestand | Kein belegter eigener Linkeinstieg | Siehe Produktquellen unten und Ausgangsübersicht | Nicht neu webverifiziert |
| DeepSeek | Nicht bestätigt | Starter-/Balancezugang ist keine Vermittlungsprämie. | Kein offiziell belegter produktbezogener Einstieg. | [Balance](https://api-docs.deepseek.com/api/get-user-balance) | Übernommen 24.10.2026; am 26.10. nur konsolidiert |

**Wirtschaftliche Einordnung:** NanoGPT ist wegen dokumentierter Appattribution und Nutzungsgutschriften besonders prüfenswert, wird dadurch aber kein belegtes gutes Abo. Kilo kann nach Zulassung Cashprovision auf qualifizierte Passkäufe bringen; Gateway-PAYG zählt nicht und der persönliche Referral ist wegen Kanalrestriktionen kein Ersatz für einen Partnerlink. Z.ai/MiMo liefern begrenzt nutzbare Credits; Kimi eine variable Loschance. Synthetic hat ohne zugängliche heutige Regeln keinen kalkulierbaren Bonus. Öffentliche Verbreitung, automatische Attribution und eigener Code wären separate Schritte; hier wurde nichts eingerichtet. Keine fremden Codes oder angeblich persönlichen Links konstruiert.

## Produktquellen und prüfbare Bestandsdaten je Manifest

Die verlinkten offiziellen Primärquellen sind **zur Nachprüfung**, nicht als in diesem Lauf erfolgreich abgerufen gekennzeichnet. Detaillierte Planobjekte, Quellprüfzeiten und Beschränkungen stehen zusätzlich in der Evidenzdatei. Die folgenden Zusammenfassungen übernehmen die Katalogwerte; `unbekannt` bleibt unbekannt.

### Alibaba Cloud Model Studio (`alibaba-model-studio`)

Source: `src/Lain.Host/Providers/Catalog/alibaba-model-studio.json`. Adapter `prepared`, Live `not_tested`, Quota `unsupported`.

- **Eligible new-user model trial** (trial): kein verifizierter Festpreis im Manifest; Pool `account + concrete model; RAM users shared`; enthalten: None tokens. 90 days from the latest of activation, model release or access approval; exact expiry unknown. Only Singapore + International scope. Typically 1M tokens per eligible model; actual amount unknown. Input/output share one total. Separate model/snapshot quotas cannot be transferred.
- **Regional regular API** (payg): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: None currency. Reset/Verfall nicht konkret angegeben. Regular regional API can bill after quota exhaustion/expiry unless effective provider-side Free Quota Only protection stops it.

- [New-user free quota, updated 16 October 2026](https://www.alibabacloud.com/help/en/model-studio/new-free-quota) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Regions/endpoints, updated 13 October 2026](https://www.alibabacloud.com/help/en/model-studio/regions) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Qwen API quick start](https://www.alibabacloud.com/help/en/model-studio/getting-started/first-api-call-to-qwen) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Coding Plan usage policy](https://www.alibabacloud.com/help/en/model-studio/coding-plan) — Bestandsprüfzeit 2026-10-23T22:13:08Z.

### Cloudflare Workers AI (`cloudflare-workers-ai`)

Source: `src/Lain.Host/Providers/Catalog/cloudflare-workers-ai.json`. Adapter `prepared`, Live `not_tested`, Quota `manual`.

- **Workers Free · daily allocation** (free): kein verifizierter Festpreis im Manifest; Pool `cloudflare-account`; enthalten: 10000 neurons. Daily at 00:00 UTC Neuron compute units are not tokens. Free plan stops at its allocation; certain models require paid billing.
- **Workers Paid / PAYG inference** (payg): kein verifizierter Festpreis im Manifest; Pool `cloudflare-account`; enthalten: 10000 neurons. Daily at 00:00 UTC Usage above the included allocation is billed. Prepaid AI Gateway credits are a separate billing path, not a free fallback.

- [Neurons, free allocation and paid-only models](https://developers.cloudflare.com/workers-ai/platform/pricing/) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Account ID and API token direct REST setup](https://developers.cloudflare.com/workers-ai/get-started/rest-api/) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [OpenAI-compatible account endpoint](https://developers.cloudflare.com/workers-ai/configuration/open-ai-compatibility/) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Free-access models and paid-plan change](https://developers.cloudflare.com/changelog/post/2026-07-28-models-require-workers-paid/) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Recent paid-only additions](https://developers.cloudflare.com/changelog/product/workers-ai/) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.

### Cohere (`cohere`)

Source: `src/Lain.Host/Providers/Catalog/cohere.json`. Adapter `prepared`, Live `not_tested`, Quota `unsupported`.

- **Cohere evaluation** (trial): kein verifizierter Festpreis im Manifest; Pool `evaluation-key`; enthalten: 1000 requests. Reset/Verfall nicht konkret angegeben. Published trial ceiling: 1,000 calls/month; remaining calls and exact reset are unknown. Newer Chat variants can retain trial limits on production keys. No paid upgrade or production approval is performed. Shared organization pooling is not qualified; do not aggregate key limits into an organization allowance.

- [Official Cohere OpenAI compatibility contract](https://docs.cohere.com/docs/compatibility-api) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Evaluation and production key limits](https://docs.cohere.com/v2/docs/rate-limits) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Production approval and agreements](https://docs.cohere.com/v2/docs/going-live) — Bestandsprüfzeit 2026-10-24T15:47:23Z.

### DeepSeek (`deepseek`)

Source: `src/Lain.Host/Providers/Catalog/deepseek.json`. Adapter `available`, Live `not_tested`, Quota `partial`.

- **API account balance** (payg): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: None currency. Reset/Verfall nicht konkret angegeben. Actual CNY and USD available balances are read separately. No fixed grant, spending limit, reset, conversion or recurring allowance is assumed.

- [DeepSeek API quick start](https://api-docs.deepseek.com/) — Bestandsprüfzeit 2026-10-26T00:15:39Z.
- [DeepSeek Chat Completions API](https://api-docs.deepseek.com/api/create-chat-completion) — Bestandsprüfzeit 2026-10-26T00:15:39Z.
- [DeepSeek Lists Models](https://api-docs.deepseek.com/api/list-models) — Bestandsprüfzeit 2026-10-26T00:15:39Z.
- [DeepSeek Get User Balance](https://api-docs.deepseek.com/api/get-user-balance) — Bestandsprüfzeit 2026-10-26T00:15:39Z.

### Fireworks (`fireworks`)

Source: `src/Lain.Host/Providers/Catalog/fireworks.json`. Adapter `available`, Live `not_tested`, Quota `partial`.

- **Starter credit (if granted)** (trial): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: unbekannt USD. Reset/Verfall nicht konkret angegeben. The FAQ describes $1 starter credit. Actual allocation, balance and expiry are unverified; this is not a recurring free tier.
- **Serverless pay as you go** (payg): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: unbekannt USD. Reset/Verfall nicht konkret angegeben. Uses account credits or the existing billing arrangement. No purchase, Auto Reload or paid fallback is enabled by lain.

- [Serverless quickstart and API key setup](https://docs.fireworks.ai/getting-started/quickstart) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [OpenAI-compatible chat and streaming usage](https://docs.fireworks.ai/tools-sdks/openai-compatibility) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Official account quota list API](https://docs.fireworks.ai/api-reference/list-quotas) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Official quota schema](https://docs.fireworks.ai/api-reference/get-quota) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Quota types and monthly spend scope](https://docs.fireworks.ai/guides/quotas_usage/account-quotas) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Account roles](https://docs.fireworks.ai/accounts/users) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Starter credit and service suspension](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit) — Bestandsprüfzeit 2026-10-24T00:00:00Z.

### Google Gemini API (`gemini-api`)

Source: `src/Lain.Host/Providers/Catalog/gemini-api.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **Free Tier** (free): kein verifizierter Festpreis im Manifest; Pool `project`; enthalten: None tokens. Reset/Verfall nicht konkret angegeben. Only eligible models/projects. Actual remaining quota unknown; standard inference Free Tier is documented, not a live entitlement check.
- **Paid API usage** (payg): kein verifizierter Festpreis im Manifest; Pool `project`; enthalten: None tokens. Reset/Verfall nicht konkret angegeben. Requires configured API billing. Same endpoint/model IDs may incur charges; do not infer free-only eligibility.

- [Official OpenAI compatibility](https://ai.google.dev/gemini-api/docs/openai) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Project rate limits](https://ai.google.dev/gemini-api/docs/rate-limits) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Free Tier and paid API categories](https://ai.google.dev/gemini-api/docs/pricing) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Current models and access](https://ai.google.dev/gemini-api/docs/models) — Bestandsprüfzeit 2026-10-23T22:12:49Z.

### Groq (`groq`)

Source: `src/Lain.Host/Providers/Catalog/groq.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **Groq Free tier** (free): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: None tokens. Reset/Verfall nicht konkret angegeben. No fixed quota amount embedded. Promotional credits are separate optional offers.
- **Groq Developer API** (payg): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: None tokens. Reset/Verfall nicht konkret angegeben. PAYG after explicit upgrade/payment configuration. Not an unlimited subscription.

- [Official OpenAI-compatible API](https://console.groq.com/docs/openai) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Current hosted model matrix](https://console.groq.com/docs/models) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Organization rate limits and observed response headers](https://console.groq.com/docs/rate-limits) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Free versus Developer PAYG](https://console.groq.com/docs/billing-faqs) — Bestandsprüfzeit 2026-10-23T22:12:49Z.

### Inception Mercury (`inception`)

Source: `src/Lain.Host/Providers/Catalog/inception.json`. Adapter `prepared`, Live `not_tested`, Quota `unsupported`.

- **New-account trial** (trial): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: None tokens. One-time grant; no recurring reset. Expiry is not stated. Official offer: 100 million initial tokens. Eligibility, actual grant, remaining balance and expiry are not observed. No payment details required.
- **Paid API usage** (payg): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: None currency. Reset/Verfall nicht konkret angegeben. Requires separately configured billing; promotional prices are not fixed in lain.

- [Inception quick start](https://docs.inceptionlabs.ai/get-started/get-started.md) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Inception models](https://docs.inceptionlabs.ai/get-started/models.md) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Mercury 3 launch, 19 October 2026](https://www.inceptionlabs.ai/blog/introducing-mercury-3) — Bestandsprüfzeit 2026-10-23T22:13:08Z.

### Kilo Gateway (`kilo`)

Source: `src/Lain.Host/Providers/Catalog/kilo.json`. Adapter `prepared`, Live `not_tested`, Quota `unsupported`.

- **Explicit Kilo free models** (free): kein verifizierter Festpreis im Manifest; Pool `ip`; enthalten: None requests. Reset/Verfall nicht konkret angegeben. Only explicitly pinned concrete free models are prepared. Account credit, current model availability and request rate limits remain separate; a public zero-price observation does not prove account usability.

- [Official Kilo OpenAI-compatible gateway](https://kilo.ai/docs/gateway/api-reference) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Kilo model routing and free availability](https://kilo.ai/docs/gateway/models-and-providers) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Personal key and organization authentication](https://kilo.ai/docs/gateway/authentication) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Free-model billing and account limits](https://kilo.ai/docs/gateway/usage-and-billing) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Get the personal Gateway API key](https://kilo.ai/docs/getting-started/setup-authentication) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Kilo exact Gemma free model catalog entry](https://kilo.ai/models/google-gemma-4-26b-a4b-it-free) — Bestandsprüfzeit 2026-10-24T15:47:23Z.
- [Kilo Gateway public models metadata](https://ai-gateway.kilo.ai/api/v1/models) — Bestandsprüfzeit 2026-10-25T01:53:55.931571+00:00.

### Mistral API (`mistral`)

Source: `src/Lain.Host/Providers/Catalog/mistral.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **Mistral Free mode** (free): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: None included_usage. Monthly billing period; actual reset instant unknown. Included monthly usage shared across Studio/API/Vibe Code. Amount/unit not verified; do not translate into token credits.
- **Mistral paid plan included usage** (subscription): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: None included_usage. Monthly billing period; actual reset instant unknown. Current docs list Pro/Education/Team/Enterprise. Actual plan entitlement must be verified; included monthly usage shared across Studio/API/Vibe Code.
- **Optional prepaid PAYG overflow** (payg): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: None billing_currency. Reset/Verfall nicht konkret angegeben. Requires explicit enabling and prepaid credits. Consumed after included monthly usage; not an independent API plan.

- [Free mode and API key setup](https://docs.mistral.ai/getting-started/quickstarts/studio/activate-and-generate-api-key) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Official chat completions API](https://docs.mistral.ai/api/endpoint/chat) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Mistral Small 4 concrete model](https://docs.mistral.ai/models/mistral-small-4-0-26-03) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Global plans, shared included monthly usage and optional PAYG](https://docs.mistral.ai/admin/billing-usage/subscriptions) — Bestandsprüfzeit 2026-10-23T22:12:49Z.
- [Organization and workspace limits](https://docs.mistral.ai/admin/billing-usage/usage-limits) — Bestandsprüfzeit 2026-10-23T22:12:49Z.

### NanoGPT (`nanogpt`)

Source: `src/Lain.Host/Providers/Catalog/nanogpt.json`. Adapter `prepared`, Live `not_tested`, Quota `api`.

- **Existing account billing settings** (unknown): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: unbekannt unknown. Reset/Verfall nicht konkret angegeben. Subscription tier, price, included limits and coverage are unknown. A public model listing is not proof of inclusion. No purchase or paid fallback is enabled by setup.

- [Public shortcut OAuth PKCE](https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce) — Bestandsprüfzeit 2026-10-26T00:16:29.507951+00:00.
- [Separate usage:read management tokens](https://docs.nano-gpt.com/api-reference/management-api) — Bestandsprüfzeit 2026-10-26T00:16:29.507951+00:00.
- [Public model catalog (unauthenticated snapshot)](https://api.nano-gpt.com/api/v1/models) — Bestandsprüfzeit 2026-10-26T00:16:29.507951+00:00.

### Nebius Token Factory (`nebius`)

Source: `src/Lain.Host/Providers/Catalog/nebius.json`. Adapter `prepared`, Live `not_tested`, Quota `unsupported`.

- **Normal signup trial** (trial): kein verifizierter Festpreis im Manifest; Pool `billing account`; enthalten: None currency. One-time credit, valid 30 days; actual start/expiry unknown. Official offer is $1 trial credit valid for 30 days. A billing account and bank card are required. Actual grant/balance unknown.
- **Optional Builder membership offer** (trial): kein verifizierter Festpreis im Manifest; Pool `eligible program member / billing account`; enthalten: None currency. No recurring reset or verified expiry. Separate compute grants require application. Optional program currently advertises $25 Token Factory credit subject to eligibility, availability and terms. Joining/claiming is separate. Not guaranteed or added to balance; expiry unknown.
- **Token Factory PAYG** (payg): kein verifizierter Festpreis im Manifest; Pool `billing account`; enthalten: None currency. Reset/Verfall nicht konkret angegeben. Paid inference; automatic card charges can occur. Retrieve current model pricing rather than using example rates.

- [Billing & Consumption](https://docs.tokenfactory.nebius.com/other-capabilities/billing-new.md) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [AI Builder Program](https://nebius.com/builders) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Token Factory quick start](https://docs.tokenfactory.nebius.com/quickstart.md) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Verbose models discovery](https://docs.tokenfactory.nebius.com/api-reference/examples/list-of-models.md) — Bestandsprüfzeit 2026-10-23T22:13:08Z.
- [Dynamic rate limits](https://docs.tokenfactory.nebius.com/ai-models-inference/rate-limits.md) — Bestandsprüfzeit 2026-10-23T22:13:08Z.

### NVIDIA Build (`nvidia-build`)

Source: `src/Lain.Host/Providers/Catalog/nvidia-build.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **Developer prototyping access** (free): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: unbekannt unknown. Reset/Verfall nicht konkret angegeben. Official free API access for prototyping; not an unlimited allowance. Exact account limits, expiry and remaining budget unknown. The separate 90-day Enterprise evaluation and self-hosted GPU allowance are not hosted API token credits.

- [NVIDIA Build Step 5 Preview](https://build.nvidia.com/stepfun-ai/step-5-preview) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [NVIDIA Step 5 Preview inference API](https://docs.api.nvidia.com/nim/reference/stepfun-ai-step-5-preview-infer) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [NVIDIA NIM product FAQ](https://docs.api.nvidia.com/nim/docs/product) — Bestandsprüfzeit 2026-10-23T22:13:05Z.

### OpenCode Go / Go Plus (`opencode-go`)

Source: `src/Lain.Host/Providers/Catalog/opencode-go.json`. Adapter `prepared`, Live `not_tested`, Quota `unsupported`.

- **Go** (subscription): 10 USD/month; Pool `workspace_plan`; enthalten: unbekannt weighted_plan_allowance. 5-hour, weekly and monthly constraints. Current usage and exact resets are not verified by an API. Single-member workspace subscription. Model-specific accounting references must not be added into an account dollar total. Current documentation does not publish a machine-readable cross-model aggregation formula.
- **Go Plus** (subscription): 40 USD/month; Pool `workspace_plan`; enthalten: unbekannt weighted_plan_allowance. 5-hour, weekly and monthly constraints. Current usage and exact resets are not verified by an API. Single-member workspace subscription. Model-specific accounting references must not be added into an account dollar total. Current documentation does not publish a machine-readable cross-model aggregation formula.

- [OpenCode Go official plans, limits and endpoint table](https://opencode.ai/v2/docs/console/go/) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [OpenCode API keys](https://opencode.ai/v2/docs/console/api/) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [OpenCode inference protocols](https://opencode.ai/v2/docs/console/api/inference/) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [OpenCode Go public model list](https://opencode.ai/inference/go/v1/models) — Bestandsprüfzeit 2026-10-24T00:00:00Z.

### OpenRouter (`openrouter`)

Source: `src/Lain.Host/Providers/Catalog/openrouter.json`. Adapter `prepared`, Live `not_tested`, Quota `read_only_api`.

- **Free model request allowance** (free): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: 50 requests. Daily account request window; per-minute constraint also applies. Documented daily ceiling becomes 1,000 after at least USD 10 in all-time purchased credits; no purchase is performed. The current API counter, not is_free_tier, determines the observed ceiling and remaining requests. Failed attempts count.
- **Prepaid PAYG** (payg): kein verifizierter Festpreis im Manifest; Pool `billing-account`; enthalten: unbekannt usd. Reset/Verfall nicht konkret angegeben. Model-specific prices and purchase fees apply. The key spend cap is not the account credit balance.

- [OpenRouter pricing](https://openrouter.ai/pricing) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Free catalog variants](https://openrouter.ai/docs/guides/routing/model-variants/free) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Random free router](https://openrouter.ai/docs/guides/routing/routers/free-router) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Current API key](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-key) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Credit, budget and request limits](https://openrouter.ai/docs/api_reference/limits) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Endpoint price caps and provider fallback controls](https://openrouter.ai/docs/guides/routing/provider-selection) — Bestandsprüfzeit 2026-10-23T22:31:43.946962+00:00.
- [Default plugin settings and enforced overrides](https://openrouter.ai/docs/guides/features/plugins) — Bestandsprüfzeit 2026-10-23T22:31:43.946962+00:00.
- [Hard per-key spending caps and BYOK boundaries](https://openrouter.ai/blog/insights/governing-team-ai-spend/) — Bestandsprüfzeit 2026-10-23T22:31:43.946962+00:00.
- [OpenRouter public models metadata](https://openrouter.ai/api/v1/models) — Bestandsprüfzeit 2026-10-25T01:53:12.609439+00:00.

### Poe (`poe`)

Source: `src/Lain.Host/Providers/Catalog/poe.json`. Adapter `prepared`, Live `not_tested`, Quota `available`.

- **Poe subscription points** (subscription): kein verifizierter Festpreis im Manifest; Pool `user_account`; enthalten: None points. Allocation and reset depend on the account plan; unavailable in the balance response. Subscription and add-on points share the same reported balance. Plan price and point allocation are unknown without account evidence.
- **Poe add-on points** (payg): kein verifizierter Festpreis im Manifest; Pool `user_account`; enthalten: unbekannt points. Reset/Verfall nicht konkret angegeben. One-time purchased points; not a free trial. Balance response does not separate their amount or expiry from plan points.

- [Poe external applications](https://creator.poe.com/docs/external-applications/external-application-guide) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Poe OpenAI compatibility](https://creator.poe.com/docs/external-applications/openai-compatible-api) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Sign in with Poe (OAuth)](https://creator.poe.com/docs/external-applications/sign-in-with-poe) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Poe current balance](https://creator.poe.com/api-reference/getCurrentBalance) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Poe usage history](https://creator.poe.com/api-reference/getPointsHistory) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Poe model list](https://creator.poe.com/api-reference/listModels) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Poe live public model metadata](https://api.poe.com/v1/models) — Bestandsprüfzeit 2026-10-24T00:00:00Z.

### Requesty (`requesty`)

Source: `src/Lain.Host/Providers/Catalog/requesty.json`. Adapter `prepared`, Live `not_tested`, Quota `manual`.

- **Free models · organization pool** (free): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: 200 requests. Daily; exact reset timestamp unknown until observed. Shared across all free models. Paying organizations: 1,000/day and 60/minute; this does not establish the user organization tier or its remaining requests.
- **PAYG** (payg): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: unbekannt usd. Reset/Verfall nicht konkret angegeben. Separate from free allowance; upstream pricing plus gateway markup. No current credit-balance endpoint qualified.

- [Requesty quickstart](https://docs.requesty.ai/quickstart) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Free models and organization limits](https://docs.requesty.ai/features/free-models) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Current model catalog](https://docs.requesty.ai/features/supported-models) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Fallback policy references](https://docs.requesty.ai/features/fallback-policies) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.
- [Requesty terms](https://www.requesty.ai/terms) — Bestandsprüfzeit 2026-10-23T22:13:17.697734+00:00.

### SambaNova (`sambanova`)

Source: `src/Lain.Host/Providers/Catalog/sambanova.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **SambaCloud Free Tier** (free): kein verifizierter Festpreis im Manifest; Pool `user`; enthalten: None unknown. Reset/Verfall nicht konkret angegeben. No linked payment method. Published per-model rate limits are offer limits, not observed remaining quota. Individual account/tier unverified.
- **SambaCloud Developer Tier** (payg): kein verifizierter Festpreis im Manifest; Pool `user`; enthalten: None unknown. Reset/Verfall nicht konkret angegeben. A linked payment method changes the tier. Not a free overflow route; no purchase or upgrade is performed.

- [SambaCloud keys and exact API URLs](https://docs.sambanova.ai/docs/en/get-started/api-keys-urls) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Current SambaCloud model IDs and context](https://docs.sambanova.ai/docs/en/models/sambacloud-models) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Model-specific function calling](https://docs.sambanova.ai/docs/en/features/function-calling) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Streaming Chat example with usage](https://docs.sambanova.ai/docs/en/get-started/quickstart) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Free versus Developer tiers and rate headers](https://docs.sambanova.ai/docs/en/models/rate-limits) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [SambaCloud terms](https://sambanova.ai/cloud-end-user-license-agreement) — Bestandsprüfzeit 2026-10-24T15:49:15Z.

### StepFun (`stepfun`)

Source: `src/Lain.Host/Providers/Catalog/stepfun.json`. Adapter `available`, Live `not_tested`, Quota `partial`.

- **Pay as you go API** (payg): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: unbekannt CNY. Reset/Verfall nicht konkret angegeben. Separate prepaid/postpaid account balance. Actual tier depends on cumulative cash recharge and account type; vouchers do not determine tier. No free entitlement inferred.
- **Step Plan Mini** (subscription): 49 CNY/month; Pool `account`; enthalten: 400000000 credits. Allocated monthly; unused credits expire at period end without rollover. Actual account reset unknown. Offer, not an observed account budget. Quarter/year prepayment still allocates monthly. Step 5 peak consumption doubles on weekdays 09–12 and 14–18 Beijing. Add-ons expire independently after 30 days. Legacy subscriptions may differ.
- **Step Plan Plus** (subscription): 99 CNY/month; Pool `account`; enthalten: 1600000000 credits. Allocated monthly; unused credits expire at period end without rollover. Actual account reset unknown. Offer, not an observed account budget. Quarter/year prepayment still allocates monthly. Step 5 peak consumption doubles on weekdays 09–12 and 14–18 Beijing. Add-ons expire independently after 30 days. Legacy subscriptions may differ.
- **Step Plan Pro** (subscription): 199 CNY/month; Pool `account`; enthalten: 8000000000 credits. Allocated monthly; unused credits expire at period end without rollover. Actual account reset unknown. Offer, not an observed account budget. Quarter/year prepayment still allocates monthly. Step 5 peak consumption doubles on weekdays 09–12 and 14–18 Beijing. Add-ons expire independently after 30 days. Legacy subscriptions may differ.
- **Step Plan Max** (subscription): 699 CNY/month; Pool `account`; enthalten: 40000000000 credits. Allocated monthly; unused credits expire at period end without rollover. Actual account reset unknown. Offer, not an observed account budget. Quarter/year prepayment still allocates monthly. Step 5 peak consumption doubles on weekdays 09–12 and 14–18 Beijing. Add-ons expire independently after 30 days. Legacy subscriptions may differ.

- [Step 5 Preview model specifications](https://platform.stepfun.com/docs/zh/guides/models/step-5-preview.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [StepFun PAYG quickstart](https://platform.stepfun.com/docs/zh/quickstart/overview.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [Step Plan reasoning API](https://platform.stepfun.com/docs/zh/step-plan/integrations/reasoning-api.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [Step Plan pricing](https://platform.stepfun.com/docs/zh/step-plan/pricing.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [StepFun account balance API](https://platform.stepfun.com/docs/zh/api-reference/accounts/get.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [Step Plan paid-service agreement, effective 2026-05-25](https://platform.stepfun.com/docs/zh/agreement/step-plan.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [Step Plan supported tool integrations](https://platform.stepfun.com/docs/zh/step-plan/tool-integrations.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.
- [StepFun PAYG rate limits](https://platform.stepfun.com/docs/zh/guides/pricing/rate-limits.md) — Bestandsprüfzeit 2026-10-23T22:13:05Z.

### Synthetic (`synthetic`)

Source: `src/Lain.Host/Providers/Catalog/synthetic.json`. Adapter `available`, Live `not_tested`, Quota `available`.

- **Go** (subscription): 9 USD/month; Pool `account`; enthalten: unbekannt credits. Gemeinsamer Pool; Kredite regenerieren über die Woche. Modellgewichte unterscheiden sich; keine festen Tokenbudgets. Extra PAYG separat konfigurieren.
- **Standard** (subscription): 30 USD/month; Pool `account`; enthalten: unbekannt credits. Gemeinsamer Pool; Kredite regenerieren über die Woche. Modellgewichte unterscheiden sich; keine festen Tokenbudgets. Extra PAYG separat konfigurieren.
- **Big** (subscription): 90 USD/month; Pool `account`; enthalten: unbekannt credits. Gemeinsamer Pool; Kredite regenerieren über die Woche. Modellgewichte unterscheiden sich; keine festen Tokenbudgets. Extra PAYG separat konfigurieren.
- **API nach Verbrauch** (payg): kein verifizierter Festpreis im Manifest; Pool `account`; enthalten: unbekannt USD. Reset/Verfall nicht konkret angegeben. 

- [Tarife und gemeinsame Modellgewichte](https://synthetic.new/pricing) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Offizielle API-Basen](https://dev.synthetic.new/docs/api/overview) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Modellliste und Kontext](https://dev.synthetic.new/docs/api/models) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Tools, Reasoning, SSE und JSON-Parameter](https://dev.synthetic.new/docs/openai/chat-completions) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Offizieller Quota-Vertrag](https://dev.synthetic.new/docs/synthetic/quotas) — Bestandsprüfzeit 2026-10-24T00:00:00Z.

### Upstage (`upstage`)

Source: `src/Lain.Host/Providers/Catalog/upstage.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **Upstage introductory credits** (trial): kein verifizierter Festpreis im Manifest; Pool `organization`; enthalten: None unknown. Reset/Verfall nicht konkret angegeben. Official signup offer advertises $10. Granted amount, current balance, expiry and card requirement are unverified; no recurring free API allowance is claimed.

- [Solar Mini 4 model and dated snapshot](https://console.upstage.ai/docs/models/solar-mini-4) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Upstage Chat API and supported parameters](https://console.upstage.ai/api/chat) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Solar model tool-calling guide](https://console.upstage.ai/docs/capabilities/generate/tool-calling) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Private key setup and introductory credit offer](https://console.upstage.ai/docs/getting-started) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Organization keys, credits and payment fallback](https://console.upstage.ai/docs/resources/faq) — Bestandsprüfzeit 2026-10-24T15:49:15Z.
- [Tier-dependent rate limits](https://console.upstage.ai/docs/guides/rate-limits) — Bestandsprüfzeit 2026-10-24T15:49:15Z.

### xAI / Grok (`xai`)

Source: `src/Lain.Host/Providers/Catalog/xai.json`. Adapter `available`, Live `not_tested`, Quota `unsupported`.

- **API nach Verbrauch** (payg): kein verifizierter Festpreis im Manifest; Pool `team`; enthalten: unbekannt USD. Reset/Verfall nicht konkret angegeben. Kein dauerhaftes Gratisbudget bestätigt; Preise und Key-Limits modellabhängig.

- [Inferenz-Key und API-Credits](https://docs.x.ai/developers/quickstart.md) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Grok 4.7 Fähigkeiten und Kontext](https://docs.x.ai/developers/models/grok-4.7.md) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Reasoning und verschlüsselte Fortsetzung](https://docs.x.ai/developers/model-capabilities/text/reasoning.md) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [SSE-Streaming](https://docs.x.ai/developers/model-capabilities/text/streaming.md) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Team-Tiers und Key-Limits](https://docs.x.ai/developers/rate-limits.md) — Bestandsprüfzeit 2026-10-24T00:00:00Z.
- [Getrennter Management-Key](https://docs.x.ai/developers/management-api-guide.md) — Bestandsprüfzeit 2026-10-24T00:00:00Z.

### Zusätzliche Verbindungsmethoden ohne eigenes Manifest

| Provider | Offizielle Produkt-/Preisquelle zur noch offenen Nachprüfung | Bereits vorliegende technische Grenze |
| --- | --- | --- |
| Codex | https://learn.chatgpt.com/docs/pricing ; https://developers.openai.com/codex/ | Gateway/Standalone/Snapshot nicht gleichwertig; keine direkte OpenAI-API-Flatrate ableiten. |
| Command Code | https://commandcode.ai/pricing ; https://commandcode.ai/docs | API-Key verfügbar; allgemeine CLI nicht qualifiziert. |
| Kimi | https://www.kimi.com/code/docs/ ; https://www.kimi.com/ | Mitgliedschaft, Region und Codeberechtigung separat; Device nur Monitoring. |
| Z.ai | https://z.ai/subscribe ; https://docs.z.ai/devpack/overview | Codingendpoint getrennt von allgemeiner API; Managedlogin gesperrt. |
| Xiaomi MiMo | https://platform.xiaomimimo.com/ ; https://mimo.mi.com/docs/en-US/quick-start/faq/promotions | CN/EU-Bindung und Planumfang unbestätigt; browsergebundener Monitor. |

## Recherchegrenzen und verbleibende Entscheidung

Der angeforderte aktuelle Primärquellenvergleich ist wegen Webtransport-/Browser-/DNS-Ausfall **unvollständig**. Keine scheinpräzisen heutigen Preise für Z.ai, Kimi, Kilo Pass, Poe, Mistral oder MiMo einsetzen. Auch NanoGPT $12 und die zwei priorisierten Kandidaten müssen erneut offiziell gelesen werden, bevor daraus eine Kaufempfehlung wird. Aus alten Referralaktionen keine heutige Preisaktion ableiten. Kimi-Guthabenende 31.12.2026 und MiMo-40-Tage-Credits sind Referralverfall, keine bestätigten Abo-Promotionenden.

Die konkrete Restrecherche ist eng: zuerst Synthetic-Preis/Go-Creditpool, OpenCode-Go-Preis/Entitlements, dann Z.ai- und Kimi-Monatspreis inklusive Renewal/Aktionsende, NanoGPT-Neuabo/API-Abdeckung und Kilo-Pass-Creditmechanik. Jeweils DE-Steuer-/Regionsangabe, monatlich gegen jährlich und erlaubte persönliche Coding-/Workerlast prüfen. Kostenlose Zugänge zuerst über offizielle Karten-/Verfalls-/Abschaltregeln absichern. Dafür braucht die öffentliche Quellenprüfung keinen Accountzugriff. Eine bestehende persönliche Berechtigung oder ein Live-Qualifikationsergebnis wird hier nicht behauptet.

Erstellt wurden ausschließlich dieser Bericht und `docs/provider-recommendations-evidence-2026-10-26.json`; keine Änderungen an Code, Konfiguration, alten Recherchedateien oder Accounts.
