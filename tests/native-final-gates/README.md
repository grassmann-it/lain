# Targeted native editor gates

Source base `e48bc118a5ecd2ada80648370ed2b8ef102fd01e`. Independent harness; no
production code or existing test is changed. Current status: **not built/not run**.
Only the coordinator may allocate the serial build/runtime slot.

Four cases cover Models/Profiles at 1120×780 and 800×560. Real XTEST pointer input
opens the page; all editor traversal, expansion, typing, Save and Cancel use real
XTEST keys. The harness never sets a control's focus/value or raises routed Click.
It records the mounted input inventory and actual Tab/Shift+Tab traversal, checks
stable r1/p1 mutation and unchanged bindings/policies, reopens saved/cancelled
edits, and checks focus return without repairing focus itself. A failure remains
a failure and goes to the product owner. The in-memory fixture rejects unexpected
HTTP paths; there is no real Host, provider, login, inference or client install.

The free-only model state supplies an actually disabled paid-fallback control.
Runtime theme observations record actual text brushes, alpha, brush opacity,
ancestor opacity/backgrounds, font sizes and enabled state. Ratios are calculated
from composited solid ancestor layers, not copied from design constants. They
do not certify overlapped/gradient/antialiased pixels or all accessibility.
Native X11 PNGs must be inspected individually; automatic capture is not review.

Build `NativeFinalGates.csproj` only in the agreed resource-limited serial slot.
Use the repository's pinned RID/self-contained restore convention. A new test
lockfile belongs here; do not regenerate production locks. For an exact-package
follow-up, copy the built harness and replace Desktop/Client/Core DLLs with the
final package's matching files; record their hashes and run the same four cases.
Do not call this a source rebuild or reuse an earlier binary's result.

The runner never builds and requires an absolute existing DLL:

```sh
LAIN_TEST_ASSEMBLY=/absolute/approved/NativeFinalGates.dll \
  tests/native-final-gates/run-private-xvfb.sh /absolute/private/evidence
```

It creates its own Xvfb, Xauthority, session D-Bus, HOME/XDG/LAIN_HOME. There is no
fallback to a user display/bus. `LAIN_NATIVE_GATES_ONLY=models` or `profiles`
permits a justified focused rerun; no inherited full matrix is run.

`editor-field-inventory.md` maps source fields and preservation semantics.
`handoff-closure.json` is a read-only inventory of the existing design owner's
uncommitted documents and local image dependencies for Root's explicit ST-05
handoff. It does not stage those files or claim their documents are committed.
