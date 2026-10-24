#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
go_bin="${LAIN_GO:-}"
if [[ -z "$go_bin" ]]; then
 if command -v go >/dev/null 2>&1; then go_bin="$(command -v go)";
 elif [[ -x "$HOME/.local/share/model-usage/toolchain/go/bin/go" ]]; then go_bin="$HOME/.local/share/model-usage/toolchain/go/bin/go";
 else echo 'Go 1.27.2 is required; set LAIN_GO to its absolute executable.' >&2; exit 1; fi
fi
expected_go="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["goVersion"])' "$root/proxy/toolchain.json")"
actual_go="$(GOTOOLCHAIN=local GOWORK=off "$go_bin" env GOVERSION)"
if [[ "$actual_go" != "$expected_go" ]]; then echo "Pinned compiler $expected_go required; found $actual_go" >&2; exit 1; fi
output="${1:-$root/artifacts/proxy/lain-proxy}"
[[ "$output" == /* ]] || { echo 'Output path must be absolute.' >&2; exit 1; }
mkdir -p -- "$(dirname -- "$output")"
umask 077
build_parent="$(mktemp -d "${TMPDIR:-/tmp}/lain-core-build.XXXXXXXX")"
trap 'rm -rf -- "$build_parent"' EXIT
python3 "$root/scripts/prepare-core.py" --destination "$build_parent/source" > "$build_parent/paths.json"
cd "$build_parent/source/adapter"
GOTOOLCHAIN=local GOWORK=off GOFLAGS= "$go_bin" build -mod=readonly -trimpath -buildvcs=false -o "$output" ./cmd/lain-proxy
sha256sum "$output"
python3 - "$root" "$output" "$go_bin" <<'PYBUILD'
import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path(sys.argv[1]);binary=pathlib.Path(sys.argv[2])
source_manifest_bytes=(root/"proxy/source-manifest.json").read_bytes()
source_manifest=json.loads(source_manifest_bytes)
upstream=json.loads((root/"proxy/upstream.json").read_text())
manifest={"schemaVersion":2,"binary":binary.name,"binarySha256":hashlib.sha256(binary.read_bytes()).hexdigest(),"sourceManifestSha256":hashlib.sha256(source_manifest_bytes).hexdigest(),"goVersion":subprocess.check_output([sys.argv[3],"version"],text=True).strip(),"snapshotVersion":source_manifest["snapshotVersion"],"lainPatchVersion":source_manifest["lainPatchVersion"],"upstreamVersion":upstream["version"],"upstreamCommit":upstream["commit"],"upstreamSourceModified":bool(upstream["patches"]),"upstreamArchiveSha256":upstream["archiveSha256"],"patchSetSha256":hashlib.sha256(json.dumps(upstream["patches"],sort_keys=True).encode()).hexdigest()}
binary.with_suffix(binary.suffix+".manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
PYBUILD
