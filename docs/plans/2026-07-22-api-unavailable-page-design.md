# API unavailable page

**Date:** 2026-07-22  
**Status:** Implemented

> Historical design record. The behavior is shipped; verify the current route and API handling in code before making changes.

## Problem

When the backend process is down, restarting, or behind a gateway during an instance update, the UI previously fell through to sign-in or raw request failures. Partners and operators need a dedicated, calm screen that matches the product design code.

## Decision

- Public route `/unavailable` (no sidebar / FABs).
- Detect offline via `/health` probe on first load and via axios errors at runtime (`ERR_NETWORK`, timeout, `502` / `503` / `504`).
- Auto-poll `/health` with backoff while on the page; restore the previous route on success.
- Copy and layout follow `docs/platform/product.md` and AuthLayout / notFound patterns (Onest, neutral palette, semantic warning only on the status eyebrow).

## Non-goals

- Distinguishing "updating" from "down" (no reliable signal without a live API).
- Treating ordinary 4xx/5xx with an application body as offline.
