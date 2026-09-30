"""Minimal HTTP runner for the Delegat Python sandbox. No pip, no DB URL.

Jobs cannot pin the host: one at a time, BLAS single-thread, Unix rlimits,
and Compose cgroup caps (0.50 CPU / 512 MiB, no extra swap).
"""

from __future__ import annotations

import json
import os
import signal
import subprocess
import sys
import tempfile
import threading
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SECRET = os.environ.get("SANDBOX_SECRET") or ""
MAX_BODY = 200_000
MAX_OUTPUT = 80_000
PORT = int(os.environ.get("SANDBOX_PORT") or "8080")

_JOBS = threading.BoundedSemaphore(1)
_THREAD_ENV = (
    "OMP_NUM_THREADS",
    "MKL_NUM_THREADS",
    "OPENBLAS_NUM_THREADS",
    "NUMEXPR_NUM_THREADS",
    "VECLIB_MAXIMUM_THREADS",
    "BLIS_NUM_THREADS",
)


def _json(handler: BaseHTTPRequestHandler, code: int, payload: dict) -> None:
    raw = json.dumps(payload).encode("utf-8")
    handler.send_response(code)
    handler.send_header("Content-Type", "application/json")
    handler.send_header("Content-Length", str(len(raw)))
    handler.end_headers()
    handler.wfile.write(raw)


def _int_env(name: str, default: int, lo: int, hi: int) -> int:
    raw = (os.environ.get(name) or "").strip()
    try:
        value = int(raw) if raw else default
    except ValueError:
        value = default
    return max(lo, min(hi, value))


def _cpu_threads() -> str:
    return str(_int_env("SANDBOX_CPU_THREADS", 1, 1, 2))


def _as_bytes() -> int:
    return _int_env("SANDBOX_AS_MB", 384, 64, 512) * 1024 * 1024


TIMEOUT_SEC = _int_env("SANDBOX_TIMEOUT_SEC", 30, 2, 120)
APP_DIR = os.path.dirname(os.path.abspath(__file__))


def _scrub_env(base: dict[str, str]) -> dict[str, str]:
    out = {}
    for key, value in base.items():
        upper = key.upper()
        if upper.startswith("MYSQL") or upper.startswith("MINIO") or "PASSWORD" in upper:
            continue
        if upper in {"JWT_KEY", "ENCRYPTION_KEY", "INSTANCE_LINK_SECRET", "ADMIN_PASSWORD"}:
            continue
        out[key] = value
    threads = _cpu_threads()
    for key in _THREAD_ENV:
        out[key] = threads
    return out


def _kill_job(proc: subprocess.Popen) -> None:
    if proc.poll() is not None:
        return
    try:
        if os.name == "posix":
            os.killpg(proc.pid, signal.SIGKILL)
        else:
            proc.kill()
    except OSError:
        try:
            proc.kill()
        except OSError:
            pass


def run_job(body: dict) -> dict:
    if not _JOBS.acquire(blocking=False):
        return {"exitCode": 1, "error": "sandbox_busy", "stdout": "", "stderr": ""}
    try:
        return _run_job(body)
    finally:
        _JOBS.release()


