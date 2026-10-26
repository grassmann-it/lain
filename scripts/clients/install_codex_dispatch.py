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
CODE_MODE_HOST = "codex-code-mode-host"
MANAGED = ("dispatch.py", "manifest.json", "receipt.json", *SUPPORT, CODE_MODE_HOST,
           "runtime/bin/" + CODE_MODE_HOST)
SUPPORTED_GRAMMAR = "codex-0.164.0"
RUNTIME_TARGET = "x86_64-unknown-linux-gnu"
RUNTIME_EXECUTABLES = frozenset(("codex-path/rg", "codex-resources/bwrap",
                                 "codex-resources/voice/bin/codex-voice-host"))
# Finite GNU package contract. New upstream resources need an explicit review.
RUNTIME_FILES = frozenset("""
codex-package.json codex-path/rg codex-resources/bwrap
codex-resources/voice/NOTICE.md codex-resources/voice/manifest.json
codex-resources/voice/runtime.json codex-resources/voice/sources.json
codex-resources/voice/bin/codex-voice-host
codex-resources/voice/lib/libffi.so.8 codex-resources/voice/lib/libgio-2.0.so.0
codex-resources/voice/lib/libglib-2.0.so.0 codex-resources/voice/lib/libgmodule-2.0.so.0
codex-resources/voice/lib/libgobject-2.0.so.0 codex-resources/voice/lib/libgstallocators-1.0.so.0
codex-resources/voice/lib/libgstapp-1.0.so.0 codex-resources/voice/lib/libgstaudio-1.0.so.0
codex-resources/voice/lib/libgstbase-1.0.so.0 codex-resources/voice/lib/libgstnet-1.0.so.0
codex-resources/voice/lib/libgstpbutils-1.0.so.0 codex-resources/voice/lib/libgstreamer-1.0.so.0
codex-resources/voice/lib/libgstrtp-1.0.so.0 codex-resources/voice/lib/libgsttag-1.0.so.0
codex-resources/voice/lib/libgstvideo-1.0.so.0 codex-resources/voice/lib/libintl.so.8
codex-resources/voice/lib/libopus.so.0 codex-resources/voice/lib/libpcre2-8.so.0
codex-resources/voice/lib/libz.so.1
codex-resources/voice/lib/gstreamer-1.0/libgstapp.so
codex-resources/voice/lib/gstreamer-1.0/libgstaudioconvert.so
codex-resources/voice/lib/gstreamer-1.0/libgstaudioresample.so
codex-resources/voice/lib/gstreamer-1.0/libgstcoreelements.so
codex-resources/voice/lib/gstreamer-1.0/libgstopus.so
codex-resources/voice/lib/gstreamer-1.0/libgstrtp.so
codex-resources/voice/lib/gstreamer-1.0/libgstrtpmanager.so
codex-resources/voice/licenses/LGPL-2.1.txt codex-resources/voice/licenses/Opus.txt
codex-resources/voice/licenses/PCRE2.md codex-resources/voice/licenses/libffi.txt
codex-resources/voice/licenses/proxy-libintl.txt codex-resources/voice/licenses/sljit.txt
codex-resources/voice/licenses/zlib.txt
""".split())


class NonExecutableLauncher(ValueError):
    pass


class DependencyQualificationError(ValueError):
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


def patched_path(manifest):
    relative = manifest.get("patchedRelativePath", "codex-lain")
    if relative not in ("codex-lain", "runtime/bin/codex"):
        raise ValueError("unsupported patched relative path")
    return Path(relative)


def qualified_runtime_package(patch_record):
    if "runtimePackage" not in patch_record:
        return None
    package = patch_record["runtimePackage"]
    if (not isinstance(package, dict) or set(package) != {"target", "files"}
            or package["target"] != RUNTIME_TARGET or not isinstance(package["files"], dict)
            or not {"codex-package.json", "codex-path/rg", "codex-resources/bwrap"} <= package["files"].keys()
            or package["files"].keys() - RUNTIME_FILES
            or any(not isinstance(pin, str) or len(pin) != 64
                   or any(char not in "0123456789abcdef" for char in pin)
                   for pin in package["files"].values())):
        raise ValueError("invalid runtime package qualification: target, finite paths or SHA256 pins")
    return package


def bound_runtime_package(manifest, patch_record):
    package = qualified_runtime_package(patch_record)
    if (manifest.get("runtimePackage") != package
            or patched_path(manifest) != Path("runtime/bin/codex" if package else "codex-lain")):
        raise ValueError("runtime package binding differs from qualified layout; explicitly reinstall")
    return package


def checked_runtime_file(path, expected, executable=False):
    path = safe_path(path)
    flags = os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC
    with os.fdopen(os.open(path, flags), "rb") as stream:
        before = os.fstat(stream.fileno())
        if (not stat.S_ISREG(before.st_mode) or before.st_uid != os.getuid()
                or before.st_nlink != 1 or before.st_mode & 0o022
                or (executable and not os.access(path, os.X_OK))):
            raise ValueError(f"runtime file must be an owned protected regular single-link file: {path}")
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
        after = os.fstat(stream.fileno())
        if (actual != expected
                or (before.st_size, before.st_mtime_ns, before.st_ctime_ns)
                != (after.st_size, after.st_mtime_ns, after.st_ctime_ns)
                or file_identity(path) != {"device": before.st_dev, "inode": before.st_ino}):
            raise ValueError(f"runtime file SHA256 or identity changed: {path}")
    return path


