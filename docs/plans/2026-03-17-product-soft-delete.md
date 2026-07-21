# Product Soft Delete Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace hard delete of products with soft delete (`deletedAt` field), add trash UI with restore and permanent delete.

**Architecture:** Add nullable `deletedAt` to Product model only. EF Core global query filter auto-excludes deleted products from all queries. Trash endpoints use `IgnoreQueryFilters()`. Existing hard-delete logic is reused for permanent delete. Frontend adds a trash mode toggle to the products page.

**Tech Stack:** .NET 8 / EF Core 8 / MySQL 8.4 / Vue 3 / Vuex / shadcn-vue

---

### Task 1: Add `deletedAt` to Product model

**Files:**
- Modify: `backend/Models/Products/Product.cs:63` (after `updatedAt`)

**Step 1: Add the field**

In `Product.cs`, after line 63 (`public DateTime updatedAt ...`), add:

```csharp
/// <summary>
/// Timestamp when the product was soft-deleted. Null means active.
/// </summary>
public DateTime? deletedAt { get; set; }
```

**Step 2: Add `deletedAt` to `ProductReviewDTO`**

In the `ProductReviewDTO` struct (around line 438), add after `updatedAt`:

```csharp
public DateTime? deletedAt { get; set; }
```

And in `IMapToDTO<ProductReviewDTO>.MapToDTO` (around line 224), add to the return:

```csharp
deletedAt = deletedAt,
```

**Step 3: Commit**

```
feat: add deletedAt field to Product model
```

---

### Task 2: EF Core global query filter + migration

**Files:**
- Modify: `backend/DataContext.cs:146` (in OnModelCreating, near Product index)

**Step 1: Add global query filter**

In `DataContext.OnModelCreating`, after the existing `builder.Entity<Product>().HasIndex(p => p.storeId);` line (~147), add:

```csharp
builder.Entity<Product>().HasQueryFilter(p => p.deletedAt == null);

builder.Entity<Product>()
    .HasIndex(p => new { p.storeId, p.deletedAt });
```

**Step 2: Generate migration**

```bash
cd backend && dotnet ef migrations add AddProductSoftDelete
```

**Step 3: Commit**

```
feat: add Product soft delete query filter + migration
```

---

### Task 3: Rewrite DeleteProduct and BulkDeleteProducts to soft delete

**Files:**
- Modify: `backend/Controllers/ProductController.cs` - `DeleteProduct` (line 744) and `BulkDeleteProducts` (line 812)

**Step 1: Replace `DeleteProduct`**

Replace the entire `DeleteProduct` method body with:

```csharp
[HttpDelete]
[Route("/products/{productId}")]
public async Task<IActionResult> DeleteProduct(int productId)
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    var product = await _context.products
        .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value);

    if (product == null)
        throw ApiException.NotFound("Товар не найден");

    product.deletedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();

    return Ok(new { next = "/products" });
}
```

**Step 2: Replace `BulkDeleteProducts`**

Replace the entire `BulkDeleteProducts` method body with:

```csharp
[HttpPost]
[Route("/products/bulk-delete")]
public async Task<IActionResult> BulkDeleteProducts([FromBody] BulkDeleteRequest request)
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    if (request.productIds == null || request.productIds.Count == 0)
        throw ApiException.BadRequest("Товары не указаны");

    var now = DateTime.UtcNow;
    var deletedCount = await _context.products
        .Where(p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.deletedAt, now));

    if (deletedCount == 0)
        throw ApiException.NotFound("Товары не найдены");

    return Ok(new { success = true, deletedCount });
}
```

**Step 3: Commit**

```
feat: replace hard delete with soft delete for products
```

---

### Task 4: Add trash endpoints (list, restore, permanent delete)

**Files:**
- Modify: `backend/Controllers/ProductController.cs` - add new endpoints after BulkDeleteProducts

**Step 1: Add GetTrash endpoint**

Add after `BulkDeleteProducts`:

