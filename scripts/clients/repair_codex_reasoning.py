#!/usr/bin/env python3
"""Bounded, offline repair of the legacy direct-Codex Astra client artifact.

No default user paths, HTTP, auth-file reads, profile apply, or service commands.
Prepare with explicit --config/--catalog/--output/--models/--source-fixture.
The output must be a new 32-hex generation beside the old artifact directory.
Apply requires the prepared plan and all three expected SHA256s; a conditional
rollback receipt is persisted BEFORE replacing config. Rollback requires its
expected SHA256 and the unchanged post-apply config SHA256.

flock serializes repair processes; the final content/identity check immediately
precedes atomic rename. As with ordinary filesystem CAS-by-hash, a writer that
ignores these locks must be quiescent during the final check/rename interval.
Copied host manifests are historical bytes, not receipts for a host installation.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import ctypes
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import tempfile
import tomllib

MODEL = "gpt-6-astra"
EFFORTS = ("low", "medium", "high", "xhigh", "max")
SOURCE = "https://github.com/openai/codex/blob/6aaac694803004a63c6f393d153c96d22521a6b9/codex-rs/models-manager/models.json"
SOURCE_SHA256 = "943ca7fe1d19ed019054158303f0dbbd80b3bef43b3aa425a71aa7cb3fb52c2b"
FIXTURE_SHA256 = "db59cd8caad8321c4e167e3b43f8341986008e77346103bd5d253cdb4481772f"
LIMIT = 16 * 1024 * 1024
PLAN = "repair.plan.json"
BEFORE = "repair.config.before.toml"
OLD_CATALOG = "repair.catalog.before.json"
AFTER = "repair.config.toml"


class RepairError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise RepairError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode()


def absolute(path):
    path = Path(path)
    require(path.is_absolute() and ".." not in path.parts, "paths must be absolute without parent traversal")
    return path


def identity(info):
    return (info.st_dev, info.st_ino, info.st_mode, info.st_uid, info.st_nlink,
            info.st_size, info.st_mtime_ns, info.st_ctime_ns)


@contextmanager
def directory(path):
    """Walk with openat/O_NOFOLLOW: no symlink ancestors or untrusted writers."""
    path = absolute(path)
    fd = os.open("/", os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        for part in path.parts[1:]:
            next_fd = os.open(part, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd)
            fd = next_fd
            info = os.fstat(fd)
            require(info.st_uid in (0, os.getuid()), "foreign directory owner")
            sticky_root = info.st_uid == 0 and info.st_mode & stat.S_ISVTX
            require(not info.st_mode & 0o022 or sticky_root, "untrusted writable directory")
        yield fd
    finally:
        os.close(fd)


def checked_read(fd, name):
    handle = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=fd)
    with os.fdopen(handle, "rb") as stream:
        before = os.fstat(stream.fileno())
        require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1,
                "input must be a single-link regular file")
        require(before.st_uid == os.getuid() and not before.st_mode & 0o022,
                "unsafe input ownership or permissions")
        require(before.st_size <= LIMIT, "input exceeds size limit")
        data = stream.read(LIMIT + 1)
        require(len(data) <= LIMIT and identity(before) == identity(os.fstat(stream.fileno())) ==
                identity(os.stat(name, dir_fd=fd, follow_symlinks=False)), "input changed during read")
    return data, before


def read(path):
    path = absolute(path)
    with directory(path.parent) as fd:
        return checked_read(fd, path.name)[0]


def pairs(items):
    result = {}
    for key, value in items:
        require(key not in result, "duplicate JSON key")
        result[key] = value
    return result


def document(data):
    try:
        return json.loads(data, object_pairs_hook=pairs,
                          parse_constant=lambda _: (_ for _ in ()).throw(RepairError("nonfinite JSON value")))
    except (UnicodeError, json.JSONDecodeError, RecursionError):
        raise RepairError("invalid JSON input") from None


def toml(data):
    try:
        return tomllib.loads(data.decode("utf-8"))
    except (UnicodeError, tomllib.TOMLDecodeError):
        raise RepairError("invalid TOML input") from None


def edit_config(data, old_catalog, new_catalog):
    """Parse first, then replace only unambiguous root scalar value tokens."""
    config = toml(data)
    require(config.get("model") == MODEL and config.get("model_provider") == "lain" and
            config.get("model_reasoning_effort") == "none" and
            config.get("model_catalog_json") == str(old_catalog) and "profile" not in config,
            "expected plain root lain/Astra configuration with effort none")
    provider = config.get("model_providers", {}).get("lain", {})
    require(isinstance(provider, dict) and provider.get("wire_api") == "responses",
            "expected lain Responses client binding")
    text = data.decode("utf-8")
    # Multiline TOML strings can imitate table boundaries or assignments. Fail
    # closed rather than pretending a regex is a general TOML concrete parser.
    require('"""' not in text and "'''" not in text, "multiline TOML is outside this repair contract")
    boundary = re.search(r"(?m)^[ \t]*\[", text)
    end = boundary.start() if boundary else len(text)
    root, tail = text[:end], text[end:]
    for name, replacement in (("model_catalog_json", str(new_catalog)), ("model_reasoning_effort", "low")):
        pattern = re.compile(r"(?m)^(?P<prefix>[ \t]*" + name + r"[ \t]*=[ \t]*)"
                             r"(?P<value>\"(?:[^\"\\\r\n]|\\.)*\"|'[^'\r\n]*')"
                             r"(?P<suffix>[ \t]*(?:#[^\r\n]*)?)(?=\r?$)")
        matches = list(pattern.finditer(root))
        require(len(matches) == 1, "expected one line-anchored root repair setting")
        match = matches[0]
        root = root[:match.start("value")] + json.dumps(replacement, ensure_ascii=False) + root[match.end("value"):]
    result = (root + tail).encode()
    expected = dict(config, model_catalog_json=str(new_catalog), model_reasoning_effort="low")
    require(toml(result) == expected, "repair changed unrelated TOML settings")
    return result


