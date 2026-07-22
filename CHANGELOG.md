# Changelog

All notable changes to Delegat.Module are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

How we maintain this file: [docs/platform/releasing.md](docs/platform/releasing.md).

## [Unreleased]

### Added
- Dependabot version updates (npm, NuGet, Docker, Compose, GitHub Actions) with grouped weekly PRs
- Admin UI toggles for instance logging (errors, API requests, adapter HTTP) with Save-to-apply; settings persisted in MySQL
- Auto-start Vite on `:5173` when the API runs in Development
- Roles and permissions catalog; Platform Link / reference catalog plumbing
- Folder archive and delete as background jobs; AI explain UI for operations
- API request logs on the API admin page (when logging is enabled)
- vue-i18n (ru default, en fallback) across shell, products, marketplace, settings, admin, AI
- Control Plane architecture docs and MVP brief

### Changed
- Dependabot: version updates limited to minor/patch only (no auto major PRs for Node/.NET/MySQL/npm)
- Further runtime RAM/CPU cuts: MinIO stream buffer cap, InnoDB default 256M, AI gated at startup, logging env/DB toggles
- Settings nav renamed to Administration; log pages hidden when corresponding toggles are off
- Env logging flags (`LOG_*`) become seed defaults only; DB is source of truth after first boot

### Fixed
- CI checkout for Dependabot: fall back when `SUBMODULE_PAT` is unavailable; bump Actions off Node 20 runtimes
- MySQL dump for app users and UTF-8 BOM option file
- Logout handling; `ENCRYPTION_KEY` passed into the API container in production
- Product save gating for required fields; AI model dialog / DialogContent hardening
- Dependabot security bumps (including DOMPurify 3.4.12)

### Security
- Remediation from 2026-07-17 security audits (uploads, secrets, AI, injection)
- Hardened admin ops: env-only quotas, queue TTL, break-glass

## [1.3.2] - 2026-07-17

### Fixed
- Proxy Swagger static assets through nginx with `^~` priority so `/swagger/*` JS/CSS are not served as SPA 404s

## [1.3.1] - 2026-07-17

### Added
- Public API v1 with API keys for partner integrations
- Yandex adapter manufacturer data fields
- Broader test coverage for Public API auth, store access, import queue, and related helpers

### Fixed
- Enable Public API Swagger in production and polish 404 handling

## [1.3.0] - 2026-07-15

### Added
- Multi-column sorting on the products table
- Adapter HTTP request logs page and instrumentation
- Store timezone formatting for UI datetimes
- Description HTML `<br>` formatting and Yandex additional offer fields

### Fixed
- Yandex filter characteristics, ENUM params, free-text select params, price export rules
- Mapping node collapse/pin persistence across sync
- Product multi-sort query parsing and tiebreaker behavior

## [1.2.1] - 2026-07-14

### Changed
- Patch release (see GitHub compare `v1.1.0...v1.2.1`)

## [1.1.0] - 2026-07-09

### Changed
- Minor release (see GitHub compare `v1.0.1...v1.1.0`)

## [1.0.1] - 2026-07-02

### Fixed
- Patch after initial 1.0.0

## [1.0.0] - 2026-07-02

### Added
- Initial Delegat.Module release

[Unreleased]: https://github.com/akhilya/delegat-module/compare/v1.3.2...HEAD
[1.3.2]: https://github.com/akhilya/delegat-module/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/akhilya/delegat-module/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/akhilya/delegat-module/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/akhilya/delegat-module/compare/v1.1.0...v1.2.1
[1.1.0]: https://github.com/akhilya/delegat-module/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/akhilya/delegat-module/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/akhilya/delegat-module/releases/tag/v1.0.0
