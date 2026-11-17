import hashlib
import io
import json
import os
import stat
import sys
import tempfile
import unittest
import urllib.request
import zipfile
from http.server import ThreadingHTTPServer
from threading import Thread

import generated_outputs
import server


class ParseOutputsTests(unittest.TestCase):
    def test_rejects_bad_declarations(self):
        bad = [
            {"outputs": "x"},
            {"outputs": []},
            {"outputs": [{"fileName": "a"}, {"fileName": "a"}]},
            {"outputs": [{"fileName": n} for n in ("1", "2", "3", "4", "5")]},
            {"outputs": [{"fileName": "../a"}]},
            {"outputs": [{"fileName": "a/b"}]},
            {"outputs": [{"fileName": "a\\b"}]},
            {"outputs": [{"fileName": " a"}]},
            {"outputs": [{"fileName": "a "}]},
            {"outputs": [{"fileName": "a\x00b"}]},
            {"outputs": [{"fileName": ".."}]},
            {"outputs": [{"fileName": "x" * 181}]},
            {"outputs": [{"fileName": "a", "contentType": "text/csv; x=1"}]},
            {"outputs": [{"fileName": "a", "contentType": "not a type"}]},
            {"outputs": [{"fileName": "a", "extra": 1}]},
            {"outputs": [{"fileName": 3}]},
        ]
        for body in bad:
            self.assertIsNone(generated_outputs.parse_outputs(body), body)

    def test_accepts_and_normalizes(self):
        specs = generated_outputs.parse_outputs(
            {"outputs": [{"fileName": "r.csv", "contentType": "Text/CSV"},
                          {"fileName": "b.bin"}]}
        )
        self.assertEqual(
            specs,
            [{"fileName": "r.csv", "contentType": "text/csv"},
             {"fileName": "b.bin", "contentType": "application/octet-stream"}],
        )


class OutputPathSdkTests(unittest.TestCase):
    def test_output_path_requires_declaration(self):
        os.environ.pop("DELEGAT_OUTPUT_FILES", None)
        from delegat_sdk import client
        with self.assertRaises(ValueError):
            client.output_path("report.csv")

    def test_output_path_returns_relative(self):
        os.environ["DELEGAT_OUTPUT_FILES"] = json.dumps(["report.csv"])
        try:
            from delegat_sdk import client
            self.assertEqual(client.output_path("report.csv"), "outputs/report.csv")
            with self.assertRaises(ValueError):
                client.output_path("other.csv")
            with self.assertRaises(ValueError):
                client.output_path("../report.csv")
        finally:
            os.environ.pop("DELEGAT_OUTPUT_FILES", None)


