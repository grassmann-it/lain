#!/usr/bin/env python3
"""Bounded real ENOSPC on a private 1 MiB tmpfs; never fills the host filesystem.

The launcher creates fresh user/mount/network namespaces and enables only lo.
Requires the serialized runtime slot; uses only the explicitly supplied actual Core.
"""
import argparse
import errno
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).parent))
from process_probe import PrivateRun, ROOT, expect


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def probe(core):
    run = PrivateRun(core)
    mount = run.root / "bounded-journal-fs"
    mount.mkdir(mode=0o700)
    mounted = False
    try:
        subprocess.run(["mount", "-t", "tmpfs", "-o", "size=1m,mode=0700", "lain-synthetic-journal", str(mount)], check=True, capture_output=True)
        mounted = True
        capacity = os.statvfs(mount).f_blocks * os.statvfs(mount).f_frsize
        expect(0 < capacity <= 1024 * 1024, "test filesystem exceeds its 1 MiB bound")
        run.journal = mount / "events.jsonl"
        run.start()
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "synthetic seed failed")
        run.stop()
        # Align the synthetic final JSON record with harmless trailing whitespace
        # before its newline. Then an append needs a new block, making real
        # ENOSPC deterministic instead of consuming a previously allocated tail.
        data = run.journal.read_bytes()
        block = os.statvfs(mount).f_frsize
        padding = (-len(data)) % block
        with run.journal.open("wb") as output:
            output.write(data[:-1] + b" " * padding + b"\n")
            output.flush(); os.fsync(output.fileno())
        run.start()
        before = run.events()
        original = run.journal.read_bytes()
        original_hash = digest(run.journal)
        source = Path(str(run.journal) + ".source").read_bytes()
        calls = len(run.provider.snapshot())
        filler = mount / "owned-synthetic-filler"
        observed_errno = None
        descriptor = os.open(filler, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        try:
            for _ in range(32):
                try:
                    os.write(descriptor, b"f" * 65536)
                except OSError as error:
                    observed_errno = error.errno
                    break
        finally:
            os.close(descriptor)
        expect(observed_errno == errno.ENOSPC and os.statvfs(mount).f_bavail == 0, "fixture did not reach actual kernel ENOSPC")
        status, _, _ = run.infer("fixture-a", protocol="chat")
        expect(status == 503 and len(run.provider.snapshot()) == calls, "full journal allowed an unregistered upstream attempt")
        storage = run.get("/lain/journal/storage")
        expect(not storage["healthy"] and storage["filesystemAvailableBytes"] == 0, "actual disk exhaustion was not reported")
        expect(run.events() == before and run.journal.read_bytes() == original, "failed append changed confirmed history")
        filler.unlink()
        expect(os.statvfs(mount).f_bavail > 0, "owned filler removal did not restore capacity")
        expect(run.infer("fixture-a", protocol="chat")[0] == 503 and len(run.provider.snapshot()) == calls, "sticky journal failure reopened automatically")
        run.stop(); run.start()
        expect(run.events() == before and Path(str(run.journal) + ".source").read_bytes() == source, "restart reset journal identities/cursor")
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "original journal did not resume after explicit restart")
        recovered_batch = run.events()
        expect(run.journal.read_bytes().startswith(original), "post-restart append rewrote original history")
        run.stop()
        copy_directory = run.root / "recovered-copy"
        result = subprocess.run([core, "journal-recover-copy", "--journal", str(run.journal), "--destination", str(copy_directory)],
                                env=run.env, capture_output=True, check=True, timeout=8)
        inspection = json.loads(result.stdout)
        expect(inspection["uncertainTailBytes"] == 0 and inspection["sourceId"] == before["sourceId"], "unexpected recovered-copy identities")
        run.journal = copy_directory / "events.jsonl"
        run.start()
        expect(run.events() == recovered_batch, "recovered copy changed replay/source/cursor")
        return {"filesystem": "private_tmpfs", "capacityBytes": capacity, "actualErrno": observed_errno,
                "confirmedEventsBeforeFault": before["highWatermark"], "preFaultJournalSha256": original_hash,
                "failedDispatchStatus": status, "failedDispatchUpstreamCalls": 0, "stickyAfterSpaceFreed": True,
                "originalHistoryUnchanged": True, "sourceCursorPreservedAcrossRestart": True,
                "recoveredCopyReplayIdentical": True, "automaticTruncationOrDeletion": False}
    finally:
        run.stop()
        if mounted:
            subprocess.run(["umount", str(mount)], check=True, capture_output=True)
        run.__exit__()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--isolated-child", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    core = args.core.resolve(strict=True)
    report_path = args.report.absolute()
    if not args.isolated_child:
        environment = {name: os.environ[name] for name in ("PATH", "LANG") if name in os.environ}
        environment["LAIN_TMPFS_PARENT_MOUNT_NAMESPACE"] = os.readlink("/proc/self/ns/mnt")
        return subprocess.run(["unshare", "-Urnm", "sh", "-c", 'ip link set lo up && exec "$@"', "lain-tmpfs-probe",
                               sys.executable, str(Path(__file__).resolve()), "--core", str(core), "--report", str(report_path), "--isolated-child"], env=environment).returncode
    expect([name for _, name in socket.if_nameindex()] == ["lo"], "private loopback-only network required")
    parent_namespace = os.environ.get("LAIN_TMPFS_PARENT_MOUNT_NAMESPACE")
    expect(parent_namespace and os.readlink("/proc/self/ns/mnt") != parent_namespace, "refusing to mount in the caller's filesystem namespace")
    os.umask(0o077)
    core_hash = digest(core)
    qualification = json.loads((ROOT / "proxy/qualification.json").read_text())
    expect(qualification["sha256"] == core_hash and qualification["passed"], "explicit qualified Core required")
    try:
        evidence = probe(str(core))
        report = {"passed": True, "evidence": evidence}
    except Exception as error:
        report = {"passed": False, "error": type(error).__name__, "detail": str(error)}
    report.update(schemaVersion=1, actualCoreProcess=True, syntheticOnly=True,
                  actualKernelENOSPC=report.get("evidence", {}).get("actualErrno") == errno.ENOSPC,
                  coreSha256=core_hash, binaryUnchanged=digest(core) == core_hash,
                  coreSourceManifestSha256=qualification["sourceManifestSha256"],
                  networkIsolation="private_network_namespace_loopback_only", filesystemIsolation="private_mount_namespace_1MiB_tmpfs",
                  notQualified=["production filesystem/device failures", "full Host backup/restore transaction", "sustained growth"])
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n")
    report_path.with_suffix(report_path.suffix + ".sha256").write_text(digest(report_path) + "\n")
    print(json.dumps(report), flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