def checked_runtime_package(root, package):
    """Verify the exact tree without following links or opening special files."""
    expected = set(package["files"]) | {"bin/codex", "bin/" + CODE_MODE_HOST}
    directories = {str(parent) for name in expected for parent in Path(name).parents if str(parent) != "."}

    def inspect(directory, relative=""):
        details = safe_path(directory).lstat()
        if not stat.S_ISDIR(details.st_mode) or details.st_uid != os.getuid() or details.st_mode & 0o022:
            raise ValueError(f"runtime package directory must be owned and protected: {directory}")
        found = set()
        for path in directory.iterdir():
            name = relative + path.name
            mode = path.lstat().st_mode
            if stat.S_ISDIR(mode) and name in directories:
                found.update(inspect(path, name + "/"))
            elif stat.S_ISREG(mode) and name in expected:
                found.add(name)
            else:
                raise ValueError(f"unexpected runtime package path or non-regular file: {path}")
        return found

    if inspect(root) != expected:
        raise ValueError("runtime package is missing qualified files")
    for name, pin in package["files"].items():
        checked_runtime_file(root / name, pin, name in RUNTIME_EXECUTABLES)


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


def atomic_binary_copy(source, destination, expected, mode=0o700, executable=True):
    descriptor, name = tempfile.mkstemp(prefix=".runtime-", dir=destination.parent)
    pending = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as output, os.fdopen(os.open(source, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK), "rb") as stream:
            if not stat.S_ISREG(os.fstat(stream.fileno()).st_mode):
                raise ValueError("runtime copy source must be a regular file")
            shutil.copyfileobj(stream, output)
            output.flush()
            os.fchmod(output.fileno(), mode)
            os.fsync(output.fileno())
        checked_runtime_file(pending, expected, executable)
        os.replace(pending, destination)
        sync_directory(destination.parent)
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


def qualified_pair(original_sha, patched_sha, version=None, catalog=None):
    if catalog is None:
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


def qualified_dependencies(patch_record):
    """The trusted patch record qualifies the complete runtime, not a version label."""
    dependencies = patch_record.get("runtimeDependencies")
    if not isinstance(dependencies, dict) or set(dependencies) != {CODE_MODE_HOST}:
        raise DependencyQualificationError("code-mode helper qualification is missing; qualify its bytes and provenance for this patched release before install/rebind")
    record = dependencies[CODE_MODE_HOST]
    if (not isinstance(record, dict)
            or not isinstance(record.get("sha256"), str)
            or len(record["sha256"]) != 64
            or any(character not in "0123456789abcdef" for character in record["sha256"])
            or record.get("version") != patch_record["version"]
            or not isinstance(record.get("upstreamCommit"), str)
            or len(record["upstreamCommit"]) != 40
            or any(character not in "0123456789abcdef" for character in record["upstreamCommit"])
            or record["upstreamCommit"] != patch_record.get("upstreamCommit")
            or record.get("provenance") not in ("same-source-build", "qualified-release")
            or not isinstance(record.get("evidence"), str) or not record["evidence"].strip()):
        raise DependencyQualificationError("code-mode helper qualification has invalid SHA256, version or provenance for this patched release")
    return dependencies


def checked_dependencies(root, dependencies):
    for name, record in dependencies.items():
        # Installed helpers must be owned copies, never a link to an updater's files.
        path = safe_path(root / name)
        details = path.lstat()
        if (not stat.S_ISREG(details.st_mode) or details.st_uid != os.getuid()
                or details.st_nlink != 1 or details.st_mode & 0o022):
            raise ValueError(f"runtime dependency must be an owned protected regular single-link executable file: {path}")
        checked_binary(path, record["sha256"])


def dependency_input(args, state, dependencies):
    source = getattr(args, "code_mode_host", None)
    explicit_sha = getattr(args, "code_mode_host_sha256", None)
    expected = dependencies[CODE_MODE_HOST]["sha256"]
    if source is None:
        if explicit_sha is not None:
            raise ValueError("--code-mode-host-sha256 requires --code-mode-host")
        checked_dependencies(state, dependencies)
        return state / CODE_MODE_HOST
    if explicit_sha is None or explicit_sha.lower() != expected:
        raise ValueError("code-mode helper SHA256 must explicitly match its qualified release pin")
    return checked_binary(source, expected)


def support_files(catalog=None):
    if catalog is None:
        _, catalog = qualifications()
    return {"maintenance.py": Path(__file__).read_bytes(),
            "codex_dispatch.py": Path(__file__).with_name("codex_dispatch.py").read_bytes(),
            "qualified-releases.json": catalog}


