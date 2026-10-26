#!/usr/bin/env python3
"""Install only the pinned official firectl artifact into a private lain directory. Never executes it."""
import argparse
import gzip
import hashlib
import io
import os
from pathlib import Path
import tempfile
import urllib.request

URL = 'https://storage.googleapis.com/fireworks-public/firectl/stable/linux-amd64.gz'
ARCHIVE_SHA256 = 'f6fac5dc456b526d9378af9d5240aa92f98c927a9ba2ca8a00fd5f41938c0353'
BINARY_SHA256 = 'dfc6f83e3af156b44a3d27237b192efae431c00fce9e56e62b8c71ea5cf8802f'


def install(directory: Path):
    os.umask(0o077)
    if not directory.is_absolute():
        raise ValueError('Use an absolute private installation directory.')
    for part in (directory, *directory.parents):
        if part.is_symlink():
            raise ValueError('Installation paths must not contain symlinks.')
    directory.mkdir(parents=True, mode=0o700, exist_ok=True)
    if directory.stat().st_mode & 0o077:
        raise ValueError('Installation directory must already be private (0700).')
    destination = directory / 'firectl-1.8.19'
    if destination.exists() or destination.is_symlink():
        if destination.is_file() and not destination.is_symlink() and hashlib.sha256(destination.read_bytes()).hexdigest() == BINARY_SHA256:
            return destination
        raise ValueError('Existing installation differs; nothing was overwritten.')
    # stable is mutable: a vendor update fails the hash check and requires a new reviewed pin.
    with urllib.request.urlopen(URL, timeout=60) as response:
        archive = response.read(32 * 1024 * 1024 + 1)
    if len(archive) > 32 * 1024 * 1024 or hashlib.sha256(archive).hexdigest() != ARCHIVE_SHA256:
        raise ValueError('Official archive differs from pinned publication.')
    with gzip.GzipFile(fileobj=io.BytesIO(archive)) as compressed:
        binary = compressed.read(128 * 1024 * 1024 + 1)
    if len(binary) > 128 * 1024 * 1024 or hashlib.sha256(binary).hexdigest() != BINARY_SHA256:
        raise ValueError('Official executable differs from pinned publication.')
    descriptor, temp = tempfile.mkstemp(prefix='.firectl-', dir=directory)
    try:
        with os.fdopen(descriptor, 'wb') as stream:
            stream.write(binary)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temp, 0o700)
        os.link(temp, destination)  # exclusive publication, never replace another file
    finally:
        os.unlink(temp)
    return destination


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--directory', type=Path, required=True)
    args = parser.parse_args()
    print(install(args.directory))
