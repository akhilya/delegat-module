# Анализ платформы PrintO Dashboard — готовность к продаже

## Модель дистрибуции

**Per-instance автохостинг:** каждый селлер получает отдельный Docker stack на поддомене.

- Своя БД (MySQL), своё хранилище (MinIO), свой бэкенд + фронтенд
- Текущий стек: `docker-compose.prod.yml` (mysql + minio + api + nginx)
- Деплой: Dokploy + GitHub Actions
- Изоляция: физическая (отдельные контейнеры, БД, хранилище) — утечка данных между селлерами невозможна по архитектуре

> Детали инфраструктуры дистрибуции (провижининг, обновления, лицензирование, центральная панель, биллинг) — отдельный проект: [distribution-platform-analysis.md](./distribution-platform-analysis.md)

---

## Текущие возможности платформы

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
- 2 встроенных плагина: Wildberries (v1.7.1), Ozon (v1.1.1)
- Подключения (MarketplaceConnection) с зашифрованными credentials
- Маппинг полей (FieldMapping) с пайплайном модификаторов (regex, template, calculation)
- Маппинг категорий (CategoryMapping) + импорт категорий с маркетплейса
- Двусторонняя синхронизация товаров (ProductSyncService)
- Синхронизация цен (PriceSyncService) и остатков
- Синхронизация групп (GroupSyncService)
- Bulk-операции экспорта/импорта
- Rate limiting на уровне SDK (RateLimitConfig, MarketplaceRateLimiter)

### Мультитенантность (внутри инстанса)
- Магазины (Store) как основная единица организации
- Пользователь может быть участником нескольких магазинов
- Переключение магазинов через selectedStoreId cookie
- Все сущности привязаны к storeId

### Аутентификация
- JWT в cookie (HttpOnly, Secure)
- ASP.NET Identity (User : IdentityUser)
- Инвайт-токены для регистрации
- Простой isAdmin флаг (не ролевая модель)
- Admin seed через env vars (ADMIN_SEED_EMAIL, ADMIN_SEED_PASSWORD)

### Фоновые сервисы
- ImageDownloadBackgroundService — загрузка изображений с маркетплейсов
- ProductImportBackgroundService — импорт товаров из очереди (PendingProductImport)
- ProductExportBackgroundService — экспорт товаров в очередь (PendingProductExport)
- Все три используют DB-очередь, но состояние обработки не персистентно

### Инфраструктура
- .NET 8 + EF Core 8 + MySQL 8.0
- Vue 3 + Vite + Vuex (Options API)
- MinIO (S3-compatible) для хранения файлов
- Docker Compose (MySQL + MinIO + backend)
- Автоматические миграции при старте
- Брендинг: VITE_BRAND_NAME, VITE_BRAND_LOGO, VITE_BRAND_FAVICON

### Админка
- Управление магазинами (CRUD)
- Управление пользователями (добавление/удаление из магазина)
- Генерация инвайт-токенов

---

## Что необходимо платформе

### КРИТИЧНО — Безопасность

#### 1. CSRF защита
- **Текущее состояние:** Не настроена. JWT хранится в HttpOnly cookie + CORS AllowCredentials — уязвимо к CSRF.
- **Нужно:** CSRF tokens (AntiForgery) или переход на `SameSite=Strict`.
- **Ключевые файлы:** `Program.cs`

#### 2. Account lockout
- **Текущее состояние:** `lockoutOnFailure: false` — бесконечный брутфорс паролей.
- **Нужно:** Включить lockout policy (5 попыток, 15 мин блокировка).
- **Ключевые файлы:** `JwtAuthController.cs:47`, `Program.cs` (Identity config)

#### 3. Refresh tokens
- **Текущее состояние:** Только access token (1 день / 30 дней с "запомнить"). При истечении — полный перелогин.
- **Нужно:** Refresh token в DB, ротация при использовании, revocation.
- **Ключевые файлы:** `JwtService.cs`, `JwtAuthController.cs`

#### 4. Request size limits на JSON endpoints
- **Текущее состояние:** 10GB лимит на все endpoint'ы (настроен для upload файлов).
- **Нужно:** Снизить default до 1-5MB, оставить 10GB только на upload endpoint'ах через `[RequestSizeLimit]`.
- **Ключевые файлы:** `Program.cs`

#### 5. Password complexity
- **Текущее состояние:** Минимум 6 символов, нет требований к цифрам/спецсимволам/регистру.
- **Нужно:** Ужесточить: min 8 символов, цифра + спецсимвол или uppercase.
- **Ключевые файлы:** `Program.cs:50-59`

#### 6. Шифрование credentials маркетплейсов at rest
- **Текущее состояние:** API-ключи маркетплейсов хранятся как plain JSON в поле `EncryptedCredentials` (название не соответствует действительности).
- **Нужно:** AES-256 шифрование с ключом из env var, расшифровка только при использовании.
- **Ключевые файлы:** `MarketplaceConnection.cs:32-36`

---

### КРИТИЧНО — Надёжность

#### 7. Персистентность задач синхронизации (UI истории задач)
- **Текущее состояние:** BackgroundService'ы используют DB-очередь (PendingProductImport/Export), retry есть (3/5 попыток), но:
  - При рестарте контейнера задачи в статусе "processing" зависают навсегда
  - Нет UI истории завершённых/проваленных задач
  - Прогресс не сохраняется — после обновления страницы состояние теряется
