# Stored orders and overview tabs Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. TDD: failing test → run (red) → minimal code → run (green) → commit. EF migrations **only** via `dotnet ef migrations add` (never hand-write / hand-edit). Canon: [`MEMORY.md`](../../MEMORY.md). Design: [`2026-09-04-stored-orders-overview-tabs-design.md`](2026-09-04-stored-orders-overview-tabs-design.md).

**Goal:** Persist every marketplace order Delegat sees from now on, drive cabinet lists and Public API GET from that snapshot, and give `/orders` four all-adapter tabs (To pack / Being packed / In delivery / Delivered).

**Architecture:** New EF rows `MarketplaceOrder` (full `ExternalOrder` JSON + denormalized list columns) and `MarketplaceOrderSyncState` (last pull per connection × status). `MarketplaceOrderSnapshotService` upserts/reads. `MarketplaceOrderSyncService` pulls adapters with a 5‑minute page-load cooldown and a 15‑minute background cooldown (`TimeProvider`). Silent `OrderSyncBackgroundService` paginates in-flight statuses, takes the first page of terminal statuses, then `GetOrder` for stored in-flight ids. Cabinet `GET /marketplace/orders?tab=` is store-wide; connection mailboxes and Public API `GET …/orders` read the DB. Seller actions still hit the marketplace, then upsert. Labels/shipments stay live.

**Tech Stack:** ASP.NET Core 8, EF Core + MySQL (InMemory in tests), Vue 3 SPA, vue-i18n, node:test, xUnit.

**Do not:** historical backfill, `MAX_ORDERS`, store labels/supplies, Public API live refresh, `/orders/ship` as an overview path (legacy mailbox redirect).

---

### Task 1: Overview tab → status map (host)

**Files:**
- Create: `backend.Tests/MarketplaceOrderBucketsTests.cs`
- Create: `backend/Services/Marketplace/MarketplaceOrderBuckets.cs`

**Step 1: Write the failing test**

```csharp
using Delegat.Marketplace.SDK.DTOs;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MarketplaceOrderBucketsTests
{
    [Theory]
    [InlineData("assembly", new[] { "new", "unpaid", "confirmed", "awaiting_pack" })]
    [InlineData("ship", new[] { "awaiting_deliver" })]
    [InlineData("delivery", new[] { "delivering" })]
    [InlineData("done", new[] { "delivered" })]
    public void StatusesForOverviewTab_MatchesDesign(string tab, string[] expected)
    {
        Assert.Equal(expected, MarketplaceOrderBuckets.StatusesForOverviewTab(tab));
    }

    [Fact]
    public void StatusesForOverviewTab_Unknown_IsEmpty()
    {
        Assert.Empty(MarketplaceOrderBuckets.StatusesForOverviewTab("closed"));
        Assert.Empty(MarketplaceOrderBuckets.StatusesForOverviewTab(""));
    }

    [Fact]
    public void ListStatusesForAdapter_HidesMissingMailboxes()
    {
        Assert.DoesNotContain(
            MarketplaceOrderStatuses.New,
            MarketplaceOrderBuckets.ListStatusesForAdapter("ozon"));
        Assert.DoesNotContain(
            MarketplaceOrderStatuses.AwaitingDeliver,
            MarketplaceOrderBuckets.ListStatusesForAdapter("woocommerce"));
        Assert.Contains(
            MarketplaceOrderStatuses.New,
            MarketplaceOrderBuckets.ListStatusesForAdapter("yandex"));
        Assert.Contains(
            MarketplaceOrderStatuses.Cancelled,
            MarketplaceOrderBuckets.ListStatusesForAdapter("ozon"));
    }

    [Theory]
    [InlineData("delivered", true)]
    [InlineData("cancelled", true)]
    [InlineData("returned", true)]
    [InlineData("awaiting_pack", false)]
    public void IsTerminal(string status, bool expected)
        => Assert.Equal(expected, MarketplaceOrderBuckets.IsTerminal(status));
}
```

**Step 2: Run test to verify it fails**

```bash
dotnet test backend.Tests/Delegat.Tests.csproj --filter FullyQualifiedName~MarketplaceOrderBucketsTests
```

Expected: FAIL (type not found).

**Step 3: Write minimal implementation**

