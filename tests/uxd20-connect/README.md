# UXD-20 Connect delivery

Baseline: `c3db87e`. Existing provider search, recommended method selection,
private input fields, safe non-secret drafts, login polling/cancellation and
separate quota access are retained. Setup/Prepare/Install belongs to the Profiles
owner and is not changed here.

The missing path was prerequisite explanation and an entry-point guard: the
previous Core check inferred availability from Gateway intent when capabilities
were absent, and `StartConnectionLoginAsync` could POST without checking methods.
The new prerequisite card offers sources, the existing deliberate host-mode
review, reload and available alternatives. All new operations re-read status and
the installed method catalog before submitting. Unavailable methods remain
disabled; requirement review itself is a separate action. A pending operation
still resumes its existing identity without starting another operation.

Provider discovery now renders installed method availability/purposes and includes
the method catalog in its refresh identity. New providers and methods need no
desktop provider switch. Monitor access reuses the same prerequisite review.

`prerequisites.json` supplies synthetic cases for UXD-22, including the direct
login-entry bypass, disappearing methods, Core loss between form and Continue,
non-Core Monitor login and source/mode-review return paths. No credential data is
included. The central native owner should assert request logs and capture both
sizes; these cases are inputs, not a claim of executed acceptance.

Delayed chooser and reconnect responses additionally require their original
Accounts section, mounted content, owner request and shell navigation generation.
Leaving and returning invalidates the old operation, as does a newer connection
request. Stale errors are discarded too. Opening connect editors uses the shell's
`CanOpenSectionEditor` draft contract; no global navigation lock was added.

Source validation: `git diff --check` passed. No build, runtime, GUI, provider,
credential or service operation was performed in this worktree. Native/request
qualification awaits the integrator's serial slot. Normal account settings,
Profiles, shell layout, native controls, cat and mint palette are untouched.
