#!/usr/bin/env python3
"""Synthetic fixture generator. No environment reads, credentials or real tool execution."""
import base64
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
observations = []

def add(request, kind, body=None, model="model-a", conversation="synthetic-conversation", attempt=None,
        complete=True, gap=None, epoch="synthetic-epoch-a", **extra):
    seq = 1 + sum(o["sourceEpoch"] == epoch for o in observations)
    dimensions = {"conversationId": conversation, "evidence": [{"field": "conversationId", "source": "native_request"}]}
    dimensions.update(extra.pop("dimensions", {}))
    o = dict(sourceEpoch=epoch, sequence=seq, eventId=f"{epoch}:{seq}", captureId=f"capture-{request}",
             requestId=request, observedAt=f"2026-10-30T12:{len(observations)//60:02}:{len(observations)%60:02}Z",
             boundary="client_request" if kind == "request_body" else "client_response", protocol="openai-chat",
             kind=kind, model=model, routeId="synthetic-route", dimensions=dimensions, complete=complete,
             schemaVersion=1, policyRevision=1, stopGeneration=1)
    if attempt: o["attemptId"] = attempt
    if gap: o["gapReason"] = gap
    if body is not None:
        text = body if isinstance(body, str) else json.dumps(body, separators=(",", ":"))
        o.update(bodyEncoding="utf8_json", body=base64.b64encode(text.encode()).decode())
    o.update(extra)
    observations.append(o)
    return o

history = []
for turn, model in [(1, "model-a"), (2, "model-b"), (3, "moving-alias")]:
    history.append({"role": "user", "content": "same ambiguous text" if turn > 1 else "Synthetic first turn"})
    add(f"request-{turn}", "request_body", {"messages": history.copy(), "model": model}, model=model)
    response = {"role": "assistant", "content": f"Synthetic reply {turn}"}
    add(f"request-{turn}", "response_body", {"id": f"response-{turn}", "choices": [{"message": response, "finish_reason": "stop"}]}, model=model)
    history.append(response)
add("request-fork", "request_body", {"messages": history[:2] + [{"role":"user", "content":"Synthetic fork"}]}, conversation="synthetic-fork")
for worker in ["worker-a", "worker-b"]:
    dims = dict(workerId=worker, parentWorkerId="worker-root", parentRequestId="request-3",
                evidence=[{"field": "workerId", "source": "explicit_worker_manifest"},
                          {"field": "parentRequestId", "source": "explicit_worker_manifest"}])
    add(worker, "request_body", {"messages": [{"role": "user", "content": "Synthetic delegated task"}]}, conversation=f"conversation-{worker}", dimensions=dims)
    add(worker, "response_body", {"choices": [{"message": {"role": "assistant", "content": "Synthetic worker reply"}}]}, conversation=f"conversation-{worker}", dimensions=dims)
add("tools", "response_body", {"choices": [{"message": {"role": "assistant", "tool_calls": [
    {"id":"call-one", "type":"function", "function":{"name":"same_tool", "arguments":"{\"n\":1}"}},
    {"id":"call-two", "type":"function", "function":{"name":"same_tool", "arguments":"{\"n\":2}"}}]}}]})
add("tool-result", "request_body", {"messages": [{"role":"tool", "tool_call_id":"call-two", "content":"Synthetic client-reported result"},
    {"role":"tool", "tool_call_id":"call-one", "content":"Synthetic rejected result"}]})
for index, fragment in [(0, '{"token":"sk-'), (1, 'syntheticcanary123"}')]:
    add("split-tool", "response_chunk", f'data: {{"index":{index},"arguments":{json.dumps(fragment)}}}\n\n', complete=False, bodyEncoding="utf8_sse")
