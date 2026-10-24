#!/usr/bin/env python3
"""Exercise the real installer with a temporary fake package; starts no service."""
from pathlib import Path
import hashlib
import os
import shutil
import stat
import subprocess
import tempfile

root = Path(__file__).resolve().parent.parent
with tempfile.TemporaryDirectory(prefix="lain-package-test-") as temporary:
    temporary = Path(temporary)
    package = temporary / "package"
    package.mkdir()
    for relative in ("cli/Lain.Cli", "host/Lain.Host", "desktop/Lain.Desktop", "proxy/lain-proxy"):
        path = package / relative
        path.parent.mkdir(exist_ok=True)
        path.write_text('#!/bin/sh\nprintf "%s\\n" "' + relative + '" "$LAIN_HOME" "$@"\n')
        path.chmod(0o755)
    shutil.copytree(root / "packaging", package / "packaging")
    brand = package / "desktop/Assets/Brand"
    brand.mkdir(parents=True)
    shutil.copyfile(root / "src/Lain.Desktop/Assets/Brand/lain-cat.png", brand / "lain-cat.png")
    (package / "SHA256SUMS").write_text("".join(
        hashlib.sha256(path.read_bytes()).hexdigest() + "  " + str(path.relative_to(package)) + "\n"
        for path in sorted(package.rglob("*")) if path.is_file() and path.name != "SHA256SUMS"))
    home = temporary / "data with spaces"
    prefix = temporary / "prefix"
    units = temporary / "units"
    env = dict(os.environ, LAIN_HOME=str(home), LAIN_SYSTEMD_DIR=str(units))
    env.pop('LAIN_DESKTOP_DATA_DIR', None)
    command = ["bash", str(root / "scripts/install-linux.sh"), str(package), str(prefix)]

    def run(args, **options):
        return subprocess.run(args, env=env, capture_output=True, text=True, **options)

    for character in ("'", '"', "%", "\\", "\t"):
        unsupported = temporary / ("unsupported" + character + "path")
        env["LAIN_HOME"] = str(unsupported)
        result = run(command)
        assert result.returncode != 0 and not unsupported.exists(), result.stderr
        assert not prefix.exists() and not units.exists()
    env["LAIN_HOME"] = str(home)
    result = run(command)
    assert result.returncode == 0, result.stderr
    del env["LAIN_HOME"]
    launcher = str(prefix / "bin/lain")
    result = run([launcher, "status", "--json", "two words"], cwd="/")
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == ["cli/Lain.Cli", str(home), "status", "--json", "two words"]
    result = run([launcher, "ui", "two words"], cwd="/")
    assert result.stdout.splitlines() == ["desktop/Lain.Desktop", str(home), "two words"]
    assert stat.S_IMODE(home.stat().st_mode) == 0o700
    desktop = prefix / 'share/applications/lain.desktop'
    icon = prefix / 'share/icons/lain.png'
    entry = desktop.read_text()
    assert 'Exec="' + launcher + '" ui\n' in entry
    assert 'Icon=' + str(icon) + '\n' in entry
    assert icon.read_bytes() == (brand / 'lain-cat.png').read_bytes()
    icon.write_bytes(b'user customized icon')
    result = run(command)
    assert result.returncode != 0 and icon.read_bytes() == b'user customized icon'
    assert not (home / 'app.previous').exists()
    icon.write_bytes((brand / 'lain-cat.png').read_bytes())
    if shutil.which('desktop-file-validate'):
        assert run(['desktop-file-validate', str(desktop)]).returncode == 0
    unit = (units / "lain.service").read_text()
    assert "18418" in unit and "UMask=0077" in unit
    assert "WorkingDirectory=" + str(home).replace("%", "%%") + "\n" in unit
    if os.environ.get("LAIN_VERIFY_SYSTEMD") == "1":
        # Verify ordinary and space-containing service paths with the real parser.
        for label in ("default-data", "data with spaces"):
            verify_home = temporary / label
            verify_prefix = temporary / (label + "-prefix")
            verify_units = temporary / (label + "-units")
            env["LAIN_HOME"] = str(verify_home)
            env["LAIN_SYSTEMD_DIR"] = str(verify_units)
            result = run(command[:-1] + [str(verify_prefix)])
            assert result.returncode == 0, result.stderr
            result = run(["systemd-analyze", "--user", "verify", str(verify_units / "lain.service")])
            assert result.returncode == 0, result.stderr
        env["LAIN_SYSTEMD_DIR"] = str(units)
    env["LAIN_HOME"] = str(home)
    # Prefixes use desktop-entry Exec quoting rather than shell quoting. Verify
    # literal metacharacters stay in one absolute launcher argument after both
    # desktop string decoding and Exec argument parsing.
    for label in ('prefix with spaces', 'prefix"quoted', 'prefix$money`tick', 'prefix%field', 'prefix\\slash', 'prefix@ICON@@EXEC@'):
        alternate_prefix = temporary / label
        env['LAIN_SYSTEMD_DIR'] = str(temporary / ('units-' + label))
        # LAIN_HOME deliberately has a restricted systemd path grammar.
        env['LAIN_HOME'] = str(temporary / ('safe-home-' + str(len(label))))
        # Make each iteration independent, including equal-length labels.
        env['LAIN_HOME'] += '-' + hashlib.sha256(label.encode()).hexdigest()[:8]
        alternate = command[:-1] + [str(alternate_prefix)]
        result = run(alternate)
        assert result.returncode == 0, result.stderr
        alternate_entry = (alternate_prefix / 'share/applications/lain.desktop').read_text()
        encoded = next(line[5:] for line in alternate_entry.splitlines() if line.startswith('Exec='))
        decoded = encoded.replace('\\\\', '\\').replace('\\t', '\t')
        argument = decoded[:-3]
        assert argument[0] == argument[-1] == '"' and decoded.endswith(' ui')
        argument = argument[1:-1]
        for character in ('"', '`', '$', '\\'):
            argument = argument.replace('\\' + character, character)
        assert argument.replace('%%', '%') == str(alternate_prefix / 'bin/lain')
        if shutil.which('desktop-file-validate'):
            result = run(['desktop-file-validate', str(alternate_prefix / 'share/applications/lain.desktop')])
            assert result.returncode == 0, result.stderr
    env['LAIN_HOME'] = str(home)
    env['LAIN_SYSTEMD_DIR'] = str(units)
    sentinel = home / "retain-user-data"
    sentinel.write_text("credentials, preferences and history stay outside app")
    result = run(command)
    assert result.returncode == 0, result.stderr
    assert (home / "app.previous/.lain-package").is_file()
    assert sentinel.read_text() == "credentials, preferences and history stay outside app"
    assert run(command).returncode != 0
    (package / "cli/Lain.Cli").write_text("tampered")
    untouched = temporary / "untouched"
    env["LAIN_HOME"] = str(untouched)
    assert run(command).returncode != 0 and not untouched.exists()
print("PASS isolated installation, arbitrary cwd/arguments, permissions, update backup, checksum rejection")
