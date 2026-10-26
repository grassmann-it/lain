# Lain 1.0 – Prüfung der tatsächlichen Paketnotices

Stand 26.10.2026, Sourcecommit `5fab859b8f603b7f7ce430d0d2fff748b05bd150`.
Gegenstand sind die beiden `package-{monitor,gateway}.tar.gz` und entpackten
Pakete unter `/home/tomka/.cache/lain-quota-release-20261026/`, keine historischen
r4-Archive. Hashes und Betriebsanleitung: [Lieferform](release-1.0-distribution-readiness.md).
Eigene Prüfartefakte: `/home/tomka/.cache/lain-notice-review-20261026/`.
Keine Credentials-/DB-Lektüre, Produktivdienstmutation, Builds oder Veröffentlichung.

## Nachtrag: konkrete Textlücken für r3 geschlossen

Die Folgearbeit hat die unten historisch festgestellten Textlücken in Main
konkret geschlossen: neue Texte, exakte NuGet-/Quellhashbindungen, sechs
bytegenau zugeordnete Interfonts und rekursive h1-geprüfte Go-Notices.
[Zusatzinventar und Abschlussbericht](../packaging/licenses/review-20261026.md)
sind der aktuelle Abschluss dieser Noticekorrektur. Parent übernimmt die
Packaginginputs in r3 und baut neu; die geprüften 5fab-Archive bleiben erhalten.
Die Tabellen unten dokumentieren weiterhin deren ursprünglichen Bestand.

Die vollständigen offiziellen Skia-/HarfBuzz-Sammelnotices sind vorhandene
Belege. Offizielle Buildtargets ordnen libmicrohttpd dem Werkzeug skiaserve zu;
keine unerfüllte LGPL-Bedingung für die gelieferten Bibliotheken ist belegt.
Fehlende individuelle Compilefeaturelisten begründen keinen neuen Blocker.
Native SQLite ist dem offiziellen NuGet-Asset bytegenau zugeordnet, mit
Public-Domain-Erklärung im offiziellen NOTICE und separater Apache-Wrapperlizenz.
Aus dieser konkret begrenzten Noticeprüfung bleibt für die korrigierten
Packaginginputs keine nachvollziehbar betroffene unerfüllte Bedingung offen.
Dies erklärt die unveränderten alten Pakete nicht rückwirkend für ergänzt.

## Methode und Aussagegrenze

`audit.py` prüft jeden Eintrag von `SHA256SUMS` gegen die entpackten Bytes
und die regulären Dateien im tatsächlichen Tararchiv; das Sourcearchiv wird
nach Entfernung seines `lain-source/`-Präfixes gegen `git archive HEAD` des
benannten eingefrorenen Commits verglichen. Der erste Prüflauf verwendete
abweichende Pfadpräfixe; korrigierte Läufe normalisieren `./`, Paketordner
und Sourceordner. Ein solcher Harnessfehler ist kein Paketfehler.
Ergebnisse stehen in `audit.json`: Monitor 907 manifestierte Dateien / 908
reguläre Archivdateien, Gateway 1.056 / 1.057 (jeweils zusätzlich `SHA256SUMS`),
keine unerwarteten regulären Archivdateien. Beide Sourcearchive stimmen mit
1.493 regulären Gitarchiv-Dateien überein; Sourcearchiv-SHA-256:
`ec3d8cd306be8aa4621dded18518f607b8f55850e2c14f8844e8a991c4191a77`. Große Hash-/Dekompressionsläufe erfolgen
seriell unter der vorgegebenen flock-/systemd-Ressourcenbegrenzung.

Zusätzlich gelesen: beide Root-Lizenz-/Noticetexte, Paket-/Buildmetadaten,
NuGet- und Go-Inventare, originale nuspecs, native Sammelnotices, Fontnotices
und ausgewählte verschachtelte Lizenzen der exakt versionierten lokalen
Go-Modulquellen. Keine bloße Auswertung der Lizenzlabels als Compliancefreigabe.
Go-Inventar: 59 verlinkte Module, `missingEvidence: []`; dessen Scope umfasst
Rootnotices und ist keine rekursive Prüfung aller Quelldatei-Lizenzen.
Das NuGet-Inventar enthält konservativ auch andere Plattformen und Buildtools;
deren Vorkommen beweist keine Auslieferung eines Windows-/macOS-Binaries.

## Vorhandene Belege