`MarketplaceOrderBuckets.cs` — English identifiers. Mirror `ADAPTER_ORDER_MAILBOXES` in `frontend/src/lib/ordersUi.js`:

- `ozon` / `wildberries`: pack, ship, delivery, done, closed (pack = `confirmed` + `awaiting_pack`; also list `new` + `unpaid` so a rare Ozon accept row still upserts).
- `yandex`: new, pack, ship, delivery, done, closed.
- `woocommerce` / `opencart`: new, pack, done, closed.
- Unknown adapter: union of all normalized statuses except `unknown`.

`ListStatusesForAdapter` flattens those mailbox status lists (distinct, stable order). `StatusesForOverviewTab("assembly")` is always the four assembly statuses (SPA still hides empty Woo/Ozon groups).

**Step 4: Run tests (pass)**

Same `dotnet test` filter. Expected: PASS.

**Step 5: Commit**

```bash
git add backend/Services/Marketplace/MarketplaceOrderBuckets.cs backend.Tests/MarketplaceOrderBucketsTests.cs
git commit -m "$(cat <<'EOF'
Add host mapping for stored-order overview tabs.

EOF
)"
```

---

### Task 2: EF entities (model only, no migration yet)

**Files:**
- Create: `backend/Models/Marketplace/MarketplaceOrder.cs`
- Create: `backend/Models/Marketplace/MarketplaceOrderSyncState.cs`
- Modify: `backend/DataContext.cs` (DbSets + Fluent)
- Modify: `backend/Models/Marketplace/MarketplaceConnection.cs` (optional nav collections)
- Create: `backend.Tests/MarketplaceOrderEntityTests.cs`

InMemory tests do **not** need a MySQL migration. Do **not** add files under `backend/Migrations/` by hand.

**Step 1: Failing test** — save + reload via `TestDbFactory.CreateDb()` + `TestDataBuilder.AddStore` / `AddConnection`.

```csharp
public class MarketplaceOrderEntityTests
{
    [Fact]
    public async Task RoundTrip_StoresPayloadAndDenormalizedColumns()
    {
        using var db = TestDbFactory.CreateDb();
        var data = new TestDataBuilder(db);
        var store = data.AddStore();
        var connection = data.AddConnection(store.Id, "ozon");
        db.marketplaceOrders.Add(new MarketplaceOrder
        {
            storeId = store.Id,
            connectionId = connection.Id,
            marketplaceOrderId = "123-1",
            displayId = "123",
            status = "awaiting_pack",
            channel = "fbs",
            currency = "RUB",
            total = 10.5m,
            orderCreatedAt = DateTime.UtcNow,
            syncedAt = DateTime.UtcNow,
            payloadJson = """{"externalId":"123-1","status":"awaiting_pack"}"""
        });
        await db.SaveChangesAsync();

        var row = await db.marketplaceOrders.AsNoTracking().SingleAsync();
        Assert.Equal("123-1", row.marketplaceOrderId);
        Assert.Equal("awaiting_pack", row.status);
        Assert.Contains("123-1", row.payloadJson);
    }
}
```

**Step 2:** `dotnet test --filter FullyQualifiedName~MarketplaceOrderEntityTests` → FAIL (DbSet missing).

**Step 3: Entities**

`MarketplaceOrder` (`IEntity`), camelCase like `AdapterHttpLog`:

| Column | Notes |
| --- | --- |
| `Id` | PK |
| `storeId`, `connectionId` | FK cascade with `MarketplaceConnection` |
| `marketplaceOrderId` | `[StringLength(128)]` adapter `ExternalId` |
| `displayId` | `[StringLength(128)]` nullable |
| `status` | `[StringLength(32)]` |
| `channel` | `[StringLength(16)]` default `shop` |
| `currency` | `[StringLength(8)]` |
| `total` | `decimal?` |
| `orderCreatedAt`, `orderUpdatedAt` | `DateTime?` UTC |
| `syncedAt` | `DateTime` UTC |
| `payloadJson` | `[Column(TypeName = "longtext")]` full `ExternalOrder` |

`MarketplaceOrderSyncState`: `connectionId`, `status` (`StringLength(32)`), `lastPulledAt`, `lastError` (`StringLength(2000)` nullable).

Fluent in `DataContext.OnModelCreating`:

