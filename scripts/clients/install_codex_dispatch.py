#!/usr/bin/python3
"""Explicit, reversible install of the opt-in Codex dispatcher. Dry run by default."""
import argparse
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import tempfile
import uuid

SUPPORT = ("maintenance.py", "codex_dispatch.py", "qualified-releases.json")
MANAGED = ("dispatch.py", "manifest.json", "receipt.json", *SUPPORT)
SUPPORTED_GRAMMAR = "codex-0.164.0"


class NonExecutableLauncher(ValueError):
    pass


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


def safe_path(path):
    path = Path(path).absolute()
    if ".." in path.parts or any(item.is_symlink() for item in (path, *path.parents)):
        raise ValueError("maintenance paths must not contain symlinks or traversal")
    return path


def atomic_write(path, data, mode=0o600):
    descriptor, name = tempfile.mkstemp(prefix=".maintenance-", dir=path.parent)
    pending = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        pending.chmod(mode)
        os.replace(pending, path)
        sync_directory(path.parent)
    finally:
        pending.unlink(missing_ok=True)


def sync_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def json_bytes(value):
    return (json.dumps(value, indent=2) + "\n").encode()


def qualifications():
    sibling = Path(__file__).with_name("qualified-releases.json")
    source = sibling if sibling.is_file() else Path(__file__).resolve().parents[2] / "clients/codex/qualified-releases.json"
    raw = source.read_bytes()
    return json.loads(raw), raw


def qualified_pair(original_sha, patched_sha, version=None):
    catalog, _ = qualifications()
    if catalog.get("schemaVersion") != 1:
        raise ValueError("unsupported qualification registry schema")
    original = catalog.get("originals", {}).get(original_sha)
    patched = catalog.get("patched", {}).get(patched_sha)
    if not original or not patched or original["grammarVersion"] != patched["grammarVersion"] or original["grammarVersion"] != SUPPORTED_GRAMMAR or original["grammarVersion"] not in catalog["supportedGrammars"]:
        raise ValueError("unqualified binary or CLI grammar; no automatic upstream upgrade is supported")
    if version is not None and version != original["version"]:
        raise ValueError("requested original version does not match its qualified SHA256")
    return original, patched


def support_files():
    _, catalog = qualifications()
    return {"maintenance.py": Path(__file__).read_bytes(),
            "codex_dispatch.py": Path(__file__).with_name("codex_dispatch.py").read_bytes(),
            "qualified-releases.json": catalog}


def render_dispatch(original, patched, original_sha, patched_sha, template=None):
    template = Path(__file__).with_name("codex_dispatch.py").read_text() if template is None else template
    if 'GRAMMAR_VERSION = "' + SUPPORTED_GRAMMAR + '"' not in template:
        raise ValueError("dispatcher template grammar is not supported")
    for key, value in {"LAIN_ORIGINAL": str(original), "LAIN_PATCHED": str(patched),
                       "LAIN_ORIGINAL_SHA256": original_sha, "LAIN_PATCHED_SHA256": patched_sha}.items():
        token = '"@' + key + '@"'
        if token not in template:
            raise ValueError("dispatcher source template is not intact")
        template = template.replace(token, json.dumps(value))
    return template.encode()


def upstream_path(entry, target):
    candidate = Path(target) if Path(target).is_absolute() else entry.parent / target
    try:
        return str(candidate.resolve(strict=True))
    except (OSError, RuntimeError):
        return None


def receipt(manifest, state):
    return {"schemaVersion": 1, "entry": manifest["entry"], "state": str(state),
            "generation": manifest["generation"], "manifestSha256": sha256(state / "manifest.json"),
            "dispatcherSha256": manifest["dispatcherSha256"], "originalSha256": manifest["originalSha256"],
            "patchedSha256": manifest["patchedSha256"], "grammarVersion": manifest["grammarVersion"],
            "originalVersion": manifest["originalVersion"], "patchedVersion": manifest["patchedVersion"],
            "maintenance": str(state / "maintenance.py"), "maintenanceSha256": manifest["supportSha256"]["maintenance.py"]}


