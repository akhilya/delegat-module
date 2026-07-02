# Delegat SaaS — архитектура дистрибуции

> Этот документ описывает инфраструктуру дистрибуции: провижининг, масштабирование, управление квотами, центральный портал.
>
> Возможности самой платформы: [../platform/capabilities.md](../platform/capabilities.md)

---

## 1. Модель изоляции: per-instance

Каждый клиент получает **изолированный Docker stack**: свой MySQL, MinIO, delegat-api, frontend.

**Почему не shared MySQL/MinIO:**
- Миграция с shared на per-instance требует дампа каждой схемы, координированного downtime и обновления connection string в Dokploy для каждого инстанса — сложность неоправданно высока
- Физическая изоляция исключает cross-tenant утечку данных по архитектуре

**Потребление ресурсов per-instance (idle):**

| Сервис | RAM |
|--------|-----|
| delegat-api (.NET 8) | 150–250 MB |
| frontend (nginx) | 10–20 MB |
| MySQL 8.4 (оптимизированный) | 180–350 MB |
| MinIO | 50–80 MB |
| **Итого** | **390–700 MB** |

На ВМ 8 GB: 10–12 инстансов. На 16 GB: 20–24 инстанса.

**Оптимизация MySQL** (добавить в `docker-compose.prod.yml`):
```yaml
services:
  mysql:
    command: >
      --innodb-buffer-pool-size=64M
      --performance-schema=OFF
      --max-connections=50
      --skip-name-resolve
```
Снижает потребление MySQL с ~350 MB до ~180 MB.

---

## 2. DNS — Selectel wildcard

**Текущая конфигурация:** Selectel DNS с wildcard-записью `*.your-domain.com → IP ноды`.

Отдельные A-записи для каждого поддомена **не нужны** — wildcard покрывает все поддомены автоматически.

**Мульти-нодная схема:**
При наличии нескольких нод с разными IP, wildcard работает только с одним IP. Варианты:
- **Вариант A (рекомендуется для старта):** Один nginx/HAProxy перед всеми нодами — один IP, маршрутизация по HTTP Host header к нужной ноде
- **Вариант B:** Per-node wildcard (`*.node-a.your-domain.com`, `*.node-b.your-domain.com`) — разные поддомены для разных нод

---

## 3. Общая схема

```
[Selectel DNS]
  *.your-domain.com → L4 балансировщик (или IP единственной ноды)

[Node A — ВМ 16 GB]        [Node B — ВМ 16 GB]
  Dokploy Agent               Dokploy Agent
  ├── client1.your-domain.com   ├── client3.your-domain.com
  │   ├── delegat-api          │   ├── delegat-api
  │   ├── frontend            │   └── ...
  │   ├── mysql
  │   └── minio
  └── client2.your-domain.com
      └── ...

[Central Portal — отдельная ВМ, 4 GB]
  PostgreSQL (Registry DB)
  Provisioner Service
  Monitoring Poller

[CI/CD — GitHub Actions]
  push → build image → push GHCR → rolling update всех инстансов
```

---

## 4. Управление квотами при смене плана

**Текущая реализация** (уже в коде):
- `AppSettingsService` читает из env vars: `MAX_STORAGE_GB`, `MAX_PRODUCTS`, `MAX_FILES`, `MAX_CONNECTIONS`
- `QuotaService` проверяет per-store (по `storeId`)
- При превышении → `ApiException.Conflict` (409)

**Таблица планов:**

| План | MAX_STORAGE_GB | MAX_PRODUCTS | MAX_CONNECTIONS |
|------|---------------|--------------|----------------|
| Starter | 15 | 5 000 | 3 |
| Pro | 50 | 50 000 | 10 |
| Business | 150 | 200 000 | 25 |
| Enterprise | 500 | — | — |

**Апгрейд** (15 GB → 500 GB):
1. Портал обновляет `MAX_STORAGE_GB=500` через Dokploy API
2. Trigger redeploy (~30–60 сек)

