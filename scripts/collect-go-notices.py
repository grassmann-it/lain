#!/usr/bin/env python3
"""Inventory linked Go modules from the actual binary and copy cached root notices."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

binary, output = Path(sys.argv[1]).resolve(strict=True), Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
go = os.environ.get("LAIN_GO") or shutil.which("go") or str(Path.home() / ".local/share/model-usage/toolchain/go/bin/go")
env = dict(os.environ, GOTOOLCHAIN="local")
metadata = subprocess.check_output([go, "version", "-m", str(binary)], text=True, env=env)
settings = json.loads(subprocess.check_output([go, "env", "-json", "GOMODCACHE", "GOROOT", "GOVERSION"], text=True, env=env))
compiled_version = metadata.splitlines()[0].rsplit(": ", 1)[-1]
if settings["GOVERSION"] != compiled_version:
    raise SystemExit("Go notice toolchain must match compiled version: " + compiled_version)
(output / "binary-build-metadata.txt").write_text("lain-proxy: " + compiled_version + "\n" + "\n".join(metadata.splitlines()[1:]) + "\n")
modules = []
for line in metadata.splitlines()[1:]:
    fields = line.split()
    if fields and fields[0] == "dep":
        modules.append({"module": fields[1], "version": fields[2], "goSum": fields[3] if len(fields) > 3 else None})
    elif fields and fields[0] == "=>":
        if not modules:
            raise SystemExit("Unexpected replacement without dependency")
        modules[-1]["replacement"] = {"module": fields[1], "version": fields[2] if len(fields) > 2 else None,
                                      "goSum": fields[3] if len(fields) > 3 else None}


def escape(value):
    # Go module cache encodes every uppercase ASCII character as !lowercase.
    return re.sub(r"[A-Z]", lambda match: "!" + match.group().lower(), value)


def copy_notices(source, destination):
    records = []
    if not source.is_dir():
        return records
    for path in sorted(source.iterdir()):
        if path.is_file() and any(path.name.upper().startswith(prefix) for prefix in ("LICENSE", "LICENCE", "COPYING", "NOTICE", "COPYRIGHT", "PATENTS")):
            destination.mkdir(parents=True, exist_ok=True)
            target = destination / path.name
            shutil.copyfile(path, target)
            records.append({"path": str(target.relative_to(output)), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    return records


missing = []
for module in modules:
    actual = module.get("replacement", module)
    name, version = actual["module"], actual["version"]
    module["sourceUrl"] = f"https://pkg.go.dev/{name}@{version}"
    cache_relative = escape(name) + "@" + escape(version or "")
    # Local replacements are deliberately not guessed from another checkout.
    cache = Path(settings["GOMODCACHE"]) / cache_relative
    if not version or not version.startswith("v"):
        module["noticeFiles"] = []
        module["evidenceState"] = "unresolved-local-replacement"
        missing.append({"module": name, "version": version, "reason": module["evidenceState"]})
        continue
    module["noticeFiles"] = copy_notices(cache, output / "modules" / cache_relative)
    info = Path(settings["GOMODCACHE"]) / "cache/download" / escape(name) / "@v" / (escape(version) + ".info")
    if info.is_file():
        origin = json.loads(info.read_text()).get("Origin")
        if origin:
            module["cachedSourceOrigin"] = origin
    module["evidenceState"] = "cached-root-notices-collected" if module["noticeFiles"] else "root-license-evidence-missing"
    if not module["noticeFiles"]:
        missing.append({"module": name, "version": version, "reason": module["evidenceState"]})
toolchain = {"version": compiled_version, "sourceUrl": f"https://go.googlesource.com/go/+/refs/tags/{compiled_version}/LICENSE",
             "noticeFiles": copy_notices(Path(settings["GOROOT"]), output / "toolchain" / compiled_version)}
if not any(Path(entry["path"]).name == "LICENSE" for entry in toolchain["noticeFiles"]):
    missing.append({"toolchain": compiled_version, "reason": "Go toolchain LICENSE missing"})
report = {"format": 1, "binarySha256": hashlib.sha256(binary.read_bytes()).hexdigest(),
          "scope": "exact Go dependencies reported by go version -m for the shipped binary; root cached notices only",
          "reviewStatus": "evidence-collected-not-a-legal-compliance-approval",
          "toolchain": toolchain, "modules": modules, "missingEvidence": missing}
(output / "inventory.json").write_text(json.dumps(report, indent=2) + "\n")
print(f"Collected {len(modules)} linked Go module inventories; missing evidence: {len(missing)}")
