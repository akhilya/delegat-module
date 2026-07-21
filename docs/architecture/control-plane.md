# Delegat Control Plane — центральная платформа

> Продуктово-архитектурный документ **control plane**: продажа, контроль и развёртывание
> изолированных инстансов Delegat.Module.
>
> Инфраструктурные детали нод, квот и compose-шаблона: [saas-architecture.md](saas-architecture.md)  
> Возможности самого инстанса: [../platform/capabilities.md](../platform/capabilities.md)  
> Продуктовый brief инстанса (UI каталога): [../platform/product.md](../platform/product.md)
>
> **Статус:** дизайн / pre-implementation. Код control plane **не** живёт в этом репозитории —
> для него заведён **отдельный monorepo** (см. §2). Здесь — каноническое описание продукта
> и решений, пока control-plane repo пуст или зеркалит этот документ.
>
> Инстанс уже готов быть управляемым снаружи (`/api/health`, квоты через env, admin seed, GHCR).

---

## 1. Зачем отдельная платформа

**Delegat.Module** — продукт, которым пользуются селлеры (каталог, маппинги, sync).

**Control Plane** — отдельный продукт, которым пользуются:

| Аудитория | Задача |
|-----------|--------|
| Покупатель (владелец бизнеса) | Выбрать тариф, оплатить, получить инстанс, управлять подпиской, писать в поддержку |
| Ops (вы) | Подтверждать релизы, обновлять флот, бэкапы/restore, break-glass, ноды, тикеты |

Это **не** multi-tenant внутри одной БД. Модель дистрибуции — **per-instance**: каждый клиент получает изолированный Docker-стек (api + frontend + MySQL + MinIO) на своём subdomain. Физическая изоляция — ключевой selling point.

```
[Marketing / Landing]
        │
        ▼
[Customer Console] ──── billing ────► [T-Bank]
        │                                    │
        │ trial / payment                    │ webhooks
        ▼                                    ▼
[Provisioner] ── Dokploy API ──► [Node] ──► Instance stack
        │                                      │
        │   Platform Link (m2m + user SSO)     │
        │◄──── catalog / 5xx / support SSO ────┘
        ▼
[Ops Console] — releases, fleet, backups, tickets, nodes
```

Инстанс остаётся физически изолированным по данным каталога, но имеет **доверенный канал** к control plane (не «совсем оффлайн appliance»).

---

## 2. Зафиксированные продуктовые решения

Решения из product brainstorm (2026-07). Менять осознанно и с записью в этом документе.

| # | Тема | Решение |
|---|------|---------|
| 1 | Онбординг | **Full self-serve:** оплата → автоматический provision инстанса с **approved release** |
| 2 | Кардинальность | **Сейчас продуктово:** 1 аккаунт → 1 активный инстанс. **Архитектурно сразу:** `Account` / `Organization` не привязаны жёстко к одному instance (связь 1:N, лимит «1 active» на тарифе) |
| 3 | Бэкапы | В control plane бэкап/restore — **только Ops**. Пользовательский dump/restore — отдельная фича **внутри инстанса** (не путать) |
| 4 | Документация | UX «часть продукта»; движок — **GitBook** (см. §11). Самописный docs CMS не делать |
| 5 | Поддержка | **Свой ticket-модуль** + Telegram как **ops-канал**. MVP: уведомления/reply в **личку** одному ops (группу/shared — позже при необходимости) |
| 6 | Рынок / платежи | Старт — **Россия**. Эквайринг: **Т-Банк** |
| 7 | Релизы | Новые инстансы и обновления флота идут с **последней подтверждённой в Ops ревизии**, не с «сырого» `latest` из CI |
| 8 | Trial | **7 дней** пробного периода |
| 9 | Неоплата / отмена | Сначала **stop** контейнеров инстанса; через **2 месяца** — **purge** volumes/данных |
| 10 | Первый вход | **Обязательная смена** временного admin-пароля при первом входе в инстанс |
| 11 | Репозиторий | Control plane — **отдельный monorepo** от `delegat-module` (платформа инстанса) |
| 12 | Связь инстанс ↔ CP | Инстанс **слабо связан** с control plane через **Platform Link**: machine credentials + user SSO. Use cases: **Reference Catalog** (валюты, timezones, реестр плагинов…), 5xx, позже поддержка в UI инстанса |