```csharp
/// <summary>
/// Retrieves a paginated list of soft-deleted products for the current store.
/// </summary>
[HttpGet]
[Route("/products/trash")]
public async Task<IActionResult> GetTrash(string? searchQuery, int startIndex = 0, int pageSize = 25)
{
    if (startIndex < 0) startIndex = 0;
    if (pageSize < 1) pageSize = 25;

    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    var hasSearch = !string.IsNullOrEmpty(searchQuery);

    var query = _context.products
        .IgnoreQueryFilters()
        .AsNoTracking()
        .Where(p => p.storeId == selectedStoreId.Value && p.deletedAt != null);

    if (hasSearch)
        query = query.Where(p => p.name.Contains(searchQuery!) || p.sku.Contains(searchQuery!));

    var totalCount = await query.CountAsync();

    var items = await query
        .OrderByDescending(p => p.deletedAt)
        .Skip(startIndex)
        .Take(pageSize)
        .Include(p => p.category)
        .Include(p => p.images.OrderBy(i => i.index).Take(1))
            .ThenInclude(i => i.file)
        .Include(p => p.files.Where(f => f.contentType.StartsWith("image/")).Take(1))
        .Include(p => p.storeAttributeValues)
        .Select(p => ((IMapToDTO<Product.ProductReviewDTO>)p).MapToDTO(_serviceProvider))
        .ToListAsync();

    return Ok(new
    {
        items,
        totalCount,
        lastPage = startIndex + pageSize >= totalCount
    });
}
```

**Step 2: Add RestoreProduct endpoint**

```csharp
/// <summary>
/// Restores a soft-deleted product.
/// </summary>
[HttpPost]
[Route("/products/{productId}/restore")]
public async Task<IActionResult> RestoreProduct(int productId)
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    var product = await _context.products
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value && p.deletedAt != null);

    if (product == null)
        throw ApiException.NotFound("Товар не найден в корзине");

    product.deletedAt = null;
    await _context.SaveChangesAsync();

    return Ok(new { success = true });
}
```

**Step 3: Add BulkRestore endpoint**

```csharp
/// <summary>
/// Restores multiple soft-deleted products.
/// </summary>
[HttpPost]
[Route("/products/bulk-restore")]
public async Task<IActionResult> BulkRestore([FromBody] BulkDeleteRequest request)
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    if (request.productIds == null || request.productIds.Count == 0)
        throw ApiException.BadRequest("Товары не указаны");

    var restoredCount = await _context.products
        .IgnoreQueryFilters()
        .Where(p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value && p.deletedAt != null)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.deletedAt, (DateTime?)null));

    return Ok(new { success = true, restoredCount });
}
```

**Step 4: Add PermanentDelete endpoint**

Move the old hard-delete logic here:

