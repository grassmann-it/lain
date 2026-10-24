# Repository publication

The initial GitHub repository is private. Runtime credentials, private databases,
build output and local assistant configuration are not part of the source upload.
The other conversation's uncommitted provider research is excluded from this
delivery.

Before publication, Gitleaks 8.30.1 inspected all reachable Git history with
redacted reporting. An independent review inspected 65 refs, 782 commits and
2,529 distinct blobs, including commit messages and binary asset types. No
individual access keys, access/refresh tokens, private keys, credential-bearing
URLs, authentication files, databases or runtime archives were found.

The initial 534 scanner findings were reviewed individually by category:

- 504 were SHA-256 inventory entries in the two proxy manifests.
- 6 were public OAuth application constants matching the pinned upstream source
  at `9bdde54b59d1af70ae0534a0ef61b2c3361a1257`.
- 20 were upstream protocol/model/session identifiers and test values.
- 4 were synthetic request-construction fixture values.

Public application constants identify an OAuth client and are not individual
account credentials. They remain part of the pinned third-party source. No
blanket exclusion for `proxy/` or `tests/` was introduced. The final committed
tree and rewritten history are scanned again before push; counts above describe
the initial reviewed history, not a claim of zero raw scanner matches.

Some checked-in acceptance records contain local machine paths, account labels
or observed usage and budget figures. These are not credentials, but should be
reviewed and sanitized separately before making the repository public.

All reachable commit author and committer email addresses are rewritten to
`tom@grassmann-it.de`; names, dates, trees and parent relationships are retained.
An original-history bundle remains outside the repository as a private rollback
copy. `git-history-rewrite.json` maps old commit identities to the rewritten
equivalents. Historical binary/source attestations retain their original IDs.
