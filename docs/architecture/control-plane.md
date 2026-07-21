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
[Customer Console] ──── billing ────► [Payment provider RF]
        │                                    │
        │ successful payment                 │ webhooks
        ▼                                    ▼
[Provisioner] ── Dokploy API ──► [Node] ──► Instance stack
        │
        ▼
[Ops Console] — releases, fleet update, backups, tickets, nodes
```

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

## 9. Backup & DR (два контура)

| | Control Plane (Ops) | Внутри инстанса (клиент) |
|--|---------------------|---------------------------|
| Цель | Авария ноды, миграция, DR | Пользовательский dump каталога |
| Кто | Только Ops Console | Пользователь Delegat |
| Что | MySQL + MinIO по расписанию, encrypted, retention | Отдельный дизайн в продукте инстанса |
| Restore | Ops (in-place или новый стек) | Не через control plane |

В документации и саппорте всегда указывать, о каком контуре речь.

---

## 10. Поддержка: тикеты + Telegram

### 10.1 Рекомендованная модель (зафиксирована)

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

### 10.2 Почему не «чат = зеркало Telegram»

Полноценный realtime-чат с двусторонней синхронизацией (прочитано, несколько операторов, гонки, вложения) по объёму близок к Intercom. Для старта тикетный UX честнее по SLA и проще в поддержке.

### 10.3 Docs и тикеты

Вместо комментариев к статьям в MVP: CTA «статья не помогла → открыть тикет» с `article_id` / URL в контексте тикета.

---

## 11. Документация для пользователей

### 11.1 Выбор движка: GitBook

Сравнение коротко (критерий: «проще наполнить + видео + меньше сопровождения»):

| Вариант | Плюсы | Минусы |
|---------|-------|--------|
| **GitBook** ✅ | Визуальный редактор, вставка видео URL (YouTube и т.п.) в один клик, хостинг из коробки, поиск, версии | SaaS-подписка; видео лучше хостить снаружи (не грузить большие MP4 в GitBook) |
| Mintlify | Красивый DX, docs-as-code (MDX), быстрый сайт | Больше «разработческий» workflow; видео через iframe/MDX |
| VitePress (self-host) | Бесплатно, стек Vue | Сами деплой/поиск/версии/авторский UX — против цели «только наполнение» |

**Решение:** использовать **GitBook**. Видео-гайды — на Rutube/YouTube (или аналог) + embed в статью.  
В Customer Console и UI инстанса — пункт «Документация» с deep-link на GitBook space.  
Комментарии к статьям — **не MVP**; CTA «не помогло → тикет» с URL статьи.

SSO в GitBook — опционально later (§12).

---

## 12. Open questions

Закрыто в §2: Т-Банк, trial 7 дней, stop→purge 2 мес., force password change, GitBook, Telegram DM (MVP), отдельный monorepo control plane.

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

---

## 13. Roadmap control plane (укрупнённо)

Связано с [saas-architecture.md](saas-architecture.md) §8; здесь — продуктовый срез.

| Этап | Содержание |
|------|------------|
| 0 | Инстанс готов к управлению снаружи (health, quotas, seed, GHCR) — ✅ |
| A | Registry DB + Provisioner + Ops: create instance вручную / semi-auto |
| B | Billing РФ + self-serve Customer Console (оплата → авто-provision) |
| C | Release approve + fleet update + health poller |
| D | Ops backups/restore + break-glass |
| E | Tickets + Telegram bridge |
| F | Docs (внешний движок) + feature requests + полировка lifecycle/оферты |
| G | Canary, multi-instance org, auto-scale нод |

---

## 14. Связь с документацией инстанса

| Документ | Роль |
|----------|------|
| [capabilities.md](../platform/capabilities.md) | Что умеет **инстанс** |
| [product.md](../platform/product.md) | UX/бренд **инстанса** (каталог) |
| [saas-architecture.md](saas-architecture.md) | Ноды, RAM, compose, квоты, черновик Registry/Provisioner |
| **Этот файл** | Продукт и решения **control plane** (продажа, контроль, деплой) |

**Репозитории:**

| Repo | Содержимое |
|------|------------|
| `delegat-module` (этот) | Платформа инстанса + design docs control plane (этот файл как источник правды до/параллельно с переносом) |
| **control-plane monorepo** (отдельный) | Customer Console, Ops Console, Provisioner, billing Т-Банк, tickets, Registry DB |

В control-plane monorepo по мере роста имеет смысл разнести:

```
docs/control-plane.md          ← обзор (копия/канон)
docs/billing.md
docs/instance-lifecycle.md
docs/support-tickets.md
```

---

## 15. Исходный backlog (из brainstorm)

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
