# Integration reliability audit
Date: 2026-11-20. Status: expanded source audit; proposed remediation, not implemented fixes and not a live reliability measurement. Initial offline probes used host revision 270b122d8686a874594c4718ff005879bde1ce00 plus pre-existing unrelated worktree edits. The complete contract/HTTP/commerce/rate-limit expansion uses host revision 71318b9aae47401f4306cd3f858393994f9521ea and the adapter revisions recorded in Snapshot provenance below. SDK revision is 8a040961f904574059e9937556a3bd27959d203d. Scope now includes all seven adapters, all 50 named IMarketplacePlugin methods, all capability enum values, SDK/host boundaries, orders, shipments, promotions, groups, HTTP routes and rate admission. Complete static inventory is not category-by-category or end-to-end certification; source revisions do not prove deployed binaries. The current API-documentation baseline is the local 2026-11-20 bundle, superseding earlier online excerpts and excluding July copies. Local route/schema/limit comparisons and their gaps are distinguished below. No production logs, database, real marketplace calls or current seller permissions were accessed; selected deprecation/change notices were read, not a complete API-history diff. No failure-rate measurement or attribution of a particular incident to an upstream API change is possible from this audit.

> Primary objective: correct end-to-end integration operations, not merely better error reporting. Diagnostics/security are supporting workstreams.