---

## 3. Четыре поверхности продукта

| Поверхность | Для кого | Содержание |
|-------------|----------|------------|
| **Marketing** | Публика | Лендинг, pricing, оферта, CTA на регистрацию |
| **Customer Console** | Покупатель | Аккаунт, подписка, инстанс, тикеты, ссылка на docs, feature request / смена тарифа |
| **Ops Console** | Вы | Approve release, fleet update, nodes, health, break-glass, backups/restore, тикеты |
| **Knowledge + Support** | Все | Внешний docs-движок + ticket-модуль (+ Telegram bridge) |

Клиентский кабинет и Ops — **два UI**, один backend control plane в **отдельном monorepo** от `delegat-module`.

---

## 4. Доменная модель (логическая)

Даже при лимите «один активный инстанс» сущности разделяются:

```
Account (email, auth)
  └── Organization (billing owner, plan, subscription)
        ├── InstanceMembership (role: owner/billing/tech/readonly)
        │     └── Instance (0..N; product limit: 1 active)
        ├── Subscription / PaymentIntent / Invoice
        └── SupportTicket
```

**Instance** в Registry (не путать с данными каталога внутри инстанса):

- `subdomain`, `node_id`, `plan_id`, `status`
- зашифрованные секреты стека (MySQL, MinIO, JWT, ENCRYPTION_KEY, admin password)
- `last_deployed_version` / `release_id`
- `last_seen_at` (health)
- `instance_public_id` + зашифрованный **Platform Link secret** (m2m; выдаётся при provision, ротация из Ops)

**Release:**

- CI собирает **Build** (image tag в GHCR)
- Ops помечает **Release** как `draft` → `canary` → `stable` (approved)
- Provision новых клиентов и default fleet update берут `stable`

Подробнее схема Registry DB и compose env: [saas-architecture.md](saas-architecture.md) §7.

---

## 5. Customer Console — возможности

### 5.1 Must-have (MVP)

1. **Лендинг** + регистрация / логин  
2. **Тарифы:** выбор плана, оплата, автопродление, отмена  
3. **Инстанс:** статус provision/active, URL, вход (credentials из welcome-письма)  
4. **Смена / отмена тарифа** (plan change → обновление env квот на инстансе через Dokploy)  
5. **Тикеты:** создать, переписка в треде, статусы  
6. **Документация:** ссылка / embed на внешний docs-движок  
7. **Feature request** (отдельный поток от смены тарифа)

### 5.2 Should-have / later

- Usage (products / storage / connections) и предупреждения до hard limit  
- Несколько инстансов на Organization (снять product limit)  
- Team seats на уровне портала  
- Custom domain  
- Self-service restore **нет** в control plane (см. §2 п.3)

---

## 6. Ops Console — возможности

### 6.1 Must-have (MVP)

1. **Releases:** список builds, approve → `stable`  
2. **Provision visibility:** очередь/статусы создания инстансов, ошибки  
3. **Fleet update:** обновить все или выбранные инстансы до approved release; rollback image при failed health  
4. **Health / activity:** poll `/api/health`, `last_seen_at`, degraded  
5. **Break-glass:** сброс пароля суперадмина инстанса (аудит, одноразовость)  
6. **Backups:** расписание дампа MySQL + MinIO; restore (in-place или в новый стек) — только ops  
7. **Nodes:** реестр, capacity, least-loaded  
8. **Тикеты:** очередь, ответ (в UI и/или reply из Telegram → в тикет)

### 6.2 Should-have / later

- Canary % / maintenance windows  
- Auto-provision VM при нехватке capacity  
- Status page  
- Impersonation с жёстким аудитом  