def render_dispatch(original, patched, original_sha, patched_sha, template=None, dependencies=None, runtime_package=None):
    template = Path(__file__).with_name("codex_dispatch.py").read_text() if template is None else template
    if 'GRAMMAR_VERSION = "' + SUPPORTED_GRAMMAR + '"' not in template:
        raise ValueError("dispatcher template grammar is not supported")
    for key, value in {"LAIN_ORIGINAL": str(original), "LAIN_PATCHED": str(patched),
                       "LAIN_ORIGINAL_SHA256": original_sha, "LAIN_PATCHED_SHA256": patched_sha}.items():
        token = '"@' + key + '@"'
        if token not in template:
            raise ValueError("dispatcher source template is not intact")
        template = template.replace(token, json.dumps(value))
    token = '"@LAIN_PATCHED_RUNTIME_DEPENDENCIES@"'
    if token not in template:
        raise ValueError("dispatcher source template lacks runtime dependency checks; use the trusted AP1 installer")
    if dependencies is None:
        raise ValueError("qualified runtime dependencies are required for the dispatcher")
    template = template.replace(token, json.dumps({name: record["sha256"] for name, record in dependencies.items()}))
    token = '"@LAIN_PATCHED_RUNTIME_PACKAGE@"'
    if token not in template and runtime_package is not None:
        raise ValueError("dispatcher source template lacks runtime package checks")
    template = template.replace(token, json.dumps(runtime_package) if runtime_package is not None else "None")
    return template.encode()


def upstream_path(entry, target):
    candidate = Path(target) if Path(target).is_absolute() else entry.parent / target
    try:
        return str(candidate.resolve(strict=True))
    except (OSError, RuntimeError):
        return None


def receipt(manifest, state):
    result = {"schemaVersion": 1, "entry": manifest["entry"], "state": str(state),
            "generation": manifest["generation"], "manifestSha256": sha256(state / "manifest.json"),
            "dispatcherSha256": manifest["dispatcherSha256"], "originalSha256": manifest["originalSha256"],
            "patchedSha256": manifest["patchedSha256"], "grammarVersion": manifest["grammarVersion"],
            "originalVersion": manifest["originalVersion"], "patchedVersion": manifest["patchedVersion"],
            "maintenance": str(state / "maintenance.py"), "maintenanceSha256": manifest["supportSha256"]["maintenance.py"]}
    if "runtimeDependencies" in manifest:
        result["runtimeDependencies"] = manifest["runtimeDependencies"]
    for key in ("patchedRelativePath", "runtimePackage", "preservedLegacyRuntime"):
        if key in manifest:
            result[key] = manifest[key]
    return result


def enrich_manifest(manifest, state, original_record, patch_record, support):
    result = dict(manifest)
    result.update(schema=3, generation=uuid.uuid4().hex, originalVersion=original_record["version"],
                  patchedVersion=patch_record["version"], grammarVersion=original_record["grammarVersion"],
                  upstreamResolvedAtBinding=upstream_path(Path(manifest["entry"]), manifest["previousSymlink"]),
                  supportSha256={name: hashlib.sha256(data).hexdigest() for name, data in support.items()})
    result["runtimeDependencies"] = qualified_dependencies(patch_record)
    package = qualified_runtime_package(patch_record)
    if package is not None:
        result.update(patchedRelativePath="runtime/bin/codex", runtimePackage=package)
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
        if "preservedLegacyRuntime" in manifest:
            preserved = manifest["preservedLegacyRuntime"]
            verify_legacy_runtime(state, {"patchedSha256": preserved["patchedSha256"]}, preserved)
        result["receipt"] = {"entry": manifest["entry"], "state": str(state), "manifestSha256": initial_manifest_sha}
        for name, path, expected, identity in (
                ("original", Path(manifest["original"]), manifest["originalSha256"], manifest["originalIdentity"]),
                ("patched", state / patched_path(manifest), manifest["patchedSha256"], None)):
            try:
                if name == "patched":
                    safe_path(path)
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
        dependencies = None
        package = None
        try:
            package = bound_runtime_package(manifest, patched)
            if package is not None:
                checked_runtime_package(state / "runtime", package)
                checked_runtime_file(state / patched_path(manifest), manifest["patchedSha256"], True)
        except (OSError, ValueError, RuntimeError) as error:
            issue("runtime-package-missing-or-changed", str(error))
        try:
            dependencies = qualified_dependencies(patched)
        except DependencyQualificationError as error:
            issue("helper-qualification-missing", str(error))
        if dependencies is not None:
            if manifest.get("runtimeDependencies") != dependencies:
                issue("helper-binding-upgrade-required", "Manifest lacks this release's qualified runtime dependencies; use the trusted AP1 installer for explicit rebind.")
            try:
                checked_dependencies((state / patched_path(manifest)).parent, dependencies)
            except (OSError, ValueError, RuntimeError):
                issue("helper-missing-or-changed", "Qualified codex-code-mode-host is missing, linked externally, not executable or changed; explicitly rebind with a qualified helper copy.")
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
            if dependencies is not None:
                try:
                    bound = render_dispatch(manifest["original"], state / patched_path(manifest), manifest["originalSha256"], manifest["patchedSha256"],
                                            safe_path(state / "codex_dispatch.py").read_text(), dependencies=dependencies, runtime_package=package)
                    if hashlib.sha256(bound).hexdigest() != manifest["dispatcherSha256"]:
                        raise ValueError("wrapper bindings differ")
                except ValueError:
                    issue("dispatcher-binding-mismatch", "Wrapper does not match its recorded original, patch, runtime dependencies and supported template; explicitly upgrade with the trusted AP1 installer.")
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


