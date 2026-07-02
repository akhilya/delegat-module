# Delegat Dashboard — возможности платформы

## Модель дистрибуции

**Per-instance автохостинг:** каждый клиент получает отдельный Docker stack на поддомене.

- Своя БД (MySQL), своё хранилище (MinIO), свой бэкенд + фронтенд
- Стек: `docker-compose.prod.yml` (mysql + minio + delegat-api + frontend/nginx)
- Деплой: Dokploy + GitHub Actions
- Изоляция: физическая — утечка данных между клиентами невозможна по архитектуре

> Детали инфраструктуры дистрибуции (провижининг, масштабирование, центральный портал): [../architecture/saas-architecture.md](../architecture/saas-architecture.md)

---

## Текущие возможности

### Управление товарами
- CRUD товаров с автосохранением
- Категории с типизированными атрибутами (Text, Number, Boolean, Select, MultiSelect, Barcode)
- Глобальные атрибуты магазина (StoreAttribute)
- Варианты/размеры (ProductVariant) с ценой и штрихкодом на вариант
- Изображения (ImageReference) + файлы (File) с MinIO хранилищем
- Группы товаров (ProductGroup) с сайдбаром навигации
- Папки товаров (ProductFolder) для иерархической организации
- Заметки (Note) к товарам
- Excel импорт/экспорт товаров (ExcelProductService)
- Расширенная таблица: поиск, фильтры, сортировка, выбор столбцов, пресеты, bulk edit

### Интеграция с маркетплейсами
- Плагинная архитектура (DLL, изолированные AssemblyLoadContext, hot reload)
- Плагины: Wildberries, Ozon, OpenCart (через bridge)
- Подключения (MarketplaceConnection) с зашифрованными credentials (AES-256)
- Визуальный редактор маппинга полей (граф-узлы, MappingCanvas)
- Пайплайн модификаторов: regex, template, calculation, value-map
- Маппинг категорий (CategoryMapping) + импорт категорий с маркетплейса
- Двусторонняя синхронизация товаров (ProductSyncService)
- Синхронизация цен (PriceSyncService) и остатков
- Синхронизация групп (GroupSyncService)
- Bulk-операции экспорта/импорта
- Rate limiting на уровне SDK (RateLimitConfig, MarketplaceRateLimiter)

### Мультитенантность (внутри инстанса)
- Магазины (Store) как основная единица организации
- Пользователь может быть участником нескольких магазинов
- Переключение магазинов через selectedStoreId cookie (проверка членства в БД)
- Все сущности привязаны к storeId

### Аутентификация и безопасность
- JWT в cookie (HttpOnly, SameSite=Strict)
- Refresh tokens в БД с ротацией при использовании
- ASP.NET Identity с account lockout (5 попыток, 15 мин блокировка)
- Инвайт-токены для регистрации
- CSRF защита через X-CSRF header (CsrfHeaderMiddleware)
- Rate limiting: 300 req/min глобально, 10 req/min на auth endpoints
- isAdmin флаг (не ролевая модель)
- Admin seed через env vars (ADMIN_EMAIL, ADMIN_USERNAME, ADMIN_PASSWORD)

### Квоты
- `MAX_PRODUCTS` — максимум товаров на магазин
- `MAX_STORAGE_GB` — максимум хранилища на магазин
- `MAX_FILES` — максимум файлов на магазин
- `MAX_CONNECTIONS` — максимум подключений к маркетплейсам
- Настраиваются через env vars, редактируемы через UI

### Фоновые сервисы
- ImageDownloadBackgroundService — загрузка изображений с маркетплейсов
- ProductImportBackgroundService — импорт товаров из очереди (PendingProductImport)
- ProductExportBackgroundService — экспорт товаров в очередь (PendingProductExport)
- Startup recovery: при рестарте контейнера задачи InProgress сбрасываются в Pending

### Инфраструктура
- .NET 8 + EF Core 8 + MySQL 8.4
- Vue 3 + Vite + Vuex (Options API)
- MinIO (S3-compatible) для хранения файлов
- Docker Compose + Dokploy
- Автоматические миграции EF Core при старте
- Брендинг: VITE_BRAND_NAME, VITE_BRAND_LOGO, VITE_BRAND_FAVICON
- Health check: `/api/health` с проверкой MySQL + MinIO

### Админка
- Управление магазинами (CRUD)
- Управление пользователями (добавление/удаление из магазина)
- Генерация инвайт-токенов
- Просмотр error logs

---

## Что ещё необходимо

### КРИТИЧНО — до первых клиентов

#### Онбординг / first-run wizard
- **Текущее состояние:** Admin seed через env vars + инвайт-токены. Нет визуального wizard.
- **Нужно:** Шаги: создание учётной записи → настройка магазина → подключение маркетплейса → первая синхронизация.

#### Backup & Recovery
- **Текущее состояние:** Docker volumes без автобэкапов.
- **Нужно:** Автоматические бэкапы MySQL (mysqldump) + MinIO данных. Cron job или sidecar контейнер.

---

### ВЫСОКИЙ

#### Мониторинг
- **Текущее состояние:** Только ILogger, логи только в stdout контейнера.
- **Нужно:** Sentry для ошибок (или structured logging через Serilog → Loki).

#### История задач (UI)
- **Текущее состояние:** Прогресс синхронизации виден только пока страница открыта.
- **Нужно:** Страница «Задачи» с историей всех операций (статус, timestamps, ошибки).

---

### СРЕДНИЙ

#### Уведомления (email)
- Email при завершении синхронизации, критических ошибках, инвайтах.

#### Производительность
- ProductImportBackgroundService: увеличить batch size (сейчас медленно для больших каталогов).
- Стратегия индексирования БД.

#### Брендинг (расширение)
- Кастомная цветовая схема (primary/accent) через env vars.
- Email templates.

---

### НЕАКТУАЛЬНО для per-instance модели

| Пункт | Причина |
|-------|---------|
| Аудит изоляции / Global Query Filters | БД физически изолирована |
| RBAC | isAdmin достаточно на старте |
| Маркетплейс сторонних плагинов | После стабилизации платформы |
| Аудит-логирование | Nice-to-have |
| i18n | Только русскоязычный рынок |