```csharp
/// <summary>
/// Permanently deletes a soft-deleted product and all its related data.
/// </summary>
[HttpDelete]
[Route("/products/{productId}/permanent")]
public async Task<IActionResult> PermanentDeleteProduct(int productId)
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    var product = await _context.products
        .IgnoreQueryFilters()
        .Include(p => p.files)
        .Include(p => p.images)
        .Include(p => p.notes)
        .Include(p => p.attributeValues)
        .Include(p => p.storeAttributeValues)
        .FirstOrDefaultAsync(p => p.Id == productId && p.storeId == selectedStoreId.Value && p.deletedAt != null);

    if (product == null)
        throw ApiException.NotFound("Товар не найден в корзине");

    await _unitOfWork.ExecuteInTransactionAsync(async () =>
    {
        await _context.Set<PendingProductExport>()
            .Where(pe => pe.productId == productId)
            .ExecuteDeleteAsync();

        await _context.pendingProductImports
            .Where(pi => pi.importedProductId == productId)
            .ExecuteDeleteAsync();

        await _context.pendingImageDownloads
            .Where(pid => pid.productId == productId)
            .ExecuteDeleteAsync();

        await _context.productVariantLinks
            .Where(vl => vl.variant.productId == productId)
            .ExecuteDeleteAsync();

        await _context.productVariants
            .Where(v => v.productId == productId)
            .ExecuteDeleteAsync();

        await _context.productMarketplaceLinks
            .Where(ml => ml.productId == productId)
            .ExecuteDeleteAsync();

        await _context.productGroupMembers
            .Where(gm => gm.productId == productId)
            .ExecuteDeleteAsync();

        var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
        var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
        var noteRepo = _unitOfWork.Repository<Note>();
        var imageRepo = _unitOfWork.Repository<ImageReference>();
        var fileRepo = _unitOfWork.Repository<Models.File>();
        var folderRepo = _unitOfWork.Repository<ProductFolder>();
        var productRepo = _unitOfWork.Repository<Product>();

        // Delete file share tokens and folder share tokens
        await _context.fileShareTokens
            .Where(fst => product.files.Select(f => f.Id).Contains(fst.fileId))
            .ExecuteDeleteAsync();

        // Delete file tags
        await _context.tags
            .Where(t => product.files.Select(f => f.Id).Contains(t.fileId))
            .ExecuteDeleteAsync();

        // Delete folder share tokens
        var folderIds = await _context.productFolders
            .Where(pf => pf.productId == productId)
            .Select(pf => pf.Id)
            .ToListAsync();
        if (folderIds.Any())
        {
            await _context.folderShareTokens
                .Where(fst => folderIds.Contains(fst.folderId))
                .ExecuteDeleteAsync();
        }

        storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());
        attrValueRepo.DeleteRange(product.attributeValues.ToList());
        noteRepo.DeleteRange(product.notes.ToList());
        imageRepo.DeleteRange(product.images.ToList());
        fileRepo.DeleteRange(product.files.ToList());

        // Delete folders (children first due to Restrict)
        await _context.productFolders
            .Where(pf => pf.productId == productId && pf.parentFolderId != null)
            .ExecuteDeleteAsync();
        await _context.productFolders
            .Where(pf => pf.productId == productId)
            .ExecuteDeleteAsync();

        productRepo.DeleteWithoutSave(product);
        await _unitOfWork.SaveChangesAsync();
    });

    return Ok(new { success = true });
}
```

**Step 5: Add BulkPermanentDelete endpoint**

