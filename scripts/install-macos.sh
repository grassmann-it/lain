#!/usr/bin/env bash
set -euo pipefail
base=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
exec python3 "$base/packaging/platform-installers/install.py" --platform macos "$@"
