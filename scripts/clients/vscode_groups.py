#!/usr/bin/env python3
"""Prepare and maintain a pinned local Codex VSIX; never reload a window."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import ctypes
import fcntl
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import signal
import stat
import subprocess
import sys
import tempfile
import uuid
import xml.etree.ElementTree as ET
import zipfile

EXTENSION_ID = "openai.chatgpt"
ORIGINAL_VERSION = "26.1021.22027"
LOCAL_VERSION = "26.1021.22028"
PLATFORM = "linux-x64"
RUNTIME = "bin/linux-x86_64/codex"
PINS = {
    "package.json": "ea31244803e0f7b70071b1743bdf50fcf197534db032cc0fcbc48347a44de4ed",
    "out/extension.js": "a9cc6736cd2c2d22f6cf43fc5763d5f8a873e4f741f522b44a600b1926f4e627",
    "webview/assets/model-picker-options-2e5c9d5f09f6.js": "4ed468ee7276465093996f01fe20654a8b0c380ba58843d1052eacc97c7cd47d",
    "webview/assets/access-row-56c64d268a49.js": "ed7d897994129ac9aa1c9c5af316cf12a48c6eab8272bf5395d6ac58610117fb",
    RUNTIME: "e6d386e8822288f6d5f327e966a5a5e1de928de8795bb6149716005a410f38f2",
}
NS = "http://schemas.microsoft.com/developer/vsx-schema/2011"
MAX_JSON = 16 * 1024 * 1024
NATIVE_CHECKS = (
    "originalActiveBeforeInstall", "originalTreePreserved", "singleSelectedRecord",
    "noAutomaticReload", "candidateActiveAfterManualReload",
    "rollbackOriginalAfterManualReload", "userStatePreserved",
)


class MaintenanceError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise MaintenanceError(message)


def encoded(value):
    return (json.dumps(value, sort_keys=True, indent=2, ensure_ascii=False) + "\n").encode()


def digest(value):
    return hashlib.sha256(encoded(value)).hexdigest()


def absolute(path):
    path = Path(path).absolute()
    require(".." not in path.parts, f"parent traversal: {path}")
    return path


def safe_dir(path):
    """Reject links/untrusted writable ancestors, including the directory itself."""
    path = absolute(path)
    root_uid = os.stat("/").st_uid
    for item in reversed((path, *path.parents)):
        st = item.lstat()
        require(stat.S_ISDIR(st.st_mode), f"not a real directory: {item}")
        require(st.st_uid in (root_uid, os.getuid()), f"foreign directory owner: {item}")
        sticky_root = st.st_uid == root_uid and st.st_mode & stat.S_ISVTX
        require(not st.st_mode & 0o022 or sticky_root, f"writable directory: {item}")
    return path


def identity(st):
    return (st.st_dev, st.st_ino, st.st_mode, st.st_size, st.st_mtime_ns, st.st_ctime_ns)


@contextmanager
def read_file(path):
    path = absolute(path)
    safe_dir(path.parent)
    before = path.lstat()
    require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1,
            f"not a single-link regular file: {path}")
    require(before.st_uid == os.getuid() and not before.st_mode & 0o022,
            f"unsafe file owner/permissions: {path}")
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as handle:
        require(identity(before) == identity(os.fstat(handle.fileno())), f"file changed: {path}")
        yield handle
        require(identity(before) == identity(os.fstat(handle.fileno())) == identity(path.lstat()),
                f"file changed during read: {path}")


def file_hash(path):
    with read_file(path) as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def read_json(path):
    with read_file(path) as handle:
        data = handle.read(MAX_JSON + 1)
    require(len(data) <= MAX_JSON, f"JSON too large: {path}")
    return json.loads(data)


def pinned_json(path):
    with read_file(path) as handle:
        data = handle.read(MAX_JSON + 1)
    require(len(data) <= MAX_JSON, f"JSON too large: {path}")
    return json.loads(data), hashlib.sha256(data).hexdigest()


def inventory(root):
    root = safe_dir(root)
    entries = {}
    for base, dirs, files in os.walk(root, followlinks=False):
        dirs.sort()
        files.sort()
        for name in dirs + files:
            path = Path(base) / name
            st = path.lstat()
            rel = path.relative_to(root).as_posix()
            if stat.S_ISDIR(st.st_mode):
                safe_dir(path)
                entries[rel] = {"kind": "directory", "mode": stat.S_IMODE(st.st_mode)}
            else:
                entries[rel] = {"kind": "file", "mode": stat.S_IMODE(st.st_mode),
                                "size": st.st_size, "sha256": file_hash(path)}
    return entries


def sync_dir(path):
    fd = os.open(safe_dir(path), os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def publish(source, dest):
    """Linux atomic no-replace publication: never clobber a concurrent creator."""
    safe_dir(Path(dest).parent)
    libc = ctypes.CDLL(None, use_errno=True)
    rename = libc.renameat2
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    if rename(-100, os.fsencode(source), -100, os.fsencode(dest), 1) != 0:
        error = ctypes.get_errno()
        raise OSError(error, os.strerror(error), str(dest))
    sync_dir(Path(dest).parent)


def create_file(path, data, mode=0o600):
    safe_dir(path.parent)
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, mode)
    with os.fdopen(fd, "wb") as handle:
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())


def rewrite_staged(path, data):
    """Only used inside our unpublished, private preparation directory."""
    prior = file_hash(path)
    temp = path.with_name(".lain-" + uuid.uuid4().hex)
    create_file(temp, data, stat.S_IMODE(path.stat().st_mode))
    require(file_hash(path) == prior, f"staged file changed: {path}")
    os.replace(temp, path)
    sync_dir(path.parent)


def copy_tree(source, dest, expected):
    dest.mkdir(mode=0o700)
    for rel, info in sorted(expected.items(), key=lambda item: (item[0].count("/"), item[0])):
        target = dest / rel
        if info["kind"] == "directory":
            target.mkdir(mode=info["mode"])
            target.chmod(info["mode"])
            continue
        with read_file(source / rel) as src:
            fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, info["mode"])
            with os.fdopen(fd, "wb") as out:
                while chunk := src.read(1024 * 1024):
                    out.write(chunk)
                out.flush()
                os.fchmod(out.fileno(), info["mode"])
                os.fsync(out.fileno())
    require(inventory(dest) == expected == inventory(source), "source/copy changed during backup")
    for base, dirs, _ in os.walk(dest, topdown=False):
        sync_dir(Path(base))


def validate_original(source, pins):
    require(isinstance(pins, dict) and pins.get("schemaVersion") == 1 and pins.get("extensionId") == EXTENSION_ID
            and pins.get("version") == ORIGINAL_VERSION and pins.get("targetPlatform") == PLATFORM,
            "unsupported input manifest identity/version/platform")
    package = read_json(source / "package.json")
    require((package.get("publisher"), package.get("name"), package.get("version")) ==
            ("openai", "chatgpt", ORIGINAL_VERSION), "unknown extension build")
    require(package.get("__metadata", {}).get("targetPlatform", PLATFORM) == PLATFORM,
            "wrong installed platform")
    tree = inventory(source)
    actual = {rel: info["sha256"] for rel, info in tree.items() if info["kind"] == "file"}
    supplied = pins.get("files", {})
    require(isinstance(supplied, dict) and supplied == actual,
            "input pins must match the complete original file inventory")
    require(all(supplied.get(rel) == sha for rel, sha in PINS.items()),
            "input is not the qualified original build")
    return tree


def patchable(rel):
    return rel.endswith((".js", ".cjs")) and (rel.startswith("out/") or rel.startswith("webview/assets/"))


def set_local_identity(candidate, original_hash):
    package = read_json(candidate / "package.json")
    require((package.get("publisher"), package.get("name"), package.get("version")) ==
            ("openai", "chatgpt", ORIGINAL_VERSION), "transform changed extension identity")
    package.pop("__metadata", None)  # VS Code owns installation metadata.
    package.update(version=LOCAL_VERSION, displayName="Codex (Lain local groups)")
    package["x-lain-local-groups"] = {"schemaVersion": 1, "originalVersion": ORIGINAL_VERSION,
                                       "originalTreeSha256": original_hash, "origin": "local-lain-patch"}
    rewrite_staged(candidate / "package.json", encoded(package))
    manifest = candidate / ".vsixmanifest"
    with read_file(manifest) as handle:
        xml = ET.parse(handle)
    node = xml.find(f".//{{{NS}}}Identity")
    require(node is not None and node.get("Id") == "chatgpt" and node.get("Publisher") == "openai"
            and node.get("Version") == ORIGINAL_VERSION and node.get("TargetPlatform") == PLATFORM,
            "wrong VSIX identity")
    node.set("Version", LOCAL_VERSION)
    xml.find(f".//{{{NS}}}DisplayName").text = "Codex (Lain local groups)"
    rewrite_staged(manifest, ET.tostring(xml.getroot(), encoding="utf-8", xml_declaration=True))


def make_vsix(tree, output):
    entries = inventory(tree)
    with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for rel, info in sorted(entries.items()):
            name = "extension/" + rel
            if info["kind"] == "directory":
                name += "/"
            zi = zipfile.ZipInfo(name, (2026, 10, 26, 0, 0, 0))
            zi.create_system = 3
            zi.external_attr = ((stat.S_IFDIR if info["kind"] == "directory" else stat.S_IFREG) | info["mode"]) << 16
            zi.compress_type = zipfile.ZIP_DEFLATED
            if info["kind"] == "directory":
                archive.writestr(zi, b"")
                continue
            with read_file(tree / rel) as src, archive.open(zi, "w", force_zip64=True) as out:
                while chunk := src.read(1024 * 1024):
                    out.write(chunk)
        # Preserve .vsixmanifest inside the tree as well as standard VSIX metadata.
        with read_file(tree / ".vsixmanifest") as handle:
            archive.writestr("extension.vsixmanifest", handle.read())
        archive.writestr("[Content_Types].xml", '<?xml version="1.0" encoding="utf-8"?>'
                         '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                         '<Default Extension="json" ContentType="application/json"/>'
                         '<Default Extension="vsixmanifest" ContentType="text/xml"/>'
                         '</Types>')
    os.chmod(output, 0o600)
    with read_file(output) as handle:
        os.fsync(handle.fileno())


def default_transform(source, dest):
    location = Path(__file__).with_name("vscode_groups_patch.py")
    spec = importlib.util.spec_from_file_location("vscode_groups_patch", location)
    module = importlib.util.module_from_spec(spec)
    with read_file(location) as handle:
        script = handle.read()
    exec(compile(script, str(location), "exec"), module.__dict__)
    report = module.prepare_tree(source, dest)
    require(isinstance(report, dict), "prepare_tree must return a dictionary")
    report["maintenancePatchHelperSha256"] = hashlib.sha256(script).hexdigest()
    return report


def prepare(source, dest, input_manifest, transform=None):
    source, dest = absolute(source), absolute(dest)
    safe_dir(dest.parent)
    require(not dest.exists() and not dest.is_symlink(), "candidate destination already exists")
    require(not dest.is_relative_to(source) and not source.is_relative_to(dest), "overlapping paths")
    pins, input_sha = pinned_json(input_manifest)
    original = validate_original(source, pins)
    with tempfile.TemporaryDirectory(prefix=".lain-vscode-prepare-", dir=dest.parent) as scratch:
        staging = Path(scratch) / "result"
        staging.mkdir(mode=0o700)
        copy_tree(source, staging / "original", original)
        report = (transform or default_transform)(staging / "original", staging / "candidate")
        require(isinstance(report, dict), "prepare_tree must return a dictionary")
        require(inventory(staging / "original") == original, "transform modified original backup")
        before_identity = inventory(staging / "candidate")
        for rel, sha in report.get("files", {}).items():
            require(before_identity.get(rel, {}).get("sha256") == sha, "transform report differs from copied files")
        source_targets = {"lain-groups-host.cjs": "out/lain-groups-host.cjs",
                          "lain-groups-query.js": "webview/assets/lain-groups-query.js",
                          "lain-groups-ui.js": "webview/assets/lain-groups-ui.js"}
        for name, sha in report.get("sourcePins", {}).items():
            require(name in source_targets and before_identity.get(source_targets[name], {}).get("sha256") == sha,
                    "adapter source changed while preparing candidate")
        require(all(rel in before_identity for rel in original), "transform removed original resources")
        for rel in set(original) | set(before_identity):
            if rel == "package.json":
                before = read_json(staging / "original" / rel)
                after = read_json(staging / "candidate" / rel)
                after["displayName"] = before.get("displayName")
                marker = after.pop("lainLocalModelGroups", None)
                require(after == before and (marker is None or marker == {
                    "revision": 1, "upstreamVersion": ORIGINAL_VERSION, "official": False}),
                    "transform changed protected package metadata")
                continue
            require(original.get(rel) == before_identity.get(rel) or patchable(rel),
                    f"transform changed a protected resource: {rel}")
        require(before_identity != original, "transform made no changes")
        set_local_identity(staging / "candidate", digest(original))
        candidate = inventory(staging / "candidate")
        make_vsix(staging / "original", staging / "original.vsix")
        make_vsix(staging / "candidate", staging / "candidate.vsix")
        require(inventory(source) == original, "original source changed during preparation")
        require(file_hash(input_manifest) == input_sha, "input manifest changed during preparation")
        manifest = {"schemaVersion": 1, "extensionId": EXTENSION_ID, "targetPlatform": PLATFORM,
                    "originalVersion": ORIGINAL_VERSION, "localVersion": LOCAL_VERSION,
                    "inputManifestSha256": input_sha, "inputPins": pins,
                    "original": original, "candidate": candidate, "transform": report,
                    "packages": {name: file_hash(staging / name) for name in ("original.vsix", "candidate.vsix")},
                    "activationQualified": False}
        create_file(staging / "manifest.json", encoded(manifest))
        sync_dir(staging)
        publish(staging, dest)
    return {"code": "prepared", "manifestSha256": file_hash(dest / "manifest.json"),
            "bundle": str(dest), "activationQualified": False, "reloadPerformed": False}


def load_bundle(bundle, expected=None):
    bundle = safe_dir(bundle)
    manifest, sha = pinned_json(bundle / "manifest.json")
    require(expected is None or expected == sha, "stale or changed candidate manifest")
    require(isinstance(manifest, dict), "invalid bundle manifest")
    require((manifest.get("schemaVersion"), manifest.get("extensionId"), manifest.get("targetPlatform"),
             manifest.get("originalVersion"), manifest.get("localVersion")) ==
            (1, EXTENSION_ID, PLATFORM, ORIGINAL_VERSION, LOCAL_VERSION), "unsupported bundle")
    for name in ("original", "candidate"):
        require(inventory(bundle / name) == manifest[name], f"{name} tree missing or changed")
    validate_original(bundle / "original", manifest["inputPins"])
    for name in ("original.vsix", "candidate.vsix"):
        require(file_hash(bundle / name) == manifest["packages"][name], f"changed package: {name}")
    return manifest, sha


def registry(extensions):
    entries = read_json(extensions / "extensions.json")
    require(isinstance(entries, list), "invalid VS Code extension registry")
    require(all(isinstance(e, dict) and isinstance(e.get("identifier"), dict)
                and isinstance(e["identifier"].get("id"), str) for e in entries), "malformed extension record")
    matches = [e for e in entries if e.get("identifier", {}).get("id", "").lower() == EXTENSION_ID]
    require(len(matches) == 1, "expected exactly one selected openai.chatgpt record")
    record = matches[0]
    require(isinstance(record.get("metadata"), dict) and isinstance(record.get("location"), dict),
            "malformed selected extension metadata/location")
    rel = record.get("relativeLocation")
    require(isinstance(rel, str) and len(PurePosixPath(rel).parts) == 1 and rel not in (".", ".."),
            "invalid selected extension location")
    location = extensions / rel
    require(record.get("location", {}).get("scheme") == "file"
            and record["location"].get("path") == str(location), "extension location mismatch")
    if "fsPath" in record["location"]:
        require(record["location"]["fsPath"] == str(location), "extension fsPath mismatch")
    require(record.get("metadata", {}).get("targetPlatform", "undefined") in (PLATFORM, "undefined"),
            "unknown selected platform")
    return entries, record, location


def same_installed_tree(location, expected, reference):
    actual = inventory(location)
    # VS Code adds __metadata to package.json; no other content is exempted.
    require(set(actual) == set(expected), "installed inventory differs from prepared tree")
    for rel, info in expected.items():
        if rel == "package.json":
            current = read_json(location / rel)
            wanted = read_json(reference / rel)
            current.pop("__metadata", None)
            wanted.pop("__metadata", None)
            require(current == wanted and actual[rel]["mode"] == info["mode"], "installed package differs")
        else:
            require(actual[rel] == info, f"installed resource differs: {rel}")


def selected_variant(extensions, bundle, manifest):
    entries, record, location = registry(extensions)
    version = record.get("version")
    if version == ORIGINAL_VERSION:
        variant = "original"
    elif version == LOCAL_VERSION:
        variant = "candidate"
        require(record.get("metadata", {}).get("source") == "vsix"
                and record["metadata"].get("pinned") is True, "local variant is not a pinned VSIX")
        require(location.name == f"{EXTENSION_ID}-{LOCAL_VERSION}", "unexpected local VSIX directory")
    else:
        raise MaintenanceError(f"unknown extension update {version}; left untouched")
    same_installed_tree(location, manifest[variant], bundle / variant)
    return variant, entries, record, location


def profile_guard(user_data):
    safe_dir(user_data)
    profiles = user_data / "User" / "profiles"
    if profiles.exists() or profiles.is_symlink():
        safe_dir(profiles)
        for profile in profiles.iterdir():
            safe_dir(profile)
            resource = profile / "extensions.json"
            if resource.exists() or resource.is_symlink():
                entries = read_json(resource)
                require(isinstance(entries, list), "unknown profile registry")
                require(not any(e.get("identifier", {}).get("id", "").lower() == EXTENSION_ID
                                for e in entries), "Codex in another VS Code profile needs separate qualification")


def validate_evidence(evidence, manifest, sha):
    require(isinstance(evidence, dict) and evidence.get("schemaVersion") == 1 and evidence.get("kind") == "lain-vscode-groups-native"
            and evidence.get("result") == "passed", "native qualification did not pass")
    require(evidence.get("manifestSha256") == sha
            and evidence.get("candidateVsixSha256") == manifest["packages"]["candidate.vsix"]
            and evidence.get("candidateTreeSha256") == digest(manifest["candidate"]),
            "native evidence is for different candidate bytes")
    require(all(evidence.get("checks", {}).get(check) is True for check in NATIVE_CHECKS),
            "native evidence lacks required runtime assertions")


def qualification(bundle, manifest, sha):
    path = bundle / "qualification.json"
    if not path.exists() and not path.is_symlink():
        return None
    receipt = read_json(path)
    require(receipt.get("schemaVersion") == 1 and receipt.get("manifestSha256") == sha,
            "stale qualification receipt")
    require(digest(receipt["evidence"]) == receipt.get("evidenceContentSha256"), "qualification evidence changed")
    validate_evidence(receipt["evidence"], manifest, sha)
    return receipt


def qualify(bundle, evidence_path, expected):
    bundle = safe_dir(bundle)
    require(expected is not None, "qualification requires the reviewed manifest pin")
    with mutation_lock(bundle):
        manifest, sha = load_bundle(bundle, expected)
        evidence, evidence_sha = pinned_json(evidence_path)
        validate_evidence(evidence, manifest, sha)
        receipt = {"schemaVersion": 1, "manifestSha256": sha, "evidenceFileSha256": evidence_sha,
                   "evidenceContentSha256": digest(evidence), "evidence": evidence}
        with tempfile.TemporaryDirectory(prefix=".qualification-", dir=bundle) as temporary:
            staged = Path(temporary) / "receipt.json"
            create_file(staged, encoded(receipt))
            publish(staged, bundle / "qualification.json")
    return {"code": "qualified", "manifestSha256": sha, "qualificationSha256": file_hash(bundle / "qualification.json")}


def isolation_guard(root, extensions, user_data):
    root = safe_dir(root)
    require(root != Path.home() and extensions != root and user_data != root
            and extensions.is_relative_to(root) and user_data.is_relative_to(root),
            "isolated extension/user-data directories must be children of --isolation-root")
    for production in (Path.home() / ".vscode/extensions", Path.home() / ".config/Code"):
        require(not extensions.is_relative_to(production) and not user_data.is_relative_to(production),
                "production directory is not an isolated target")


@contextmanager
def mutation_lock(bundle):
    path = bundle / ".maintenance.lock"
    fd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    try:
        st = os.fstat(fd)
        require(stat.S_ISREG(st.st_mode) and st.st_uid == os.getuid() and st.st_nlink == 1
                and stat.S_IMODE(st.st_mode) == 0o600, "unsafe maintenance lock")
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise MaintenanceError("another maintenance operation is running") from exc
        require(identity(st) == identity(path.lstat()), "maintenance lock replaced")
        yield
    finally:
        os.close(fd)


def run_cli(command):
    env = os.environ.copy()
    # A launcher inherited from a remote VS Code terminal must not redirect this
    # explicitly local package operation into a remote CLI or an existing window.
    for key in ("VSCODE_IPC_HOOK_CLI", "VSCODE_IPC_HOOK", "VSCODE_CLI"):
        env.pop(key, None)
    process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, env=env, start_new_session=True)
    try:
        process.communicate(timeout=180)
    except BaseException:
        try:
            os.killpg(process.pid, signal.SIGTERM)
            process.communicate(timeout=3)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.communicate()
        except ProcessLookupError:
            process.wait()
        raise
    require(process.returncode == 0, f"VS Code CLI failed ({process.returncode}); inspect transaction; no automatic retry")


def maintain(bundle, extensions, user_data, code, expected, *, rollback=False, apply=False, runner=None,
             isolation_root=None):
    bundle, extensions, user_data = absolute(bundle), safe_dir(extensions), absolute(user_data)
    require(Path(code).is_absolute(), "--code must name an absolute trusted local VS Code launcher")
    require(not bundle.is_relative_to(extensions) and not extensions.is_relative_to(bundle), "overlapping state paths")
    manifest, sha = load_bundle(bundle, expected)
    qualified = None if rollback else qualification(bundle, manifest, sha)
    if isolation_root is not None:
        isolation_guard(absolute(isolation_root), extensions, user_data)
    profile_guard(user_data)
    variant, entries, record, location = selected_variant(extensions, bundle, manifest)
    desired = "original" if rollback else "candidate"
    require(variant != desired, f"{desired} is already selected; no overwrite attempted")
    # A previous candidate directory must never be silently replaced by --force.
    destination = extensions / f"{EXTENSION_ID}-{manifest['originalVersion' if rollback else 'localVersion']}"
    require(not destination.exists() and not destination.is_symlink(), "install destination already exists")
    command = [str(code), "--user-data-dir", str(user_data), "--extensions-dir", str(extensions),
               "--install-extension", str(bundle / (desired + ".vsix")), "--force",
               "--do-not-include-pack-dependencies"]
    if record.get("metadata", {}).get("isMachineScoped"):
        command.append("--do-not-sync")
    plan = {"code": "rollback-planned" if rollback else "install-planned", "manifestSha256": sha,
            "selectedVariant": variant, "desiredVariant": desired, "command": command,
            "reloadPerformed": False, "activationQualified": qualified is not None}
    if not apply:
        return plan
    require(expected is not None, "--apply requires --expect-manifest-sha256")
    require(rollback or qualified is not None or isolation_root is not None,
            "production install requires qualification; native fixtures may use --isolation-root")
    with mutation_lock(bundle):
        load_bundle(bundle, expected)
        require(registry(extensions)[:2] == (entries, record), "extension registry changed before install")
        selected_variant(extensions, bundle, manifest)
        preserved = {location: inventory(location)}
        gallery_original = extensions / f"{EXTENSION_ID}-{ORIGINAL_VERSION}-{PLATFORM}"
        if gallery_original.exists() and gallery_original != location:
            same_installed_tree(gallery_original, manifest["original"], bundle / "original")
            preserved[gallery_original] = inventory(gallery_original)
        require(not destination.exists() and not destination.is_symlink(), "install destination appeared concurrently")
        # Durable append-only intent before invoking the external package manager.
        transaction = bundle / ("transaction-" + uuid.uuid4().hex)
        transaction.mkdir(mode=0o700)
        create_file(transaction / "intent.json", encoded(dict(plan, extensions=str(extensions),
                    userData=str(user_data), previousRegistry=entries, previousLocation=str(location))))
        sync_dir(transaction)
        sync_dir(bundle)
        (runner or run_cli)(command)
        for path, before in preserved.items():
            require(inventory(path) == before,
                    "CLI changed running Original resources; inspect transaction; no automatic recovery")
        selected, after, current, _ = selected_variant(extensions, bundle, manifest)
        require(selected == desired, "CLI did not select the requested variant")
        unrelated = lambda values: [e for e in values if e.get("identifier", {}).get("id", "").lower() != EXTENSION_ID]
        require(unrelated(entries) == unrelated(after), "CLI changed unrelated extension records")
        for key in ("isApplicationScoped", "isMachineScoped", "isBuiltin"):
            require(record.get("metadata", {}).get(key) == current.get("metadata", {}).get(key),
                    f"CLI changed identity/scope metadata: {key}")
        receipt = dict(plan, code="reload-needed", selectedVariant=desired,
                       transaction=transaction.name, installedRegistrySha256=file_hash(extensions / "extensions.json"))
        create_file(transaction / "completed.json", encoded(receipt))
        sync_dir(transaction)
        return receipt


def status(bundle, extensions):
    """Read only; disk selection cannot prove the running host has reloaded."""
    result = {"schemaVersion": 1, "readyForGroups": False, "reloadPerformed": False,
              "runtimeActivation": "not-observed"}
    try:
        bundle, extensions = safe_dir(bundle), safe_dir(extensions)
        manifest, sha = load_bundle(bundle)
        qualified = qualification(bundle, manifest, sha)
        result.update(manifestSha256=sha, activationQualified=qualified is not None)
        variant, _, record, location = selected_variant(extensions, bundle, manifest)
        result.update(selectedVariant=variant, installedVersion=record["version"], location=str(location),
                      code="reload-needed" if variant == "candidate" else "original-selected",
                      readyForGroups=variant == "candidate" and qualified is not None)
        incomplete = []
        for transaction in bundle.glob("transaction-*"):
            safe_dir(transaction)
            intent = read_json(transaction / "intent.json")
            if intent.get("extensions") == str(extensions) and not (transaction / "completed.json").exists():
                incomplete.append(transaction.name)
        if incomplete:
            result.update(code="maintenance-required", incompleteTransactions=incomplete, readyForGroups=False)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        result.update(code="maintenance-required", issue=str(exc), readyForGroups=False)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("--source", type=Path, required=True)
    prep.add_argument("--bundle", type=Path, required=True)
    prep.add_argument("--input-manifest", type=Path, required=True)
    qual = commands.add_parser("qualify")
    qual.add_argument("--bundle", type=Path, required=True)
    qual.add_argument("--evidence", type=Path, required=True)
    qual.add_argument("--expect-manifest-sha256", required=True)
    for name in ("status", "install", "rollback"):
        command = commands.add_parser(name)
        command.add_argument("--bundle", type=Path, required=True)
        command.add_argument("--extensions-dir", type=Path, required=True)
        if name != "status":
            command.add_argument("--user-data-dir", type=Path, required=True)
            command.add_argument("--code", type=Path, required=True)
            command.add_argument("--expect-manifest-sha256")
            command.add_argument("--apply", action="store_true")
            command.add_argument("--isolation-root", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "prepare":
            result = prepare(args.source, args.bundle, args.input_manifest)
        elif args.command == "status":
            result = status(args.bundle, args.extensions_dir)
        elif args.command == "qualify":
            result = qualify(args.bundle, args.evidence, args.expect_manifest_sha256)
        else:
            result = maintain(args.bundle, args.extensions_dir, args.user_data_dir, args.code,
                              args.expect_manifest_sha256, rollback=args.command == "rollback", apply=args.apply,
                              isolation_root=args.isolation_root)
        print(json.dumps(result, indent=2, ensure_ascii=False))
        return 0
    except (OSError, ValueError, KeyError, TypeError, subprocess.TimeoutExpired) as exc:
        print(json.dumps({"code": "refused", "issue": str(exc), "reloadPerformed": False}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