```csharp
/// <summary>
/// Permanently deletes multiple soft-deleted products.
/// </summary>
[HttpPost]
[Route("/products/bulk-permanent-delete")]
public async Task<IActionResult> BulkPermanentDelete([FromBody] BulkDeleteRequest request)
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        throw ApiException.BadRequest("Магазин не выбран");

    if (request.productIds == null || request.productIds.Count == 0)
        throw ApiException.BadRequest("Товары не указаны");

    var products = await _context.products
        .IgnoreQueryFilters()
        .Include(p => p.files)
        .Include(p => p.images)
        .Include(p => p.notes)
        .Include(p => p.attributeValues)
        .Include(p => p.storeAttributeValues)
        .Where(p => request.productIds.Contains(p.Id) && p.storeId == selectedStoreId.Value && p.deletedAt != null)
        .ToListAsync();

    if (!products.Any())
        throw ApiException.NotFound("Товары не найдены в корзине");

    var foundIds = products.Select(p => p.Id).ToList();

    await _unitOfWork.ExecuteInTransactionAsync(async () =>
    {
        await _context.Set<PendingProductExport>()
            .Where(pe => foundIds.Contains(pe.productId))
            .ExecuteDeleteAsync();

        await _context.pendingProductImports
            .Where(pi => foundIds.Contains(pi.importedProductId ?? 0))
            .ExecuteDeleteAsync();

        await _context.pendingImageDownloads
            .Where(pid => foundIds.Contains(pid.productId))
            .ExecuteDeleteAsync();

        await _context.productVariantLinks
            .Where(vl => foundIds.Contains(vl.variant.productId))
            .ExecuteDeleteAsync();

        await _context.productVariants
            .Where(v => foundIds.Contains(v.productId))
            .ExecuteDeleteAsync();

        await _context.productMarketplaceLinks
            .Where(ml => foundIds.Contains(ml.productId))
            .ExecuteDeleteAsync();

        await _context.productGroupMembers
            .Where(gm => foundIds.Contains(gm.productId))
            .ExecuteDeleteAsync();

        // Collect all file IDs for token/tag cleanup
        var allFileIds = products.SelectMany(p => p.files.Select(f => f.Id)).ToList();
        if (allFileIds.Any())
        {
            await _context.fileShareTokens
                .Where(fst => allFileIds.Contains(fst.fileId))
                .ExecuteDeleteAsync();
            await _context.tags
                .Where(t => allFileIds.Contains(t.fileId))
                .ExecuteDeleteAsync();
        }

        // Folder share tokens
        var allFolderIds = await _context.productFolders
            .Where(pf => foundIds.Contains(pf.productId))
            .Select(pf => pf.Id)
            .ToListAsync();
        if (allFolderIds.Any())
        {
            await _context.folderShareTokens
                .Where(fst => allFolderIds.Contains(fst.folderId))
                .ExecuteDeleteAsync();
        }

        var storeAttrValueRepo = _unitOfWork.Repository<StoreProductAttributeValue>();
        var attrValueRepo = _unitOfWork.Repository<ProductAttributeValue>();
        var noteRepo = _unitOfWork.Repository<Note>();
        var imageRepo = _unitOfWork.Repository<ImageReference>();
        var fileRepo = _unitOfWork.Repository<Models.File>();
        var productRepo = _unitOfWork.Repository<Product>();

        foreach (var product in products)
        {
            storeAttrValueRepo.DeleteRange(product.storeAttributeValues.ToList());
            attrValueRepo.DeleteRange(product.attributeValues.ToList());
            noteRepo.DeleteRange(product.notes.ToList());
            imageRepo.DeleteRange(product.images.ToList());
            fileRepo.DeleteRange(product.files.ToList());
            productRepo.DeleteWithoutSave(product);
        }

        // Delete folders (children first due to Restrict)
        await _context.productFolders
            .Where(pf => foundIds.Contains(pf.productId) && pf.parentFolderId != null)
            .ExecuteDeleteAsync();
        await _context.productFolders
            .Where(pf => foundIds.Contains(pf.productId))
            .ExecuteDeleteAsync();

        await _unitOfWork.SaveChangesAsync();
    });

    return Ok(new { success = true, deletedCount = products.Count });
}
```

**Step 6: Add trash count endpoint**

```csharp
/// <summary>
/// Returns the count of soft-deleted products for the current store.
/// </summary>
[HttpGet]
[Route("/products/trash/count")]
public async Task<IActionResult> GetTrashCount()
{
    var selectedStoreId = await _currentUserService.GetSelectedStoreIdAsync();
    if (selectedStoreId == null)
        return Ok(new { count = 0 });

    var count = await _context.products
        .IgnoreQueryFilters()
        .CountAsync(p => p.storeId == selectedStoreId.Value && p.deletedAt != null);

    return Ok(new { count });
}
```

**Step 7: Commit**

```
feat: add trash endpoints - list, restore, permanent delete, bulk ops
```

---

### Task 5: Frontend - Vuex actions for trash

**Files:**
- Modify: `frontend/src/store/modules/productModule.js` - add new actions

**Step 1: Add trash actions**

Add these actions to the `actions` object in `productModule.js`:

