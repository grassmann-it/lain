# Local recording: operation and rollback

31 October 2026 · Candidate implementation. See [execution evidence](recording-execution-2026-10-30.md) for qualification and installation status.

## Scope and defaults

New installations save sanitized messages and tool content by default, separately
from accounts, Usage, profile Apply and transient Live inspection. Existing policies,
including an explicit stop, are preserved. Opening Saved conversations never changes
a saved policy. The default covers all model routes on the owned local Gateway;
optional route filters are supported by the policy API. Client identity is evidence,
not a verified per-user/per-application isolation boundary.

Storage is local and **unencrypted**, with private OS permissions on Linux.
Credential fields/patterns are screened before persistence; raw stream fragments
never reach the durable store. Unknown formats, limits and loss become explicit
coverage gaps. This is sanitized API observation, not a byte-original packet dump.
No external telemetry, judge, training export or prompt modification is enabled.

Default policy limits are 16 MiB per record, a 64 MiB source queue, a 5 GiB disk
budget, seven days of content and thirty days of metadata retention. Different
bounded in-process buffers also have their own ceilings; the queue limit is not a
promise about total process RSS. Large content may be omitted with a reason.
Source frames use checksums and independent Brotli payloads. SQLite is a disposable
metadata index; usage/account databases are separate.

## CLI and Desktop

These commands require the candidate Host, CLI and matching Core. An old installed
Host will not acquire recorder endpoints merely by updating the Desktop.

```sh
lain recording status --json
lain recording config --json
# Replace N with the independent recording revision returned by config:
lain recording enable --mode metadata_only --revision N
# Or explicitly opt into sanitized message/tool content:
lain recording enable --mode content --revision N
lain recording list --limit 25 --json
lain recording show CONVERSATION_ID --limit 25 --json
lain recording runs CONVERSATION_ID --json
lain recording run-label CONVERSATION_ID RUN_ID --revision N --variant skill-v2
# Read config again for its current revision before stopping:
lain recording off --revision N
```

In Desktop, use **Settings → Saved conversations**, **Live → Saved conversations**
or **More → Saved conversations**. Recording settings offer direct enable/stop;
there is no second consent checkbox. Lists load metadata; opening a conversation
loads its timeline. Message, part, text and selected-source pages are bounded.
Tool requests and client-reported tool results remain distinct; a recorded result
does not establish local execution. Worker relationships appear only where IDs
were actually supplied. Run labels/hashes identify configured or reported variants,
not proof that a skill was loaded or obeyed.

The local authenticated API is under `/api/v1/recording`. Policy updates use
`expectedRevision`; this revision is independent of configuration Apply. Status
shows queue/loss/storage and unavailable-source conditions. Selected-source reads
require the exact epoch, sequence and event ID, support bounded UTF-16 offsets,
and return redacted representations. A one-unit page may include a complete
surrogate pair (two units); the absolute page cap is 32,768 units.

## Files and recovery

All recorder files are under `recording/` in the existing lain state root:
`policy.json`, `archive/`, `index.sqlite` and run-label state. The Host passes only
the policy path to its owned Core. Existing archives recover asynchronously with
checksum validation; health reports `recovering` until source access is ready.
Interrupted/torn records and corruption are distinguished. Deletion-control format v2
uses hashed tombstones and supports migration from v1; older recorder readers
refuse v2 rather than silently losing deletion decisions. A full bounded deletion
ledger pauses new capture until explicit archive clear, while reads remain available. Retention invalidates
the index as well as the source, including when the UI is closed.

Stopping invalidates queued work from the previous policy generation. A bounded
write that already passed its final admission gate may finish. Inference remains
independent; stopping does not remove retained history. Failed content admission
or redaction omits content, while recorder pressure drops observations and reports
loss instead of blocking inference.

## Delete and rollback

To clear this archive, turn recording off, read the current recording revision,
then explicitly run:

```sh
lain recording delete archive --revision N
```

This clears recorder source/index/labels through the API, not accounts or Usage.
It is not an export and does not remove an independently made backup. Tombstones
prevent deleted observations from being replayed by the source feed.

For component rollback, disable recording first and restore the previous matched
Host/CLI/Core/Desktop package with the established service-update tooling. Keep
`recording/` intact if history must be retained. The old package cannot read this
new archive, but its absence of recorder support does not authorize deleting it.
Do not manually overwrite a running SQLite/journal or merge recorder files into
the existing accounting store. No automatic archive downgrade or cloud backup is
provided in this increment.

## Boundaries

Responses/Chat/Anthropic/Gemini parsers have protocol fixtures; only explicitly
listed actual Host/Core flows have end-to-end evidence. Native Gemini ingress,
WebSocket capture, client-side shell/test/Git/approval adapters, automatic import,
blind model comparison, anonymized exports, training rights/review and Memory are
separate follow-up work. Visible API messages/summaries do not reveal hidden
internal reasoning or guarantee a provider model revision.
