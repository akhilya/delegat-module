# AI-система - полная документация

> Документ охватывает всё: архитектуру, хранение диалогов, инструменты, маппинг,
> прокси-поддержку, сбор данных для файнтюнинга и стратегию развития модели.
> Предназначен в том числе для использования в RAG-пайплайнах.

---

## 1. Обзор

Платформа использует AI-ассистента на базе больших языковых моделей (LLM) для управления
товарами, категориями, маппингами и экспортом на маркетплейсы.

**Принцип работы:** пользователь пишет текст → ассистент вызывает backend-инструменты (tool calls)
→ предлагает план (propose_plan) → пользователь подтверждает → система выполняет изменения
детерминированно, без повторного обращения к модели.

**Поддерживаемые провайдеры:**
- **Anthropic** (Claude) - через официальный `anthropic-sdk-csharp`, Messages API
- **OpenAI-compatible** - через официальный `openai-dotnet`, Chat Completions API
  (LM Studio, Ollama, vLLM, OpenAI, любой OpenAI-совместимый сервер)

---

## 2. Файловая структура

| Файл | Назначение |
|------|-----------|
| `backend/Services/Ai/Prompts/AiPrompts.cs` | Все системные промпты - редактировать только здесь |
| `backend/Services/Ai/AiChatService.cs` | Агентный цикл + реализация всех 84 инструментов |
| `backend/Services/Ai/AiApiClient.cs` | Транспорт: официальные SDK + маппинг в Anthropic-shaped события |
| `backend/Services/Ai/AiModelConfigService.cs` | CRUD моделей, шифрование ключей, discover |
| `backend/Services/Ai/AiMappingService.cs` | AI автомаппинг полей адаптера |
| `backend/Services/Ai/GraphMutationService.cs` | Программные мутации graphJson маппинга |
| `backend/Services/Ai/AiOperationService.cs` | История операций + снапшоты для rollback |
| `backend/Services/Ai/AiDtos.cs` | Request/Response DTO, SSE-типы |
| `backend/Controllers/AiController.cs` | `/api/ai/chat`, `/api/ai/mapping-suggest/{id}`, `/api/ai/explain-error` |
| `backend/Controllers/AiSettingsController.cs` | Админский CRUD `/api/ai/models` |
| `backend/Controllers/AiConversationController.cs` | CRUD истории диалогов |
| `backend/Models/Ai/AiModelConfig.cs` | Сущность модели (apiUrl, apiKey, proxyUrl, ...) |
| `backend/Models/Ai/AiConversation.cs` | Сущность диалога (storeId, userId, sessionId) |
| `backend/Models/Ai/AiConversationMessage.cs` | Сообщение диалога |
| `frontend/src/components/ai/AiChatPanel.vue` | Chat UI: три вида - чат / история / инструменты |
| `frontend/src/components/ai/AiPlanCard.vue` | Карточка подтверждения плана |
| `frontend/src/components/ai/AiErrorExplainer.vue` | Объяснение ошибок синхронизации |
| `frontend/src/store/modules/aiChatModule.js` | Vuex: состояние чата, SSE-парсер |
| `frontend/src/store/modules/aiModelsModule.js` | Vuex: CRUD AI-моделей |
| `frontend/src/pages/settings/AiModelsPage.vue` | Страница управления моделями (админ) |

---

## 3. Хранение диалогов

### 3.1 Схема БД

```sql
aiConversations (
  Id INT PK,
  sessionId VARCHAR(36) UNIQUE,   -- client UUID, stable per chat session
  storeId INT NOT NULL,           -- изоляция по магазину
  userId INT NOT NULL,            -- изоляция по пользователю
  title VARCHAR(200),
  modelName VARCHAR(200),
  createdAt DATETIME,
  lastMessageAt DATETIME,
  messageCount INT
)

aiConversationMessages (
  Id INT PK,
  conversationId INT FK CASCADE,
  role VARCHAR(20),               -- 'user' | 'assistant'
  content TEXT,
  toolCallsJson TEXT,             -- JSON массив вызовов инструментов
  feedbackRating TINYINT,         -- 1 (👍) / -1 (👎) / null
  createdAt DATETIME
)
```

