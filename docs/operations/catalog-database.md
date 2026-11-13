# Catalog database operations

This reference describes catalog query and transaction boundaries, plus checks for
slow requests during imports. It does not establish a production latency baseline:
local functional tests and SQL translation tests cannot substitute for production
MySQL plans, lock waits, and host resource measurements.

## Catalog reads

`GET /products` keeps the existing filters, count, sorting, and response shape.
It counts the filtered query, selects ordered page IDs once, then loads bounded
review projections for those IDs. Search/relevance sorting is not repeated for
every collection. Rows contain one gallery image (or an image-file fallback),
store attribute values and ordered File IDs, and small adapter/mapping summaries.
They do not materialize mapping `graphJson`, connection credentials, product
descriptions, attribute dictionaries, or every file/gallery item.

The mapping editor uses `GET /products/lookup` only when opening/searching the
preview selector. It returns at most 50 ID/name/SKU choices in the selected store,
without a count or catalog collections. A saved preview product's label loads
independently of the editor. Neither route includes trashed products.

Requests taking at least 500 ms emit `Slow product list` with count, page-ID, row
load, and total elapsed milliseconds. The total also includes access checks,
DTO construction, and mapping enrichment, but not HTTP response serialization or
thumbnail/download requests. Logs omit search text and product content. SQL tags
`Catalog.Products.Count`, `Catalog.Products.PageIds`, and
`Catalog.Products.ReviewRows` help locate the corresponding commands.

## Permanent deletion and transactions

Single and bulk permanent deletion share the same dependency cleanup. Bulk
deletion processes up to 100 products per batch **inside one transaction**.
An error or cancellation rolls back earlier batches; batching bounds loaded
products/variants, not transaction lifetime or the number of held locks.
Only trashed products belonging to the selected store are eligible, preserving
the existing selection behavior. Related rows are deleted in sets rather than
materializing files, notes, gallery entries, and attribute values into EF.

File attribute references are removed before files because their FKs restrict
file deletion. Root and folder shares are both removed. Folder parent references
are cleared before deleting the hierarchy, avoiding MySQL's immediate,
row-by-row self-referencing `RESTRICT` checks. Product and variant deletion still
uses `SaveChanges`; barcode validation is not bypassed.

No marketplace deletion/export is performed. As before, this path removes file
database records, not MinIO objects; orphan-object reclamation is a separate
storage lifecycle concern.

Transaction rules for other expensive workflows:

- A single `SaveChanges` is already transactional on relational EF providers.
  Multiple related `SaveChanges`/set-based writes need an explicit transaction
  when partial persistence would violate the operation's contract.
- Do not hold a database transaction or store-row barcode lock while waiting for
  marketplace HTTP, media download/upload, or AI inference. Fetch/prepare external
  data first, commit a short local unit, and execute external side effects through
  durable jobs with retries/idempotency or explicit compensation.
- Large resumable jobs may commit per item/batch only when partial completion is
  part of their documented result. Do not silently change an all-or-nothing delete
  into such a job. Smaller committed batches release locks but cannot be rolled
  back by a transaction in a later batch.

### Other destructive workflows

Single-category deletion uses the same atomic save as bulk category deletion:
detaching trashed products cannot commit before a category-delete failure.
Product field/attribute replacement is also covered by a real MySQL failure test:
failed insertion of replacement values preserves both old values and card fields.

Store deletion runs through the configured MySQL execution strategy, serializes
the last-store check with store-row locks, and uses set-based cleanup rather than
tracking entire histories. It includes trashed folder trees, clears restricting
File references and category FKs before cascade deletion, and purges scoped
queue/chat/audit/schedule/cache data. Storage cleanup runs only after commit:
both legacy `<storeId>/` and current `stores/<storeId>/` prefixes, plus the staged
chat/archive keys captured before deleting their rows. Branding keeps its own
post-commit cleanup. Blob cleanup remains best-effort, not an atomic filesystem
transaction. Deleting a large store can hold locks until its DB transaction ends.

Transfers stage file copies under opaque, store-scoped transfer paths before
opening a DB transaction. The transaction covers category creation/synchronization,
group creation, the cloned catalog structure, and move-source trashing. File-copy
failure retains existing partial-copy semantics; a database file-row failure
aborts the whole DB unit. Cancellation/failure removes staged objects best-effort
and restores the pre-attempt tracker state, including many-to-many memberships.
Real MySQL lock-timeout tests verify DB retries do not copy files again or create
duplicate categories/groups. Operation logs and persisted progress are deferred
until commit; failed notifications do not delete committed file objects.

Queue retention deletes at most 500 queue rows per statement, yielding between
committed batches. Terminal operations and their string-keyed logs are deleted
together in 500-operation transactions. Old orphan logs from earlier versions
are recovered separately without deleting recent orphan/active-operation logs.
Completed/failed/cancelled Catalog Connect jobs are included. The EF-generated
`OperationLogRetentionIndex` migration indexes log timestamps for recovery.
One operation with an exceptionally large log can still be a large delete unit;
measure its lock duration rather than assuming operation count bounds log bytes.

