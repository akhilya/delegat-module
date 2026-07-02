# Delegat.Module

Инструмент синхронизации товаров с маркетплейсами (Ozon, Wildberries, OpenCart).

## Стек

| Слой | Технологии |
|------|------------|
| Backend | .NET 8 · EF Core · MySQL 8.4 · MinIO |
| Frontend | Vue 3 · Vite · Tailwind CSS 4 |
| Инфраструктура | Docker Compose · Dokploy |

## Быстрый старт

```bash
cp .env.example .env
# Заполнить значения в .env
docker compose -f docker-compose.prod.yml up -d
```

## Структура

```
backend/          — ASP.NET Core API
frontend/         — Vue 3 SPA
plugins/          — Плагины маркетплейсов (DLL)
  Delegat.Marketplace.Ozon/
  Delegat.Marketplace.Wildberries/
  Delegat.Marketplace.OpenCart/
sdk/              — SDK для разработки плагинов
  Delegat.Marketplace.SDK/
docs/             — Документация
```

## Документация

- [Возможности платформы](docs/platform/capabilities.md)
- [SaaS-архитектура](docs/architecture/saas-architecture.md)
- [OpenCart адаптер](docs/adapters/opencart.md)

## Релизы

Релизы создаются через git-теги:

```bash
git tag v1.0.0
git push origin v1.0.0
```

CI автоматически собирает Docker-образы и публикует в GHCR.
