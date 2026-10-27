#!/usr/bin/python3
"""Linux Codex 0.164 interactive-only Lain dispatcher; installed from a template."""
import contextlib
import json
import math
import os
import hashlib
from pathlib import Path
import stat
import sys
import tomllib

# Installer replaces these JSON string literals in its private copy.
ORIGINAL = "@LAIN_ORIGINAL@"
PATCHED = "@LAIN_PATCHED@"
ORIGINAL_SHA256 = "@LAIN_ORIGINAL_SHA256@"
PATCHED_SHA256 = "@LAIN_PATCHED_SHA256@"
PATCHED_RUNTIME_DEPENDENCIES = "@LAIN_PATCHED_RUNTIME_DEPENDENCIES@"
PATCHED_RUNTIME_PACKAGE = "@LAIN_PATCHED_RUNTIME_PACKAGE@"
GRAMMAR_VERSION = "codex-0.164.0"
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

SUBCOMMANDS = frozenset("agents tcp-tunnel exec e review login logout mcp plugin app-server remote-control app completion update doctor sandbox debug execpolicy apply a resume queue archive delete migrate-rollouts migrate-nfs-db unarchive fork cloud cloud-tasks responses-api-proxy stdio-to-uds exec-server features help".split())
VALUES = frozenset("config model local-provider profile sandbox cd add-dir ask-for-approval remote remote-auth-token-env enable disable image".split())
FLAGS = frozenset("strict-config oss approve-for-me not-so-yolo dangerously-bypass-approvals-and-sandbox yolo dangerously-bypass-hook-trust worktree search no-alt-screen no-daemon".split())
SHORT = {"c": "config", "m": "model", "p": "profile", "s": "sandbox", "C": "cd", "a": "ask-for-approval", "i": "image"}
ALIASES = {"not-so-yolo": "approve-for-me", "yolo": "dangerously-bypass-approvals-and-sandbox"}
REPEATABLE = {"config", "add-dir", "enable", "disable", "image"}
CONFIG_LIMIT = 1024 * 1024
CATALOG_LIMIT = 16 * 1024 * 1024
ROUTING_SETTINGS = {"profile", "model_provider", "model_catalog_json", "model_providers", "oss_provider"}


@contextlib.contextmanager
def trusted_directory(path, *, dir_fd=None):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=dir_fd)
    try:
        details = os.fstat(descriptor)
        if details.st_uid != os.getuid() or details.st_mode & 0o022:
            raise ValueError("untrusted config directory")
        yield descriptor
    finally:
        os.close(descriptor)


def read_trusted_file(directory, name, limit):
    flags = os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC
    with os.fdopen(os.open(name, flags, dir_fd=directory), "rb") as stream:
        before = os.fstat(stream.fileno())
        if (not stat.S_ISREG(before.st_mode) or before.st_uid != os.getuid()
                or before.st_mode & 0o022 or before.st_nlink != 1 or before.st_size > limit):
            raise ValueError("untrusted or oversized config file")
        raw = stream.read(limit + 1)
        after = os.fstat(stream.fileno())
        if (len(raw) > limit or len(raw) != before.st_size
                or (before.st_size, before.st_mtime_ns, before.st_ctime_ns)
                != (after.st_size, after.st_mtime_ns, after.st_ctime_ns)):
            raise ValueError("config file changed while reading")
        return raw


def strict_json(raw):
    def pairs(entries):
        result = {}
        for key, value in entries:
            if key in result:
                raise ValueError("duplicate JSON key")
            result[key] = value
        return result

    def invalid_constant(value):
        raise ValueError("nonfinite JSON number")

    def finite_float(value):
        result = float(value)
        return result if math.isfinite(result) else invalid_constant(value)

    return json.loads(raw.decode("utf-8"), object_pairs_hook=pairs,
                      parse_constant=invalid_constant, parse_float=finite_float)


