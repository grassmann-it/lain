# Kimi Code: eigener Device-Login und Quota-Anbindung

Stand: 25.10.2026. Implementierter Herstellervertrag; ein echter Login, eine echte
Erneuerung und numerische Live-Quota sind damit noch nicht nachgewiesen.

## Herstellerquelle und Cliententscheidung

Bezugsstand ist der öffentliche MoonshotAI-Quellbaum
[`743f140978bddaf8b6aa524765fcd578f7c22fc9`](https://github.com/MoonshotAI/kimi-code/tree/743f140978bddaf8b6aa524765fcd578f7c22fc9/packages/oauth).
Das MIT-Toolkit `@moonshot-ai/kimi-code-oauth` v0.5.0 exportiert auch `./device`.
Seine `KimiOAuthToolkitOptions` nehmen eine Hostidentität und optional eine
`flowConfig` entgegen; ohne Override verwendet das Toolkit `KIMI_CODE_FLOW_CONFIG`.
`identity.ts` verlangt die tatsächliche Plattform des aufrufenden Hosts.

Für diese Lieferung wurde die öffentliche Clientkonfiguration als vorgesehener
Toolkitvertrag akzeptiert. lain verwendet deren Client-ID
`17e5f671-d194-4dfb-9706-5516cb48c098` mit eigener Identität: `User-Agent: lain/<version>`,
`X-Msh-Platform: lain`, tatsächliche lain-Version und Betriebssystemversion sowie
eine neu erzeugte Geräte-ID je Login, die bei der Erneuerung erhalten bleibt.
Client-ID und Geräte-ID sind keine Zugangstokens. CLI-Identitäten, CLI-Speicher,
fremde Grants und fremde Registrierungen werden nicht übernommen.

„Öffentlich“ bezeichnet hier die verfügbare Herstellerquelle und öffentliche
Clientkonfiguration. `package.json` trägt `private: true`; eine npm-Veröffentlichung
oder individuelle Herstellerfreigabe für lain wird nicht behauptet. Die frühere
pauschale Zulassungs-Unklarheit in den Managed-Login-Recherchen ist für diesen
konkreten Quellvertrag durch die obige Integrationsentscheidung überholt.

Die ausschlaggebenden Dateien liegen unter `packages/oauth/src/` im genannten
Commit. SHA-256 der bei der Recherche gelesenen Bytes:

| Datei | SHA-256 |
| --- | --- |
| `constants.ts` | `030d2b874ba94b77a9b068ba502b6706addb7b90279122c8f91f5796d6205444` |
| `oauth.ts` | `a2717cc91f4c884b0310220983d798d4b6c9e2fb880b1fd184b8702e0c88e981` |
| `types.ts` | `a49b560c0570b64e58bd6c22dcd58398f37d1b47778f187bc444d86b9d087dc9` |
| `identity.ts` | `1aa8023e80de63af97a2b8c679bba4124573524d768d94a51edbfd5dc50eee00` |
| `toolkit.ts` | `65d321ded8431891d6004a433a781d6cd2b0013b6ce159e6998405f95eb86b34` |
| `region.ts` | `117ccc927a0306548f90d9c732710b836c7521e74c30bdf8f3520ab97805f890` |
| `managed-userinfo.ts` | `f7ede85b92a93fa2b87ebfbf231568f8def84811207f7cba3aa550def5a4098e` |
| `managed-usage.ts` | `130afd97e8c8e9d3b53f8ccaaa7bb1181af1f878a2e5d5c72f53bfab4e46ece9` |

`packages/oauth/package.json`: `d6d8ab9a14484f16c6b9762e8dd82eb0c71cb165db7909895bb0e469a76b5330`;
Repository-`LICENSE`: `23cc68e17992e0b512ae2e80afc5787d7d8e0fbfbdb4fff54ec0245508fa400e`.

## Ablauf und Grenzen

Vor dem ersten Provideraufruf wählt der Nutzer die Region über die gemeinsame
`resource-required`-Auswahl. Feste Hostpaare:

| Region | OAuth | API |
| --- | --- | --- |
| `global` | `https://auth.kimi.ai` | `https://api.kimi.ai/coding/v1` |
| `mainland-cn` | `https://auth.kimi.com` | `https://api.kimi.com/coding/v1` |

Regionkandidaten verfeinern vorhandene Bindungsfelder; bekannte Subject- und
Ressourcenidentität sowie unterstützte Provideraliases bleiben erhalten.
Ein Quota-Reconnect erhält bestehende Inference-Credentials und die lokale
Accountidentität. Providerablauf, Device-TTL und Operationsfrist liefern `expired`.

Der Adapter sendet Form-POSTs an `/api/oauth/device_authorization` und
`/api/oauth/token`; Device-Grant ist
`urn:ietf:params:oauth:grant-type:device_code`, Erneuerung `refresh_token`.
Serverintervall, `authorization_pending`, `slow_down`, Ablehnung und Ablauf werden
beachtet. Verification-Adressen sind auf HTTPS und die gewählte Kimi-Domainfamilie
begrenzt. HTTP folgt keinen Redirects, nutzt keine Cookies und begrenzt Antworten
auf 1 MiB und JSON-Tiefe 16; doppelte Properties werden abgelehnt.

Ein ausgegebener Grant wird vor `/me` ohne nutzbare Zwecke privat im gemeinsamen
Generationstore gecheckpointet. Erst das verifizierte `user_id` bindet den Zweck
`quota` an Produkt `coding`, gewählte Region und Ressource `user`. Der separate
Providerwert `region` bleibt erhalten; eine Zuordnung unbekannter Provider-Enums
zu lokalen Regionen wird nicht erfunden. Cancel und Release lösen keinen Logout
oder Widerruf aus. Spät eintreffende Grants bleiben privat recoverbar.

Der gemeinsame `CredentialOwnerService` serialisiert Erneuerungen, pensioniert
verbrauchte Generationen dauerhaft und veröffentlicht per bestehendem CAS. Der
Kimi-Owner führt genau einen Refreshversuch aus, prüft danach dieselbe Identität
und denselben Provider-Regionswert und erhält ein rotiertes Ergebnis auch bei
fehlgeschlagener Profilprüfung mit leerer Zweckmenge für Recovery. Dieses Ergebnis
kann die bestehende Quota-Berechtigung nicht stillschweigend ersetzen.

Die OAuth-Quota-Abfrage prüft `/me` erneut und liest anschließend `/usages` auf
dem gebundenen API-Host. Native 5h-, 7d- und Monatsfenster bleiben eigenständig;
ungültige Werte sind unbekannt, gemessene Null bleibt Null. Kein Tokenbudget,
Wallet, Inferenzrecht, Modell oder Profil wird daraus abgeleitet. Bestehende
API-Key-Quotas werden unverändert an `KimiQuotaAdapter` delegiert.
OAuth-Werte tragen im `Source` und `SourceUrl` den tatsächlich ausgewählten
regionalen API-Host; ihre nativen Fenster-IDs bleiben erhalten.

## Integration und Prüfung

Produktionsklassen: `KimiDeviceLoginAdapter` für Methode `kimi-device` und Owner
`kimi-oauth`, `KimiDeviceQuotaAdapter` als Composite mit dem vorhandenen Keypfad.
Program registriert dieselbe Logininstanz auch als Owner; im Quota-Array ersetzt
die Compositeinstanz den bisherigen Kimi-Keyadapter. Der Katalog aktiviert nur
Device-Quota. Der alte `KimiConnectAdapter`-Kandidat bleibt unverändert.

Das Fixtureprojekt `tests/kimi-device/KimiDevice.Tests.csproj` ist auf den
gemeinsamen Device-Display-DTO-Vertrag angewiesen. Es verwendet synthetische
HTTP-Antworten und einen eigenen temporären Generationstore: Regionauswahl,
Polling, Codeanzeige, Checkpointreihenfolge, Ablehnung/ungültige Antworten,
Identitätsabweichung, spätes Cancel, Veröffentlichung, Accessprojektion,
Live-HTTP-Form der Quota, Keykompatibilität, Neustart, konkurrierende Erneuerung
und dauerhafte Recovery nach Rotation. Das Lockfile übernimmt den unveränderten
Host-Referenzgraphen aus `tests/managed-connections`.

Bei Quelltextübergabe wurden keine .NET-Builds oder Fixtureläufe ausgeführt; die
serielle Buildlane übernimmt diese Nachweise am integrierten Sourcepin. Es gab
keinen echten OAuth-Start, Providerprofil-/Usage-Aufruf, Inferenzaufruf oder
Zugriff auf persönliche Kimi-Dateien. Öffentliche Herstellerquelldateien wurden
per GET gelesen. Ein Live-Device-Login ist ein eigener, interaktiver Nachweis.
