"""Declared-output validation shared by the sidecar and the job SDK.

The server validates the request's ``outputs`` declaration before the child
starts; the child validates each ``output_path`` name against the same rules
plus the declared list in ``DELEGAT_OUTPUT_FILES``.
"""

from __future__ import annotations

import json
import os
import re

MAX_OUTPUTS = 4
MAX_FILE_BYTES = 32 * 1024 * 1024
MAX_BATCH_BYTES = 64 * 1024 * 1024
MAX_MANIFEST_BYTES = 2 * 1024 * 1024
MAX_FILE_NAME_CHARS = 180
MAX_FILE_NAME_UTF8_BYTES = 255
MAX_CONTENT_TYPE_CHARS = 200
DEFAULT_CONTENT_TYPE = "application/octet-stream"
ARTIFACT_PROTOCOL = 1

_MEDIA_TYPE_RX = re.compile(r"^[a-zA-Z0-9!#$&^_.+\-]+/[a-zA-Z0-9!#$&^_.+\-]+$")


def valid_output_name(name) -> bool:
    """Strict basename, at most 180 UTF-16 units and 255 UTF-8 bytes; never rewritten."""
    if not isinstance(name, str) or not name:
        return False
    try:
        if (len(name.encode('utf-16-le')) // 2 > MAX_FILE_NAME_CHARS
                or len(name.encode('utf-8')) > MAX_FILE_NAME_UTF8_BYTES):
            return False
    except UnicodeEncodeError:
        return False
    if name != name.strip() or name in ('.', '..'):
        return False
    for ch in name:
        if ord(ch) < 0x20 or 0x7F <= ord(ch) <= 0x9F or ch in '/\\':
            return False
    return True


def normalize_content_type(raw):
    """Explicit single media type (no parameters/whitespace), lowercase."""
    if raw is None:
        return DEFAULT_CONTENT_TYPE
    if not isinstance(raw, str):
        return None
    if raw != raw.strip() or not raw or len(raw) > MAX_CONTENT_TYPE_CHARS:
        return None
    if not _MEDIA_TYPE_RX.match(raw):
        return None
    return raw.lower()


def parse_outputs(body) -> list[dict] | None:
    """Validate the request ``outputs`` declaration; None rejects."""
    raw = body.get("outputs")
    if not isinstance(raw, list) or not raw or len(raw) > MAX_OUTPUTS:
        return None
    specs: list[dict] = []
    seen: set[str] = set()
    for item in raw:
        if not isinstance(item, dict) or set(item) - {"fileName", "contentType"}:
            return None
        name = item.get("fileName")
        if not valid_output_name(name) or name in seen:
            return None
        content_type = normalize_content_type(item.get("contentType"))
        if content_type is None:
            return None
        seen.add(name)
        specs.append({"fileName": name, "contentType": content_type})
    return specs


def declared_output_names() -> list[str]:
    """Declared names the server passed to this job, or [] when none."""
    raw = os.environ.get("DELEGAT_OUTPUT_FILES") or ""
    try:
        names = json.loads(raw)
    except (TypeError, ValueError):
        return []
    if not isinstance(names, list):
        return []
    return [n for n in names if isinstance(n, str)]