class RunArtifactsJobTests(unittest.TestCase):
    """Direct _run_job_artifacts calls: real subprocess, real ZIP capture."""

    CSV_CODE = (
        "p = client.output_path('r.csv')\n"
        "with open(p, 'w', encoding='utf-8', newline='') as f:\n"
        "    f.write('a,b\\n1,2\\n')\n"
        "import os\n"
        "p2 = client.output_path('b.bin')\n"
        "with open(p2, 'wb') as f:\n"
        "    f.write(bytes(range(256)) * 4096)\n"
        "print('done')\n"
    )

    def test_zip_contains_manifest_and_verified_bytes(self):
        kind, fh, length = server._run_job_artifacts(
            {"code": self.CSV_CODE},
            [
                {"fileName": "r.csv", "contentType": "text/csv"},
                {"fileName": "b.bin", "contentType": "application/octet-stream"},
            ],
        )
        self.assertEqual(kind, "zip")
        try:
            data = fh.read()
            self.assertEqual(len(data), length)
        finally:
            fh.close()
        zf = zipfile.ZipFile(io.BytesIO(data))
        self.assertEqual(sorted(zf.namelist()), ["manifest.json", "outputs/0", "outputs/1"])
        manifest = json.loads(zf.read("manifest.json"))
        self.assertEqual(manifest["protocol"], 1)
        self.assertEqual(manifest["result"]["exitCode"], 0)
        self.assertIn("done", manifest["result"]["stdout"])
        self.assertEqual(len(manifest["outputs"]), 2)
        csv_bytes = zf.read("outputs/0")
        bin_bytes = zf.read("outputs/1")
        self.assertEqual(csv_bytes, b"a,b\n1,2\n")
        self.assertEqual(bin_bytes, bytes(range(256)) * 4096)
        self.assertEqual(
            manifest["outputs"][0]["sha256"], hashlib.sha256(csv_bytes).hexdigest())
        self.assertEqual(
            manifest["outputs"][1]["sha256"], hashlib.sha256(bin_bytes).hexdigest())
        self.assertEqual(manifest["outputs"][0]["length"], len(csv_bytes))
        self.assertEqual(manifest["outputs"][0]["fileName"], "r.csv")

    def test_nonzero_exit_returns_json_no_zip(self):
        kind, payload = server._run_job_artifacts(
            {"code": "import sys; sys.exit(3)"},
            [{"fileName": "r.csv", "contentType": "text/csv"}],
        )
        self.assertEqual(kind, "json")
        self.assertEqual(payload["exitCode"], 3)

    def test_missing_declared_output_is_capture_failure(self):
        kind, payload = server._run_job_artifacts(
            {"code": "print('no file written')"},
            [{"fileName": "r.csv", "contentType": "text/csv"}],
        )
        self.assertEqual(kind, "json")
        self.assertIn("output_capture_failed", payload["error"])
        self.assertNotEqual(payload["exitCode"], 0)

    def test_later_output_beyond_batch_budget_rejected(self):
        # Each file is well under MAX_FILE_BYTES but the second exceeds the
        # remaining batch budget: it must be refused at fstat time, before its
        # bytes are ever spooled into the archive.
        code = (
            "with open('outputs/a.csv', 'w') as f: f.write('x' * 8)\n"
            "with open('outputs/b.csv', 'w') as f: f.write('y' * 32)\n"
        )
        old = generated_outputs.MAX_BATCH_BYTES
        generated_outputs.MAX_BATCH_BYTES = 20
        try:
            kind, payload = server._run_job_artifacts(
                {"code": code},
                [
                    {"fileName": "a.csv", "contentType": "text/csv"},
                    {"fileName": "b.csv", "contentType": "text/csv"},
                ],
            )
        finally:
            generated_outputs.MAX_BATCH_BYTES = old
        self.assertEqual(kind, "json")
        self.assertIn("output_capture_failed", payload["error"])

    def test_undeclared_output_path_raises_in_child(self):
        kind, payload = server._run_job_artifacts(
            {"code": "client.output_path('evil.csv')\n"},
            [{"fileName": "r.csv", "contentType": "text/csv"}],
        )
        self.assertEqual(kind, "json")
        self.assertNotEqual(payload["exitCode"], 0)

    @unittest.skipUnless(os.name == "posix", "dir-fd capture hardening is POSIX-only")
    def test_symlink_output_rejected(self):
        kind, payload = server._run_job_artifacts(
            {"code": "import os\nos.symlink('/etc/passwd', 'outputs/r.csv')\n"},
            [{"fileName": "r.csv", "contentType": "text/csv"}],
        )
        self.assertEqual(kind, "json")
        self.assertIn("output_capture_failed", payload["error"])

    @unittest.skipUnless(os.name == "posix", "nlink check is POSIX-only")
    def test_hardlink_output_rejected(self):
        code = (
            "import os\n"
            "with open('real.csv', 'w') as f: f.write('x')\n"
            "os.link('real.csv', 'outputs/r.csv')\n"
        )
        kind, payload = server._run_job_artifacts(
            {"code": code},
            [{"fileName": "r.csv", "contentType": "text/csv"}],
        )
        self.assertEqual(kind, "json")
        self.assertIn("output_capture_failed", payload["error"])

    @unittest.skipUnless(os.name == "posix", "FIFO requires POSIX")
    def test_fifo_output_rejected(self):
        kind, payload = server._run_job_artifacts(
            {"code": "import os\nos.mkfifo('outputs/r.csv')\n"},
            [{"fileName": "r.csv", "contentType": "text/csv"}],
        )
        self.assertEqual(kind, "json")
        self.assertIn("output_capture_failed", payload["error"])

    def test_old_run_still_json(self):
        result = server._run_job({"code": "print('hi')"})
        self.assertEqual(result["exitCode"], 0)
        self.assertIn("hi", result["stdout"])


class RunArtifactsHttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._secret = server.SECRET
        cls.http = ThreadingHTTPServer(("127.0.0.1", 0), server.Handler)
        cls.port = cls.http.server_address[1]
        cls.thread = Thread(target=cls.http.serve_forever, daemon=True)
        cls.thread.start()

    @classmethod
    def tearDownClass(cls):
        server.SECRET = cls._secret
        cls.http.shutdown()
        cls.thread.join(timeout=5)

    def setUp(self):
        server.SECRET = "test-secret"

    def _post(self, body, secret=None):
        req = urllib.request.Request(
            f"http://127.0.0.1:{self.port}/run-artifacts",
            data=json.dumps(body).encode(),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        if secret is not None:
            req.add_header("X-Sandbox-Secret", secret)
        try:
            return urllib.request.urlopen(req, timeout=60), None
        except urllib.error.HTTPError as exc:
            return None, exc

    def _body(self):
        return {
            "code": "p = client.output_path('r.csv')\nwith open(p, 'w') as f: f.write('ok')",
            "artifactProtocol": 1,
            "outputs": [{"fileName": "r.csv", "contentType": "text/csv"}],
        }

    def test_requires_secret_even_when_unset(self):
        server.SECRET = ""
        resp, err = self._post(self._body())
        self.assertIsNone(resp)
        self.assertEqual(err.code, 403)

    def test_wrong_secret_rejected(self):
        resp, err = self._post(self._body(), secret="nope")
        self.assertIsNone(resp)
        self.assertEqual(err.code, 403)

    def test_protocol_required(self):
        body = self._body()
        del body["artifactProtocol"]
        resp, err = self._post(body, secret="test-secret")
        self.assertIsNone(resp)
        self.assertEqual(err.code, 400)

    def test_zip_response_streams_manifest(self):
        resp, err = self._post(self._body(), secret="test-secret")
        self.assertIsNone(err)
        self.assertEqual(resp.headers.get("Content-Type"), "application/zip")
        data = resp.read()
        zf = zipfile.ZipFile(io.BytesIO(data))
        manifest = json.loads(zf.read("manifest.json"))
        self.assertEqual(manifest["result"]["exitCode"], 0)
        self.assertEqual(zf.read("outputs/0"), b"ok")

    def test_health_advertises_protocol(self):
        resp = urllib.request.urlopen(
            f"http://127.0.0.1:{self.port}/health", timeout=5)
        health = json.loads(resp.read())
        self.assertTrue(health["ok"])
        self.assertEqual(health["artifactProtocol"], 1)


if __name__ == "__main__":
    unittest.main()