def runtime_package_input(args, state, package, patched_sha, dependencies):
    package_source = getattr(args, "runtime_package", None)
    if package is not None:
        if package_source is None:
            raise ValueError("--runtime-package is required for this qualified patched release")
        package_source = safe_path(package_source)
        if state == package_source or state in package_source.parents:
            raise ValueError("runtime package input must be outside the new install state")
        checked_runtime_package(package_source, package)
        checked_runtime_file(package_source / "bin/codex", patched_sha, True)
        checked_runtime_file(package_source / "bin" / CODE_MODE_HOST, dependencies[CODE_MODE_HOST]["sha256"], True)
    elif package_source is not None:
        raise ValueError("--runtime-package requires a qualified runtimePackage record")
    return package_source


def stage_runtime(stage, installed_relative, patched, patched_sha, helper, dependencies, package_source, package):
    stage_patch = stage / installed_relative
    stage_patch.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    atomic_binary_copy(patched, stage_patch, patched_sha)
    atomic_binary_copy(helper, stage_patch.parent / CODE_MODE_HOST, dependencies[CODE_MODE_HOST]["sha256"])
    if package is not None:
        for name, pin in package["files"].items():
            destination = stage / "runtime" / name
            destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            executable = name in RUNTIME_EXECUTABLES
            checked_runtime_file(package_source / name, pin, executable)
            atomic_binary_copy(package_source / name, destination, pin, 0o700 if executable else 0o600, executable)
        checked_runtime_package(package_source, package)
        checked_runtime_package(stage / "runtime", package)
    checked_dependencies(stage_patch.parent, dependencies)


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
    catalog, catalog_raw = qualifications()
    original_record, patch_record = qualified_pair(args.original_sha256.lower(), args.patched_sha256.lower(), catalog=catalog)
    dependencies = qualified_dependencies(patch_record)
    package = qualified_runtime_package(patch_record)
    package_source = runtime_package_input(args, state, package, args.patched_sha256.lower(), dependencies)
    installed_relative = patched_path({"patchedRelativePath": "runtime/bin/codex"} if package else {})
    installed_patch = state / installed_relative
    helper = dependency_input(args, installed_patch.parent, dependencies)
    if state in helper.parents:
        raise ValueError("helper input must be outside the new install state")
    support = support_files(catalog_raw)
    source = render_dispatch(original, installed_patch, args.original_sha256.lower(), args.patched_sha256.lower(), dependencies=dependencies, runtime_package=package)
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
        stage_runtime(stage, installed_relative, patched, args.patched_sha256.lower(), helper, dependencies, package_source, package)
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
        for staged_file in stage.rglob("*"):
            if staged_file.is_dir():
                continue
            with staged_file.open("rb") as stream:
                os.fsync(stream.fileno())
        for directory in sorted((path for path in stage.rglob("*") if path.is_dir()), reverse=True):
            sync_directory(directory)
        sync_directory(stage)
        os.rename(stage, state)
        sync_directory(state.parent)
        manifest["installedEntry"] = replace_link(entry, str(wrapper), old_entry)
        checked_binary(original, args.original_sha256)
        checked_dependencies(installed_patch.parent, dependencies)
        if package is not None:
            checked_runtime_package(state / "runtime", package)
            checked_runtime_file(installed_patch, args.patched_sha256.lower(), True)
        if file_identity(original) != original_identity or file_identity(wrapper) != manifest["dispatcherIdentity"] or sha256(wrapper) != manifest["dispatcherSha256"]:
            raise ValueError("Original Codex or dispatcher changed after installation; inspect retained state")
        atomic_write(state / "manifest.json", json_bytes(manifest))
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


def snapshot_files(state, preserve_legacy=False):
    result = {}
    manifest = json.loads((state / "manifest.json").read_text())
    helper_name = str(patched_path(manifest).parent / CODE_MODE_HOST)
    inactive = state / CODE_MODE_HOST
    if helper_name != CODE_MODE_HOST and (inactive.exists() or inactive.is_symlink()):
        preserved = manifest.get("preservedLegacyRuntime")
        if not isinstance(preserved, dict):
            raise ValueError("inactive legacy helper has no explicit preservation binding")
        verify_legacy_runtime(state, {"patchedSha256": preserved["patchedSha256"]}, preserved)
    for name in MANAGED:
        if (name.endswith(CODE_MODE_HOST) and name != helper_name) or (preserve_legacy and name == CODE_MODE_HOST):
            continue
        path = state / name
        if path.exists() or path.is_symlink():
            safe_path(path)
            if not path.is_file():
                raise ValueError("managed maintenance item is not a regular file")
            result[name] = {"sha256": sha256(path), "mode": stat.S_IMODE(path.stat().st_mode)}
    return result