```javascript
async getTrashProducts(ctx, { searchQuery, startIndex, pageSize, onSuccess, onError }) {
  const params = new URLSearchParams();
  if (searchQuery) params.set("searchQuery", searchQuery);
  params.set("startIndex", startIndex);
  params.set("pageSize", pageSize);

  return await useAxios(
    `/products/trash?${params.toString()}`,
    { method: "GET", cache: false },
    http,
    { immediate: true, abortPrevious: true },
  )
    .then((response) => onSuccess(response))
    .catch((error) => onError(error));
},

async getTrashCount(ctx, { onSuccess, onError }) {
  return await useAxios(
    `/products/trash/count`,
    { method: "GET", cache: false },
    http,
    { immediate: true, abortPrevious: true },
  )
    .then((response) => onSuccess(response))
    .catch((error) => onError(error));
},

async restoreProduct(ctx, { productId, onSuccess, onError }) {
  return await useAxios(
    `/products/${productId}/restore`,
    { method: "POST" },
    http,
    { immediate: true },
  )
    .then((response) => onSuccess(response))
    .catch((error) => onError(error));
},

async bulkRestoreProducts(ctx, { productIds, onSuccess, onError }) {
  return await useAxios(
    `/products/bulk-restore`,
    { method: "POST", data: { productIds } },
    http,
    { immediate: true },
  )
    .then((response) => onSuccess(response))
    .catch((error) => onError(error));
},

async permanentDeleteProduct(ctx, { productId, onSuccess, onError }) {
  return await useAxios(
    `/products/${productId}/permanent`,
    { method: "DELETE" },
    http,
    { immediate: true },
  )
    .then((response) => onSuccess(response))
    .catch((error) => onError(error));
},

async bulkPermanentDeleteProducts(ctx, { productIds, onSuccess, onError }) {
  return await useAxios(
    `/products/bulk-permanent-delete`,
    { method: "POST", data: { productIds } },
    http,
    { immediate: true },
  )
    .then((response) => onSuccess(response))
    .catch((error) => onError(error));
},
```

**Step 2: Commit**

```
feat: add Vuex actions for product trash operations
```

---

### Task 6: Frontend - Trash UI in productsPage.vue

**Files:**
- Modify: `frontend/src/pages/productsPage.vue`

**Step 1: Add trash-related state, methods, mapActions**

In `mapActions`, add:

```javascript
"getTrashProducts",
"getTrashCount",
"restoreProduct",
"bulkRestoreProducts",
"permanentDeleteProduct",
"bulkPermanentDeleteProducts",
```

In `data()`, add:

```javascript
// Trash mode
isTrashMode: false,
trashProducts: [],
isTrashLoading: false,
trashLastPage: false,
trashStartIndex: 0,
trashTotalCount: 0,
trashSearchQuery: "",
trashCount: 0,

// Trash dialogs
isBulkPermanentDeleteDialogOpen: false,
isPermanentDeleting: false,
isRestoring: false,
```

Add new methods:

```javascript
async fetchTrashProducts() {
  this.isTrashLoading = true;
  this.trashProducts = [];
  await this.getTrashProducts({
    startIndex: this.trashStartIndex,
    pageSize: this.pageSize,
    searchQuery: this.trashSearchQuery,
    onSuccess: (response) => {
      this.trashProducts = response.data.value.items;
      this.trashLastPage = response.data.value.lastPage;
      this.trashTotalCount = response.data.value.totalCount;
    },
    onError: (error) => displaySonnerError(error),
  });
  this.isTrashLoading = false;
},

async fetchTrashCount() {
  await this.getTrashCount({
    onSuccess: (response) => {
      this.trashCount = response.data.value.count;
    },
    onError: () => { this.trashCount = 0; },
  });
},

async toggleTrashMode() {
  this.isTrashMode = !this.isTrashMode;
  this.clearSelection();
  if (this.isTrashMode) {
    this.trashStartIndex = 0;
    await this.fetchTrashProducts();
  }
},

async handleBulkRestore() {
  this.isRestoring = true;
  await this.bulkRestoreProducts({
    productIds: this.selectedProductIds,
    onSuccess: ({ data }) => {
      toast.success(`Восстановлено товаров: ${data.value.restoredCount}`);
      this.clearSelection();
      this.fetchTrashProducts();
      this.fetchTrashCount();
    },
    onError: (error) => displaySonnerError(error),
  });
  this.isRestoring = false;
},

async handleBulkPermanentDelete() {
  this.isPermanentDeleting = true;
  await this.bulkPermanentDeleteProducts({
    productIds: this.selectedProductIds,
    onSuccess: ({ data }) => {
      toast.success(`Удалено навсегда: ${data.value.deletedCount}`);
      this.isBulkPermanentDeleteDialogOpen = false;
      this.clearSelection();
      this.fetchTrashProducts();
      this.fetchTrashCount();
    },
    onError: (error) => displaySonnerError(error),
  });
  this.isPermanentDeleting = false;
},
```