def enrich_manifest(manifest, state, original_record, patch_record, support):
    result = dict(manifest)
    result.update(schema=3, generation=uuid.uuid4().hex, originalVersion=original_record["version"],
                  patchedVersion=patch_record["version"], grammarVersion=original_record["grammarVersion"],
                  upstreamResolvedAtBinding=upstream_path(Path(manifest["entry"]), manifest["previousSymlink"]),
                  supportSha256={name: hashlib.sha256(data).hexdigest() for name, data in support.items()})
    return result


def owned_launcher(state, manifest):
    if manifest.get("schema") not in (2, 3) or manifest.get("dispatcher") != str(state / "dispatch.py"):
        raise ValueError("unrecognized launcher manifest")
    entry = Path(manifest["entry"])
    if not entry.is_absolute() or link_snapshot(entry) != manifest["installedEntry"] or current_link(entry) != str(state / "dispatch.py"):
        raise ValueError("Codex entry changed; refusing to replace a foreign launcher")
    wrapper = safe_path(state / "dispatch.py")
    if not stat.S_ISREG(wrapper.lstat().st_mode) or not os.access(wrapper, os.X_OK):
        raise NonExecutableLauncher("dispatcher must be a regular executable file; inspect its permissions before maintenance")
    if file_identity(wrapper) != manifest["dispatcherIdentity"] or sha256(wrapper) != manifest["dispatcherSha256"]:
        raise ValueError("dispatcher identity or bytes changed; explicit inspection required")


def check_status(state):
    state = state.absolute()
    result = {"schemaVersion": 1, "code": "not-installed", "readyForGroups": False, "issues": [], "receipt": None}
    if not (state / "manifest.json").exists():
        result["issues"].append({"code": "not-installed", "message": "lain dispatcher is not installed; install it explicitly."})
        return result
    def issue(code, message):
        result["issues"].append({"code": code, "message": message})
    try:
        safe_path(state)
        manifest_data = safe_path(state / "manifest.json").read_bytes()
        initial_manifest_sha = hashlib.sha256(manifest_data).hexdigest()
        result["manifestSha256"] = initial_manifest_sha
        manifest = json.loads(manifest_data)
        owned_launcher(state, manifest)
        result["receipt"] = {"entry": manifest["entry"], "state": str(state), "manifestSha256": initial_manifest_sha}
        for name, path, expected, identity in (
                ("original", Path(manifest["original"]), manifest["originalSha256"], manifest["originalIdentity"]),
                ("patched", state / "codex-lain", manifest["patchedSha256"], None)):
            try:
                resolved = checked_binary(path, expected)
                if identity is not None and file_identity(resolved) != identity:
                    issue(name + "-identity-changed", "Chosen original file identity changed; inspect and explicitly rebind.")
            except (OSError, ValueError):
                issue(name + "-missing-or-changed", "Chosen " + name + " executable is missing, not executable, or differs from its pinned bytes.")
        try:
            original, patched = qualified_pair(manifest["originalSha256"], manifest["patchedSha256"])
        except ValueError:
            issue("unqualified-release-or-grammar", "These binary pins or this CLI grammar are not qualified; no unknown upstream upgrade is supported.")
            result["code"] = "maintenance-required"
            return result
        if manifest["schema"] == 2:
            issue("receipt-upgrade-required", "Legacy dispatcher has no versioned receipt; explicitly rebind the qualified original to upgrade it.")
            expected_upstream = manifest["original"]
        else:
            expected_upstream = manifest["upstreamResolvedAtBinding"]
            if manifest["grammarVersion"] != original["grammarVersion"] or manifest["originalVersion"] != original["version"] or manifest["patchedVersion"] != patched["version"]:
                issue("unsupported-grammar", "Manifest versions do not match the qualified binary grammar.")
            expected_receipt = receipt(manifest, state)
            result["receipt"] = expected_receipt
            if json.loads(safe_path(state / "receipt.json").read_text()) != expected_receipt:
                issue("receipt-changed", "Receipt no longer matches the current manifest; inspect the maintenance transaction.")
            for name in SUPPORT:
                if sha256(safe_path(state / name)) != manifest["supportSha256"][name]:
                    issue("maintenance-files-changed", "Installed maintenance files differ from their receipt pins.")
            bound = render_dispatch(manifest["original"], state / "codex-lain", manifest["originalSha256"], manifest["patchedSha256"],
                                    safe_path(state / "codex_dispatch.py").read_text())
            if hashlib.sha256(bound).hexdigest() != manifest["dispatcherSha256"]:
                issue("dispatcher-binding-mismatch", "Wrapper does not match its recorded original, patch and supported template.")
        observed = upstream_path(Path(manifest["entry"]), manifest["previousSymlink"])
        result["upstream"] = {"boundResolvedPath": expected_upstream, "currentResolvedPath": observed, "changed": observed != expected_upstream}
        if observed != expected_upstream:
            issue("upstream-release-changed", "Original upstream release path changed; selected fallback remains pinned until a conscious qualified rebind.")
        if sha256(state / "manifest.json") != initial_manifest_sha:
            issue("state-changed-during-check", "Maintenance state changed while checking it; read status again before launching.")
        owned_launcher(state, manifest)
        result["readyForGroups"] = not result["issues"]
        result["code"] = "ready" if result["readyForGroups"] else "maintenance-required"
    except NonExecutableLauncher:
        issue("dispatcher-not-executable", "Dispatcher is not a regular executable file; inspect its permissions before launching or rebinding.")
        result["code"] = "maintenance-required"
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as error:
        issue("unrecognized-or-incomplete", "Dispatcher state is unrecognized, changed or incomplete; inspect status and maintenance backups before changing it.")
        result["code"] = "unrecognized"
    return result


