#!/usr/bin/env python3
"""Collect exact restored NuGet metadata and available notices without networking."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import subprocess
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("output", type=Path)
parser.add_argument("--prepared-adapter", type=Path, help="verified schema-2 prepared adapter for Go notices")
parser.add_argument("--without-core", action="store_true", help="monitor package: omit Go/Core inventory entirely")
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
output = args.output.resolve()
output.mkdir(parents=True, exist_ok=True)
packages = {}
folders = set()
for project in ("Lain.Cli", "Lain.Host", "Lain.Desktop"):
    data = json.loads((root / "src" / project / "obj/project.assets.json").read_text())
    folders.update(data["packageFolders"])
    for name, library in data["libraries"].items():
        if library["type"] == "package":
            packages.setdefault(name, set()).add(project)
    for framework in data["project"]["frameworks"].values():
        for download in framework.get("downloadDependencies", []):
            version = download["version"].strip("[]").split(",")[0].strip()
            packages.setdefault(download["name"] + "/" + version, set()).add(project)

inventory = []
for identity, projects in sorted(packages.items(), key=lambda item: item[0].lower()):
    name, version = identity.split("/", 1)
    package = next((Path(folder) / name.lower() / version.lower() for folder in sorted(folders)
                    if (Path(folder) / name.lower() / version.lower()).is_dir()), None)
    if package is None:
        raise SystemExit("Restored package missing from cache: " + identity)
    nuspec = next(package.glob("*.nuspec"), None)
    if nuspec is None:
        raise SystemExit("NuGet metadata missing: " + identity)
    tree = ET.parse(nuspec).getroot()
    metadata = {}
    for element in tree.iter():
        tag = element.tag.split("}")[-1]
        if tag in ("license", "licenseUrl", "projectUrl", "copyright", "authors", "repository"):
            metadata[tag] = {"value": element.text or "", **element.attrib}
    destination = output / "nuget" / name / version
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(nuspec, destination / nuspec.name)
    notices = []
    for path in sorted(package.rglob("*")):
        if path.is_file() and any(term in path.name.lower() for term in ("license", "licence", "notice", "copying", "copyright")):
            relative = path.relative_to(package)
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, target)
            notices.append({"path": str(target.relative_to(output)), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    inventory.append({"id": name, "version": version, "projects": sorted(projects),
                      "nugetSource": f"https://www.nuget.org/packages/{name}/{version}",
                      "metadata": metadata, "nuspec": str((destination / nuspec.name).relative_to(output)),
                      "availableNoticeFiles": notices})

# Pinned official source notices supplement packages that omit the license text.
source = root / "packaging/licenses"
sources = json.loads((source / "sources.json").read_text())
for entry in sources["files"]:
    if hashlib.sha256((source / entry["file"]).read_bytes()).hexdigest() != entry["sha256"]:
        raise SystemExit("Official notice integrity mismatch: " + entry["file"])
shutil.copytree(source, output / "official", dirs_exist_ok=True)
report = {"format": 1, "scope": "restored application graph including runtime downloads and conservative extra-platform dependencies",
          "reviewStatus": "inventory-and-source-notices-collected-not-a-legal-compliance-approval", "packages": inventory}
# Fonts are embedded Avalonia resources, so NuGet metadata cannot account for them.
font_inventory = []
font_root = root / "src/Lain.Desktop/Assets/Fonts"
for font in sorted(font_root.rglob("*.ttf")):
    license_path = font.parent / "OFL.txt"
    if not license_path.is_file():
        raise SystemExit("Bundled font missing OFL: " + str(font.relative_to(root)))
    destination = output / "fonts" / font.parent.name / "OFL.txt"
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(license_path, destination)
    font_inventory.append({"sourcePath": str(font.relative_to(root)),
                           "sha256": hashlib.sha256(font.read_bytes()).hexdigest(),
                           "license": "SIL Open Font License 1.1",
                           "notice": str(destination.relative_to(output)),
                           "noticeSha256": hashlib.sha256(license_path.read_bytes()).hexdigest()})
report["bundledFonts"] = font_inventory
(output / "inventory.json").write_text(json.dumps(report, indent=2) + "\n")
(output / "README.txt").write_text(
    "Redistribution notice inventory\n\n"
    "inventory.json records exact restored NuGet versions, original nuspec license expressions, "
    "URLs/copyright/repository metadata, and any LICENSE/NOTICE files present in the package cache. "
    "nuget/ preserves those original files and nuspecs. The restored graph conservatively includes "
    "some build-only/other-platform packages in addition to shipped runtime dependencies.\n\n"
    "official/ contains pinned upstream .NET runtime, ASP.NET Core, Avalonia, Semi.Avalonia, "
    "Ursa.Avalonia, Irihi.Avalonia.Shared and SQLitePCLRaw "
    "license/third-party notice texts with source URLs and SHA-256 in sources.json. "
    "Core's license is separately in proxy/LICENSE. This inventory is evidence for review, "
    "not a declaration that every possible redistribution obligation has been satisfied.\n")
with (output / "README.txt").open("a") as handle:
    handle.write("\nFonts: inventory.json bundledFonts lists embedded application font sources and hashes; "
                 "fonts/ preserves their original family-specific OFL texts and copyright notices. "
                 "The desktop publish also carries Assets/Fonts/<family>/OFL.txt.\n")
if not args.without_core:
    go_command = [sys.executable, str(root / "scripts/collect-go-notices.py"),
                  str(output.parent / "proxy/lain-proxy"), str(output / "go")]
    if args.prepared_adapter:
        go_command.extend(["--prepared-adapter", str(args.prepared_adapter),
                           "--source-manifest", str(output.parent / "proxy/source-manifest.json"),
                           "--build-manifest", str(output.parent / "proxy/build-manifest.json")])
    subprocess.run(go_command, check=True)
    with (output / "README.txt").open("a") as handle:
        handle.write("\nGo: go/inventory.json lists the actual binary's linked module versions/checksums, "
                     "source URLs, cached root LICENSE/COPYING/NOTICE files and the matching Go toolchain LICENSE. "
                     "Missing evidence is explicit; no modules are downloaded/upgraded. Nested or generated "
                     "third-party attribution obligations still require review.\n")
print(f"Collected metadata for {len(inventory)} exact NuGet/runtime packages and pinned official notices")