Индексы: `(sessionId)` уникальный; `(storeId, userId)` составной.

### 3.2 Изоляция по магазину

**Критично:** все операции фильтруются по `storeId + userId`. При смене магазина:
- Фронтенд: `<AiChatPanel :key="appKey" />` - компонент перемонтируется, генерирует новый sessionId
- Бэкенд: `SaveMessages` ищет диалог по `sessionId + storeId + userId` (не только sessionId)

Без двойной фильтрации возможен "cross-store bleed" - сообщения из магазина A попадали бы в контекст магазина B.

### 3.3 API истории

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/ai/conversations` | Список диалогов текущего магазина/пользователя |
| GET | `/api/ai/conversations/{sessionId}` | Диалог со всеми сообщениями |
| GET | `/api/ai/conversations/latest` | Последний диалог для восстановления при открытии панели |
| PATCH | `/api/ai/conversations/{sessionId}` | Переименовать диалог |
| DELETE | `/api/ai/conversations/{sessionId}` | Удалить диалог |
| POST | `/api/ai/conversations/messages` | Сохранить сообщения (upsert по sessionId) |
| POST | `/api/ai/conversations/{sessionId}/generate-title` | AI-генерация заголовка |
| POST | `/api/ai/messages/{id}/feedback` | Поставить оценку сообщению |

---

## 4. Конфигурация моделей

### 4.1 Сущность AiModelConfig

| Поле | Тип | Описание |
|------|-----|---------|
| `name` | string(100) | Человекочитаемое имя |
| `apiUrl` | string(500) | Endpoint API |
| `apiKeyEncrypted` | string | AES-256 зашифрованный ключ |
| `modelId` | string(200) | Идентификатор модели (`claude-sonnet-4-6`, `gpt-4o`, ...) |
| `provider` | enum | `Anthropic` или `OpenAiCompatible` |
| `maxTokens` | int | Лимит токенов ответа (default 8192) |
| `isDefault` | bool | Используется по умолчанию |
| `proxyUrl` | string(500)? | HTTP или SOCKS5 прокси (опционально) |
| `createdAt` | DateTime | Дата создания |

### 4.2 Поддержка прокси

Поле `proxyUrl` принимает полный URI с опциональными credentials:

```
http://host:8080
http://user:password@host:8080
socks5://host:1080
socks5://user:password@host:1080
```

**Реализация** (`AiApiClient.CreateProxiedClient`):
- Использует `SocketsHttpHandler` - поддерживает SOCKS5 нативно в .NET 6+
- `WebProxy` не парсит credentials из URI автоматически → они извлекаются вручную и передаются через `WebProxy.Credentials = new NetworkCredential(user, pass)`
- Применяется для Anthropic SDK (`client.HttpClient = proxied`),
  OpenAI SDK (`options.Transport = new HttpClientPipelineTransport(proxied)`),
  и при discover моделей

### 4.3 Admin API

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/ai/models` | Список моделей (ключ маскирован) |
| POST | `/api/ai/models` | Создать модель |
| PUT | `/api/ai/models/{id}` | Обновить модель |
| DELETE | `/api/ai/models/{id}` | Удалить модель |
| POST | `/api/ai/models/{id}/set-default` | Назначить основной |
| POST | `/api/ai/models/{id}/test` | Проверить подключение |
| POST | `/api/ai/models/discover` | Получить список моделей от провайдера |

---

## 5. Поток чата (Chat Flow)

```
POST /api/ai/chat
  → AiController.StreamChat
  → AiChatService.StreamChatAsync(messages, storeId, userId, modelId, pageContext, sessionId)
  → AiApiClient.StreamAsync
  → SSE stream (text | tool | plan | error | done)

Агентный цикл (max 20 итераций):
  1. Отправить сообщения + инструменты → получить стрим
  2. Собрать text-дельты → emit SSE "text"
  3. Собрать tool_use блоки
  4. Для каждого tool:
     a. propose_plan → emit SSE "plan" → break (ждём подтверждения)
     b. Любой другой → emit "tool running" → DispatchToolAsync → emit "tool done"
  5. Добавить tool_results → продолжить цикл
  6. Нет tools → emit "done" → break
```