@contextmanager
def maintenance_lock(state):
    parent = safe_path(state.parent)
    parent.mkdir(parents=True, exist_ok=True)
    details = parent.stat()
    if details.st_uid != os.getuid() or details.st_mode & 0o022:
        raise ValueError("maintenance lock parent must be owned and protected from other writers")
    path = parent / ".codex-profile-lain.lock"
    flags = os.O_RDWR | os.O_NOFOLLOW | os.O_CLOEXEC
    try:
        descriptor = os.open(path, flags | os.O_CREAT | os.O_EXCL, 0o600)
    except FileExistsError:
        descriptor = os.open(path, flags)
    try:
        details = os.fstat(descriptor)
        if not stat.S_ISREG(details.st_mode) or details.st_uid != os.getuid() or details.st_nlink != 1 or stat.S_IMODE(details.st_mode) != 0o600 or file_identity(path) != {"device": details.st_dev, "inode": details.st_ino}:
            raise ValueError("maintenance lock must be an owned private regular single-link file")
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if file_identity(path) != {"device": details.st_dev, "inode": details.st_ino}:
            raise ValueError("maintenance lock path changed while locking; aborting")
        yield
    finally:
        os.close(descriptor)


def install(args):
    entry = args.entry.absolute()
    safe_path(entry.parent)
    state = safe_path(args.state)
    if state.exists() or state.is_symlink():
        raise ValueError(f"state already exists; uninstall or inspect it first: {state}")
    old_entry = link_snapshot(entry)
    old_target = old_entry["target"]
    original = checked_binary(entry, args.original_sha256)
    original_identity = file_identity(original)
    patched = checked_binary(args.patched, args.patched_sha256)
    if original == patched or state in original.parents or state in patched.parents:
        raise ValueError("original and patched binary must be separate, outside the install state")
    for candidate in (original, patched):
        with candidate.open("rb") as stream:
            prefix = stream.read(16384)
        if b"@LAIN_ORIGINAL@" in prefix or b"def uses_lain(args):" in prefix:
            raise ValueError("refusing recursive dispatcher as a Codex binary")
    wrapper = state / "dispatch.py"
    installed_patch = state / "codex-lain"
    original_record, patch_record = qualified_pair(args.original_sha256.lower(), args.patched_sha256.lower())
    support = support_files()
    source = render_dispatch(original, installed_patch, args.original_sha256.lower(), args.patched_sha256.lower())
    manifest = {"schema": 2, "entry": str(entry), "previousSymlink": old_target,
                "previousEntry": old_entry, "originalIdentity": original_identity,
                "original": str(original), "originalSha256": args.original_sha256.lower(),
                "patchedSha256": args.patched_sha256.lower(), "dispatcher": str(wrapper),
                "dispatcherSha256": hashlib.sha256(source).hexdigest(), "uninstallSymlink": old_target}
    manifest = enrich_manifest(manifest, state, original_record, patch_record, support)
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
        (stage / "dispatch.py").write_bytes(source)
        (stage / "dispatch.py").chmod(0o700)
        manifest["dispatcherIdentity"] = file_identity(stage / "dispatch.py")
        for name, data in support.items():
            (stage / name).write_bytes(data)
            (stage / name).chmod(0o700 if name.endswith(".py") else 0o600)
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
        atomic_write(state / "receipt.json", json_bytes(receipt(manifest, state)))
        if link_snapshot(entry) != manifest["installedEntry"]:
            raise ValueError("Codex symlink changed after installation; inspect retained state")