def _run_job(body: dict) -> dict:
    code = body.get("code") or body.get("script") or ""
    if not isinstance(code, str) or not code.strip():
        return {"exitCode": 2, "error": "code is required", "stdout": "", "stderr": ""}
    if len(code) > 80_000:
        return {"exitCode": 2, "error": "script too large", "stdout": "", "stderr": ""}

    store_id = str(body.get("storeId") or body.get("store_id") or "")
    base_url = str(body.get("baseUrl") or body.get("base_url") or "")
    token = str(body.get("token") or "")
    shops = body.get("shops")
    if isinstance(shops, str):
        shops_json = shops or "[]"
    elif shops is None:
        shops_json = "[]"
    else:
        shops_json = json.dumps(shops)

    env = _scrub_env(dict(os.environ))
    env["DELEGAT_BASE_URL"] = base_url
    env["DELEGAT_TOKEN"] = token
    env["DELEGAT_STORE_ID"] = store_id
    env["DELEGAT_SHOPS"] = shops_json
    env["PYTHONPATH"] = APP_DIR + os.pathsep + env.get("PYTHONPATH", "")
    env.pop("SANDBOX_SECRET", None)
    env["SANDBOX_TIMEOUT_SEC"] = str(TIMEOUT_SEC)
    env["SANDBOX_AS_MB"] = str(_as_bytes() // (1024 * 1024))
    env["SANDBOX_CPU_THREADS"] = _cpu_threads()

    posix = os.name == "posix"
    with tempfile.TemporaryDirectory(prefix="delegat-job-") as tmp:
        script_path = os.path.join(tmp, "job.py")
        with open(script_path, "w", encoding="utf-8") as fh:
            fh.write("import sandbox_limits\n")
            fh.write("sandbox_limits.apply()\n")
            fh.write("import json as _delegat_json, os as _delegat_os\n")
            fh.write("shops = _delegat_json.loads(_delegat_os.environ.get('DELEGAT_SHOPS') or '[]')\n")
            fh.write("from delegat_sdk import client\n")
            fh.write(code)
            if not code.endswith("\n"):
                fh.write("\n")
        proc = subprocess.Popen(
            [sys.executable, script_path],
            cwd=tmp,
            env=env,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            start_new_session=posix,
        )
        try:
            stdout, stderr = proc.communicate(timeout=TIMEOUT_SEC)
        except subprocess.TimeoutExpired:
            _kill_job(proc)
            try:
                stdout, stderr = proc.communicate(timeout=2)
            except subprocess.TimeoutExpired:
                _kill_job(proc)
                stdout, stderr = "", ""
            out = (stdout or "")[:MAX_OUTPUT]
            err = ((stderr or "") + "\ntimeout")[:MAX_OUTPUT]
            return {"exitCode": 124, "stdout": out, "stderr": err, "error": "timeout"}

    return {
        "exitCode": proc.returncode,
        "stdout": (stdout or "")[:MAX_OUTPUT],
        "stderr": (stderr or "")[:MAX_OUTPUT],
    }


def _read_chunked(stream, max_bytes: int) -> bytes | None:
    chunks: list[bytes] = []
    total = 0
    while True:
        line = stream.readline()
        if not line:
            return None
        size_token = line.split(b";", 1)[0].strip()
        try:
            size = int(size_token, 16)
        except ValueError:
            return None
        if size == 0:
            stream.readline()
            break
        total += size
        if total > max_bytes:
            return None
        chunks.append(stream.read(size))
        stream.read(2)
    return b"".join(chunks)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args) -> None:
        if self.path in ("/health", "/"):
            return
        sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))

    def do_GET(self) -> None:
        if self.path in ("/health", "/"):
            _json(self, 200, {"ok": True})
            return
        self.send_error(404)

    def do_POST(self) -> None:
        if self.path != "/run":
            self.send_error(404)
            return
        if SECRET:
            got = self.headers.get("X-Sandbox-Secret") or ""
            if got != SECRET:
                _json(self, 403, {"exitCode": 1, "error": "forbidden", "stdout": "", "stderr": ""})
                return
        try:
            length = int(self.headers.get("Content-Length") or "0")
        except ValueError:
            length = 0
        te = (self.headers.get("Transfer-Encoding") or "").lower()
        if "chunked" in te:
            raw = _read_chunked(self.rfile, MAX_BODY)
            if raw is None:
                _json(self, 413, {"exitCode": 1, "error": "invalid body", "stdout": "", "stderr": ""})
                return
        elif length <= 0 or length > MAX_BODY:
            _json(self, 413, {"exitCode": 1, "error": "invalid body", "stdout": "", "stderr": ""})
            return
        else:
            raw = self.rfile.read(length)
        try:
            body = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            _json(self, 400, {"exitCode": 1, "error": "invalid json", "stdout": "", "stderr": ""})
            return
        try:
            result = run_job(body if isinstance(body, dict) else {})
        except Exception as exc:
            result = {
                "exitCode": 1,
                "error": str(exc),
                "stdout": "",
                "stderr": traceback.format_exc()[-MAX_OUTPUT:],
            }
        code = 429 if result.get("error") == "sandbox_busy" else 200
        _json(self, code, result)


def main() -> None:
    threads = _cpu_threads()
    for key in _THREAD_ENV:
        os.environ.setdefault(key, threads)
    server = ThreadingHTTPServer(("0.0.0.0", PORT), Handler)
    sys.stderr.write(
        f"sandbox listening on {PORT} (threads={threads} as_mb={_as_bytes() // (1024 * 1024)} timeout={TIMEOUT_SEC}s)\n"
    )
    server.serve_forever()


if __name__ == "__main__":
    main()
