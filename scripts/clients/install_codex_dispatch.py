#!/usr/bin/python3
"""Explicit, reversible install of the opt-in Codex dispatcher. Dry run by default."""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import tempfile
import uuid


def sha256(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def checked_binary(path, expected):
    resolved = path.resolve(strict=True)
    if not resolved.is_file() or not os.access(resolved, os.X_OK):
        raise ValueError(f"not an executable file: {path}")
    if len(expected) != 64 or sha256(resolved) != expected.lower():
        raise ValueError(f"SHA256 does not match explicit pin: {path}")
    return resolved


def file_identity(path):
    details = path.lstat()
    return {"device": details.st_dev, "inode": details.st_ino}


def link_snapshot(path):
    before = path.lstat()
    if not stat.S_ISLNK(before.st_mode):
        raise ValueError(f"refusing to replace a non-symlink: {path}")
    target = os.readlink(path)
    identity = file_identity(path)
    if identity != {"device": before.st_dev, "inode": before.st_ino}:
        raise ValueError("Codex symlink changed while inspecting it; aborting")
    return {"target": target, "identity": identity}


def current_link(path):
    return link_snapshot(path)["target"]


def replace_link(path, target, expected):
    """Replace only in a quiet setup; checks detect changes but provide no CAS."""
    if link_snapshot(path) != expected:
        raise ValueError(f"Codex symlink changed; leaving it untouched: {path}")
    temporary = path.parent / (".lain-codex-link-" + uuid.uuid4().hex)
    try:
        temporary.symlink_to(target)
        replacement = link_snapshot(temporary)
        # Recheck after preparing the replacement, as updaters do not use our lock.
        if link_snapshot(path) != expected:
            raise ValueError("Codex symlink changed during operation; leaving it untouched")
        os.replace(temporary, path)
        if link_snapshot(path) != replacement:
            # No rollback: evidence of a foreign updater makes another mutation
            # unsafe. Keep the private state for explicit recovery/inspection.
            raise ValueError("Codex symlink changed after replacement; stop other updaters and inspect state")
        return replacement
    finally:
        temporary.unlink(missing_ok=True)


def install(args):
    entry = args.entry.absolute()
    state = args.state.absolute()
    if state.exists() or state.is_symlink():
        raise ValueError(f"state already exists; uninstall or inspect it first: {state}")
    old_entry = link_snapshot(entry)
    old_target = old_entry["target"]
    original = checked_binary(entry, args.original_sha256)
    original_identity = file_identity(original)
    patched = checked_binary(args.patched, args.patched_sha256)
    if original == patched or state in original.parents or state in patched.parents:
        raise ValueError("original and patched binary must be separate, outside the install state")
    template = Path(__file__).with_name("codex_dispatch.py").read_text()
    if "@LAIN_ORIGINAL@" not in template or "@LAIN_PATCHED@" not in template:
        raise ValueError("dispatcher source template is not intact")
    for candidate in (original, patched):
        with candidate.open("rb") as stream:
            prefix = stream.read(16384)
        if b"@LAIN_ORIGINAL@" in prefix or b"def uses_lain(args):" in prefix:
            raise ValueError("refusing recursive dispatcher as a Codex binary")
    wrapper = state / "dispatch.py"
    installed_patch = state / "codex-lain"
    source = template.replace('"@LAIN_ORIGINAL@"', json.dumps(str(original))).replace('"@LAIN_PATCHED@"', json.dumps(str(installed_patch)))
    manifest = {"schema": 2, "entry": str(entry), "previousSymlink": old_target,
                "previousEntry": old_entry, "originalIdentity": original_identity,
                "original": str(original), "originalSha256": args.original_sha256.lower(),
                "patchedSha256": args.patched_sha256.lower(), "dispatcher": str(wrapper),
                "dispatcherSha256": hashlib.sha256(source.encode()).hexdigest()}
    print(json.dumps({"operation": "install", "apply": args.apply, **manifest}, indent=2))
    if not args.apply:
        return
    state.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".codex-stage-", dir=state.parent) as scratch:
        stage = Path(scratch) / "client"
        stage.mkdir(mode=0o700)
        shutil.copyfile(patched, stage / "codex-lain")
        (stage / "codex-lain").chmod(0o700)
        checked_binary(stage / "codex-lain", args.patched_sha256)
        (stage / "dispatch.py").write_text(source)
        (stage / "dispatch.py").chmod(0o700)
        manifest["dispatcherIdentity"] = file_identity(stage / "dispatch.py")
        (stage / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
        # The exact previous target is a separate reviewable rollback artifact.
        (stage / "previous-symlink.json").write_text(json.dumps({"target": old_target}) + "\n")
        checked_binary(original, args.original_sha256)
        if link_snapshot(entry) != old_entry or entry.resolve(strict=True) != original or file_identity(original) != original_identity:
            raise ValueError("Original Codex changed while staging; aborting")
        os.rename(stage, state)
        manifest["installedEntry"] = replace_link(entry, str(wrapper), old_entry)
        checked_binary(original, args.original_sha256)
        if file_identity(original) != original_identity or file_identity(wrapper) != manifest["dispatcherIdentity"] or sha256(wrapper) != manifest["dispatcherSha256"]:
            raise ValueError("Original Codex or dispatcher changed after installation; inspect retained state")
        (state / "manifest.json.next").write_text(json.dumps(manifest, indent=2) + "\n")
        os.replace(state / "manifest.json.next", state / "manifest.json")
        if link_snapshot(entry) != manifest["installedEntry"]:
            raise ValueError("Codex symlink changed after installation; inspect retained state")


def uninstall(args):
    state = args.state.absolute()
    manifest = json.loads((state / "manifest.json").read_text())
    entry = Path(manifest["entry"])
    wrapper = state / "dispatch.py"
    if manifest["schema"] != 2 or manifest["dispatcher"] != str(wrapper):
        raise ValueError("invalid manifest; no changes made")
    expected_entry = manifest["installedEntry"]
    if link_snapshot(entry) != expected_entry or current_link(entry) != str(wrapper) or file_identity(wrapper) != manifest["dispatcherIdentity"] or sha256(wrapper) != manifest["dispatcherSha256"]:
        raise ValueError("Codex entry or launcher changed since installation; refusing to overwrite it")
    target = manifest["previousSymlink"]
    resolved = (entry.parent / target).resolve() if not Path(target).is_absolute() else Path(target).resolve()
    if resolved == wrapper.resolve() or state in resolved.parents:
        raise ValueError("backup would recurse into the dispatcher")
    original = checked_binary(Path(manifest["original"]), manifest["originalSha256"])
    if resolved != original or file_identity(original) != manifest["originalIdentity"]:
        raise ValueError("Original fallback identity or saved symlink resolution changed; aborting")
    print(json.dumps({"operation": "uninstall", "apply": args.apply, "entry": str(entry), "restoreSymlink": target, "keepState": str(state)}, indent=2))
    if args.apply:
        restored_entry = replace_link(entry, target, expected_entry)
        checked_binary(original, manifest["originalSha256"])
        if link_snapshot(entry) != restored_entry or entry.resolve(strict=True) != original or file_identity(original) != manifest["originalIdentity"]:
            raise ValueError("Original fallback or symlink changed after rollback; inspect retained state")
        # Keep the manifest, backup and binary for audit/recovery; never delete a running executable.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state", type=Path, default=Path.home() / ".local/share/lain/clients/codex-profile-lain")
    parser.add_argument("--apply", action="store_true", help="mutate only after reviewing the default dry run")
    sub = parser.add_subparsers(dest="operation", required=True)
    add = sub.add_parser("install")
    add.add_argument("--entry", type=Path, default=Path.home() / ".local/bin/codex")
    add.add_argument("--patched", type=Path, required=True)
    add.add_argument("--original-sha256", required=True)
    add.add_argument("--patched-sha256", required=True)
    sub.add_parser("uninstall")
    args = parser.parse_args()
    try:
        if not args.apply:
            (install if args.operation == "install" else uninstall)(args)
            return
        # Own operations serialize. Other installers do not share this lock:
        # concurrent foreign updates are unsupported, and checks are not CAS.
        args.state.absolute().parent.mkdir(parents=True, exist_ok=True)
        lock_path = args.state.absolute().parent / ".codex-profile-lain.lock"
        with lock_path.open("a") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            (install if args.operation == "install" else uninstall)(args)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(2, f"lain Codex: {error}\n")


if __name__ == "__main__":
    main()
