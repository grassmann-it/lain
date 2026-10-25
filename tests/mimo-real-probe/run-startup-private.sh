#!/usr/bin/env bash
# Startup-only outer isolation. Never builds and never changes HOME's value.
set -euo pipefail
if [[ $# != 2 || ${LAIN_MIMO_PROBE_SLOT:-0} != 1 \
  || ! ${LAIN_MIMO_PROBE_HOST_SHA256:-} =~ ^[0-9a-fA-F]{64}$ \
  || ! ${LAIN_MIMO_PROBE_SOURCE_COMMIT:-} =~ ^[0-9a-f]{40}$ ]]; then
  echo 'Assigned slot, reviewed Host SHA/source commit, absolute probe DLL and new evidence directory required.' >&2
  exit 2
fi
for dependency in bwrap timeout python3 sha256sum; do
  command -v "$dependency" >/dev/null || { echo "Missing dependency: $dependency" >&2; exit 2; }
done
# Refuse a shell that already substituted HOME. Do not repair it by assignment.
python3 - <<'PY'
import os, pwd
if os.environ.get('HOME') != pwd.getpwuid(os.getuid()).pw_dir:
    raise SystemExit('Run from the original user environment; HOME must remain unchanged.')
PY
[[ $1 = /* && $2 = /* ]] || exit 2
probe=$(realpath -e -- "$1")
output=$(realpath -m -- "$2")
runner=$(realpath -e -- "$(dirname -- "${BASH_SOURCE[0]}")/run-private.sh")
artifact_directory=$(dirname -- "$probe")
[[ -f "$probe" && -f "$artifact_directory/Lain.Host.dll" && ! -e "$output" \
  && "$output" == "$HOME/.cache/"* && "$output" != "$artifact_directory" \
  && "$output" != "$artifact_directory/"* ]] || exit 2
host_sha=$(sha256sum -- "$artifact_directory/Lain.Host.dll")
[[ ${host_sha%% *} == "${LAIN_MIMO_PROBE_HOST_SHA256,,}" ]] || { echo 'Reviewed Host hash differs' >&2; exit 2; }
umask 077
mkdir -p -- "$output"
scratch=$(mktemp -d "$output/private-XXXXXXXX")
cleanup() { rm -rf -- "$scratch"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -p "$scratch/home/.cache/lain-build-tmp" "$scratch/tmp"
printf '%s\n' "$LAIN_MIMO_PROBE_SOURCE_COMMIT" >"$output/source-commit.txt"
sha256sum -- "$runner" "${BASH_SOURCE[0]}" "$probe" "$artifact_directory/Lain.Host.dll" \
  "$artifact_directory/Lain.Core.dll" >"$output/inputs.sha256"
# Like N09, hide the real home and desktop sockets while preserving HOME itself.
# Keep the outer network namespace: the public console may load ordinary resources.
# The private /tmp filesystem owns this display's X11 pathname socket. Unique
# Xauthority still protects the X11 abstract socket in the shared network namespace.
set +e
timeout --signal=TERM --kill-after=5s 115s \
  bwrap --die-with-parent --new-session --unshare-pid --unshare-ipc --unshare-uts \
    --ro-bind / / --proc /proc --dev /dev --tmpfs /tmp --tmpfs /run \
    --bind "$scratch/home" "$HOME" --bind "$output" /mnt \
    --bind "$scratch/tmp" /mnt/tmp \
    --ro-bind "$artifact_directory" /mnt/artifacts --ro-bind "$runner" /mnt/run-private.sh \
    --chdir /mnt --clearenv --setenv PATH /usr/bin:/bin --setenv HOME "$HOME" \
    --setenv LANG C.UTF-8 --setenv DOTNET_PROCESSOR_COUNT 1 \
    --setenv DOTNET_CLI_TELEMETRY_OPTOUT 1 \
    --setenv LAIN_MIMO_PROBE_SLOT 1 \
    --setenv LAIN_MIMO_PROBE_SHORT_TMPDIR /mnt/tmp \
    --setenv LAIN_MIMO_PROBE_HOST_SHA256 "$LAIN_MIMO_PROBE_HOST_SHA256" \
    /usr/bin/bash -c 'mkdir -m 1777 /tmp/.X11-unix; exec /usr/bin/bash /mnt/run-private.sh --startup-only "$1" /mnt/probe-evidence' \
    lain-mimo-startup "/mnt/artifacts/$(basename -- "$probe")" \
  >"$output/supervisor.log" 2>&1
result=$?
set -e
printf '%s\n' "$result" >"$output/exit-code.txt"
exit "$result"
