# Control Plane MVP Brief (decisions 2026-07-21)

Канон для старта monorepo **`delegat-module-platform`**.  
Продуктовый контекст: [control-plane.md](../architecture/control-plane.md) (после переноса - `docs/control-plane.md` в CP-репо).  
Контракт инстанса: [platform-link.md](../platform/platform-link.md) (после переноса - `docs/platform-link.md`).

---

## 0. Перенос документации в CP-репо

При создании `delegat-module-platform` документация Control Plane становится **каноном там**, не в `delegat-module`.

### Скопировать из `delegat-module` → `delegat-module-platform`

| Источник (instance repo) | Назначение (CP repo) |
|--------------------------|----------------------|
| `docs/architecture/control-plane.md` | `docs/control-plane.md` |
| `docs/platform/platform-link.md` | `docs/platform-link.md` |
| `docs/plans/2026-07-21-control-plane-mvp-brief.md` | `docs/mvp-brief.md` |
| Фрагмент Registry/Provisioner из `docs/architecture/saas-architecture.md` §7 | влить в `docs/provisioning.md` (обновить под Account/Org + Platform Link env) |

Опционально позже разнести: `docs/billing.md`, `docs/instance-lifecycle.md`, `docs/support-tickets.md`.

### Что сделать в `delegat-module` после копирования

1. **Не удалять сразу** - заменить `control-plane.md` и `platform-link.md` на короткие stubs (~15 строк): «канон переехал в `delegat-module-platform`», ссылка на GitHub + локальный path sibling если есть.  
2. Brief: stub или redirect на CP repo.  
3. В `docs/README.md` instance-репо - пометка *Control Plane docs → delegat-module-platform*.  
4. `saas-architecture.md` остаётся в instance-репо (ноды, RAM, квоты инстанса); в §7 - ссылка на CP `docs/provisioning.md`.  
5. Instance-specific docs (`capabilities.md`, `product.md`, `public-v1.md`, AI) **не** переносить.

### Порядок в git

1. Создать пустой GitHub repo `delegat-module-platform`.  
2. В новой папке: scaffold + скопированные docs + первый commit.  
3. Отдельным PR в `delegat-module`: stubs + README links (чтобы не было двух канонов).

---

## 1. Scope v0

| В scope | Позже / поэтапно |
|---------|------------------|
| Customer Console + Ops Console (один backend) | Пользовательский custom DNS |
| Self-serve: **успешная оплата → авто-provision в Dokploy** | Полная интеграция Т-Банк (подключить когда готовы; модель подписки в БД уже есть) |
| Registry + Provisioner (Dokploy API реально) | Смена оркестратора (K8s и т.п.) - интерфейс Provisioner абстрагировать |
| Platform Link API: catalog, heartbeat, errors, **SSO exchange** | SSO для non-admin users инстанса |
| Subscription-модель в БД (без обязательного live-эквайринга в день 1) | Внешние SaaS по мере надобности |
| Tickets + Telegram DM (порт notifier) | Команды бота / группа; live bot token |
| Break-glass + backup/restore в Ops UI | — |
| Approve release + fleet image tag | Canary / multi-org |
| Reference catalog: currencies, timezones, plugins (+ `artifactUrl`) | UI-редактор справочников |
| **Фундамент** под SMTP, Т-Банк, Telegram (порты + no-op/log adapters) | Подключение живых внешних сервисов |
| **i18n RU/EN** на всём UI CP (лендинг, Customer, Ops, auth) | — |
| **Юридические страницы** + согласия при регистрации + ссылки в футере | Финальный текст оферты/152-ФЗ с юристом |

**Не путать:** «payment succeeded» на первом этапе может быть stub/Ops-кнопка; тот же код-путь, что и webhook Т-Банка later. Живая почта/эквайринг/Telegram - не блокер acceptance, но вызовы только через порты (`IEmailSender`, payment port, `ITelegramNotifier`).

---

## 2. Naming & DNS

| Что | Значение |
|-----|----------|
| Monorepo | `delegat-module-platform` |
| Control Plane UI/API | `https://module.delegat.pro` |
| Instance | `https://{label}.module.delegat.pro` |
| Docs (GitBook) | `https://docs.module.delegat.pro` |

`{label}` - уникальный лейбл инстанса (subdomain). Custom DNS клиентов - later.

TLS: wildcard `*.module.delegat.pro` (+ `module` / `docs`).

Env при provision (обязательно в compose-шаблоне):

```text
CONTROL_PLANE_URL=https://module.delegat.pro
INSTANCE_ID=inst_…
INSTANCE_LINK_SECRET=…
```

плюс MySQL/MinIO/JWT/quotas/admin seed (см. instance `docker-compose.prod.yml`).

---

## 3. Stack

- Backend: **.NET 8** + **PostgreSQL**
- Frontend: **Vue 3** + **актуальный shadcn-vue** (Customer + Ops)
- Оркестрация v0: **Dokploy API**
- Абстракция: `IInstanceProvisioner` (Dokploy сейчас; K8s/Swarm later без смены домена)

Dokploy выбран для v0: один compose-стек = один клиент, уже в плане. K8s - когда появится реальная боль масштаба.

---

## 3.1 Localization (обязательно)

