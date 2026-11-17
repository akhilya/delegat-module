import os
import subprocess
import sys
import unittest

import sandbox_limits
import server


class SandboxLimitTests(unittest.TestCase):
    def test_cpu_threads_clamped(self):
        os.environ["SANDBOX_CPU_THREADS"] = "99"
        self.assertEqual(server._cpu_threads(), "2")
        os.environ["SANDBOX_CPU_THREADS"] = "0"
        self.assertEqual(server._cpu_threads(), "1")
        os.environ.pop("SANDBOX_CPU_THREADS", None)
        self.assertEqual(server._cpu_threads(), "1")

    def test_as_bytes_clamped(self):
        os.environ["SANDBOX_AS_MB"] = "9999"
        self.assertEqual(server._as_bytes(), 512 * 1024 * 1024)
        os.environ["SANDBOX_AS_MB"] = "10"
        self.assertEqual(server._as_bytes(), 64 * 1024 * 1024)
        os.environ.pop("SANDBOX_AS_MB", None)

    def test_scrub_drops_secrets_and_pins_blas(self):
        os.environ["SANDBOX_CPU_THREADS"] = "1"
        env = server._scrub_env(
            {"PATH": "/bin", "MYSQL_PASSWORD": "x", "JWT_KEY": "k", "OMP_NUM_THREADS": "64"}
        )
        self.assertNotIn("MYSQL_PASSWORD", env)
        self.assertNotIn("JWT_KEY", env)
        self.assertEqual(env["OMP_NUM_THREADS"], "1")
        self.assertEqual(env["OPENBLAS_NUM_THREADS"], "1")
        os.environ.pop("SANDBOX_CPU_THREADS", None)

    def test_shops_are_loaded_for_the_script(self):
        result = server._run_job({
            "code": "print(shops[0]['hosts']['content'])",
            "shops": [{
                "id": 1,
                "adapter": "wildberries",
                "hosts": {"content": "https://content-api.wildberries.ru"},
                "credentials": {"apiKey": "do-not-print"},
            }],
        })
        self.assertEqual(result["exitCode"], 0, result.get("stderr"))
        self.assertIn("content-api.wildberries.ru", result["stdout"])

    def test_health_log_is_quiet(self):
        class Fake(server.Handler):
            def __init__(self):
                self.path = "/health"

            def address_string(self):
                return "test"

        class Buf:
            def write(self, _data):
                raise AssertionError("health must not log")

        original = sys.stderr
        sys.stderr = Buf()
        try:
            Fake().log_message("%s", "GET /health")
        finally:
            sys.stderr = original

    def test_second_job_is_busy(self):
        self.assertTrue(server._JOBS.acquire(blocking=False))
        try:
            result = server.run_job({"code": "print(1)"})
            self.assertEqual(result["error"], "sandbox_busy")
        finally:
            server._JOBS.release()

    def test_limits_apply_does_not_raise(self):
        # apply() sets RLIMIT_NPROC/AS on the caller — run it in a child so the
        # test process does not lose the ability to fork for later tests.
        out = subprocess.run(
            [sys.executable, "-c", "import sandbox_limits; sandbox_limits.apply()"],
            capture_output=True,
            timeout=15,
        )
        self.assertEqual(out.returncode, 0, out.stderr)


if __name__ == "__main__":
    unittest.main()
