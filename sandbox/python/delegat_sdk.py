"""Thin Delegat operator client for the Python sandbox. Calls Agent HTTP only."""

from __future__ import annotations

import json
import os
import re
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

    def shop_request(self, connection_id: int, host: str, path: str):
        """Read a built-in Marketplace API through the server-side credential broker.

        This helper issues a brokered GET. For APIs that use POST for reads, use
        ``shop_operations`` and ``shop_read`` instead. Returns the broker envelope
        (``status``, ``body``, ``truncated``); the response contains no credentials.
        """
        if type(connection_id) is not int or connection_id < 1:
            raise ValueError("connection_id must be a positive integer")
        if not isinstance(host, str) or not host.strip():
            raise ValueError("host is required")
        if (
            not isinstance(path, str)
            or not path.startswith("/")
            or path.startswith("//")
            or "\\" in path
            or "#" in path
        ):
            raise ValueError("path must be an absolute API path, not a URL")
        return self._req(
            "POST",
            f"/marketplace/{connection_id}/read",
            {"host": host, "path": path},
        )

    def shop_read(self, connection_id: int, operation: str, arguments: dict | None = None):
        """Call a named, adapter-defined read-only operation through the credential broker.

        Adapters may use this for upstream APIs that require POST even for reads. The
        operation is not an HTTP method or path; each adapter must allow-list it and
        validate its arguments. No Marketplace credentials are exposed to the script.
        """
        if type(connection_id) is not int or connection_id < 1:
            raise ValueError("connection_id must be a positive integer")
        if not isinstance(operation, str) or not re.fullmatch(r"[a-z][a-z0-9_]{0,63}", operation):
            raise ValueError("operation must be a lowercase adapter operation name")
        if arguments is None:
            arguments = {}
        if not isinstance(arguments, dict):
            raise ValueError("arguments must be a JSON object")
        try:
            encoded_arguments = json.dumps(arguments, allow_nan=False).encode("utf-8")
        except (TypeError, ValueError) as exc:
            raise ValueError("arguments must be JSON serializable") from exc
        if len(encoded_arguments) > 16_384:
            raise ValueError("arguments exceed the broker limit")
        return self._req(
            "POST",
            f"/marketplace/{connection_id}/read-operation",
            {"operation": operation, "arguments": arguments},
        )

    def shop_operations(self, connection_id: int):
        """List the named read-only operations exposed for one connection."""
        if type(connection_id) is not int or connection_id < 1:
            raise ValueError("connection_id must be a positive integer")
        result = self._req("GET", f"/marketplace/{connection_id}/read-operations")
        if isinstance(result, dict) and isinstance(result.get("operations"), list):
            for operation in result["operations"]:
                if not isinstance(operation, dict):
                    continue
                schema = operation.get("argumentsSchemaJson")
                if isinstance(schema, str):
                    try:
                        operation["argumentsSchema"] = json.loads(schema)
                    except json.JSONDecodeError:
                        pass
        return result

    def match_files(self, files: list):
        return self._req("POST", "/files/match", {"files": files})

    def marketplace_doc(self, adapter: str, query: str = ""):
        from urllib.parse import quote

        url = (
            f"{self.base}/api/agent/stores/{self.store_id}/marketplace-docs/"
            f"{quote(adapter or '')}"
        )
        q = quote(query or "")
        if q:
            url += f"?q={q}"
        req = urllib.request.Request(url, method="GET")
        if self.token:
            req.add_header("Authorization", self.token)
        try:
            with urllib.request.urlopen(req, timeout=60) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"Agent HTTP {exc.code}: {detail}") from exc

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
