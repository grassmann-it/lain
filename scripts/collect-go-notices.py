#!/usr/bin/env python3
"""Collect notices for the exact linked Go modules; never download dependencies.

Schema-2 builds use --prepared-adapter from prepare-core.py. Only the pinned
upstream dependency may resolve to a private local replacement. Its original
license text is checked against the source manifest instead of being omitted.
Module archives are h1-verified against binary metadata and notices are recursive.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import zipfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def escape(value):
    return re.sub(r"[A-Z]", lambda match: "!" + match.group().lower(), value)


def json_stream(value):
    decoder = json.JSONDecoder()
    while value.strip():
        value = value.lstrip()
        item, end = decoder.raw_decode(value)
        yield item
        value = value[end:]


def notice_name(name):
    return any(Path(name).name.upper().startswith(prefix) for prefix in
               ("LICENSE", "LICENCE", "COPYING", "NOTICE", "COPYRIGHT", "PATENTS", "AUTHORS"))


def collect_module_archive(archive, module, version, expected_sum, output, destination):
    """Read original notices recursively from the h1-verified module archive.

    Go's dirhash.Hash1 hashes sorted archive names and each file's SHA-256;
    using the archive avoids attributing edited extracted cache files to upstream.
    Conservative: notice-bearing test and optional subpackages are retained too.
    """
    records = []
    archive_bytes = archive.read_bytes()
    import io
    with zipfile.ZipFile(io.BytesIO(archive_bytes)) as package:
        entries = [entry for entry in package.infolist() if not entry.is_dir()]
        names = [entry.filename for entry in entries]
        require(len(names) == len(set(names)), "Duplicate Go archive paths")
        prefix = module + "@" + version + "/"
        hasher = hashlib.sha256()
        notice_bytes = {}
        for name in sorted(names):
            require(name.startswith(prefix), "Go archive module prefix mismatch: " + name)
            relative = name[len(prefix):]
            require(relative and not any(part in ("", ".", "..") for part in relative.split("/"))
                    and "\\" not in relative and "\n" not in name,
                    "Unsafe Go archive path: " + name)
            data = package.read(name)
            hasher.update((hashlib.sha256(data).hexdigest() + "  " + name + "\n").encode())
            if notice_name(relative):
                notice_bytes[relative] = data
        actual_sum = "h1:" + base64.b64encode(hasher.digest()).decode()
        require(expected_sum and actual_sum == expected_sum,
                "Go module archive differs from binary checksum: " + module)
        for relative, data in notice_bytes.items():
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            records.append({"path": str(target.relative_to(output)),
                            "sourcePath": relative, "sha256": hashlib.sha256(data).hexdigest()})
    return records, {"sha256": hashlib.sha256(archive_bytes).hexdigest(), "goSum": actual_sum,
                     "noticeScope": "all nested notice files in exact module archive; conservative subpackage coverage"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--prepared-adapter", type=Path)
    parser.add_argument("--source-manifest", type=Path)
    parser.add_argument("--build-manifest", type=Path)
    args = parser.parse_args()
    require(all((args.prepared_adapter, args.source_manifest, args.build_manifest)) or
            not any((args.prepared_adapter, args.source_manifest, args.build_manifest)),
            "Prepared adapter, source manifest and build manifest must be supplied together")
    binary, output = args.binary.resolve(strict=True), args.output.resolve()
    require(not output.exists(), "Go notice output must not exist (prevents stale evidence)")
    output.mkdir(parents=True)
    go = os.environ.get("LAIN_GO") or shutil.which("go") or str(Path.home() / ".local/share/model-usage/toolchain/go/bin/go")
    env = dict(os.environ, GOTOOLCHAIN="local", GOWORK="off", GOFLAGS="-mod=readonly", GOPROXY="off", GOSUMDB="off")
    metadata = subprocess.check_output([go, "version", "-m", str(binary)], text=True, env=env)
    settings = json.loads(subprocess.check_output([go, "env", "-json", "GOMODCACHE", "GOROOT", "GOVERSION"], text=True, env=env))
    compiled_version = metadata.splitlines()[0].rsplit(": ", 1)[-1]
    require(settings["GOVERSION"] == compiled_version, "Go notice toolchain must match compiled version: " + compiled_version)
    (output / "binary-build-metadata.txt").write_text("lain-proxy: " + compiled_version + "\n" + "\n".join(metadata.splitlines()[1:]) + "\n")
    modules = []
    for line in metadata.splitlines()[1:]:
        fields = line.split()
        if fields and fields[0] == "dep":
            modules.append({"module": fields[1], "version": fields[2], "goSum": fields[3] if len(fields) > 3 else None})
        elif fields and fields[0] == "=>":
            require(modules, "Unexpected replacement without dependency")
            modules[-1]["replacement"] = {"module": fields[1], "version": fields[2] if len(fields) > 2 else None,
                                          "goSum": fields[3] if len(fields) > 3 else None}

    upstream, graph, source_manifest = None, {}, None
    if args.prepared_adapter:
        source_manifest = json.loads(args.source_manifest.read_text())
        build = json.loads(args.build_manifest.read_text())
        require(source_manifest.get("schemaVersion") == 2, "External upstream requires source-manifest schema 2")
        upstream = source_manifest["upstreamDependency"]
        require(build.get("binarySha256") == digest(binary) and build.get("sourceManifestSha256") == digest(args.source_manifest),
                "Go notices require build evidence for this exact binary and source manifest")
        for key in ("Version", "Commit", "ArchiveSha256"):
            require(build.get("upstream" + key) == upstream[key[0].lower() + key[1:]], "Upstream build evidence mismatch: " + key)
        require(build.get("patchSetSha256") == source_manifest["patchSetSha256"], "Patch-set build evidence mismatch")
        adapter = args.prepared_adapter.resolve(strict=True)
        prepared_path = adapter.parent / "prepared-manifest.json"
        prepared = json.loads(prepared_path.read_text())
        require(prepared.get("sourceManifestSha256") == digest(args.source_manifest), "Prepared source manifest mismatch")
        for key in ("upstreamVersion", "upstreamCommit", "upstreamArchiveSha256", "patchSetSha256"):
            require(prepared.get(key) == build.get(key), "Prepared source provenance mismatch: " + key)
        def prepared_inventory():
            result = {}
            for name in ("upstream", "adapter"):
                for path in sorted((adapter.parent / name).rglob("*")):
                    require(not path.is_symlink(), "Prepared source symlink is unsupported")
                    if path.is_file():
                        result[str(path.relative_to(adapter.parent))] = digest(path)
            return result
        require(prepared_inventory() == prepared["files"], "Prepared source tree changed before notice collection")
        # Inspect the modules actually linked into this binary. Listing "all"
        # would require uncached metadata for unrelated optional/test modules
        # even though none contributes bytes or notices to the shipped artifact.
        linked_modules = [module["module"] for module in modules]
        require(linked_modules, "Binary has no linked module evidence")
        raw_graph = subprocess.check_output([go, "list", "-mod=readonly", "-m", "-json", *linked_modules], cwd=adapter, text=True, env=env)
        graph = {entry["Path"]: entry for entry in json_stream(raw_graph)}
        # Record stable evidence without ephemeral build paths.
        def stable(entry):
            value = {key: item for key, item in entry.items() if key not in ("Dir", "GoMod")}
            if "Replace" in value:
                value["Replace"] = stable(value["Replace"])
            return value
        (output / "prepared-module-graph.json").write_text(json.dumps([stable(entry) for entry in graph.values()], indent=2) + "\n")
        require(prepared_inventory() == prepared["files"], "Readonly module inspection changed prepared source")

    def copy_notices(source, destination):
        records = []
        if not source.is_dir():
            return records
        for path in sorted(source.rglob("*")):
            require(not path.is_symlink(), "Notice source symlink is unsupported: " + str(path))
            if path.is_file() and notice_name(path.name):
                destination.mkdir(parents=True, exist_ok=True)
                target = destination / path.relative_to(source)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, target)
                records.append({"path": str(target.relative_to(output)), "sha256": digest(path)})
        return records

    missing = []
    for module in modules:
        entry = graph.get(module["module"])
        if graph:
            require(entry and entry.get("Version") == module["version"], "Prepared module graph differs from binary: " + module["module"])
            if module.get("goSum"):
                require(entry.get("Sum") == module["goSum"], "Prepared module checksum differs from binary: " + module["module"])
        actual = module.get("replacement", module)
        name, version = actual["module"], actual["version"]
        if not version or not version.startswith("v"):
            if upstream and module["module"] == upstream["module"]:
                require(module["version"] == upstream["version"] and name == "../upstream", "Linked upstream version/replacement differs from source lock")
                replacement = entry.get("Replace", {})
                source = Path(replacement.get("Dir", "")).resolve(strict=True)
                require(source == adapter.parent / "upstream" and replacement.get("Path") == "../upstream",
                        "Pinned upstream must use prepared sibling replacement")
                require(digest(source / "LICENSE") == source_manifest["licenseSha256"], "Prepared upstream license differs from source manifest")
                module["sourceUrl"] = upstream.get("repository", "https://github.com/router-for-me/CLIProxyAPI") + "/tree/" + upstream["commit"]
                module["noticeFiles"] = copy_notices(source, output / "upstream")
                module["evidenceState"] = "verified-external-upstream-recursive-notices-collected"
                module["upstreamDependency"] = upstream
                module["upstreamSourceModified"] = build["upstreamSourceModified"]
                module["patchSetSha256"] = build["patchSetSha256"]
                require(any(Path(item["path"]).name == "LICENSE" for item in module["noticeFiles"]), "Pinned upstream LICENSE missing")
                continue
            module["noticeFiles"] = []
            module["evidenceState"] = "unresolved-local-replacement"
            missing.append({"module": name, "version": version, "reason": module["evidenceState"]})
            continue
        module["sourceUrl"] = f"https://pkg.go.dev/{name}@{version}"
        cache_relative = escape(name) + "@" + escape(version)
        if entry:
            resolved = entry.get("Replace", entry)
            require(resolved.get("Path") == name and resolved.get("Version") == version,
                    "Prepared module replacement differs from binary: " + module["module"])
            if actual.get("goSum"):
                require(resolved.get("Sum") == actual["goSum"], "Replacement checksum differs from binary")
        archive = Path(settings["GOMODCACHE"]) / "cache/download" / escape(name) / "@v" / (escape(version) + ".zip")
        require(archive.is_file(), "Exact Go module archive missing from cache: " + name + "@" + version)
        module["noticeFiles"], module["sourceArchive"] = collect_module_archive(
            archive, name, version, actual.get("goSum"), output, output / "modules" / cache_relative)
        info = Path(settings["GOMODCACHE"]) / "cache/download" / escape(name) / "@v" / (escape(version) + ".info")
        if info.is_file():
            origin = json.loads(info.read_text()).get("Origin")
            if origin:
                module["cachedSourceOrigin"] = origin
        module["evidenceState"] = "verified-module-archive-recursive-notices-collected" if module["noticeFiles"] else "module-license-evidence-missing"
        if not module["noticeFiles"]:
            missing.append({"module": name, "version": version, "reason": module["evidenceState"]})
    if upstream:
        require(prepared_inventory() == prepared["files"], "Prepared source changed during notice collection")
        require(any(item["module"] == upstream["module"] and item.get("evidenceState") == "verified-external-upstream-recursive-notices-collected" for item in modules),
                "Binary does not contain the pinned external upstream replacement")
    toolchain = {"version": compiled_version, "sourceUrl": f"https://go.googlesource.com/go/+/refs/tags/{compiled_version}/LICENSE",
                 "noticeFiles": copy_notices(Path(settings["GOROOT"]), output / "toolchain" / compiled_version)}
    if not any(Path(entry["path"]).name == "LICENSE" for entry in toolchain["noticeFiles"]):
        missing.append({"toolchain": compiled_version, "reason": "Go toolchain LICENSE missing"})
    report = {"format": 2, "binarySha256": digest(binary),
              "scope": "Exact linked Go modules from the shipped binary; h1-verified recursive module archive notices and explicitly pinned prepared upstream notices",
              "reviewStatus": "evidence-collected-not-a-legal-compliance-approval",
              "toolchain": toolchain, "modules": modules, "missingEvidence": missing}
    if source_manifest:
        report.update(sourceManifestSha256=digest(args.source_manifest), upstreamDependency=upstream,
                      upstreamSourceModified=build["upstreamSourceModified"], patchSetSha256=build["patchSetSha256"])
    (output / "inventory.json").write_text(json.dumps(report, indent=2) + "\n")
    (output / "README.txt").write_text(
        "Go notice evidence for the exact binary identified by inventory.json.\n"
        "modules/ contains recursive notices from h1-verified archives of linked versioned modules.\n" +
        ("upstream/ contains explicitly pinned CLIProxyAPI recursive notices from the verified private\n"
         "prepared source tree. Its modification flag/patchset is recorded in inventory.json;\n"
         "the original upstream license is preserved unchanged.\n" if upstream else
         "Local replacement notice sources are unresolved unless explicitly recorded in inventory.json.\n") +
        "toolchain/ contains the matching\n"
        "Go compiler's recursive source notices. Missing evidence is explicit. Generated/file-header attribution\n"
        "obligations still require review. This inventory does not provide legal approval.\n")
    print(f"Collected {len(modules)} linked Go module inventories; missing evidence: {len(missing)}")


if __name__ == "__main__":
    main()
