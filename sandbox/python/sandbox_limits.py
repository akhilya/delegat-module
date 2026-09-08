"""Unix rlimits for a sandbox job. Imported inside the child, not via Popen preexec_fn."""

from __future__ import annotations

import os


def _int_env(name: str, default: int, lo: int, hi: int) -> int:
    raw = (os.environ.get(name) or "").strip()
    try:
        value = int(raw) if raw else default
    except ValueError:
        value = default
    return max(lo, min(hi, value))


def apply() -> None:
    try:
        os.nice(10)
    except (OSError, AttributeError):
        pass
    try:
        import resource

        as_bytes = _int_env("SANDBOX_AS_MB", 384, 64, 512) * 1024 * 1024
        resource.setrlimit(resource.RLIMIT_AS, (as_bytes, as_bytes))
        cpu = max(2, _int_env("SANDBOX_TIMEOUT_SEC", 30, 2, 120) + 2)
        resource.setrlimit(resource.RLIMIT_CPU, (cpu, cpu))
        fsize = 32 * 1024 * 1024
        resource.setrlimit(resource.RLIMIT_FSIZE, (fsize, fsize))
        resource.setrlimit(resource.RLIMIT_NPROC, (32, 32))
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    except (ValueError, OSError, ImportError):
        pass