```csharp
builder.Entity<MarketplaceOrder>(e =>
{
    e.HasIndex(x => new { x.storeId, x.connectionId, x.marketplaceOrderId }).IsUnique();
    e.HasIndex(x => new { x.storeId, x.status });
    e.HasIndex(x => new { x.connectionId, x.status });
    e.HasIndex(x => x.displayId);
    e.HasOne<MarketplaceConnection>()
        .WithMany()
        .HasForeignKey(x => x.connectionId)
        .OnDelete(DeleteBehavior.Cascade);
});
builder.Entity<MarketplaceOrderSyncState>(e =>
{
    e.HasIndex(x => new { x.connectionId, x.status }).IsUnique();
    e.HasOne<MarketplaceConnection>()
        .WithMany()
        .HasForeignKey(x => x.connectionId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

DbSets: `marketplaceOrders`, `marketplaceOrderSyncStates`.

**Step 4:** tests PASS.

**Step 5: Commit** `Add marketplace order snapshot entities.`

---

### Task 3: Snapshot upsert / list / get

**Files:**
- Create: `backend/Services/Marketplace/MarketplaceOrderSnapshotService.cs`
- Create: `backend.Tests/MarketplaceOrderSnapshotServiceTests.cs`

**Step 1: Failing tests**

- `Upsert_InsertsThenUpdatesStatusAndPayload` — second upsert same `(connectionId, marketplaceOrderId)` changes `status` / `payloadJson` / `syncedAt`, does not add a second row.
- `List_FiltersByStatusesChannelSearchAndPage` — two orders; filter `awaiting_pack`; search by display id; `pageSize=1` sets `hasMore`.
- `Get_MatchesMarketplaceIdOrDisplayId` — find by posting id and by display id; missing → null.
- `ToExternalOrder_SetsSyncedAt` — after SDK field exists (Task 5) **or** for now assert deserialized `ExternalId` / `Items`. If `SyncedAt` is not on the DTO yet, assert it in Task 5 and skip here.

Use `TestDbFactory` + `TestDataBuilder`. Clock: inject `TimeProvider` (custom `FixedTimeProvider : TimeProvider` with settable `UtcNow`).

**Step 2:** run filter `MarketplaceOrderSnapshotServiceTests` → FAIL.

**Step 3: Implementation**

```csharp
public sealed class MarketplaceOrderSnapshotService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task UpsertAsync(int storeId, int connectionId, ExternalOrder order, CancellationToken ct);
    public Task<IReadOnlyList<MarketplaceOrder>> ListAsync(
        int storeId,
        int? connectionId,
        IReadOnlyList<string> statuses,
        string? channel,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int page,
        int pageSize,
        CancellationToken ct);
    public Task<int> CountAsync(/* same filters without page */);
    public Task<MarketplaceOrder?> FindAsync(int storeId, int connectionId, string orderId, CancellationToken ct);
    public ExternalOrder ToExternalOrder(MarketplaceOrder row);
}
```

`UpsertAsync`: no-op if `order.ExternalId` blank. `FindAsync` matches `marketplaceOrderId` **or** `displayId` (ordinal). Search: `Contains` on `marketplaceOrderId` and `displayId`. Order list: `orderCreatedAt` desc, then `Id` desc. `ToExternalOrder` deserializes `payloadJson`; if JSON is broken, build a minimal `ExternalOrder` from columns. Set `SyncedAt` when that property exists.

Register `AddScoped<MarketplaceOrderSnapshotService>()` in `backend/Program.cs` next to `MarketplaceCommerceService`.

**Step 4:** PASS.

**Step 5: Commit** `Upsert and query marketplace order snapshots.`

---

### Task 4: Throttled adapter sync

**Files:**
- Create: `backend/Services/Marketplace/MarketplaceOrderSyncService.cs`
- Create: `backend.Tests/MarketplaceOrderSyncServiceTests.cs`
- Create: `backend.Tests/Fakes/OrdersAdapterFake.cs` — copy non-order members from `LookupAdapter` in `backend.Tests/CatalogMatchServiceTests.cs` (credentials, catalog stubs, grouping). Override `GetOrdersAsync` / `GetOrderAsync` only. `Capabilities = GetOrders`. Key `"ozon"`.
- Create: `backend.Tests/Fakes/SingleAdapterRegistry.cs` **only if** you do not reuse the private nested registries — prefer a small public test registry: `GetByKey` / `GetAll`.

**Step 1: Failing tests** (inject `TimeProvider` + fake adapter + in-memory db).

1. `PullStatus_PaginatesInFlightUntilHasMoreFalse` — in-flight status returns two pages; both orders upserted; `GetOrders` called twice.
2. `PullStatus_Terminal_OnlyFirstPage` — `delivered` `HasMore=true` but only page 1 requested.
3. `Cooldown_SkipsSecondPageLoadWithinFiveMinutes` — `reason: page`; first pull calls adapter; second at +4 min does not; third at +5 min does.
4. `Background_UsesFifteenMinuteCooldown` — `reason: background` skipped at +14 min, pulled at +15.
5. `PageLoad_UpdatesTimestampSoBackgroundSkips` — page pull then background 1 min later skips.
6. `PullError_LeavesExistingSnapshot_RecordsLastError` — first upsert succeeds; second `GetOrders` throws; row still present; `lastError` set.
7. `RefreshInFlightById_AfterLists` — stored `awaiting_pack` order; list returns empty; `GetOrder` returns `delivering`; snapshot status updates. Call this from `SyncConnectionAsync` after list pulls.
8. `SyncTab_AllActiveGetOrdersConnections` — two connections; `tab=assembly` pulls assembly statuses on both.

`OrderSyncReason` enum or string: `"page"` | `"background"`.

**Step 2:** FAIL.

**Step 3: `MarketplaceOrderSyncService`**

```csharp
public sealed class OrderSyncResult
{
    public bool Pulled { get; init; }
    public bool Skipped { get; init; }
    public int Upserted { get; init; }
    public string? Reason { get; init; }
}