def verify_snapshot(state, expected, preserve_legacy=False):
    if snapshot_files(state, preserve_legacy) != expected:
        raise ValueError("maintenance state changed while preparing; aborting")


def legacy_runtime_snapshot(state, manifest):
    """Preserve the inactive legacy runtime without adopting/qualifying its helper."""
    patched = safe_path(state / patched_path(manifest))
    checked_runtime_file(patched, manifest["patchedSha256"], True)
    helper = state / CODE_MODE_HOST
    result = {"patchedIdentity": file_identity(patched), "patchedSha256": manifest["patchedSha256"]}
    if helper.is_symlink():
        result["helper"] = {"kind": "unqualified-symlink", **link_snapshot(helper)}
    elif helper.exists():
        pin = sha256(safe_path(helper)) if stat.S_ISREG(helper.lstat().st_mode) else None
        checked_runtime_file(helper, pin, True)
        result["helper"] = {"kind": "regular", "sha256": pin, "identity": file_identity(helper)}
    else:
        result["helper"] = {"kind": "missing"}
    return result


def verify_legacy_runtime(state, manifest, expected):
    if legacy_runtime_snapshot(state, manifest) != expected:
        raise ValueError("preserved legacy runtime changed; refusing maintenance")


def remounted_identities(state, manifest):
    if manifest.get("schema") not in (2, 3) or manifest.get("dispatcher") != str(state / "dispatch.py"):
        raise ValueError("unrecognized launcher manifest")
    entry = Path(manifest["entry"])
    if not entry.is_absolute():
        raise ValueError("entry must be absolute")
    observed_entry = link_snapshot(entry)
    if observed_entry["target"] != str(state / "dispatch.py") or observed_entry["target"] != manifest["installedEntry"]["target"]:
        raise ValueError("entry symlink target changed; refusing identity acceptance")
    wrapper = safe_path(state / "dispatch.py")
    checked_runtime_file(wrapper, manifest["dispatcherSha256"], True)
    original = checked_binary(Path(manifest["original"]), manifest["originalSha256"])
    if str(original) != manifest["original"]:
        raise ValueError("original path resolution changed")
    checked_runtime_file(state / patched_path(manifest), manifest["patchedSha256"], True)
    previous = (manifest["installedEntry"]["identity"], manifest["dispatcherIdentity"], manifest["originalIdentity"])
    current = (observed_entry["identity"], file_identity(wrapper), file_identity(original))
    if any(set(old) != {"device", "inode"} or old["inode"] != new["inode"] for old, new in zip(previous, current)):
        raise ValueError("file inode changed; device-only identity acceptance refused")
    if (len({old["device"] for old in previous}) != 1 or len({new["device"] for new in current}) != 1
            or previous[0]["device"] == current[0]["device"]):
        raise ValueError("identities must show one consistent device-only remount")
    return {"installedEntry": observed_entry, "dispatcherIdentity": current[1], "originalIdentity": current[2]}


def reaccept_identities(args):
    """Explicitly accept a remount, keeping raw prior metadata for inspection."""
    state = safe_path(args.state)
    raw = safe_path(state / "manifest.json").read_bytes()
    if hashlib.sha256(raw).hexdigest() != args.expect_manifest_sha256:
        raise ValueError("manifest changed since inspection; refusing identity acceptance")
    manifest = json.loads(raw)
    identities = remounted_identities(state, manifest)
    # Require a known tuple, but do not adopt or qualify any legacy helper.
    qualified_pair(manifest["originalSha256"], manifest["patchedSha256"])
    old_receipt = None
    if manifest["schema"] == 3:
        old_receipt = safe_path(state / "receipt.json").read_bytes()
        if json.loads(old_receipt) != receipt(manifest, state):
            raise ValueError("receipt changed; inspect before identity acceptance")
    updated = dict(manifest, **identities)
    if updated["schema"] == 3:
        updated["generation"] = uuid.uuid4().hex
    transaction_id = uuid.uuid4().hex
    print(json.dumps({"operation": "reaccept-identities", "apply": args.apply, "transaction": transaction_id,
                      "expectManifestSha256": args.expect_manifest_sha256, "acceptedIdentities": identities,
                      "helperQualificationUnchanged": True}, indent=2))
    if not args.apply:
        return
    history = safe_path(state / "maintenance-history")
    history.mkdir(mode=0o700, exist_ok=True)
    transaction = history / transaction_id
    transaction.mkdir(mode=0o700)
    previous = transaction / "previous"
    previous.mkdir(mode=0o700)
    atomic_write(previous / "manifest.json", raw)
    if old_receipt is not None:
        atomic_write(previous / "receipt.json", old_receipt)
    journal = {"schemaVersion": 1, "operation": "reaccept-identities", "phase": "prepared",
               "beforeManifestSha256": args.expect_manifest_sha256, "acceptedIdentities": identities,
               "plannedManifestSha256": hashlib.sha256(json_bytes(updated)).hexdigest()}
    atomic_write(transaction / "transaction.json", json_bytes(journal))
    for directory in (previous, transaction, history, state):
        sync_directory(directory)
    if (sha256(state / "manifest.json") != args.expect_manifest_sha256
            or remounted_identities(state, manifest) != identities
            or (old_receipt is not None and (state / "receipt.json").read_bytes() != old_receipt)):
        raise ValueError("identity acceptance inputs changed while preparing")
    try:
        atomic_write(state / "manifest.json", json_bytes(updated))
        if updated["schema"] == 3:
            atomic_write(state / "receipt.json", json_bytes(receipt(updated, state)))
        owned_launcher(state, updated)
        if file_identity(checked_binary(Path(updated["original"]), updated["originalSha256"])) != updated["originalIdentity"]:
            raise ValueError("original changed after identity acceptance")
        journal.update(phase="committed", afterManifestSha256=sha256(state / "manifest.json"))
        atomic_write(transaction / "transaction.json", json_bytes(journal))
    except BaseException:
        journal["phase"] = "interrupted-inspect-before-recovery"
        atomic_write(transaction / "transaction.json", json_bytes(journal))
        raise