Sections: [Verdict](#verdict) / [Execution-first target design](#execution-first-target-design) / [Implementation gates](#current-proposed-rollout-and-acceptance) / [Current local documentation baseline](#current-local-documentation-baseline) / [Complete seven-adapter contract inventory](#complete-seven-adapter-contract-inventory) · [Shared SDK, host orchestration, and adapter-specific commerce](#shared-sdk-host-orchestration-and-adapter-specific-commerce) · [Called HTTP endpoint documentation ledger](#called-http-endpoint-documentation-ledger) · [Unexecuted test-environment acceptance work](#unexecuted-test-environment-acceptance-work) · [Rate limits: declared status versus actual request admission](#rate-limits-declared-status-versus-actual-request-admission)

## Verdict

**The architectural decision is ready: keep the plugins, but refactor the shared execution boundary around a durable business command, not around one adapter call or one queue row.** The evidence already establishes internal correctness defects without requiring an upstream API change. Endpoint fixes are necessary maintenance, not a sufficient reliability strategy. This is not a proposal to rewrite the whole platform, replace the queue product, or solve the problem by adding catches and logs.

### What is structurally wrong

1. **There is no uniform meaning of "the operation is complete".** Card acceptance, applied fields, dependent prices/stocks/media, and final publication are different outcomes. F3/F11/F15 show that requested work can be omitted or rejected while a broader export appears successful. SDK [ExportResult](../../sdk/Delegat.Marketplace.SDK/DTOs/Results.cs#L82-L107) has Success, Retryable and untyped Details, but no standard durable stage receipt or explicit accepted/unknown/applied contract.
2. **Business workflows are split between entry paths and layers.** Single versus bulk changes business steps (F11); worker grouping changes ownership (F4); prices/stocks, order actions, shipment steps, promotion changes and groups do not all pass through the same lifecycle. For example, [order actions](../../backend/Controllers/PublicApi/PublicMarketplaceOrdersController.cs#L101-L129), [shipments](../../backend/Controllers/PublicApi/PublicMarketplaceShipmentsController.cs#L63-L80) and [promotions](../../backend/Controllers/PublicApi/PublicMarketplacePromotionsController.cs#L87-L105) invoke adapters in the caller's request. [InvokeAsync](../../backend/Services/Marketplace/MarketplaceCommerceService.cs#L57-L81) maps exceptions; it is not a durable command coordinator.
3. **Persistence is too coarse for safe external recovery.** Queues/history/retries exist, but a pending export row is not a record of every remote effect. F2/F4/F5/F12/F17 expose premature finalization, mixed owners, unknown accepted writes, mismatched lifetimes and unchecked result coverage. A restart cannot safely infer "not performed" from "no result saved". Replaying the whole command can repeat already-applied steps.
4. **Readiness does not validate the exact provider request that execution will send.** F13/F14/F16 show metadata failure, graph omission and invalid values crossing a nominally ready boundary. Category schema, variants, warehouse identity and operation version must be inputs to one preparation/validation path, not separate approximations in the UI, host and plugin.
5. **Support and policy are weaker than the advertised operation.** Broad capability bits do not describe action/direction/state support; rate metadata does not govern every outbound request; some errors and empty/stale results conceal incomplete work (F7/F9/F10 and the rate analysis). The local D1-D4 contract deltas demonstrate that finding the route or overriding the method does not establish semantic compatibility.
6. **There is no end-to-end proof behind the whole support surface in this audit.** Existing tests are valuable, but the selected green gate coexists with reproduced correctness failures (F18). No isolated shop or provider acceptance run has certified the claimed final state across the supported profiles. A flag, stable label, HTTP 200 or passing serializer test must not substitute for that evidence.

These are interacting causes: a schema mismatch should be caught by conformance/preflight; an item rejection must block aggregate success; an accepted response must survive a crash; a delayed dependency must not replay the card. Fixing only the first observed exception leaves the other paths intact.

### The reliability promise we should make

For supported, valid input, perform the declared business intent through its required stages, preserve its evidence, and recover without omissions or unsafe duplicate effects. For invalid or unsupported input, block before mutation with a precise reason. During a provider delay or quota wait, retain the operation and continue safely. Never relabel an unverified remote result as applied just to remove an error from the interface.

External obstacles are not limited to HTTP 500: revoked credentials, permissions, limits, changing validation/moderation, concurrent seller tools and lost responses also exist. They do not excuse wrong local serialization, false success, lost work or blind replay. Our responsibility is to handle them as explicit preconditions, waits, conflicts or unresolved outcomes rather than exposing an unexplained exception as the workflow.

No client can guarantee exactly-once remote effects if the provider offers neither idempotency nor a reliable way to discover a write after its response is lost. In that case the correct controlled behavior is a persisted unknown outcome, reconciliation and, if evidence cannot resolve it, explicit review without automatic resubmission. Even a 5xx after a write is not proof that the provider made no change.

### Decision readiness versus completion

**The investigation is sufficient to choose the architecture and the first engineering work package; the integrations are not yet remediated or certified.** Keep the existing foundations below, including atomic local import graphs/receipts. Do not spend another round merely counting matching endpoints before addressing execution correctness.

The remaining evidence work is implementation-facing: reproduce the agreed invariants through the real host flow, prove recovery and final state in the isolated environments, validate installed bridges and close each supported conformance profile. Current failure frequency, all category combinations and deployed-version behavior remain unmeasured. The rollout below gives concrete gates; it does not claim those gates already pass.

## Verification evidence (2026-11-20)

Factual execution evidence collected while packaging this snapshot. Reproduction passes below assert **current defects**; they are not corrected behavior.

### Offline SDK probe (defect reproduction)

- Source: `C:\Users\ARTEM\AppData\Local\Temp\delegat-integration-probe-270b122\` (`Probe.csproj`, `Program.cs`) — temporary console project outside the repository, referencing `sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj`; in-memory fake requests and a fake plugin only, no network.
- Command: `dotnet run --project "C:/Users/ARTEM/AppData/Local/Temp/delegat-integration-probe-270b122/Probe.csproj" -p:GeneratePackageOnBuild=false`
- Result: exit code 0, all four reproduction assertions passed:

```
REPRODUCED: X-Delegat-Api-Key survives SDK header redaction
REPRODUCED: SDK HTTP helper emits no operation correlation
REPRODUCED: SDK bulk fallback loses Retryable=false and Details
REPRODUCED: SDK bulk fallback converts cancellation into retryable failure
```

These executions confirm the code paths cited in F1 (header redaction gap), F6 (no operation correlation emitted by the helper), and F10 (lost `Retryable`/`Details` semantics and cancellation converted to a retryable failure in the SDK bulk fallback).

### Narrow existing-test gate

- Command: `dotnet test backend.Tests/Delegat.Tests.csproj --configuration Debug -p:BuildPlugins=false -maxcpucount:1 --filter "FullyQualifiedName~AdapterHttpLogServiceTests|FullyQualifiedName~AdapterHttpLogCleanupTests|FullyQualifiedName~OperationLifecycleTests|FullyQualifiedName~PluginCapabilitySmokeTests|FullyQualifiedName~MarketplaceApiErrorTextTests|FullyQualifiedName~WildberriesRateLimitAndCacheTests" --logger "console;verbosity=minimal"`
- Result: `Passed!  - Failed: 0, Passed: 48, Skipped: 0, Total: 48` — current-behavior contract/smoke tests only; not remediation acceptance tests.

### Offline host-flow probe

- Source: `C:\Users\ARTEM\AppData\Local\Temp\delegat-flow-probe-270b122\` (`FlowProbe.csproj`, `Program.cs`) — temporary console project outside the repository, referencing `backend.Tests/Delegat.Tests.csproj`; invokes the existing `ProductMediaExportFlowTests` in-memory scenario via reflection and runs the actual production `ProductSyncService`/`MappingService` code with a `DispatchProxy` fake adapter. No marketplace, HTTP, or database network calls.
- Command: `dotnet run --project "C:/Users/ARTEM/AppData/Local/Temp/delegat-flow-probe-270b122/FlowProbe.csproj" -p:BuildPlugins=false -p:GeneratePackageOnBuild=false -p:UseSharedCompilation=false -maxcpucount:1`
- Result: exit code 0, all three reproduction assertions passed:

```
REPRODUCED: actual host reports product success after both follow-up results reject the item
REPRODUCED: actual readiness reports ready for invalid required numeric input without sending a card
REPRODUCED: overlapping operations for one product throw before dispatch and leave both queue rows persisted InProgress
```

Confirms F3 and the numeric-readiness example in F14 in the actual host service with a fake adapter/in-memory database; not real marketplace confirmation and not a remediation. The third assertion invokes the actual `ProductExportBackgroundService.ProcessBulkExportAsync` worker method directly via reflection with in-memory pending rows — it does not start the polling loop or any background service.



## Existing foundations to retain
Durable import/export queue rows and retry budgets; operation history and SignalR progress; outbound HTTP log sink with body limits, redaction, separate summary/detail retention; SDK rate limiter and adapter-specific 429 handling; mapping/readiness checks; substantial offline tests. Imports have atomic prepared local graphs and receipt protections described in [catalog-database.md](../operations/catalog-database.md). Wildberries new-card confirmation already polls for catalog/draft evidence; Ozon already polls import task status. These are useful partial mechanisms, not evidence that every path has the same guarantees. HTTP log retention intentionally bounds storage: normally 7-day summaries, 24-hour details and at most 5,000 detailed records instance-wide (also time/cleanup effects). Increasing all payload limits is not a reliability fix.

## Operational flow findings
### F11 - P1 - Single and bulk export are different business algorithms
Single [ProductSyncService.ExportProductAsync](../../backend/Services/Marketplace/ProductSyncService.cs#L695-L752) performs card upsert, saves links/variant IDs, then calls price and stock export. Bulk [ExportProductsBulkAsync](../../backend/Services/Marketplace/ProductSyncService.cs#L1055-L1063) prepares items and calls the plugin once, persists links/variant IDs, optionally syncs endpoint groups and returns ([1200-1239](../../backend/Services/Marketplace/ProductSyncService.cs#L1200-L1239)); it has no equivalent shared price/stock follow-up. The worker chooses the path from whether the current connection group has more than one item ([ProductExportBackgroundService.cs:125-136](../../backend/Services/ProductExportBackgroundService.cs#L125-L136)). Thus queue shape, not just user intent, changes which stages run. Some plugins put price in the card request and shop bridges can include quantity, so this does NOT mean every adapter loses every price. It does mean dedicated-stage behavior has no parity guarantee; e.g. a WB bulk card call does not invoke the marketplace stock API. A one-item tail of a batch can get a different algorithm. This is an actual flow discrepancy, not a logging issue.

### F12 - P1 - Interactive export lifetime is shorter than supported provider work
Cabinet single export directly awaits the full operation in [ProductSyncController.cs:216-245](../../backend/Controllers/ProductSyncController.cs#L216-L245) with the request cancellation token. [frontend/src/store/modules/marketplaceModule.js:789-800](../../frontend/src/store/modules/marketplaceModule.js#L789-L800) imposes a 3-minute HTTP timeout (also link export around [1440-1450](../../frontend/src/store/modules/marketplaceModule.js#L1440-L1450)). WildberriesPlugin.ConfirmCreatedCardsAsync supports up to 30 minutes for new-card confirmation ([CardCreateMaxWait](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L4207), [ConfirmCreatedCardsAsync](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L1353)); the host logs that expectation in [ProductSyncService.cs:529-533](../../backend/Services/Marketplace/ProductSyncService.cs#L529-L533). The browser can therefore time out while the remote action is accepted or still processing. That is not evidence the card was rejected and not fixed by increasing every timeout. Use the same durable admission/worker path for single and bulk actions; the browser waits for an operation ID, not marketplace completion. Apply only when provider processing exceeds the interactive lifetime; no production frequency is claimed.

### F13 - P1 - Category metadata failure can silently change payload semantics
OzonPlugin.GetCategoryFieldsAsync (roughly [863-898](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L863-L898)) loads options for dictionary attributes and sets select/multiselect only INSIDE the successful dictionary-loading try. catch leaves the primitive/string type, then caches the resulting field list for six hours ([CategoryFieldsTtl](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L54)). BuildImportItem around [2670-2775](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L2670-L2775) chooses DictionaryValueId versus text from this _fieldTypes metadata. A temporary dictionary request failure can thus change a field's meaning rather than report schema unavailable. This is a concrete category-dependent reliability mechanism; whether a specific Ozon dictionary accepts free text still depends on that attribute. Preserve dictionary kind/id independent of option-fetch success; options unavailable != free-text field. Partial schemas must be marked incomplete, not cached as authoritative complete schemas.

### F14 - P1/P2 - Readiness is not adapter request validation, and schema freshness is weak
CheckExportReadinessAsync checks saved-graph required values for null/blank strings ([ProductSyncService.cs:150-165](../../backend/Services/Marketplace/ProductSyncService.cs#L150-L165)). Runtime single and bulk repeat similar checks ([417-456](../../backend/Services/Marketplace/ProductSyncService.cs#L417-L456), [971-1014](../../backend/Services/Marketplace/ProductSyncService.cs#L971-L1014)), not the final provider request. SDK ExternalField carries MaxLength, Min/MaxValue, ValidationPattern, MaxValues and AllowCustomValues, but Graph.AdapterField ([MappingGraph.cs:158-194](../../backend/Services/Marketplace/Graph/MappingGraph.cs#L158-L194)) does not preserve those constraints; the readiness/runtime paths do not enforce them. Empty lists and invalid numeric text can pass this gate; [MappingFieldCoercion.cs](../../backend/Services/Marketplace/Graph/MappingFieldCoercion.cs) deliberately returns unparseable values unchanged. Product-field auto bindings are marked not required when constructing field specs ([CategoryImportService.cs:424](../../backend/Services/Marketplace/CategoryImportService.cs#L424) and [PluginVersionSyncService.cs:287](../../backend/Services/Plugins/PluginVersionSyncService.cs#L287), sourceType != ProductField && field.IsRequired), so seller-required name/SKU/weight may not be enforced by this gate. [GraphGenerator.SyncAdapterFields](../../backend/Services/Marketplace/Graph/GraphGenerator.cs#L236-L303) preserves existing bindings but only refreshes units/group/description/repeatOf, not existing outputType/isRequired; PluginVersionSyncService only calls it when NEW keys exist and skips startup syncing when plugin version is unchanged. Category requirements can change without a plugin version change. Do not treat a green readiness item as proof the final request is valid. Separate provider schema constraints from user bindings, track schema identity/freshness, and validate the compiled provider payload using the same compiler as execution. Never recommend disabling genuine marketplace requirements to force a pass.

### F15 - P1 - Variant and warehouse policy is not carried consistently through the flow
Host injects _variants with local IDs, linked external IDs, size, price and quantity ([ProductVariantExport.cs:16-28](../../backend/Services/Marketplace/ProductVariantExport.cs#L16-L28)). WB BuildUpdateSizes ([WildberriesPlugin.cs:3881-3923](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3881-L3923)) enumerates EXISTING remote sizes and can merge linked prices, but cannot append a newly added local size; that local change can be silently absent from an otherwise successful update. Single host auto-stock follow-up creates one StockInfo with the parent's storageQuantity and no VariantId/WarehouseId ([ProductSyncService.cs:732-736](../../backend/Services/Marketplace/ProductSyncService.cs#L732-L736)). WB ExportStocksAsync picks warehouses[0] and, absent VariantId, applies quantity only to chrtIds[0] ([2583-2634](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2583-L2634)). Ozon BuildImportItem does not consume _variants at all; [MarketplaceCapabilities](../../sdk/Delegat.Marketplace.SDK/MarketplaceCapabilities.cs) has no per-variant create/update capability contract. This is not a claim that all marketplaces represent variants alike: it proves that exporting a local multi-variant product is not a uniformly supported operation and unsupported dimensions are not uniformly rejected. Declare category/operation-level support, choose warehouse explicitly, plan per-variant stock, and require final identity matching for every intended variant. Never infer intended warehouse from API list order.

### F16 - P1/P2 - Graph computation errors can become silent field omissions
[GraphEvaluator.cs:354-359](../../backend/Services/Marketplace/Graph/GraphEvaluator.cs#L354-L359) catches node failures and leaves a null pin; TopologicalOrder at [623-649](../../backend/Services/Marketplace/Graph/GraphEvaluator.cs#L623-L649) drops cyclic nodes. [MappingService.EvaluateGraphAsync](../../backend/Services/Marketplace/MappingService.cs#L63-L82) returns an empty result for malformed/missing graph JSON. These are useful editor-preview behaviors but execution consumes the same Outputs without a typed compilation-error contract. Missing optional outputs can be omitted silently; missing required outputs become generic missing-field messages without the causative node. Export compilation must surface structural/node errors and identify affected promised fields, while the editor may still offer partial previews. Do not make every unused disconnected editor node fatal; validate the active exported subgraph.

### F17 - P1/P2 - Bulk-result coverage is not validated as a protocol invariant
[ProductSyncService.cs:1083-1209](../../backend/Services/Marketplace/ProductSyncService.cs#L1083-L1209) iterates whatever plugin Items returns, without a final exact request-set reconciliation on the normal-success path. Missing plugin items do not become explicit item failures/unknown outcomes. ProductExportBackgroundService updates only returned IDs, selecting the first success if duplicate IDs exist (around [237-242](../../backend/Services/ProductExportBackgroundService.cs#L237-L242)); omitted requested rows can remain InProgress until stale replay. Validate exactly one authoritative result per requested identity; treat missing/foreign/conflicting duplicate results as adapter protocol faults requiring safe reconciliation, not permission to resend the whole card. Some bridge adapters already synthesize missing per-item failures; the host contract must not rely on each plugin remembering this.

### F18 - P2 - Test seams and current tests do not certify the full workflow
Ozon has an injected client factory and its HTTP client accepts a supplied HttpClient; WB HTTP client has an internal HttpClient seam but its plugin CreateClient is static; Yandex HTTP calls use static SharedHttpClient. The SDK does not require a common injectable transport/clock/confirmation interface. Host [ProductMediaExportFlowTests](../../backend.Tests/ProductMediaExportFlowTests.cs) use a fake adapter; pure adapter mapper tests verify field shapes; [frontend/e2e/bulk-adapter-mapping.spec.ts](../../frontend/e2e/bulk-adapter-mapping.spec.ts) explicitly mocks all API traffic and does not export. These tests are valuable, but none of those particular tests crosses the whole host -> actual plugin -> HTTP transport -> remote task -> read-back flow. The passing 48-test gate is not evidence that all category/adapter workflows work. This audit does not claim there are no useful integration tests anywhere. A shared harness must exercise real production adapter code against controlled fake HTTP providers and real local shop bridges.

## Verified findings
### F1 - P1 - Bridge credentials can enter diagnostic headers
[OpenCartApiClient.cs:193-196](../../plugins/Delegat.Marketplace.OpenCart/OpenCartApiClient.cs#L193-L196) and [WooCommerceApiClient.cs:237-240](../../plugins/Delegat.Marketplace.WooCommerce/WooCommerceApiClient.cs#L237-L240) send X-Delegat-Api-Key as well as Authorization. [sdk/Delegat.Marketplace.SDK/AdapterHttpLogHelper.cs:23-27](../../sdk/Delegat.Marketplace.SDK/AdapterHttpLogHelper.cs#L23-L27) masks Authorization but not X-Delegat-Api-Key. Host [AdapterHttpLogService.cs:101-108](../../backend/Services/Marketplace/AdapterHttpLogService.cs#L101-L108) likewise omits that header; Sanitize at [465-481](../../backend/Services/Marketplace/AdapterHttpLogService.cs#L465-L481) only truncates and applies the media-grant redactor to already-serialized headers, not general header-secret masking. With adapter HTTP logging enabled, the bridge key can therefore be persisted unmasked. This proves a code path, not actual credential exposure in any deployed instance. Priority: fix masks at both boundaries and use credential-aware safe capture; inspect retained logs only under authorized access, rotate affected credentials if exposure is confirmed. Never print real secrets or silently purge logs.

### F2 - P1 - An operation can finish before its automatic retries finish
[ProductExportBackgroundService.cs:81-85](../../backend/Services/ProductExportBackgroundService.cs#L81-L85) selects Failed rows with retryCount < MAX_RETRIES, while [455-467](../../backend/Services/ProductExportBackgroundService.cs#L455-L467) counts all Failed rows as final and may FinishAsync immediately. [OperationLifecycle.cs:31-37](../../backend/Services/OperationLifecycle.cs#L31-L37) rejects failed -> completed; [OperationLogger.cs:126-130 and 163-166](../../backend/Services/OperationLogger.cs#L126-L166) suppress later progress/finish for terminal operations. Scenario: the only item fails on attempt one, UI becomes failed, retry succeeds on the next poll, queue row becomes Completed but operation remains failed. [ProductImportBackgroundService.cs:390-409](../../backend/Services/ProductImportBackgroundService.cs#L390-L409) has the equivalent failed-count aggregation. Distinguish attempt failure, scheduled retry, and terminal rejection; only exhaustively terminal items may finalize the operation.

### F3 - P1 - Follow-up price/stock failures may be reported as success
[ProductSyncService.cs:710-752](../../backend/Services/Marketplace/ProductSyncService.cs#L710-L752) awaits ExportPricesAsync/ExportStocksAsync but discards their ImportResult and then emits Price exported/Stock exported and returns product success. An adapter is allowed to return FailedCount/Errors without throwing. A rejected follow-up is invisible here; caught exceptions also do not affect the product-level success. Separate card, price, stock, media and grouping outcomes, preserve warnings, and derive the final user-visible status from the promised scope. For media, Wildberries can return Success=true plus an ErrorMessage warning; the single-product service logs that warning only when operation context is supplied and returns ProductExportResult.Success without carrying it. Card success must not imply gallery success.

### F4 - P1 - Bulk work mixes operation ownership
[ProductExportBackgroundService.cs:98-109](../../backend/Services/ProductExportBackgroundService.cs#L98-L109) groups only by connectionId; [133-136](../../backend/Services/ProductExportBackgroundService.cs#L133-L136) can bulk-export rows from different batchIds together. ProcessBulkExportAsync uses firstItem for the watcher/progress and finalizes only firstItem.batchId at [321-337](../../backend/Services/ProductExportBackgroundService.cs#L321-L337). Other fully drained operations in that HTTP batch can remain running; stop checks also do not cover every contributing operation. Cancellation on an account-wide permanent string error at [350-360](../../backend/Services/ProductExportBackgroundService.cs#L350-L360) affects other batches on the connection. Group by operation as well as connection, aggregate each operation independently, and make genuine connection-wide blocks explicit rather than an accidental side effect. Source-proven scenario requires overlapping queued operations; it is not an assertion that this happened on a live instance.

Overlapping operations for the SAME product are worse: ProcessBulkExportAsync saves all rows InProgress before pendingItems.ToDictionary(p => p.productId) at lines [190-202](../../backend/Services/ProductExportBackgroundService.cs#L190-L202), which throws for duplicate product IDs before the method's try/catch. Those rows remain InProgress without adapter dispatch and are excluded until stale recovery; the same grouping can repeat the collision. Queue admission does not prevent the two operations. This scenario was reproduced offline against the real worker method. Work-item identity and product concurrency must be explicit; silently dropping duplicate dictionary entries is not a correct fix.

### F5 - P1 - Accepted/unknown remote outcomes lack durable reconciliation
[OzonPlugin.cs:2342-2389](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L2342-L2389) stores the remote taskId only in the active call and produces ordinary retryable item failures when polling fails; the queue cannot resume polling that task after a restart. [ProductExportBackgroundService.cs:79-85](../../backend/Services/ProductExportBackgroundService.cs#L79-L85) blindly reselects stale InProgress exports after 30 minutes. [YandexMarketPlugin.cs:2168-2200](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L2168-L2200) returns Success with Details.async=true; [ProductSyncService.cs](../../backend/Services/Marketplace/ProductSyncService.cs) marks the link Synced and the worker completes it without a shared accepted/confirmed distinction. A lost response does not prove rejection. Some adapters have offer-id/SKU upserts or lookup safeguards, so duplicates are a risk, not a guaranteed outcome. Persist the remote receipt/task and submission checkpoint; resume confirmation, not creation. Where the provider offers no confirmable receipt or safe idempotency, use needs_reconciliation and do not auto-replay an ambiguous write. Do not claim exactly-once external effects.

### F6 - P2 - Diagnostic context is broken across layers
The single-export worker calls ExportProductAsync(productId, connectionId, ct) at [402-405](../../backend/Services/ProductExportBackgroundService.cs#L402-L405) but its service's detailed logger is a no-op unless operationId and operationStoreId are provided ([ProductSyncService.cs:227-239](../../backend/Services/Marketplace/ProductSyncService.cs#L227-L239)). SDK [ConnectionContext](../../sdk/Delegat.Marketplace.SDK/ConnectionContext.cs) has no operation/item/attempt context; [AdapterHttpLogHelper.cs:56-71](../../sdk/Delegat.Marketplace.SDK/AdapterHttpLogHelper.cs#L56-L71) never assigns the existing AdapterHttpLogEntry.OperationId property ([IAdapterHttpLogger.cs:18](../../sdk/Delegat.Marketplace.SDK/IAdapterHttpLogger.cs#L18)). The host stores OperationId but normal helper-based calls cannot supply it. [AdapterHttpLogQuery](../../backend/Services/Marketplace/AdapterHttpLogService.cs#L27) has no OperationId filter. Bulk worker also ignores per-item Details when writing operation events. Introduce a durable operation/item/attempt context independent of sampled traces, propagate it explicitly and expose authorized cross-links. Do not depend on Activity.Current alone across persisted queues.

### F7 - P2 - The error contract cannot support reliable localization or policy
SDK [Results.cs](../../sdk/Delegat.Marketplace.SDK/DTOs/Results.cs) models most failures as ErrorMessage plus optional free-form Details and Retryable=true. Host IsPermanentError at [ProductExportBackgroundService.cs:375-378](../../backend/Services/ProductExportBackgroundService.cs#L375-L378) recognizes one Russian phrase, not a stable upstream/internal error code. [OperationLogEntry.cs:3-9](../../backend/Models/OperationLogEntry.cs#L3-L9) stores only level/message/time and [OperationLogTimeline.vue:79-91](../../frontend/src/components/operations/OperationLogTimeline.vue#L79-L91) renders message text. Translating UI labels cannot translate such an event stream safely. Use stable event/issue codes and typed safe interpolation parameters, a separate raw diagnostic field for operators, and source/stage/category/recoverability fields. Keep unknown marketplace codes visible in technical details and map them to a stable generic localized code; never parse translated text to decide retries. Existing API localization rules already express this principle, but this operation stream does not implement it.

### F8 - P2 - Diagnostic storage is best-effort and error filters miss semantic failures
[AdapterHttpLogService.cs:127-145](../../backend/Services/Marketplace/AdapterHttpLogService.cs#L127-L145) uses a bounded DropOldest channel with no dropped-entry accounting; failed persistence logs a warning but does not requeue the batch ([529-547](../../backend/Services/Marketplace/AdapterHttpLogService.cs#L529-L547)); disposal cancels before draining ([589-599](../../backend/Services/Marketplace/AdapterHttpLogService.cs#L589-L599)). This is a valid design for optional HTTP debugging, not for the only durable incident evidence. Query errorCount and StatusClass=error use only status >=400 or 0: HTTP 200/207 with per-item marketplace failures, or a parse error after HTTP 200, are not counted as errors. Persist essential operation outcomes durably, measure capture loss, capture request timestamp at observation, and retain HTTP status separately from semantic outcome. Keep bounded/sanitized raw diagnostics optional; failed capture must not make a successful remote write look failed and trigger replay.

### F9 - P2 - Fallbacks can hide that data is stale
[PriceSyncService.cs:64-76](../../backend/Services/Marketplace/PriceSyncService.cs#L64-L76) swallows every price-import exception and ComparePricesAsync returns success using cached values at [114-117](../../backend/Services/Marketplace/PriceSyncService.cs#L114-L117) without a freshness indicator. [WildberriesPlugin.cs:332-334](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L332-L334) similarly returns no quota information on failure. Optional fallbacks are sometimes correct, but callers need live/cached/unavailable and last-verified timestamps, plus a visible warning. Cancellation must propagate rather than look like a normal fallback.

### F10 - P2 - SDK fallback loses policy and cancellation
[MarketplacePluginBase.cs:193-227](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L193-L227) maps ExportResult to ExportItemResult without copying Details/Retryable, so Retryable=false becomes the default true. Its catch(Exception) turns cancellation into a retryable failure and continues. Wrapper UpdateSku/Archive paths also reconstruct old-style results without the newer Retryable field ([PluginAdapterWrapper.cs:560-580](../../backend/Services/Plugins/PluginAdapterWrapper.cs#L560-L580)). Preserve all result semantics through every conversion, and rethrow caller cancellation. Add one reusable SDK conformance suite; do not keep fixing each conversion ad hoc.

## Execution-first target design

The unit of work is a **business intent**: synchronize a declared product projection, update stock for specified variants/warehouses, perform one allowed order action, assemble/deliver a supply, change promotion membership, or change a group. One command may require several provider calls. Conversely, one safe bulk call may carry stages belonging to several independently owned commands.

Use this common execution spine:

`Admit intent -> capture versioned inputs -> acquire required metadata -> compile and validate -> persist stage plan -> schedule/admit requests -> dispatch -> persist receipts -> confirm effects -> run dependent stages -> aggregate evidence`.

This is one execution mechanism, **not one universal business recipe**. FBS packing, shop refunds, field-based grouping and coupon disable keep different preconditions, transitions and effects. Bulk is a transport optimization over the same prepared per-item stages as single, never a separate business algorithm.

Initially batch within one command, with compatible account/environment/schema/action/stage policy. Cross-command batching is an optional later optimization requiring per-item ownership, result correlation and Stop semantics; it is not needed to repair the current mixed-operation grouping.

### Responsibility boundaries

- **Host:** owns command/item/stage identity, authorization, immutable intent/version references, durable transitions, claims/leases, retry scheduling, conflict ordering, aggregate outcomes and UI/API progress. It owns the request-admission coordinator and policy observations; no controller independently decides that an adapter return means final completion.
- **SDK:** defines backward-compatible companion contracts for prepared artifacts, validation issues, stage outcomes, receipts, semantic retry/reconciliation safety, cancellation and transport descriptors. Success/Retryable/Details alone is insufficient. Do not break every independently deployed plugin by silently changing the old interface or positional result constructors.
- **Adapter:** owns provider-specific category/field rules, typed native payloads, stage recipe, allowed actions/states, stable external identities, receipt interpretation and confirmation predicates. It declares request scope/cost and supplies native reconciliation; it does not privately own a competing operation lifecycle or hide incomplete data as an empty success.
- **Delegat bridge:** is part of the controlled integration boundary. Pin bridge/platform/plugin compatibility and prove its mapping to native effects; native Medusa/WooCommerce/OpenCart documentation does not certify the bridge wire contract.
- **UI and Public API:** render the same authoritative operation outcome, including waits, actionable blockers, partial application and unresolved effects. Localization renders structured issue/event codes; it does not determine retry policy from translated messages.

### Preparation is a checked artifact, not a boolean

Pure compilation consumes a captured local product/mapping version, complete versioned provider metadata, selected capabilities/actions, resolved variants/warehouses and relevant remote identity/state. It yields the exact payload/stages or structured field/action issues. Metadata retrieval is explicit, admitted I/O before pure compilation; a dictionary outage must not change a field's type.

Readiness invokes this same preparation logic. Execution uses the immutable validated artifact, or deliberately reprepares and revalidates if a material input expired before dispatch. A new edit is a new intent/version, not an excuse to silently rebuild the old attempt from mutable data. Validate provider limits, IDs, types, units and batch bytes/items as well as required values. Do not invent missing business values to make validation pass.

Recheck state-dependent preconditions before dispatch. Where the provider offers no conditional write, concurrent remote change can still race the check; handle the resulting conflict/reconciliation explicitly rather than assuming preflight made such a race impossible.

### Durable outcomes and recovery

Use explicit stage semantics: not started, definitely rejected before effect, accepted awaiting confirmation, applied with evidence, partially applied with per-effect evidence, deferred until a known time, or outcome unknown. These are proposed semantic distinctions, not implemented public API enums.

- Persist command/attempt identity, input hash and intended external identity **before dispatch**; persist returned remote task/receipt and next check before releasing ownership.
- A started attempt without a saved receipt is not safe to replay merely because a process restarted. Resolve it with a provider idempotency key or reliable lookup; if neither exists, preserve unknown and stop automatic mutation.
- One layer owns semantic replay decisions. Transport retries of semantically safe reads can be bounded, but must participate in the same deadline, budget and attempt evidence; do not stack independent write-retry loops.
- After partial application, continue the missing stages. Preserve known effects even when a provider call is not atomic within one item. Do not recreate the card, supply or refund because its later price, membership, label or local refresh failed. Compensation is a separate explicit business action, not an assumed rollback of a remote transaction.
- User Stop prevents new writes. An in-flight request may already have taken effect: reconcile it and report what applied and what was cancelled. Cancellation cannot honestly imply remote undo.
- Aggregate results require coverage of every requested item and required stage. Missing, duplicate or foreign bulk results are protocol failures; retain known successful items rather than declaring everything successful or resending everything.

Structured issues must carry origin/code, stage, affected field/entity, effect certainty, recovery action and optional retry time/evidence reference. Retry policy cannot depend on a translated message or HTTP code alone. Invalid customer input, quota wait, authorization loss, provider rejection, contract/serialization defect and unknown outcome are different classes. **An internal defect must not be relabeled as bad user input to make the interface look reliable.**

Command ownership and remote-entity ordering are different keys. Never collapse two commands merely because they mention one product. Serialize conflicting mutations for the same seller/entity and retain input versions so stale intent cannot overtake newer intent unnoticed. Multiple connections/keys can refer to the same seller. In-flight/unknown effects block unsafe successors; a local lease expiring cannot fence an already-sent remote request. Cross-entry-point ordering is an acceptance requirement, not a concurrency scenario reproduced in this audit.

### Confirmation, queries and imports

Each stage declares the appropriate evidence: definitive per-item provider completion, a confirmed task result, or authoritative comparison of intended writable fields after provider normalization. Accepted is not applied, and seller-catalog applied is not moderation-approved or storefront-buyable. Verification has its own quota and timing budget; do not add unconditional read-after-write to every call or compare provider-generated fields as if they were user intent.

Keep **observed remote state** separate from pending/desired state and cached projections. [FinishSellerStatusAsync](../../backend/Services/Marketplace/MarketplaceCommerceService.cs#L243-L270) can advance an old order read or stored snapshot to the requested status for display; that is optimistic projection, not independent provider confirmation. Preserve useful optimistic feedback but label pending work rather than turning it into observed truth.

Short bounded queries need common authentication, admission, schema/pagination checks and explicit freshness/completeness, not a durable job for every GET. Empty, unsupported, unavailable and partial must be distinguishable. Long imports/synchronizations need resumable traversal and explicit completion; a stale cache or failed page must not silently terminate a complete import.

Preserve [atomic local import graphs and receipts](../../backend/Services/Marketplace/ProductSyncService.Import.cs#L18-L136), unique linked identities, barcode ownership and durable media-queue writes. Do not replace this implemented protection with another importer or put remote calls inside its local transaction. A committed graph plus pending media is also not proof that all media downloads finished; declare that completion level explicitly.

All outbound requests, including metadata, token refresh, polls, read brokers, media and confirmation, use shared admission with provider/account/method/entity scope. The detailed rate section defines the missing windows, cost units, concurrency, cooldown and reset handling. A new queue product is not required to enforce this boundary.

## Test environments and evidence levels
1. Offline stateful provider simulation: real host worker + real plugin/wrapper + injectable HTTP/clock + seeded local DB and fake provider state. Verify request ordering, exact payloads, durable checkpoints, poll progression, final read-back, no extra mutations. This must be a stateful scenario, not 'return 200 for any request'. Assert prohibited requests are never sent and block all non-test network destinations.
2. Real isolated shop stacks: sibling [delegat-shop-opencart/docker-compose.yml](../../../delegat-shop-opencart/docker-compose.yml) and [delegat-shop-woocommerce/docker-compose.yml](../../../delegat-shop-woocommerce/docker-compose.yml) already define local shop+MariaDB+bridge environments. These are useful starting points, not tests already executed here. Use separate compose project names, non-conflicting ports, fresh private named volumes and fake credentials; never attach configured adapters or existing customer volumes. Verify both seller/bridge representation and storefront when relevant. Inspect bridge health/version compatibility before mutation. No docker commands were run in this audit. Provisioning and exports require a separately designated test instance and explicit plan; never run product exports to already configured adapters.
3. Provider sandbox: the current WB schemas include sandbox hosts and separate method/category budgets; our WildberriesApiEnvironment already switches hosts. This is not proof of simulator completeness, category coverage or production parity. Catalog sandbox availability/coverage for Ozon and Yandex remains unestablished by the reviewed local contracts; a production server URL or test-order flag is not a universal catalog sandbox. No shop, seller account or sandbox was called.
4. Isolated authorized provider-account acceptance: only after test-environment designation, no existing marketplace adapter exports; explicit known test fixtures and scope. Real API contract, category permissions, moderation and external media fetching can differ from mocks. Read-only canaries and spec/changelog drift checks complement, not replace, end-to-end tests.
These layers answer different questions. An OpenAPI/mock server checks protocol shape but does not prove category business rules, moderation, remote media retrieval or asynchronous application. Do not claim 'all requests in all categories tested' from generated schemas or a few happy-path examples.

## Current proposed rollout and acceptance

This is the recommended implementation sequence, not work performed by this audit. It replaces an endpoint-by-endpoint repair campaign as the main plan. Each phase has a proof gate, not just a list of classes to refactor.

### A - Establish the correctness harness and stop known false outcomes

Build an isolated stateful provider emulator with injectable HTTP/clock, real host services and worker lifecycle, and a dedicated test database for persistence/concurrency behavior. Keep all non-test network destinations blocked. Reuse the existing probes/tests; turn the agreed defects into desired-behavior regressions, not tests that bless current behavior.

First protective changes cover F2/F3/F4/F10/F17: no premature terminal failure while retry remains; no success after a required stage rejects; operation ownership independent of connection batching/product-ID collisions; no lost retry/cancellation semantics; complete per-item bulk coverage. Unsafe ambiguous replay must stop rather than wait for a large refactor to make it safe. Local API deltas D1-D4 need focused compatibility fixes with fixtures in parallel; fixing them is not the architecture gate.

**Gate:** reproduce the existing counterexamples before changes, then make their intended invariants pass through the real entry paths. No duplicate remote create in response-loss/crash scenarios; unresolved effects remain explicitly unresolved. These acceptance tests have not been implemented/run here.

### B - Deliver one shared product-command vertical slice

Introduce the additive prepared-stage/outcome/transport companions and host execution journal. Move both one-item and bulk entry paths onto it; neither may bypass required price/stock/variant/media stages. Begin with **OpenCart bridge** for native-state proof in the isolated shop and **Ozon Seller API product import as an export stage** for asynchronous receipt/polling/stock-prerequisite behavior in the emulator. This deliberately proves both synchronous bridge and asynchronous seller workflows before generalizing.

Use provider-applicable simple, dictionary-heavy and multi-variant fixtures, changed categories/units, multiple images, an added local variant and explicit warehouse assignments. Unsupported combinations must be blocked rather than faked into a native feature. Include a one-item batch tail. Preserve field-based grouping and local import protections. Add shared admission at the transport seam so hidden metadata/auth/poll calls do not bypass the policy.

**Gate:** single, bulk and batch tail reach the same declared normalized state for the same intent; invalid/incomplete preparation sends no mutation; restart resumes the missing stage, not the whole export. Shop execution still requires explicit isolated test-instance designation; no configured adapter is used.

### C - Extend the mechanism, not the business recipe

Migrate remaining product profiles across the seven adapters, then prices/stocks, order actions, shipments/packing/labels, promotions and groups. For each, specify supported action/direction/state, dependency stages, irreversibility, confirmation and safe reconciliation. A broad enum bit is insufficient; unsupported subactions must be blocked before side effects.

Read-side completeness/freshness and cursor progress are part of this phase: a disabled pagination parameter, swallowed page failure or stale order snapshot must not look like a complete successful synchronization. Keep short reads lightweight while applying shared transport and data-quality rules.

**Gate:** every migrated command has the cross-cutting cases below and provider-specific assertions for actual effects. Tests cover seller-action success followed by stale refresh, supply created followed by membership failure, asynchronous campaign application and group partial failure. Current source inspection does not establish global cross-entry-point entity ordering; prove it rather than assuming a single worker provides it.

### D - Make conformance a release gate

For every advertised support profile, pin host/SDK/plugin/bridge/platform and provider-schema versions; validate exact serialized requests and parsed responses using sanitized local fixtures, including asynchronous and per-item outcomes. Apply the existing complete contract/capability inventory as the coverage index, not as proof that the tests exist.

Continue structural/semantic drift checks after release using refreshed official artifacts and separately authorized safe canaries where available. Tolerate compatible optional additions; required-field/type/envelope changes and violated result invariants must surface as contract incidents, not empty success, zero-valued money or customer validation errors. A schema check alone does not replace domain-effect assertions.

Run bridge profiles against `delegat-shop-opencart` and `delegat-shop-woocommerce`, checking native state as well as adapter responses. Use WB sandbox only for documented supported scenarios; provider-account acceptance outside those environments requires separate authorization. No local schema or sandbox can certify all categories or moderation behavior.

**Gate:** release support claims match recorded scenario evidence. Unimplemented/unsupported profiles are unavailable before dispatch, and unverified coverage is explicit rather than silently certified. API/bridge drift must fail or flag the affected profile before a compatibility claim ships; add localized diagnostics and safe correlation as supporting UX.

### Cross-cutting proof obligations

All entries are **unrun remediation acceptance**, not additional test passes:

- [ ] Single/bulk/tail equivalence for card fields, variants, media, prices, stocks and grouping included in the declared intent.
- [ ] Two commands on the same entity and multiple operations in one batch preserve ownership; older intent cannot silently overwrite newer intent across entry paths.
- [ ] Accepted write with lost response; crash before receipt commit; restart during confirmation; provider without idempotency/reliable lookup remains unknown without blind replay.
- [ ] Partial/semantic 200 failures and missing/duplicate/foreign bulk results preserve known effects and prevent false aggregate success.
- [ ] Metadata outage, expired schema, graph failure and invalid fields stop mutation without changing payload meaning.
- [ ] Virtual-clock quota/concurrency/reset/error-cost tests include every auxiliary request, several keys for one seller and process restart.
- [ ] Stop before send, during in-flight write and after partial application prevents further writes without pretending already-applied effects were rolled back.
- [ ] Delayed/optimistic/cached reads, incomplete pages and cursor stalls cannot certify final state or complete import; native-state assertions separate observation from intent.
- [ ] Atomic new-import graph/receipt and media queue behavior survive local rollback/retry; actual media completion is verified separately when promised.

The engineering work can start from this decision; further static endpoint enumeration is not a prerequisite. Completion means these invariants and each advertised profile's domain effects have evidence in the appropriate layer, not that error popups disappeared. No failure-rate percentage, zero-defect guarantee or unperformed acceptance result is inferred.

## Target architecture (proposal, not an implementation)
Entry points (cabinet, Public API, schedules, tools) -> durable operation and per-item work -> host execution boundary -> mapping/validation -> plugin -> shared transport primitives -> marketplace -> durable confirmation/reconciliation -> outcome aggregation -> localized event view + linked sanitized HTTP detail.
Keep provider-specific auth, payloads, pagination, semantic error parsing, quotas and confirmation in plugins. The host owns operation identity, lifecycle, checkpoints, durable results, user cancellation and final aggregation. Shared SDK transport components provide timing, safe capture, typed transport faults and explicit retry hooks, not a universal provider algorithm.
Use a small additive contract: execution context (operationId, itemId, attemptId, traceId, connectionId, adapterVersion, sdkVersion); issue (code, source, stage, category, safeParams, vendorCode, vendorRequestId, httpStatus, recoverability, diagnosticRef); result outcome (applied, accepted, rejected, unknown) plus separate warnings and per-stage results. Suggested categories: credentials/permissions, quota/rate_limit, invalid_data/mapping, transport/timeout, provider_unavailable, contract_mismatch, internal. Retryability alone is insufficient: recoverability distinguishes wait-and-retry, fix-data, reauthenticate and reconcile-first. Keep event/outcome identifiers without tracing enabled.
Queued/running work may wait for a scheduled retry or remote confirmation. Terminal user statuses must be derived only after all required work is terminal; partial and completed-with-warnings are different from plain completed. User stop remains sticky. A new manual retry needs an explicit attempt/new operation rather than mutating a finished history silently.
Bound overall deadlines separately from HTTP-attempt timeouts. One layer owns replay decisions, with bounded backoff/jitter and persisted nextAttemptAt. Respect provider Retry-After and endpoint/account limits. Read-like POSTs need declared semantic safety; HTTP method alone is not enough. Retry writes only with verified provider idempotency or reconciliation evidence. Circuit breakers may address transport/provider outages, not invalid products, permissions, or individual validation failures. Do not stack three independent retry loops.
Keep SQL operation events/outcomes as the durable product record. Add ActivitySource spans for stage timings and optional standard trace export, with no credentials/payloads/model content by default. Metrics use bounded adapter/operation/stage/outcome labels, not SKU, customer, raw URL or request IDs. Do not use trace sampling as durable audit storage.
Use backward-compatible companion types/interfaces or an explicit coordinated SDK/adapter release; changing a positional record constructor can be binary-breaking even with optional parameters. Pin and record host/SDK/plugin/bridge versions, and test old/new binary compatibility before independently shipping DLLs.

## Initial supporting-work sequence (superseded as the overall roadmap by the execution-first sequence)
1. Security hotfix: cover X-Delegat-Api-Key in SDK and host capture, including custom sink inputs; fake-key assertions cover headers, URLs, bodies and diagnostic output. Authorized exposure review/rotation is separate and never automatic destructive cleanup.
2. Truthful execution: fix F2/F3/F4 and lost SDK flags; offline tests must show transient fail -> successful retry -> completed exactly once; two simultaneous operations on one connection finish independently and Stop affects only its operation; a follow-up rejection cannot be shown as full success; caller cancellation is not a retryable provider fault. Reconcile missing/duplicate/foreign bulk item results against the requested item set, never leave rows InProgress silently.
3. Minimal structured evidence: propagate operation/item/attempt identity end to end; typed event codes with en/ru renderers; safe technical detail links, request timestamp and provider request id; essential outcomes remain queryable without HTTP body logging or trace export. Additive partner behavior changes require Public API docs/Swagger sync. EF model changes require generated migrations only.
4. Safe asynchronous recovery: persist remote task receipts/checkpoints and explicit unknown state; fault tests cover accepted-write then timeout, crash before local commit, restart during polling, and ensure no duplicate create is automatically submitted. Reads may retry according to policy; ambiguous writes must reconcile first. Implement per-operation atomic work claims/leases before introducing overlapping workers or multiple replicas; this audit does not establish that current deployment is multi-replica.
5. Adapter conformance and API change detection: shared deterministic fixtures for 401/403/429 with date and delta Retry-After, 5xx/HTML/invalid or changed JSON, 200/207 per-item errors, missing result items, cancellation, pagination stalls, partial media and group failures. Layer transport tests, adapter semantic tests, and host worker lifecycle tests. Existing API watch prompt/catalog is useful input, not proof of a continuously running contract test. Track called endpoints and bridge versions against official changes; use only designated sandbox/test-instance canaries, read-only by default. Never run exports against already configured marketplace adapters.

## What not to do
No catch-all-and-continue blanket, no translating raw messages as policy, no blanket POST retry, no gigantic unbounded payload logging, no Sentry-only solution, no claim that installing Polly/another worker framework resolves ambiguous external writes. A new workflow engine/broker may be evaluated later if durable confirmation/throughput requirements justify it; there is no evidence here that one is needed first.

## Documentation evidence policy

The current revision uses only the [2026-11-20 local bundle](#current-local-documentation-baseline). Earlier online snippets are superseded as the adapter-contract baseline; no site was opened for this revision. Offline probe evidence remains unchanged and is not a live API certification.

## Next incident evidence
For a concrete failure collect, with authorized access: operation ID and timestamps, exact host/SDK/adapter/bridge versions, sanitized per-stage events and HTTP status/vendor request id, product/item identity, mapping version, remote task receipt, retry history and final read-back evidence. Do not attach credentials or arbitrary full customer payloads. This distinguishes data/mapping, our adapter, transport, provider contract and provider asynchronous processing; it does not promise attribution from a single exception string.

## Complete seven-adapter contract inventory

Source inventory host revision: `71318b9aae47401f4306cd3f858393994f9521ea`. This is a static source inventory, not a live feature certification. No plugin method was invoked.

The inventory includes every one of the 50 named methods and 9 metadata properties in [IMarketplacePlugin](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs), all 27 atomic capability bits, and the 3 enum values `None`, `Basic`, and `FullProducts`. Optional companion interfaces (media, public-read broker, bulk progress) are outside that interface and are identified separately below.

Cell legend: **I** = implemented in adapter source; **B** = inherited SDK base stub/default; **N** = governing capability not declared. `N (I)`, `N (B)`, and `N (F)` retain the physical origin even when unadvertised; **F** = functional SDK sequential fallback, not a provider-native implementation. A base empty/null/no-op default is not automatically a defect; the Base behavior column states what it does. An I cell says code exists, not that every action/category/input works. No feature bit exists for common metadata/tool/rate-limit methods.

### Every method

| Method | Governing capability | Base behavior | Avito | Medusa | OpenCart | Ozon | Wildberries | WooCommerce | Yandex Market |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [`GetCredentialsSchema`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L64) | No dedicated flag | abstract; adapter required | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L85) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L97) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L123) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L523) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L349) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L93) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L308) |
| [`GetRateLimitConfig`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L69) | No dedicated flag | null | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L43) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L41) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L41) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L439) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L298) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L41) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L292) |
| [`GetQuotasAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L74) | No dedicated flag | empty list | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L44) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L44) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L44) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L442) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L301) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L44) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L295) |
| [`GetRateLimiterForConnection`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L80) | No dedicated flag | null | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L48) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L48) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L48) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L502) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L338) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L48) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L298) |
| [`ClearLocalMetadataCaches`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L87) | No dedicated flag | no-op | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L51) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L51) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L51) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L430) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L62) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L51) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L47) |
| [`GetTools`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L94) | No dedicated flag | empty list | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L56) |
| [`GetOrderActionFields`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L101) | No dedicated flag | empty dictionary | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L59) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L134) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L177) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L553) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L59) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L130) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L346) |
| [`RunToolAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L108) | No dedicated flag | throws NotSupportedException | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L63) |
| [`TestConnectionAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L122) | No dedicated flag | abstract; adapter required | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L111) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L178) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L193) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L592) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L390) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L146) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L374) |
| [`GetCategoriesAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L135) | `GetCategories` | throws NotSupportedException | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L136) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L212) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L227) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L627) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L423) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L181) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L418) |
| [`GetCategoryFieldsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L144) | `GetCategoryFields` | throws NotSupportedException | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L151) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L235) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L265) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L804) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L495) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L203) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L443) |
| [`GetPriceFieldsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L152) | No dedicated flag | empty list | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L170) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L271) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L108) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L978) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L629) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L243) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L490) |
| [`CreateCategoryAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L160) | `CreateCategory` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L117) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L277) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L308) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L117) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L117) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L249) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L117) |
| [`ImportProductAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L172) | `ImportProduct` | throws NotSupportedException | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L180) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L326) | [N (I)](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L401) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L1190) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L956) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L303) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L500) |
| [`ImportProductsBatchAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L181) | `ImportProductBatch` | sequential fallback | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L198) | [N (F)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L135) | [N (I)](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L415) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L1234) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L850) | [N (F)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L135) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L509) |
| [`ImportAllProductsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L194) | `ImportAllProducts` | throws NotSupportedException | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L219) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L151) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L431) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L1278) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L1014) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L151) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L548) |
| [`SearchPublicCatalogAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L204) | `SearchPublicCatalog` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L162) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L162) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L162) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L1172) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L985) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L162) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L530) |
| [`GetPublicProductAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L215) | `SearchPublicCatalog` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L173) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L173) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L173) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L1182) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L995) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L173) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L540) |
| [`ExportProductAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L228) | `ExportProduct` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L182) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L358) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L451) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L1773) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L1191) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L335) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L619) |
| [`ExportProductsBulkAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L238) | `ExportProductBulk` | sequential fallback | [N (F)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L192) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L390) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L481) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L2136) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L1562) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L444) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L658) |
| [`UpdateSkuAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L247) | `UpdateSku` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L230) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L230) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L230) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L2977) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L230) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L230) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L230) |
| [`ArchiveProductAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L257) | `ArchiveProduct` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L241) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L241) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L241) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3008) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L1539) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L241) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L788) |
| [`ImportPricesAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L272) | `ImportPrices` | throws NotSupportedException | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L239) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L250) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L613) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3035) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2146) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1129) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L817) |
| [`ExportPricesAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L280) | `ExportPrices` | throws NotSupportedException | [I](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L259) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L485) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L544) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3077) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2197) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1043) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L839) |
| [`ImportStocksAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L288) | `ImportStocks` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L268) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L268) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L268) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3167) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2516) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L268) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L908) |
| [`ExportStocksAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L296) | `ExportStocks` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L277) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L517) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L579) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3220) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2582) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1087) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L935) |
| [`GetOrdersAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L308) | `GetOrders` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L286) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L1152) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L653) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3298) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2673) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1163) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L986) |
| [`GetOrderAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L316) | `GetOrders` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L295) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L1166) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L665) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3463) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2717) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1183) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1017) |
| [`UpdateOrderStatusAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L324) | `UpdateOrderStatus` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L304) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L1183) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L681) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3648) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2756) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1200) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1104) |
| [`GetOrderLabelAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L333) | `GetOrderLabels` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L314) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L1269) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L314) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3727) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2996) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L314) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1143) |
| [`GetFulfillmentLocationsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L341) | `GetFulfillmentLocations` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L323) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L323) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L323) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3773) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3037) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L323) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L323) |
| [`GetShipmentsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L349) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L332) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L332) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L332) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L332) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3061) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L332) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L332) |
| [`GetShipmentAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L357) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L341) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L341) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L341) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L341) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3088) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L341) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L341) |
| [`CreateShipmentAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L365) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L350) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L350) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L350) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L350) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3107) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L350) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L350) |
| [`AddOrdersToShipmentAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L373) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L359) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L359) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L359) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L359) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3132) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L359) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L359) |
| [`CreateShipmentBoxesAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L382) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L369) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L369) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L369) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L369) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3172) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L369) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L369) |
| [`GetShipmentBoxesAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L391) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L379) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L379) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L379) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L379) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3197) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L379) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L379) |
| [`AssignOrdersToShipmentBoxAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L399) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L388) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L388) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L388) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L388) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3216) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L388) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L388) |
| [`GetShipmentBoxLabelAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L409) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L399) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L399) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L399) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L399) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3243) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L399) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L399) |
| [`GetShipmentLabelAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L418) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L409) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L409) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L409) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L409) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3269) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L409) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L409) |
| [`DeliverShipmentAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L426) | `ManageShipments` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L418) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L418) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L418) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L418) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3292) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L418) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L418) |
| [`GetPromotionsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L434) | `GetPromotions` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L427) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L427) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L708) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3787) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3408) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1229) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1182) |
| [`GetPromotionAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L442) | `GetPromotions` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L436) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L436) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L718) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3811) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3421) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1241) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1201) |
| [`UpdatePromotionProductsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L450) | `UpdatePromotions` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L445) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L445) | [I](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L734) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3838) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3453) | [I](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1258) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1210) |
| [`GetFieldBasedGroupingConfig`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L465) | `FieldBasedGrouping` | null | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L459) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L459) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L459) | [I](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3968) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L459) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L459) | [I](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1282) |
| [`CreateGroupAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L474) | `EndpointBasedGrouping` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L466) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L547) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L466) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L466) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3558) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L466) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L466) |
| [`AddProductsToGroupAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L483) | `ModifyGroupMembership` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L476) | [N (I)](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L571) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L476) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L476) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3640) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L476) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L476) |
| [`RemoveProductsFromGroupAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L492) | `ModifyGroupMembership` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L486) | [N (I)](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L596) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L486) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L486) | [B](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L486) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L486) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L486) |
| [`DeleteGroupAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L501) | `DeleteGroups` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L496) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L620) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L496) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L496) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L496) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L496) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L496) |
| [`ImportGroupsAsync`](../../sdk/Delegat.Marketplace.SDK/IMarketplacePlugin.cs#L509) | `ImportGroups` | throws NotSupportedException | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L505) | [I](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L637) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L505) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L505) | [I](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3510) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L505) | [N (B)](../../sdk/Delegat.Marketplace.SDK/MarketplacePluginBase.cs#L505) |

### Every capability flag

Here I means the bit is declared and its required operational methods have adapter overrides; B means a declared capability reaches a required base stub. N means the bit is absent. The method table remains authoritative for optional metadata and unadvertised overrides. `ModifyGroupMembership` covers both add and remove, not just add.

| MarketplaceCapabilities value | Avito | Medusa | OpenCart | Ozon | Wildberries | WooCommerce | Yandex Market |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `GetCategories` | I | I | I | I | I | I | I |
| `GetCategoryFields` | I | I | I | I | I | I | I |
| `ImportProduct` | I | I | N | I | I | I | I |
| `ImportAllProducts` | I | N | I | I | I | N | I |
| `ExportProduct` | N | I | I | I | I | I | I |
| `UpdateSku` | N | N | N | I | N | N | N |
| `ImportPrices` | I | N | I | I | I | I | I |
| `ExportPrices` | I | I | I | I | I | I | I |
| `ImportStocks` | N | N | N | I | I | N | I |
| `ExportStocks` | N | I | I | I | I | I | I |
| `GetOrders` | N | I | I | I | I | I | I |
| `UpdateOrderStatus` | N | I | I | I | I | I | I |
| `FieldBasedGrouping` | N | N | N | I | N | N | I |
| `EndpointBasedGrouping` | N | I | N | N | I | N | N |
| `ImportGroups` | N | I | N | N | I | N | N |
| `DeleteGroups` | N | I | N | N | N | N | N |
| `ModifyGroupMembership` | N | N | N | N | B (remove is base stub) | N | N |
| `ImportProductBatch` | I | N | N | I | I | N | I |
| `ExportProductBulk` | N | I | I | I | I | I | I |
| `ArchiveProduct` | N | N | N | I | I | N | I |
| `CreateCategory` | N | I | I | N | N | I | N |
| `SearchPublicCatalog` | N | N | N | I | I | N | I |
| `GetPromotions` | N | N | I | I | I | I | I |
| `UpdatePromotions` | N | N | I | I | I | I | I |
| `GetOrderLabels` | N | I | N | I | I | N | I |
| `GetFulfillmentLocations` | N | N | N | I | I | N | N |
| `ManageShipments` | N | N | N | N | I | N | N |
| `None` (zero/composite, not a separate bit) | N | N | N | N | N | N | N |
| `Basic` (zero/composite, not a separate bit) | N | I | N | I | I | I | I |
| `FullProducts` (zero/composite, not a separate bit) | N | N | N | I | I | N | I |

`None` means an empty capability set, not the tautological `.HasFlag(0)`. `Basic`/`FullProducts` are evaluated as their complete constituent masks; partial overlap is N.

### Metadata properties

| Property | Avito | Medusa | OpenCart | Ozon | Wildberries | WooCommerce | Yandex Market |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `Key` | I | I | I | I | I | I | I |
| `DisplayName` | I | I | I | I | I | I | I |
| `Version` | I | I | I | I | I | I | I |
| `Stability` | I | I | I | I | I | I | I |
| `Description` | I | I | I | I | I | I | I |
| `LogoUrl` | I | I | I | I | I | I | I |
| `Capabilities` | I | I | I | I | I | I | I |
| `ProductEditUrlTemplate` | B (SDK metadata default) | B (SDK metadata default) | B (SDK metadata default) | I | I | I | I |
| `IsSystem` | I | I | I | I | I | I | I |

### Snapshot provenance

| Adapter | Source revision | Capability declaration |
| --- | --- | --- |
| Avito | `71318b9aae47401f4306cd3f858393994f9521ea` | [AvitoPlugin.cs](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L34) |
| Medusa | `71318b9aae47401f4306cd3f858393994f9521ea` | [MedusaPlugin.cs](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L55) |
| OpenCart | `9fad2b7fd07a7173030dd5bc2cd4708115a2b965` | [OpenCartPlugin.cs](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L82) |
| Ozon | `4a79568d949eb254b1cd313b36d122903a619c4d` | [OzonPlugin.cs](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L74) |
| Wildberries | `7c414b51db4130def0b9c9c2e773a7f59d8b8f24` | [WildberriesPlugin.cs](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L84) |
| WooCommerce | `2e07a2237ad14008a93b5bc9c67fd4f56a0aaede` | [WooCommercePlugin.cs](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L52) |
| YandexMarket | `ed4db80007aceccb25510bb405ffa5632174eb5b` | [YandexMarketPlugin.cs](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L70) |


## Shared SDK, host orchestration, and adapter-specific commerce

This expansion uses the `71318b9` host snapshot and the adapter revisions in the inventory above, not the older `270b122` snapshot used for the initial offline probes. A source implementation is not a live certification. No connection, marketplace, shop, or sandbox was called for this expansion.

### Ownership boundaries

| Layer | What actually belongs here | What it does not guarantee |
| --- | --- | --- |
| SDK contract and defaults | `ConnectionContext`, provider-neutral order/shipment/promotion/group DTOs, capability flags, nullable/empty metadata defaults, sequential import/bulk-export fallbacks, local rate-window utility, HTTP evidence helpers | No common durable shipment/promotion workflow, no proof a capability has every required override, no universal API compatibility or end-state verification |
| Host wrapper | Builds context, credentials/OAuth access, logger/media/cache/progress services and converts SDK results to host results; see [PluginAdapterWrapper](../../backend/Services/Plugins/PluginAdapterWrapper.cs) | Not a provider-specific state machine; result conversion still needs conformance checks |
| Host orchestration | Store/connection permissions and feature gates, product queues/mapping/links, order snapshots/sync, group links, cabinet and Public API endpoints | Capability admission is not validation of every provider action; the product queue is not automatically reused for order/shipment/promotion mutations |
| Adapter | Native identifiers, category payloads, FBS/FBO/shop channels, status transitions, pagination, request splitting, provider HTTP, media transfer, confirmation and error mapping | An override can be partial, return empty metadata, suppress an error, or cover only one direction of an advertised operation |
| Shop bridge | Delegat-owned server routes and their application to OpenCart/WooCommerce/Medusa data models | These are not the native vendor API contracts. Native WooCommerce, WordPress, OpenCart or Medusa documentation does not certify `/delegat/...` bridge endpoints |

Media capability and sandbox-read broker interfaces are companions, not missing methods of `IMarketplacePlugin`. Ozon/WB/Yandex public-catalog clients also use storefront endpoints outside the seller API. Optional browser fallback can generate additional navigation/resource requests, so a finite seller-route list does not enumerate that browser traffic.

### Contract inconsistencies exposed by the complete matrix

- **Wildberries group removal is advertised but inherits a throwing stub.** `ModifyGroupMembership` is declared; add is overridden but remove is not. [GroupSyncService:391-409](../../backend/Services/Marketplace/GroupSyncService.cs#L391-L409) admits remove through that flag and calls the base implementation. This is a reachable contract mismatch, not a statement that the WB API cannot separate listings: its `moveNm` documentation describes separation.
- **Medusa membership code is unreachable through the normal host gate.** Both [add/remove overrides](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L571-L619) exist, but `ModifyGroupMembership` is absent; host checks at [341](../../backend/Services/Marketplace/GroupSyncService.cs#L341) and [391](../../backend/Services/Marketplace/GroupSyncService.cs#L391) reject them.
- **OpenCart has unadvertised import methods.** [Single and batch implementations](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L401-L430) exist while their capability bits are absent. This is not an inherited unsupported implementation; it is admission/advertisement divergence.
- **Empty metadata is not quota enforcement.** Yandex explicitly overrides `GetQuotasAsync` with an empty list; Medusa/OpenCart/WooCommerce inherit the SDK empty list. All seven inherit empty `GetTools`; `RunToolAsync` remains unsupported. These cells must not be interpreted as tested business functionality.

### Orders: live provider mutations, stored host reads

The SDK order comments say pass-through/not persisted, but the current host persists orders. Cabinet and Public API list/detail endpoints read local snapshots, not a fresh seller API response. Never-synced is an empty page or not-found, not evidence the provider has no orders. See [MarketplaceCommerceController](../../backend/Controllers/MarketplaceCommerceController.cs#L13-L178), [PublicMarketplaceOrdersController](../../backend/Controllers/PublicApi/PublicMarketplaceOrdersController.cs#L27-L92), and [MarketplaceOrderSyncService](../../backend/Services/Marketplace/MarketplaceOrderSyncService.cs).

`OrderSyncBackgroundService` starts after 20 seconds and cycles every 30 seconds over active `GetOrders` connections. Sync has a process-local per-connection semaphore, deep/status scans with cooldown/page limits and a hot recent-window pull. Deep scanning stops after the first page for terminal statuses; hot pulls request at most one source page. These are bounded snapshot policies, not a guarantee of complete historical replication. Failures are logged and tracked in sync state; they are not the same lifecycle as `PendingProductExport`.

Order mutation endpoints call the adapter live, check `Success`, then [FinishSellerStatusAsync:243-268](../../backend/Services/Marketplace/MarketplaceCommerceService.cs#L243-L268) obtains the returned order, tries live refresh, or falls back to a stored snapshot; it advances the local seller status and saves it. Remote mutation can already have succeeded before that refresh/save fails. There is no durable per-stage remote mutation receipt in this flow, and the local fallback is not independent confirmation of the provider state.

| Adapter | Order path down to HTTP | Provider-specific semantics and residual risks |
| --- | --- | --- |
| Avito | Not declared; order methods remain SDK throwing defaults | No order/status/label HTTP path implemented by this plugin; do not infer support from other Avito APIs |
| Medusa | [1152-1305](../../plugins/Delegat.Marketplace.Medusa/MedusaPlugin.cs#L1152-L1305) -> bridge `GET orders`, `GET orders/{id}`, `POST orders/{id}/cancel` or `/status`, `GET orders/{id}/label` | List forwards page/status/search but not all `OrderQuery` fields; cancel sends refund/reason/note; labels are bridge/Ozon-derived and can be unavailable before assembly. No native Medusa order-state conformance was run |
| OpenCart | [653-707](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L653-L707) -> bridge orders list/detail and `POST orders.status` | List forwards page size/cursor/status, not every query field. Action/reason are forwarded to the bridge. No labels capability. REST literal `orders.status` differs from the REST example in the local adapter doc; actual bridge-mode compatibility remains untested |
| Ozon | [3298-3786](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3298-L3786) -> FBS/FBO list/detail; FBS ship/awaiting-delivery/cancel; package-label | Maintains separate FBS/FBO cursor state; FBS list is v4. Seller mutations normalize to cancel/pack/ship, not every generic action. Pack reads posting products and forms one package; labels require an eligible FBS status, not FBO |
| Wildberries | [2673-2855](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L2673-L2855) -> new/dated orders, statuses, cancel, supply create/add/deliver, optional boxes, stickers | Pack is multi-step supply/box work; ship delivers the supply, affecting all its orders. Missing supplied IDs plus a partially completed prior attempt can create extra remote objects on replay. Detail lookup catches provider exceptions as null and may build a sparse order from status alone |
| WooCommerce | [1163-1228](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1163-L1228) -> bridge order list/detail/status | Sends action/status/reason/tracking number. List forwards page/status/search/from, not the entire neutral query. No order-label capability. Native Woo status behavior is mediated by the installed bridge |
| Yandex Market | [986-1181](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L986-L1181) -> campaign order list/detail/status/labels, with offer-mapping reads for images | Resolves business/campaign and maps status/substatus; detail catches provider errors as null. List still uses page/pageSize and legacy pager handling. Official fetched spec marks campaign order list/detail deprecated; announced detail degradation 2027-01-18 and shutdown 2027-04-12 are future compatibility risks at this audit date, not a proven present incident |

### Shipments, locations, boxes and labels

Only Wildberries advertises `ManageShipments`; it overrides all ten shipment methods. Ozon and WB advertise fulfillment locations; Ozon returns seller warehouses, WB combines warehouses/offices. Order packing on Ozon or Medusa is not an implementation of the common shipment interface.

The path is cabinet/Public API -> [MarketplaceCommerceService.RequireAsync](../../backend/Services/Marketplace/MarketplaceCommerceService.cs#L28-L54) -> wrapper -> WB [3061-3310](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3061-L3310) -> the exact supply/box endpoints in the ledger below. List/detail/create/add-orders/create-boxes/list-boxes/assign-orders/box-sticker/supply-QR/deliver are live calls, not a durable local shipment workflow. Public API gates this family on `ManageShipments` in [PublicMarketplaceShipmentsController](../../backend/Controllers/PublicApi/PublicMarketplaceShipmentsController.cs).

Positive existing control: WB's reconciliation logic treats a duplicate-membership 409 as successful only after reading supply order IDs and proving all requested orders are present. D3 identifies a current route mismatch in that read, so the logic is not certified against the supplied current contract. Retain that evidence-based reconciliation. Gaps: create/add/assign/deliver may succeed before the subsequent `LoadShipmentAsync` fails; `GetShipmentBoxesAsync` returns an empty list on any WB API exception, and `LoadShipmentAsync` suppresses order-ID/box fetch errors. A successful detail response can therefore be incomplete without a completeness marker. QR labels also have provider state prerequisites: the official supply barcode is available after handing the supply to delivery.

### Promotions and coupons

Host promotion endpoints are live pass-through, gated by `GetPromotions`/`UpdatePromotions`, and check aggregate `Success`; they do not persist an execution plan for add-then-remove or wait for every asynchronous provider upload. See [PublicMarketplacePromotionsController](../../backend/Controllers/PublicApi/PublicMarketplacePromotionsController.cs) and the analogous cabinet methods.

| Adapter | Promotion path down to HTTP | Actual support boundary |
| --- | --- | --- |
| Avito | Not declared; SDK defaults throw | No promotion calls implemented |
| Medusa | Not declared; SDK defaults throw | No promotion calls implemented |
| OpenCart | [708-766](../../plugins/Delegat.Marketplace.OpenCart/OpenCartPlugin.cs#L708-L766) -> bridge `GET promotions`, detail, `POST promotions.products` | Coupons, not arbitrary marketplace campaigns. List returns one complete bridge response and ignores query pagination; detail catches all OpenCart API exceptions as null. Add/removal counts come from bridge; removeAll needs the right bridge version |
| Ozon | [3787-3964](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3787-L3964) -> actions list/products/update/deactivate | Current local schemas expose payload, response-envelope, cursor and action-type mismatches (D1); apparent paging/parsed IDs are not proof of compatible promotion execution |
| Wildberries | [3408-3507](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3408-L3507) -> calendar list/nomenclatures/upload | Date window is now-7 to now+60 days; no generic all-history promise. Only additions implemented; removal/removeAll explicitly fail. Upload returns success/count immediately, while official API returns an asynchronous uploadID; no persisted confirmation of every enrolled item. Nomenclature-read failure is suppressed |
| WooCommerce | [1229-1290](../../plugins/Delegat.Marketplace.WooCommerce/WooCommercePlugin.cs#L1229-L1290) -> bridge coupon list/detail/products | Requests one page but returns HasMore=false, so complete coupon traversal is not established. removeAll is bridge-defined disable, not emptying the product list (which can make a Woo coupon store-wide) |
| Yandex Market | [1182-1275](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1182-L1275) -> business promos/list and offer update/delete | List maps provider 400/404 to an empty list. Mutations return requested add/remove counts after the HTTP call, not verified resulting membership; removeAll reports a sentinel count of 1. Response schema/per-item errors and eventual visibility are not certified |

### Groups

Host [GroupSyncService](../../backend/Services/Marketplace/GroupSyncService.cs) owns local-to-provider links and chooses field-based vs endpoint-based grouping. It resolves exported product IDs before endpoint calls; import/delete/membership have independent flags. Group membership and product export are not a single atomic remote operation.

| Adapter | Group path to provider |
| --- | --- |
| Avito | No grouping flags; SDK defaults |
| Medusa | Endpoint groups: `groups/upsert`, `groups`, `groups/{id}/products`, `/products/remove`, `/delete` on Delegat bridge; maps to bridge collections. Add/remove exist but normal host feature admission blocks them because the membership flag is absent |
| OpenCart | No grouping flags; SDK defaults |
| Ozon | [GetFieldBasedGroupingConfig](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3968) injects category-dependent grouping fields into product import payload; no separate SDK group endpoint methods advertised |
| Wildberries | [3510-3690](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L3510-L3690) derives groups from imtID on card reads; create/add use `cards/moveNm`, creation can use `cards/upload/add`. Remove remains a base stub despite advertised membership |
| WooCommerce | No common grouping flags; `_variants`/variable products are not an implementation of the SDK group methods |
| Yandex Market | [GetFieldBasedGroupingConfig](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1282) injects `groupId` into offer payload; separate create/delete/import group methods are not advertised |

The target execution/checkpoint architecture must apply to commerce mutations too, but must not force FBS pack, supply delivery, shop refund and coupon disable into one undifferentiated status update. Capability contracts need action/direction/channel/state support, not just a single broad bit.

## Current local documentation baseline

This revision supersedes the earlier online/snippet-based endpoint and rate comparison. Its only API-documentation inputs are files under `C:/Users/ARTEM/OpenBot/Shared/adapter-official-docs-current`, downloaded on **2026-11-20** according to that directory's `README.md`. The July `adapter-official-docs` directory and the provisional July comparison are excluded. No provider site, documentation URL, seller account, sandbox or configured adapter was called during this revision. URLs embedded in the files were treated as citations, not fetched.

The source-code inventory above remains the frozen host/SDK/plugin snapshot already recorded; refreshing documentation does not certify deployed binaries or silently update those source revisions. Native platform documentation is not a substitute for the Delegat bridge contract.

| ID | Local input relative to the current directory | Provenance / coverage |
| --- | --- | --- |
| O | `ozon/ozon-seller-api.openapi.json` | OpenAPI 3.0.0; document version 2.1; **497 paths**. Includes current FBS/FBO lists and promotion v2/update routes. General limits are in `tags[5]` (Limits); embedded dated notices are also present |
| W01-W13 | `wildberries/01-general.yaml` through `13-finances.yaml` | **13 Russian OpenAPIs**, not a products-only snapshot. Fixed calls here use W02=`02-items.yaml`, W03=`03-orders-fbs.yaml`, W08=`08-promotion.yaml`; general headers/token-bucket rules use W01 |
| A | `avito/specs/*.openapi.json`, `avito/guides/*` | **28 sections / 264 path entries**. Actual fixed calls map to auth, user, item and autoload; the unrelated Autoteka `/token` match is excluded by API host/product |
| Y | `yandex/repo/openapi/openapi.yaml` and its local `$ref` files; `yandex/limits.md` | Verified repository commit **b71cd4391c4e3a1e0819ace67806a4ec35b970ba**, 2026-11-20 14:05:17 +0300. Root spec 3.0.3/LATEST; per-operation policy and request/response schemas live in referenced files |
| M | `medusa/admin.openapi.yaml`, `medusa/store.openapi.yaml` | OpenAPI 3.0.0 / **2.23.0**; native Admin/Store contracts, **not** the adapter's Delegat bridge |
| WC | `woocommerce/repo/docs/apis/rest-api/` | Verified repository commit **48d302d9dcfc542e8a6a9d6a16e35787395d77a2**, 2026-11-20 16:41:18 UTC. Current REST contract is **v3**, documented as text rather than a separate OpenAPI; Store API is a different surface |
| OC | `opencart/docs/`, `opencart/llms.txt`, `opencart/api-source/upload/catalog/controller/api/` | Manifest: **87 platform pages**; PHP API release **4.1.0.4**, commit **3b0597ec**. Native `index.php?route=api/...` calls, not Delegat bridge actions |

Reproducible content fingerprints (SHA-256; no documentation files are copied into this product repository):

| Input | SHA-256 |
| --- | --- |
| O | `568c50e166371362933b91933ccff34c7ab8b2e8280208af5e6e71ab9878486f` |
| W01 | `44d3bf09b5a4528f67ee36096788e4a96cafa0d28e4ed19cc7b96149898f3a3b` |
| W02 | `691be0e380d103ab69681306aec70429ca27fb9f21604f74fcaae913f502e0a9` |
| W03 | `907d0364a62be686e7a36c45992e2477925a1a2ac4c4512114ce290b65eb02c6` |
| W08 | `3272698a6e05d11b942c4a059c0dfeb582c9acbb07ee9d3aed32bc396e26b56a` |
| A auth / item | `7f96772f40cbf5abf9616271fc6ca8a4cf40e0fb5d66511b2fc28504a79695e9` / `88f9854c411e79572478a52d3ce71ceb9078a5d647c99351ca1ce3c5c25c4271` |
| A user / autoload | `dbae888414e155a7599963bbb99d043d805dd079cbf6897fc3e6a8bfa027c809` / `9bc8ad76b9b3590d2a80d3a071520cf072cae080b749ad0c330215267687ef41` |
| Y root / limits | `74a840c6dfd18d106407d04b7a1f91fd0ceed012abc5896e1cc99bf9dbdceb00` / `07e6a3ff26ec17ba68c68fece6873fef6b06c2ff8fa06d19b5b533b2cc722238` |
| M admin / store | `265a4223f70268996dbb40c24d715b83fdf1e68dcbc30cee401b621ab7a1b9ea` / `0ddf2f02b6268891b6cd95094523005e0adca3158f88eeaf81c66ec4179b9595` |

### Local contract comparison: scope and results

The fixed transport inventory has **28 Ozon pairs** and **20 Yandex pairs**, all found with the called HTTP verb in the current local specifications. **42 of 45 WB pairs** match; three do not. Avito's **six fixed pairs** match the appropriate product/host specification; its seventh ledger entry is an input-dependent dictionary URL family, not one fixed endpoint. Parameter names such as `{id}` vs `{orderId}` are normalized only for route matching, not payload validation.

These are route/verb-presence results, **not** an overall compatibility score. Selected schemas, pagination, deprecations, business rules and limits were then read and compared with code, producing the deltas below. Presence of a retired endpoint in an OpenAPI file is not evidence that it remains supported. There was no live confirmation or new execution probe for these deltas.

### D1 - Ozon promotion payload and response contracts have drifted

O `POST /v1/actions/products/update`, schema `actions.v1.ActionsProductsUpdateRequest.Product`, defines `action_price` as `money.MoneyAction_price`: an **object with string amount and currency**, not a decimal JSON number. [ActionModels:113-123](../../plugins/Delegat.Marketplace.Ozon/Models/ActionModels.cs#L113-L123) sends a decimal. This is a concrete serialization-contract mismatch despite using the right route.

O `POST /v2/actions/products` returns **top-level products / total / last_id**, schema `actions.v2.ActionsProductsResponse`. [ActionModels:61-84](../../plugins/Delegat.Marketplace.Ozon/Models/ActionModels.cs#L61-L84) expects an outer `result`. [GetPromotionAsync](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3811-L3835) and [ListActionProductIdsAsync](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3910-L3939) consume only that wrapper. A response following the supplied schema is therefore treated as no members; removeAll can stop with no IDs instead of traversing existing members. The latter loop also advances **offset**, whereas the current request contract uses **last_id**.

The money reader [ActionModels:188-222](../../plugins/Delegat.Marketplace.Ozon/Models/ActionModels.cs#L188-L222) understands units/nanos or numeric value, **not amount/currency**; it returns zero for the documented Money object. This is a separate latent mismatch even after fixing the response envelope. Mutation models handle several ID envelopes, but do not preserve current `warnings`. For elastic/maximal boosting, O documents withdrawal through price adjustment on `/v1/actions/products/update`; [the host-facing removal path](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L3876-L3890) instead applies `/v2/actions/products/deactivate` generically. Action type and side effects on the card's price must be part of validation, not hidden behind `UpdatePromotions`.

These are source/spec inconsistencies, not claims that a production request was rejected or that they explain a measured percentage of incidents.

### D2 - Ozon label retirement and total migration need different treatment

O `/v2/posting/fbs/package-label` still exists in the file but its description says **disabled from 2026-11-02**, with replacement `/v3/posting/fbs/package-label/create` then `/v2/posting/fbs/package-label/get`. [The client](../../plugins/Delegat.Marketplace.Ozon/OzonApiClient.cs#L435-L448) still calls the old synchronous route. The notice is already past at this audit date; no current-server alias or response was checked. The new asynchronous generation task needs a receipt/confirmation stage, not an endpoint-string replacement alone. The old method also documents 45-60 seconds after packing and all-or-nothing label generation for a request.

O descriptions for `/v3/product/list`, `/v4/product/info/stocks`, `/v5/product/info/prices` announce `total` -> `total_items` on **2026-11-23**. Current models/consumers already contain `TotalItems` / `ResolvedTotal`; do not publish this as an unhandled defect merely because a migration notice exists. Add exact-current fixtures for all relevant consumers instead of assuming every total-related path is either broken or certified.

### D3 - WB shipment route coverage is not the same as interface coverage

W03 provides these current routes:

- PATCH `/api/marketplace/v3/supplies/{supplyId}/orders`
- GET `/api/marketplace/v3/supplies/{supplyId}/order-ids`

[WildberriesApiClient:379-385](../../plugins/Delegat.Marketplace.Wildberries/WildberriesApiClient.cs#L379-L385) and [423-427](../../plugins/Delegat.Marketplace.Wildberries/WildberriesApiClient.cs#L423-L427) use `/api/v3/...` without `marketplace`. The add path is reached both from pack and explicit add-orders; the read participates in duplicate-membership reconciliation and detail loading. A partially created supply followed by a failed add is precisely the kind of multi-step state that needs durable continuation.

The third unmatched pair is PATCH `/api/v3/supplies/{supplyId}/trbx/{boxId}` in [the assignment client](../../plugins/Delegat.Marketplace.Wildberries/WildberriesApiClient.cs#L445-L454). No exact operation was found across the 13 current files. W03 does document box creation/listing and stickers, but these do not establish this assignment contract. Do not invent a replacement route. Documentation absence does not prove a live 404 or that an old compatibility alias is disabled; it does prevent certification of those three calls.

### D4 - Yandex order pagination is already past its parameter retirement

Y `components/parameters/PageNum.yaml` and `PageSize.yaml` give shutdown **2026-10-05**, replacing them with pageToken/limit. The called orders path still lists legacy parameter refs alongside token pagination, so merely finding `GET /orders` is insufficient.

[Client:200-215](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketApiClient.cs#L200-L215) sends page/pageSize; [plugin:1008-1017](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L1008-L1017) determines continuation only from legacy pager/currentPage/pagesCount. [OrderModels](../../plugins/Delegat.Marketplace.YandexMarket/Models/OrderModels.cs#L17-L70) has no paging.nextPageToken consumption. This is a current documented pagination incompatibility, not just a future method-retirement concern. Runtime alias handling was not exercised.

Separately, the list/detail operations have degradation-start **2027-01-18** and shutdown **2027-04-12** in `x-deprecation-config`. Those operation dates are still future; they must not be conflated with the already-past parameter date.

### D5 - Provider-specific completion and eligibility cannot be generalized away

- Y promotion offer update/delete descriptions say changes can take **4-6 hours** and refer to `processing` on the promotions read. Current mutation success/requested counts do not establish final participation. Confirmation deadlines must be stage/provider-specific.
- O stock writes require card status **price_sent**, max **100 product/warehouse pairs**, **80 calls/minute per seller**, and at most one update per pair in **30 seconds**. Too-fast pair updates can be a semantic `result.errors` failure, not only HTTP 429. This is a required plan/admission/test invariant, not a newly executed reproduction of host timing.
- A item listing says it returns listings of the owning user; an employee token can return an empty list. Price updates are category-restricted. Preflight must distinguish eligibility/permissions from an actually empty account or universally supported export.
- W02 card update overwrites the listing and permits adding sizes. W03 packing/supply/box rules and W08 campaign limits are separate contracts. A broad capability bit or an `I` implementation cell does not establish every state/action/category.

### Native platform files versus actual Delegat bridge wires

M Admin 2.23.0 separately defines order cancel, fulfillment, payment refund, collection add/remove and uploads (for example POST `/admin/orders/{id}/cancel`, `/admin/payments/{id}/refund`, `/admin/collections/{id}/products`). Those are useful for validating intended native effects; they do **not** document the bridge's `orders/{id}/status`, `groups/...` or result envelopes.

WC v3 docs explicitly cover native `/wp-json/wc/v3/...` products/variations, orders, refunds and coupons. `orders.mdx`, `order-refunds.mdx`, `product-variations.mdx`, `coupons.mdx` distinguish status, payment refund, per-variation stock and product restrictions. Actual adapter calls remain `/wp-json/delegat/v1/...`. WC Store API rate limiting is optional and on another surface; it is not a bridge RPS default.

OC platform docs and PHP `api/order.php` document a `call` dispatcher including cart/product/payment/shipping/confirm/history_add, not Delegat's categories/products.bulk/orders.status/promotions.products. Native session/auth, order behavior and platform limits cannot certify those custom actions.

For these three adapters the remaining evidence gap is **the installed bridge version's wire contract and bridge-to-platform behavior**, not absence of platform documentation. Require versioned bridge request/response fixtures, supported actions, semantic partial results and native-state read-back in the isolated shops. Do not feed the platform OpenAPI into a bridge mock and call it conformance.

## Called HTTP endpoint documentation ledger

`Local spec` means the exact HTTP verb and normalized route were found and the operation entry read in the current bundle, identified by file and operationId. It does not mean complete payload conformance or live success: D1-D5 show why. `Mismatch` means the called pair differs from the supplied current contract. `No exact contract on disk` is scoped to that pair/family, not a claim the whole provider API is undocumented.

For M/WC/OC, platform documentation is available but does not define the adapter's custom Delegat bridge wires. Their status is deliberately different from direct seller-API matches. The local IDs/files and versions are recorded in the baseline above. All verification here is offline.

Paths are templates; parameter-name differences alone are normalized. Dynamic returned URLs and browser subrequests cannot be certified by matching one fixed template.

### Avito

Source: [AvitoApiClient:34-218](../../plugins/Delegat.Marketplace.Avito/AvitoApiClient.cs#L34-L218). Product detail/batch/prices scan listing pages; there is no separate item-detail endpoint call here.

| Verb and route | Used for | Official contract |
| --- | --- | --- |
| POST `/token` | Client-credentials authorization | Local spec: `avito/specs/auth.openapi.json`; `getAccessToken` |
| GET `/core/v1/accounts/self` | Test/current account | Local spec: `avito/specs/user.openapi.json`; `getUserInfoSelf` |
| GET `/core/v1/items` | Import single/batch/all and prices via page scans | Local spec: `avito/specs/item.openapi.json`; `getItemsInfo` |
| GET `/autoload/v1/user-docs/tree` | Category tree | Local spec: `avito/specs/autoload.openapi.json`; `userDocsTree` |
| GET `/autoload/v1/user-docs/node/{slug}/fields` | Category fields | Local spec: `avito/specs/autoload.openapi.json`; `userDocsNodeFields` |
| GET `{valuesUrl.PathAndQuery}` on the allowed Avito API host | Field dictionary URL returned by metadata | Dynamic metadata URL; exact request contract not established from the supplied files |
| POST `/core/v1/items/{id}/update_price` | Price write | Local spec: `avito/specs/item.openapi.json`; `updatePrice` |

### Medusa

Source: [MedusaApiClient:40-169](../../plugins/Delegat.Marketplace.Medusa/MedusaApiClient.cs#L40-L169). All routes below are Delegat bridge-relative. M native contracts were read separately; they do not define these bridge wires.

| Verb and bridge route | Used for | Official contract |
| --- | --- | --- |
| GET `/health` | Connection test | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/categories` | Category tree | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/categories/{id}/fields` | Category schema | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/categories/upsert` | Create category | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/products/{id}` | Import one product | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/products/upsert` | Single export | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/products/bulk` | Bulk export | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/prices/bulk` | Prices | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/stocks/bulk` | Stock | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/orders` | Orders list | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/orders/{id}` | Order detail | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/orders/{id}/cancel` | Cancel/refund | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/orders/{id}/status` | Other seller action | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/orders/{id}/label` | Packing label | Platform docs available (M); Delegat bridge wire contract not supplied |
| GET `/groups` | Import groups | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/groups/upsert` | Create group | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/groups/{id}/products` | Add members | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/groups/{id}/products/remove` | Remove members | Platform docs available (M); Delegat bridge wire contract not supplied |
| POST `/groups/{id}/delete` | Delete group | Platform docs available (M); Delegat bridge wire contract not supplied |

### OpenCart

Source: [OpenCartApiClient:48-165](../../plugins/Delegat.Marketplace.OpenCart/OpenCartApiClient.cs#L48-L165), [URI construction:280-320](../../plugins/Delegat.Marketplace.OpenCart/OpenCartApiClient.cs#L280-L320). Query mode uses `index.php?route=extension/delegat_bridge/api/delegat&action=...`; `NormalizeAction` converts slash/underscore to dot. Local first-party [adapter instructions](../adapters/opencart.md) were read, but are not vendor-official endpoint verification.

| Verb and REST-style literal / query-mode action | Used for | Official contract |
| --- | --- | --- |
| GET `/health` / `health` | Connection test | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/categories` / `categories` | Categories | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/categories/{id}/fields` / `category.fields` plus category_id | Fields | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/categories/upsert` / `categories.upsert` | Create category | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/products` / `products` | Product import pages | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/products/upsert` / `products.upsert` | Single export | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/products/bulk` / `products.bulk` | Bulk export | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/prices/bulk` / `prices.bulk` | Prices | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/stocks/bulk` / `stocks.bulk` | Stock | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/orders` / `orders` | Orders list | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/orders/{id}` / `order` plus order_id | Order detail | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/orders.status` / `orders.status` | Order action, ID in payload | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/promotions` / `promotions` | Coupons list | Platform docs available (OC); Delegat bridge wire contract not supplied |
| GET `/promotions/{id}` / `promotion` plus promotion_id | Coupon detail | Platform docs available (OC); Delegat bridge wire contract not supplied |
| POST `/promotions.products` / `promotions.products` | Coupon products | Platform docs available (OC); Delegat bridge wire contract not supplied |

### Ozon

Source: [OzonApiClient:120-547](../../plugins/Delegat.Marketplace.Ozon/OzonApiClient.cs#L120-L547). All routes use the seller API base.

| Verb and route | Used for | Official contract |
| --- | --- | --- |
| POST `/v1/description-category/tree` | Categories | Local spec: `ozon/ozon-seller-api.openapi.json`; `DescriptionCategoryAPI_GetTree` |
| POST `/v1/description-category/attribute` | Fields | Local spec: `ozon/ozon-seller-api.openapi.json`; `DescriptionCategoryAPI_GetAttributes` |
| POST `/v1/description-category/attribute/values` | Dictionaries | Local spec: `ozon/ozon-seller-api.openapi.json`; `DescriptionCategoryAPI_GetAttributeValues` |
| POST `/v3/product/list` | Product IDs/pages | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetProductList` |
| POST `/v3/product/info/list` | Products | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetProductInfoList` |
| POST `/v4/product/info/attributes` | Product attributes | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetProductAttributesV4` |
| POST `/v3/product/import` | Create/update cards | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_ImportProductsV3` |
| POST `/v1/product/import/info` | Import task status | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetImportProductsInfo` |
| POST `/v1/product/update/offer-id` | SKU change | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_ProductUpdateOfferID` |
| POST `/v1/product/archive` | Archive | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_ProductArchive` |
| POST `/v5/product/info/prices` | Price read | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetProductInfoPrices` |
| POST `/v1/product/import/prices` | Price write | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_ImportProductsPrices` |
| POST `/v4/product/info/stocks` | Stock read | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetProductInfoStocks` |
| POST `/v2/products/stocks` | Stock write | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_ProductsStocksV2` |
| POST `/v2/warehouse/list` | Warehouses | Local spec: `ozon/ozon-seller-api.openapi.json`; `WarehouseListV2` |
| POST `/v4/product/info/limit` | Create/update/product quotas | Local spec: `ozon/ozon-seller-api.openapi.json`; `ProductAPI_GetUploadQuota` |
| POST `/v4/posting/fbs/list` | FBS orders | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingFbsList` |
| POST `/v3/posting/fbs/get` | FBS detail | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingAPI_GetFbsPostingV3` |
| POST `/v3/posting/fbo/list` | FBO orders | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingFboList` |
| POST `/v2/posting/fbo/get` | FBO detail | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingAPI_GetFboPosting` |
| POST `/v4/posting/fbs/ship` | Pack posting | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingAPI_ShipFbsPostingV4` |
| POST `/v2/posting/fbs/awaiting-delivery` | Ship action | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingAPI_MoveFbsPostingToAwaitingDelivery` |
| POST `/v2/posting/fbs/cancel` | Cancel | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingAPI_CancelFbsPosting` |
| POST `/v2/posting/fbs/package-label` | Label PDF | Local spec: `ozon/ozon-seller-api.openapi.json`; `PostingAPI_PostingFBSPackageLabel` |
| GET `/v1/actions` | Promotions | Local spec: `ozon/ozon-seller-api.openapi.json`; `Promos` |
| POST `/v2/actions/products` | Promotion members | Local spec: `ozon/ozon-seller-api.openapi.json`; `ActionsProducts` |
| POST `/v1/actions/products/update` | Add/update promotion products | Local spec: `ozon/ozon-seller-api.openapi.json`; `ActionsProductsUpdate` |
| POST `/v2/actions/products/deactivate` | Remove promotion products | Local spec: `ozon/ozon-seller-api.openapi.json`; `ActionsProductsDeactivate` |

### Wildberries

Source: [WildberriesApiClient:144-508](../../plugins/Delegat.Marketplace.Wildberries/WildberriesApiClient.cs#L144-L508). Domain aliases are C=Content, P=Prices, M=Marketplace/FBS, A=Calendar; production and sandbox host selection is separate.

| Domain, verb and route | Used for | Official contract |
| --- | --- | --- |
| C GET `/content/v2/object/parent/all` | Parent categories | Local spec: `wildberries/02-items.yaml`; `getV2ObjectParentAll` |
| C GET `/content/v2/object/all` | Subjects | Local spec: `wildberries/02-items.yaml`; `getV2ObjectAll` |
| C GET `/content/v2/object/charcs/{subjectId}` | Fields | Local spec: `wildberries/02-items.yaml`; `getV2ObjectCharcsSubjectId` |
| C GET `/content/v2/directory/colors` | Dictionary | Local spec: `wildberries/02-items.yaml`; `getV2DirectoryColors` |
| C GET `/content/v2/directory/kinds` | Dictionary | Local spec: `wildberries/02-items.yaml`; `getV2DirectoryKinds` |
| C GET `/content/v2/directory/countries` | Dictionary | Local spec: `wildberries/02-items.yaml`; `getV2DirectoryCountries` |
| C GET `/content/v2/directory/seasons` | Dictionary | Local spec: `wildberries/02-items.yaml`; `getV2DirectorySeasons` |
| C GET `/content/v2/directory/vat` | Dictionary | Local spec: `wildberries/02-items.yaml`; `getV2DirectoryVat` |
| C GET `/content/v2/directory/tnved` | TN VED dictionary | Local spec: `wildberries/02-items.yaml`; `getV2DirectoryTnved` |
| C POST `/content/v2/get/cards/list` | Import/card lookup/groups | Local spec: `wildberries/02-items.yaml`; `postV2GetCardsList` |
| C POST `/content/v2/cards/upload` | Create cards | Local spec: `wildberries/02-items.yaml`; `postV2CardsUpload` |
| C POST `/content/v2/cards/update` | Update cards/sizes | Local spec: `wildberries/02-items.yaml`; `postV2CardsUpdate` |
| C POST `/content/v2/cards/error/list` | Rejected drafts | Local spec: `wildberries/02-items.yaml`; `postV2CardsErrorList` |
| C POST `/content/v2/cards/delete/trash` | Archive | Local spec: `wildberries/02-items.yaml`; `postV2CardsDeleteTrash` |
| C GET `/content/v2/cards/limits` | Card capacity | Local spec: `wildberries/02-items.yaml`; `getV2CardsLimits` |
| C POST `/content/v2/cards/upload/add` | Create cards in a group | Local spec: `wildberries/02-items.yaml`; `postV2CardsUploadAdd` |
| C POST `/content/v2/cards/moveNm` | Merge/separate listings | Local spec: `wildberries/02-items.yaml`; `postV2CardsMoveNm` |
| C POST `/content/v3/media/save` | Media | Local spec: `wildberries/02-items.yaml`; `postV3MediaSave` |
| P GET `/api/v2/list/goods/filter` | Price lists/single lookup | Local spec: `wildberries/02-items.yaml`; `getV2ListGoodsFilter` |
| P POST `/api/v2/list/goods/filter` | Batched prices | Local spec: `wildberries/02-items.yaml`; `postV2ListGoodsFilter` |
| P POST `/api/v2/upload/task` | Product prices | Local spec: `wildberries/02-items.yaml`; `postV2UploadTask` |
| P POST `/api/v2/upload/task/size` | Variant prices | Local spec: `wildberries/02-items.yaml`; `postV2UploadTaskSize` |
| M GET `/api/v3/warehouses` | Warehouses | Local spec: `wildberries/02-items.yaml`; `getV3Warehouses` |
| M POST `/api/v3/stocks/{warehouseId}` | Stock read | Local spec: `wildberries/02-items.yaml`; `postV3StocksWarehouseId` |
| M PUT `/api/v3/stocks/{warehouseId}` | Stock write | Local spec: `wildberries/02-items.yaml`; `putV3StocksWarehouseId` |
| M GET `/api/v3/orders` | Dated orders | Local spec: `wildberries/03-orders-fbs.yaml`; `getV3Orders` |
| M GET `/api/v3/orders/new` | New assembly orders | Local spec: `wildberries/03-orders-fbs.yaml`; `getV3OrdersNew` |
| M POST `/api/v3/orders/status` | Status read | Local spec: `wildberries/03-orders-fbs.yaml`; `postV3OrdersStatus` |
| M PATCH `/api/v3/orders/{id}/cancel` | Cancel | Local spec: `wildberries/03-orders-fbs.yaml`; `patchV3OrdersOrderIdCancel` |
| M POST `/api/v3/orders/stickers` | Packing stickers | Local spec: `wildberries/03-orders-fbs.yaml`; `postV3OrdersStickers` |
| M GET `/api/v3/offices` | Offices | Local spec: `wildberries/02-items.yaml`; `getV3Offices` |
| M GET `/api/v3/supplies` | Shipments list | Local spec: `wildberries/03-orders-fbs.yaml`; `getV3Supplies` |
| M POST `/api/v3/supplies` | Create shipment | Local spec: `wildberries/03-orders-fbs.yaml`; `postV3Supplies` |
| M GET `/api/v3/supplies/{id}` | Shipment detail | Local spec: `wildberries/03-orders-fbs.yaml`; `getV3SuppliesSupplyId` |
| M PATCH `/api/v3/supplies/{id}/orders` | Add assembly orders | Mismatch: W03 documents PATCH `/api/marketplace/v3/supplies/{supplyId}/orders`; see D3 |
| M PATCH `/api/v3/supplies/{id}/deliver` | Deliver supply | Local spec: `wildberries/03-orders-fbs.yaml`; `patchV3SuppliesSupplyIdDeliver` |
| M GET `/api/v3/supplies/{id}/order-ids` | Membership/read-back | Mismatch: W03 documents GET `/api/marketplace/v3/supplies/{supplyId}/order-ids`; see D3 |
| M GET `/api/v3/supplies/{id}/trbx` | Boxes list | Local spec: `wildberries/03-orders-fbs.yaml`; `getV3SuppliesSupplyIdTrbx` |
| M POST `/api/v3/supplies/{id}/trbx` | Create boxes | Local spec: `wildberries/03-orders-fbs.yaml`; `postV3SuppliesSupplyIdTrbx` |
| M PATCH `/api/v3/supplies/{id}/trbx/{boxId}` | Assign orders | No exact contract on disk for this called pair across the 13 current OpenAPIs; see D3 |
| M POST `/api/v3/supplies/{id}/trbx/stickers` | Box stickers | Local spec: `wildberries/03-orders-fbs.yaml`; `postV3SuppliesSupplyIdTrbxStickers` |
| M GET `/api/v3/supplies/{id}/barcode` | Supply QR | Local spec: `wildberries/03-orders-fbs.yaml`; `getV3SuppliesSupplyIdBarcode` |
| A GET `/api/v1/calendar/promotions` | Calendar campaigns | Local spec: `wildberries/08-promotion.yaml`; `getV1CalendarPromotions` |
| A GET `/api/v1/calendar/promotions/nomenclatures` | Campaign members | Local spec: `wildberries/08-promotion.yaml`; `getV1CalendarPromotionsNomenclatures` |
| A POST `/api/v1/calendar/promotions/upload` | Add campaign members | Local spec: `wildberries/08-promotion.yaml`; `postV1CalendarPromotionsUpload` |

### WooCommerce

Source: [WooCommerceApiClient:41-210](../../plugins/Delegat.Marketplace.WooCommerce/WooCommerceApiClient.cs#L41-L210). Routes are relative to the custom `/wp-json/delegat/v1` namespace, not `/wc/v3`. Local [adapter instructions](../adapters/woocommerce.md) were read; native WC v3 contracts were read separately and do not certify these bridge wires.

| Verb and bridge route | Used for | Official contract |
| --- | --- | --- |
| GET `/health` | Connection test | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/categories` | Categories | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/categories/{id}/fields` | Schema | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/categories/upsert` | Create category | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/products/{id}` | Import single | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/products/upsert` | Export single | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/products/bulk` | Export bulk | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/prices` | Prices read | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/prices/bulk` | Prices write | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/stocks/bulk` | Stock write | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/images/import-archive` | Multipart ZIP media import | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/images/drain` | Legacy queued media | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/orders` | Orders | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/orders/{id}` | Order detail | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/orders/{id}/status` | Seller action | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/promotions` | Coupons | Platform docs available (WC); Delegat bridge wire contract not supplied |
| GET `/promotions/{id}` | Coupon detail | Platform docs available (WC); Delegat bridge wire contract not supplied |
| POST `/promotions/{id}/products` | Coupon membership/disable | Platform docs available (WC); Delegat bridge wire contract not supplied |

### Yandex Market

Source: [YandexMarketApiClient:47-258](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketApiClient.cs#L47-L258).

| Verb and route | Used for | Official contract |
| --- | --- | --- |
| GET `/v2/campaigns` | Resolve campaign/business | Local spec: `yandex/repo/openapi/paths/v2_campaigns.yaml`; `getCampaigns` |
| POST `/v2/auth/token` | Token information | Local spec: `yandex/repo/openapi/paths/v2_auth_token.yaml`; `getAuthTokenInfo` |
| POST `/v2/categories/tree` | Category tree | Local spec: `yandex/repo/openapi/paths/v2_categories_tree.yaml`; `getCategoriesTree` |
| POST `/v2/category/{categoryId}/parameters` | Category schema | Local spec: `yandex/repo/openapi/paths/v2_category_categoryId_parameters.yaml`; `getCategoryContentParameters` |
| POST `/v2/businesses/{businessId}/offer-mappings` | Import/lookup/images | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-mappings.yaml`; `getOfferMappings` |
| POST `/v2/businesses/{businessId}/offer-mappings/update` | Export cards/group field | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-mappings_update.yaml`; `updateOfferMappings` |
| POST `/v2/businesses/{businessId}/offer-cards/update` | Category characteristics | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-cards_update.yaml`; `updateOfferContent` |
| POST `/v2/businesses/{businessId}/offer-cards` | Content status/read-back | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-cards.yaml`; `getOfferCardsContentStatus` |
| POST `/v2/businesses/{businessId}/offer-mappings/archive` | Archive | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-mappings_archive.yaml`; `addOffersToArchive` |
| POST `/v2/businesses/{businessId}/offer-prices` | Price read | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-prices.yaml`; `getDefaultPrices` |
| POST `/v2/businesses/{businessId}/offer-prices/updates` | Price write | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_offer-prices_updates.yaml`; `updateBusinessPrices` |
| POST `/v2/campaigns/{campaignId}/offers/stocks` | Stock read | Local spec: `yandex/repo/openapi/paths/v2_campaigns_campaignId_offers_stocks.yaml`; `getStocks` |
| PUT `/v2/campaigns/{campaignId}/offers/stocks` | Stock write | Local spec: `yandex/repo/openapi/paths/v2_campaigns_campaignId_offers_stocks.yaml`; `updateStocks` |
| GET `/v2/campaigns/{campaignId}/orders` | Orders list | Local spec: `yandex/repo/openapi/paths/v2_campaigns_campaignId_orders.yaml`; `getOrders` |
| GET `/v2/campaigns/{campaignId}/orders/{orderId}` | Order detail | Local spec: `yandex/repo/openapi/paths/v2_campaigns_campaignId_orders_orderId.yaml`; `getOrder` |
| PUT `/v2/campaigns/{campaignId}/orders/{orderId}/status` | Seller action | Local spec: `yandex/repo/openapi/paths/v2_campaigns_campaignId_orders_orderId_status.yaml`; `updateOrderStatus` |
| GET `/v2/campaigns/{campaignId}/orders/{orderId}/delivery/labels` | Label file | Local spec: `yandex/repo/openapi/paths/v2_campaigns_campaignId_orders_orderId_delivery_labels.yaml`; `generateOrderLabels` |
| POST `/v2/businesses/{businessId}/promos` | Promotions list | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_promos.yaml`; `getPromos` |
| POST `/v2/businesses/{businessId}/promos/offers/update` | Add/change promotion offers | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_promos_offers_update.yaml`; `updatePromoOffers` |
| POST `/v2/businesses/{businessId}/promos/offers/delete` | Remove promotion offers | Local spec: `yandex/repo/openapi/paths/v2_businesses_businessId_promos_offers_delete.yaml`; `deletePromoOffers` |

### Storefront, dynamic reads, and media URL families

No exact official contract on disk was established for these storefront/dynamic families. A public web endpoint is not made stable/documented by having a local parser or retry limiter.

| Adapter/helper | Called URL family | Evidence boundary |
| --- | --- | --- |
| Ozon public catalog | GET `www.ozon.ru/api/composer-api.bx/page/json/v2?url=...` for `/search/` and `/product/{id}/`; HTML/browser navigation to those pages | [OzonPublicCatalogClient](../../plugins/Delegat.Marketplace.Ozon/OzonPublicCatalogClient.cs#L21-L99); captured browser subrequests are dynamic |
| WB public catalog | GET `search.wb.ru/exactmatch/ru/common/v18/search`; `card.wb.ru/cards/v4/detail`; `basket-{n}.wbbasket.ru/.../info/ru/card.json`; image URLs under `/images/big/{n}.webp` are constructed | [WbPublicCatalogClient](../../plugins/Delegat.Marketplace.Wildberries/WbPublicCatalogClient.cs); constructing an image URL is not itself a GET |
| Yandex public catalog | GET `market.yandex.ru/api/search`, `/api/resolve/`; HTML/browser `/search`, `/card/{id}` and captured `api/apphost` families | [YandexPublicCatalogClient](../../plugins/Delegat.Marketplace.YandexMarket/YandexPublicCatalogClient.cs#L21-L106); browser requests are not all covered by an outer HTTP request counter |
| Adapter sandbox-read brokers | Named Ozon/WB/Yandex POST reads reuse fixed seller routes above; path-read companions can forward permitted relative GET paths on the selected base | Companion interfaces, not a guarantee that arbitrary user-selected paths were officially reviewed; no broker was invoked |
| Bridge media downloads | Host blob reader or GET of prepared external image URLs, plus store-side download/thumbnail jobs | Provider/server URL is input-dependent; no seller route, full URL or live transfer certification inferred |


## Unexecuted test-environment acceptance work

**Nothing below was run in this audit.** The earlier 48 offline tests and seven defect reproductions are not passes on these environments. No Docker shop was started, no seller/test token was used, and no configured production connection was called. The linked Compose projects being present is not evidence their current images/bridges were tested.

### delegat-shop-opencart

Source setup: [sibling README](../../../delegat-shop-opencart/README.md) and [Compose](../../../delegat-shop-opencart/docker-compose.yml); OpenCart 4.1.0.4 test shop, default documented port 8089. No credentials are copied into this audit.

- [ ] Start an isolated fresh shop without resetting any existing volume; record exact shop/bridge/plugin versions and compare extension installed in the shop with the adapter contract.
- [ ] Check health/auth failures, REST-vs-query actions (especially `category.fields`, `orders.status`, `promotions.products`), language/store selection and unsupported bridge versions.
- [ ] Round-trip category/schema creation and product import pages; exercise single/batch methods that currently exist without advertised capability bits.
- [ ] Compare single/bulk create and update with the same intended fields; verify SKU identity, attributes, options/variants, dimensions/units, prices/special prices, stock and images by read-back in shop DB/API.
- [ ] Test bridge partial results, malformed JSON/HTML errors, proxy timeout after write, restart/replay and overlapping operations without creating duplicates.
- [ ] Seed paginated orders; verify all exposed filters, allowed/invalid status changes, host snapshot freshness, and remote-success/local-refresh-failure recovery. Assert labels/shipments/groups are rejected as unadvertised.
- [ ] Seed coupons; verify detail, add/remove, removeAll disable and older bridge behavior; never allow an emptied restriction list to expand a coupon store-wide.
- [ ] Load/backpressure tests for PHP/DB/proxy capacity and caller cancellation; no current RPS/quota policy has been certified for this custom bridge.

### delegat-shop-woocommerce

Source setup: [sibling README](../../../delegat-shop-woocommerce/README.md) and [Compose](../../../delegat-shop-woocommerce/docker-compose.yml); default documented port 8088. Source README identifies WordPress 7.1.4/WooCommerce 11.0.1; actual running versions were not inspected.

- [ ] Start an isolated shop; record installed bridge version rather than assuming the copy bundled with an adapter matches the shop repo/image.
- [ ] Test health/key/version compatibility, category/schema/create, single-product import, price import pages and unsupported stock-import/group/shipment/label feature gates.
- [ ] Round-trip simple and variable products, add/update variants, stable `_delegat_product_id`/`_delegat_variant_id` identity, single/bulk parity, sale prices, per-variant stock and repeated updates.
- [ ] Verify ZIP media manifest/order, exact photos attached after import, thumbnail completion, replacement semantics, 404/405 fallback to drain, pending/failed queues, archive limits and cancellation after card upsert.
- [ ] Seed multi-page orders; test filters and all status actions, tracking/reason payloads, snapshots, and ambiguous write/read-back failures.
- [ ] Seed more than one page of coupons; check `HasMore` against bridge/native totals, add/remove restrictions and removeAll disable with bridge >=1.3.1. Validate older bridge behavior explicitly.
- [ ] Test partial bulk responses, unknown/missing item IDs, rate/proxy errors, large chunks, restart and replay without duplicate products/variations/attachments.
- [ ] Stress concurrent image reads vs bridge writes, WordPress/PHP/DB capacity, fair scheduling and retry amplification; no production workload is authorized.

### Wildberries developer sandbox

The adapter already has [explicit domain/token selection](../../plugins/Delegat.Marketplace.Wildberries/WildberriesApiEnvironment.cs). The existence of this code is not a sandbox test. The supplied schemas document separate sandbox budgets/hosts for selected methods/categories; simulator state and coverage were not exercised. Unsupported mutations must be marked untestable, not silently sent to production.

- [ ] Use a designated test token; prove every requested domain is sandbox (Content, Prices, Marketplace, Calendar), including JWT/override/error-learning paths. No fallback to production is an acceptable test recovery.
- [ ] Category/dictionary availability, pagination, metadata faults and cache invalidation with synthetic values.
- [ ] New/update/archive cards, rejected drafts, asynchronous accepted-but-invisible cards, create/update grouping, merge and missing removal implementation; distinguish simulator constraints from seller-API contracts.
- [ ] Multi-size cards: add sizes, stable chrtID/barcodes, units, media, per-size prices and stocks by warehouse; wait for the documented synchronization window before dependent writes.
- [ ] New/dated/status orders, pagination, all supported seller actions and unsupported ones; pack/create supply/add/create box/assign/ship transitions, duplicate 409 membership reconciliation and lost-response recovery.
- [ ] All ten shipment methods, warehouse/office lists, box and order stickers, supply QR before/after delivery, malformed label content and partial detail loads.
- [ ] Calendar list/member reads/add uploads, asynchronous upload status and rejected items; assert removal/removeAll is explicitly unsupported. First establish which calendar methods the sandbox actually simulates.
- [ ] Method/category/account rate buckets, interval/burst, weighted error cost, free/paid card capacity, long Retry-After, parallel jobs and multiple process instances. An intentional quota violation requires a separately agreed bounded test.
- [ ] Document every unsupported sandbox method/category and require a separate explicitly authorized canary environment for remaining acceptance; never substitute configured live adapters.

## Rate limits: declared status versus actual request admission

This is a source/documentation comparison, not a load test or a measurement of configured seller accounts. Official rules below were actually read; missing scope/burst/daily/header information is explicitly unknown, not interpreted as unlimited. Vendor limits depend on method, account, token class, tariff and environment and can change independently of a plugin release.

### What is common today

[RateLimitConfig](../../sdk/Delegat.Marketplace.SDK/RateLimit/RateLimitConfig.cs) has exactly one property: `MaxRequestsPerSecond`. The shared [MarketplaceRateLimiter](../../sdk/Delegat.Marketplace.SDK/RateLimit/MarketplaceRateLimiter.cs) uses process-local timestamps for a sliding one-second window. The utility additionally supports optional in-flight leases and temporary pauses; those features are not represented in `RateLimitConfig` and are not consistently used by adapters.

The window counts permits acquired, not independently observed provider-side usage. Its `Count < limit` check and subsequent enqueue are not one atomic reservation, so concurrent non-serialized callers can pass the check together. There is no common per-minute/hour/day account budget, method bucket, minimum spacing, weighted request cost, business-quota reservation or durable reset/cooldown state. Static dictionaries do not coordinate two processes, two customer instances, external seller tools or a restart.

The host product queue groups work and dispatches adapter calls; it does not turn `GetRateLimitConfig()` into outbound request admission. One product job can make many metadata, identity, media, polling, price, stock and reconciliation requests; bulk size is not an HTTP request budget. Wrapper [80-97](../../backend/Services/Plugins/PluginAdapterWrapper.cs#L80-L97) only forwards rate metadata/status access.

The host [GET connection limits endpoint:439-517](../../backend/Controllers/MarketplaceConnectionController.cs#L439-L517) displays configured RPS, the locally registered limiter's current/total count and best-effort provider quota status. It may fetch quota HTTP and cache nonempty results for 15 minutes; it is **not an admission controller**. `supported=false` means no plugin rate config, not unlimited provider capacity. An empty quota list is not proof that all quotas are available. No quota or connection was queried live in this audit.

### Actual wait paths in each adapter

| Adapter | Advertised/common metadata | What actually waits before HTTP | Key/scope and retry limitations |
| --- | --- | --- | --- |
| Avito | `MaxRequestsPerSecond=1`; inherits empty quotas and null limiter-status accessor | [Read broker:43-58](../../plugins/Delegat.Marketplace.Avito/AvitoPlugin.cs#L43-L58) acquires a connection-keyed one-second permit. **Normal catalog/dictionary/price requests do not acquire it**: [SendJsonAsync and token HTTP:144-218](../../plugins/Delegat.Marketplace.Avito/AvitoApiClient.cs#L144-L218) go straight to HttpClient | Broker dictionary keyed by connectionId; `AcquireAsync`, not a lease, so constructor maxConcurrent=1 does not serialize in-flight requests. A fresh broker client can make token HTTP plus the GET under one outer permit. Ordinary client has no 429/Retry-After wait/retry. Limits UI cannot retrieve that private broker limiter and can show zero usage |
| Medusa | SDK `GetRateLimitConfig=null`, `GetQuotasAsync=[]`, `GetRateLimiterForConnection=null` | No shared HTTP rate admission in [MedusaApiClient](../../plugins/Delegat.Marketplace.Medusa/MedusaApiClient.cs) | Requests/timeouts and provider errors are not a quota policy. Bridge deployment/proxy/application limits are unknown. No common Retry-After handling or shared cooldown |
| OpenCart | Same inherited null/empty metadata | No shared rate admission in [OpenCartApiClient](../../plugins/Delegat.Marketplace.OpenCart/OpenCartApiClient.cs) | Single/bulk loops and chunk sizes do not enforce account/method quotas. Query-action and REST modes share this absence; no Retry-After admission policy |
| Ozon | [439-510](../../plugins/Delegat.Marketplace.Ozon/OzonPlugin.cs#L439-L510): 50 RPS; reads daily create/update/total quota status | Seller [OzonApiClient:71-72](../../plugins/Delegat.Marketplace.Ozon/OzonApiClient.cs#L71-L72) acquires a local 50-per-second permit before each attempt. Separate public storefront client has **2 RPS / 2 in-flight leases**, not the seller budget | Seller key is Client-Id, or stable `oauth:{sellerId}` / connection fallback. No method/entity/hour/day admission. On 429, up to 3 additional attempts, Retry-After **Delta only**, otherwise 1 second; no shared PauseFor for all competing requests and no Ratelimit-Remaining/Item headers policy. Separate OAuth/API-key keys for the same seller are not proven to share the provider budget |
| Wildberries | [298-342](../../plugins/Delegat.Marketplace.Wildberries/WildberriesPlugin.cs#L298-L342): 1 RPS, reads free/paid card capacity | [Client:77-82](../../plugins/Delegat.Marketplace.Wildberries/WildberriesApiClient.cs#L77-L82) uses 1 RPS plus 1 in-flight lease across its API domains; same limiter is passed to public catalog reads. Two media phases additionally have per-call SemaphoreSlim(5), not five independent provider permits | **Dictionary key is the first eight token characters, not a seller ID or cryptographic hash.** Different credentials with a common prefix share a bucket; same-seller credentials with different prefixes need not. One additional 429 retry; X-Ratelimit-Retry then Retry-After seconds/date, default 2 seconds, clamped to 1-60 seconds; shared PauseFor. Header delays above 60 seconds are shortened. Some directory calls disable 429 retry. No per-method bucket or account-wide cross-process counter |
| WooCommerce | Same inherited null/empty metadata as OpenCart/Medusa | No shared rate admission in [WooCommerceApiClient](../../plugins/Delegat.Marketplace.WooCommerce/WooCommerceApiClient.cs); image byte-read semaphore only bounds local media work | Chunk-size and image parallelism are not request-rate/coupon/order budgets. No common Retry-After/cooldown handling; custom bridge and PHP/proxy limits unmeasured |
| Yandex Market | [292-301](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketPlugin.cs#L292-L301): 50 RPS; quota override explicitly returns an empty list | [Seller client:30-39](../../plugins/Delegat.Marketplace.YandexMarket/YandexMarketApiClient.cs#L30-L39) waits on a process-local API-key bucket. Public catalog independently uses **2 RPS / 2 in-flight leases** | Seller 420/429 gets up to 3 additional attempts, Retry-After Delta only, otherwise 2/4/6 seconds; no shared server cooldown or hour/minute tariff budget. Per-key scope is not proof of matching provider account/resource scope. Browser fallback subrequests are outside the simple outer HTTP counter |

The Avito distinction is intentional: “declares 1 RPS” is true; “all Avito integration HTTP is throttled at 1 RPS per connection” is not true in this source. Likewise WB's local dictionary key must not be described as a verified per-account limiter.

The named Ozon/WB/Yandex read-broker companions call the common [MarketplaceSandboxReadBrokerHttp transport](../../sdk/Delegat.Marketplace.SDK/MarketplaceSandboxReadBrokerHttp.cs#L120-L227), not their seller ApiClient request methods. That transport has no acquisition of those client limiters. Any outer host tool safety limit is not evidence of sharing the provider's account/method budget. These read paths must be included in the future admission coordinator rather than silently counted as already covered by the ApiClient RPS.

Ozon and WB quota endpoints are called from `GetQuotasAsync`; reading/caching these values for the limits panel is not reservation or enforcement in product dispatch. WB free/paid card capacity is not automatically a daily-reset counter. Ozon daily limits are seller-specific values, not constants that can be inferred from the SDK.

### Official rules in the current local files

Only the [current local baseline](#current-local-documentation-baseline) is authoritative for this comparison. This replaces the prior online/snippet table, including its erroneous WB error-list allowance and the treatment of Yandex item quotas as request quotas. Limits not stated in the supplied contract remain unknown, not unlimited. No seller's actual tier, token class, quota usage or reset was queried.

| Adapter / family | Local rule, scope, burst/day/reset | What current code does not model |
| --- | --- | --- |
| Avito listing / price | A `item.openapi.json`: GET `/core/v1/items` **25 requests/minute**; price update **150/minute**, category-restricted. Listing documents owner-user access and can be empty for an employee. Listing 429 declares X-RateLimit-Limit/Remaining. Burst, exact cross-token account aggregation, daily budget and Retry-After are not established here; auth's 24-hour token lifetime is not a daily request allowance | Ordinary HTTP does not acquire the declared 1-RPS broker limiter. No 25/minute bucket, shared cooldown or eligibility preflight |
| Avito auth/account/autoload | Route schemas exist. Method-specific numeric rate/burst/day and retry-delay policy were not established for these calls; no unrelated Avito/Autoteka product quota is substituted | Token requests and dictionaries need explicit admission and unknown-policy handling, not hidden auxiliary traffic |
| Ozon general | O `tags[5]` Limits: **50 requests/second per Client ID** unless a method specifies its own rule; Ratelimit-Remaining and Retry-After **seconds**. Independent additional burst value not established | Local 50/second is a default only; no method/entity budget, remaining-header feedback or cross-process account coordination |
| Ozon stock | `/v2/products/stocks`: **80 calls/minute/seller**, max **100 product/warehouse pairs/request**, a given pair at most **once per 30 seconds**; too-fast pair updates return semantic `result.errors` TOO_MANY_REQUESTS; requires price_sent | No per-pair next-eligible timestamp or coordinated stock coalescing; HTTP-only 429 handling misses semantic throttle errors |
| Ozon prices | `/v1/product/import/prices`: **10 price changes/hour/product**; max request size comes from its schema, not the general RPS config | No entity/hour reservation; short retries do not fix an exhausted product budget |
| Ozon import/SKU/quota | Import max **100 items/request**. Item-Retry-After is **MINUTES**, unlike general Retry-After seconds. Daily reset described as **03:00 Moscow**; Item-Rate-Limit-Remaining reports operations. `/v4/product/info/limit` exposes daily/assortment/operation/restore limits as applicable; account values are dynamic | Current Delta-only general retry does not consume Item-* signals, preserve units or reserve item operations. Panel projection/cache is not admission and omits operation_limits |
| WB Content | W02 ordinary category/field/group methods use **100/minute/profile**, **600 ms**, burst **5**. Card list is **80/minute**, **750 ms**, burst **5**. Create/update are **10/minute**, **6 s**, burst **5**. **Error list is 8/minute, 7500 ms, burst 5** | A common 1/second window can exceed slower methods while unnecessarily coupling other families. Polls also consume these budgets |
| WB token-class exceptions | W02 colors/kinds/seasons/vat, trash and capacity tables distinguish Personal/Service/Base-with-secret from Base; selected Base allowances are **2/hour**, **30-minute interval**, burst **1**. Group upload/add Base is **1 per 2 hours**, interval **2 hours**, burst **1** | Token prefix is not token-class/profile policy. Directory retry-disabled behavior is not safe scheduling of a two-hour allowance |
| WB Marketplace/FBS | W02/W03 described warehouse/FBS families: **300/minute/profile**, **200 ms**, burst **20**; 4XX cost **10 requests**. Selected sandbox descriptions: **1/second aggregate per Marketplace category** | No weighted error debt or profile/method buckets; single local 1-RPS setting does not prove either production or sandbox compliance |
| WB prices/media/calendar | W02 price methods: **10 per 6 seconds**, **600 ms**, burst **5**; media save **100/minute**, **600 ms**, burst **5**. W08 Calendar list/upload token tables: Personal/Service/Base-secret **10 per 6 seconds**, **600 ms**, burst **5**; Base **1/hour**, burst **1**. Member-list table gives 10/6s without a separate Base row; do not invent that row | No token/category policy. Free/paid card capacity is separate from HTTP/daily budgets |
| WB response headers | W01 explains X-Ratelimit-Retry in **seconds**, Limit as maximum burst and Reset as seconds to replenish burst. Method-specific sandbox/production tables are distinct | Code parses Retry and Retry-After but truncates waits to 60 seconds and only shares a process-local pause; no durable long cooldown |
| Yandex concurrency/scope | `yandex/limits.md`: **4 parallel** per campaign for campaign URLs, per business for business URLs; otherwise API-key business or OAuth user. Resource limits apply per login. Order list/detail path YAML also declares parallel-limit-value **6** | This internal documentation tension needs clarification, not silent selection of 6. Use conservative 4 until resolved. Current per-key 50-RPS bucket does not model either concurrency/scope rule |
| Yandex resource cost | Per-path policies below distinguish request count (`resp`) from request/response **collection elements**. none/medium tiers differ. 2xx and 4xx consume resource cost; 5xx do not | Previously describing mappings/prices limits as "requests/minute" was wrong. Need weighted input and response reconciliation, not a universal token per HTTP attempt |
| Yandex reset/body | Resource headers: X-RateLimit-Resource-Limit, **Until (RFC822 date)**, Remaining; exceeded limits return **420**. General body max **512 KB**. No Retry-After guarantee is inferred from those headers | Current client reads only Retry-After Delta with 2/4/6-second fallback, ignores Until/Remaining and has no shared reset or payload-byte admission |
| Medusa Delegat bridge | Native M 2.23.0 docs exist; no applicable numeric method/account/burst/day/Retry-After policy for the actual custom bridge wires was established | Inherits null rate config / empty quota / null limiter; platform docs cannot manufacture a bridge budget |
| WooCommerce Delegat bridge | WC native REST v3 docs exist. `docs/apis/store-api/rate-limiting.md` describes a **different**, optional, default-disabled Store API policy (25/10s; UI checkout 3/60s). Neither is a Delegat bridge default | Inherits no rate admission; image semaphore is not a bridge request budget |
| OpenCart Delegat bridge | OC platform docs/PHP exist; actual Delegat bridge method/account/burst/day/retry policy remains unestablished | Native api/order dispatcher is not the bridge API, and null rate metadata is not unlimited server capacity |
| Public storefronts | No exact official seller-API contract on disk for Ozon composer, WB search/card/basket, Yandex storefront/browser families | Local Ozon/Yandex 2-RPS policies and WB shared bucket are best-effort, not official quotas or browser-wide accounting |

The per-method tables below quote the current files. Presence of a rate rule does not certify a mismatched route or its payload.

### Yandex per-operation resource budgets

Source: Y path YAML at the recorded commit; `resp` is a request/response-count budget, while a collection path counts elements. Values show none / medium tiers. All periods have duration 1.

| Called pair | Cost path | none / medium allowance | Period |
| --- | --- | --- | --- |
| GET `/v2/campaigns` | `resp` | 1000 / 1000 | hours |
| POST `/v2/auth/token` | `resp` | 100 / 100 | hours |
| POST `/v2/categories/tree` | `resp` | 50 / 100 | hours |
| POST `/v2/category/{categoryId}/parameters` | `resp` | 100 / 100 | minutes |
| POST `/v2/businesses/{businessId}/offer-mappings` | `resp` | 100 / 600 | minutes |
| POST `/v2/businesses/{businessId}/offer-mappings/update` | `req.offerMappings` | 5000 / 10000 | minutes |
| POST `/v2/businesses/{businessId}/offer-cards/update` | `req.offersContent` | 5000 / 10000 | minutes |
| POST `/v2/businesses/{businessId}/offer-cards` | `resp` | 100 / 600 | minutes |
| POST `/v2/businesses/{businessId}/offer-mappings/archive` | `req.offerIds` | 5000 / 10000 | minutes |
| POST `/v2/businesses/{businessId}/offer-prices` | `resp.result.offers` | 5000 / 10000 | minutes |
| POST `/v2/businesses/{businessId}/offer-prices/updates` | `req.offers` | 10000 / 10000 | minutes |
| POST `/v2/campaigns/{campaignId}/offers/stocks` | `resp.result.warehouses.offers.offerId` | 100000 / 100000 | minutes |
| PUT `/v2/campaigns/{campaignId}/offers/stocks` | `req.skus` | 100000 / 100000 | minutes |
| GET `/v2/campaigns/{campaignId}/orders` | `resp` | 10000 / 10000 | hours |
| GET `/v2/campaigns/{campaignId}/orders/{orderId}` | `resp` | 10000 / 10000 | hours |
| PUT `/v2/campaigns/{campaignId}/orders/{orderId}/status` | `resp` | 10000 / 10000 | hours |
| GET `/v2/campaigns/{campaignId}/orders/{orderId}/delivery/labels` | `resp` | 10000 / 10000 | hours |
| POST `/v2/businesses/{businessId}/promos` | `resp` | 1000 / 1000 | hours |
| POST `/v2/businesses/{businessId}/promos/offers/update` | `resp` | 5000 / 5000 | hours |
| POST `/v2/businesses/{businessId}/promos/offers/delete` | `resp` | 5000 / 5000 | hours |

### WB per-operation limit tables read

Source: current W02/W03/W08 descriptions. The final column quotes the file's period / allowance / interval / burst rows verbatim, including token-class rows where present. Profile/category scope, sandbox differences and header rules are summarized above; three unmatched called pairs cannot inherit a guessed current policy.

| Called pair | Local file | Quoted policy rows |
| --- | --- | --- |
| GET `/content/v2/object/parent/all` | `wildberries/02-items.yaml` | 1 мин / 100 запросов / 600 мс / 5 запросов |
| GET `/content/v2/object/all` | `wildberries/02-items.yaml` | 1 мин / 100 запросов / 600 мс / 5 запросов |
| GET `/content/v2/object/charcs/{subjectId}` | `wildberries/02-items.yaml` | 1 мин / 100 запросов / 600 мс / 5 запросов |
| GET `/content/v2/directory/colors` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| GET `/content/v2/directory/kinds` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| GET `/content/v2/directory/countries` | `wildberries/02-items.yaml` | 1 мин / 100 запросов / 600 мс / 5 запросов |
| GET `/content/v2/directory/seasons` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| GET `/content/v2/directory/vat` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| GET `/content/v2/directory/tnved` | `wildberries/02-items.yaml` | 1 мин / 100 запросов / 600 мс / 5 запросов |
| POST `/content/v2/get/cards/list` | `wildberries/02-items.yaml` | 1 мин / 80 запросов / 750 мс / 5 запросов |
| POST `/content/v2/cards/upload` | `wildberries/02-items.yaml` | 1 мин / 10 запросов / 6 сек / 5 запросов |
| POST `/content/v2/cards/update` | `wildberries/02-items.yaml` | 1 мин / 10 запросов / 6 сек / 5 запросов |
| POST `/content/v2/cards/error/list` | `wildberries/02-items.yaml` | 1 мин / 8 запросов / 7500 мс / 5 запросов |
| POST `/content/v2/cards/delete/trash` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| GET `/content/v2/cards/limits` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| POST `/content/v2/cards/upload/add` | `wildberries/02-items.yaml` | Персональный / 1 мин / 10 запросов / 6 сек / 5 запросов; Сервисный / 1 мин / 10 запросов / 6 сек / 5 запросов; Базовый с секретом / 1 мин / 10 запросов / 6 сек / 5 запросов; Базовый / 2 ч / 1 запрос / 2 ч / 1 запрос |
| POST `/content/v2/cards/moveNm` | `wildberries/02-items.yaml` | 1 мин / 100 запросов / 600 мс / 5 запросов |
| POST `/content/v3/media/save` | `wildberries/02-items.yaml` | Персональный / 1 мин / 100 запросов / 600 мс / 5 запросов; Сервисный / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый с секретом / 1 мин / 100 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 2 запроса / 30 мин / 1 запрос |
| GET `/api/v2/list/goods/filter` | `wildberries/02-items.yaml` | Персональный / 6 сек / 10 запросов / 600 мс / 5 запросов; Сервисный / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый с секретом / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 4 запроса / 15 мин / 1 запрос |
| POST `/api/v2/list/goods/filter` | `wildberries/02-items.yaml` | Персональный / 6 сек / 10 запросов / 600 мс / 5 запросов; Сервисный / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый с секретом / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 4 запроса / 15 мин / 1 запрос |
| POST `/api/v2/upload/task` | `wildberries/02-items.yaml` | Персональный / 6 сек / 10 запросов / 600 мс / 5 запросов; Сервисный / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый с секретом / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 4 запроса / 15 мин / 1 запрос |
| POST `/api/v2/upload/task/size` | `wildberries/02-items.yaml` | Персональный / 6 сек / 10 запросов / 600 мс / 5 запросов; Сервисный / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый с секретом / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 4 запроса / 15 мин / 1 запрос |
| GET `/api/v3/warehouses` | `wildberries/02-items.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| POST `/api/v3/stocks/{warehouseId}` | `wildberries/02-items.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| PUT `/api/v3/stocks/{warehouseId}` | `wildberries/02-items.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/orders` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/orders/new` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| POST `/api/v3/orders/status` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| PATCH `/api/v3/orders/{id}/cancel` | `wildberries/03-orders-fbs.yaml` | 1 мин / 100 запросов / 600 мс / 20 запросов |
| POST `/api/v3/orders/stickers` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/offices` | `wildberries/02-items.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/supplies` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| POST `/api/v3/supplies` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/supplies/{id}` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| PATCH `/api/v3/supplies/{id}/orders` | No exact match | Contract mismatch / no exact policy for the called pair |
| PATCH `/api/v3/supplies/{id}/deliver` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/supplies/{id}/order-ids` | No exact match | Contract mismatch / no exact policy for the called pair |
| GET `/api/v3/supplies/{id}/trbx` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| POST `/api/v3/supplies/{id}/trbx` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| PATCH `/api/v3/supplies/{id}/trbx/{boxId}` | No exact match | Contract mismatch / no exact policy for the called pair |
| POST `/api/v3/supplies/{id}/trbx/stickers` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v3/supplies/{id}/barcode` | `wildberries/03-orders-fbs.yaml` | 1 мин / 300 запросов / 200 мс / 20 запросов |
| GET `/api/v1/calendar/promotions` | `wildberries/08-promotion.yaml` | Персональный / 6 сек / 10 запросов / 600 мс / 5 запросов; Сервисный / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый с секретом / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 1 запрос / 1 ч / 1 запрос |
| GET `/api/v1/calendar/promotions/nomenclatures` | `wildberries/08-promotion.yaml` | 6 сек / 10 запросов / 600 мс / 5 запросов |
| POST `/api/v1/calendar/promotions/upload` | `wildberries/08-promotion.yaml` | Персональный / 6 сек / 10 запросов / 600 мс / 5 запросов; Сервисный / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый с секретом / 6 сек / 10 запросов / 600 мс / 5 запросов; Базовый / 1 ч / 1 запрос / 1 ч / 1 запрос |

### Required shared mechanism, without a rewrite or new queue product

Use one **host-owned request admission coordinator** across every adapter HTTP entry point, including metadata, OAuth/token calls, read brokers, polls, media and retries. Adapters retain native payloads and publish versioned transport policy descriptors through a companion SDK contract. The product/order/shipment workflow uses the admission result; it does not guess request cost from the number of products.

The policy must identify provider, environment, stable seller/account identity, token class/tariff where relevant, route/verb family, operation stage, and entity/warehouse when the provider quota is entity-scoped. Do not partition by connection ID or a raw token prefix when multiple connections/keys consume one seller budget. Do not log raw credentials to construct a scope key.

| Missing from current RateLimitConfig | Required representation/behavior |
| --- | --- |
| Only a one-second integer | Multiple simultaneous windows and token buckets: period, allowance, refill/interval, burst, minimum spacing and optional concurrency; acquire all applicable scopes atomically |
| No route identity or shared scope | Stable normalized route IDs and account/category/method/entity bucket hierarchy; aliases share a budget when official rules say so |
| No request cost model | HTTP attempt cost, item count, product/warehouse pairs, per-product price changes and provider-specific error cost/debt; count retries and auxiliary requests too |
| No daily/business quota | limit/usage/reset/timezone and source freshness for daily_create/daily_update/assortment/card capacity; reserve intended mutations and reconcile with provider usage, not just display cached status |
| No durable server cooldown | Observe vendor remaining/reset headers, Retry-After seconds **and HTTP date**, Item-* headers and WB headers; keep account/route blocked-until and never shorten an authoritative long wait just to fit a local retry loop |
| No cross-worker ownership | Atomic budget reservations/leases across concurrent workers sharing an account; persist long cooldowns/next-eligible state and coordinate all active processes, not just one static dictionary |
| No rate-wait outcome | Admission returns admitted lease, deferred-until with reason, or quota/preflight blocker. Waiting for rate capacity is nonterminal and not an export failure. Persist delayed continuation instead of occupying a worker or exhausting three short retries |
| No policy evidence/version | Official source, reviewed date/version, applicable token/environment/tier and explicit unknown/unsupported limits; prevent accidental promotion of a guessed number to universal policy |
| No scheduling/fairness contract | Bound in-flight calls and give order/fulfillment deadlines fair access without starving exports; combine permitted item/byte batching with actual request budgets |
| No safe-retry contract | Keep throttling separate from idempotency. A 429 delay does not authorize replay of an ambiguous write. Persist receipts/stages and reconcile first when the prior mutation may have succeeded |
| No decision telemetry | Record route/scope, attempt, budget/cost, queue wait, next eligible time, provider reset and reason. `/limits` becomes an observation of this coordinator, not an independent display of a plugin guess |

The current local contracts additionally require explicit cost units (requests vs input/output elements vs product/warehouse mutations), delayed reconciliation of response-sized cost, vendor-specific header units (notably Ozon Item-Retry-After minutes), per-entity spacing, and documented general/method policy precedence. Conflicting local concurrency statements must remain visible rather than silently becoming configuration.

The host must not keep a database transaction open while waiting for admission or provider HTTP. A completed stage is not replayed because its later rate-limited dependency was delayed. Existing queues can persist delayed work and checkpoints; choosing a new queue product is not the first requirement.

### Unrun rate-policy acceptance cases

- [ ] Atomic admission at a bucket boundary under concurrent callers; no `Count`/enqueue oversubscription and no starvation between order and export work.
- [ ] Same seller represented by multiple connections/tokens, same-prefix WB tokens belonging to different sellers, rotating OAuth credentials, multiple processes and a restart.
- [ ] Method/category/account/entity buckets together: WB 10/min card methods vs Content/FBS; Ozon 80/min stocks and 10/hour per product; Yandex minute/hour/tier routes; Avito token+GET and ordinary price calls.
- [ ] Burst/interval and weighted error debt, per-request item/byte caps, daily reset with provider timezone, cached quota freshness and external usage changes.
- [ ] 420/429 with numeric Retry-After, HTTP-date, missing/malformed headers, WB retry over 60 seconds, Item-Retry-After, cancelled waits, and durable continuation after a long cooldown.
- [ ] Provider accepted a write but response/read-back was lost before the rate wait: reconciliation continues without repeating completed mutation stages.
- [ ] Confirm no HTTP path, read broker, image/browser fallback or retry bypasses the intended policy; explicitly label non-seller storefront traffic and deployment-specific bridge limits.
