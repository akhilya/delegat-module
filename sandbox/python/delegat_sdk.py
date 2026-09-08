"""Thin Delegat operator client for the Python sandbox. Calls Agent HTTP only."""

from __future__ import annotations

import json
import os
import urllib.error
import urllib.request


class DelegatClient:
    def __init__(self) -> None:
        self.base = (os.environ.get("DELEGAT_BASE_URL") or "").rstrip("/")
        raw = os.environ.get("DELEGAT_TOKEN") or ""
        self.token = raw if raw.lower().startswith("bearer ") else (f"Bearer {raw}" if raw else "")
        self.store_id = os.environ.get("DELEGAT_STORE_ID") or ""

    def _req(self, method: str, path: str, body: dict | None = None):
        if not self.base or not self.store_id:
            raise RuntimeError("DELEGAT_BASE_URL and DELEGAT_STORE_ID are required")
        url = f"{self.base}/api/agent/stores/{self.store_id}{path}"
        data = None if body is None else json.dumps(body).encode("utf-8")
        req = urllib.request.Request(url, data=data, method=method)
        if self.token:
            req.add_header("Authorization", self.token)
        req.add_header("Content-Type", "application/json")
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"Agent HTTP {exc.code}: {detail}") from exc

    def search_catalog(self, query: str, limit: int = 50):
        return self._req("POST", "/catalog/search", {"query": query, "limit": limit})

    def sql(self, sql: str):
        return self._req("POST", "/sql", {"sql": sql})

    def platform(self, method: str, path: str, body=None):
        payload = {"method": method, "path": path}
        if body is not None:
            payload["body"] = body
        return self._req("POST", "/platform", payload)

    def match_files(self, files: list):
        return self._req("POST", "/files/match", {"files": files})

    def search_knowledge(self, query: str, limit: int = 8):
        from urllib.parse import quote

        q = quote(query or "")
        url = f"{self.base}/api/agent/stores/{self.store_id}/knowledge?q={q}&limit={int(limit)}"
        req = urllib.request.Request(url, method="GET")
        if self.token:
            req.add_header("Authorization", self.token)
        with urllib.request.urlopen(req, timeout=60) as resp:
            return json.loads(resp.read().decode("utf-8"))


client = DelegatClient()
