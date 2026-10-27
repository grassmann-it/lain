#!/usr/bin/env bash
# Installs an already published package. Fresh installs enable login startup;
# starting the service is deferred to opening lain or an explicit service start.
set -euo pipefail
umask 077
package=${1:?Usage: install-linux.sh PACKAGE [PREFIX]}
prefix=${2:-"$HOME/.local"}
lain_home=${LAIN_HOME:-"$HOME/.local/share/lain"}
unit_dir=${LAIN_SYSTEMD_DIR:-"${XDG_CONFIG_HOME:-$HOME/.config}/systemd/user"}
data_dir=${LAIN_DESKTOP_DATA_DIR:-"$prefix/share"}
desktop_file="$data_dir/applications/lain.desktop"
icon_file="$data_dir/icons/lain.png"
login_autostart=${LAIN_LOGIN_AUTOSTART:-1}
[[ "$login_autostart" == 0 || "$login_autostart" == 1 ]] || { echo 'LAIN_LOGIN_AUTOSTART must be 0 or 1' >&2; exit 2; }
for path in "$package" "$prefix" "$lain_home" "$unit_dir" "$data_dir"; do
    [[ "$path" == /* && "$path" != *$'\n'* && "$path" != *$'\r'* ]] || { echo 'All installation paths must be absolute and single-line' >&2; exit 2; }
done
python3 - "$package" "$prefix/bin/lain" "$lain_home" "$lain_home/app" "$lain_home/app.previous" "$unit_dir/lain.service" "$desktop_file" "$icon_file" <<'PYPATHS'
import pathlib, sys
for name in sys.argv[1:]:
    p = pathlib.Path(name)
    for ancestor in (p, *p.parents):
        if ancestor.is_symlink(): raise SystemExit('Installation paths cannot contain symlinks: ' + str(ancestor))
PYPATHS
# systemd rejects these literal characters in executable paths. Keep supported
# homes explicit instead of creating an installation with an unusable service.
case "$lain_home" in
    *[\\\"\'%]*|*$'\t'*) echo 'LAIN_HOME cannot contain quotes, backslashes, percent signs or tabs; spaces are supported' >&2; exit 2;;
esac
# Legacy packages without metadata remain Gateway; new packages declare their mode.
mode=$(python3 - "$package" <<'PYMODE'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1])
p = root / 'package-metadata.json'
mode = json.loads(p.read_text())['runtimeMode'] if p.is_file() else 'gateway'
if mode not in ('monitor', 'gateway'): raise SystemExit('Invalid package runtimeMode')
if mode == 'monitor' and (root / 'proxy').exists(): raise SystemExit('Monitor package contains Core directory')
print(mode)
PYMODE
)
parts=(cli/Lain.Cli host/Lain.Host desktop/Lain.Desktop)
if [[ "$mode" == gateway ]]; then parts+=(proxy/lain-proxy); fi
for part in "${parts[@]}"; do
    [[ -x "$package/$part" ]] || { echo "Missing executable: $part" >&2; exit 2; }
done
[[ -f "$package/SHA256SUMS" && -f "$package/packaging/lain.service.in" && -f "$package/packaging/lain" && -f "$package/packaging/lain.desktop.in" && -f "$package/desktop/Assets/Brand/lain-cat.png" ]] || { echo 'Not a lain package with desktop branding' >&2; exit 2; }
# Verify all files, reject traversal/symlinks and files omitted from the checksum inventory.
python3 - "$package" <<'PYINTEGRITY'
import hashlib, pathlib, re, sys
root = pathlib.Path(sys.argv[1])
listed = set()
for line in (root / 'SHA256SUMS').read_text().splitlines():
    match = re.fullmatch(r'([0-9a-f]{64})  (.+)', line)
    if not match: raise SystemExit('Invalid SHA256SUMS record')
    digest, name = match.groups()
    p = pathlib.PurePosixPath(name)
    if p.is_absolute() or '..' in p.parts or str(p) in listed: raise SystemExit('Unsafe/duplicate package path')
    listed.add(str(p))
    path = root / p
    if path.is_symlink() or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
        raise SystemExit('Package integrity failure: ' + name)
actual = set()
for path in root.rglob('*'):
    if path.is_symlink(): raise SystemExit('Package symlinks are unsupported')
    if path.is_file() and path.name != 'SHA256SUMS': actual.add(str(path.relative_to(root)))
if actual != listed: raise SystemExit('Package inventory does not cover exact files')
PYINTEGRITY
# A package change must never silently remove an installed Core or change intent.
if [[ "$mode" == monitor && -e "$lain_home/app/proxy/lain-proxy" ]]; then
    echo 'Refusing Gateway-to-Monitor package replacement: retain Gateway package and change runtime mode explicitly' >&2; exit 2
fi
fresh=false
[[ -e "$lain_home/configuration.json" || -e "$lain_home/initial-runtime-mode" || -e "$lain_home/app" ]] || fresh=true
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
python3 - "$lain_home" "$unit_dir/lain.service" "$fresh" "$login_autostart" <<'PYREGISTRATION'
import hashlib, json, pathlib, sys
home, unit, fresh, enabled = sys.argv[1:]
root, registration = pathlib.Path(home), pathlib.Path(unit)
if registration.exists():
    metadata_path = root / 'installation.json'
    if metadata_path.is_symlink(): raise SystemExit('Installation metadata cannot be a symlink')
    if metadata_path.is_file():
        metadata = json.loads(metadata_path.read_text())
        if (metadata.get('registrationPath') != unit or
            metadata.get('registrationSha256') != hashlib.sha256(registration.read_bytes()).hexdigest()):
            raise SystemExit('Existing service integration is foreign or customized')
    else:
        template = root / 'app/packaging/lain.service.in'
        if not template.is_file(): raise SystemExit('Existing service integration is not owned by this installation')
        escaped = home.replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%')
        expected = template.read_text().replace('@HOME@', escaped).replace('@WORKING_DIRECTORY@', home.replace('%', '%%'))
        if registration.read_text() != expected: raise SystemExit('Existing service integration is foreign or customized')
link = registration.parent / 'default.target.wants/lain.service'
if fresh == 'true' and enabled == '1':
    for ancestor in (link.parent, *link.parent.parents):
        if ancestor.is_symlink(): raise SystemExit('Login integration directories cannot be symlinks')
    if (link.exists() or link.is_symlink()) and (not link.is_symlink() or link.resolve() != registration):
        raise SystemExit('Existing login startup integration is foreign or customized')
PYREGISTRATION
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
if [[ "$fresh" == true ]]; then printf '%s\n' "$mode" > "$lain_home/initial-runtime-mode"; fi
# Embed the chosen private home while keeping an explicit runtime override.
python3 - "$lain_home" "$prefix/bin/lain" "$unit_dir/lain.service" "$desktop_file" "$icon_file" "$fresh" "$login_autostart" "$mode" <<'PY'
import hashlib, json, pathlib, re, shlex, sys
home, launcher, unit, desktop, icon, fresh, login_autostart, mode = sys.argv[1:]
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
substitutions = {'EXEC': exec_argument(launcher), 'ICON': desktop_string(icon)}
entry = re.sub(r'@(EXEC|ICON)@', lambda match: substitutions[match[1]], entry)
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
# Enable the user unit for the next login without starting processes or requiring
# a running user bus during installation. Updates preserve the user's choice.
login_link = pathlib.Path(unit).parent / 'default.target.wants' / 'lain.service'
if fresh == 'true' and login_autostart == '1':
    for ancestor in (login_link.parent, *login_link.parent.parents):
        if ancestor.is_symlink(): raise SystemExit('Login integration directories cannot be symlinks')
    login_link.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    if login_link.exists() or login_link.is_symlink():
        if not login_link.is_symlink() or login_link.resolve() != pathlib.Path(unit):
            raise SystemExit('Existing login startup integration is foreign or customized')
    else:
        login_link.symlink_to(unit)
enabled = login_link.is_symlink() and login_link.resolve() == pathlib.Path(unit)
app = pathlib.Path(home) / 'app'
metadata = {'schemaVersion': 1, 'platform': 'linux', 'installationRoot': home,
    'binaryRoot': str(app), 'hostPath': str(app / 'host/Lain.Host'),
    'cliPath': str(app / 'cli/Lain.Cli'), 'desktopPath': str(app / 'desktop/Lain.Desktop'),
    'proxyPath': str(app / 'proxy/lain-proxy') if mode == 'gateway' else None,
    'manager': 'systemd', 'serviceName': 'lain.service', 'registrationPath': unit,
    'loginAutostart': enabled,
    'registrationSha256': hashlib.sha256(pathlib.Path(unit).read_bytes()).hexdigest()}
metadata_path = pathlib.Path(home) / 'installation.json'
temporary = metadata_path.with_name('installation.json.new')
with temporary.open('x') as stream:
    json.dump(metadata, stream, indent=2)
    stream.write('\n')
temporary.chmod(0o600)
temporary.replace(metadata_path)
PY
printf 'Installed lain in %s\nLauncher: %s/bin/lain\nService: %s/lain.service\n' "$lain_home" "$prefix" "$unit_dir"
printf 'To start: systemctl --user daemon-reload && systemctl --user start lain.service\n'
if [[ "$fresh" == true && "$login_autostart" == 1 ]]; then printf 'Background service enabled for the next user login.\n'; fi
printf 'Desktop entry: %s\nIcon: %s\n' "$desktop_file" "$icon_file"