**Даунгрейд** (500 GB → 15 GB) — стратегия «мягкий кап»:
1. Установить новый лимит через Dokploy API → trigger redeploy
2. Существующие данные не удаляются (грандфазеринг)
3. Новые загрузки блокируются (`EnsureStorageQuotaAsync` уже это делает)
4. Пользователю отправить email: текущее использование + дедлайн для удаления лишних файлов

---

## 5. Масштабирование

**Алгоритм выбора ноды при провизионинге:**
```
1. SELECT * FROM nodes WHERE is_active AND current_instances < max_instances
2. ORDER BY current_instances ASC → берём наименее загруженную
3. Если список пуст → alert администратору
4. Создать инстанс на выбранной ноде, current_instances++
```

**Когда все ноды заполнены:**
- **Этап A (старт):** email/Telegram алерт → администратор вручную добавляет ВМ, устанавливает Dokploy, регистрирует в Registry DB
- **Этап B (при росте >1 ВМ/нед):** авто-создание ВМ через Hetzner Cloud API + cloud-init скрипт

---

## 6. Redis — не нужен

| Кейс | Нужен Redis? | Решение |
|------|-------------|---------|
| Rate limiting | ❌ | In-memory (1 API контейнер per-instance) |
| Refresh tokens | ❌ | MySQL достаточно |
| SignalR backplane | ❌ | In-memory (1 API контейнер) |
| Кэш маркетплейс-API | ⚠️ | `IMemoryCache` (встроен в ASP.NET) — TTL 1 час |

**Единственный ценный кейс — кэш `GetCategoryFieldsAsync`:**

Запрос к маркетплейсу занимает 1–5 сек. Открытие `fieldMappingPage` каждый раз делает новый запрос.

Решение без Redis:
```csharp
// Program.cs
builder.Services.AddMemoryCache();

// В CategoryMappingController:
var cacheKey = $"category-fields:{connection.adapterKey}:{externalCategoryId}";
if (!_cache.TryGetValue(cacheKey, out List<ExternalField> fields))
{
    fields = await adapter.GetCategoryFieldsAsync(connection, externalCategoryId, ct);
    _cache.Set(cacheKey, fields, TimeSpan.FromHours(1));
}
```

Redis добавлять только если: горизонтальное масштабирование API внутри одного инстанса (не планируется).

---

## 7. Центральный портал

**Стек:** .NET 8 + PostgreSQL + Vue 3. Отдельная ВМ (~4 GB RAM).

### Registry DB

```sql
nodes (
  id, name, ip, dokploy_url, dokploy_api_key_encrypted,
  max_instances DEFAULT 20, current_instances DEFAULT 0,
  is_active BOOLEAN, last_health_check TIMESTAMP
)

instances (
  id, node_id FK, subdomain UNIQUE,
  user_id FK, plan_id FK,
  mysql_password_encrypted, minio_key_encrypted,
  jwt_key_encrypted, encryption_key_encrypted, admin_password_encrypted,
  status ENUM('provisioning','active','suspended','terminated'),
  created_at, last_seen_at, last_deployed_version
)

users (id, email, password_hash, current_plan_id FK, subscription_status)
plans (id, name, max_storage_gb, max_products, max_connections, price_monthly)
```

### Provisioner flow

```
Триггер: успешная оплата
  1. Выбрать ноду (least-loaded)
  2. Сгенерировать секреты (JWT_KEY, MYSQL_PASSWORD, MINIO_ACCESS_KEY, MINIO_SECRET_KEY,
                             ENCRYPTION_KEY, ADMIN_PASSWORD)
  3. Зашифровать и сохранить в Registry DB (AES-256)
  4. Dokploy API → создать compose-проект из шаблона с env vars
  5. Поллить {subdomain}.your-domain.com/api/health до status=ok (timeout 5 мин)
  6. Отправить welcome email: ссылка + логин admin + временный пароль
  7. instances.status = 'active'
```