---

## 7. Billing (РФ)

### 7.1 Принципы

- Рынок старта: **Россия**.  
- Платёжный провайдер: **Т-Банк** (T-Bank Acquiring / API — конкретный продукт API уточнить при интеграции).  
- Full automation: успешная оплата или старт trial (webhook / подтверждение) → Provisioner.  
- Подписка с автопродлением; отмена прекращает продление — инстанс остаётся доступным до конца уже оплаченного периода (зафиксировать в оферте).

### 7.2 Trial

- Длительность: **7 дней**.  
- Open: нужна ли привязка карты / оплата ₽1 на старте trial (зависит от возможностей Т-Банк и антифрода) — см. §12.

### 7.3 Состояния подписки и поведение инстанса

```
trial → active → past_due → grace? → suspended → cancelled → purged
```

Зафиксированное поведение при неоплате / после отмены (когда доступ должен прекратиться):

| Фаза | Инстанс |
|------|---------|
| `suspended` / доступ прекращён | **Stop** контейнеров (стек остановлен, данные на диске сохраняются) |
| + **2 месяца** после stop | **Purge**: удаление volumes (MySQL + MinIO) и запись instance как `terminated` / `purged` |

Детали `past_due` / длина `grace` (баннер в консоли до stop) — уточнить в оферте и §12.

### 7.4 Тарифы ↔ квоты инстанса

Инстанс уже читает лимиты из env (`MAX_STORAGE_GB`, `MAX_PRODUCTS`, `MAX_CONNECTIONS`, `AI_ENABLED`).  
Control plane при provision и plan change выставляет их через Dokploy API и redeploy.

Ориентир планов: [saas-architecture.md](saas-architecture.md) §4.

---

## 8. Lifecycle инстанса

### 8.1 Статусы (черновик)

`provisioning` → `active` → `degraded` | `suspended` → (`terminated` / `purged` после purge)  
(+ `failed` на ошибке provision)

Stop сохраняет volumes; purge через 2 месяца удаляет данные (см. §7.3).

### 8.2 Provision flow (self-serve)

```
1. Старт trial (7 дней) или успешная оплата (webhook Т-Банк)
2. Создать Organization + Subscription + Instance (status=provisioning)
3. Выбрать ноду (least-loaded)
4. Сгенерировать секреты, зашифровать в Registry
5. Dokploy: создать compose-проект из шаблона + env (квоты плана, IMAGE_TAG=stable release)
6. Poll https://{subdomain}/api/health (timeout ~5 min)
7. Welcome email: URL + admin + временный пароль
8. status=active
9. Первый вход в инстанс → обязательная смена временного admin-пароля (force change)
```

### 8.3 Обновления флота

```
Trigger: Ops «обновить все / выбранные» до Release X
  Rolling: смена image tag → redeploy → health poll
  Fail → rollback предыдущего tag + alert
  Запись last_deployed_version
```

Новые клиенты всегда получают текущий **stable** release.

### 8.4 Break-glass (сброс суперадмина)

Протокол (продуктовые требования):

- Инициируется только из Ops Console  
- Пишется в audit log (кто, когда, какой instance)  
- Результат: новый временный пароль / one-time link; старые сессии инстанса по возможности инвалидируются  
- Техническая реализация (API инстанса vs пересоздание env `ADMIN_PASSWORD` + redeploy) — TBD  

---

## 9. Связь инстанса с Control Plane (Platform Link)

Инстанс и control plane — разные системы и репозитории, но **продуктово инстанс не «глухой»**: он знает, куда стучаться, и умеет делать это **с автоматической авторизацией**.

### 9.1 Зачем