def uninstall(args):
    state = safe_path(args.state)
    manifest = json.loads(safe_path(state / "manifest.json").read_text())
    entry = Path(manifest["entry"])
    wrapper = state / "dispatch.py"
    if manifest["schema"] not in (2, 3) or manifest["dispatcher"] != str(wrapper):
        raise ValueError("invalid manifest; no changes made")
    expected_entry = manifest["installedEntry"]
    if link_snapshot(entry) != expected_entry or current_link(entry) != str(wrapper) or file_identity(wrapper) != manifest["dispatcherIdentity"] or sha256(wrapper) != manifest["dispatcherSha256"]:
        raise ValueError("Codex entry or launcher changed since installation; refusing to overwrite it")
    target = manifest.get("uninstallSymlink", manifest["previousSymlink"])
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


def snapshot_files(state):
    result = {}
    for name in MANAGED:
        path = state / name
        if path.exists() or path.is_symlink():
            safe_path(path)
            if not path.is_file():
                raise ValueError("managed maintenance item is not a regular file")
            result[name] = {"sha256": sha256(path), "mode": stat.S_IMODE(path.stat().st_mode)}
    return result


def verify_snapshot(state, expected):
    if snapshot_files(state) != expected:
        raise ValueError("maintenance state changed while preparing; aborting")