**SSE формат:**
```
data: {"type":"text",  "data":{"delta":"..."}}
data: {"type":"tool",  "data":{"name":"list_products","status":"running|done","summary":"..."}}
data: {"type":"plan",  "data":{"summary":"...","steps":[...],"affectedCount":5,"isDestructive":false}}
data: {"type":"error", "data":{"message":"..."}}
data: {"type":"done",  "data":{}}
```

---

## 6. Детерминированное выполнение планов

**Проблема с обычным подходом:** после подтверждения пользователем ассистент повторно вызывает
инструменты - мог предложить другой план или "забыть" ID полученные ранее.

**Решение:** каждый шаг `propose_plan` содержит `args` - точные аргументы инструмента.
После подтверждения фронтенд вызывает `POST /api/ai/execute-plan`, бэкенд выполняет шаги
через `ExecutePlanStreamAsync` **без обращения к модели** - только `ToolDispatcher.DispatchAsync`.

Преимущества:
- Нет повторных предложений плана
- Нет изобретения ID (берутся из args, собранных до предложения)
- Работает даже с маломощными локальными моделями

Fallback: если шаги без `args` - фронтенд отправляет "Подтверждаю" и цикл продолжается классически.

**Page-aware фильтрация не применяется** к `ExecutePlanStreamAsync` - планы всегда имеют доступ ко всем инструментам.

---

## 7. Инструменты - полный список (84 шт.)

### Товары
| Инструмент | Описание |
|-----------|---------|
| `list_products` | Список с фильтрами (поиск, категория, цена, наличие фото, пустые поля) |
| `get_products_count` | Количество по фильтру |
| `get_product` | Полные данные товара с атрибутами |
| `create_product` | Создать товар |
| `create_products` | Создать товары пачкой |
| `update_product` | Обновить поля (название, цена, описание, штрихкод, количество, категория) |
| `update_product_sku` | Изменить SKU |
| `update_product_prices` | Массовое изменение цен (add/subtract/set/multiply) |
| `update_product_attribute` | Задать значение атрибута для нескольких товаров |
| `update_product_attributes` | Несколько атрибутов для нескольких товаров одним вызовом |
| `delete_products` | Мягкое удаление |
| `restore_products` | Восстановить из корзины |
| `list_trash` | Корзина |
| `generate_barcodes` | Сгенерировать EAN-13 штрихкоды (с rollback) |

### Категории
| Инструмент | Описание |
|-----------|---------|
| `list_categories` | Список категорий |
| `get_category` | Категория с атрибутами |
| `create_category` | Создать |
| `update_category` | Переименовать |
| `delete_category` | Удалить |
| `list_category_fields` | Атрибуты категории |
| `add_category_field` | Добавить атрибут |
| `update_category_field` | Изменить атрибут |
| `delete_category_field` | Удалить атрибут |
| `find_categories_with_field` | Найти категории с атрибутом по имени |

### Маппинги
| Инструмент | Описание |
|-----------|---------|
| `list_mappings` | Список маппингов (фильтр по подключению) |
| `get_mapping` | Детали + покрытие |
| `create_mapping` | Создать маппинг (с опцией AI автозаполнения) |
| `update_mapping_name` | Переименовать |
| `delete_mapping` | Удалить |
| `duplicate_mapping` | Скопировать с graphJson |
| `list_external_categories` | Категории маркетплейса |
| `get_external_fields` | Поля адаптера (ключи, типы, ограничения, первые 15 опций Select) |
| `search_field_options` | Поиск в словаре опций поля (тысячи значений) |
| `get_available_sources` | Поля товаров + атрибуты категории/магазина для маппинга |
| `get_mapping_coverage` | Анализ покрытия (mapped vs unmapped) |
| `preview_mapping_for_product` | Перенаправляет в редактор |
| `generate_mapping_suggestions` | AI автомаппинг существующего маппинга |
| `connect_field` | Подключить источник к полю адаптера |
| `connect_fields` | Массовое подключение полей |
| `set_field_static_value` | Статическое значение для поля |
| `set_fields_static_value` | Статические значения пачкой |
| `add_field_modifier` | Добавить трансформацию (truncate/round/multiply/...) |
| `remove_field_connection` | Отключить поле |
| `merge_adapter_fields` | Объединить дубли полей (alias) |
| `unmerge_adapter_fields` | Разъединить alias |