Modify `handleBulkDelete` to refresh trash count after delete:

```javascript
// In handleBulkDelete onSuccess callback, add:
this.fetchTrashCount();
```

**Step 2: Update `mounted()` to fetch trash count**

```javascript
async mounted() {
  await Promise.all([
    this.fetchStoreAttributes(),
    this.fetchCategories(),
    this.fetchTrashCount(),  // <-- add this
  ]);
  // ...rest unchanged
},
```

**Step 3: Add trash toggle button to template**

In the NavigationBar center template area, after the search+filter section, add a trash toggle button. Place it right after the closing `</Popover>`:

```html
<Button
  variant="outline"
  size="icon"
  class="h-9 w-9 shrink-0 relative"
  :class="isTrashMode ? 'border-destructive text-destructive' : ''"
  @click="toggleTrashMode"
>
  <Trash2 class="size-4" />
  <span
    v-if="trashCount > 0 && !isTrashMode"
    class="absolute -top-1.5 -right-1.5 bg-destructive text-white text-[10px] font-medium rounded-full min-w-[18px] h-[18px] flex items-center justify-center px-1"
  >
    {{ trashCount > 99 ? '99+' : trashCount }}
  </span>
</Button>
```

**Step 4: Replace table data source when in trash mode**

Wrap the `ProductsTable` and footer to conditionally use trash data. The simplest approach: change the `:data` and `:is-loading` props conditionally:

```html
<ProductsTable
  :data="isTrashMode ? trashProducts : products"
  :is-loading="isTrashMode ? isTrashLoading : isProductsLoading"
  ...rest unchanged...
>
```

**Step 5: Replace footer action buttons when in trash mode**

Replace the selection action buttons block (lines 680-707 area, inside `<template v-if="hasSelection">`) with conditional rendering:

```html
<template v-if="hasSelection">
  <div class="h-4 w-px bg-border" />
  <span class="text-sm font-medium">
    Выбрано: {{ selectedProductIds.length }}
  </span>
  <Button variant="ghost" size="sm" class="h-8" @click="clearSelection">
    <X class="h-4 w-4" />
  </Button>

  <!-- Normal mode actions -->
  <template v-if="!isTrashMode">
    <div class="flex">
      <Button variant="outline" size="sm" class="h-8 rounded-r-none border-r-0" @click="openBulkEditDialog">
        <Pencil class="h-4 w-4 mr-1" />
        Изменить
      </Button>
      <Button variant="outline" size="sm" class="h-8 rounded-l-none" @click="isExcelDialogOpen = true">
        <FileSpreadsheet class="h-4 w-4 mr-1" />
        Редактирование из таблицы
      </Button>
    </div>
    <Button variant="destructive" size="sm" class="h-8" @click="openBulkDeleteDialog">
      <Trash2 class="h-4 w-4 mr-1" />
      Удалить
    </Button>
  </template>

  <!-- Trash mode actions -->
  <template v-else>
    <Button variant="outline" size="sm" class="h-8" :disabled="isRestoring" @click="handleBulkRestore">
      {{ isRestoring ? 'Восстановление...' : 'Восстановить' }}
    </Button>
    <Button variant="destructive" size="sm" class="h-8" @click="isBulkPermanentDeleteDialogOpen = true">
      Удалить навсегда
    </Button>
  </template>
</template>
```

**Step 6: Add permanent delete confirmation dialog**

After the existing bulk delete AlertDialog, add:

