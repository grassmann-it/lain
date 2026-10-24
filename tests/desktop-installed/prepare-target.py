#!/usr/bin/env python3
"""Copy an exact installed/staged desktop into a new isolated runtime; read-only inputs."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--app', required=True, type=Path)
parser.add_argument('--harness', required=True, type=Path, help='built harness directory')
parser.add_argument('--runtime', required=True, type=Path)
args = parser.parse_args()
app = args.app.resolve(strict=True)
desktop = app / 'desktop' if (app / 'desktop').is_dir() else app
harness = args.harness.resolve(strict=True)
runtime = args.runtime.resolve(strict=True)
target = runtime / 'target'
if target.exists():
    parser.error('runtime target must not exist')
if any(path.is_symlink() for path in desktop.rglob('*')):
    parser.error('desktop target contains symlinks; provide regular published files')
required = ['Lain.Desktop', 'Lain.Desktop.dll', 'Lain.Client.dll', 'Lain.Core.dll', 'Avalonia.Controls.dll']
for filename in required:
    if not (desktop / filename).is_file():
        parser.error('missing target file: ' + filename)
artwork = desktop / 'Assets/Brand/lain-cat.png'
expected_artwork = '6c9024b7220b6e015eeeb36aec9763fa5d804224b2b9e260caa7c4947c8436b1'
if not artwork.is_file() or hashlib.sha256(artwork.read_bytes()).hexdigest() != expected_artwork:
    parser.error('target must contain the approved contrast-adjusted cat photo artwork')
shutil.copytree(desktop, target)
# Overlay only harness entrypoint metadata, never a production dependency.
for suffix in ('.dll', '.deps.json', '.runtimeconfig.json'):
    path = harness / ('Lain.Desktop.Installed' + suffix)
    if not path.is_file():
        parser.error('build the harness first: missing ' + str(path))
    shutil.copy2(path, target / path.name)
records = {}
for name in ('Lain.Desktop', 'Lain.Client', 'Lain.Core', 'Avalonia.Controls'):
    source, copied = desktop / (name + '.dll'), target / (name + '.dll')
    expected = hashlib.sha256(source.read_bytes()).hexdigest()
    if hashlib.sha256(copied.read_bytes()).hexdigest() != expected:
        parser.error('target changed while copying: ' + name)
    records[name] = {'Source': str(source), 'Copy': str(copied), 'Sha256': expected}
(runtime / 'assemblies.json').write_text(json.dumps(records, indent=2) + '\n')
(runtime / 'target-selection.json').write_text(json.dumps({
    'sourceApp': str(app), 'sourceDesktop': str(desktop), 'artworkSha256': expected_artwork, 'artworkVariant': 'approved contrast-adjusted cat photo',
    'scope': 'Selected target copied read-only; runtime checks actual loaded assembly hashes and source hashes before and after GUI workflow.'
}, indent=2) + '\n')
