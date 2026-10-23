#!/usr/bin/env bash
# Synthetic expansion checks; no live credentials, provider calls, installation, or GUI.
set -Eeuo pipefail

usage() {
  cat <<'HELP'
Usage: scripts/verify-expansion.sh [--core /absolute/lain-proxy]
                                 [--host /absolute/Lain.Host[.dll]]
                                 [--report-dir /absolute/new-directory]

Builds and runs integrated .NET expansion checks serially. Optional Core and
Host process probes use a fresh Linux user/network namespace with loopback only.
--core also runs the existing durability gate for that new binary exactly once.
GUI checks are separate: tests/desktop/run-private-xvfb.sh (private Xvfb only).
HELP
}
fail() { printf '%s\n' "$1" >&2; exit 2; }
core_path=
host_path=
report_dir=
while (($#)); do
  case "$1" in
    --core)
      (($# >= 2)) || fail '--core requires an absolute executable path.'
      [[ -z "$core_path" ]] || fail '--core may be supplied only once.'
      core_path=$2; shift 2;;
    --host)
      (($# >= 2)) || fail '--host requires an absolute executable or DLL path.'
      [[ -z "$host_path" ]] || fail '--host may be supplied only once.'
      host_path=$2; shift 2;;
    --report-dir)
      (($# >= 2)) || fail '--report-dir requires an absolute new directory.'
      [[ -z "$report_dir" ]] || fail '--report-dir may be supplied only once.'
      report_dir=$2; shift 2;;
    --help|-h) usage; exit 0;;
    *) fail "Unknown option: $1";;
  esac
done

root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"
for dependency in dotnet python3 git tee; do
  command -v "$dependency" >/dev/null || fail "Missing dependency: $dependency"
done
[[ -z "$core_path" || ( "$core_path" == /* && -f "$core_path" && -x "$core_path" ) ]] || fail '--core must be an absolute executable file.'
host_command=()
if [[ -n "$host_path" ]]; then
  [[ "$host_path" == /* && -f "$host_path" && -r "$host_path" ]] || fail '--host must be an absolute readable file.'
  if [[ "$host_path" == *.dll ]]; then host_command=(dotnet "$host_path")
  else
    [[ -x "$host_path" ]] || fail '--host must be an executable or .dll file.'
    host_command=("$host_path")
  fi
fi

# Commands are passed as argument vectors. Never eval caller-supplied paths.
isolated() {
  unshare --user --map-root-user --net -- bash -c 'set -euo pipefail
ip link set lo up
exec "$@"' lain-expansion-namespace "$@"
}
if [[ -n "$core_path" || -n "$host_path" ]]; then
  [[ $(uname -s) == Linux ]] || fail 'Process probes require Linux user/network namespaces.'
  for dependency in unshare ip; do
    command -v "$dependency" >/dev/null || fail "Missing process-isolation dependency: $dependency"
  done
  isolated true || fail 'Cannot create isolated loopback. No fallback to installed Host/Core ports is allowed.'
fi

umask 077
if [[ -n "$report_dir" ]]; then
  [[ "$report_dir" == /* && ! -e "$report_dir" ]] || fail '--report-dir must be an absolute directory that does not yet exist.'
  mkdir -p -- "$report_dir"
else
  report_dir=$(mktemp -d "${TMPDIR:-/tmp}/lain-expansion.XXXXXXXX")
fi
trap 'printf "Expansion verification failed; retained evidence: %s\n" "$report_dir" >&2' ERR
export MSBUILDDISABLENODEREUSE=1 PYTHONDONTWRITEBYTECODE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 -p:BuildInParallel=false -nr:false)

python3 - "$root" "$report_dir" "$core_path" "$host_path" <<'PY'
import datetime, hashlib, json, pathlib, subprocess, sys
root, reports = map(pathlib.Path, sys.argv[1:3])
artifacts = {}
for name, value in zip(('core', 'host'), sys.argv[3:5]):
    if value:
        path = pathlib.Path(value)
        artifacts[name] = {'path': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
result = {
    'schemaVersion': 1, 'passed': False, 'syntheticOnly': True, 'guiExecuted': False,
    'startedAt': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'sourceRevision': subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip(),
    'trackedSourceDirty': subprocess.run(['git', '-C', str(root), 'diff', '--quiet', 'HEAD']).returncode != 0,
    'requestedArtifacts': artifacts,
    'note': 'Synthetic suite result only; live provider, GUI, installation and release qualification are separate.'
}
(reports / 'result.json').write_text(json.dumps(result, indent=2) + '\n')
PY

run_logged() {
  local label=$1
  shift
  printf 'Checking %s\n' "$label"
  "$@" 2>&1 | tee "$report_dir/$label.log"
}
run_project() {
  local label=$1 project=$2
  run_logged "$label-build" dotnet build "$project" -c Release --nologo "${flags[@]}"
  run_logged "$label" dotnet run --project "$project" -c Release --no-build --no-restore
}

# Compile the complete product first. Existing packaging/account/model/quota
# baseline suites are deliberately not rerun here; scripts/verify.sh owns them.
run_logged solution-build dotnet build Lain.sln -c Release --nologo "${flags[@]}"
run_project client-cli tests/client-cli/ClientCli.Tests.csproj
run_logged usage-build dotnet build tests/usage/Lain.Usage.Tests.csproj -c Release --nologo "${flags[@]}"
run_logged usage dotnet test tests/usage/Lain.Usage.Tests.csproj -c Release --no-build --no-restore \
  "${flags[@]}" --logger 'trx;LogFileName=usage.trx' --results-directory "$report_dir/usage-results"
run_project inspector tests/inspector/Inspector.Tests.csproj
run_project inspector-independent tests/inspection/host/InspectorIntegration.csproj
run_project provider-policy tests/provider-policy/ProviderPolicy.Tests.csproj
run_project provider-policy-independent tests/inspection/policy/PolicyIntegration.csproj
run_project provider-transport tests/provider-transport/ProviderTransport.Tests.csproj
run_project providers-direct tests/providers-direct/DirectApi.Tests.csproj
run_project provider-trials tests/provider-trials/ProviderTrials.Tests.csproj
run_project gateways tests/gateways/Gateways.Tests.csproj

# These wrappers build their temporary linked-source project with the same
# serialized flags and execute with --no-build --no-restore.
run_logged providers-nvidia-step bash tests/providers-nvidia-step/run.sh
run_logged providers-poe-opencode bash tests/providers-poe-opencode/run.sh
run_logged providers-synthetic-xai bash tests/providers-synthetic-xai/run.sh
run_logged direct-manifests python3 -B tests/providers-direct/test_manifests.py
run_logged inspection-contracts python3 -B -m unittest discover -s tests/inspection -p 'test_*.py' -v

if [[ -n "$core_path" ]]; then
  # A new Core must retain the existing identity/durability gate. Reuse the
  # verified frozen binary for independent probes and keep its full journal.
  qualified_core="$report_dir/lain-proxy-qualified"
  run_logged core-qualification isolated python3 -B tests/backend/qualify.py --core "$core_path" \
    --report "$report_dir/core-qualification.json" --qualified-artifact "$qualified_core" \
    --batch-fixture "$report_dir/core-journal.json"
  run_logged core-acceptance isolated python3 -B tests/acceptance/core_probe.py --core "$qualified_core" --report "$report_dir/core-acceptance.json"
  run_logged core-inspector isolated python3 -B tests/inspection/core_probe.py --core "$qualified_core" --report "$report_dir/core-inspector.json"
  run_logged core-usage-replay dotnet run --project tests/usage/Lain.Usage.Tests.csproj -c Release --no-build --no-restore \
    -- --ingest-journal "$report_dir/core-journal.json" "$report_dir/core-usage.db"
fi
if ((${#host_command[@]})); then
  # The test's fixed 18417/18418 ports exist only inside this namespace. Installed
  # services remain visible on the user's original network and are untouched.
  run_logged host-endpoints isolated python3 -B tests/acceptance/host_probe.py -- "${host_command[@]}"
fi

python3 - "$report_dir/result.json" <<'PY'
import datetime, json, pathlib, sys
path = pathlib.Path(sys.argv[1])
result = json.loads(path.read_text())
result.update(passed=True, finishedAt=datetime.datetime.now(datetime.timezone.utc).isoformat())
path.write_text(json.dumps(result, indent=2) + '\n')
PY
printf 'Synthetic expansion checks passed. Evidence: %s\n' "$report_dir"
printf '%s\n' 'GUI remains separate: tests/desktop/run-private-xvfb.sh (private Xvfb only).'
