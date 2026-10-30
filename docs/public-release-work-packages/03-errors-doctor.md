# PUB-03 · Actionable safe errors and client diagnosis

Owner: diagnostics worker; Core boundary edits coordinated with integrator.
Depends on00; capability facts from01, generation facts from02.

## Outcome and ownership

Explain actual failures without full provider-body logs, and expose existing
Doctor findings in a small UI/support copy. Own CLI Diagnostics/** and new Desktop
Diagnostics component/tests; shared report DTO/API/navigation via integrator.
Optional Core error evidence lives at reviewed transport/terminal boundaries;
no simultaneous upstream/adapter edits by another worker.

## Tasks

- Preserve exact known error category/code/parameter through a bounded allowlist,
  with explicit observation source. Fixed human explanation/action; no arbitrary
  provider text, prompt or credential echo. Unknown reason remains unknown.
- Test HTTP rejection vs in-stream failed/error vs missing terminal vs disconnect
  after partial output. Never synthesize response.completed, truncate tools or
  delete conversation history to turn an error into success.
- Extend existing Doctor with qualified modality/context/compaction consistency
  and generation checks, using01 evidence; don't maintain a second model table.
- Add bounded UI findings and previewable support copy: versions, safe codes/IDs,
  generation/coverage and proposed next action. No chat/image/tool payload, secret
  header, arbitrary path or raw error in support bundle. Actual backend binding
  remains unknown if only static config was inspected.
- Any future repair shows its scoped diff, guards against stale data and uses
  existing owned installation/update paths. Diagnosis doesn't automatically
  Apply/restart/login/change client config. Repair execution is a separate action.

## Acceptance

Native pinned client fixtures receive an actionable qualified failure rather than
only reconnect spam for known invalid input; unknown errors retain honest fallback.
Canary secrets never enter report/error paths; malformed/oversize diagnostics stay
bounded. Doctor detects regressions in01 metadata and02 generations without paid
inference. UI and CLI use one report contract and preserve drafts/focus.

Return source/native/privacy/report evidence, coverage limitations and interface
requests. The current live400 cause isn't retroactively called context-only just
because the missing auto-compaction threshold was independently reproduced.
