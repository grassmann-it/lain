#!/usr/bin/python3
"""Linux Codex 0.164 interactive-only profile dispatcher; installed from a template."""
import os
import hashlib
from pathlib import Path
import sys

# Installer replaces these JSON string literals in its private copy.
ORIGINAL = "@LAIN_ORIGINAL@"
PATCHED = "@LAIN_PATCHED@"
ORIGINAL_SHA256 = "@LAIN_ORIGINAL_SHA256@"
PATCHED_SHA256 = "@LAIN_PATCHED_SHA256@"
GRAMMAR_VERSION = "codex-0.164.0"

SUBCOMMANDS = frozenset("agents tcp-tunnel exec e review login logout mcp plugin app-server remote-control app completion update doctor sandbox debug execpolicy apply a resume queue archive delete migrate-rollouts migrate-nfs-db unarchive fork cloud cloud-tasks responses-api-proxy stdio-to-uds exec-server features help".split())
VALUES = frozenset("config model local-provider profile sandbox cd add-dir ask-for-approval remote remote-auth-token-env enable disable image".split())
FLAGS = frozenset("strict-config oss approve-for-me not-so-yolo dangerously-bypass-approvals-and-sandbox yolo dangerously-bypass-hook-trust worktree search no-alt-screen no-daemon".split())
SHORT = {"c": "config", "m": "model", "p": "profile", "s": "sandbox", "C": "cd", "a": "ask-for-approval", "i": "image"}
ALIASES = {"not-so-yolo": "approve-for-me", "yolo": "dangerously-bypass-approvals-and-sandbox"}
REPEATABLE = {"config", "add-dir", "enable", "disable", "image"}


def uses_lain(args):
    """Recognize an unambiguous root TUI invocation, otherwise defer to Codex.

    This deliberately does not parse subcommand grammars. Unknown options, help,
    duplicate singleton arguments and parser errors always use the original.
    """
    seen = set()
    profile = None
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
        elif name == "config" and ("=" not in value or not value.split("=", 1)[0].strip()):
            return False
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
    return profile == "lain"


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


if __name__ == "__main__":
    sys.exit(main())
