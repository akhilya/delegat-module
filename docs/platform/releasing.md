# Releasing and CHANGELOG

Source of truth for the product version is the root [`VERSION`](../../VERSION) file.
User-facing history lives in root [`CHANGELOG.md`](../../CHANGELOG.md) ([Keep a Changelog](https://keepachangelog.com/) + [SemVer](https://semver.org/)).

## Keep `CHANGELOG.md` current

### On every meaningful change (feature, fix, security, breaking)

1. Add a short bullet under `## [Unreleased]` in the right section:
   - **Added** / **Changed** / **Fixed** / **Deprecated** / **Removed** / **Security**
2. Write for operators and partners, not for git history.
   - Good: `Admin UI toggles for instance logging with Save-to-apply`
   - Bad: `Wire LoggingSettingsService into ErrorLogService`
3. Skip noise: typo-only commits, pure refactors with no user impact, CI-only churn, dependency bumps that are not security-related (security bumps go under **Security** or **Fixed**).

Do this in the **same PR/commit** as the code change whenever the change is user-visible or operationally important.

### PR documentation checklist

Keep the four acknowledgement lines from [the PR template](../../.github/pull_request_template.md) unchanged and mark each reviewed item `[x]`. CI checks their exact wording. Put explanations about updated files or why a change is not applicable in a separate paragraph below the checklist.

### On every release

1. Choose the next SemVer in `VERSION` (major / minor / patch).
2. In `CHANGELOG.md`:
   - Rename `## [Unreleased]` → `## [X.Y.Z] - YYYY-MM-DD`
   - Add a fresh empty `## [Unreleased]` (with empty subsection headings as needed)
   - Update the compare links at the bottom of the file
3. Commit together, for example: `chore(release): X.Y.Z`
4. Push to `main`. Changing `VERSION` triggers [`.github/workflows/release.yml`](../../.github/workflows/release.yml):
   - git tag `vX.Y.Z`
   - Docker images to GHCR
   - GitHub Release whose body is the matching `CHANGELOG.md` section for that version

## What is not the version source

- `frontend/package.json` `version` — may lag; do not treat as canonical
- Git tags alone — always bump `VERSION` so the release workflow runs
- Control Plane / Platform Link — does not inject the SemVer shown in the instance user menu

## How the UI version badge is resolved

The user-menu badge shows the **instance** SemVer (prefixed with `v` in the UI):

1. `GET /api/health` → `version` from API assembly **InformationalVersion** (Docker `APP_VERSION`, or root `VERSION` at build)
2. Else a real bake-time `VITE_APP_VERSION` (CI/release build-arg from `VERSION`)

Placeholders `dev` / `vdev` are ignored so a bad frontend bake cannot hide a correct API version.

CI pushes to `:latest` pass `APP_VERSION` / `VITE_APP_VERSION` from `VERSION` (same as release). Redeploy the instance after new images land.

## CI note for Dependabot

Dependabot PR workflows do **not** receive Actions repository secrets. Private submodule checkout needs `SUBMODULE_PAT` duplicated under **Settings → Secrets and variables → Dependabot** (same name as the Actions secret). Workflows use `secrets.SUBMODULE_PAT || github.token` so a missing secret does not fail checkout with an empty `token` input.