### Подключения
| Инструмент | Описание |
|-----------|---------|
| `list_connections` | Список маркетплейс-подключений |
| `get_connection` | Детали подключения |
| `test_connection` | Проверить связь с адаптером |
| `get_connection_limits` | Лимиты и квоты API |
| `get_adapter_info` | Метаданные адаптера (возможности, схема группировки) |

### Экспорт
| Инструмент | Описание |
|-----------|---------|
| `export_products_batch` | Экспорт товаров (batch), возвращает batchId |
| `export_prices` | Экспорт цен |
| `export_stocks` | Экспорт остатков |
| `get_batch_status` | Статус задачи |
| `list_product_links` | Sync-ссылки товар ↔ маркетплейс |

### Импорт
| Инструмент | Описание |
|-----------|---------|
| `preview_marketplace_products` | Товары на маркетплейсе (с пометкой уже импортированных) |
| `import_products` | Импортировать в локальный каталог (batch) |
| `import_prices` | Импортировать цены с маркетплейса |
| `import_stocks` | Импортировать остатки |
| `archive_product_on_marketplace` | Архивировать товар на площадке |

### Группы
| Инструмент | Описание |
|-----------|---------|
| `list_groups` | Список групп |
| `get_group` | Группа + полный список товаров |
| `create_group` | Создать (с опциональным списком товаров) |
| `update_group` | Переименовать |
| `delete_group` | Удалить (товары остаются) |
| `add_products_to_group` | Добавить товары |
| `remove_products_from_group` | Убрать товары |

### Файлы
| Инструмент | Описание |
|-----------|---------|
| `list_files` | Список файлов/папок |
| `create_folder` | Создать папку |
| `rename_file` | Переименовать файл |
| `rename_folder` | Переименовать папку |
| `move_files` | Переместить файлы/папки |
| `delete_files` | Удалить файлы |
| `delete_folder` | Удалить папку (recursive) |
| `share_file` | Публичная ссылка (TTL 1ч-30д) |

### Аналитика
| Инструмент | Описание |
|-----------|---------|
| `get_store_stats` | Статистика магазина |
| `find_products_without_images` | Товары без изображений |
| `find_products_without_barcode` | Товары без штрихкода |
| `find_products_missing_field` | Товары с пустым атрибутом |
| `find_products_with_sync_errors` | Товары с ошибками синхронизации |
| `analyze_mapping_quality` | Покрытие по всем маппингам |
| `get_image_download_status` | Статус загрузки изображений |

### Магазин
| Инструмент | Описание |
|-----------|---------|
| `list_store_attributes` | Глобальные атрибуты магазина |

### Служебные
| Инструмент | Описание |
|-----------|---------|
| `propose_plan` | Показать план перед мутацией - обязателен для всех изменений |
| `rollback_last_operation` | Отменить последнюю (или конкретную) AI-операцию |
| `navigate_to` | Кнопка перехода на страницу в панели чата |

---

## 8. Page-aware фильтрация инструментов

`AiChatService.BuildToolDefinitions(pageContext)` урезает список инструментов по контексту страницы
(экономия 35-40% токенов):

| `pageContext.Page` | Получает | Исключено |
|--------------------|---------|---------|
| `"product"` | Товары, Категории (read), Магазин, Файлы, Группы, Экспорт, Аналитика, nav helpers | Маппинги (write), подключения (write) |
| `"fieldMapping"` | Маппинги (full), Товары (read), Категории (read), Подключения (read+info) | Экспорт/импорт, файлы, группы, аналитика |
| `"connection"` | Подключения, Маппинги (full), Экспорт/Импорт, Товары (read), Категории (read) | Файлы, группы, аналитика |
| `null` | Все инструменты | - |

