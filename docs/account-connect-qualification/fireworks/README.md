# Fireworks: offizieller isolierter CLI-Login

EC3 implementiert `FireworksCliLoginAdapter` mit dem offiziellen `firectl`
1.8.19 für Linux x64. Der produktive CLIpfad ist weiterhin gesperrt:
Die gepinnte CLI bindet ihren Signincallback fest mit
`net.Listen("tcp", ":18000")` an alle Interfaces. Der vorbereitete Runner mit
geteiltem Hostnetz stellt deshalb keinen nur lokal gebundenen Callback sicher.
`FireworksCliQualification.Validate()` verweigert diesen Pfad auch bei exakt
passendem Binaryhash; Installation allein schaltet ihn nicht frei. Es gibt
keinen manuellen Availabilityschalter. Erst eine separat implementierte und
geprüfte Callbackisolation kann diese Grenze beseitigen. Kein echter Signin
wurde gestartet.

Der erste nutzbare Stand verwendet die vorhandene private API-Key-Eingabe mit
expliziter Account-ID. Offizieller Herstellerlink:
[Fireworks API-Keys](https://app.fireworks.ai/settings/users/api-keys), belegt im
[Serverless Quickstart](https://docs.fireworks.ai/getting-started/quickstart).
Die vorhandene API-Key-Eingabe bleibt bestehen. Fire Pass
ist kein Bestandteil dieses Anschlusses: Benutzer haben API-/Serverless-Guthaben
oder Trial, kein hier belegtes Fire-Pass-Abo.

## Installation und Hosteinbindung

`python3 tools/fireworks/install_cli.py --directory /ABSOLUT/PRIVATE/lain-cli/fireworks`
lädt ausschließlich das offizielle Artefakt und prüft Archiv- und ELF-Hash.
Die Installation führt die CLI nicht aus, verändert keine fremden Stores und
überschreibt keine abweichende Installation. Das Verzeichnis muss privat sein.
Die mutable Vendoradresse `stable` ist ausdrücklich kein stilles Upgrade: bei
abweichendem Downloadhash bricht die Installation ab.

Der Integrator registriert
`new FireworksCliLoginAdapter(cliStateStore, new FireworksCliQualification(binaryPath))`
und `FireworksCliLoginAdapter.ConnectionMethod(adapter.IsAvailable)` im vorhandenen
Login-/Methodenkatalog. Der parameterlose Adapter startet keinen Login.
Ein bloßes Availabilityflag reicht nicht: jede Ausführung prüft erneut ELF-Hash,
Plattform und Besitzerpfade; anschließend sperrt die konkrete Callbackgrenze
die produktive Ausführung. Der vorbereitete Runner würde den eigenen HOME-Mount
setzen, wird aber nicht durch ein Verfügbarkeitsflag aktiviert. Gemeinsamer
`OfficialCliProcess` und seine Umwelt-/Versionsgarantien bleiben unverändert.

## Ablauf und Eigentum

Der sichtbare Verbindungsdialog erklärt vor Beginn, dass nach der offiziellen
Anmeldung ein neuer benannter gewöhnlicher API-Key erzeugt wird. Die CLI nutzt
`signin` für Google SSO; bei schon gewählter oder erneut verbundener Account-ID
`signin <account-id>` für Kontenauswahl bzw. Enterprise-SSO. `--account-id` ist
bei Signin ausdrücklich kein Ersatz für dieses positionale Argument.

Jeder Versuch bekommt einen frischen privaten Home-Store. Der HOME-Umgebungswert
bleibt unverändert (`/home/<user>`); bubblewrap mountet die eigene leere
Verzeichnisstruktur an genau diesen Pfad. `/home`, `/run`, `/tmp`, `/var/tmp`,
`/root` und `/mnt` werden verdeckt, PID-/Mount-/User-Namensräume getrennt;
Desktopvariablen, D-Bus, Keyringruntime, Proxy-/Cloud-/API-Umwelt fehlen.
Eigene Binary und Browsercapture sind readonly eingebunden. Hostnetz wird nur
für offizielle Signin-/Account-/Key-Kommandos geteilt. Der Versionsaufruf hat
einen eigenen Netznamensraum. Unterstützt ist der tatsächlich geprüfte direkte
Linux-Pfad `/home/<user>` ohne Symlink; andere Layouts starten nicht.

Die offizielle CLI erzeugt OAuthparameter und Callback selbst. Ein privates
`xdg-open` fängt ausschließlich ihr URLargument auf und startet keinen Browser.
`StatusAsync` liefert die ursprüngliche HTTPS-Cognito-URL an den bestehenden
Dialog; dieser öffnet sie erst durch die bewusste Benutzeraktion. Der Validator
beschränkt den Host auf `fireworks-*.auth.us-west-2.amazoncognito.com`,
Pfade `/oauth2/authorize`, `/login` und das tatsächlich belegte initiale
`/logout` sowie bekannte Parameter, verlangt lokale HTTP-Callback-URI
und verhindert Token-/Codeausgabe als vermeintliche Autorisierungs-URL.
Die offizielle `/logout`-Übergabe kann die vorhandene Cognitositzung im
bewusst verwendeten Browser abmelden; auch dies ist keine von lain neu
konstruierte Login-URL. Eine lokale Callback-URI beweist ausdrücklich keinen
loopbackgebundenen Listener: genau diese Unterscheidung sperrt den Livepfad.
Keine eigenen Clients, PKCEverifier, Authcodes oder Refreshgrants werden gebaut.

Nach Signin liest die CLI `account list -o json`. Vollständige, eindeutige
`accounts/<id>` werden als Serverless-Accountbindungen angezeigt; auch bei nur
einem Account ist eine Auswahl erforderlich. Ein Reconnect mit bestehender
Accountbindung darf ausschließlich denselben Account verwenden. Revision,
Credentialgeneration und übriger Operationskontext dürfen sich nicht ändern.
Danach genau ein Aufruf
`api-key create --account-id <id> --key-name lain-<operation-id> -o json`;
kein `--firepass`, kein Serviceaccount, kein Lesen/Importieren bestehender Keys.
Der Ausgabeparser verlangt den eigenen neuen Anzeigenamen, Key-ID und privaten
Schlüssel; FirePassmarker und `fpk_` werden verweigert. Die Account-ID aus dem
expliziten Erzeugungsaufruf wird mit dem Schlüssel in den privaten Settings
gebunden und steht dem bestehenden Quotaadapter zur Verfügung.

Ein Keyresultat wird vor Übergabe an die gemeinsame Veröffentlichung durable
gecheckpointet. Host und gemeinsame Credentialgenerationen besitzen allein die
Adoption. Der Adapter erzeugt keine Accounts/Routen/Profile eigenständig.
Die eigene OIDCsession bleibt im isolierten Store; an lain geht ausschließlich
der neue statische API-Key (`static-key`) mit `remoteKeyId` und Recoverybezug.
Es gibt keine behauptete API-Key-Rotation oder Refreshownership. Disconnect
löscht keine fremde Sitzung und behauptet keine Remote-Revoke. Eigene Schlüssel
müssen bei Bedarf über den Anbieter bewusst gelöscht werden. Der normale
API-Key-Weg bleibt jederzeit vorhanden.

Abbruch, Timeout, Schema-/Identitätsfehler bewahren private Ergebnisdateien und
markieren Recovery statt automatischer Wiederholung. Ein möglicherweise
angelegter Key wird nie still erneut angelegt. Prozessbaum und private
PID-Namensräume werden beendet; stdout/stderr bleiben pro Kommando exklusiv
privat und auf je 1 MiB begrenzt. Keine Secrets erscheinen in Argumenten,
Fehlermeldungen oder Status DTOs.

## Nachweise und verbleibende echte Prüfung

[vendor-pins.json](vendor-pins.json) trennt Herstellerpublikation und proprietäre
CLIquelle ausdrücklich. Der offizielle Homebrew-Tap bestätigt den exakten
Archivhash. Eine frei verfügbare firectl-Implementierungsquelle und SPDXlizenz
werden nicht behauptet; die CLI wird nicht im Repository eingecheckt.

Am 26.10.2026 liefen `version`, `--help`, `signin --help` und
`api-key create --help` erfolgreich im echten eigenen Mount bei unverändertem
HOME, getrenntem Netzwerk und leerem Home. Budget: MemoryHigh256MiB,
MemoryMax512MiB, Swap0, CPU100%, äußerer Timeout120s. Beendet nach0.14s.
Private lokale Belege liegen unter
`~/.cache/lain-qualification-20261026/fireworks-public/offline-evidence`.
Die Hilfe bestätigt OIDC, accountgebundenes SSO und opt-in `--firepass`;
die fest gepinnte Browserabhängigkeit verwendet Linux `xdg-open` zuerst.

`tests/account-connect-fireworks` prüft synthetische Account-/Keyverträge,
FirePassabgrenzung, Issuer/Callback, Bindungen, Kontextfences und einmalige
Ausstellung. Synthetische Fixtures sind keine echte Anmeldung. Tatsächlicher
Signinchallenge-/Callback-, CLI-JSON- und Keyanlage-/Quotaablauf erfordert noch
das separate Root-GO und bewusste Anbieterzustimmung des Benutzers. Falls der
proprietäre Herstelleroutput vom geprüften Vertrag abweicht, bleibt das
Resultat privat zur Recovery; keine Alias-/Issueraufweichung ohne neue Prüfung.
Geldsaldo wird nicht erfunden: Fireworksquotas sind Ressourcenconstraints,
Genehmigungsobergrenzen oder Monatslimitraum, kein bestätigtes Prepaidwallet.

## Zusätzliche statische Prüfung: Signin ist keine belegte Keyanlage

Die Dokumentation beschreibt Signin als OIDC, ohne einen impliziten
API-Key zu versprechen. Eine ergänzende reine ELFprüfung des exakt gepinnten
Artefakts ordnet über Go-PCLNtab und Typmetadaten die Signinbefehlsroutine
`0x2254440..0x2254ce0` zu. Sie ruft `signin`, `getEmail`,
`findDefaultAccountID`, `saveAuthConfig` und `writeSettings` auf. Die
SSO-RPC-Argumenttypen dort sind `gateway.CreateUserRequest` und `gateway.User`,
keine API-Key-Erzeugung. Die eigene Signinroutine
`0x2254da0..0x2255a60` verwendet OIDCprovider, Tokenexchange und gegebenenfalls
die offizielle Deviceauthfallbackfunktion. Ein CreateApiKey-Aufruf ist in den
untersuchten Signinroutinen nicht belegt. Damit gibt es derzeit keinen Beleg
für einen redundanten normalen Default-API-Key; der explizit benannte spätere
Erzeugungsschritt bleibt der nachvollziehbare Keyhandoff. Dies ist statische
Binärevidenz und keine tatsächlich durchgeführte Anmeldung oder Zusicherung
über nicht einsehbare Herstellerimplementierungen.

Der statische Testprojektvertrag liegt in
`tests/account-connect-fireworks/Fireworks.Tests.csproj` mit committed
`packages.lock.json` (gleicher Host-/Core-Referenzgraph wie bestehende
`tests/kimi-device`). Der Runner erzeugt kein temporäres Projekt ohne Lock mehr.
Die Lockdatei wurde aus diesem vorhandenen identischen Abhängigkeitsgraph
vorbereitet, ohne Restore oder Build. Erst die gemeinsame serielle Lane
bestätigt Locked-Restore, Kompilierung und Fixtures.