def fields(text, start):
    """JSON object value spans; json.loads already checked syntax/duplicates."""
    decoder = json.JSONDecoder()
    pos, result = start + 1, {}
    while True:
        pos = re.compile(r"\s*").match(text, pos).end()
        if text[pos] == "}":
            return result
        key, pos = decoder.raw_decode(text, pos)
        pos = re.compile(r"\s*:\s*").match(text, pos).end()
        _, end = decoder.raw_decode(text, pos)
        result[key] = (pos, end)
        pos = re.compile(r"\s*").match(text, end).end()
        if text[pos] == "}":
            return result
        pos += 1


def repair_catalog(data, fixture_data, models_data):
    require(sha(fixture_data) == FIXTURE_SHA256, "native reasoning fixture SHA256 mismatch")
    fixture = document(fixture_data)
    require(fixture.get("client_version") == "0.164.0" and fixture.get("source") == SOURCE and
            fixture.get("source_sha256") == SOURCE_SHA256, "native fixture provenance mismatch")
    native = next(item for item in fixture["models"] if item["slug"] == MODEL)
    levels = [item for item in native["supported_reasoning_levels"] if item["effort"] in EFFORTS]
    require(tuple(item["effort"] for item in levels) == EFFORTS and native["default_reasoning_level"] == "low",
            "native fixture does not establish the screenshot-supported efforts")
    models = document(models_data)
    require(isinstance(models, dict) and type(models.get("contractVersion")) is int and models["contractVersion"] == 1 and
            type(models.get("revision")) is int and models["revision"] >= 0 and
            isinstance(models.get("models"), list), "expected public lain models JSON snapshot")
    matches = [item for item in models["models"] if isinstance(item, dict) and item.get("id") == MODEL]
    require(len(matches) == 1 and matches[0].get("provider") == "codex" and
            matches[0].get("source") == "configured", "Astra direct Codex route is not uniquely established")
    catalog = document(data)
    require(isinstance(catalog, dict) and isinstance(catalog.get("models"), list), "expected models catalog")
    entries = catalog["models"]
    require(all(isinstance(item, dict) and isinstance(item.get("slug"), str) for item in entries),
            "invalid catalog entries")
    require(len({item["slug"] for item in entries}) == len(entries), "duplicate catalog slugs")
    selected = [item for item in entries if item["slug"] == MODEL]
    require(len(selected) == 1 and selected[0].get("default_reasoning_level") == "none" and
            selected[0].get("supported_reasoning_levels") == [] and
            selected[0].get("visibility") == "list" and selected[0].get("supported_in_api") is True and
            selected[0].get("description") == "Model: gpt-6-astra. Service: Codex.",
            "catalog is not the recognized broken direct-Codex Astra entry")
    text = data.decode("utf-8")
    root = fields(text, len(text) - len(text.lstrip()))
    pos = root["models"][0] + 1
    decoder = json.JSONDecoder()
    for entry in entries:
        pos = re.compile(r"\s*").match(text, pos).end()
        _, end = decoder.raw_decode(text, pos)
        if entry["slug"] == MODEL:
            spans = fields(text, pos)
            edits = [(spans["default_reasoning_level"], json.dumps("low")),
                     (spans["supported_reasoning_levels"], json.dumps(levels, ensure_ascii=False))]
            for (start, stop), replacement in sorted(edits, reverse=True):
                text = text[:start] + replacement + text[stop:]
            break
        pos = re.compile(r"\s*,\s*").match(text, end).end()
    result = text.encode()
    # Mutation above is deliberately span-based, preserving even unrelated JSON whitespace.
    selected[0]["default_reasoning_level"] = "low"
    selected[0]["supported_reasoning_levels"] = levels
    require(document(result) == catalog, "repair changed unrelated catalog data")
    return result, {"contractVersion": 1, "revision": models["revision"], "model": MODEL, "provider": "codex"}