### Шаблон compose для провизионинга

```yaml
services:
  delegat-api:
    image: ghcr.io/delegat-studio/dashboard-api:${IMAGE_TAG}
    environment:
      MYSQL_SERVER: mysql
      MYSQL_DATABASE: delegat
      MYSQL_USER: delegat
      MYSQL_PASSWORD: ${MYSQL_PASSWORD}
      MINIO_ENDPOINT: minio:9000
      MINIO_ACCESS_KEY: ${MINIO_ACCESS_KEY}
      MINIO_SECRET_KEY: ${MINIO_SECRET_KEY}
      MINIO_BUCKET: delegat
      JWT_KEY: ${JWT_KEY}
      ENCRYPTION_KEY: ${ENCRYPTION_KEY}
      CORS_ORIGINS: https://${SUBDOMAIN}.your-domain.com
      ADMIN_EMAIL: ${ADMIN_EMAIL}
      ADMIN_USERNAME: admin
      ADMIN_PASSWORD: ${ADMIN_PASSWORD}
      MAX_STORAGE_GB: ${MAX_STORAGE_GB}
      MAX_PRODUCTS: ${MAX_PRODUCTS}
      MAX_CONNECTIONS: ${MAX_CONNECTIONS}
      ASPNETCORE_ENVIRONMENT: Production
  mysql:
    image: mysql:8.4
    command: >
      --innodb-buffer-pool-size=64M
      --performance-schema=OFF
      --max-connections=50
      --skip-name-resolve
  # ... остальное как в docker-compose.prod.yml
```

### Plan Change Handler

```
Триггер: смена тарифа пользователем
  1. Обновить env vars через Dokploy API (MAX_STORAGE_GB, MAX_PRODUCTS, MAX_CONNECTIONS)
  2. Trigger redeploy
  3. Если downgrade → email пользователю с текущим использованием хранилища
  4. Обновить instances.plan_id в Registry DB
```

### Update Manager

```
Триггер: POST /internal/update-all с { imageTag }
  Rolling update для каждого активного инстанса:
    1. Обновить image tag в Dokploy проекте
    2. Trigger redeploy
    3. Поллить /api/health (timeout 3 мин)
    4. Fail → rollback к предыдущему тегу + alert
    5. Записать last_deployed_version
```

### Monitoring Poller

```
Cron: каждые 5 мин
  Для каждого активного инстанса:
    GET {subdomain}.your-domain.com/api/health
    → 200: instances.last_seen_at = now()
    → 3 подряд fail: email/Telegram алерт
    → 10 подряд fail: instances.status = 'degraded'
```

---

## 8. Roadmap

| Этап | Задача | Статус |
|------|--------|--------|
| 0 | Health check, квоты, admin seed, startup recovery | ✅ Готово |
| 1 | MySQL оптимизация в `docker-compose.prod.yml` | Быстро |
| 2 | IMemoryCache для маркетплейс-API | 1–2 дня |
| 3 | First-run wizard в Dashboard | До первых клиентов |
| 4 | Central Portal backend (Registry DB + Provisioner) | 2–3 недели |
| 5 | Central Portal frontend + billing | 1–2 недели |
| 6 | Auto-scaling (Hetzner API + cloud-init) | При росте >1 ВМ/нед |

---

## Открытые вопросы

1. **DNS мульти-нод**: L4 балансировщик (один IP на все ноды) или per-node wildcard?
2. **Оплата**: ЮКасса (РФ) или Stripe (зарубежные клиенты)?
3. **Email-провайдер**: SendGrid / Postmark / собственный SMTP?
4. **License key**: нужна offline-защита (HMAC) или достаточно закрытого репозитория?
5. **Алерты**: только email или добавить Telegram-бот для мониторинга?
