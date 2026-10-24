# Refreshing the instrumented Core build boundary

Historical schema-1/patch-2 procedure below. For the external v8.0.41 dependency,
explicit patches and schema-2 delivery use [core-upgrade-delivery.md](core-upgrade-delivery.md)
and `proxy/README.md`. Do not run this vendored-source regeneration against schema 2.

This is a root/integrator procedure for the next owned Core. It does not authorize
replacement of the installed binary. Source workers do not regenerate the shared
manifest while other Core changes are still being integrated.

1. Merge the complete instrumentation, transport and cost-policy changes. Finish
   formatting/review and commit all `proxy/core` source inputs. Keep compiler and
   generated artifact output outside `proxy/core`. Retain the previously
   qualified binary and its manifest as rollback material.
2. Preserve the upstream record: `proxy/baseline.json` must keep SHA-256
   `a3dcb37a26d25f0df4173c203dba69fe04a813ddf94d826ccc9adc8efc884045`;
   `upstreamCommit` stays `9bdde54b59d1af70ae0534a0ef61b2c3361a1257`;
   `snapshotVersion`, `verifiedOriginalFileCount`, original license hash and
   exclusion metadata are unchanged. This expansion changes lain's patch level,
   not the provenance of the vendored snapshot.
3. Regenerate both `files` and `patches` in `proxy/source-manifest.json` from the
   final clean source tree. Patch baselines come from
   `baseline.current_source_files`, never from the previous lain version. New
   files have `baseline: null`. Increment `lainPatchVersion` to 2 for this
   expansion. Unexpected removals/untracked source files require inspection;
   do not silently bless compiled outputs or ignored local files.

The following one-time root command implements step 3 for this additive
expansion. It intentionally refuses removed existing inputs and requires a
clean, fully tracked source tree. It has **not** been executed by the Core
instrumentation worker.

```sh
python3 - <<'PY'
import hashlib, json, pathlib, subprocess

root = pathlib.Path.cwd()
source = root / 'proxy/core'
path = root / 'proxy/source-manifest.json'
manifest = json.loads(path.read_text())
baseline_bytes = (root / 'proxy/baseline.json').read_bytes()
baseline = json.loads(baseline_bytes)
assert hashlib.sha256(baseline_bytes).hexdigest() == manifest['originalBaselineSha256']
assert baseline['upstream_commit'] == manifest['upstreamCommit']
assert manifest['lainPatchVersion'] == 1, 'Review the next patch version explicitly'
assert not subprocess.check_output(['git', 'status', '--porcelain', '--', 'proxy/core'])
files = sorted(p for p in source.rglob('*') if p.is_file())
assert not any(p.is_symlink() for p in source.rglob('*')), 'Unexpected source symlink'
tracked = {
    pathlib.PurePosixPath(p).relative_to('proxy/core').as_posix()
    for p in subprocess.check_output(['git', 'ls-files', '-z', '--', 'proxy/core'])
        .decode().split('\0') if p
}
actual = {p.relative_to(source).as_posix() for p in files}
assert actual == tracked, 'Untracked or absent source input'
assert set(manifest['files']) <= actual, 'Review removed inputs explicitly'
assert hashlib.sha256((root / 'proxy' / manifest['licenseFile']).read_bytes()).hexdigest() == manifest['licenseSha256']
current = {p.relative_to(source).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
original = baseline['current_source_files']
manifest['files'] = current
manifest['patches'] = {
    name: {'baseline': original.get(name), 'lain': digest}
    for name, digest in current.items() if original.get(name) != digest
}
manifest['lainPatchVersion'] = 2
temporary = path.with_suffix('.json.tmp')
temporary.write_text(json.dumps(manifest, indent=2) + '\n')
temporary.replace(path)
print(f'Refreshed {len(current)} inputs; {len(manifest["patches"])} lain patches')
PY
python3 scripts/build-core-verify.py
git diff --check
git diff -- proxy/source-manifest.json
```

4. Review the manifest diff and commit it. The verifier must pass on a fresh
   checkout as well as the integrator's worktree. It checks every actual Core
   file, including tracked dotfiles and test fixtures; metadata exclusions are
   historical snapshot choices, not a wildcard to hide new build inputs.
5. In the serialized compiler lane, use the pinned Go 1.27.2 toolchain. Run the
   focused Go suites including `./internal/lain`, `./sdk/cliproxy/auth`, modified
   transport packages and any new cost-policy helper. Build to a new absolute
   output path with `scripts/build-core.sh`. Its binary manifest now takes
   `snapshotVersion` and `lainPatchVersion` from the verified source manifest.
6. Freeze the candidate bytes and run the updated synthetic backend/Inspector
   qualification against that exact binary. Known usage paths now have a third
   metadata-only `attempt_finished`; unknown reporter paths remain two events.
   Successful earlier reference-provider evidence does not qualify newly added
   adapters or substitute for testing this changed binary.
7. Compare the frozen binary hash, its manifest's source-manifest hash and the
   qualification report before staging. Preserve the old
   `proxy/qualification.json` as evidence of its recorded binary until a new
   exact-hash report is ready. Do not silently transfer `passed: true` to a new
   hash. Existing component-only refresh scripts deliberately require the old
   qualified Core hash; use a new-Core staging procedure rather than weakening
   those rollback protections.

Do not regenerate the original upstream baseline, rerun legacy collectors,
modify legacy queues/configuration, or install as part of this procedure. Root
coordinates any later package acceptance and controlled deployment separately.
