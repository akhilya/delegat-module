# AI Assistant - Architecture & Maintenance

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
| `backend/Services/Ai/Prompts/AiPrompts.cs` | **All prompts** - edit here to tune behavior |
| `backend/Services/Ai/AiChatService.cs` | Main agentic loop + all tool implementations |
| `backend/Services/Ai/AiMappingService.cs` | AI auto-mapping logic (non-chat) |
| `backend/Services/Ai/GraphMutationService.cs` | Programmatic graphJson mutations |
| `backend/Services/Ai/AiApiClient.cs` | Transport via official SDKs (Anthropic C# SDK + OpenAI .NET SDK); maps to internal Anthropic-shaped events |
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
- `POST /api/ai/models/{id}/set-default` - sets default model
- `POST /api/ai/models/{id}/test` - sends a minimal request to verify connection (success = no exception, not content check)
- `POST /api/ai/models/discover` - queries `/v1/models` to list available model IDs from a provider
- Supports **Anthropic** (Messages API) and **OpenAI-compatible** (Chat Completions) providers
- Active model selected per-request; falls back to first available if no default set

**Proxy support:** each model can have an optional `proxyUrl` field (full URI format):
```
http://host:8080
http://user:password@host:8080
socks5://host:1080
socks5://user:password@host:1080
```
Uses `SocketsHttpHandler` (SOCKS5 native in .NET 6+). Credentials are manually extracted from
`Uri.UserInfo` and set via `WebProxy.Credentials` because `WebProxy` ignores embedded credentials.
Applied in `AiApiClient.CreateProxiedClient()` and threaded through `BuildOpenAiClient`,
`BuildAnthropicClient`, and `DiscoverModelsAsync`.

**Access control:** AI features are only available when `AI_ENABLED=true` is set as an env var
(read at process start). When disabled, AI services and controllers are not registered and the
frontend does not load the chat panel; changing the flag requires an instance restart.

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

**Plan confirmation (deterministic execution):** plan steps carry `args` - the exact tool
arguments. On confirm, when every step has `tool` + `args`, the frontend calls
`POST /api/ai/execute-plan` and the backend executes the steps directly via
`AiChatService.ExecutePlanStreamAsync` (same SSE tool events, final summary text) - **no model
round-trip**, which eliminates re-proposed plans and invented keys on weak local models.
`propose_plan`/`rollback_last_operation` are forbidden inside a plan. Fallback: steps without
`args` go through the legacy path - client sends `"Подтверждаю, выполняй"` and the agentic loop resumes.

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
| `create_products` | Bulk create products from a list |
| `update_product_attribute` | Set attribute value on multiple products |
| `update_product_attributes` | Bulk set multiple attributes on multiple products in one call |
| `delete_products` | Soft-delete products |
| `restore_products` | Restore soft-deleted products |
| `list_trash` | List soft-deleted products |
| `generate_barcodes` | Generate unique EAN-13 barcodes for products without one (rollbackable) |

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
| `get_adapter_info` | Adapter metadata: display name, description, capability flags, grouping scheme (field/endpoint + grouping field key) |

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
| `get_external_fields` | Get adapter fields (keys, labels, constraints, first 15 Select options) - by mappingId or connectionId+externalCategoryId |
| `search_field_options` | Server-side search in a Select field's option dictionary (handles thousands of values) |
| `get_available_sources` | Get product fields + category/store attributes for a mapping |
| `get_mapping_coverage` | Analyze mapped vs. unmapped fields |
| `preview_mapping_for_product` | Note: redirects user to UI editor |
| `generate_mapping_suggestions` | Run AI auto-mapping on existing mapping |
| `connect_field` | Connect source (productField/attribute/storeAttribute) to adapter field |
| `merge_adapter_fields` | Merge duplicate fields: aliases export the primary's value, import uses only the primary |
| `unmerge_adapter_fields` | Detach alias fields from their primary |
| `set_field_static_value` | Set a constant value for an adapter field |
| `connect_fields` | Bulk connect multiple fields in one call |
| `set_fields_static_value` | Bulk set static values for multiple fields |
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
| `preview_marketplace_products` | List products ON the marketplace (paginated), marks already-imported |
| `import_products` | Queue import of marketplace products into local catalog; returns batchId |
| `import_prices` | Import prices FROM marketplace into local products (via PriceSyncService) |
| `import_stocks` | Import stocks FROM marketplace (sums across warehouses, updates links) |
| `archive_product_on_marketplace` | Archive (hide) a linked product on the marketplace |

### Groups
| Tool | Description |
|------|-------------|
| `list_groups` | List product groups (filter: name search, containing product) |
| `get_group` | Group details + full member list (id, name, sku, price, quantity) |
| `create_group` | Create group (with optional initial products) |
| `update_group` | Rename group / change description |
| `delete_group` | Delete group (products stay in the store) |
| `add_products_to_group` | Add products to existing group |
| `remove_products_from_group` | Remove products from a group |

### Files (scope: `product` / `store`)
| Tool | Description |
|------|-------------|
| `list_files` | List files + folders (product files or store storage) |
| `create_folder` | Create a folder |
| `rename_file` | Rename a file (display name) |
| `rename_folder` | Rename a folder |
| `move_files` | Bulk move files/folders into another folder |
| `delete_files` | Permanently delete files (storage objects removed) |
| `delete_folder` | Delete folder (recursive option) |
| `share_file` | Create public share link for file/folder (TTL 1h-30d) |

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

### Navigation
| Tool | Description |
|------|-------------|
| `navigate_to` | Render a navigation button to a page (product, category, etc.). URL validated server-side (local paths only). Panel stays open. |

### Special
| Tool | Description |
|------|-------------|
| `propose_plan` | Show plan to user before any mutation; pauses loop |
| `rollback_last_operation` | Undo last AI operation (or specific operationId) |

---

## Page-Aware Tool Filtering

`AiChatService.BuildToolDefinitions(pageContext)` filters the tool list based on `pageContext.Page`
to reduce token usage (~35-40% fewer tokens on focused pages). The frontend sends page context on
each request.

| `pageContext.Page` | Tools sent | Excluded |
|--------------------|-----------|---------|
| `"product"` | Products, Categories (read), Store, Files, Groups, Export, Analytics, `list_mappings`, `list_connections` (nav helpers) | All write-only mapping/connection tools |
| `"fieldMapping"` | Mappings (full), Products (read), Categories (read), Connections (read+adapter_info) | Export/import, file, group, analytics-only tools |
| `"connection"` | Connections, Mappings (full), Export/Import, Products (read), Categories (read) | File, group, product-analytics tools |
| `null` (other pages) | All tools | - |

**Plan execution is not affected by filtering.** `ExecutePlanStreamAsync` dispatches tool steps
directly via `ToolDispatcher` with no page filter - confirmed plans always have access to all tools.

---

## Mapping Semantics: Grouping & Field Aliases

**Card grouping (объединение в карточку).** Every mapping exposes exactly one grouping field
(`isGroupingField` in `get_external_fields`): field-based adapters (Ozon) use their real field
(e.g. 9048), endpoint-based adapters (WB) get a synthetic `__group__` appended at mapping
creation (`GraphGenerator.EnsureGroupingField`). Default binding is `groupName` 1:1; modifiers
can transform it (merge different local groups into one card). Products whose mapping evaluates
the field to the **same value** join one marketplace card. For endpoint adapters the value is
stripped from the export payload and drives `ProductSyncService.SyncEndpointGroupsByValueAsync`
(value → external group id stored in `GroupValueMarketplaceLink`). The mapping never creates or
modifies local groups.

**Field aliases (merged duplicates).** `AdapterField.aliasOf` marks a field as a duplicate of a
primary: export copies the primary's evaluated value under the alias key (`GraphEvaluator`),
import reverses **only** the primary (`MappingService.ProjectGraphForImport`), the editor renders
the alias without an input pin («= primary»). Managed via `merge_adapter_fields` /
`unmerge_adapter_fields` or the field settings dropdown.

---

## Auto-Mapping (AiMappingService)

Triggered on mapping creation (the «Использовать AI» checkbox), via the chat tool
`generate_mapping_suggestions`, and via `POST /api/ai/mapping-suggest/{id}`.

**Batched algorithm (partial success by design):**
1. Filter out already-connected fields; split the rest into batches of 15
2. Load local category attributes + 2 example mappings for the same adapter (shared context)
3. Per batch: build prompt (batch fields only), call the model, parse element-by-element,
   filter `confidence ≥ 0.4`, apply immediately via `GraphMutationService.ApplySuggestionsAsync`
4. A failed batch is logged as a warning and skipped - earlier batches stay applied
5. Return aggregated `AiMappingResult { connected, skipped, alreadyConnected, proposed, ... }`

**Progress tracking:** with `{ operationId }` in the request body the endpoint returns **202**
immediately and runs in a detached DI scope (`Task.Run` + `IServiceScopeFactory`) - a page reload
does not cancel it. The controller owns the operation lifecycle (`StartAsync` type `ai-mapping`,
title `AI-маппинг: <категория> → <подключение> (#mappingId)`, `FinishAsync` with a summary);
`AiMappingService` streams per-batch `LogAsync`/`ProgressAsync` into the operations panel via
SignalR. `fieldMappingPage` subscribes to `FINISH_OPERATION` and reloads the graph when the
finished operation's title carries its mapping id. Without `operationId` the endpoint stays
synchronous (used by the chat tool).

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
| `add_category_field` | `{ fieldId }` - deletes the field |
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

## Conversation History

Conversations are persisted per-store and per-user:

```sql
aiConversations  (sessionId VARCHAR UNIQUE, storeId, userId, title, lastMessageAt, ...)
aiConversationMessages  (conversationId FK, role, content, toolCallsJson, feedbackRating, ...)
```

**Store isolation:** `SaveMessages` scopes the lookup to `sessionId + storeId + userId`.
On the frontend `<AiChatPanel :key="appKey" />` forces a full remount (new `sessionId` + fresh
`loadLastConversation`) when the user switches stores, preventing cross-store session bleed.

| Endpoint | Description |
|----------|-------------|
| `GET /api/ai/conversations` | List conversations for current store/user |
| `GET /api/ai/conversations/{sessionId}` | Full conversation with messages |
| `GET /api/ai/conversations/latest` | Last conversation (restore on panel open) |
| `PATCH /api/ai/conversations/{sessionId}` | Rename |
| `DELETE /api/ai/conversations/{sessionId}` | Delete |
| `POST /api/ai/conversations/messages` | Upsert messages (by sessionId + storeId + userId) |
| `POST /api/ai/conversations/{sessionId}/generate-title` | AI-generated title |
| `POST /api/ai/messages/{id}/feedback` | Rate a message (👍 = 1, 👎 = −1) |

---

## Data Collection & Fine-Tuning Strategy

Feedback ratings (`feedbackRating`) on `aiConversationMessages` are the primary signal.
All confirmed plans and successful tool call sequences are stored implicitly in `toolCallsJson`.

**To prepare training data:**
- Filter conversations where user gave 👍 or confirmed a plan
- Export as JSONL: `{ messages: [{role, content, tool_calls}, ...] }` (Anthropic fine-tune format)
- Mask PII before export (store/product names → generic placeholders)

**Best candidates for fine-tuning:**
- Mapping suggestions: adapters have consistent field semantics - a model trained on past mappings would skip the general mapping prompt and propose accurate connections directly
- Plan structuring: teach a smaller model to always emit `propose_plan` with correct `args`

**Recommended path:**
1. Accumulate 500+ quality examples with 👍 or confirmed plans
2. Add admin export endpoint for JSONL download
3. Fine-tune on Anthropic or open-weight model (Qwen, Llama) for cost reduction on repetitive tasks

See also: `docs/architecture/ai-system.md` - comprehensive reference including all tables, APIs, fine-tuning strategy, and RAG-ready descriptions.

---

## Frontend Integration Points

| Location | Feature |
|----------|---------|
| `app.vue` | Floating AI button + `<AiChatPanel :key="appKey" />` (all non-public pages; remounts on store switch) |
| `fieldMappingPage.vue` | "✦ AI: улучшить маппинг" button → `POST /api/ai/mapping-suggest/{id}` |
| `connectionPage.vue` | "✦ Объяснить ошибку" inline on test connection failure |
| `settingsOverviewPage.vue` | "AI Модели" link → `/settings/admin/ai` |
| `/settings/admin/ai` | `AiModelsPage.vue` - admin model CRUD with proxy field |
