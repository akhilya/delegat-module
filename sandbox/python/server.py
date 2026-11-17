"""Minimal HTTP runner for the Delegat Python sandbox. No pip, no DB URL.

Jobs cannot pin the host: one at a time, BLAS single-thread, Unix rlimits,
and Compose cgroup caps (0.50 CPU / 512 MiB, no extra swap).
"""

from __future__ import annotations

import hashlib
import json
import os
import signal
import stat
import subprocess
import sys
import tempfile
import threading
import traceback
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import generated_outputs

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


class _CaptureError(Exception):
    """A declared output failed capture validation; the whole batch fails."""


def _job_env(body: dict, output_names: list[str] | None) -> dict[str, str] | None:
    """Shared env for /run and /run-artifacts; None on invalid input."""
    code = body.get("code") or body.get("script") or ""
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
    if output_names is not None:
        env["DELEGAT_OUTPUT_FILES"] = json.dumps(output_names)
    env["PYTHONPATH"] = APP_DIR + os.pathsep + env.get("PYTHONPATH", "")
    env.pop("SANDBOX_SECRET", None)
    env["SANDBOX_TIMEOUT_SEC"] = str(TIMEOUT_SEC)
    env["SANDBOX_AS_MB"] = str(_as_bytes() // (1024 * 1024))
    env["SANDBOX_CPU_THREADS"] = _cpu_threads()
    return env


def _write_job_script(tmp: str, code: str) -> str:
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
    return script_path


def _bounded(stdout: str | None, stderr: str | None) -> tuple[str, str]:
    return (stdout or "")[:MAX_OUTPUT], (stderr or "")[:MAX_OUTPUT]


def _reap_group(proc: subprocess.Popen) -> None:
    """Kill leftover process-group descendants even if the leader exited."""
    if os.name != "posix":
        return
    try:
        os.killpg(proc.pid, signal.SIGKILL)
    except (ProcessLookupError, PermissionError, OSError):
        pass


def _capture_output(
    dir_fd, out_dir: str, spec: dict, zf: zipfile.ZipFile, entry: str, remaining: int
) -> tuple[int, str]:
    """Stream one declared basename into the ZIP entry; returns (length, sha256).

    ``remaining`` is the unconsumed batch budget: the entry must fit within it
    at fstat time and while streaming, so an oversized later file cannot spool
    past the aggregate cap before being rejected. On POSIX the file is opened
    relative to the pinned outputs-dir fd with O_NOFOLLOW|O_NONBLOCK so a child
    cannot swap the name for a symlink/FIFO.
    """
    name = spec["fileName"]
    if name == "job.py":
        raise _CaptureError("reserved name")
    try:
        if dir_fd is not None and hasattr(os, "O_NOFOLLOW"):
            flags = os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK
            fd = os.open(name, flags, dir_fd=dir_fd)
        else:
            fd = os.open(os.path.join(out_dir, name), os.O_RDONLY | getattr(os, "O_BINARY", 0))
    except FileNotFoundError:
        raise _CaptureError(f"{name}: declared output missing")
    except OSError:
        # O_NOFOLLOW open on a symlink raises ELOOP; other OSErrors (e.g.
        # device nodes) must also become a capture failure, not a 500.
        raise _CaptureError(f"{name}: not a regular file")
    try:
        st = os.fstat(fd)
        if not stat.S_ISREG(st.st_mode):
            raise _CaptureError(f"{name}: not a regular file")
        if st.st_nlink != 1:
            raise _CaptureError(f"{name}: hardlinked output")
        limit = min(generated_outputs.MAX_FILE_BYTES, remaining)
        if st.st_size > limit:
            raise _CaptureError(f"{name}: output too large")
        sha = hashlib.sha256()
        total = 0
        with zf.open(entry, mode="w", force_zip64=True) as dst:
            while True:
                chunk = os.read(fd, 65536)
                if not chunk:
                    break
                total += len(chunk)
                if total > limit:
                    raise _CaptureError(f"{name}: output too large")
                sha.update(chunk)
                dst.write(chunk)
        return total, sha.hexdigest()
    finally:
        os.close(fd)


def run_job_artifacts(body: dict, outputs: list[dict]):
    """Returns ("json", payload) or ("zip", tmp_file, length). Caller closes the file."""
    if not _JOBS.acquire(blocking=False):
        return "json", {"exitCode": 1, "error": "sandbox_busy", "stdout": "", "stderr": ""}
    try:
        return _run_job_artifacts(body, outputs)
    finally:
        _JOBS.release()


def _run_job_artifacts(body: dict, outputs: list[dict]):
    code = body.get("code") or body.get("script") or ""
    if not isinstance(code, str) or not code.strip():
        return "json", {"exitCode": 2, "error": "code is required", "stdout": "", "stderr": ""}
    if len(code) > 80_000:
        return "json", {"exitCode": 2, "error": "script too large", "stdout": "", "stderr": ""}
    env = _job_env(body, [s["fileName"] for s in outputs])
    posix = os.name == "posix"
    with tempfile.TemporaryDirectory(prefix="delegat-job-") as tmp:
        # Dedicated outputs directory exists before the child starts; on POSIX a
        # dir fd is pinned so the child cannot replace the directory itself.
        out_dir = os.path.join(tmp, "outputs")
        os.mkdir(out_dir)
        dir_fd = None
        if posix and hasattr(os, "O_DIRECTORY"):
            dir_fd = os.open(out_dir, os.O_RDONLY | os.O_DIRECTORY)
        try:
            script_path = _write_job_script(tmp, code)
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
                out, err = _bounded(stdout, stderr)
                return "json", {
                    "exitCode": 124,
                    "stdout": out,
                    "stderr": (err + "\ntimeout")[:MAX_OUTPUT],
                    "error": "timeout",
                }
            _reap_group(proc)
            out, err = _bounded(stdout, stderr)
            if proc.returncode != 0:
                return "json", {
                    "exitCode": proc.returncode,
                    "stdout": out,
                    "stderr": err,
                    "error": "nonzero_exit",
                }
            result_info = {"stdout": out, "stderr": err, "exitCode": 0, "error": None}
            tmpzip = tempfile.TemporaryFile(prefix="delegat-artifacts-", suffix=".zip")
            try:
                metas: list[dict] = []
                remaining = generated_outputs.MAX_BATCH_BYTES
                with zipfile.ZipFile(tmpzip, "w", zipfile.ZIP_STORED) as zf:
                    for index, spec in enumerate(outputs):
                        length, sha = _capture_output(
                            dir_fd, out_dir, spec, zf, f"outputs/{index}", remaining)
                        remaining -= length
                        metas.append({
                            "index": index,
                            "fileName": spec["fileName"],
                            "contentType": spec["contentType"],
                            "length": length,
                            "sha256": sha,
                        })
                    manifest = {
                        "protocol": generated_outputs.ARTIFACT_PROTOCOL,
                        "result": result_info,
                        "outputs": metas,
                    }
                    zf.writestr("manifest.json", json.dumps(manifest).encode("utf-8"))
            except _CaptureError as exc:
                tmpzip.close()
                return "json", {
                    "exitCode": 1,
                    "error": f"output_capture_failed:{exc}",
                    "stdout": out,
                    "stderr": err,
                }
            except Exception:
                tmpzip.close()
                raise
            length = os.fstat(tmpzip.fileno()).st_size
            tmpzip.seek(0)
            return "zip", tmpzip, length
        finally:
            if dir_fd is not None:
                os.close(dir_fd)


def _run_job(body: dict) -> dict:
    code = body.get("code") or body.get("script") or ""
    if not isinstance(code, str) or not code.strip():
        return {"exitCode": 2, "error": "code is required", "stdout": "", "stderr": ""}
    if len(code) > 80_000:
        return {"exitCode": 2, "error": "script too large", "stdout": "", "stderr": ""}

    env = _job_env(body, None)

    posix = os.name == "posix"
    with tempfile.TemporaryDirectory(prefix="delegat-job-") as tmp:
        script_path = _write_job_script(tmp, code)
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
            _json(self, 200, {"ok": True, "artifactProtocol": generated_outputs.ARTIFACT_PROTOCOL})
            return
        self.send_error(404)

    def _read_body(self):
        """Returns (body_bytes, error_code) - error_code None on success."""
        try:
            length = int(self.headers.get("Content-Length") or "0")
        except ValueError:
            length = 0
        te = (self.headers.get("Transfer-Encoding") or "").lower()
        if "chunked" in te:
            raw = _read_chunked(self.rfile, MAX_BODY)
            if raw is None:
                return None, 413
            return raw, None
        if length <= 0 or length > MAX_BODY:
            return None, 413
        return self.rfile.read(length), None

    def do_POST(self) -> None:
        if self.path == "/run-artifacts":
            self._post_artifacts()
            return
        if self.path != "/run":
            self.send_error(404)
            return
        if SECRET:
            got = self.headers.get("X-Sandbox-Secret") or ""
            if got != SECRET:
                _json(self, 403, {"exitCode": 1, "error": "forbidden", "stdout": "", "stderr": ""})
                return
        raw, err_code = self._read_body()
        if err_code is not None:
            _json(self, err_code, {"exitCode": 1, "error": "invalid body", "stdout": "", "stderr": ""})
            return
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

    def _post_artifacts(self) -> None:
        # Fails closed: capture requires an explicitly configured secret, even
        # when plain /run would accept unauthenticated requests.
        if not SECRET or (self.headers.get("X-Sandbox-Secret") or "") != SECRET:
            _json(self, 403, {"exitCode": 1, "error": "forbidden", "stdout": "", "stderr": ""})
            return
        raw, err_code = self._read_body()
        if err_code is not None:
            _json(self, err_code, {"exitCode": 1, "error": "invalid body", "stdout": "", "stderr": ""})
            return
        try:
            body = json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            _json(self, 400, {"exitCode": 1, "error": "invalid json", "stdout": "", "stderr": ""})
            return
        if not isinstance(body, dict) or body.get("artifactProtocol") != generated_outputs.ARTIFACT_PROTOCOL:
            _json(self, 400, {"exitCode": 2, "error": "invalid artifact protocol", "stdout": "", "stderr": ""})
            return
        outputs = generated_outputs.parse_outputs(body)
        if outputs is None:
            _json(self, 400, {"exitCode": 2, "error": "invalid outputs declaration", "stdout": "", "stderr": ""})
            return
        try:
            result = run_job_artifacts(body, outputs)
        except Exception:
            # Never leak str(exc), tracebacks or filesystem paths to the API.
            _json(self, 200, {
                "exitCode": 1,
                "error": "ai_artifact_capture_failed",
                "stdout": "",
                "stderr": "",
            })
            return
        kind = result[0]
        if kind == "json":
            payload = result[1]
            code = 429 if payload.get("error") == "sandbox_busy" else 200
            _json(self, code, payload)
            return
        _, fh, length = result
        try:
            self.send_response(200)
            self.send_header("Content-Type", "application/zip")
            self.send_header("Content-Length", str(length))
            self.end_headers()
            while True:
                chunk = fh.read(65536)
                if not chunk:
                    break
                self.wfile.write(chunk)
        finally:
            fh.close()


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
