#!/usr/bin/env bash
# Installs an already published package. Does not start or enable services.
set -euo pipefail
umask 077
package=${1:?Usage: install-linux.sh PACKAGE [PREFIX]}
prefix=${2:-"$HOME/.local"}
lain_home=${LAIN_HOME:-"$HOME/.local/share/lain"}
unit_dir=${LAIN_SYSTEMD_DIR:-"${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"}
data_dir=${LAIN_DESKTOP_DATA_DIR:-"$prefix/share"}
desktop_file="$data_dir/applications/lain.desktop"
icon_file="$data_dir/icons/lain.png"
for path in "$package" "$prefix" "$lain_home" "$unit_dir" "$data_dir"; do
    [[ "$path" == /* && "$path" != *$'\n'* && "$path" != *$'\r'* ]] || { echo 'All installation paths must be absolute and single-line' >&2; exit 2; }
done
# systemd rejects these literal characters in executable paths. Keep supported
# homes explicit instead of creating an installation with an unusable service.
case "$lain_home" in
    *[\\\"\'%]*|*$'\t'*) echo 'LAIN_HOME cannot contain quotes, backslashes, percent signs or tabs; spaces are supported' >&2; exit 2;;
esac
for part in cli/Lain.Cli host/Lain.Host desktop/Lain.Desktop proxy/lain-proxy; do
    [[ -x "$package/$part" ]] || { echo "Missing executable: $part" >&2; exit 2; }
done
[[ -f "$package/SHA256SUMS" && -f "$package/packaging/lain.service.in" && -f "$package/packaging/lain" && -f "$package/packaging/lain.desktop.in" && -f "$package/desktop/Assets/Brand/lain-cat.png" ]] || { echo 'Not a lain package with desktop branding' >&2; exit 2; }
(cd "$package" && sha256sum --check --strict SHA256SUMS >/dev/null)
# Refuse accidental ownership of another application or proxy installation.
[[ ! -e "$lain_home/app" || -f "$lain_home/app/.lain-package" ]] || { echo 'Existing app directory is not a lain package' >&2; exit 2; }
[[ ! -e "$prefix/bin/lain" || -f "$lain_home/app/.lain-package" ]] || { echo 'Existing lain launcher is not managed by this installation' >&2; exit 2; }
# Refuse foreign or user-customized launcher artwork/entries even when app/ is
# already managed. Updates only replace exact files previously written by lain.
python3 - "$lain_home/desktop-integration.json" "$desktop_file" "$icon_file" <<'PY'
import hashlib, json, pathlib, sys
manifest, *paths = map(pathlib.Path, sys.argv[1:])
try:
    records = json.loads(manifest.read_text()) if manifest.is_file() else {}
except (ValueError, OSError) as error:
    raise SystemExit('Cannot verify desktop integration ownership: ' + str(error))
for path in paths:
    if path.is_symlink():
        raise SystemExit('Refusing desktop integration symlink: ' + str(path))
    if path.exists() and (not path.is_file() or records.get(str(path)) != hashlib.sha256(path.read_bytes()).hexdigest()):
        raise SystemExit('Existing desktop integration is foreign or customized: ' + str(path))
PY
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
python3 - "$lain_home" "$prefix/bin/lain" "$unit_dir/lain.service" "$desktop_file" "$icon_file" <<'PY'
import hashlib, json, pathlib, shlex, sys
home, launcher, unit, desktop, icon = sys.argv[1:]
root = pathlib.Path(home) / 'app' / 'packaging'
wrapper = (root / 'lain').read_text().replace(': "${LAIN_HOME:=$HOME/.local/share/lain}"', 'if [ -z "${LAIN_HOME:-}" ]; then LAIN_HOME=' + shlex.quote(home) + '; fi')
pathlib.Path(launcher).write_text(wrapper)
pathlib.Path(launcher).chmod(0o700)
# systemd specifiers and quoted argument syntax have different escaping than sh.
escaped = home.replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%')
service = (root / 'lain.service.in').read_text().replace('@HOME@', escaped)
# WorkingDirectory is a single path directive, not a quoted argument list.
# Spaces/quotes are literal here; escape only systemd percent specifiers.
service = service.replace('@WORKING_DIRECTORY@', home.replace('%', '%%'))
pathlib.Path(unit).write_text(service)
pathlib.Path(unit).chmod(0o600)
# Desktop entries have their own string escaping, followed by Exec quoting.
# Never send these paths through a shell or rely on PATH to resolve `lain`.
def desktop_string(value):
    return value.replace('\\', '\\\\').replace('\t', '\\t')
def exec_argument(value):
    value = value.replace('%', '%%')
    for character in ('\\', '"', '`', '$'):
        value = value.replace(character, '\\' + character)
    return desktop_string('"' + value + '"')
entry = (root / 'lain.desktop.in').read_text()
entry = entry.replace('@EXEC@', exec_argument(launcher)).replace('@ICON@', desktop_string(icon))
pathlib.Path(desktop).parent.mkdir(parents=True, exist_ok=True)
pathlib.Path(icon).parent.mkdir(parents=True, exist_ok=True)
pathlib.Path(desktop).write_text(entry)
pathlib.Path(desktop).chmod(0o600)
pathlib.Path(icon).write_bytes((pathlib.Path(home) / 'app/desktop/Assets/Brand/lain-cat.png').read_bytes())
pathlib.Path(icon).chmod(0o600)
ownership = {path: hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest() for path in (desktop, icon)}
ownership_path = pathlib.Path(home) / 'desktop-integration.json'
ownership_path.write_text(json.dumps(ownership, indent=2) + '\n')
ownership_path.chmod(0o600)
PY
printf 'Installed lain in %s\nLauncher: %s/bin/lain\nService: %s/lain.service\n' "$lain_home" "$prefix" "$unit_dir"
printf 'To start: systemctl --user daemon-reload && systemctl --user start lain.service\n'
printf 'Desktop entry: %s\nIcon: %s\n' "$desktop_file" "$icon_file"
