# Delegat.Module

Product catalog and marketplace synchronization tool (Ozon, Wildberries, OpenCart).

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
docker compose -f docker-compose.prod.yml up -d
```

## Structure

```
backend/          — ASP.NET Core API
frontend/         — Vue 3 SPA
plugins/          — Marketplace adapter submodules (DLL)
  Delegat.Marketplace.Ozon/        → delegat-adapter-ozon
  Delegat.Marketplace.Wildberries/ → delegat-adapter-wildberries
  Delegat.Marketplace.OpenCart/    → delegat-adapter-opencart
  Delegat.Marketplace.YandexMarket/→ delegat-adapter-yandex-market
sdk/              — SDK submodule → delegat-marketplace-sdk
docs/             — Documentation
```

## Documentation

- [Platform capabilities](docs/platform/capabilities.md)
- [SaaS architecture](docs/architecture/saas-architecture.md)
- [Public API v1](docs/api/public-v1.md)
- [OpenCart adapter](docs/adapters/opencart.md)

## Releases

Releases are created via git tags:

```bash
git tag v1.0.0
git push origin v1.0.0
```

CI automatically builds Docker images and publishes them to GHCR.