add("retry", "response_body", {"choices":[{"message":{"role":"assistant", "content":"partial"}}]}, attempt="attempt-1", complete=False, gap="timeout")
add("retry", "response_body", {"choices":[{"message":{"role":"assistant", "content":"fallback result"}}], "usage":{"prompt_tokens":0,"completion_tokens":0,"total_tokens":0}}, model="model-b", attempt="attempt-2", tokens=dict(input=0,output=0,cached=None,reasoning=None,total=0))
add("cancelled", "gap", complete=False, gap="cancelled")
add("missing-terminal", "response_chunk", 'data: {"text":"unfinished"}\n\n', complete=False, bodyEncoding="utf8_sse")
add("resume", "request_body", {"previous_response_id":"response-3", "input":"Synthetic compacted continuation"}, protocol="openai-responses", dimensions=dict(previousResponseId="response-3",reasoningEffort="high",effectiveReasoningEffort="low"))
add("missing-native-id", "request_body", {"messages":[{"role":"user","content":"No inferred session identity"}]}, conversation=None)
add("lost-epoch", "gap", complete=False, gap="source_epoch_lost", epoch="synthetic-epoch-b")
add("dropped", "gap", complete=False, gap="queue_overflow", epoch="synthetic-epoch-b")
add("invalid", "request_body", "{malformed", epoch="synthetic-epoch-b")
add("secrets", "request_body", {"authorization":"Bearer synthetic-canary", "password":"synthetic-password-canary", "messages":[{"role":"user","content":"ordinary code"}]}, epoch="synthetic-epoch-b")

pack = dict(fixtureVersion=1, synthetic=True, observations=observations,
    manifests=[dict(id="run-a",variant="skill-a",configuredRevision="fixture-v1",skillHash="sha256:synthetic-a",evidence="configured"),
               dict(id="run-b",variant="skill-b",configuredRevision="fixture-v1",skillHash="sha256:synthetic-b",evidence="configured")],
    expected=dict(threeTurns=["request-1","request-2","request-3"], modelChange=True,
                  fullHistoryResendsAreNotExtraMessages=True, ambiguousTextIsNotIdentity=True,
                  explicitWorkers=["worker-a","worker-b"], distinctSameNameTools=["call-one","call-two"],
                  toolResultEvidence="client_reported", localExecutionEvidence=None,
                  omittedBodies=["split-tool","retry:attempt-1","missing-terminal","invalid"],
                  unknownUsage=None, zeroUsage=0, movingAliasRevision=None,
                  configuredSkillIsNotLoadedSkillProof=True))
(ROOT / "conversations-v1.json").write_text(json.dumps(pack, indent=2) + "\n")

def scalable(count=200):
    """Yield bounded SourceObservation dictionaries: 200 calls and 100 tool events."""
    sequence = 0
    for n in range(count):
        sequence += 1
        body = {"model": f"model-{n%3}", "messages": [{"role":"user", "content": f"Synthetic scale call {n}"}]}
        yield dict(sourceEpoch="synthetic-scale", sequence=sequence, eventId=f"synthetic-scale:{sequence}",
                   captureId=f"capture-scale-{n}", requestId=f"request-scale-{n}",
                   observedAt="2026-10-30T14:00:00Z", boundary="client_request", protocol="openai-chat",
                   kind="request_body", model=f"model-{n%3}", bodyEncoding="utf8_json",
                   body=base64.b64encode(json.dumps(body).encode()).decode(), complete=True,
                   schemaVersion=1, policyRevision=1, stopGeneration=1)
        sequence += 1
        message = {"role":"assistant", "content":f"Synthetic scale response {n}"}
        if n%2 == 0:
            message["tool_calls"] = [{"id":f"tool-{n//2}","type":"function", "function":{"name":"synthetic_tool", "arguments":"{}"}}]
        body = {"choices":[{"message":message,"finish_reason":"tool_calls" if n%2 == 0 else "stop"}]}
        yield dict(sourceEpoch="synthetic-scale", sequence=sequence, eventId=f"synthetic-scale:{sequence}",
                   captureId=f"capture-scale-{n}", requestId=f"request-scale-{n}",
                   observedAt="2026-10-30T14:00:01Z", boundary="client_response", protocol="openai-chat",
                   kind="response_body", model=f"model-{n%3}", bodyEncoding="utf8_json",
                   body=base64.b64encode(json.dumps(body).encode()).decode(), complete=True,
                   schemaVersion=1, policyRevision=1, stopGeneration=1)

if __name__ == "__main__":
    print(json.dumps({"synthetic":True,"observations":len(observations),"scalableCalls":200,"scalableObservations":sum(1 for _ in scalable()),"toolEvents":100}))
