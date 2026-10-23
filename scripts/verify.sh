#!/usr/bin/env bash
# Synthetic verification only. Optional explicit binary paths enable process probes.
set -euo pipefail
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cd "$root"
export MSBUILDDISABLENODEREUSE=1
export PYTHONDONTWRITEBYTECODE=1
flags=(-m:1 -p:BuildInParallel=false -nr:false)
core=
host=
while (($#)); do
    case "$1" in
        --core) core=${2:?--core requires an absolute binary path}; shift 2;;
        --host) host=${2:?--host requires an absolute binary path}; shift 2;;
        --help) printf '%s\n' 'verify.sh [--core /absolute/lain-proxy] [--host /absolute/Lain.Host]' 'No real provider probes are run. Core/host options run synthetic process probes.'; exit 0;;
        *) echo "Unknown option: $1" >&2; exit 2;;
    esac
done
for binary in "$core" "$host"; do
    [[ -z "$binary" || ( "$binary" == /* && -x "$binary" ) ]] || { echo 'Binary paths must be absolute executables' >&2; exit 2; }
done
# Serial builds avoid exhausting memory and make failures directly visible.
dotnet build Lain.sln -c Release "${flags[@]}"
for project in tests/Lain.Accounts.Tests/Lain.Accounts.Tests.csproj tests/client-cli/ClientCli.Tests.csproj tests/models/Models.Tests.csproj; do
    dotnet build "$project" -c Release "${flags[@]}"
    dotnet run --project "$project" -c Release --no-build --no-restore
done
bash tests/quota/run.sh
dotnet test tests/usage/Lain.Usage.Tests.csproj -c Release "${flags[@]}"
python3 packaging/test-install.py
for suite in tests/backend tests/acceptance tests/live; do
    if [[ -d "$suite" ]]; then python3 -B -m unittest discover -s "$suite" -p 'test_*.py' -v; fi
done
report_dir=$(mktemp -d "${TMPDIR:-/tmp}/lain-verification.XXXXXXXX")
if [[ -n "$core" ]]; then
    [[ -f tests/backend/qualify.py ]] || { echo 'Backend qualification suite missing' >&2; exit 2; }
    python3 tests/backend/qualify.py --core "$core" --report "$report_dir/backend.json"
    python3 tests/acceptance/core_probe.py --core "$core" --report "$report_dir/core-acceptance.json"
fi
if [[ -n "$host" ]]; then python3 tests/acceptance/host_probe.py -- "$host"; fi
printf 'Synthetic verification passed. Reports (if requested): %s\n' "$report_dir"