| Use case | Когда | Кто инициирует |
|----------|-------|----------------|
| **Reference Catalog («заполнение»)** | Списки валют, timezones, реестр плагинов и др. справочники — единый источник на CP вместо хардкода в инстансе | Backend/UI инстанса → CP (часто публичный или m2m read) |
| **5xx / критические ошибки** | Необработанные исключения, health degradation | Backend инстанса → CP (telemetry / incident) |
| **Диагностика** | Upload diagnostic pack по запросу ops/support | UI/API инстанса → CP API |
| **Поддержка в UI инстанса** (later) | Тикет / чат без ухода на другой логин | Пользователь в инстансе → CP Support с **SSO** |
| Heartbeat (опционально) | Дополнение к внешнему health poller | Инстанс → CP |

Каталог товаров, MySQL и MinIO инстанса **не** становятся multi-tenant. Справочники с CP — **read-mostly metadata**, не данные продавца.

### 9.1.1 Reference Catalog — что выносится на Control Plane

Сейчас часть справочников зашита в инстанс (пример: `frontend/src/lib/timezones.js`, `frontend/src/lib/currency.js` → `ADDABLE_CURRENCIES`; плагины ставятся локально upload’ом DLL). Цель: **один раз править на CP**, все инстансы получают актуальный список.

| Справочник | Сейчас (инстанс) | На CP | Что остаётся в инстансе |
|------------|------------------|-------|-------------------------|
| **Валюты (каталог кодов)** | Хардкод `ADDABLE_CURRENCIES` | Список ISO-кодов + label/locale hints | Выбор магазина, `StoreCurrency`, курсы `rateToBase`, `baseCurrency` |
| **Timezones** | Хардкод `STORE_TIMEZONES` | Список IANA + display labels | Поле `store.timezone` (выбранное значение) |
| **Реестр плагинов** | Локальный upload / DLL на диске инстанса | Каталог адаптеров: key, имя, версии, changelog, URL артефакта, min platform version | Установленные плагины, connections, credentials |
| *(позже)* | — | Локали UI hints, feature flags, шаблоны онбординга… | Локальный кэш + fallback |

**Принцип:** CP владеет **глобальным справочником**; инстанс владеет **выборами и бизнес-данными** магазина.

```
Control Plane                         Instance
─────────────────                     ────────
currencies catalog  ──fetch/cache──►  UI: «добавить валюту»
timezones catalog   ──fetch/cache──►  UI: select timezone
plugin registry     ──fetch/cache──►  UI: «доступные адаптеры»
                                      + install from registry URL
store.currencies / rates   (local DB)
store.timezone             (local DB)
installed plugin DLLs      (local disk)
```

**Доставка в инстанс:**

1. При старте / по TTL инстанс тянет каталоги с CP (`GET /platform/v1/catalog/...`).  
2. Кэш в памяти (и опционально на диск) — UI не зависит от сети на каждый клик.  
3. Если CP недоступен → **fallback** на последний кэш или встроенный минимальный seed (self-host / offline).  
4. Auth: для публичных справочников достаточно `INSTANCE_ID` + подпись **или** даже signed URL / read-only token; секрет нужен, чтобы не открывать произвольный scrape без контроля. Для self-host без CP — только локальный seed.

**Реестр плагинов (особо):**

- CP хранит метаданные + ссылку на артефакт (GHCR / blob storage CP), не исходники клиентов.  
- Инстанс показывает «доступно к установке» vs «уже установлено».  
- Установка: скачать пакет по URL (m2m) → существующий `PluginManager` / upload path.  
- Совместимость: поле `minPlatformVersion` / `compatibleWith` сверяется с версией инстанса.  
- Ops на CP публикует/снимает версии плагинов отдельно от fleet update образа платформы (можно связать позже).

### 9.2 Два уровня авторизации

Нужны **оба** — путать нельзя.

#### A. Machine-to-machine (инстанс как устройство)

При provision control plane кладёт в env инстанса:

```text
CONTROL_PLANE_URL=https://console.example.com
INSTANCE_ID=inst_…
INSTANCE_LINK_SECRET=…   # или пара id+secret; хранить как остальные секреты
```

Инстанс подписывает запросы к CP (HMAC или short-lived JWT, signed secret’ом):