def artifact_snapshot(catalog):
    require(catalog.name == "lain.models.json" and re.fullmatch(r"[0-9a-f]{32}", catalog.parent.name),
            "expected a generation artifact catalog")
    with directory(catalog.parent) as fd:
        names = sorted(os.listdir(fd))
        require(len(names) <= 64, "too many artifact files")
        require(all(name in ("lain.models.json", "lain.config.toml", "manifest.json") or
                    re.fullmatch(r"lain\.models\.json\.bak-[A-Za-z0-9_.-]+", name) for name in names),
                "unrecognized artifact: refusing to read potential auth or unrelated files")
        files = {name: checked_read(fd, name)[0] for name in names}
        require(sum(map(len, files.values())) <= 64 * 1024 * 1024, "artifact snapshot exceeds size limit")
        require(names == sorted(os.listdir(fd)), "artifact generation changed during read")
    return files


def hashes(files):
    return {name: sha(data) for name, data in files.items()}


def write_new(fd, name, data, mode=0o400):
    handle = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=fd)
    with os.fdopen(handle, "wb") as stream:
        stream.write(data)
        stream.flush()
        os.fchmod(stream.fileno(), mode)
        os.fsync(stream.fileno())


def publish(fd, source, target):
    """Linux no-replace rename; never overwrite a concurrently created output."""
    libc = ctypes.CDLL(None, use_errno=True)
    rename = libc.renameat2
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    require(rename(fd, os.fsencode(source), fd, os.fsencode(target), 1) == 0,
            "output already exists or atomic no-replace publication failed")
    os.fsync(fd)


def prepare(config, catalog, output, source_fixture, models):
    config, catalog, output, source_fixture, models = map(absolute, (config, catalog, output, source_fixture, models))
    require(output.parent == catalog.parent.parent and output != catalog.parent and
            re.fullmatch(r"[0-9a-f]{32}", output.name), "output must be a new sibling 32-hex generation")
    require(not output.exists() and not output.is_symlink(), "output already exists")
    before = read(config)
    artifacts = artifact_snapshot(catalog)
    old_catalog = artifacts[catalog.name]
    fixture_data, models_data = read(source_fixture), read(models)
    repaired, proof = repair_catalog(old_catalog, fixture_data, models_data)
    after = edit_config(before, catalog, output / catalog.name)
    files = dict(artifacts)
    files.update({catalog.name: repaired, BEFORE: before, OLD_CATALOG: old_catalog, AFTER: after})
    plan = {"schemaVersion": 1, "kind": "astra-reasoning-repair", "config": str(config),
            "catalog": str(catalog), "output": str(output), "beforeConfigSha256": sha(before),
            "afterConfigSha256": sha(after), "beforeCatalogSha256": sha(old_catalog),
            "afterCatalogSha256": sha(repaired), "sourceSha256": SOURCE_SHA256,
            "fixtureSha256": sha(fixture_data), "modelsSha256": sha(models_data), "routeProof": proof,
            "sourceArtifacts": hashes(artifacts), "files": hashes(files),
            "efforts": list(EFFORTS), "default": "low"}
    plan_data = encoded(plan)
    with directory(output.parent) as parent_fd:
        # mkdtemp is relative to the pinned descriptor, never a re-resolved ancestor.
        staging = Path(tempfile.mkdtemp(prefix=".reasoning-", dir=f"/proc/self/fd/{parent_fd}"))
        stage_name = staging.name
        stage_fd = os.open(stage_name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent_fd)
        try:
            for name, data in files.items():
                write_new(stage_fd, name, data)
            write_new(stage_fd, PLAN, plan_data)
            require(read(config) == before and artifact_snapshot(catalog) == artifacts,
                    "inputs changed during preparation")
            os.fchmod(stage_fd, 0o500)
            os.fsync(stage_fd)
            publish(parent_fd, stage_name, output.name)
        except BaseException:
            # A parent fsync may fail after publication has already succeeded.
            # Never dismantle a complete, visible candidate in that case.
            try:
                pending = os.stat(stage_name, dir_fd=parent_fd, follow_symlinks=False)
            except FileNotFoundError:
                pass
            else:
                opened = os.fstat(stage_fd)
                require((pending.st_dev, pending.st_ino) == (opened.st_dev, opened.st_ino),
                        "staging directory identity changed")
                os.fchmod(stage_fd, 0o700)
                for name in os.listdir(stage_fd):
                    os.unlink(name, dir_fd=stage_fd)
                os.rmdir(stage_name, dir_fd=parent_fd)
            raise
        finally:
            os.close(stage_fd)
    return {"plan": str(output / PLAN), "planSha256": sha(plan_data),
            "configSha256": sha(before), "catalogSha256": sha(old_catalog),
            "afterConfigSha256": sha(after), "afterCatalogSha256": sha(repaired)}