def grouped_catalog(document):
    """Check the versioned extension; native Codex validates full ModelInfo data."""
    if not isinstance(document, dict) or not isinstance(document.get("models"), list):
        return False
    models = document["models"]
    if not models or any(not isinstance(model, dict) or not isinstance(model.get("slug"), str)
                         or not model["slug"] for model in models):
        return False
    slugs = {model["slug"] for model in models}
    if len(slugs) != len(models):
        return False
    picker = document.get("lain_model_picker")
    if (not isinstance(picker, dict) or set(picker) != {"schema_version", "groups"}
            or type(picker["schema_version"]) is not int or picker["schema_version"] != 1
            or not isinstance(picker["groups"], list) or not 1 <= len(picker["groups"]) <= 128):
        return False
    ids = set()
    for group in picker["groups"]:
        if (not isinstance(group, dict) or not {"id", "name", "models"} <= set(group)
                or set(group) - {"id", "name", "models", "router_model"}):
            return False
        for key, limit in (("id", 256), ("name", 1024)):
            label = group[key]
            if (not isinstance(label, str) or not label.strip() or len(label.encode("utf-8")) > limit
                    or any(ord(char) < 32 or 127 <= ord(char) <= 159 for char in label)):
                return False
        if group["id"] in ids:
            return False
        ids.add(group["id"])
        members = group["models"]
        router = group.get("router_model")
        if (not isinstance(members, list) or any(not isinstance(member, str) or member not in slugs for member in members)
                or len(set(members)) != len(members)
                or (router is not None and (not isinstance(router, str) or router not in slugs))):
            return False
    return True


def configured_lain_default():
    """Read only owned, bounded base config and its known Lain grouped catalog.

    Never load the lain profile as an overlay: base model and user settings win.
    Artifact receipts diagnose installation state, not picker eligibility; valid
    legacy exports and locally maintained catalogs need no receipt migration.
    """
    try:
        home = Path(os.environ.get("CODEX_HOME") or Path.home() / ".codex")
        if not home.is_absolute() or ".." in home.parts:
            return False
        with trusted_directory(home) as root:
            config = tomllib.loads(read_trusted_file(root, "config.toml", CONFIG_LIMIT).decode("utf-8"))
            if config.get("model_provider") != "lain" or "profile" in config:
                return False
            catalog_path = config.get("model_catalog_json")
            if not isinstance(catalog_path, str):
                return False
            relative = Path(catalog_path).relative_to(home)
            if relative.parts == ("lain.models.json",):
                return grouped_catalog(strict_json(read_trusted_file(root, "lain.models.json", CATALOG_LIMIT)))
            if len(relative.parts) != 3 or relative.parts[0] != ".lain-artifacts" or relative.name != "lain.models.json":
                return False
            generation = relative.parts[1]
            if len(generation) != 32 or any(char not in "0123456789abcdef" for char in generation):
                return False
            with trusted_directory(".lain-artifacts", dir_fd=root) as storage:
                with trusted_directory(generation, dir_fd=storage) as artifact:
                    catalog = read_trusted_file(artifact, "lain.models.json", CATALOG_LIMIT)
                    return grouped_catalog(strict_json(catalog))
    except (OSError, ValueError, UnicodeError, RecursionError, RuntimeError):
        # Uncertain evidence must leave Codex's own parser/error behavior intact.
        return False