- `GET  /platform/v1/catalog/currencies` — справочник валют  
- `GET  /platform/v1/catalog/timezones` — справочник timezones  
- `GET  /platform/v1/catalog/plugins` — реестр плагинов (+ optional `GET …/plugins/{key}`)  
- `POST /platform/v1/errors` — 5xx / batch ошибок  
- `POST /platform/v1/heartbeat` — опционально  
- `POST /platform/v1/diagnostics` — diagnostic upload  
- `POST /platform/v1/sso/exchange` — user handoff  
- будущие cloud endpoints  

CP проверяет подпись, `INSTANCE_ID`, статус инстанса (`active` / не `purged`).  
Ротация secret — из Ops (новый secret → Dokploy env → redeploy или hot-reload, если появится).

#### B. User SSO (человек в UI инстанса → CP без повторного логина)

Когда пользователь (уже залогиненный в инстансе) открывает «Поддержка» / «Кабинет» / «Заполнить через CP»:

```
1. Frontend инстанса → Backend инстанса: «нужен CP session»
2. Backend инстанса (m2m + локальный userId/email/role) → CP:
     POST /platform/v1/sso/exchange
     → one-time code / short-lived handoff token (TTL секунды)
3. Browser redirect или iframe:
     https://console…/sso/consume?code=…
4. CP создаёт/привязывает сессию Account (по заранее связанному email / org membership)
5. Пользователь сразу в тикетах / нужном сервисе CP — без формы логина
```

**Привязка идентичности:** при provision admin email инстанса = email Account в CP (или явный `platform_user_id` в Registry). Расширение на non-admin пользователей инстанса — later (invite sync / email match).

Требования к SSO:

- code одноразовый, короткий TTL  
- привязка к `instance_id` + `user`  
- аудит в CP  
- пользователь без Account в CP → понятный fallback (создать soft-link / «откройте консоль»)

### 9.3 Что меняется в репозитории инстанса (`delegat-module`)

Минимальный задел (когда дойдём до реализации):

| Компонент | Назначение |
|-----------|------------|
| Env: `CONTROL_PLANE_URL`, `INSTANCE_ID`, `INSTANCE_LINK_SECRET` | Конфиг канала |
| `PlatformLinkClient` (backend) | Подпись и HTTP к CP |
| `ReferenceCatalogService` | Fetch + cache currencies / timezones / plugin registry; fallback seed |
| UI stores settings / plugins page | Читать справочники из API инстанса (который ходит в CP), не из hardcoded `lib/*` |
| Middleware / exception hook | Отправка 5xx (с сэмплингом / dedup) |
| UI: «Поддержка» / «Документация» | Deep-link или SSO handoff |
| Kill-switch | Если URL/secret пусты — инстанс работает offline на встроенном seed |

Self-host / dev без control plane остаётся валидным: Platform Link **опционален**.

### 9.4 Безопасность

- Secret только на backend инстанса, не в frontend bundle  
- CP API для m2m — отдельный префикс, rate limit per instance  
- Тело 5xx: stack/message + версия / request id; **не** слать PII каталога и credentials маркетплейсов по умолчанию  
- SSO code не логировать в plaintext  
- При `suspended` / `purged` CP отклоняет m2m и SSO  

### 9.5 Эволюция поддержки

| Этап | UX |
|------|-----|
| MVP | Тикеты в Customer Console; из инстанса — SSO deep-link «открыть поддержку» |
| Next | Виджет/панель поддержки **внутри** инстанса (тот же ticket API CP, сессия через SSO) |
| Later | Богатый контекст тикета: версия, последние 5xx, adapter errors (через Platform Link) |

---

## 10. Backup & DR (два контура)

| | Control Plane (Ops) | Внутри инстанса (клиент) |
|--|---------------------|---------------------------|
| Цель | Авария ноды, миграция, DR | Пользовательский dump каталога |
| Кто | Только Ops Console | Пользователь Delegat |
| Что | MySQL + MinIO по расписанию, encrypted, retention | Отдельный дизайн в продукте инстанса |
| Restore | Ops (in-place или новый стек) | Не через control plane |

