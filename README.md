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
cp .env.example .env
# Fill in the values in .env
docker compose -f docker-compose.prod.yml up -d
```

## Structure

```
backend/          — ASP.NET Core API
frontend/         — Vue 3 SPA
plugins/          — Marketplace plugins (DLL)
  Delegat.Marketplace.Ozon/
  Delegat.Marketplace.Wildberries/
  Delegat.Marketplace.OpenCart/
sdk/              — SDK for plugin development
  Delegat.Marketplace.SDK/
docs/             — Documentation
```

## Documentation

- [Platform capabilities](docs/platform/capabilities.md)
- [SaaS architecture](docs/architecture/saas-architecture.md)
- [OpenCart adapter](docs/adapters/opencart.md)

## Releases

Releases are created via git tags:

```bash
git tag v1.0.0
git push origin v1.0.0
```

CI automatically builds Docker images and publishes them to GHCR.