```html
<!-- Permanent Delete Confirmation Dialog -->
<AlertDialog v-model:open="isBulkPermanentDeleteDialogOpen">
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>Удалить навсегда?</AlertDialogTitle>
      <AlertDialogDescription>
        Вы собираетесь безвозвратно удалить {{ selectedProductIds.length }} товаров.
        Все файлы, фото, заметки и данные маркетплейсов будут удалены. Это действие нельзя отменить.
      </AlertDialogDescription>
    </AlertDialogHeader>
    <AlertDialogFooter>
      <AlertDialogCancel :disabled="isPermanentDeleting">Отмена</AlertDialogCancel>
      <AlertDialogAction
        @click.prevent="handleBulkPermanentDelete"
        :disabled="isPermanentDeleting"
        class="bg-destructive text-white hover:bg-destructive/90"
      >
        {{ isPermanentDeleting ? "Удаление..." : "Удалить навсегда" }}
      </AlertDialogAction>
    </AlertDialogFooter>
  </AlertDialogContent>
</AlertDialog>
```

**Step 7: Add trash pagination watcher**

Add to watchers:

```javascript
trashStartIndex() {
  if (this.isTrashMode) this.fetchTrashProducts();
},
```

And add a computed for trash pagination:

```javascript
trashPage: {
  get() {
    return this.trashStartIndex / this.pageSize + 1;
  },
  set(value) {
    this.trashStartIndex = (value - 1) * this.pageSize;
  },
},
trashLastPossiblePage() {
  return Math.max(1, Math.ceil(this.trashTotalCount / this.pageSize));
},
```

Update the pagination footer to switch between normal and trash pagination when in trash mode. The pagination buttons should use `trashPage`/`trashLastPage`/`trashLastPossiblePage` when `isTrashMode` is true.

**Step 8: Update the bulk delete dialog text**

Change the existing bulk delete dialog text from "Это действие нельзя отменить" to:

```html
<AlertDialogDescription>
  Вы собираетесь удалить {{ selectedProductIds.length }} товаров в корзину.
  Их можно будет восстановить из корзины.
</AlertDialogDescription>
```

**Step 9: Hide row actions in trash mode**

In the `#rowActions` template slot, add a conditional:

```html
<template #rowActions="{ row }">
  <ProductRowActionButtons v-if="!isTrashMode" :id="row.original.id" />
</template>
```

**Step 10: Disable row click navigation in trash mode**

Update `onRowClick`:

```javascript
onRowClick(product) {
  if (this.isTrashMode) return;
  this.$router.push({
    name: PAGE_PRODUCT_EDIT,
    params: { id: product.id },
  });
},
```

**Step 11: Commit**

```
feat: add product trash UI - toggle, restore, permanent delete
```

---

### Task 7: Version bump + build verification

**Files:**
- Modify: `backend/PlatformVersion.cs`

**Step 1: Bump version**

`1.6.3` → `1.6.4`

**Step 2: Build both projects**

```bash
cd backend && dotnet build
cd frontend && npx vite build
```

**Step 3: Commit**

```
chore: bump platform version to 1.6.4
```

---

## Files to modify (summary)

| File | Change |
|------|--------|
| `backend/Models/Products/Product.cs` | Add `deletedAt` field + DTO mapping |
| `backend/DataContext.cs` | Add global query filter + composite index |
| `backend/Controllers/ProductController.cs` | Soft delete + trash/restore/permanent endpoints |
| `frontend/src/store/modules/productModule.js` | Vuex actions for trash API |
| `frontend/src/pages/productsPage.vue` | Trash mode toggle, restore, permanent delete UI |
| `backend/PlatformVersion.cs` | Version bump |

## Verification

1. `dotnet build` - backend compiles
2. `npx vite build` - frontend compiles
3. Delete a product → disappears from list, appears in trash
4. Restore from trash → returns to product list
5. Permanent delete from trash → gone from DB with all related data
6. Bulk operations work for all 3 actions
7. Trash badge shows correct count
8. Existing product queries (marketplace sync, etc.) automatically exclude deleted products via query filter