В документации и саппорте всегда указывать, о каком контуре речь.

---

## 11. Поддержка: тикеты + Telegram

### 11.1 Рекомендованная модель (зафиксирована)

**Источник истины — тикет в БД control plane.**  
Telegram — канал для ops: уведомление о новом сообщении / тикете и возможность ответить так, чтобы ответ попал в тред тикета.

**MVP delivery:** бот пишет в **личку** одному ops (простота). Shared-группа / несколько операторов — когда нагрузка вырастет.

```
Клиент (Customer Console)          Ops
─────────────────────────          ───
Создаёт тикет (+ instance_id)  →   Бот: «#123 …» + deep-link в Ops
Пишет в тред                   →   Бот: текст / вложение
Видит ответы в UI              ←   Ответ в Ops UI или reply в боте → в тикет
Статусы: open / waiting / …        Меняются в Ops (или командой бота)
```

### 11.2 Почему не «чат = зеркало Telegram»

Полноценный realtime-чат с двусторонней синхронизацией (прочитано, несколько операторов, гонки, вложения) по объёму близок к Intercom. Для старта тикетный UX честнее по SLA и проще в поддержке.

### 11.3 Docs и тикеты

Вместо комментариев к статьям в MVP: CTA «статья не помогла → открыть тикет» с `article_id` / URL в контексте тикета.

Из **инстанса** тот же поток предпочтительно через Platform Link SSO (§9), а не вторую регистрацию.

---

## 12. Документация для пользователей

### 12.1 Выбор движка: GitBook

Сравнение коротко (критерий: «проще наполнить + видео + меньше сопровождения»):

| Вариант | Плюсы | Минусы |
|---------|-------|--------|
| **GitBook** ✅ | Визуальный редактор, вставка видео URL (YouTube и т.п.) в один клик, хостинг из коробки, поиск, версии | SaaS-подписка; видео лучше хостить снаружи (не грузить большие MP4 в GitBook) |
| Mintlify | Красивый DX, docs-as-code (MDX), быстрый сайт | Больше «разработческий» workflow; видео через iframe/MDX |
| VitePress (self-host) | Бесплатно, стек Vue | Сами деплой/поиск/версии/авторский UX — против цели «только наполнение» |

**Решение:** использовать **GitBook**. Видео-гайды — на Rutube/YouTube (или аналог) + embed в статью.  
В Customer Console и UI инстанса — пункт «Документация» с deep-link на GitBook space.  
Комментарии к статьям — **не MVP**; CTA «не помогло → тикет» с URL статьи.

SSO в GitBook — опционально later (§13). Platform Link SSO (§9) относится к **Customer Console / поддержке**, не к GitBook.

---

## 13. Open questions

Закрыто в §2: Т-Банк, trial 7 дней, stop→purge 2 мес., force password change, GitBook, Telegram DM (MVP), отдельный monorepo control plane, **Platform Link** (m2m + user SSO).

Ещё открыто:

