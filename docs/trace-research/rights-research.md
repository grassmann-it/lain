# Trace-Rechte: Archiv, Evaluation und eigenes Qwen-Training

Stand: 29.10.2026. Phase 0, Recherche und Architekturvorschlag. Keine Umsetzung,
Inferenz, Erfassung echter Gespräche, Datensatzdownloads oder neue Berechtigung
zur Sammlung. Gelesen: `AGENTS.md`, `.architecture-sparring/config.json`, relevanter
Projektindex und Architektur-Sparring-Skill. Bestehende Arbeitskopie enthält
fremde Änderungen; ausschließlich diese Datei gehört zu diesem Recherchepaket.

## Ergebnis für die anstehende Entscheidung

Ein optionales privates Tracearchiv ist als eigenes Vorhaben bewertbar. Daraus
folgt keine Trainingsfreigabe. Für einen späteren ersten Qwen-Versuch ist ein
isolierter Korpus aus eigenen synthetischen Aufgaben, eigenen Sandboxartefakten
und Ausgaben eines konkret geprüften lokal betriebenen Modells der engste
prüfbare Einstieg. Gehostete Antworten bleiben davon getrennt. Ein offenes
Gewichtsmodell auf einem gehosteten Endpunkt hat zusätzlich einen eigenen
Dienstvertrag; Zugang, Zahlung, Modellname und Gewichtslizenz ersetzen diesen
Nachweis nicht.

Diese Empfehlung ist ein technisches Freigabemodell mit dokumentierten
Quellenbefunden. Sie ist keine pauschale Rechtsentscheidung über sämtliche
Ausgaben, Nutzerverträge oder möglichen Trainingszwecke. Kein konkreter Korpus,
keine ausgewählte Qwen-Version und kein Trainingslauf sind damit genehmigt.

## Offizielle Quellen und ihre Reichweite

Öffentliche Seiten am 29.10.2026 gelesen; spätere Verträge können abweichen.
`main`-Links sind Recherchefundstellen und noch keine unveränderlichen
Freigabebelege. Vor tatsächlichem Export sind der genaue Versionsbezug und der
zugehörige akzeptierte Dienstvertrag zu sichern.