## Background work and indexes

Product imports select queue candidates without tracking, then resolve a new DI
scope/context per product. Large attribute dictionaries and failed entity writes
cannot accumulate across a polling batch. Product and image progress is grouped
in SQL; full queue rows, image URLs, and errors are not fetched for every counter
refresh. Existing retry/stop semantics and worker concurrency remain unchanged.

### Atomic new-product imports

Marketplace fetch/price requests and preparation happen before persistence. The
prepared graph contains the card, mapped category/store values, size variants,
marketplace links, optional group/membership, and media queue rows. A single
`SaveChanges` commits the graph under EF's relational transaction and existing
barcode validation. No adapter HTTP, media bytes, DNS validation, or operation
log writes run inside that write transaction. Preparation and failed attempts
discard only their own tracked entities, leaving the caller's queue state intact.

The unique connection/external-product link is the marketplace import retry key.
A retry reuses even a trashed linked card; it does not restore the card or enqueue
duplicate media. A concurrent product/group uniqueness conflict rebuilds from
committed state once, without refetching the marketplace. Other failures roll
back the new graph and remain visible as import failures.

Public-card copies intentionally create no seller-account link. When a durable
operation ID is available, a hashed operation/store/connection/external-ID receipt
is committed with the card. Replaying that operation returns the same product;
a different operation can intentionally copy it again. Calls without an operation
ID remain explicit, non-idempotent copies. Receipts cascade on permanent product
deletion. Neither mechanism repairs partially imported data from earlier versions.

The shared barcode namespace includes a card and its own variants. When a selected
card barcode is also owned by an imported variant (including variant fallback),
the variant keeps the code and the parent remains unassigned rather than creating
a duplicate claim or half-imported card.

### Bulk edit commits and restart recovery

Bulk edits retain partial-result semantics, unlike all-or-nothing permanent
deletion. They normalize duplicate IDs and load at most 50 products plus their
values/variants at a time. A successful save commits that chunk's changes and its
job `processedCount`, `completedCount`, and `failedCount` together. Progress is
reported only after persistence. A failed save rolls back the entire chunk;
every item in that chunk is counted as failed, tracked product/value/variant
mutations are discarded, and later chunks can proceed. Failure of the job-only
checkpoint save aborts processing rather than claiming a durable failed result.

Cancellation discards the uncommitted chunk and preserves earlier committed
counts. A restarted durable job skips its committed prefix, so `priceDelta` is
not applied twice. `processedCount` is also an optimistic concurrency token: a
worker with a stale checkpoint cannot commit another application of that prefix.
The losing worker reloads the job without overwriting the other worker's result.

The EF-generated `AtomicCatalogImportAndBulkCheckpoints` migration adds receipts
and the job checkpoint. Existing jobs receive `processedCount = -1`, while new
jobs explicitly start at zero. Legacy unfinished jobs are rejected for automatic
execution because neither old counters nor a transaction can establish which
old price deltas already committed. Drain jobs before rollout where possible;
otherwise inspect legacy partial changes before deliberately requeueing them.
Previously committed effects and new receipts/checkpoints are not backfilled.

The EF-generated `CatalogQueryIndexes` migration adds:

- `products(storeId, barcode)` and `productVariants(barcode, productId)` for
  barcode equality/current reads. The store lock and shared product/variant
  barcode namespace remain in place.
- `products(storeId, deletedAt, categoryId, Id)` for category-scoped catalog pages.
- `pendingImageDownloads(status, createdAt)` for queue polling and a 191-character
  prefix index on `batchId` for progress lookup. The existing `LONGTEXT` column is
  not narrowed or truncated.

Back up MySQL and schedule index creation during low traffic on large tables.
The instance applies migrations on startup; allow sufficient startup time and
verify the migration completed before comparing latency. Do not disable barcode
locking or increase the connection pool as a substitute for inspecting plans.

## Verification on the designated test instance

Do not run exports against existing configured marketplace adapters. Reproduce
imports only on the user's designated test instance.

1. Record `/products?pageSize=25` TTFB for repeated idle requests, including first
   and later pages, category filters, and the actual slow sort/search. Separate
   API time from image/thumbnail requests and browser rendering.
2. Repeat during the reported 92-product Ozon import plus media download. Capture
   API/MySQL/MinIO CPU, memory, disk latency, and connection usage at the same time.
3. Inspect slow-list phase logs. Use the actual tagged SQL and bound test values
   with MySQL `EXPLAIN ANALYZE`; examine rows scanned, filesorts, and the chosen
   indexes. Diagnose locks using `performance_schema.data_lock_waits`,
   `information_schema.innodb_trx`, and `SHOW ENGINE INNODB STATUS` with appropriate
   operator permissions. Do not publish statements containing private catalog data.
