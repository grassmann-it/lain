# Synthetic recording contract v1 fixtures

`conversations-v1.json` is entirely synthetic. Source bodies are base64 transient
bytes as in `SourceObservation`; they must pass policy before storage. Never copy
the pack directly to a durable recorder journal. `generate.py` deterministically
regenerates the pack and exposes streaming `scalable(200)` generation (200 calls,
100 client tool events), without provider calls or command execution.

The 25 source records exercise three turns/model changes, full-history resends,
ambiguous duplicate text, a fork with shared prefix, two explicitly evidenced
workers, two same-name tool calls with distinct IDs, later client results,
interleaved unassembled argument fragments, retry/fallback, partial/cancel/missing
terminal, zero versus unknown usage, resume/previous response, reported/effective
reasoning mismatch, an unpinned moving alias, absent identity, source epoch loss,
dropped interval, invalid JSON and fake secret canaries. Two configured skill
manifests do not claim proof of loaded skill content. No tool event claims actual
local execution.

`expected` contains explicit semantic invariants for normalization/projection
consumers. It is not a claim that all protocol cells have already passed. Size-limit
coverage is generated in the policy executable without storing a giant fixture.
