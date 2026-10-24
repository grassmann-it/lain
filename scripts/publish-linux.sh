#!/usr/bin/env bash
# Produce a self-contained, versioned linux-x64 package without installing it.
set -euo pipefail
umask 077
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
out=${1:-"$root/artifacts/lain-linux-x64"}
[[ "$out" == /* && ! -e "$out" ]] || { echo 'Output must be an absolute, nonexistent directory' >&2; exit 2; }
cd "$root"
revision=$(git rev-parse HEAD)
# Application source pins describe an exact tracked tree; unrelated notes may be dirty.
frozen_inputs=(src Directory.Build.props Directory.Build.targets global.json NuGet.Config nuget.config scripts proxy packaging skills)
verify_frozen_inputs() {
    [[ "$(git rev-parse HEAD)" == "$revision" ]] || { echo 'Git HEAD changed during publish' >&2; exit 2; }
    git diff --exit-code "$revision" -- "${frozen_inputs[@]}"
    [[ -z "$(git ls-files --others --exclude-standard -- "${frozen_inputs[@]}")" ]] || { echo 'Untracked compile/packaging inputs must be committed before publishing' >&2; exit 2; }
}
verify_frozen_inputs
publish_parent=$(mktemp -d "${TMPDIR:-/tmp}/lain-publish.XXXXXXXX")
trap 'rm -rf -- "$publish_parent"' EXIT
mkdir -p "$publish_parent/properties"
[[ "$(python3 -c 'import json; print(json.load(open("proxy/source-manifest.json"))["schemaVersion"])')" == 2 ]] || { echo 'Fresh publish requires external Core schema 2' >&2; exit 2; }
expected=$(python3 -c 'import json; print(json.load(open("global.json"))["sdk"]["version"])')
[[ "$(dotnet --version)" == "$expected" ]] || { echo "Requires .NET SDK $expected" >&2; exit 2; }
[[ -x scripts/build-core.sh && -f proxy/source-manifest.json ]] || { echo 'Qualified proxy source and build script required' >&2; exit 2; }
# Locked restore refuses package graph changes. Update committed lockfiles explicitly.
for project in Lain.Cli Lain.Host Lain.Desktop; do
    [[ -f "src/$project/packages.lock.json" ]] || { echo "Missing lockfile: $project" >&2; exit 2; }
    dotnet restore "src/$project/$project.csproj" --locked-mode -m:1 -p:BuildInParallel=false -nr:false -r linux-x64 -p:SelfContained=true
 done
mkdir -p "$out/proxy" "$out/packaging" "$out/skills"
for spec in 'Lain.Cli cli' 'Lain.Host host' 'Lain.Desktop desktop'; do
    read -r project directory <<< "$spec"
    # Read each project's evaluated version without triggering a build. The exact
    # explicit informational version below is passed to publish and verified in its DLL.
    dotnet msbuild "src/$project/$project.csproj" -nologo -verbosity:quiet -target:GetAssemblyVersion \
        -getProperty:Version,InformationalVersion -p:Configuration=Release -p:RuntimeIdentifier=linux-x64 \
        -p:SelfContained=true -p:IncludeSourceRevisionInInformationalVersion=false \
        > "$publish_parent/properties/$directory.evaluated.json"
    python3 - "$publish_parent/properties/$directory.evaluated.json" "$publish_parent/properties/$directory.json" "$revision" "$project" <<'PYPUBLISHVERSION'
import json, pathlib, re, sys
source, output = map(pathlib.Path, sys.argv[1:3])
revision, project = sys.argv[3:5]
evaluated = json.loads(source.read_text())['Properties']
version = evaluated['InformationalVersion']
if not re.fullmatch(r'[0-9A-Za-z.+_-]+', version) or not re.fullmatch('[0-9a-f]{40}', revision):
    raise SystemExit('Evaluated informational version or frozen revision is invalid')
version += ('.' if '+' in version else '+') + revision
output.write_text(json.dumps({'project': project, 'sourceCommit': revision, 'evaluatedProperties': evaluated,
    'publishProperties': {'InformationalVersion': version, 'IncludeSourceRevisionInInformationalVersion': 'false',
        'SourceRevisionId': revision, 'Configuration': 'Release', 'RuntimeIdentifier': 'linux-x64', 'SelfContained': 'true'}}, indent=2) + '\n')
PYPUBLISHVERSION
    informational_version=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["publishProperties"]["InformationalVersion"])' "$publish_parent/properties/$directory.json")
    dotnet publish "src/$project/$project.csproj" --no-restore -m:1 -p:BuildInParallel=false -nr:false -c Release -r linux-x64 --self-contained true -o "$out/$directory" \
        -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false -p:PathMap="$root=/_/lain" \
        -p:InformationalVersion="$informational_version" -p:IncludeSourceRevisionInInformationalVersion=false -p:SourceRevisionId="$revision"
done
scripts/build-core.sh "$out/proxy/lain-proxy"
cp proxy/LICENSE.upstream "$out/proxy/LICENSE"
cp proxy/upstream.json proxy/source-manifest.json proxy/toolchain.json "$out/proxy/"
cp "$out/proxy/lain-proxy.manifest.json" "$out/proxy/build-manifest.json"
python3 scripts/prepare-core.py --destination "$publish_parent/prepared" > "$publish_parent/paths.json"
# Ship own adapter, explicit patchset and archive pin as bounded source evidence.
python3 - "$out/core-source-provenance" <<'PYSOURCE'
import json, pathlib, shutil, sys
root = pathlib.Path.cwd()
out = pathlib.Path(sys.argv[1])
manifest = json.loads((root / 'proxy/source-manifest.json').read_text())
paths = sorted(set([*manifest['buildInputs'], 'proxy/source-manifest.json']))
for name in paths:
    relative = pathlib.Path(name)
    if relative.is_absolute() or '..' in relative.parts:
        raise SystemExit('Unsafe Core provenance path')
    target = out / name
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(root / name, target)
PYSOURCE
python3 - "$out/proxy" <<'PYMANIFEST'
import hashlib, json, pathlib, sys
root = pathlib.Path(sys.argv[1])
build = json.loads((root / 'build-manifest.json').read_text())
source = json.loads((root / 'source-manifest.json').read_text())
if build['binarySha256'] != hashlib.sha256((root / 'lain-proxy').read_bytes()).hexdigest() or build['sourceManifestSha256'] != hashlib.sha256((root / 'source-manifest.json').read_bytes()).hexdigest():
    raise SystemExit('Core build sidecar must bind the exact binary and source')
json.dump({'format': 2, 'binary': 'lain-proxy',
 'sha256': build['binarySha256'], 'sourceManifestSha256': build['sourceManifestSha256'],
 'upstreamVersion': build['upstreamVersion'], 'upstreamCommit': build['upstreamCommit'],
 'upstreamSourceModified': build['upstreamSourceModified'], 'patchSetSha256': build['patchSetSha256']},
 (root / 'binary-manifest.json').open('w'), indent=2)
PYMANIFEST
# Final acceptance is separate from source/binary integrity. Never synthesize it.
python3 - "$out" "${LAIN_QUALIFICATION_FILE:-}" <<'PYGATE'
import hashlib, json, pathlib, shutil, sys
root, approved = pathlib.Path(sys.argv[1]), sys.argv[2]
qualified = False
if approved:
    report = json.loads(pathlib.Path(approved).read_text())
    actual = hashlib.sha256((root / 'proxy/lain-proxy').read_bytes()).hexdigest()
    source_hash = hashlib.sha256((root / 'proxy/source-manifest.json').read_bytes()).hexdigest()
    if report.get('passed') is not True or report.get('sha256') != actual or report.get('sourceManifestSha256') != source_hash:
        raise SystemExit('Qualification must pass for the exact built proxy and source manifest SHA-256')
    shutil.copyfile(approved, root / 'proxy/qualification.json')
    qualified = True
(root / 'gate-status.json').write_text(json.dumps({'backendQualified': qualified, 'releaseQualified': False,
    'status': 'awaiting-final-package-and-desktop-acceptance' if qualified else 'staged-awaiting-final-acceptance'}, indent=2))
PYGATE
cp packaging/lain packaging/lain.service.in packaging/lain.desktop.in "$out/packaging/"
cp scripts/install-linux.sh "$out/install-linux.sh"
python3 scripts/collect-notices.py "$out/notices" --prepared-adapter "$publish_parent/prepared/adapter"
python3 - "$out/notices/go/inventory.json" <<'PYNOTICES'
import json, pathlib, sys
if json.loads(pathlib.Path(sys.argv[1]).read_text())['missingEvidence']:
    raise SystemExit('Go notice evidence is incomplete; review before packaging')
PYNOTICES
cp -a skills/lain-usage "$out/skills/"
verify_frozen_inputs
python3 - "$out" "$expected" "$revision" "$publish_parent/properties" <<'PYBUILDINFO'
import hashlib, json, pathlib, runpy, shutil, subprocess, sys
out, sdk, revision, properties = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3], pathlib.Path(sys.argv[4])
root = pathlib.Path.cwd()
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
serialized_version = runpy.run_path(str(root / 'scripts/stage-component-refresh.py'))['serialized_version']
projects = {'cli': 'Lain.Cli', 'host': 'Lain.Host', 'desktop': 'Lain.Desktop'}
components, versions, provenance = {}, {}, []
for name, project in projects.items():
    record = json.loads((properties / (name + '.json')).read_text())
    if record['sourceCommit'] != revision or record['project'] != project:
        raise SystemExit('Publish property source/project mismatch: ' + name)
    version = record['publishProperties']['InformationalVersion']
    assembly = out / name / (project + '.dll')
    if serialized_version(version) not in assembly.read_bytes():
        raise SystemExit('Exact explicit informational version missing from assembly: ' + name)
    versions[name] = version
    lock = root / 'src' / project / 'packages.lock.json'
    components[name] = {'sourceCommit': revision, 'informationalVersion': version,
        'assemblySha256': digest(assembly), 'lockfileSha256': digest(lock),
        'localSharedAssemblies': {path.stem: digest(path) for path in sorted((out / name).glob('Lain.*.dll')) if path != assembly}}
    destination = out / 'source-provenance/publish-properties' / (name + '.json')
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(properties / (name + '.json'), destination)
    provenance.append({'path': str(destination.relative_to(out)), 'sha256': digest(destination)})
source_paths = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', revision], text=True).splitlines()
for name in source_paths:
    if (name.startswith('src/') and (name.endswith('.csproj') or name.endswith('/packages.lock.json'))) or name in ('Directory.Build.props', 'Directory.Build.targets', 'global.json', 'NuGet.Config', 'nuget.config'):
        destination = out / 'source-provenance/source' / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(subprocess.check_output(['git', 'show', revision + ':' + name]))
        provenance.append({'sourcePath': name, 'path': str(destination.relative_to(out)), 'sha256': digest(destination)})
provenance_path = out / 'source-provenance/inventory.json'
provenance_path.write_text(json.dumps({'format': 1, 'sourceCommit': revision, 'files': provenance,
    'scope': 'Frozen application project/lock/config inputs and exact explicit publish properties; full source is identified by Git commit'}, indent=2) + '\n')
proxy = out / 'proxy'
build = json.loads((proxy / 'build-manifest.json').read_text())
if build['binarySha256'] != digest(proxy / 'lain-proxy') or build['sourceManifestSha256'] != digest(proxy / 'source-manifest.json'):
    raise SystemExit('Core build provenance changed before build-info generation')
dirty = bool(subprocess.check_output(['git', 'status', '--porcelain'], text=True).strip())
info = {'format': 1, 'runtime': 'linux-x64', 'sdk': sdk, 'commit': revision, 'sourceDirty': dirty,
    'packagingCommit': revision, 'packagingSourceDirty': dirty, 'compileInputsFrozen': True,
    'componentSourceCommits': {name: revision for name in projects}, 'componentInformationalVersions': versions,
    'components': components, 'coreSourceCommit': revision, 'proxySha256': build['binarySha256'],
    'sourceManifestSha256': build['sourceManifestSha256'], 'coreBuildManifestSha256': digest(proxy / 'build-manifest.json'),
    'sourceProvenanceInventorySha256': digest(provenance_path),
    'coreBuild': {key: build[key] for key in ('goVersion', 'snapshotVersion', 'lainPatchVersion', 'upstreamVersion',
        'upstreamCommit', 'upstreamSourceModified', 'upstreamArchiveSha256', 'patchSetSha256')},
    'packagingToolSha256': {name: digest(root / 'scripts' / name) for name in
        ('publish-linux.sh', 'build-core.sh', 'prepare-core.py', 'build-core-verify.py', 'collect-notices.py', 'collect-go-notices.py')}}
if (proxy / 'qualification.json').is_file():
    info['qualificationSha256'] = digest(proxy / 'qualification.json')
(out / 'build-info.json').write_text(json.dumps(info, indent=2) + '\n')
PYBUILDINFO
(cd "$out" && find . -type f ! -name SHA256SUMS -print0 | LC_ALL=C sort -z | xargs -0 sha256sum > SHA256SUMS)
epoch=${SOURCE_DATE_EPOCH:-$(git log -1 --format=%ct)}
tar --sort=name --mtime="@$epoch" --owner=0 --group=0 --numeric-owner -cf - -C "$(dirname "$out")" "$(basename "$out")" | gzip -n > "$out.tar.gz"
sha256sum "$out.tar.gz" > "$out.tar.gz.sha256"
printf 'Staged %s.tar.gz (see gate-status.json for final acceptance status)\n' "$out"
