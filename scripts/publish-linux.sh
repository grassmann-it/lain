#!/usr/bin/env bash
# Produce a self-contained, versioned linux-x64 package without installing it.
set -euo pipefail
umask 077
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
out=${1:-"$root/artifacts/lain-linux-x64"}
[[ "$out" == /* && ! -e "$out" ]] || { echo 'Output must be an absolute, nonexistent directory' >&2; exit 2; }
cd "$root"
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
    dotnet publish "src/$project/$project.csproj" --no-restore -m:1 -p:BuildInParallel=false -nr:false -c Release -r linux-x64 --self-contained true -o "$out/$directory" \
        -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false -p:PathMap="$root=/_/lain"
done
scripts/build-core.sh "$out/proxy/lain-proxy"
cp proxy/LICENSE.upstream "$out/proxy/LICENSE"
cp proxy/upstream.json proxy/source-manifest.json proxy/toolchain.json "$out/proxy/"
cp "$out/proxy/lain-proxy.manifest.json" "$out/proxy/build-manifest.json"
notice_parent=$(mktemp -d "${TMPDIR:-/tmp}/lain-publish-notices.XXXXXXXX")
trap 'rm -rf -- "$notice_parent"' EXIT
python3 scripts/prepare-core.py --destination "$notice_parent/prepared" > "$notice_parent/paths.json"
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
python3 scripts/collect-notices.py "$out/notices" --prepared-adapter "$notice_parent/prepared/adapter"
python3 - "$out/notices/go/inventory.json" <<'PYNOTICES'
import json, pathlib, sys
if json.loads(pathlib.Path(sys.argv[1]).read_text())['missingEvidence']:
    raise SystemExit('Go notice evidence is incomplete; review before packaging')
PYNOTICES
cp -a skills/lain-usage "$out/skills/"
python3 - "$out/build-info.json" "$expected" <<'PY'
import json, subprocess, sys
json.dump({'format':1,'runtime':'linux-x64','sdk':sys.argv[2],
 'commit':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
 'sourceDirty':bool(subprocess.check_output(['git','status','--porcelain'],text=True).strip())},open(sys.argv[1],'w'),indent=2)
PY
(cd "$out" && find . -type f ! -name SHA256SUMS -print0 | LC_ALL=C sort -z | xargs -0 sha256sum > SHA256SUMS)
epoch=${SOURCE_DATE_EPOCH:-$(git log -1 --format=%ct)}
tar --sort=name --mtime="@$epoch" --owner=0 --group=0 --numeric-owner -cf - -C "$(dirname "$out")" "$(basename "$out")" | gzip -n > "$out.tar.gz"
sha256sum "$out.tar.gz" > "$out.tar.gz.sha256"
printf 'Staged %s.tar.gz (see gate-status.json for final acceptance status)\n' "$out"