def load_plan(path, expected):
    path = absolute(path)
    data = read(path)
    require(sha(data) == expected, "prepared plan SHA256 mismatch")
    plan = document(data)
    require(plan.get("schemaVersion") == 1 and plan.get("kind") == "astra-reasoning-repair" and
            path.name == PLAN and path.parent == absolute(plan["output"]), "invalid prepared plan binding")
    require(plan["sourceSha256"] == SOURCE_SHA256 and plan["fixtureSha256"] == FIXTURE_SHA256 and
            plan["efforts"] == list(EFFORTS) and plan["default"] == "low", "unexpected repair contract")
    catalog = absolute(plan["catalog"])
    output = path.parent
    require(catalog.name == "lain.models.json" and output.parent == catalog.parent.parent and
            output != catalog.parent and re.fullmatch(r"[0-9a-f]{32}", output.name), "invalid artifact binding")
    with directory(output) as fd:
        require(stat.S_IMODE(os.fstat(fd).st_mode) == 0o500, "prepared directory must be private and immutable")
        _, plan_info = checked_read(fd, PLAN)
        require(stat.S_IMODE(plan_info.st_mode) == 0o400, "prepared plan must be private and read-only")
        require(set(os.listdir(fd)) == set(plan["files"]) | {PLAN}, "prepared file inventory changed")
        for name, digest in plan["files"].items():
            require(Path(name).name == name, "invalid prepared filename")
            data, info = checked_read(fd, name)
            require(stat.S_IMODE(info.st_mode) == 0o400 and sha(data) == digest, "prepared file changed")
    require(sha(read(output / BEFORE)) == plan["beforeConfigSha256"] and
            sha(read(output / OLD_CATALOG)) == plan["beforeCatalogSha256"] and
            sha(read(output / AFTER)) == plan["afterConfigSha256"] and
            sha(read(output / catalog.name)) == plan["afterCatalogSha256"], "prepared backup binding mismatch")
    require(edit_config(read(output / BEFORE), catalog, output / catalog.name) == read(output / AFTER),
            "prepared config is outside the bounded repair")
    return plan


@contextmanager
def locked_config(config):
    config = absolute(config)
    with directory(config.parent) as fd:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        handle = os.open(config.name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=fd)
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
            data, info = checked_read(fd, config.name)
            require(identity(info) == identity(os.fstat(handle)), "config changed before lock")
            yield fd, data, info
        finally:
            os.close(handle)


def atomic_config(fd, config, before, info, after, validate):
    """Durable temporary file, final hash+inode CAS, then atomic replacement."""
    name = ".reasoning-config-" + os.urandom(16).hex()
    try:
        write_new(fd, name, after, stat.S_IMODE(info.st_mode))
        validate()
        current, current_info = checked_read(fd, config.name)
        require(sha(current) == sha(before) and identity(current_info) == identity(info),
                "config changed before commit; refusing replacement")
        with directory(config.parent) as live_fd:
            require(os.fstat(live_fd).st_ino == os.fstat(fd).st_ino and
                    os.fstat(live_fd).st_dev == os.fstat(fd).st_dev, "config directory changed")
        os.replace(name, config.name, src_dir_fd=fd, dst_dir_fd=fd)
        os.fsync(fd)
    finally:
        try:
            os.unlink(name, dir_fd=fd)
        except FileNotFoundError:
            pass


