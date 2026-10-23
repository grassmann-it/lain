#!/usr/bin/env bash
# Installs an already published package. Does not start or enable services.
set -euo pipefail
umask 077
package=${1:?Usage: install-linux.sh PACKAGE [PREFIX]}
prefix=${2:-"$HOME/.local"}
lain_home=${LAIN_HOME:-"$HOME/.local/share/lain"}
unit_dir=${LAIN_SYSTEMD_DIR:-"${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"}
for path in "$package" "$prefix" "$lain_home" "$unit_dir"; do
    [[ "$path" == /* && "$path" != *$'\n'* && "$path" != *$'\r'* ]] || { echo 'All installation paths must be absolute and single-line' >&2; exit 2; }
done
for part in cli/Lain.Cli host/Lain.Host desktop/Lain.Desktop proxy/lain-proxy; do
    [[ -x "$package/$part" ]] || { echo "Missing executable: $part" >&2; exit 2; }
done
[[ -f "$package/SHA256SUMS" && -f "$package/packaging/lain.service.in" && -f "$package/packaging/lain" ]] || { echo 'Not a lain package' >&2; exit 2; }
(cd "$package" && sha256sum --check --strict SHA256SUMS >/dev/null)
# Refuse accidental ownership of another application or proxy installation.
[[ ! -e "$lain_home/app" || -f "$lain_home/app/.lain-package" ]] || { echo 'Existing app directory is not a lain package' >&2; exit 2; }
[[ ! -e "$prefix/bin/lain" || -f "$lain_home/app/.lain-package" ]] || { echo 'Existing lain launcher is not managed by this installation' >&2; exit 2; }
mkdir -p "$lain_home" "$prefix/bin" "$unit_dir"
chmod 700 "$lain_home"
stage=$(mktemp -d "$lain_home/.app.XXXXXXXX")
trap 'if [[ -n "${stage:-}" && -d "$stage" ]]; then rm -rf -- "$stage"; fi' EXIT
cp -a "$package/." "$stage/"
chmod -R go-rwx "$stage"
touch "$stage/.lain-package"
# Preserve a rollback copy of the prior application; user data lives outside app.
if [[ -e "$lain_home/app.previous" ]]; then
    echo 'Remove or archive app.previous before another update' >&2; exit 2
fi
if [[ -e "$lain_home/app" ]]; then mv "$lain_home/app" "$lain_home/app.previous"; fi
mv "$stage" "$lain_home/app"
stage=
# Embed the chosen private home while keeping an explicit runtime override.
python3 - "$lain_home" "$prefix/bin/lain" "$unit_dir/lain.service" <<'PY'
import pathlib, shlex, sys
home, launcher, unit = sys.argv[1:]
root = pathlib.Path(home) / 'app' / 'packaging'
wrapper = (root / 'lain').read_text().replace(': "${LAIN_HOME:=$HOME/.local/share/lain}"', 'if [ -z "${LAIN_HOME:-}" ]; then LAIN_HOME=' + shlex.quote(home) + '; fi')
pathlib.Path(launcher).write_text(wrapper)
pathlib.Path(launcher).chmod(0o700)
# systemd specifiers and quoted argument syntax have different escaping than sh.
escaped = home.replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%')
service = (root / 'lain.service.in').read_text().replace('@HOME@', escaped)
# WorkingDirectory supports quoted values too.
service = service.replace('WorkingDirectory=' + escaped, 'WorkingDirectory="' + escaped + '"')
pathlib.Path(unit).write_text(service)
pathlib.Path(unit).chmod(0o600)
PY
printf 'Installed lain in %s\nLauncher: %s/bin/lain\nService: %s/lain.service\n' "$lain_home" "$prefix" "$unit_dir"
printf 'To start: systemctl --user daemon-reload && systemctl --user start lain.service\n'