| Quelle | Beobachtung | Konsequenz und Beleggrenze |
| --- | --- | --- |
| [Gemini API Additional Terms](https://ai.google.dev/gemini-api/terms), wirksam 23.03.2026, Seite zuletzt aktualisiert 28.04.2026 | Verbietet die Entwicklung konkurrierender Modelle mittels der Dienste. Google beansprucht kein Eigentum an generierten Inhalten. | Eigentumsaussage ist keine Trainingsfreigabe. Für das beabsichtigte eigene Qwen werden Antworten dieser Route im vorgeschlagenen System nicht als Trainingsquelle zugelassen, solange kein passender ausdrücklicher Vertragsnachweis vorliegt. |
| Dieselbe Gemini-Quelle, Abschnitt „Grounding with Google Search“ | Beschränkt Analyse, Lernen und Training aus Grounded Results/Search Suggestions; Speicherung hat eng benannte Zweckausnahmen. | Ein allgemeines automatisches Tracearchiv darf diese Inhalte nicht einfach dauerhaft aufnehmen. Private Aufbewahrung und Evaluation brauchen eigene Prüfung; Maps ist zusätzlich separat geregelt. |
| [Google Cloud Service Specific Terms](https://cloud.google.com/terms/service-terms), Abschnitte 17–20 | Eigene Regeln für Competitive Use und Model Restrictions; genannte Distillationsausnahme betrifft Modified Google Models. Google erhebt keinen Eigentumsanspruch auf neues geistiges Eigentum in Generated Output. | Kein Beleg für Export von Gemini-Antworten zum eigenen Qwen. Cloud-/Vertex-Verträge und Developer API nicht gleichsetzen; konkrete Bestellung, Modellart und Sonderbedingungen prüfen. „Google trainiert nicht ohne Erlaubnis auf Kundendaten“ regelt Googles Nutzung, nicht unsere. |
| [Qwen3-Ankündigung](https://qwenlm.github.io/blog/qwen3/) und [Qwen/Qwen3-8B LICENSE](https://huggingface.co/Qwen/Qwen3-8B/blob/main/LICENSE) | Die Ankündigung benennt konkrete Qwen3-Modelle unter Apache 2.0; das Beispiel Qwen3-8B enthält Apache 2.0, einschließlich Änderungs- und Weitergaberegeln. | Plausibler Kandidat für lokales eigenes Training, nach Pinning des tatsächlich gewählten Artefakts. Die Gewichtslizenz erteilt keine Rechte an fremden Prompts, Quellrepos oder Werkzeugausgaben. Auch Ausgaben sind nicht allein aufgrund dieses Labels automatisch Apache-lizenziert. |
| [Qwen2.5-3B-Instruct LICENSE](https://huggingface.co/Qwen/Qwen2.5-3B-Instruct/raw/main/LICENSE), Release 19.09.2024 | Qwen Research License, nichtkommerzielle Forschung/Evaluation; kommerzielle Verwendung erfordert eigene Lizenz. | „Qwen“ und „open weights“ sind keine ausreichenden Lizenzkennungen. Größe, Release, Basismodell, Fine-Tune, Quantisierung und ggf. Adapter einzeln zuordnen. |

Zusatzbefund zur Developer API: Free-/Paid-Datennutzung und Google-Training auf
Eingaben sind von unseren Nutzungsrechten getrennte Fragen. Die oben genannten
Google-Verwendungsrestriktionen verschwinden nicht durch einen Paid-Tarif.
Verträge von Gemini App, Gemini CLI/Code Assist, OpenCode Contributor oder einem
Gateway wurden hier nicht vollständig geprüft und werden nicht aus dem Namen
„Gemini“ abgeleitet. Ebenso wurde keine Trainingsfreigabe für gehostetes Qwen,
OpenRouter, Requesty oder andere Lain-Routen festgestellt. Ungeprüft bedeutet
`unknown_not_approved`, keine Aussage, dass jede Nutzung verboten sei.

## Vier getrennte Entscheidungen pro Quelle und Zweck

Ein gemeinsames `can_use=true` wäre zu grob. Empfohlene Felder sind
`private_inspection`, `evaluation`, `training`, `export`. Jedes Feld erhält
`unknown_not_approved | approved_scoped | blocked`, Zweck, Begründung, Prüfer,
Prüfdatum, Vertrags-/Lizenzversion, Quellenbeleg und ggf. Ablaufdatum. Eine
Freigabe kann z.B. auf die lokale Anzeige bis zu einer Frist begrenzt sein.
`export` muss zusätzlich Ziel, Empfänger und Artefaktart unterscheiden:
Trainingsdatei, externer Evaluator, veröffentlichter Datensatz, Adapter oder
Modellgewichte haben unterschiedliche Folgen.

| Vorgang | Benötigter eigenständiger Nachweis |
| --- | --- |
| Private Inspektion/Archivierung | Darf dieser konkrete Inhalt für diesen Diagnosezweck gespeichert und angezeigt werden; mit welchem Umfang, Zugriff und welcher Löschfrist? |
| Evaluation | Darf er als Referenz, Testfall, Judge-Eingabe oder zum Ableiten von Scores verwendet werden? Ein externer Judge ist zusätzlich eine Weitergabe. |
| Training | Darf der konkrete Zweck SFT, Präferenzlernen, Reinforcement Learning, Distillation oder sonstige Modellverbesserung umfassen? Nicht automatisch aus dem Wort „Forschung“ ableiten. |
| Export | Darf das konkrete Artefakt mit diesen Inhalten bzw. Ableitungen an dieses Ziel ausgegeben werden; sind Attribution, Notices und weitere Pflichten erfüllt? |

Auch das Umschreiben, Zusammenfassen, Herausfiltern von Erklärungen oder Erzeugen
von Präferenzpaaren macht eine ungeklärte Quelle nicht zu eigenen synthetischen
Daten. Auswertung zur Trainingsauswahl und automatisierte Judge-Ratings gehören
in den Zweckgraph. Ein separater Holdout darf nicht später still Trainingsdaten
werden. Sichtbare Reasoningtexte werden wie andere Ausgabeinhalte auf Herkunft
und Rechte geprüft; unzugängliche interne Modellzustände sind kein
Archivierungsgegenstand und werden nicht zu extrahieren versucht.

## Herkunfts- und Freigabegraph statt eines Modell-Labels

Ein Trainingsbeispiel kann eigene Aufgabenstellung, fremden Repositorycode,
Werkzeugdefinitionen, Compilerdiagnostik, Webauszug, Modellantwort und menschliche
Korrektur enthalten. Die relevanten Ebenen werden unabhängig erfasst:

1. **Basismodell/Teacher/Student:** exakte Modell-ID, Revision und Artefakthash;
   Lizenz, Basismodellkette, Adapter und Quantisierung; Betrieb lokal oder über
   Dienst mit eigenem Vertrag. Studentrecht erlaubt keinen beliebigen Teacher.
2. **Eingaben und Kontext:** Urheberschaft bzw. konkrete Erlaubnis für Aufgabe,
   Dateien und Vorlagen. Auch ein vom Nutzer eingegebener Text kann fremdes
   Material oder vertrauliche Informationen enthalten.
3. **Generierte Inhalte:** Teacherroute, Produkt/Tarif, Erstellzeitpunkt,
   damals geltender Vertrag, etwaige Output-Regeln und übernommene Fremdinhalte.
4. **Tools/Repositories:** Runtime-/Toollizenz von der Lizenz des gelesenen
   Inhalts unterscheiden. Ein Apache-lizenziertes Tool lizenziert seine
   Webresultate nicht um; ein öffentliches Git-Repository ist kein pauschal
   freigegebener Trainingskorpus. Datei-/Datenlizenzen und Notices prüfen.
5. **Menschliche Rechte und Vertraulichkeit:** dokumentierte Erfassungsfreigabe,
   Zweck, Personenbezug, Geheimnisse, Kundendaten, erlaubte Empfänger und Löschung.
   Ein Geheimnisfilter ist keine Rechtefreigabe.

Ein abgeleiteter Datensatz behält Verweise auf alle eingegangenen Quellen und
Transformationen. Die Zweckfreigabe ergibt sich aus allen erforderlichen
Nachweisen. Bei Mischinhalten darf ein manuell isolierter, eigenständig
belegbarer Anteil neu geprüft werden; bloßes Entfernen der Herkunft ist keine
Freigabe. Speicherung von Provenienz sollte selbst sparsam sein, beispielsweise
Hash und sichere lokale Referenz statt Zugangsdaten oder ganzer Nutzereingaben.

## Minimales späteres Verfahren (Vorschlag, noch nicht implementieren)

1. **Vor Erfassung:** getrennten Opt-in für Diagnosearchiv und Datenspende zum
   Training vorsehen. Quelle und erlaubte Zwecke im voraus zuordnen. Bei
   unbekannter Speicherberechtigung Inhaltsarchivierung auslassen; technische
   Metadaten nur im belegbar erlaubten Umfang. Unbekannte Trainingsrechte können
   hingegen im bereits rechtmäßig geführten Diagnosearchiv verbleiben, ohne in
   einen Lernkorpus zu gelangen.
2. **Erstes kontrolliertes Kandidatenpaket:** eigene neu geschriebene Aufgaben,
   vollständig eigene Sandboxdateien, deterministisch überprüfbare Tests und
   lokal betriebener Teacher/Student mit gepinnten kompatiblen Lizenzen. Keine
   echten Gespräche oder gehosteten Teacherantworten beimischen. Ein Generator,
   der selbst Fremddaten als Beispiele konsumiert, braucht deren Provenienz.
3. **Kuratiertes Manifest:** Quellkette, Lizenz-/Vertragsbelege,
   Zweckentscheidungen, Transformationen, Geheimnis-/Personenprüfung,
   Qualitätsergebnis und Duplikat-/Holdout-Zuordnung. Nur explizit freigegebene
   Felder in eine neue Trainingsprojektion übernehmen; Roharchiv ist nie direkt
   die Trainingsquelle.
4. **Exportgate, fail closed:** für jedes Beispiel und jede transitive Quelle
   gültige Freigabe für Training und konkretes Exportziel verlangen. Unbekannt,
   gesperrt, abgelaufen, geänderte Modellroute, unvollständige Lizenzkette oder
   fehlender Beleg blockieren den Trainings-/Datensatzexport. Kein „force“ aus
   Diagnoseberechtigung ableiten. Reproduzierbarer Exportbeleg enthält
   Korpus-/Policy-/Quellenversionen, Datensatzhash und Ausschlussgründe.
5. **Nutzung und Widerruf:** Exportmanifest an Trainingslauf und abgeleitete
   Artefakte binden. Korrekturen/Löschanforderungen schließen betroffene Beispiele
   aus zukünftigen Exporten aus und markieren betroffene Läufe. Löschung der
   Quelldatei beweist keine Entfernung aus bereits trainierten Gewichten;
   Rücknahme/Neutraining vor Weitergabe separat entscheiden.

Als vorgeschlagene Frischepolitik: erneute Prüfung unmittelbar vor dem ersten
Trainings-/Externexport und bei Produkt-, Modell-, Vertrags- oder Zweckwechsel.
Ein beliebiges Alter in Tagen allein garantiert keine Gültigkeit. Ein späteres
Register kann eine kurze maximale Prüfperiode ergänzen, ersetzt aber die
Änderungsereignisse nicht.

## Abgrenzbare nächste Recherchepakete

| Paket | Konkretes Ergebnis | Grenze / Abschlussnachweis |
| --- | --- | --- |
| RIGHTS-01 · Exakte Routen | Für tatsächlich gewünschte Teacher-Routen Produkt, Vertrag, Account-/Tarifscope, Datum, Grounding-/Toolregeln und die vier Zweckentscheidungen dokumentieren. | Keine Familienfreigaben; bei Mehrdeutigkeit schriftliche anwendungsbezogene Klärung als ausstehend kennzeichnen. Kein Schreiben an Anbieter oder Kauf durch diesen Rechercheauftrag. |
| RIGHTS-02 · Lokales Qwen-Paar | Ein konkretes Teacher-/Studentpaar samt Basiskette, gepinnten Lizenzdateien, Redistribution/Noticepflichten und lokalem Betriebsweg auswählen und bewerten. | Noch keine Modellgewichte herunterladen oder Inferenz starten. Apache-Beispiel oben ist keine endgültige Modellwahl. |
| RIGHTS-03 · Eigene Sandbox | Quelleninventar für eigene Aufgaben/Fixtures/Tools entwerfen; später jeden tatsächlich verwendeten Inhalt nachweisen. | Selbst geschriebene Aufgaben sind erst nach Fremdanteil-/Vertraulichkeitsprüfung eligible. Keine Übernahme bestehender Gespräche. |
| RIGHTS-04 · Manifest/Exportvertrag | Schema und prüfbare Gatefälle festlegen, inklusive gemischter Quellen, unbekannter Route, abgelaufener Freigabe, Grounding, Ableitungen, Widerruf und externem Judge. | In Phase 0 lediglich Design. Implementierung/Training braucht eigenen Auftrag; ein Status ohne Beleg blockiert. |
| RIGHTS-05 · Daten- und Löschumfang | Getrennte Aufbewahrungs-, Zugriffs-, Einwilligungs- und Löschregeln für privates Archiv und kuratierten Korpus definieren. | Keine neue globale Collectionfreigabe aus optionalem Archivziel ableiten. |

Offen bleiben der konkret beabsichtigte Qwen-Einsatz (privat, geschäftlich,
Weitergabe), ausgewählte Versionen, tatsächliche Vertragsroute und Inhalte des
ersten Korpus. Sie blockieren keine weitere Architekturarbeit mit ausdrücklich
benannten Annahmen, wohl aber eine konkrete Trainings- oder Exportfreigabe.
