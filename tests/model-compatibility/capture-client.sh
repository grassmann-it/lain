#!/bin/sh
set -eu
if [ "$#" -ne 2 ]; then
  echo 'usage: capture-client.sh ABSOLUTE_CODEX_BINARY DISPOSABLE_TEMP_ROOT' >&2
  exit 2
fi
fixture_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec python3 "$fixture_dir/capture-client.py" \
  --codex "$1" --temporary-root "$2" --report "$fixture_dir/fixtures/client-capture.json"
