# Recording protocol fixture qualification

Run from the checkout with the project's serial build lane:

```
dotnet run --project tests/recording-normalization/Recording.Normalization.Tests.csproj --disable-build-servers -p:UseSharedCompilation=false -m:1
```

The runner uses only synthetic payloads. It does not start Lain, change its policy,
contact providers or claim live capture qualification. Source DTOs must already
have a trusted namespace; a normalizer never creates a client identity.

| Protocol | JSON bodies | Incremental SSE | Relevant boundaries |
| --- | --- | --- | --- |
| Chat Completions | messages, tool calls/results, refusals, nullable usage | explicit choice/call indices, text/refusal, argument deltas, finish/usage | keepalives, CR/LF and bytewise UTF-8 |
| Responses | input/output items, message parts, function/custom tools, previous response, compaction, usage | output/item/content indices, item-ID lookup, text/refusal/arguments, final full response | failed/incomplete remain partial |
| Anthropic | message blocks, tool use/result, reported usage/cache | block indices, text/input JSON deltas, terminal and split usage | thinking/signatures unsupported, never recovered |
| Gemini | contents/candidates, text/function calls/results, reported usage | SSE candidates with explicit indices, text/full function args, finish/usage | this fixture gate does not establish admitted/live ingress |

`RecordingStreamAssembler.Push` operates before policy. Fragment observations
return metadata without body; terminal observations contain one assembled JSON
representation. Only then may policy sanitize and store it. Partial inference is
encoded inside the complete assembled representation, with `partial_stream` and
incomplete message/part flags. A malformed/oversize assembly drops its affected
content with safe classifications; no raw fallback or error-body logging exists.
Source references retain contributing event IDs. Native IDs and observed call
order remain distinct from generated message IDs. A client tool result never
claims actual local execution. Opaque reasoning, images, audio and unsupported
content produce explicit coverage gaps rather than reconstructed content.

Default bounds: 64 active streams, 16 MiB charged per stream, 64 MiB total charged
state, 1 MiB frame, 4,096 parts and 8,192 source records per stream, 120 second
idle expiry. Charges include incoming bytes and conservative source-reference
metadata overhead; these are experimental implementation limits, not live
performance acceptance. A JSON record is bounded to 16 MiB, 4,096 messages,
16,384 parts and depth 64. Input limits are fail-closed; parser callbacks never
read network, files, credentials or other-client archives.

The buffered observer can hand over a whole raw SSE stream in a single terminal
`response` observation; `Push` assembles that observation immediately. Bodyless
`chunk_metadata` records never create phantom assembly state and a supplied final
full JSON response supersedes pending transient fragments. Requested model fields
from client ingress stay separate from models reported at an output boundary.

Implemented fixture checks additionally verify full buffered SSE, no phantom
state from bodyless chunk counts, requested-versus-reported models, expiry and
safe unknown-protocol coverage. Shared policy sanitization is exercised directly,
including split Bearer tokens and fragmented JSON secret-property names.