---

## 9. AI автомаппинг полей

### 9.1 Алгоритм

Сервис: `AiMappingService.GenerateSuggestionsAsync`

1. Отфильтровать уже подключённые поля из graphJson (`IsFieldConnected`)
2. Разбить на батчи по 15 полей
3. Для каждого батча:
   - Собрать атрибуты локальной категории + 2 примера маппингов того же адаптера
   - Отправить промпт модели
   - Распарсить ответ, отфильтровать `confidence ≥ 0.4`
   - Применить через `GraphMutationService.ApplySuggestionsAsync`
4. Ошибка батча → предупреждение, пропустить (предыдущие батчи сохранены)

### 9.2 Запуск через UI и чат

- **Чат**: инструмент `generate_mapping_suggestions` → синхронно
- **При создании маппинга**: если `useAiMapping=true` - все поля создаются с пустым static value, AI заполняет

### 9.3 Флаг useAiMapping при создании

Если пользователь отмечает "Использовать AI" при создании маппинга:
- `DetermineAutoMapping()` (паттерн-матчинг по SKU/имени/цене) полностью пропускается
- Все поля получают `FieldMappingSourceType.Static` с пустым значением
- `GraphGenerator.BuildGraphJson` оставляет их unconnected (не создаёт ноду/ребро для empty-static)
- AI видит чистый граф и заполняет с нуля

### 9.4 Типы источников и модификаторов

**Источники:** `productField`, `attribute`, `storeAttribute`, `static`, `groupName`

**Модификаторы:** `truncate`, `round`, `multiply`, `toText`, `toNumber`, `uppercase`, `lowercase`, `append`, `prepend`, `replace`

---

## 10. Undo / Rollback

- Каждая мутирующая операция записывает `AiOperation` со снапшотом `rollbackDataJson`
- Максимум 20 операций на пользователя/магазин (старые удаляются автоматически)
- Инструмент `rollback_last_operation` откатывает последнюю или конкретную операцию по ID

| Тип операции | Данные rollback |
|-------------|----------------|
| `update_prices` | `{ products: [{id, oldPrice}] }` |
| `add_category_field` | `{ fieldId }` - поле удаляется |
| `connect_field`, `set_static_value`, `add_modifier`, `disconnect_field`, `generate_mapping` | `{ mappingId, previousGraphJson }` |
| `update_attribute` | `{ products: [{productId, attributeId, oldValue}] }` |
| `create_product`, `export_*` | `canRollback: false` |

---

## 11. Безопасность - что вне AI

AI-ассистент намеренно не имеет доступа к:

- Аутентификации (логин, пароль, токены)
- Созданию/удалению маркетплейс-подключений (credentials)
- Управлению пользователями
- API-токенам и инвайтам
- Управлению плагинами
- Токенам файловых шарингов
- Постоянному (hard) удалению товаров

---

## 12. Сбор данных и стратегия развития модели

### 12.1 Что уже собирается

На каждое сообщение ассистента пользователь может поставить 👍/👎 через `POST /api/ai/messages/{id}/feedback`.
Данные хранятся в `aiConversationMessages.feedbackRating` (1 / -1 / null).

Помимо оценок, в БД накапливаются:
- Полные диалоги (`aiConversations` + `aiConversationMessages`) с историей tool calls
- JSON вызовов инструментов (`toolCallsJson`) - точные аргументы и результаты
- История операций с rollback-данными (`aiOperations`)

### 12.2 Подготовка к файнтюнингу

**Цель файнтюнинга:** обучить меньшую, более дешёвую специализированную модель на паттернах
работы с платформой - понимать структуру маппингов, нейминг атрибутов, семантику инструментов.

**Данные которые стоит собирать целенаправленно:**

1. **Позитивные примеры** - диалоги где пользователь дал 👍 или выполнил confirm плана
2. **Маппинг-примеры** - примеры successful `generate_mapping_suggestions` с результатами
3. **Структурированные пары** - (контекст магазина + вопрос) → (план + вызовы инструментов)
4. **Негативные примеры** - где пользователь дал 👎 или rollback (для RLHF / DPO)