- Языки: **Русский** и **English** на всём публичном и кабинетном UI CP (лендинг, регистрация/логин, Customer Console, Ops Console, ошибки, email-шаблоны когда появятся).
- Стек: **vue-i18n** (как на инстансе) + переключалка языка; persist `locale` (cookie/localStorage).
- Default для РФ-аудитории: **`ru`**; URL/locale strategy - зафиксировать в frontend README (prefix `/en` или query - на выбор, но единообразно).
- Контент юридических страниц и GitBook deep-links тоже с учётом locale (отдельные URL или якоря).
- Backend: user-facing ошибки/уведомления - ключи или `Accept-Language`; ops-логи на английском ok.

---

## 3.2 Legal documents (обязательно в продукте)

Нужны **страницы на CP** (не только GitBook), доступные без логина, bilocalized RU/EN:

| Документ | Назначение |
|----------|------------|
| **Оферта / Terms of Service** | Условия оказания услуг, подписка, stop/purge, ответственность |
| **Политика конфиденциальности** | 152-ФЗ, обработка ПДн |
| **Политика cookies** (краткая) | Если есть аналитика/сессии на маркетинге |
| **Согласие на обработку ПДн** | Текст для чекбокса регистрации (может быть секцией Privacy) |

Опционально later: SLA, реквизиты/оферта ИП-ООО как отдельная страница «Реквизиты».

### Где показываются

1. **Футер** лендинга, Customer Console, страниц auth (sign-in / sign-up): ссылки на все обязательные документы.  
2. **Регистрация:** обязательные чекбоксы «принимаю оферту» + «согласен с политикой конфиденциальности» (без них submit disabled). Ссылки открывают актуальные версии.  
3. Версионирование: хранить `slug` + `version` + `publishedAt` (таблица или markdown в репо `docs/legal/{locale}/…`); при смене оферты - возможность запросить повторное согласие later.

Текст-заглушка (lorem / draft) допустим в v0 scaffold; **маршруты, футер, чекбоксы и i18n-ключи - нет**. Живой юридический текст - отдельный контент-таск.

---

## 4. Platform Link (CP реализует сервер)

Префикс `/platform/v1`, HMAC - [platform-link.md](../platform/platform-link.md).  
Инстанс (`delegat-module`) уже имеет client + catalog sync + heartbeat - CP должен отвечать совместимо.

| Endpoint | v0 |
|----------|-----|
| `GET /catalog/currencies` | yes |
| `GET /catalog/timezones` | yes |
| `GET /catalog/plugins` | yes (+ `artifactUrl`, version) |
| `POST /heartbeat` | yes |
| `POST /errors` | yes |
| `POST /sso/exchange` | yes (admin Account) |

Справочники: **seed JSON/SQL в репо CP**.

### Плагины

1. CI плагина публикует артефакт в **private GHCR** → URL.  
2. CP `PluginVersion`: `key`, `version`, `artifactUrl`, `minPlatformVersion`, changelog.  
3. Инстанс: реестр → download → существующий install path.

---

## 5. Identity & orgs

- Admin email инстанса = Account в CP; SSO только для него.  
- Модель сразу: `Account` → `Organization` → memberships (invite).  
- Non-admin users инстанса не обязаны регистрироваться в CP; later invite → SSO в поддержку.

---

## 6. Billing / lifecycle

| Тема | Решение |
|------|---------|
| Trial | 7 дней; карта + холд (когда Т-Банк) |
| Failed payment | **сразу stop** (без grace) |
| После stop | purge через **2 месяца** |
| Subscription | в PostgreSQL с дня 1 |
| Эквайринг / SMTP / Telegram live | поэтапно; порты с дня 1 |

---

## 7. Ops / Support

- Tickets + Telegram DM (через порт)  
- Ops: health, instances, break-glass (`--reset-admin`), backup/restore  
- Release approve → provision/fleet используют `stable`

---

## 8. Acceptance

Обязательно (A B C D E G):

- [ ] Ops: логин, nodes, instances  
- [ ] Payment success (stub ok) → Dokploy → `{label}.module.delegat.pro/api/health` ok  
- [ ] Platform Link catalog → UI инстанса  
- [ ] Heartbeat в Ops  
- [ ] 5xx в Ops  
- [ ] Approve release + image tag / fleet update  

Также обязательно по продукту (можно параллельно со scaffold UI):

- [ ] UI CP полностью на **ru + en** (переключатель работает)  
- [ ] Страницы оферты / privacy / cookies (черновик текста ok)  
- [ ] Футер со ссылками на документы (landing + auth + console)  
- [ ] Регистрация: чекбоксы согласия, без них нельзя создать аккаунт  

Фундамент внешних сервисов (не блокер live-ключей):

- [ ] `IEmailSender` (log default; SMTP adapter)  
- [ ] Payment provider port  
- [ ] Telegram notifier port  

---

## 9. Порядок работ

1. Repo + docs migration (§0).  
2. Skeleton .NET + Postgres + Vue/shadcn (Customer + Ops) + **vue-i18n ru/en**.  
3. Auth shell: sign-up с чекбоксами + **footer legal links** + legal page routes (draft text ok).  
4. DDL: Account, Organization, Node, Instance (+ link secret), Plan, Subscription, Release, PluginVersion, Ticket.  
5. `/platform/v1/*` + seed catalogs.  
6. `DokployProvisioner` + compose template + Platform Link env.  
7. Customer register/login + payment stub → provision.  
8. Ops: health, releases, break-glass, backup hooks.  
9. Tickets + notifier ports.  
10. Plugin CI → `artifactUrl`.

Sibling instance repo: `delegat-module` (уже с PlatformLinkClient).