def apply(plan_path, expected_plan, expected_config, expected_catalog, receipt):
    plan_path, receipt = map(absolute, (plan_path, receipt))
    plan = load_plan(plan_path, expected_plan)
    require(expected_config == plan["beforeConfigSha256"] and expected_catalog == plan["beforeCatalogSha256"],
            "explicit expected input hashes do not match plan")
    config, catalog, output = map(absolute, (plan["config"], plan["catalog"], plan["output"]))
    require(not receipt.is_relative_to(catalog.parent) and not receipt.is_relative_to(output),
            "receipt must be outside source and prepared artifact generations")

    def validate():
        load_plan(plan_path, expected_plan)
        require(hashes(artifact_snapshot(catalog)) == plan["sourceArtifacts"], "source artifacts changed since preparation")

    with locked_config(config) as (fd, before, info):
        require(sha(before) == expected_config, "config changed since preparation")
        validate()
        # Receipt is intentionally conditional: it also permits recovery after a
        # crash following rename and never claims that a profile was installed.
        receipt_data = encoded({"schemaVersion": 1, "kind": "conditional-astra-rollback", "plan": str(plan_path),
                                "planSha256": expected_plan, "config": str(config),
                                "beforeConfigSha256": expected_config, "afterConfigSha256": plan["afterConfigSha256"],
                                "beforeCatalogSha256": expected_catalog})
        with directory(receipt.parent) as receipt_fd:
            write_new(receipt_fd, receipt.name, receipt_data)
            os.fsync(receipt_fd)
        atomic_config(fd, config, before, info, read(output / AFTER), validate)
    return {"applied": True, "receipt": str(receipt), "receiptSha256": sha(receipt_data),
            "configSha256": plan["afterConfigSha256"]}


def rollback(receipt_path, expected_receipt, expected_config):
    data = read(receipt_path)
    require(sha(data) == expected_receipt, "rollback receipt SHA256 mismatch")
    receipt = document(data)
    require(receipt.get("schemaVersion") == 1 and receipt.get("kind") == "conditional-astra-rollback",
            "invalid rollback receipt")
    plan = load_plan(receipt["plan"], receipt["planSha256"])
    require(receipt["config"] == plan["config"] and
            receipt["beforeConfigSha256"] == plan["beforeConfigSha256"] and
            receipt["beforeCatalogSha256"] == plan["beforeCatalogSha256"] and
            expected_config == receipt["afterConfigSha256"] == plan["afterConfigSha256"], "receipt binding mismatch")
    config = absolute(plan["config"])

    def validate():
        load_plan(receipt["plan"], receipt["planSha256"])
        require(sha(read(absolute(plan["catalog"]))) == plan["beforeCatalogSha256"],
                "original catalog changed or is unavailable; refusing rollback")

    with locked_config(config) as (fd, before, info):
        require(sha(before) == expected_config, "config changed since repair; refusing rollback")
        atomic_config(fd, config, before, info, read(Path(plan["output"]) / BEFORE), validate)
    return {"rolledBack": True, "configSha256": plan["beforeConfigSha256"]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--prepare", action="store_true")
    mode.add_argument("--apply", action="store_true")
    mode.add_argument("--rollback", action="store_true")
    for flag in ("config", "catalog", "output", "source-fixture", "models", "plan", "receipt",
                 "expect-plan-sha256", "expect-config-sha256", "expect-catalog-sha256", "expect-receipt-sha256"):
        parser.add_argument("--" + flag)
    args = vars(parser.parse_args(argv))
    required = (("config", "catalog", "output", "source_fixture", "models") if args["prepare"] else
                ("plan", "expect_plan_sha256", "expect_config_sha256", "expect_catalog_sha256", "receipt") if args["apply"] else
                ("receipt", "expect_receipt_sha256", "expect_config_sha256"))
    for key in required:
        if not args[key]:
            parser.error("required for selected mode: --" + key.replace("_", "-"))
    try:
        if args["prepare"]:
            result = prepare(*(args[key] for key in required))
        elif args["apply"]:
            result = apply(*(args[key] for key in required))
        else:
            result = rollback(*(args[key] for key in required))
        print(json.dumps(result, sort_keys=True))
        return 0
    except RepairError as error:
        # RepairError messages are fixed strings authored here, never input text.
        print("reasoning repair refused: " + str(error), file=sys.stderr)
        return 1
    except (OSError, KeyError, TypeError, AttributeError, StopIteration, UnicodeError, RecursionError, ValueError):
        # Never echo parser input, configuration contents, provider auth, or raw
        # exception values (which may contain source lines) to a terminal/log.
        print("reasoning repair refused: inputs, hashes, permissions, or bindings failed validation", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
