# REC-01 · Capture policy, redaction and global control

Owner: policy worker. Depends on [REC-00](00-contracts.md).
Status: prepared plan; no settings changed or recording enabled.

## Result and ownership

Own future `src/Lain.Host/Recording/Policy/**` and
`tests/recording-policy/**`. Shared DTOs/Host and Go wiring through integrator.
No provider credentials, existing configuration/Usage migrations or global
Codex/harness settings are owned by this package.

Build an explicit opt-in capture policy with an all-model default for local human
inspection. It is independent of routing, training eligibility, display pause and
archive deletion. No encryption or key management. Preserve private OS files and
strict credential exclusions before persistence.

## Tasks

1. Validate immutable recording-only policy snapshots with independent revisions,
   enabled/mode, scope, optional narrowing filters and stop generation. New install
   off; metadata-only optional, content explicit. Capture activation doesn't Apply
   pending routes or replace provider/client credentials.
2. Support truthful scope: proposed initial whole-owned-Gateway capture includes
   its authenticated traffic across all models. A shared inference token/claimed
   User-Agent is not a verified separate application. Strict client/project filters
   require supported identity evidence; unavailable identity isn't silently inferred.
   Resolve pool/fallback policy against the actual selected source/route.
3. Sanitize bounded allowlisted metadata and a copy of permitted content. Never
   persist auth/cookie/token/password/private-key fields or arbitrary SDK errors/
   headers; unsafe URLs/known secret files handled explicitly. Do not mutate the
   request/response forwarded to the provider/client. Unknown training permission
   does not block ordinary local inspection.
4. Preserve evidence of transformation: original retained only if actually
   unmodified/permitted; redacted/omitted/unsupported/truncated bodies clearly
   tagged. Parser/redaction uncertainty omits content, keeps safe metadata, and
   never aborts inference. Define/test threat model; no absolute claim that a
   detector can recognize every possible secret embedded in free prose.
5. Hard stop gates new observations atomically; coordinate queued-discard/in-flight
   write/restart behavior with04/06. Capture pause/off is not Clear/delete and not
   inference stop. Fixed safe health/error codes, no payloads in diagnostics.
6. Define filter configuration errors, stale revision409 and policy-change races.
   Avoid content from queued earlier policy becoming permitted under a different
   unreviewed scope. Keep audit/control metadata bounded and independent of Usage.

## Acceptance

- Off produces no new capture content buffers/files; enabled all-model scope does
  not exclude a model merely because its training rights are unknown.
- Missing client/project evidence, denied route, actual pooled source change and
  global stop race have explicit deterministic outcomes; no invented isolation.
- Fake auth/cookies/JWT/PAT/password/private-key/env/credential-URL canaries absent
  from retained records, metadata, logs, errors and temp files; ordinary code and
  messages preserved where permitted. Stable typed placeholders do not corrupt
  event IDs or tool-call joins.
- Redaction failure/oversize/malformed bodies drop content with visible coverage,
  while response bytes, account selection and existing Usage behavior are unchanged.
- Display pause, retention deletion, capture stop and service stop are distinct;
  API uses recording-only revision and rejects stale mutation safely.
- No keyring/unlock/encryption workflow; owned paths retain Linux0700/0600 and
  reject symlink/path escapes. No cloud/upload/automatic training export.

Return owned diff/tests/policy contract version, scope evidence limitations,
canary fixture outcomes, policy race semantics and integration requests. Numeric
retention/queue/disk defaults remain recommendations until REC-00 settles them;
do not invent their approval in this package.