**Формат для экспорта (Anthropic fine-tuning):**
```json
{
  "messages": [
    {"role": "user", "content": "Обнови цены всех товаров категории 'Куртки' на +10%"},
    {"role": "assistant", "content": [
      {"type": "tool_use", "id": "t1", "name": "list_products",
       "input": {"categoryId": 42, "limit": 1000}},
      {"type": "tool_use", "id": "t2", "name": "propose_plan",
       "input": {"steps": [...]}}
    ]},
    {"role": "user", "content": [
      {"type": "tool_result", "tool_use_id": "t1", "content": "..."}
    ]}
  ]
}
```

### 12.3 Что нужно сделать для полноценного сбора данных

- [ ] Экспорт диалогов в JSONL/CSV для разметки (admin endpoint)
- [ ] Флаг `isTrainingCandidate` на `AiConversation` - автоматически помечать после 👍 или confirm плана
- [ ] Маскировка PII перед экспортом (названия магазинов, товаров → anonymize)
- [ ] Отдельная таблица `aiTrainingExamples` для выверенных примеров

### 12.4 Возможные направления специализации

**Вариант A: Специализация на маппинге**
Дообучить модель понимать семантику полей адаптера и предлагать правильные источники.
Данные: успешные `generate_mapping_suggestions` + 👍 фидбек на маппинг-действия.

**Вариант B: Специализация на планировании**
Научить модель всегда правильно структурировать `propose_plan` с точными args.
Данные: подтверждённые планы из `execute-plan`.

**Вариант C: Instruction tuning на платформу**
Общая специализация - понимание структуры магазина, категорий, маркетплейсов.
Данные: все диалоги с 👍 + выполненными операциями.

### 12.5 Рекомендации

- Начать с **prompt engineering** и примерами (few-shot) прежде чем переходить к файнтюнингу
- Для маппинг-задачи - хорошая база для RAG: примеры маппингов аналогичных адаптеров
- Файнтюнинг оправдан при объёме от ~500 качественных примеров на задачу
- Anthropic предоставляет fine-tuning API для Claude (проверить актуальность на момент внедрения)
- Альтернатива файнтюнингу - **extended thinking** для сложных задач маппинга

---

## 13. Фронтенд интеграция

### 13.1 Панель чата (AiChatPanel.vue)

Три вида, переключаемых кнопками в заголовке:

| Вид | Активируется | Содержимое |
|-----|-------------|-----------|
| `chat` | по умолчанию | Сообщения, поле ввода |
| `history` | иконка History | Список диалогов с датировкой |
| `tools` | иконка Wrench | 11 категорий инструментов с chip-тегами |

**Особенности:**
- Resize handle - ширина 300-660px, сохраняется в localStorage
- Escape: из history/tools → возврат в chat; из chat → закрытие
- Удаление диалога через AlertDialog с подтверждением
- `<AiChatPanel :key="appKey" />` - полный remount при смене магазина

### 13.2 Точки интеграции

| Место | Функциональность |
|-------|----------------|
| `app.vue` | Floating AI кнопка + `<AiChatPanel />` на всех не-публичных страницах |
| `fieldMappingPage.vue` | "✦ AI: улучшить маппинг" → `POST /api/ai/mapping-suggest/{id}` |
| `connectionPage.vue` | "✦ Объяснить ошибку" при ошибке подключения |
| `AiModelsPage.vue` | Админ-страница управления моделями с proxy-полем |

### 13.3 Включение AI

Для работы AI-чата необходима переменная окружения `AI_ENABLED=true`.
Управление моделями доступно администраторам независимо от этого флага.

---

## 14. Обслуживание документации

**Правило:** при добавлении/удалении/переименовании инструментов в `AiChatService.cs` обязательно:
1. Обновить список инструментов в `AiPrompts.ChatSystem` (`AiPrompts.cs`)
2. Обновить таблицу Tool Coverage в `docs/architecture/ai-assistant.md`
3. Обновить раздел 7 этого документа
4. Обновить `toolCategories` в `AiChatPanel.vue` (вид "Инструменты")
