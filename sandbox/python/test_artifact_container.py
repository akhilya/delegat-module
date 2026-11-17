"""Container-contract proof for the generated-artifact sandbox.

Runs ONLY inside the constrained sandbox image (uid 1000, read-only root,
192 MiB tmpfs /tmp, cgroup CPU/RAM/pids caps). Not part of the default local
unittest command; it is invoked explicitly inside a container started from
docker-compose.yml or docker-compose.prod.yml.
"""

import hashlib
import json
import os
import unittest
import zipfile

import server


def _mounts():
    try:
        with open("/proc/mounts") as fh:
            return fh.read()
    except OSError:
        return ""


def _mount_entry(path: str):
    for line in _mounts().splitlines():
        parts = line.split()
        if len(parts) >= 3 and parts[1] == path:
            return parts
    return None


def _read_cgroup(path: str) -> str:
    with open(path) as fh:
        return fh.read().strip()


class ContainerContractTests(unittest.TestCase):
    """Asserts the sandbox actually runs with its documented constraints."""

    def test_unprivileged_user(self):
        self.assertEqual(os.getuid(), 1000)

    def test_root_filesystem_read_only(self):
        root = _mount_entry("/")
        self.assertIsNotNone(root, "/ mount entry missing")
        self.assertIn("ro", root[3].split(","))
        self.assertTrue(os.statvfs("/").f_flag & os.ST_RDONLY)

    def test_tmp_scratch_is_bounded_tmpfs(self):
        mount = _mount_entry("/tmp")
        self.assertIsNotNone(mount, "/tmp mount entry missing")
        self.assertEqual(mount[2], "tmpfs")
        st = os.statvfs("/tmp")
        self.assertEqual(st.f_blocks * st.f_frsize, 192 * 1024 * 1024)

    def test_cgroup_limits(self):
        cgroup = "/sys/fs/cgroup"
        mib = 1024 * 1024
        if os.path.exists(os.path.join(cgroup, "memory.max")):
            self.assertEqual(int(_read_cgroup(f"{cgroup}/memory.max")), 512 * mib)
            self.assertEqual(int(_read_cgroup(f"{cgroup}/memory.swap.max")), 0)
            self.assertEqual(int(_read_cgroup(f"{cgroup}/pids.max")), 64)
            quota, period = _read_cgroup(f"{cgroup}/cpu.max").split()
            self.assertEqual(int(quota) * 2, int(period))
        else:
            # cgroup v1 equivalents.
            self.assertEqual(
                int(_read_cgroup(f"{cgroup}/memory/memory.limit_in_bytes")),
                512 * mib)
            self.assertEqual(
                int(_read_cgroup(
                    f"{cgroup}/memory/memory.memsw.limit_in_bytes")),
                512 * mib)
            self.assertEqual(
                int(_read_cgroup(f"{cgroup}/pids/pids.max")), 64)
            quota = int(_read_cgroup(f"{cgroup}/cpu/cpu.cfs_quota_us"))
            period = int(_read_cgroup(f"{cgroup}/cpu/cpu.cfs_period_us"))
            self.assertEqual(quota * 2, period)


class ConstrainedCaptureTests(unittest.TestCase):
    """Largest supported batches inside the real limits: subprocess + ZIP_STORED."""

    CODE_TEMPLATE = (
        "chunk = b'x' * (1024 * 1024)\n"
        "for name in ('a.bin', 'b.bin'):\n"
        "    with open(client.output_path(name), 'wb') as output:\n"
        "        for _ in range(%d):\n"
        "            output.write(chunk)\n"
    )

    def _capture(self, mib_per_file: int):
        code = self.CODE_TEMPLATE % mib_per_file
        specs = [
            {"fileName": "a.bin", "contentType": "application/octet-stream"},
            {"fileName": "b.bin", "contentType": "application/octet-stream"},
        ]
        kind, fh, length = server._run_job_artifacts({"code": code}, specs)
        self.assertEqual(kind, "zip")
        expected_total = 2 * mib_per_file * 1024 * 1024
        seen = 0
        try:
            fh.seek(0)
            zf = zipfile.ZipFile(fh)
            self.assertEqual(sorted(zf.namelist()),
                             ["manifest.json", "outputs/0", "outputs/1"])
            manifest = json.loads(zf.read("manifest.json"))
            self.assertEqual(manifest["result"]["exitCode"], 0)
            names = [m["fileName"] for m in manifest["outputs"]]
            self.assertEqual(names, ["a.bin", "b.bin"])
            for index in range(2):
                meta = manifest["outputs"][index]
                self.assertEqual(meta["length"], mib_per_file * 1024 * 1024)
                sha = hashlib.sha256()
                with zf.open(f"outputs/{index}") as src:
                    while True:
                        block = src.read(1024 * 1024)
                        if not block:
                            break
                        sha.update(block)
                        seen += len(block)
                self.assertEqual(sha.hexdigest(), meta["sha256"])
            self.assertEqual(seen, expected_total)
        finally:
            fh.close()

    def test_two_20mib_outputs_capture(self):
        self._capture(20)

    def test_two_32mib_outputs_capture(self):
        self._capture(32)


if __name__ == "__main__":
    unittest.main()