def uses_lain(args):
    """Recognize an unambiguous root TUI invocation, otherwise defer to Codex.

    This deliberately does not parse subcommand grammars. Unknown options, help,
    duplicate singleton arguments and parser errors always use the original.
    """
    seen = set()
    profile = None
    overrides = []
    prompt = False
    index = 0
    while index < len(args):
        token = args[index]
        index += 1
        if token == "--":
            if len(args) - index > (0 if prompt else 1):
                return False
            break
        if not token.startswith("-") or token == "-":
            if prompt or token in SUBCOMMANDS:
                return False
            prompt = True
            continue
        value = None
        if token.startswith("--"):
            name, equals, attached = token[2:].partition("=")
            if equals:
                value = attached
        else:
            name = SHORT.get(token[1:2])
            if name is None:
                return False
            if len(token) > 2:
                value = token[2:].removeprefix("=")
        name = ALIASES.get(name, name)
        if name not in FLAGS | VALUES:
            return False
        if name in seen and name not in REPEATABLE:
            return False
        seen.add(name)
        if name in FLAGS:
            if value is not None:
                return False
            continue
        if value is None:
            if index >= len(args) or args[index].startswith("-"):
                return False
            value = args[index]
            index += 1
        if not value:
            return False
        if name == "image":
            # clap num_args=1.. consumes successive positional-looking values.
            if value in SUBCOMMANDS:
                return False
            while index < len(args) and not args[index].startswith("-"):
                if args[index] in SUBCOMMANDS:
                    return False
                index += 1
        if name == "profile":
            profile = value
        elif name == "config":
            if "=" not in value or not value.split("=", 1)[0].strip():
                return False
            overrides.append(value)
        elif name == "sandbox" and value not in {"read-only", "workspace-write", "danger-full-access"}:
            return False
        elif name == "ask-for-approval" and value not in {"on-request", "never"}:
            return False
    bypass = "dangerously-bypass-approvals-and-sandbox" in seen
    auto = "approve-for-me" in seen
    if (bypass or auto) and "ask-for-approval" in seen:
        return False
    if auto and (bypass or "sandbox" in seen):
        return False
    if profile is not None:
        return profile == "lain"
    if seen & {"oss", "local-provider", "remote", "remote-auth-token-env"}:
        return False
    try:
        if any(ROUTING_SETTINGS & tomllib.loads(value).keys() for value in overrides):
            return False
    except (ValueError, RecursionError):
        return False
    return configured_lain_default()


def restore_initial_environment():
    """Undo Python's possible locale coercion, preserving the exec-time env.

    This launcher is Linux-only. /proc/self/environ contains the environment
    supplied to the interpreter, before Python may add LC_CTYPE=C.UTF-8.
    """
    raw = Path("/proc/self/environ").read_bytes()
    initial = dict(entry.split(b"=", 1) for entry in raw.split(b"\0") if b"=" in entry)
    if dict(os.environb) != initial:
        os.environb.clear()
        os.environb.update(initial)


def main():
    target = PATCHED if uses_lain(sys.argv[1:]) else ORIGINAL
    try:
        check_target(target, PATCHED_SHA256 if target == PATCHED else ORIGINAL_SHA256)
        restore_initial_environment()
        os.execv(target, sys.argv)
    except OSError as error:
        print(f"lain Codex: cannot exec {target}: {error}", file=sys.stderr)
        return 126


def check_target(target, expected):
    kind = "patched binary" if target == PATCHED else "chosen original fallback"
    if not Path(target).is_file() or not os.access(target, os.X_OK):
        raise OSError(f"{kind} is missing or not executable; run maintenance status and explicitly rebind/reinstall")
    with Path(target).open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
    if actual != expected:
        raise OSError(f"{kind} bytes changed; run maintenance status and choose a qualified release explicitly")
    if target == PATCHED:
        if PATCHED_RUNTIME_PACKAGE is not None:
            check_runtime_package(target)
        if Path(target).is_symlink():
            raise OSError("patched binary must be a private regular copy; run maintenance status")
        if not isinstance(PATCHED_RUNTIME_DEPENDENCIES, dict) or set(PATCHED_RUNTIME_DEPENDENCIES) != {"codex-code-mode-host"}:
            raise OSError("code-mode helper binding is missing; explicitly upgrade using the trusted AP1 installer")
        for name, helper_sha in PATCHED_RUNTIME_DEPENDENCIES.items():
            helper = Path(target).parent / name
            if not helper.exists() and not helper.is_symlink():
                raise OSError(f"runtime dependency {name} is missing; run maintenance status and explicitly rebind a qualified helper")
            details = helper.lstat()
            if (not stat.S_ISREG(details.st_mode) or details.st_uid != os.getuid()
                    or details.st_nlink != 1 or details.st_mode & 0o022 or not os.access(helper, os.X_OK)):
                raise OSError(f"runtime dependency {name} must be a private regular executable copy; run maintenance status/rebind")
            with helper.open("rb") as stream:
                actual = hashlib.file_digest(stream, "sha256").hexdigest()
            if actual != helper_sha:
                raise OSError(f"runtime dependency {name} bytes changed; run maintenance status and explicitly rebind qualified bytes")


