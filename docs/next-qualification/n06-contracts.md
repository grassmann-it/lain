# N06 collection order and client contract

Source baseline: `7661886`. This package adds the existing Profiles workflow's
missing global collection-order operation; it does not apply profiles, regenerate
exports, change membership, change router references or implement group visibility.

`ModelsService.ReorderCollections(long revision, string[] collectionIds)` uses the
same repository CAS as other configuration changes. The list must be a complete,
unique, ordinal permutation of every current collection ID. Null, duplicate,
foreign or missing IDs reject the whole operation without memory/disk mutation.
A stale revision fails before permutation validation. A valid reorder advances the
configuration revision once and leaves AppliedRevision unchanged.

HTTP: `POST /api/v1/collections/order` with
`{"revision":N,"value":{"collectionIds":["million","security"]}}` returns
`ConfigurationDto`; stale revision returns 409 / `revision_conflict`, invalid order
returns 400 / `validation`. POST deliberately avoids shadowing the existing
`PUT /collections/{id}` path for a legitimate collection named `order`.
The additive Core request is `CollectionOrderRequest(string[] CollectionIds)`.
The client method is `ReorderCollectionsAsync(long revision, string[] collectionIds,
CancellationToken ct = default)`. Existing typed Collections/ModelGroups and
CollectionRouter CRUD methods were already complete and are retained.

`CollectionRouterValidationException` exposes an allowlisted reason/code and
bounded configuration references. `ToApiError()` publishes only fixed reason text
and sanitized RouterId/ProfileId. Codes are `router_provider_accounts_ambiguous`,
`router_no_eligible_members`, and `router_alias_conflict`. Unknown identities are
omitted, and no provider exception, raw credential, arbitrary exception message or
inner exception is serialized. ModelEndpoints handles this type explicitly and
uses fixed text for other argument failures. Root integrates the same typed catch
before the general argument catch in Program.cs for Apply and other shared paths.
The existing client preserves the safe code/message in `LainApiException`.

Source fixtures cover permutation validation, stale/invalid CAS precedence,
memory/disk preservation, restart, member/router preservation, HTTP CRUD coexistence,
typed client envelopes/CRUD and bounded router diagnostics. They are hooked into
`tests/models` and `tests/client-cli` without shared Program.cs changes. No builds,
restore or runtime tests were run by this worker; the serial Root lane must qualify
these sources before delivery. No accounts, live configuration or installed binary
were changed.
