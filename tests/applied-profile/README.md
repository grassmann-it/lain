# Applied profile metadata checks

Run `dotnet run --project tests/applied-profile/AppliedProfile.Tests.csproj` from
the repository root. The harness source-includes the Host metadata reader only;
it starts no host, proxy, provider connection or desktop window. Fixtures live in
one temporary directory and are removed after execution.

Checks cover absent files, matching valid applied revisions, pending desired
revisions, malformed JSON and document shapes, invalid runtime validity,
mismatched/negative/overflow revisions, missing/blank/oversized profile IDs,
metadata file size bounds, evidence preservation and repair after invalid data.
Both metadata files must name the same profile: legacy runtime manifests with
no `profileId` yield unknown. Partial publication when switching profiles at the
same saved revision also yields unknown until valid runtime and active metadata
agree; desired/selected profiles are never used to guess this identity.

The status endpoint lock requires code review or an HTTP integration test: this
unit harness does not execute ASP.NET middleware. Ordinary GET status requests
acquire the global mutation semaphore inside the endpoint; mutation requests
acquire it in middleware and call the synchronous status reader without trying
to acquire it again.