def check_runtime_package(target):
    """Use only embedded qualified pins, never mutable package metadata as pins."""
    try:
        package = PATCHED_RUNTIME_PACKAGE
        if (not isinstance(package, dict) or set(package) != {"target", "files"}
                or package["target"] != RUNTIME_TARGET or not isinstance(package["files"], dict)
                or not {"codex-package.json", "codex-path/rg", "codex-resources/bwrap"} <= package["files"].keys()
                or package["files"].keys() - RUNTIME_FILES
                or any(not isinstance(pin, str) or len(pin) != 64
                       or any(char not in "0123456789abcdef" for char in pin)
                       for pin in package["files"].values())
                or not isinstance(PATCHED_RUNTIME_DEPENDENCIES, dict)
                or set(PATCHED_RUNTIME_DEPENDENCIES) != {"codex-code-mode-host"}):
            raise ValueError("invalid embedded package binding")
        root = Path(target).parent.parent
        if (not root.is_absolute() or ".." in root.parts or Path(target) != root / "bin/codex"
                or any(path.is_symlink() for path in (root, *root.parents))):
            raise ValueError("invalid package root")
        pins = dict(package["files"], **{"bin/codex": PATCHED_SHA256,
                    "bin/codex-code-mode-host": PATCHED_RUNTIME_DEPENDENCIES["codex-code-mode-host"]})
        directories = {str(parent) for name in pins for parent in Path(name).parents if str(parent) != "."}
        executable = RUNTIME_EXECUTABLES | {"bin/codex", "bin/codex-code-mode-host"}

        def inspect(directory, prefix=""):
            found = set()
            for name in os.listdir(directory):
                relative = prefix + name
                details = os.stat(name, dir_fd=directory, follow_symlinks=False)
                if stat.S_ISDIR(details.st_mode) and relative in directories:
                    with trusted_directory(name, dir_fd=directory) as child:
                        found.update(inspect(child, relative + "/"))
                elif stat.S_ISREG(details.st_mode) and relative in pins:
                    flags = os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC
                    with os.fdopen(os.open(name, flags, dir_fd=directory), "rb") as stream:
                        before = os.fstat(stream.fileno())
                        if (not stat.S_ISREG(before.st_mode) or before.st_uid != os.getuid()
                                or before.st_nlink != 1 or before.st_mode & 0o022
                                or (relative in executable and not before.st_mode & stat.S_IXUSR)):
                            raise ValueError("unprotected or non-regular package file: " + relative)
                        actual = hashlib.file_digest(stream, "sha256").hexdigest()
                        after = os.fstat(stream.fileno())
                        observed = os.stat(name, dir_fd=directory, follow_symlinks=False)
                        if (actual != pins[relative]
                                or (before.st_dev, before.st_ino) != (observed.st_dev, observed.st_ino)
                                or (before.st_size, before.st_mtime_ns, before.st_ctime_ns)
                                != (after.st_size, after.st_mtime_ns, after.st_ctime_ns)):
                            raise ValueError("package file bytes or identity changed: " + relative)
                    found.add(relative)
                else:
                    raise ValueError("unexpected package path or non-regular file: " + relative)
            return found

        with trusted_directory(root) as directory:
            if inspect(directory) != set(pins):
                raise ValueError("package files are missing")
    except (OSError, ValueError, TypeError, KeyError, RuntimeError) as error:
        raise OSError(f"runtime package is missing, changed or unsafe ({error}); run maintenance status and explicitly reinstall qualified bytes") from error


if __name__ == "__main__":
    sys.exit(main())
