#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
project="$root/tests/account-connect-fireworks/Fireworks.Tests.csproj"
# Run only in the assigned serial build/runtime lane. The committed lock is mandatory.
MSBUILDDISABLENODEREUSE=1 dotnet build "$project" -r linux-x64 -m:1 -p:BuildInParallel=false -p:RestoreLockedMode=true -nr:false --verbosity quiet
MSBUILDDISABLENODEREUSE=1 dotnet run --project "$project" -r linux-x64 --no-build --no-restore --verbosity quiet