public Task<OrderSyncResult> SyncConnectionStatusesAsync(
    MarketplaceConnection connection,
    IReadOnlyList<string> statuses,
    string reason, // page | background
    CancellationToken ct);

public Task<OrderSyncResult> SyncStoreTabAsync(
    int storeId,
    string tab,
    string reason,
    CancellationToken ct);
```

Logic per `(connectionId, status)`:

1. Load/create `MarketplaceOrderSyncState`.
2. Cooldown: page = 5 min, background = 15 min. Missing `lastPulledAt` → pull.
3. `GetOrdersAsync` with `OrderQuery { Status, PageSize = 50, Page / Cursor }`.
4. If `!IsTerminal(status)`: while `HasMore`, next page/cursor (cap **20 pages** per status per run so a stuck cursor cannot burn quota).
5. If terminal: **one** page only.
6. `UpsertAsync` each item.
7. Set `lastPulledAt = utcNow`, clear `lastError`.
8. Catch: set `lastError`, do not throw to the background worker; page-load **does** throw/`ApiException` so the SPA can toast — **or** return `{ skipped:false, pulled:false, Reason=error }` without throwing. Prefer **return result + log** for both, SPA toasts if `lastError` on an explicit sync. Keep list GET from DB even when sync fails.

After all requested statuses for a **background** connection sync: for each stored order on that connection with `!IsTerminal(status)`, `GetOrderAsync` and upsert (skip if cooldown on a dedicated sync state key `status="__inflight_ids"` with the same 15 min). Do **not** run the by-id refresh on page-load.

`SyncStoreTabAsync`: active `GetOrders` connections in the store (`isActive`, adapter has flag). Inactive → skip. Unknown adapter → skip.

Register scoped in `Program.cs`.

**Step 4:** PASS.

**Step 5: Commit** `Throttle marketplace order pulls and upsert snapshots.`

---

### Task 5: SDK `syncedAt` + commerce reads DB

**Files:**
- Modify: `sdk/Delegat.Marketplace.SDK/DTOs/Orders.cs` — additive `public DateTimeOffset? SyncedAt { get; set; }` on `ExternalOrder`. Update the class XML: snapshots may exist; adapters leave this null.
- Modify: `backend/Services/Marketplace/MarketplaceCommerceService.cs` — list/get/update go through snapshot (keep `RequireAsync`, `AttachLocalOrderItemImagesAsync`, `BuildOrderQuery`).
- Modify: `backend.Tests/MarketplaceCommerceServiceTests.cs` — still asserts `BuildOrderQuery`; add tests that list does not need an adapter call when rows exist (extract list/get onto the snapshot service and test that; keep controller as thin).
- Create: `backend.Tests/MarketplaceCommerceOrdersStoreTests.cs` if the commerce service method is easier to test with a fake adapter that **must not** be called on GET.

**Behavior:**

- `ListStoredOrdersAsync(storeId, connectionId, query)` — map `query.Status` to a one-element status list, or all adapter list-statuses if status omitted. Return `PagedResult<ExternalOrder>` with `HasMore` / `TotalCount` from DB. `NextCursor`: when `HasMore`, next **page number** as string (ignore marketplace cursors). `ToExternalOrder` + `AttachLocalOrderItemImagesAsync`.
- `GetStoredOrderAsync` — snapshot first. **Cabinet** miss: live `adapter.GetOrderAsync`, upsert if found, attach images. **Public** miss: null (404). Pass a `bool liveFallback` parameter.
- `UpdateOrderStatus` (called from controllers): live update as today; if `result.Order != null` upsert; else live get + upsert.

**Step 1:** test `List_DoesNotCallAdapter`; `Get_PublicMiss_DoesNotCallAdapter`; `Get_CabinetMiss_CallsAdapterAndUpserts`.

**Step 2–4:** red/green.

**Step 5: Commit** sdk + host together: `Read cabinet and Public API orders from snapshots.`

Submodule: commit inside `sdk/` first if that repo tracks the DTO, then bump the parent pointer in the same dashboard commit **or** two commits. Do not leave plugins failing compile — additive property only.

---

### Task 6: HTTP endpoints

**Files:**
- Modify: `backend/Controllers/MarketplaceCommerceController.cs`
- Modify: `backend/Controllers/PublicApi/PublicMarketplaceCommerceController.cs`
- Modify: `backend.Tests/MarketplaceCommerceServiceTests.cs` (`CabinetOrderEndpoints_RequireOrdersManage` — add `ListStoreOrders` + `SyncOrders` source asserts)

**Cabinet**

- `GET /marketplace/orders`  
  Query: `tab` (`assembly|ship|delivery|done`, default `assembly`), `page`, `pageSize`, `channel`, `search`, `from`, `to`.  
  Permission: `OrdersManage`, store membership.  
  Body: `{ items: [{ connectionId, adapterKey, connectionName, order }], page, pageSize, total, hasMore }`.  
  `order` is `ExternalOrder` with `syncedAt`.

- `POST /marketplace/orders/sync?tab=assembly`  
  Calls `SyncStoreTabAsync(storeId, tab, "page")`. Same permission.  
  `{ pulled, skipped, upserted, reason }`.

- `GET /marketplace/connections/{id}/orders` — **from DB** via `ListStoredOrdersAsync` (no live list). Same query params as today.

- `POST /marketplace/connections/{id}/orders/sync`  
  Query: `status` optional; if omitted, all `ListStatusesForAdapter`.  
  `{ pulled, skipped, upserted, reason }`.

- `GET …/orders/{orderId}` — `GetStoredOrderAsync(..., liveFallback: true)`.

- `POST …/status` — live + upsert (Task 5).

**Public**

- `GET …/orders` and `GET …/orders/{id}` from DB, `liveFallback: false`. XML comments: eventually consistent snapshot; `syncedAt` on each order; `cursor` ignored (page/pageSize). Status update still live + upsert.

**Step 1:** extend the existing source-string permission test so a missing `[RequiresPermission(Permissions.OrdersManage` on the new methods fails.

**Step 2–4.**

**Step 5: Commit** `Serve stored orders on cabinet and Public API.`

Update controller class XML (no longer “nothing is stored”).

---

### Task 7: Background worker

**Files:**
- Create: `backend/Services/OrderSyncBackgroundService.cs` (pattern: `QueueCleanupBackgroundService`)
- Create: `backend.Tests/OrderSyncBackgroundServiceTests.cs`
- Modify: `backend/Program.cs` — `AddSingleton(TimeProvider.System)` if not present; `AddHostedService<OrderSyncBackgroundService>()`.

**Behavior:** delay **2 minutes** after start; loop every **15 minutes**. Each cycle: every active GetOrders connection, `SyncConnectionStatusesAsync(connection, ListStatusesForAdapter(key), "background")` then in-flight by-id refresh (already inside sync for background). Swallow exceptions per connection, log.

Test `ProcessOnceAsync` with one connection + fake adapter (same as Task 4) through `IServiceScopeFactory`.

**Commit:** `Poll marketplace orders in the background.`

---

### Task 8: EF migration (CLI only)

**Files (generated, never hand-edited):**
- `backend/Migrations/YYYYMMDDHHMMSS_AddMarketplaceOrders.cs`
- `backend/Migrations/YYYYMMDDHHMMSS_AddMarketplaceOrders.Designer.cs`
- `backend/Migrations/DataContextModelSnapshot.cs`

**Steps:**

1. Confirm model compiles (`dotnet build backend/Delegat.csproj`).
2. If `Delegat.dll` is locked, stop the API process **only for this step**.
3. From `backend/`, load `MYSQL_*` from repo-root `.env.dev` into the environment (PowerShell: parse `MYSQL_SERVER`, `MYSQL_PORT`, `MYSQL_DATABASE`, `MYSQL_USER`, `MYSQL_PASSWORD`).
4. Run:

```bash
dotnet ef migrations add AddMarketplaceOrders --output-dir Migrations
```

5. Open the Designer: `[DbContext(typeof(DataContext))]` and `[Migration("YYYYMMDDHHMMSS_AddMarketplaceOrders")]` must exist. If the shape is wrong, **fix the model**, `dotnet ef migrations remove`, add again. Never edit `Up`/`Down` / `migrationBuilder.Sql`.
6. Commit the pair + snapshot: `Add marketplace order tables.`

Startup already runs `MigrateAsync` — after restart the tables appear. No manual `database update` required in prod; in dev, restarting the API is enough.

---

### Task 9: SPA helpers + i18n

**Files:**
- Modify: `frontend/src/lib/ordersUi.js`
- Modify: `frontend/src/lib/ordersUi.test.js`
- Modify: `frontend/src/lib/isActiveRoute.test.js` — `/orders?tab=ship` still highlights To pack
- Modify: `frontend/src/locales/en.json`, `frontend/src/locales/ru.json`
- Modify: `frontend/src/router.js` — keep `/orders` on `OrdersAssemblyPage`; **do not** add `/orders/ship`

**Helpers:**

```javascript
export const ORDER_OVERVIEW_TABS = [
  { id: "assembly", labelKey: "nav.ordersAssembly" },
  { id: "ship", labelKey: "nav.ordersPicking" },
  { id: "delivery", labelKey: "nav.ordersInDelivery" },
  { id: "done", labelKey: "nav.ordersDelivered" },
];

export function parseOrdersOverviewTab(queryTab) { /* default assembly */ }
export function overviewTabPath(tab) {
  return tab === "assembly" ? "/orders" : { path: "/orders", query: { tab } };
}
export function statusesForOverviewTab(tab) { /* match C# */ }
export function mailboxForOverviewTab(tab, adapterKey) {
  // assembly → pack (or new if adapter only has new and tab is assembly — use first of assemblyMailboxes)
  // ship → ship if adapter has it else null
}
```

`isOrdersOverviewPath` stays true for `/orders` with any query.

`resolveOrdersLocation`: if overview path, keep `/orders` and **preserve** `tab`.

**i18n** (nav + routes + empty copy):

| key | en | ru |
| --- | --- | --- |
| `nav.ordersPicking` | Being packed | Собираются |
| `nav.ordersInDelivery` | In delivery | В доставке |
| `nav.ordersDelivered` | Delivered | Доставлены |
| `routes.ordersPicking` / `InDelivery` / `Delivered` | same |
| `pages.ordersPage.emptyOverviewShipDescription` | No orders waiting to ship. | Нет заказов к отгрузке. |
| `emptyOverviewDeliveryDescription` | No orders in delivery. | Нет заказов в доставке. |
| `emptyOverviewDoneDescription` | No delivered orders yet. | Пока нет доставленных заказов. |

Keep mailbox labels (`nav.ordersShip` = Shipment / Отгрузка). `nav.ordersAssembly` stays To pack / К сборке.

**Tests:** tab parse; `overviewTabPath("ship")` is query not `/orders/ship`; `isOrdersOverviewPath("/orders/ship")` false; statuses match Task 1.

Run: `node --test frontend/src/lib/ordersUi.test.js frontend/src/lib/isActiveRoute.test.js`

**Commit:** `Add four-tab helpers for the orders overview.`

---

### Task 10: Overview page + mailbox refresh

**Files:**
- Modify: `frontend/src/pages/orders/OrdersAssemblyPage.vue`
- Modify: `frontend/src/pages/orders/OrdersPage.vue`

**Overview (`OrdersAssemblyPage`):**

- Navbar `#nav-center`: `Tabs` of `ORDER_OVERVIEW_TABS` (same `TabsList` / `TabsTrigger` as mailbox). `model-value` from `parseOrdersOverviewTab($route.query.tab)`. Change tab → `$router.push(overviewTabPath(id))` (drop other query except `order` / `connectionId` if the sheet is open).
- `has-nav-center` true when there is at least one GetOrders connection.
- Breadcrumb / document title follow the active tab key.
- `fetchOrders`: `GET /marketplace/orders?tab=&search=&channel=&page=&pageSize=` then group `items` by `connectionId` (adapter icon, name). Omit groups with zero rows. Then `POST /marketplace/orders/sync?tab=` (`cache: false`); if `pulled`, GET again (silent).
- Empty copy: assembly → existing pack empty; other tabs → new keys.
- `openMailbox(group)`: `mailboxForOverviewTab(activeTab, adapterKey)` or `pack`.
- Remove per-status fan-out `fetchStatusPage` for the initial load. Keep search: DB list first (`search` query); on miss, existing live `GET …/orders/{id}` per connection then navigate.
- Sheet, context menu, photo size, jobs: unchanged.
- Watch `$route.query.tab` to refetch.

**Mailbox (`OrdersPage`):**

- After `GET /marketplace/connections/{id}/orders`, `POST /marketplace/connections/{id}/orders/sync?status=` for **each** `statusesForBucket` (or one POST without status). If `pulled`, GET again. Do not block first paint.
- Lists already merge statuses client-side; DB GET with a single `status` still works per existing loop.

**Verify in the browser** (`http://localhost:5173`): four tabs; Woo/OpenCart empty on ship/delivery; mailbox Closed still there; pack still works. If the API was stopped for the migration, start it again with `.env` / `.env.dev`.

**Commit:** `Show stored orders on overview tabs and mailboxes.`

---

### Task 11: Canon and partner docs

**Files:**
- `MEMORY.md` — replace “orders … pass-through (not stored, no EF tables)” with snapshot + worker + four overview tabs + Public GET from DB; labels/shipments still live; `/orders?tab=`; sidebar unchanged.
- `CHANGELOG.md` `## [Unreleased]` Added/Changed (operators/partners, not class names).
- `docs/api/public-v1.md` — commerce section: orders are stored snapshots; `syncedAt`; eventually consistent; `cursor` unused; GET by id 404 if never synced; status POST still live. Out of scope line “Orders … are not stored” → orders **are** stored; promotions still not.
- `docs/platform/capabilities.md` — same product sentence as MEMORY.
- `DESIGN.md` Orders bullet — four overview tabs, Mail groups.
- `docs/README.md` — link this plan + the design doc.
- `backend/Controllers/PublicApi/PublicMarketplaceCommerceController.cs` XML (already in Task 6 if done).
- Example curl comment: “snapshot, not live cabinet”.

**Commit:** `Document stored orders and overview tabs.`

---

### Task 12: Verification

From repo root:

```bash
dotnet test backend.Tests/Delegat.Tests.csproj --filter FullyQualifiedName~MarketplaceOrder
cd frontend && npm run test:unit
```

Manual: open `/orders`, switch four tabs, open a mailbox, pack/ship if a sandbox order exists, confirm a row remains after reload, Public GET `/api/v1/stores/{id}/connections/{id}/orders` returns `syncedAt` and does not require the marketplace to be up if a row exists (stop is optional; do not kill the user’s API unless needed).

If anything fails, fix with TDD (do not skip).

---

## Execution notes

- Chat may be Russian; **repo English** except vue-i18n values.
- Public API: additive `syncedAt`, same URLs; document the live→snapshot behavior change in CHANGELOG **Changed**.
- Do not commit `.cursor/`, screenshots at repo root, or `.env`.
- Frequent commits as listed; do not push unless asked.