- **Нужно:** Страница "Задачи" с историей всех операций (статус, timestamps, ошибки). Recovery processing-задач при рестарте.
- **Ключевые файлы:** `PendingProductImport.cs`, `PendingProductExport.cs`, `ProductImportBackgroundService.cs`, `ProductExportBackgroundService.cs`

#### 8. Health check с проверкой DB + MinIO + background services
- **Текущее состояние:** Минимальный health endpoint — возвращает `{ status, version }` без реальных проверок.
- **Нужно:** Проверка подключения к MySQL, доступности MinIO, статуса background services. ASP.NET `AddHealthChecks()`.
- **Ключевые файлы:** `Program.cs:260`

#### 9. Backup & Recovery
- **Текущее состояние:** Docker volumes без автобэкапов. Потеря данных = потеря клиента.
- **Нужно:** Автоматические бэкапы MySQL (mysqldump / xtrabackup) + MinIO данных. Cron job или sidecar контейнер. Документация по восстановлению.
- **Ключевые файлы:** `docker-compose.prod.yml`

---

### ВЫСОКИЙ — Защита ресурсов

#### 10. Rate limiting на API
- **Текущее состояние:** Нет rate limiting на API платформы. Есть только SDK rate limiter для исходящих запросов к маркетплейсам.
- **Нужно:** ASP.NET `AddRateLimiter()` — fixed window или sliding window. Защита от абьюза: login endpoint (анти-брутфорс), API endpoints (анти-DoS). Хостингом занимается владелец платформы, инстансы нужно защищать.
- **Ключевые файлы:** `Program.cs`, SDK `MarketplaceRateLimiter.cs` (только для маркетплейсов)

#### 11. Квоты использования
- **Текущее состояние:** Безлимитное создание товаров, магазинов, подключений. Ресурсы ограничены только железом.
- **Нужно:** Квоты, регулируемые админом инстанса через env vars контейнера:
  - `MAX_PRODUCTS` — максимум товаров
  - `MAX_STORAGE_GB` — максимум хранилища
  - `MAX_CONNECTIONS` — максимум подключений к маркетплейсам
- Проверка в коде при создании (middleware или сервис). Без UI управления — настраивается через env vars.
- **Ключевые файлы:** `StoreController.cs`, `ProductController.cs`, `MarketplaceConnectionController.cs`

---

### ВЫСОКИЙ — Пользовательский опыт

#### 12. Онбординг / first-run wizard
- **Текущее состояние:** Admin seed через env vars + инвайт-токены. Нет визуального wizard.
- **Нужно:** First-run wizard: создание учётной записи -> настройка магазина -> подключение маркетплейса -> первая синхронизация.
- **Ключевые файлы:** `DatabaseSeeder.cs`, `InvitationTokenController.cs`

#### 13. Брендинг (расширение)
- **Текущее состояние:** 4 env vars (VITE_BRAND_NAME, VITE_BRAND_DESCRIPTION, VITE_BRAND_LOGO, VITE_BRAND_FAVICON). Нет цветов.
- **Нужно:** Цветовая схема (primary/accent), кастомный домен, email templates.
- **Ключевые файлы:** `frontend/src/brand.js`

---

### СРЕДНИЙ

#### 14. Мониторинг
- **Текущее состояние:** Только ILogger, нет Sentry/Prometheus. Логи только в stdout контейнера.
- **Нужно:** Sentry для ошибок, structured logging (Serilog), опционально Prometheus metrics.
- **Ключевые файлы:** `appsettings.json`, `Program.cs`

#### 15. Производительность
- **Текущее состояние:** Каждый инстанс на ограниченных ресурсах (VPS тариф).
- **Проблемы:**
  - ProductImportBackgroundService: 3 товара/batch с 1s задержкой — медленно для больших каталогов
  - Batch processing не оптимизирован
- **Нужно:** Увеличить batch size, оптимизировать memory footprint, стратегия индексирования БД.

#### 16. Уведомления (email, in-app)
- **Текущее состояние:** Нет SMTP, нет системы уведомлений.
- **Нужно:** Завершение синхронизации, ошибки, инвайты — email и/или in-app.

#### 17. Документация пользователя
- **Текущее состояние:** Нет пользовательской документации.
- **Нужно:** Help center, гайды по настройке, FAQ.

#### 18. API Versioning
- **Текущее состояние:** Нет версионирования API.
- **Нужно:** `v1/` префикс или header-based versioning для стабильности плагинов.

#### 19. Интернационализация (i18n)
- **Текущее состояние:** Весь UI на русском, hardcoded.
- **Нужно:** vue-i18n для международного рынка.

---

### НЕАКТУАЛЬНО для per-instance модели

| Пункт | Причина неактуальности |
|-------|----------------------|
| Аудит изоляции данных / Global Query Filters | БД изолирована физически, утечка между селлерами невозможна |
| RBAC (Role-Based Access Control) | У каждого инстанса свои пользователи, isAdmin достаточно на старте |
| Маркетплейс сторонних плагинов | Сначала нужна стабильная платформа |
| Аудит-логирование | Nice-to-have, но не блокер для продажи |