def rebind(args):
    state = safe_path(args.state)
    if sha256(safe_path(state / "manifest.json")) != args.expect_manifest_sha256:
        raise ValueError("manifest changed since status; inspect it again before rebind")
    manifest = json.loads((state / "manifest.json").read_text())
    owned_launcher(state, manifest)
    original = checked_binary(args.original, args.original_sha256)
    if original == state / "codex-lain" or state in original.parents:
        raise ValueError("fallback must be an external original release, never this launcher or patched client")
    original_record, patch_record = qualified_pair(args.original_sha256, manifest["patchedSha256"], args.original_version)
    checked_binary(state / "codex-lain", manifest["patchedSha256"])
    support = support_files()
    source = render_dispatch(original, state / "codex-lain", args.original_sha256, manifest["patchedSha256"])
    updated = enrich_manifest(manifest, state, original_record, patch_record, support)
    updated.update(original=str(original), originalSha256=args.original_sha256, originalIdentity=file_identity(original),
                   dispatcherSha256=hashlib.sha256(source).hexdigest(), uninstallSymlink=str(original))
    transaction_id = uuid.uuid4().hex
    print(json.dumps({"operation": "rebind", "apply": args.apply, "transaction": transaction_id,
                      "previousOriginal": manifest["original"], "selectedOriginal": str(original),
                      "originalVersion": original_record["version"], "grammarVersion": original_record["grammarVersion"],
                      "originalSha256": args.original_sha256, "expectManifestSha256": args.expect_manifest_sha256}, indent=2))
    if not args.apply:
        return
    before = snapshot_files(state)
    history = safe_path(state / "maintenance-history")
    history.mkdir(mode=0o700, exist_ok=True)
    transaction = history / transaction_id
    transaction.mkdir(mode=0o700)
    previous = transaction / "previous"
    previous.mkdir(mode=0o700)
    for name in before:
        shutil.copy2(state / name, previous / name)
        if sha256(previous / name) != before[name]["sha256"]:
            raise ValueError("maintenance backup differs; no changes made")
        with (previous / name).open("rb") as stream:
            os.fsync(stream.fileno())
    journal = {"schemaVersion": 1, "phase": "prepared", "before": before,
               "entry": manifest["installedEntry"], "beforeManifestSha256": args.expect_manifest_sha256,
               "plannedDispatcherSha256": updated["dispatcherSha256"],
               "plannedSupportSha256": updated["supportSha256"]}
    atomic_write(transaction / "transaction.json", json_bytes(journal))
    for directory in (previous, transaction, history, state):
        sync_directory(directory)
    verify_snapshot(state, before)
    owned_launcher(state, manifest)
    checked_binary(original, args.original_sha256)
    try:
        atomic_write(state / "dispatch.py", source, 0o700)
        updated["dispatcherIdentity"] = file_identity(state / "dispatch.py")
        journal["plannedManifestSha256"] = hashlib.sha256(json_bytes(updated)).hexdigest()
        atomic_write(transaction / "transaction.json", json_bytes(journal))
        for name, data in support.items():
            atomic_write(state / name, data, 0o700 if name.endswith(".py") else 0o600)
        atomic_write(state / "manifest.json", json_bytes(updated))
        receipt_data = json_bytes(receipt(updated, state))
        journal["plannedReceiptSha256"] = hashlib.sha256(receipt_data).hexdigest()
        atomic_write(transaction / "transaction.json", json_bytes(journal))
        atomic_write(state / "receipt.json", receipt_data)
        owned_launcher(state, updated)
        checked_binary(original, args.original_sha256)
        journal.update(phase="committed", afterManifestSha256=sha256(state / "manifest.json"), after=snapshot_files(state))
        atomic_write(transaction / "transaction.json", json_bytes(journal))
    except BaseException:
        journal["phase"] = "interrupted-inspect-before-recovery"
        atomic_write(transaction / "transaction.json", json_bytes(journal))
        raise


