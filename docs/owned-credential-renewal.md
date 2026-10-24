# Vertrag für automatische Erneuerung eigener Monitorzugänge

Die Hostverdrahtung startet `OwnedCredentialRefreshService.RunAsync` nur bei aktiviertem
Quota-Monitoring. `registeredOwners` enthält ausschließlich ausdrücklich registrierte und
qualifizierte `ICredentialOwner`-Adapter. Der Loop legt keine Keys, Accounts oder Projekte
beim Anbieter an und ändert keine angeforderten Rechte.

Ein Durchlauf wählt nur aktivierte Accounts und deren aktuell ausgewählte
`quota`-Capability. Deren unveränderlicher Credentialhandle muss zu einem registrierten
Owner gehören und spätestens in zwei Minuten ablaufen. Inferenz-only, nicht ausgewählte
alte Handles, unbekannte Owner, statische Keys, externe Snapshots und Core-owned Grants
werden nicht automatisch erneuert. Fehlende Ablaufzeit bleibt unbekannt. Eine zwischen
Auswahl und Ausführung geänderte Account-/Credentialgeneration oder Quota-Zuordnung
wird erneut geprüft und übersprungen.

Der Loop läuft sofort beim Start und danach im Minutenabstand. Pro Durchlauf sind höchstens
acht Erneuerungen mit höchstens zwei gleichzeitigen Owneraufrufen vorgesehen. Jeder Aufruf
erhält eine zusätzliche Abbruchfrist von 30 Sekunden; die qualifizierten Ownertransporte
müssen Abbruch und ihre eigenen begrenzten Antwort-/Timeoutregeln beachten. Für eine bereits
gesendete Rotation darf der Owner noch ein begrenzt eintreffendes Ergebnis privat sichern.
Ein Durchlauf überlappt nicht mit dem nächsten. Diagnosen enthalten höchstens 64
Beobachtungen mit Account-, Credential- und Generations-ID sowie festen sicheren Meldungen;
keine unbearbeiteten Providerantworten oder Exceptiontexte. Eine Beobachtung gilt für ihre
benannte Generation, nicht automatisch für einen späteren Reconnect.

Der gemeinsame `CredentialOwnerService` serialisiert denselben Credentialowner pro
Credential-ID. Vor dem Remoteaufruf wird die alte Generation dauerhaft als verbraucht
markiert. Das empfangene neue Material und sein Recoveryverweis werden vor lokaler
Publikation gesichert, auch bei eingeschränkten Rechten oder abweichender Bindung.
Nur die kurze lokale Startvalidierung sowie die abschließende Prüfung und Config-CAS
warten auf die gemeinsame Publikationssperre des Hosts. Dadurch kann die Publikation eines
anderen Accounts die Startrevision nicht zwischen Snapshot und Validierung überholen.
Netzwerk-/Ownerarbeit und private Checkpoints halten diese Sperre nicht. Unverwandte
Änderungen dürfen nach erneuter Prüfung des weiterhin gleichen
Accounts, Aktivierungszustands, Handles und seiner Zwecke eine neue lokale Revision
verwenden. Disconnect, Reconnect, geänderte Zwecke und Scopewechsel überschreibt die
Erneuerung nicht.

Nach einem Fehler erfolgt kein automatischer zweiter Versuch derselben ausgewählten
Generation. Die private Retirementmarkierung verhindert auch nach einem Neustart die
erneute Verwendung möglicherweise verbrauchter Refreshdaten. Ein vorhandenes neues
Ergebnis wird ausdrücklich über Recovery übernommen; bei unbekanntem/verlorenem Ergebnis
ist ein gezielter Reconnect erforderlich. Ein ausdrücklich neu verbundener Grant besitzt
eine neue Handle-/Generationsbindung und kann wieder am Loop teilnehmen. Eine abgebrochene
Publikation reaktiviert niemals die alte Refreshgeneration. Externe Snapshots werden
weder refresht noch revoked; der Core bleibt alleiniger Owner seiner eigenen Grants.