4. Confirm mapping initialization makes no catalog-list request. Opening the
   preview selector should issue only the lean lookup request.
5. On disposable test products, delete more than 100 trashed products, including
   nested folders, File attributes, variants, and shares. Inject a later-batch
   failure in automated tests and verify earlier deletions roll back.
6. Report p50/p95 and the largest observed latency before and after, both idle
   and under load. A sub-second target is not verified until these measurements
   are available.

### Repeatable isolated MySQL checks

`backend.Tests/LocalMySqlCatalogTests.cs` is opt-in through
`DELEGAT_TEST_MYSQL`. It only accepts a loopback server and creates/drops its own
random `delegat_it_<guid>` schemas; it never migrates, seeds, or deletes a schema
named in the supplied connection string. Use an explicitly authorized local test
account with schema-creation privileges. Without the variable these tests skip.

```powershell
$env:DELEGAT_TEST_MYSQL = 'Server=127.0.0.1;Port=13308;User ID=<test-user>;Password=<test-password>;SslMode=None;'
dotnet test backend.Tests/Delegat.Tests.csproj -p:BuildPlugins=false --filter FullyQualifiedName~LocalMySqlCatalogTests --logger 'console;verbosity=detailed'
```

The suite covers fresh migrations and an upgrade from the pre-fix schema,
barcode-locking import rollback, bulk restart checkpoints, permanent deletion,
category/store rollback, attribute replacement, transfer staging/cancellation,
an actual MySQL lock-wait timeout/retry, retention, and concurrent catalog reads.
All adapters and object-storage operations are fakes. No application host or
export worker is started, and no marketplace export or real media HTTP is sent.

One warmed local MySQL 8.4.0 run on 2026-11-13 used 1000 synthetic cards, four
images per card, a 512 KiB mapping graph, and a 100-option store dictionary:

- Legacy list query shape, idle: 8 samples; p50 51.0 ms, p95/max 92.4 ms.
- Projected controller plus DTO serialization, idle: 12 samples; p50 40.7 ms,
  p95/max 63.8 ms.
- Two concurrent readers during 92 fake imports and 184 fake media downloads:
  60 samples, all overlapping background work; p50 41.4 ms, p95 49.6 ms,
  maximum 65.6 ms. All imports/media rows completed.

The final full backend run passed all 3370 tests, including the 12 opt-in MySQL
tests. In this fixture, category pagination used a reverse lookup on the existing
`IX_products_categoryId` index and returned 25 rows in about 0.07 ms; this is not
evidence that the optimizer selected the new composite index.

These small-sample results establish a local functional/concurrency baseline,
not a production speedup claim. The legacy measurement is its old SQL/DTO shape,
not a separately running old API build. Timings exclude HTTP transport,
authentication, browser rendering, thumbnail generation, and actual Ozon/MinIO
network/disk work; both query pipelines are warmed. The reported 6–9 second
production delay was **not reproduced**. Inspect the real instance's phase logs,
resource limits and lock waits before attributing that delay to a specific host
or promising the same local timings in production. The test also prints a real
`EXPLAIN ANALYZE` plan for category-scoped pagination.

## Remaining database work to prioritize with evidence

The source review also covered shared `DataContext`/barcode saves, the unit of
work, bulk updates, partner product reads, category/mapping reads, quotas, queue
polling/cleanup, operation logging, store deletion, and import/export services.
This is not a claim that every query or transaction in the repository is audited.

- Existing partial imports or uncertain legacy bulk-edit effects require explicit
  operator reconciliation. Do not infer ownership from a similar SKU/name or
  silently delete/reapply old catalog data as part of migration.
- Substring search across names, codes, links, and variants is not made indexable
  by adding ordinary B-tree indexes. Deep offset pages and store-attribute sorts
  can also remain expensive. Preserve search semantics; evaluate cursor paging or
  an explicit search index only against representative plans and product needs.
- Quota checks calculate file/storage usage repeatedly during media import;
  operation logging uses separate scopes/writes; export counters still read
  status snapshots. Measure their contribution before introducing counters,
  caches, or throttling that could change correctness or progress behavior.
- Store deletion is intentionally atomic and retention batches can include large
  individual operation logs. Monitor lock duration and startup/cleanup overlap
  against actual production history sizes.
- Workers share the API process, MySQL pool (50 connections), CPU, and storage.
  Separate workers/resource budgets may be appropriate if measured contention
  remains after query fixes; merely adding parallelism can worsen it.

Local coverage includes MySQL query translation, response/order/store isolation,
SQLite relational deletion and cross-batch rollback, mapping load behavior,
import rollback at individual dependent-table writes, operation-scoped copy
receipts, bulk failed-chunk isolation, cancellation/restart, and stale-checkpoint
conflicts without double price deltas. The opt-in MySQL suite exercises the
provider-specific paths and actual lock waits described above. Neither SQLite
nor the synthetic local MySQL fixture substitutes for production measurements.