def rollback_rebind(args):
    state = safe_path(args.state)
    if len(args.transaction) != 32 or any(character not in "0123456789abcdef" for character in args.transaction):
        raise ValueError("invalid maintenance transaction ID")
    transaction = safe_path(state / "maintenance-history" / args.transaction)
    journal = json.loads((transaction / "transaction.json").read_text())
    if journal["phase"] == "rolled-back":
        raise ValueError("this maintenance transaction was already rolled back")
    previous = transaction / "previous"
    verify_snapshot(previous, journal["before"])
    current_sha = sha256(state / "manifest.json")
    if current_sha != args.expect_manifest_sha256 or current_sha not in (journal["beforeManifestSha256"], journal.get("plannedManifestSha256"), journal.get("afterManifestSha256")):
        raise ValueError("current manifest is not this transaction's known state; refusing rollback")
    old = json.loads((previous / "manifest.json").read_text())
    if link_snapshot(Path(old["entry"])) != journal["entry"]:
        raise ValueError("Codex entry changed; refusing rollback over a foreign update")
    actual = snapshot_files(state)
    if journal.get("after") is not None:
        if actual != journal["after"]:
            raise ValueError("maintenance files changed after rebind; refusing rollback")
    else:
        allowed = dict(journal["plannedSupportSha256"], **{"dispatch.py": journal["plannedDispatcherSha256"]})
        for name, details in actual.items():
            if name == "manifest.json":
                continue
            if name == "receipt.json":
                if details["sha256"] not in (journal["before"].get(name, {}).get("sha256"), journal.get("plannedReceiptSha256")):
                    raise ValueError("unknown interrupted receipt; refusing rollback")
                continue
            if details["sha256"] not in (journal["before"].get(name, {}).get("sha256"), allowed.get(name)):
                raise ValueError("unknown interrupted maintenance bytes; inspect backup manually")
    original = checked_binary(Path(old["original"]), old["originalSha256"])
    qualified_pair(old["originalSha256"], old["patchedSha256"])
    checked_binary(state / "codex-lain", old["patchedSha256"])
    if file_identity(original) != old["originalIdentity"]:
        raise ValueError("prior original identity changed; rebind a qualified existing original instead")
    print(json.dumps({"operation": "rollback-rebind", "apply": args.apply, "transaction": args.transaction,
                      "restoreOriginal": old["original"], "expectManifestSha256": args.expect_manifest_sha256}, indent=2))
    if not args.apply:
        return
    verify_snapshot(state, actual)
    for name, details in journal["before"].items():
        if name not in ("manifest.json", "receipt.json"):
            atomic_write(state / name, (previous / name).read_bytes(), details["mode"])
    for name in MANAGED:
        if name not in journal["before"] and (state / name).exists():
            (state / name).unlink()
    old["dispatcherIdentity"] = file_identity(state / "dispatch.py")
    if old["schema"] == 3:
        old["generation"] = uuid.uuid4().hex
    atomic_write(state / "manifest.json", json_bytes(old))
    if old["schema"] == 3:
        atomic_write(state / "receipt.json", json_bytes(receipt(old, state)))
    elif "receipt.json" in journal["before"]:
        atomic_write(state / "receipt.json", (previous / "receipt.json").read_bytes())
    owned_launcher(state, old)
    journal.update(phase="rolled-back", rollbackManifestSha256=sha256(state / "manifest.json"))
    atomic_write(transaction / "transaction.json", json_bytes(journal))


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
    status_parser = sub.add_parser("status", help="read-only versions, receipt and upstream drift check")
    status_parser.add_argument("--json", action="store_true")
    binding = sub.add_parser("rebind", help="explicitly select a qualified original; never download or build")
    binding.add_argument("--original", type=Path, required=True)
    binding.add_argument("--original-sha256", required=True)
    binding.add_argument("--original-version", required=True)
    binding.add_argument("--expect-manifest-sha256", required=True)
    rolling = sub.add_parser("rollback-rebind", help="restore a verified maintenance snapshot, retaining audit files")
    rolling.add_argument("--transaction", required=True)
    rolling.add_argument("--expect-manifest-sha256", required=True)
    args = parser.parse_args()
    try:
        if args.operation == "status":
            result = check_status(args.state)
            if args.json:
                print(json.dumps(result, indent=2))
            else:
                print("lain Codex: " + result["code"])
                for item in result["issues"]:
                    print(item["code"] + ": " + item["message"])
                if result["receipt"]:
                    print("Manifest SHA256: " + result["receipt"]["manifestSha256"])
            return
        operation = {"install": install, "uninstall": uninstall, "rebind": rebind, "rollback-rebind": rollback_rebind}[args.operation]
        if not args.apply:
            operation(args)
            return
        # Own operations serialize. Other installers do not share this lock:
        # concurrent foreign updates are unsupported, and checks are not CAS.
        with maintenance_lock(args.state.absolute()):
            operation(args)
    except (OSError, ValueError, KeyError, TypeError, RuntimeError) as error:
        parser.exit(2, f"lain Codex: {error}\n")


if __name__ == "__main__":
    main()
