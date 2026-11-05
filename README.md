# Delegat.Module

Product catalog and marketplace/shop integration tool for Ozon, Wildberries, Yandex Market, OpenCart, WooCommerce, and Medusa. Avito is supported by an experimental adapter for listing import and price operations; listing publication is not available.

Each customer runs an **isolated instance** (Docker stack). Selling, provisioning, billing, fleet updates, and ops control belong to the separate **Control Plane** repository (`delegat-module-platform`). This repository contains only instance-side notes and links for Control Plane topics.

## Stack

| Layer | Technologies |
|-------|-------------|
| Backend | .NET 8 · EF Core · MySQL 8.4 · MinIO |
| Frontend | Vue 3 · Vite · Tailwind CSS 4 |
| Infrastructure | Docker Compose · Dokploy |

## Quick Start

```bash
git clone --recurse-submodules https://github.com/akhilya/delegat-module.git
cd delegat-module
cp .env.example .env
# Fill in the values in .env
# AI on (self-host default): start the Python sidecar with --profile ai
docker compose -f docker-compose.prod.yml --profile ai up -d
# AI_ENABLED=false: omit --profile ai so the sidecar is not created
```

## Structure

```
backend/          — ASP.NET Core API
frontend/         — Vue 3 SPA
plugins/          — Marketplace adapters and store bridges
  Delegat.Marketplace.Ozon/         — Git submodule
  Delegat.Marketplace.Wildberries/  — Git submodule
  Delegat.Marketplace.OpenCart/     — Git submodule
  Delegat.Marketplace.YandexMarket/ — Git submodule
  Delegat.Marketplace.WooCommerce/  — Git submodule
  Delegat.Marketplace.Medusa/       — in-repository Medusa 2 store adapter
  Delegat.Marketplace.Avito/        — in-repository experimental adapter
sdk/              — SDK submodule → delegat-marketplace-sdk
docs/             — Documentation
```

## Documentation

- [Control Plane](docs/architecture/control-plane.md) — central platform: sell, deploy, control instances (+ Platform Link)
- [Platform capabilities](docs/platform/capabilities.md) — what an instance can do
- [SaaS architecture](docs/architecture/saas-architecture.md) — per-instance infra, quotas, provisioner draft
- [Public API v1](docs/api/public-v1.md) — partner API (`/api/v1`, Swagger at `/swagger`)
- [Assistant production rollout](docs/ai/production-rollout.md) — ship the assistant fixes and optionally enable local hybrid knowledge search
- [Full docs index](docs/README.md)
- [Project instructions](.github/copilot-instructions.md) — canonical docs, precedence, and maintenance rules

## Releases

Update `VERSION` and `CHANGELOG.md`, then merge or push the version change to `main`. The release workflow creates the matching Git tag and GitHub Release and publishes Docker images to GHCR. Do not create the release tag manually. See [the release procedure](docs/platform/releasing.md).
