# Init prompt — delegat-module-platform

Скопируй блок ниже в **новый** чат Cursor, открытый в **пустой** папке проекта
(например `C:\Users\ARTEM\projects\delegat-module-platform`), не в `delegat-module`.

---

```text
Ты инициализируешь новый monorepo Control Plane для Delegat.

## Репозиторий
- Имя: delegat-module-platform (отдельный GitHub repo, НЕ внутри delegat-module)
- Рабочая папка: текущий workspace (пустой или почти пустой)
- Sibling instance repo: C:\Users\ARTEM\projects\dashboard
  — только читать/копировать docs и сверять Platform Link client (backend/Services/PlatformLink)

## Канон (прочитай целиком ДО кода)
1. Скопируй docs по MVP brief §0 (миграция):
   - docs/architecture/control-plane.md → docs/control-plane.md
   - docs/platform/platform-link.md → docs/platform-link.md
   - docs/plans/2026-07-21-control-plane-mvp-brief.md → docs/mvp-brief.md
   - Из docs/architecture/saas-architecture.md §7 → docs/provisioning.md
     (DDL: Account/Organization + Platform Link secret; домены module.delegat.pro;
      compose env: CONTROL_PLANE_URL, INSTANCE_ID, INSTANCE_LINK_SECRET)
2. docs/README.md с оглавлением. Канон CP теперь ЗДЕСЬ.
3. Заготовка docs/legal/ru/ и docs/legal/en/ (оферта, privacy, cookies) — draft markdown ok.
4. В конце напомни PR в instance-репо: stubs вместо control-plane.md / platform-link.md / brief
   (не два канона). capabilities/product/public-api/AI — НЕ переносить.

## Продукт
- CP: https://module.delegat.pro
- Instance: https://{label}.module.delegat.pro
- Docs (GitBook later): https://docs.module.delegat.pro
- Self-serve: payment succeeded → авто-provision через Dokploy
- Customer Console + Ops Console; .NET 8 + PostgreSQL; Vue 3 + актуальный shadcn-vue
- Platform Link server /platform/v1: catalog, heartbeat, errors, sso/exchange (HMAC = platform-link.md)
- Instance client уже есть в sibling — совместимость обязательна
- Subscription в БД с дня 1; Т-Банк/SMTP/Telegram — порты + log/no-op, live later
- Failed payment → сразу stop; purge через 2 месяца
- SSO: только admin Account; Organization + invites в модели сразу
- Plugins: PluginVersion.artifactUrl (GHCR)
- IInstanceProvisioner → DokployProvisioner (абстракция обязательна)

## i18n (обязательно с первого scaffold)
- Русский + English на всём UI CP (лендинг, auth, Customer, Ops)
- vue-i18n; переключатель языка; default locale = ru; persist locale
- Никаких захардкоженных user-facing строк без ключей i18n
- Ориентир реализации: frontend instance-репо (vue-i18n + locales/en.json, ru.json)

## Legal documents (обязательно с первого scaffold)
Страницы (публичные, bilocalized):
  - Terms / Оферта
  - Privacy Policy / Политика конфиденциальности (152-ФЗ)
  - Cookies policy
Показ:
  - Футер: landing + auth (sign-in/sign-up) + Customer Console (и Ops если публичные ссылки уместны)
  - Регистрация: обязательные чекбоксы «оферта» + «privacy»; без них submit disabled; ссылки на актуальные страницы
Каркас маршрутов + футер + чекбоксы + i18n-ключи — обязателен.
Текст документов может быть draft/placeholder; финальный юр. текст — отдельный таск.

## Первый deliverable этой сессии
1. Git init + корневой README
2. Структура: backend/ (.NET 8), frontend/ (Vue + shadcn-vue), docs/ (скопированные + legal drafts)
3. API host + Postgres connection + /health
4. Frontend: Customer + Ops shells; vue-i18n ru/en + language switcher
5. Auth pages stub: sign-up с чекбоксами; AppFooter со ссылками на legal
6. Routes: /legal/terms, /legal/privacy, /legal/cookies (или locale-aware эквивалент) — draft content
7. Порты: IEmailSender, IPaymentProvider, ITelegramNotifier, IInstanceProvisioner
8. НЕ реализуй весь Dokploy/Provisioner в этом PR — каркас + docs + i18n/legal + порты

## Ограничения
- Не коммить секреты. Не клади код CP в instance-репо.
- Не меняй DNS/stack без записи в docs/mvp-brief.md.
- Ответы пользователю — по-русски; код/идентификаторы — English.
- После scaffold: next steps по docs/mvp-brief.md §9.

Начни с копирования docs (+ legal stubs) и README, затем scaffold backend+frontend с i18n и футером.
```
