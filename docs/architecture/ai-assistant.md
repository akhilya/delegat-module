# AI Assistant — Architecture & Maintenance

> **Maintenance rule:** Any time you add, remove, or rename tools in `AiChatService.cs`,
> you MUST also update:
> 1. The tool list in `AiPrompts.ChatSystem` (`backend/Services/Ai/Prompts/AiPrompts.cs`)
> 2. The **Tool Coverage** section in this document

---

## Overview

The AI assistant is a Claude-powered agent embedded in the platform. It can execute all
non-security-sensitive backend operations via tool calls, with a mandatory confirmation step
before any mutation.

Key files:

| File | Purpose |
|------|---------|
| `backend/Services/Ai/Prompts/AiPrompts.cs` | **All prompts** — edit here to tune behavior |
| `backend/Services/Ai/AiChatService.cs` | Main agentic loop + all tool implementations |
| `backend/Services/Ai/AiMappingService.cs` | AI auto-mapping logic (non-chat) |
| `backend/Services/Ai/GraphMutationService.cs` | Programmatic graphJson mutations |
| `backend/Services/Ai/AiApiClient.cs` | HTTP client (Anthropic + OpenAI-compatible SSE) |
| `backend/Services/Ai/AiModelConfigService.cs` | Admin model CRUD, key encryption |
| `backend/Services/Ai/AiOperationService.cs` | Operation history + rollback snapshots |
| `backend/Controllers/AiController.cs` | `/api/ai/chat`, `/api/ai/mapping-suggest/{id}`, `/api/ai/explain-error`, `/api/ai/operations` |
| `backend/Controllers/AiSettingsController.cs` | Admin `/api/ai/models` CRUD |
| `frontend/src/components/ai/AiChatPanel.vue` | Chat UI (SSE consumer) |
| `frontend/src/components/ai/AiPlanCard.vue` | Confirmation plan card |
| `frontend/src/components/ai/AiErrorExplainer.vue` | Inline error explanation |
| `frontend/src/store/modules/aiChatModule.js` | Vuex chat state (SSE parser) |
| `frontend/src/store/modules/aiModelsModule.js` | Vuex admin model API |
| `frontend/src/pages/settings/AiModelsPage.vue` | Admin model management page |

---

## Prompt Files

All prompt strings live in `backend/Services/Ai/Prompts/AiPrompts.cs`.
Edit that file to change AI behavior without touching service logic.

| Prompt | Method/Const | Used By |
|--------|-------------|---------|
| Chat system prompt | `AiPrompts.ChatSystem(storeName, count, conns)` | `AiChatService.StreamChatAsync` |
| Explain error system | `AiPrompts.ExplainErrorSystem` | `AiChatService.ExplainErrorAsync` |
| Explain error user message | `AiPrompts.ExplainErrorUser(error, context)` | `AiChatService.ExplainErrorAsync` |
| Mapping system | `AiPrompts.MappingSystem` | `AiMappingService.GenerateSuggestionsAsync` |
| Mapping rules | `AiPrompts.MappingRules` | `AiMappingService.BuildMappingPrompt` |
| Mapping response schema | `AiPrompts.MappingResponseSchema` | `AiMappingService.BuildMappingPrompt` |

---

## Model Management

- Models stored in `aiModelConfigs` table (encrypted API key via AES-256)
- Admin-only CRUD: `GET/POST/PUT/DELETE /api/ai/models`
- `POST /api/ai/models/{id}/set-default` — sets default model
- `POST /api/ai/models/{id}/test` — sends a minimal request to verify connection
- Supports **Anthropic** (Messages API) and **OpenAI-compatible** (Chat Completions) providers
- Active model selected per-request; falls back to first available if no default set

**Access control:** AI features are only available when `AI_ENABLED=true` is set as an env var.
The quota is enforced in `AiController` (see `AI_ENABLED` env var check).
Admin can configure models regardless of this flag.

---

## Chat Flow

```
POST /api/ai/chat → AiController → AiChatService.StreamChatAsync
  ↓
SSE stream: text | tool | plan | error | done

Agentic loop (max 20 iterations):
  1. Send messages + tools → AiApiClient.StreamAsync
  2. Collect text deltas → emit SSE "text" events
  3. Collect tool_use blocks
  4. For each tool:
     a. "propose_plan" → emit SSE "plan" event → break loop (await user confirm)
     b. Other tool → emit "tool running" → DispatchToolAsync → emit "tool done"
  5. Append tool results as user message → loop
  6. No tools used → break loop → emit "done"
```

**SSE event format:**
```json
data: {"type": "text",  "data": {"delta": "..."}}
data: {"type": "tool",  "data": {"name": "list_products", "status": "running|done", "summary": "..."}}
data: {"type": "plan",  "data": {"summary": "...", "steps": [...], "affectedCount": 5, "isDestructive": false}}
data: {"type": "error", "data": {"message": "..."}}
data: {"type": "done",  "data": {}}
```

**Plan confirmation:** after `propose_plan`, client sends next message `"Подтверждаю, выполняй"` → agentic loop resumes.

---

## Tool Coverage

> Update this table when adding/removing tools in `AiChatService.cs`.

### Products
| Tool | Description |
|------|-------------|
| `list_products` | List/filter products (search, category, price range, has images, missing field) |
| `get_products_count` | Count products matching filters |
| `get_product` | Get full product details with attribute values |
| `create_product` | Create a new product |
| `update_product` | Update product fields (name, price, description, barcode, quantity, category) |
| `update_product_sku` | Update product SKU |
| `update_product_prices` | Bulk price update (add/subtract/set/multiply) |
| `update_product_attribute` | Set attribute value on multiple products |
| `delete_products` | Soft-delete products |
| `restore_products` | Restore soft-deleted products |
| `list_trash` | List soft-deleted products |

