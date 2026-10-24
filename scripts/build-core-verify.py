#!/usr/bin/env python3
"""Verify the adapter, external upstream lock, patches, and toolchain inputs."""
from pathlib import Path
import subprocess
import sys
subprocess.run([sys.executable, str(Path(__file__).with_name("prepare-core.py")), "--verify-only"], check=True)
