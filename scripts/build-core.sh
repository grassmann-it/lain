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
actual_go="$("$go_bin" env GOVERSION)"
if [[ "$actual_go" != "$expected_go" ]]; then echo "Pinned compiler $expected_go required; found $actual_go" >&2; exit 1; fi
output="${1:-$root/artifacts/proxy/lain-proxy}"
mkdir -p -- "$(dirname -- "$output")"
python3 "$root/scripts/build-core-verify.py"
cd "$root/proxy/core"
GOTOOLCHAIN=local "$go_bin" build -mod=readonly -trimpath -buildvcs=false -o "$output" ./cmd/lain-proxy
sha256sum "$output"

python3 - "$root" "$output" "$go_bin" <<'PYBUILD'
import hashlib,json,pathlib,subprocess,sys
root=pathlib.Path(sys.argv[1]);binary=pathlib.Path(sys.argv[2])
manifest={"schemaVersion":1,"binary":binary.name,"binarySha256":hashlib.sha256(binary.read_bytes()).hexdigest(),"sourceManifestSha256":hashlib.sha256((root/"proxy/source-manifest.json").read_bytes()).hexdigest(),"goVersion":subprocess.check_output([sys.argv[3],"version"],text=True).strip(),"snapshotVersion":"7.3.17-parent-request.3-model-compat.3-startup-readiness.1","lainPatchVersion":1}
binary.with_suffix(binary.suffix+".manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
PYBUILD