| Anteil | Tatsächlicher Paketbeleg und Bewertung |
| --- | --- |
| Eigene Source/Skills/Dokumentation | Root `LICENSE` enthält Artistic-2.0; `NOTICE` trennt Drittanteile. `source/lain-source.tar.gz` enthält die exakte getrackte Revision, einschließlich Buildskripten, Locks, eigenen Adaptern und Patches. Kein vollständiger Offline-Abhängigkeitscache. |
| Foto/Logo | `NOTICE`: Alexander Huxel · Webentwicklung Huxel, `https://webentwicklung-huxel.de/`, `https://flickr.com/people/204852984@N05/`. Bestätigte öffentliche Nutzung für Lain einschließlich bearbeiteter Variante; keine allgemeine Artistic-Lizenz des Fotos. |
| .NET/ASP.NET Core | Tatsächliche Runtime-/Hostpakete 10.0.11, SDK 10.0.111. Lizenz und Third-Party-Notices in `notices/nuget/Microsoft.*Runtime.linux-x64/10.0.11/` und `notices/official/`; Hostpaket ebenfalls mit Texten. Microsoft.Data.Sqlite 10.0.12 ist kein Nachweis einer Runtime 10.0.12. |
| Avalonia/Semi/Ursa/Irihi | Avalonia 12.1.4, Semi 12.1.0.1, Ursa 2.2.0, Shared 0.5.0; gepinnte MIT-Texte unter `notices/official/`, teilweise zusätzlich in NuGet-Notices. Historischer Avalonia-11-Text ist Zusatzbestand. Er ersetzt nicht Inter-OFL. |
| Oxanium / IBM Plex Sans Condensed | Familienbezogene OFL-Texte in `notices/fonts/`; Quellfonthashes und Noticehashes in `notices/inventory.json`. |
| SkiaSharp / HarfBuzzSharp | `desktop/libSkiaSharp.so`, `desktop/libHarfBuzzSharp.so`; native Pakete 3.119.4 / 8.3.1.3 mit LICENSE und vollständigem `THIRD-PARTY-NOTICES.txt`. Sammeltext enthält u.a. HarfBuzz, Skia, FreeType, ICU, libpng, libjpeg-turbo, libwebp, zlib und weitere Anteile. Diese Texte fehlen nicht. |
| SQLitePCLRaw | Apache-2.0-Text für 2.1.12 unter `notices/official/SQLitePCLRaw-2.1.12-LICENSE.TXT`; vier NuGet-Komponenten separat inventarisiert. Native `host/libe_sqlite3.so` ist vorhanden; Wrapperlizenz ist kein Herkunftsbeleg für jede native Compileoption. |
| Gateway/Go | Nur Gateway: CLIProxyAPI 8.0.41 mit MIT-Upstreamtext, eigener Adapter-/Patchprovenienz, Buildmanifest, `go version -m`-Metadaten und Go-1.27.2-LICENSE/PATENTS. Binärhash `90dfebb27ec7fd5184b54afa06e7105c279d12858f1948d5ed1e011413d80985` im Go-Inventar. Rootnotices enthalten etwa auch YAML-NOTICE und Go-PATENTS. |

Native Sammelnotices sind absichtlich breiter als Linux-Linkumfang: Der
Skia-/HarfBuzz-Text enthält beispielsweise auch libmicrohttpd samt LGPL-Text.
Das ist weder ein fehlender Lizenztext noch der Nachweis, dass Linux-Lain diese
Bibliothek tatsächlich linkt. Für eine daraus abgeleitete Copyleftpflicht
braucht es die konkrete Zuordnung zum gelieferten Binary und den Buildfeatures.
Kein pauschales „alles MIT“ und keine pauschale LGPL-Entwarnung.

## Konkrete fehlende oder noch nicht zugeordnete Belege