### Categories
| Tool | Description |
|------|-------------|
| `list_categories` | List all store categories |
| `get_category` | Get category with attributes |
| `create_category` | Create a category |
| `update_category` | Update category name/description |
| `delete_category` | Delete a category |
| `list_category_fields` | List category attributes |
| `add_category_field` | Add attribute to category |
| `update_category_field` | Update attribute name/required flag |
| `delete_category_field` | Delete an attribute |
| `find_categories_with_field` | Find categories having attribute matching name |

### Store
| Tool | Description |
|------|-------------|
| `list_store_attributes` | List store-level attributes |

### Connections
| Tool | Description |
|------|-------------|
| `list_connections` | List marketplace connections |
| `get_connection` | Get connection details |
| `test_connection` | Test adapter connection |
| `get_connection_limits` | Get API rate limit/quota info |

### Mappings
| Tool | Description |
|------|-------------|
| `list_mappings` | List category mappings (filter by connection) |
| `get_mapping` | Get mapping details + coverage stats |
| `create_mapping` | Create mapping + AI auto-map fields |
| `update_mapping_name` | Rename a mapping |
| `delete_mapping` | Delete a mapping |
| `duplicate_mapping` | Copy a mapping with its graphJson |
| `list_external_categories` | Get marketplace categories from adapter |
| `get_external_fields` | Get adapter fields for a marketplace category |
| `get_available_sources` | Get product fields + category/store attributes for a mapping |
| `get_mapping_coverage` | Analyze mapped vs. unmapped fields |
| `preview_mapping_for_product` | Note: redirects user to UI editor |
| `generate_mapping_suggestions` | Run AI auto-mapping on existing mapping |
| `connect_field` | Connect source (productField/attribute/storeAttribute) to adapter field |
| `set_field_static_value` | Set a constant value for an adapter field |
| `add_field_modifier` | Add transformation modifier to field chain |
| `remove_field_connection` | Disconnect adapter field from source |

### Export / Import
| Tool | Description |
|------|-------------|
| `export_products_batch` | Queue products for export; returns batchId |
| `export_prices` | Trigger price export |
| `export_stocks` | Trigger stock export |
| `get_batch_status` | Check export batch status |
| `list_product_links` | List product ↔ marketplace sync links |

### Groups
| Tool | Description |
|------|-------------|
| `list_groups` | List product groups |
| `create_group` | Create group (with optional initial products) |
| `add_products_to_group` | Add products to existing group |

### Analytics
| Tool | Description |
|------|-------------|
| `get_store_stats` | Overall store statistics |
| `find_products_without_images` | Products with no images |
| `find_products_without_barcode` | Products with no barcode |
| `find_products_missing_field` | Products missing specific attribute value |
| `find_products_with_sync_errors` | Products with SyncError status |
| `analyze_mapping_quality` | Coverage report across all mappings |
| `get_image_download_status` | Image download queue status |

### Special
| Tool | Description |
|------|-------------|
| `propose_plan` | Show plan to user before any mutation; pauses loop |
| `rollback_last_operation` | Undo last AI operation (or specific operationId) |

---

## Auto-Mapping (AiMappingService)

Triggered automatically on `create_mapping` tool call and via `POST /api/ai/mapping-suggest/{id}`.

**Algorithm:**
1. Load local category attributes from DB
2. Load 2 example mappings for the same adapter (for context)
3. Build structured prompt: standard product fields + local attrs + adapter fields + examples
4. Send to Claude; parse JSON response
5. Filter suggestions by `confidence ≥ 0.4`
6. Apply non-conflicting suggestions via `GraphMutationService.ApplySuggestionsAsync`
7. Return `{ connected, skipped }`

**Source types:** `productField`, `attribute`, `storeAttribute`, `static`
**Modifier types:** `truncate`, `round`, `multiply`, `toText`, `toNumber`, `uppercase`, `lowercase`, `append`, `prepend`, `replace`

---

## Undo / Rollback

- Every mutation records an `AiOperation` with a `rollbackDataJson` snapshot
- Max 20 operations per user/store (auto-pruned, oldest removed first)
- `rollback_last_operation` tool reverses the most recent (or specific) operation

**Rollback data shapes by operation type:**

| operationType | Rollback data |
|--------------|--------------|
| `update_prices` | `{ products: [{id, oldPrice}] }` |
| `add_category_field` | `{ fieldId }` — deletes the field |
| `connect_field`, `set_static_value`, `add_modifier`, `disconnect_field`, `generate_mapping` | `{ mappingId, previousGraphJson }` |
| `update_attribute` | `{ products: [{productId, attributeId, oldValue}] }` |
| `create_product`, `export_*` | `canRollback: false` |

---

## Excluded from AI (security)

- Auth (login, registration, password change)
- Creating/deleting marketplace connections (credentials management)
- User management
- API/invitation tokens
- Plugins management
- File share tokens
- Permanent (hard) delete of products

---

## Frontend Integration Points

| Location | Feature |
|----------|---------|
| `app.vue` | Floating AI button + `<AiChatPanel />` (all non-public pages) |
| `fieldMappingPage.vue` | "✦ AI: улучшить маппинг" button → `POST /api/ai/mapping-suggest/{id}` |
| `connectionPage.vue` | "✦ Объяснить ошибку" inline on test connection failure |
| `settingsOverviewPage.vue` | "AI Модели" link → `/settings/admin/ai` |
| `/settings/admin/ai` | `AiModelsPage.vue` — admin model CRUD |
