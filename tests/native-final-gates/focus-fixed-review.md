# Focus-return follow-up on ef507a7

Exactly four existing Models/Profiles cases passed: **282/282 assertions** at
1120×780 and 800×560. All eight Save/Cancel returns now focus the current stable
r1/p1 editor action, including the renamed model. Eighteen model fields and
95 profile fields per size remain reachable with actual XTEST Tab/Shift+Tab.
Data, full policy/binding preservation, saved revision and no Apply/export
assertions remain unchanged. The only mutations are four deliberate PUTs: two
`/routes/r1` and two `/profiles/p1`, each at original configuration revision 2.

[Curated evidence](focus-fixed-evidence.json) records exact source/assembly hashes,
all action focus returns, actual disabled samples and six personally viewed native
PNGs. The original failure manifests remain unchanged. No general mode matrix or
provider, screenreader, installed-service or package qualification is implied.

The two disabled paid-fallback captions are now genuinely visible in both sizes:
resolved foreground **#F9F9F9**, brush opacity **0.35**, cumulative visual opacity
1, composited **#5A5C5B** over **#050706**, calculated solid-layer contrast
**2.9983:1**. This documents an actually disabled control; it is not an enabled-text
contrast pass. Native screenshots distinguish it from the active next action.
There are 365 actual runtime text/brush/opacity samples in the immutable manifest.
Active muted labels retain the previously measured 6.38/6.08/5.86:1 combinations.

The photos found a separate visual limitation: native Tab gives Save and Check
spending policy a visible ring, while programmatic returned Edit focus lacked
that clear ring. Root independently viewed the narrow disabled and saved-profile
images. Root subsequently committed `7d6d425`, explicitly using
`Focus(NavigationMethod.Tab)` for editor return. **This run predates that fix**;
its 282 passes certify functional focus return, not the newer visual ring. Root
will review the same four cases' exact-package PNGs without another source build.

Build session 48114 exited 0 in 21.96 seconds (zero errors, 28 existing harness
warnings); runtime session 6698 exited 0. Both scopes were confirmed inactive/dead
and the exclusive slot explicitly released. Private Xvfb/Xauthority/session D-Bus,
synthetic HTTP, isolated HOME/XDG/LAIN_HOME, CPU150%, MemoryHigh1536M/Max2G/Swap256M,
nice15/ionice3 and owned SSD TMP were used. Production locks are unchanged.

## Exact-package handoff

Keep the complete existing harness directory, including managed/native dependencies:
`/home/tomka/Desktop/lain-native-final-gates/tests/native-final-gates/bin/Debug/net10.0/linux-x64/`.
The harness SHA-256 is
`b02760ef77ecb11ef18b03ac92eb59608a550c87e3f20d05984713026844a48d`.
Copy it to a new private staging directory; replace only Desktop/Client/Core DLLs
in the copy with the package's verified assemblies. Root's package operator owns
package/build-info/SHA256SUMS validation and the separate before/after identities.
Never overlay the preserved original harness or historical evidence directories.

The runner itself never builds. In the newly allocated resource-limited slot:

```sh
env -u LAIN_NATIVE_GATES_ONLY LAIN_TEST_SCALE=1 \
  LAIN_TEST_ASSEMBLY=/absolute/private-overlay/NativeFinalGates.dll \
  /home/tomka/Desktop/lain-native-final-gates/tests/native-final-gates/run-private-xvfb.sh \
  /absolute/new-package-evidence
```

No filter means exactly the existing four cases. Fixture and provider JSON are
embedded; no external account inputs are needed. The emitted source field is the
runner checkout identity; package source provenance must additionally come from
the package manifest. Runtime assemblies are hashed directly by the harness.