| Komponente | Befund | Erforderlicher Abschluss |
| --- | --- | --- |
| **Avalonia.Fonts.Inter 12.1.4** (beide Pakete) | `desktop/Avalonia.Fonts.Inter.dll` enthält Inter-TTF-Ressourcen (u.a. Regular/Bold). NuGet-Inventar hat nur MIT-Metadaten, `availableNoticeFiles: []`; Familien-OFLs betreffen nur Oxanium/Plex. Kein zugeordneter Inter-OFL-/Urhebertext im Noticebaum gefunden. | Zum tatsächlich eingebetteten Interstand passenden OFL-1.1- und Copyrighttext aus gepinnter Avalonia-/Interquelle beschaffen, Herkunft/Hash binden und mitgeben. Framework-MIT ersetzt ihn nicht. |
| **MicroCom.Runtime 0.11.6** | nuspec nennt MIT und Copyright 2021 Nikita Tsukanov, aber kein eigener Lizenztext im Noticeinventar. | Gepinnten vollständigen MIT-/Copyrighttext zuordnen und beilegen; Microsoft-/Avalonia-Texte nicht pauschal substituieren. |
| **Tmds.DBus.Protocol 0.94.1** | nuspec-Lizenzausdruck MIT, kein verfügbarer Paketnoticetext und kein komponentenspezifischer offizieller Ersatz im Paket. | Originaltext mit Rechteinhabern aus der gepinnten Quelle ergänzen. |
| **YamlDotNet 16.3.0** | nuspec nennt MIT und Antoine Aubry and contributors; kein verfügbarer Paketnoticetext/zugeordneter offizieller Ersatz. | Vollständigen versionsbezogenen MIT-/Copyrighttext mitgeben. |
| **Microsoft.Data.Sqlite / .Core 10.0.12** | Beide ohne Paketnoticetext, vorhandene Microsoft-Sammeltexte stammen von Runtime 10.0.11. | Die konkrete 10.0.12-Quelle und gültigen Copyright-/Lizenztext zuordnen; ein fehlender eigener Dateiname allein beweist keine Pflichtverletzung, der aktuelle Nachweis ist aber nicht explizit. |
| **utls v1.8.2 / dicttls** (Gateway) | Lokale exakte Modulquelle hat `dicttls/LICENSE`, BSD-3-Clause, Copyright 2023 Gaukas Wang. Paket hat nur utls-Root-LICENSE, dort kein Gaukas-Hinweis. `u_tls_extensions.go`/`u_ech.go` importieren dicttls regulär. | Verschachtelten Text aufnehmen und hashgebunden inventarisieren; nicht durch leeres `missingEvidence` als erledigt werten. |
| **klauspost/compress v1.17.4 / zstd/internal/xxhash** (Gateway) | Exakte Quelle hat MIT-Text mit Copyright 2016 Caleb Spare in `zstd/internal/xxhash/LICENSE.txt`. Root-LICENSE enthält diesen Hinweis nicht; zstd-Decoder/-Encoder importieren xxhash. Paket kopiert nur Root-LICENSE. | Verschachtelten Text mitgeben und endgültige Linkrelevanz belegen. Modulpräsenz allein ersetzt keinen Subpackage-Linkbeleg. |
| **SkiaSharp/HarfBuzzSharp native Buildanteile** | Sammelnotices sind vorhanden; individuelle Linux-Compilefeature-/Linkzuordnung, insbesondere zu LGPL-Abschnitten, ist im Noticeinventar nicht ausgeführt. | Inzwischen offiziell zugeordnet: siehe Nachtrag und Zusatzbericht. Keine unerfüllte Copyleftbedingung belegt; fehlende individuelle Featureliste allein ist kein Releaseblocker. |
| **SQLitePCLRaw.lib.e_sqlite3 2.1.12** | Apache-Wrapperfamilientext vorhanden; konkrete SQLite-Version, Compileoptionen und eventuelle eingebettete Erweiterungen der nativen Bibliothek nicht aus dem Noticeinventar belegt. | Inzwischen dem offiziellen Native-Asset zugeordnet; SQLite-Public-Domain- und Wrappertexte liegen vor. Kein weiterer Blocker daraus, siehe Zusatzbericht. |

Weitere verschachtelte Go-Texte sind kein automatischer neuer Fehlbefund:
compress-Root-LICENSE enthält bereits Abschnitte für snappy, snapref, gzhttp
und weitere Unterpfade. `pion/webrtc/v4/test-wasm/LICENSE` betrifft einen
Testpfad; seine Anwesenheit beweist keinen Bestandteil des ausgelieferten Core.
Für sonstige Unterpfade ist die tatsächliche Link-/Source-Relevanz maßgeblich.
Ein vollständiges rekursives Dateilizenzen-Audit aller 59 Modulquellen wurde
hier nicht behauptet.

## Clientabgrenzung und Konsequenz

Die Lainarchive liefern keine modifizierten Codex-/VSIX-Binärclients.
Deren separate Runtime-/Cargo-/LGPL-/Extension-Redistributionsprüfung ist daher
kein fehlendes Noticepaket dieser beiden Lainarchive. Öffentlich lieferbar
vorbereitet sind eigene Patches, Transformer, Pins und Anleitungen im
Sourcearchiv; Originalsoftware beschafft der Nutzer selbst. Build-/Patchablauf
und lokale Qualifikationsgrenzen stehen in der Betriebsanleitung.

Ergebnis des ursprünglichen **historischen 5fab-Audits**: Die tatsächliche
Lizenz-/Sourceaufnahme war bestätigt; die oben beschriebenen konkreten
Restbelege waren zu diesem Zeitpunkt offen. Dieser erste, ausschließlich
dokumentierende Audit änderte keine Notices, Collector-Skripte, Binärpakete
oder eingefrorenen Quellen und erteilte keine pauschale Redistributionsfreigabe.

Die anschließende Noticekorrektur hat die konkreten Textlücken in Main durch
neue Notices, Collectoränderungen und gezielte Tests geschlossen; maßgeblich
sind der Nachtrag oben und der [Abschlussbericht](../packaging/licenses/review-20261026.md).
Für diese korrigierten Packaginginputs ist keine konkret belegte unerfüllte
Noticebedingung offen. Die eingefrorenen 5fab-Quellen und bereits gebauten
Archive blieben auch bei dieser Folgearbeit unverändert. Parent übernimmt die
korrigierten Inputs in r3 und bindet sie durch neu gebaute, erneut geprüfte
Archive und Hashes; die alten Archive werden nicht rückwirkend ergänzt.
