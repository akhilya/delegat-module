# Delegat Control Plane — центральная платформа

> Продуктово-архитектурный документ **control plane**: продажа, контроль и развёртывание
> изолированных инстансов Delegat.Module.
>
> Инфраструктурные детали нод, квот и compose-шаблона: [saas-architecture.md](saas-architecture.md)  
> Возможности самого инстанса: [../platform/capabilities.md](../platform/capabilities.md)  
> Продуктовый brief инстанса (UI каталога): [../platform/product.md](../platform/product.md)
>
> **Статус:** дизайн / pre-implementation. Код control plane в репозитории ещё не реализован.
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
| 4 | Документация | UX «часть продукта» (пункт в консоли, deep-link из инстанса), **движок контента — готовый** (GitBook / Mintlify / аналог). Самописный docs CMS не делать |
| 5 | Поддержка | **Свой ticket-модуль** + Telegram как **ops-канал** (уведомления и reply в тикет). Не строить полноценный bidirectional «чат ↔ Telegram» как Intercom в MVP |
| 6 | Рынок / платежи | Старт — **Россия**. Провайдер: **Т-Банк / ЮMoney (и аналоги)** — конкретный выбор TBD |
| 7 | Релизы | Новые инстансы и обновления флота идут с **последней подтверждённой в Ops ревизии**, не с «сырого» `latest` из CI |

---

## 3. Четыре поверхности продукта

| Поверхность | Для кого | Содержание |
|-------------|----------|------------|
| **Marketing** | Публика | Лендинг, pricing, оферта, CTA на регистрацию |
| **Customer Console** | Покупатель | Аккаунт, подписка, инстанс, тикеты, ссылка на docs, feature request / смена тарифа |
| **Ops Console** | Вы | Approve release, fleet update, nodes, health, break-glass, backups/restore, тикеты |
| **Knowledge + Support** | Все | Внешний docs-движок + ticket-модуль (+ Telegram bridge) |

Клиентский кабинет и Ops — **два UI**, один backend control plane (отдельный сервис/репозиторий от инстанса).

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

- Рынок старта: **Россия** → эквайринг/кошельки уровня **Т-Банк, ЮMoney** (и аналоги). Выбор провайдера — open question (§12).  
- Full automation: успешная оплата (webhook) → Provisioner.  
- Подписка с автопродлением; отмена прекращает продление, инстанс живёт до конца оплаченного периода (уточнить в оферте).

### 7.2 Состояния подписки (черновик)

```
trial? → active → past_due → grace → suspended → cancelled → purged
```

На каждом шаге нужно явно определить поведение **инстанса** (баннер / read-only / stop containers / wipe volumes). Детализация — в следующих итерациях brainstorm / `billing.md`.

### 7.3 Тарифы ↔ квоты инстанса

Инстанс уже читает лимиты из env (`MAX_STORAGE_GB`, `MAX_PRODUCTS`, `MAX_CONNECTIONS`, `AI_ENABLED`).  
Control plane при provision и plan change выставляет их через Dokploy API и redeploy.

Ориентир планов: [saas-architecture.md](saas-architecture.md) §4.

---

## 8. Lifecycle инстанса

### 8.1 Статусы (черновик)

`provisioning` → `active` → `degraded` | `suspended` → `terminating` → `terminated`  
(+ `failed` на ошибке provision)

### 8.2 Provision flow (self-serve)

```
1. Оплата успешна (webhook провайдера)
2. Создать Organization + Subscription + Instance (status=provisioning)
3. Выбрать ноду (least-loaded)
4. Сгенерировать секреты, зашифровать в Registry
5. Dokploy: создать compose-проект из шаблона + env (квоты плана, IMAGE_TAG=stable release)
6. Poll https://{subdomain}/api/health (timeout ~5 min)
7. Welcome email: URL + admin + временный пароль
8. status=active
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

- Контент и видео-гайды — во **внешнем готовом** решении (поиск, версии, эмбеды видео из коробки).  
- В Customer Console и (опционально) в UI инстанса — пункт «Документация» с deep-link.  
- Комментарии к статьям — **не MVP**; обратная связь через тикеты.  
- Кандидаты движков: GitBook, Mintlify, ReadMe, Outline — выбор TBD (§12).

---

## 12. Open questions

Актуальные дыры для следующего brainstorm:

1. **Платёжный провайдер:** Т-Банк vs ЮMoney (vs другой РФ) — комиссии, рекуррент, юр. лицо, webhooks.  
2. **Trial:** есть ли пробный период и сколько дней; нужна ли карта сразу.  
3. **Поведение инстанса** на `past_due` / `grace` / `suspended` / после `cancelled`.  
4. **Обязательная смена** временного admin-пароля при первом входе.  
5. **DNS / TLS multi-node:** один edge (Host-header) vs per-node wildcards ([saas-architecture.md](saas-architecture.md) §2).  
6. **Email:** transactional-провайдер (и письма на русском).  
7. **Docs-движок:** какой именно + нужен ли SSO позже.  
8. **Telegram bridge:** личка одному ops vs shared channel/группа; команды бота.  
9. **Feature request:** внутренний board vs публичный roadmap.  
10. **License / anti-abuse** для образов (нужно ли при закрытом provision).  
11. **Юридика:** оферта, 152-ФЗ, политика данных, срок purge volumes после terminate.  
12. **Репозиторий control plane:** mono-repo vs отдельный git repo.

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

При реализации control plane рекомендуется выделить отдельные файлы по мере роста:

```
docs/architecture/control-plane.md     ← этот документ (обзор + решения)
docs/architecture/billing.md           ← позже
docs/architecture/instance-lifecycle.md
docs/architecture/support-tickets.md
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
