#!/bin/sh
# Deliberate offline-source build step. Does not install or start a client.
set -eu
if [ "$#" -ne 1 ]; then
    echo "usage: $0 PREPARED_SOURCE_DIRECTORY" >&2
    exit 2
fi
cd "$1/codex-rs"
if [ ! -f ../lain-picker-source.json ]; then
    echo "source must first be reconstructed by prepare-codex-picker.py" >&2
    exit 2
fi
case "$(rustc --version)" in
    "rustc 1.99.1 "*) ;;
    *) echo "the pinned Codex source requires Rust 1.99.1" >&2; exit 2 ;;
esac
# Keep Cargo and rustc work within one serial lane; honor an explicitly selected cache.
export CARGO_BUILD_JOBS=1
export CARGO_INCREMENTAL=0
export CARGO_PROFILE_DEV_SMALL_DEBUG=0
exec cargo build --locked --profile dev-small -p codex-cli --bin codex -j 1