def rebind(args):
    state = safe_path(args.state)
    if sha256(safe_path(state / "manifest.json")) != args.expect_manifest_sha256:
        raise ValueError("manifest changed since status; inspect it again before rebind")
    manifest = json.loads((state / "manifest.json").read_text())
    owned_launcher(state, manifest)
    original = checked_binary(args.original, args.original_sha256)
    installed_patch = state / patched_path(manifest)
    if original == installed_patch or state in original.parents:
        raise ValueError("fallback must be an external original release, never this launcher or patched client")
    patch_input = getattr(args, "patched", None)
    patch_sha = getattr(args, "patched_sha256", None)
    upgrading = patch_input is not None or patch_sha is not None
    if upgrading and (patch_input is None or patch_sha is None):
        raise ValueError("--patched and --patched-sha256 are required together for patch upgrade")
    if not upgrading and getattr(args, "runtime_package", None) is not None:
        raise ValueError("--runtime-package requires explicit --patched and --patched-sha256 on rebind")
    selected_patch_sha = patch_sha.lower() if upgrading else manifest["patchedSha256"]
    catalog, catalog_raw = qualifications()
    original_record, patch_record = qualified_pair(args.original_sha256, selected_patch_sha, args.original_version, catalog=catalog)
    checked_binary(safe_path(installed_patch), manifest["patchedSha256"])
    preserved_legacy = None
    package_source = None
    patched = None
    if upgrading:
        package = qualified_runtime_package(patch_record)
        if (patched_path(manifest) != Path("codex-lain") or manifest.get("runtimePackage") is not None
                or package is None or selected_patch_sha == manifest["patchedSha256"]):
            raise ValueError("patch upgrade supports only an explicit legacy-to-package transition")
        if (state / "runtime").exists() or (state / "runtime").is_symlink():
            raise ValueError("runtime destination already exists; inspect retained runtime before upgrade")
        patched = checked_binary(patch_input, selected_patch_sha)
        if state in patched.parents or patched == original:
            raise ValueError("patch upgrade input must be a separate external binary")
        preserved_legacy = legacy_runtime_snapshot(state, manifest)
        installed_patch = state / "runtime/bin/codex"
    else:
        package = bound_runtime_package(manifest, patch_record)
    if package is not None and not upgrading:
        checked_runtime_package(state / "runtime", package)
        checked_runtime_file(installed_patch, manifest["patchedSha256"], True)
    dependencies = qualified_dependencies(patch_record)
    dependency_root = installed_patch.parent
    helper_name = str((dependency_root / CODE_MODE_HOST).relative_to(state))
    helper = dependency_input(args, dependency_root, dependencies)
    if upgrading:
        package_source = runtime_package_input(args, state, package, selected_patch_sha, dependencies)
        if state in helper.parents:
            raise ValueError("patch upgrade helper input must be external qualified bytes")
    # Refuse unknown links/non-files before creating any history or replacing files.
    before = snapshot_files(state, upgrading)
    support = support_files(catalog_raw)
    source = render_dispatch(original, installed_patch, args.original_sha256, selected_patch_sha, dependencies=dependencies, runtime_package=package)
    updated = enrich_manifest(manifest, state, original_record, patch_record, support)
    updated.update(original=str(original), originalSha256=args.original_sha256, originalIdentity=file_identity(original),
                   patchedSha256=selected_patch_sha, dispatcherSha256=hashlib.sha256(source).hexdigest(), uninstallSymlink=str(original))
    if upgrading:
        updated["preservedLegacyRuntime"] = preserved_legacy
    transaction_id = uuid.uuid4().hex
    print(json.dumps({"operation": "rebind", "apply": args.apply, "transaction": transaction_id,
                      "previousOriginal": manifest["original"], "selectedOriginal": str(original),
                      "originalVersion": original_record["version"], "grammarVersion": original_record["grammarVersion"],
                      "originalSha256": args.original_sha256, "runtimeDependencies": dependencies,
                      "patchUpgrade": upgrading, "patchedSha256": selected_patch_sha,
                      "patchedRelativePath": str(installed_patch.relative_to(state)), "runtimePackage": package,
                      "preservedLegacyRuntime": preserved_legacy,
                      "helperSource": str(helper), "expectManifestSha256": args.expect_manifest_sha256}, indent=2))
    if not args.apply:
        return
    history = safe_path(state / "maintenance-history")
    history.mkdir(mode=0o700, exist_ok=True)
    transaction = history / transaction_id
    transaction.mkdir(mode=0o700)
    previous = transaction / "previous"
    previous.mkdir(mode=0o700)
    for name in before:
        (previous / name).parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        shutil.copy2(state / name, previous / name)
        if sha256(previous / name) != before[name]["sha256"]:
            raise ValueError("maintenance backup differs; no changes made")
        with (previous / name).open("rb") as stream:
            os.fsync(stream.fileno())
    journal = {"schemaVersion": 1, "phase": "prepared", "before": before,
               "entry": manifest["installedEntry"], "beforeManifestSha256": args.expect_manifest_sha256,
               "plannedDispatcherSha256": updated["dispatcherSha256"],
               "plannedRuntimeSha256": {str((dependency_root / name).relative_to(state)): record["sha256"] for name, record in dependencies.items()},
               "plannedSupportSha256": updated["supportSha256"]}
    if upgrading:
        journal["packageUpgrade"] = {"preservedLegacyRuntime": preserved_legacy, "patchRecord": patch_record,
                                     "patchedSha256": selected_patch_sha}
    atomic_write(transaction / "transaction.json", json_bytes(journal))
    for directory in (*sorted((path for path in previous.rglob("*") if path.is_dir()), reverse=True), previous, transaction, history, state):
        sync_directory(directory)
    verify_snapshot(state, before, upgrading)
    owned_launcher(state, manifest)
    checked_binary(original, args.original_sha256)
    if package is not None and not upgrading:
        checked_runtime_package(state / "runtime", package)
    try:
        if upgrading:
            stage = transaction / "staged"
            stage.mkdir(mode=0o700)
            stage_runtime(stage, Path("runtime/bin/codex"), patched, selected_patch_sha, helper, dependencies, package_source, package)
            for directory in (*sorted((path for path in stage.rglob("*") if path.is_dir()), reverse=True), stage):
                sync_directory(directory)
            verify_snapshot(state, before, True)
            verify_legacy_runtime(state, manifest, preserved_legacy)
            owned_launcher(state, manifest)
            if (state / "runtime").exists() or (state / "runtime").is_symlink():
                raise ValueError("runtime destination changed during upgrade")
            os.rename(stage / "runtime", state / "runtime")
            sync_directory(state)
        atomic_write(state / "dispatch.py", source, 0o700)
        if not upgrading and helper != state / helper_name:
            atomic_binary_copy(helper, state / helper_name, dependencies[CODE_MODE_HOST]["sha256"])
        checked_dependencies(dependency_root, dependencies)
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
        checked_dependencies(dependency_root, dependencies)
        if package is not None:
            checked_runtime_package(state / "runtime", package)
            checked_runtime_file(installed_patch, selected_patch_sha, True)
        if upgrading:
            verify_legacy_runtime(state, manifest, preserved_legacy)
        journal.update(phase="committed", afterManifestSha256=sha256(state / "manifest.json"), after=snapshot_files(state, upgrading))
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
    if journal.get("operation") == "reaccept-identities":
        raise ValueError("identity acceptance retains raw metadata for inspection; it is not a rebind rollback transaction")
    if journal["phase"] == "rolled-back":
        raise ValueError("this maintenance transaction was already rolled back")
    upgrade = journal.get("packageUpgrade")
    previous = transaction / "previous"
    verify_snapshot(previous, journal["before"], upgrade is not None)
    current_sha = sha256(state / "manifest.json")
    if current_sha != args.expect_manifest_sha256 or current_sha not in (journal["beforeManifestSha256"], journal.get("plannedManifestSha256"), journal.get("afterManifestSha256")):
        raise ValueError("current manifest is not this transaction's known state; refusing rollback")
    old = json.loads((previous / "manifest.json").read_text())
    if upgrade is not None:
        if patched_path(old) != Path("codex-lain"):
            raise ValueError("invalid legacy package upgrade journal")
        verify_legacy_runtime(state, old, upgrade["preservedLegacyRuntime"])
        if (state / "runtime").exists() or (state / "runtime").is_symlink():
            new_package = qualified_runtime_package(upgrade["patchRecord"])
            if new_package is None:
                raise ValueError("upgrade runtime package binding missing")
            checked_runtime_package(state / "runtime", new_package)
            checked_runtime_file(state / "runtime/bin/codex", upgrade["patchedSha256"], True)
            checked_dependencies(state / "runtime/bin", qualified_dependencies(upgrade["patchRecord"]))
        elif journal.get("after") is not None:
            raise ValueError("upgrade runtime disappeared; refusing rollback")
    if link_snapshot(Path(old["entry"])) != journal["entry"]:
        raise ValueError("Codex entry changed; refusing rollback over a foreign update")
    actual = snapshot_files(state, upgrade is not None)
    if journal.get("after") is not None:
        if actual != journal["after"]:
            raise ValueError("maintenance files changed after rebind; refusing rollback")
    else:
        allowed = dict(journal["plannedSupportSha256"], **{"dispatch.py": journal["plannedDispatcherSha256"]},
                       **journal.get("plannedRuntimeSha256", {}))
        if set(journal["before"]) - set(actual):
            raise ValueError("prior managed files disappeared during interrupted maintenance; refusing rollback")
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
    prior_catalog = json.loads((previous / "qualified-releases.json").read_text()) if "qualified-releases.json" in journal["before"] else None
    _, prior_patch = qualified_pair(old["originalSha256"], old["patchedSha256"], catalog=prior_catalog)
    installed_patch = state / patched_path(old)
    checked_binary(safe_path(installed_patch), old["patchedSha256"])
    package = bound_runtime_package(old, prior_patch)
    if package is not None:
        checked_runtime_package(state / "runtime", package)
        checked_runtime_file(installed_patch, old["patchedSha256"], True)
    helper_name = str((patched_path(old).parent / CODE_MODE_HOST))
    if upgrade is None and ("runtimeDependencies" in old or helper_name in journal["before"]):
        prior_dependencies = qualified_dependencies(prior_patch)
        if "runtimeDependencies" in old and old["runtimeDependencies"] != prior_dependencies:
            raise ValueError("prior helper binding differs from its qualified release; refusing rollback")
        checked_dependencies(previous / patched_path(old).parent, prior_dependencies)
    if file_identity(original) != old["originalIdentity"]:
        raise ValueError("prior original identity changed; rebind a qualified existing original instead")
    print(json.dumps({"operation": "rollback-rebind", "apply": args.apply, "transaction": args.transaction,
                      "restoreOriginal": old["original"], "retainUpgradeRuntime": upgrade is not None,
                      "preservedLegacyRuntime": upgrade["preservedLegacyRuntime"] if upgrade else None,
                      "expectManifestSha256": args.expect_manifest_sha256}, indent=2))
    if not args.apply:
        return
    verify_snapshot(state, actual, upgrade is not None)
    for name, details in journal["before"].items():
        if name == helper_name:
            atomic_binary_copy(previous / name, state / name, details["sha256"], details["mode"])
        elif name not in ("manifest.json", "receipt.json"):
            atomic_write(state / name, (previous / name).read_bytes(), details["mode"])
    for name in actual:
        if name not in journal["before"] and not (upgrade is not None and name.startswith("runtime/")):
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
    if "runtimeDependencies" in old and upgrade is None:
        checked_dependencies(installed_patch.parent, old["runtimeDependencies"])
    if upgrade is not None:
        verify_legacy_runtime(state, old, upgrade["preservedLegacyRuntime"])
    if package is not None:
        checked_runtime_package(state / "runtime", package)
        checked_runtime_file(installed_patch, old["patchedSha256"], True)
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
    add.add_argument("--runtime-package", type=Path, help="complete native package directory; required when the qualified record pins runtimePackage")
    add.add_argument("--code-mode-host", type=Path, required=True, help="qualified helper artifact; copied beside the patched executable")
    add.add_argument("--code-mode-host-sha256", required=True, help="explicit helper SHA256, also qualified by the patched release registry")
    sub.add_parser("uninstall")
    status_parser = sub.add_parser("status", help="read-only versions, receipt and upstream drift check")
    status_parser.add_argument("--json", action="store_true")
    binding = sub.add_parser("rebind", help="explicitly select a qualified original; never download or build")
    binding.add_argument("--original", type=Path, required=True)
    binding.add_argument("--original-sha256", required=True)
    binding.add_argument("--original-version", required=True)
    binding.add_argument("--expect-manifest-sha256", required=True)
    binding.add_argument("--patched", type=Path, help="explicit legacy-to-package upgrade; retains the old runtime unchanged")
    binding.add_argument("--patched-sha256", help="required with --patched; must match a qualified package release")
    binding.add_argument("--runtime-package", type=Path, help="complete qualified package for the explicit patch upgrade")
    binding.add_argument("--code-mode-host", type=Path, help="explicit qualified helper for migration, repair or update; otherwise verify the installed copy")
    binding.add_argument("--code-mode-host-sha256", help="required with --code-mode-host; must match the qualified patched release")
    identities = sub.add_parser("reaccept-identities", help="explicit same-inode device-only remount acceptance; changes metadata only")
    identities.add_argument("--expect-manifest-sha256", required=True)
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
        operation = {"install": install, "uninstall": uninstall, "rebind": rebind, "rollback-rebind": rollback_rebind,
                     "reaccept-identities": reaccept_identities}[args.operation]
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