1. **Trial + карта:** нужна ли привязка карты / холд на старте 7-дневного trial (ограничения Т-Банк + антифрод).  
2. **Grace до stop:** сколько дней `past_due` с баннером до остановки контейнеров; совпадает ли отмена в конце периода с тем же stop→purge.  
3. **DNS / TLS multi-node:** один edge (Host-header) vs per-node wildcards ([saas-architecture.md](saas-architecture.md) §2).  
4. **Email:** transactional-провайдер для РФ (welcome, счета, suspend, purge warning).  
5. **GitBook SSO** позже или достаточно публичных docs.  
6. **Telegram:** команды бота (`/reply`, смена статуса) vs ответ только из Ops UI; позже — группа вместо лички.  
7. **Feature request:** внутренний board vs публичный roadmap.  
8. **License / anti-abuse** для образов (нужно ли при закрытом provision).  
9. **Юридика:** оферта, 152-ФЗ, политика данных; предупреждения клиенту за N дней до purge.  
10. **Имя/URL** control-plane monorepo и домены (`console.` / `docs.` / лендинг).  
11. **Platform Link crypto:** HMAC vs instance JWT; нужен ли mutual TLS; политика ротации secret.  
12. **SSO identity:** только admin email = Account, или сразу все пользователи инстанса с match по email?  
13. **Catalog cache TTL** и стратегия инвалидации (ETag / `If-None-Match` / push от CP).  
14. **Plugin artifacts:** где хранить пакеты (GHCR private vs blob на CP); кто подписывает артефакт.  
15. **Кто редактирует справочники на CP:** только Ops, или контент-роль без полного Ops доступа?

Закрыто по смыслу «сервер для заполнения»: это **Reference Catalog** (§9.1.1) — валюты, timezones, реестр плагинов и аналогичные глобальные списки.

---

## 14. Roadmap control plane (укрупнённо)

Связано с [saas-architecture.md](saas-architecture.md) §8; здесь — продуктовый срез.

| Этап | Содержание |
|------|------------|
| 0 | Инстанс готов к управлению снаружи (health, quotas, seed, GHCR) — ✅ |
| A | Registry DB + Provisioner + Ops: create instance вручную / semi-auto |
| B | Billing РФ + self-serve Customer Console (оплата → авто-provision) |
| B′ | **Platform Link:** credentials при provision; **Reference Catalog** (currencies, timezones, plugins); heartbeat; 5xx |
| C | Release approve + fleet update + health poller |
| D | Ops backups/restore + break-glass |
| E | Tickets + Telegram bridge + **SSO handoff из инстанса** |
| F | Docs (GitBook) + feature requests + полировка lifecycle/оферты |
| G | Виджет поддержки внутри инстанса; canary; multi-instance org; auto-scale нод |

---

## 15. Связь с документацией инстанса

| Документ | Роль |
|----------|------|
| [capabilities.md](../platform/capabilities.md) | Что умеет **инстанс** |
| [product.md](../platform/product.md) | UX/бренд **инстанса** (каталог) |
| [saas-architecture.md](saas-architecture.md) | Ноды, RAM, compose, квоты, черновик Registry/Provisioner |
| **Этот файл** | Продукт и решения **control plane** (продажа, контроль, деплой, Platform Link) |

**Репозитории:**

| Repo | Содержимое |
|------|------------|
| `delegat-module` (этот) | Платформа инстанса + design docs control plane (+ будущий `PlatformLinkClient`) |
| **control-plane monorepo** (отдельный) | Customer Console, Ops Console, Provisioner, billing Т-Банк, tickets, Registry DB, **Platform Link API** |

В control-plane monorepo по мере роста имеет смысл разнести:

```
docs/control-plane.md          ← обзор (копия/канон)
docs/platform-link.md          ← m2m + SSO + Reference Catalog контракт
docs/billing.md
docs/instance-lifecycle.md
docs/support-tickets.md
```

---

## 16. Исходный backlog (из brainstorm)

Сохранённый список идей основателя — покрыт доменами выше:

1. Лендинговая страница  
2. Оплата / продление / отмена тарифа на развёртывание  
3. Автодеплой последней **подтверждённой** ревизии  
4. Обновление всех или выбранных инстансов  
5. Статус развёртывания и активности + протокол сброса суперадмина  
6. Дамп БД и S3 + откат (**ops**; пользовательский dump — в инстансе)  
7. Документация и видео-гайды (готовый движок) + обратная связь через тикеты  
8. Ticket-based поддержка (+ Telegram для ops)  
9. Форма заказа доработок / смена тарифа (два потока: feature request и plan change)
10. **Platform Link:** инстанс ↔ control plane — Reference Catalog (валюты, timezones, плагины), 5xx, SSO в поддержку
