import os
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

    def test_second_job_is_busy(self):
        self.assertTrue(server._JOBS.acquire(blocking=False))
        try:
            result = server.run_job({"code": "print(1)"})
            self.assertEqual(result["error"], "sandbox_busy")
        finally:
            server._JOBS.release()

    def test_limits_apply_does_not_raise(self):
        sandbox_limits.apply()


if __name__ == "__main__":
    unittest.main()
