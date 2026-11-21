# Integration reliability: bounded implementation plan

Date: 2026-11-20. Status: **Offline implementation complete (P1–P8); live/isolated external gates blocked.** `IntegrationExecutionV1` remains **OFF** by default until the externally blocked MySQL (`DELEGAT_TEST_MYSQL`), isolated OpenCart/WooCommerce shop, and Wildberries developer sandbox gates in §11 are executed against isolated destinations.

Evidence and architectural rationale: [integration reliability audit](../architecture/integration-reliability-audit.md). Baseline: host `71318b9aae47401f4306cd3f858393994f9521ea`, SDK and adapter revisions in the audit, and the local 2026-11-20 provider bundle. Revalidate touched code before editing; do not silently expand the baseline when new findings appear.

## 1. Contract and scope lock

**Deliver one durable integration-command mechanism for the existing supported surface, not a general workflow platform.** Keep the current host, MySQL, plugin loader, SDK, bridges, operation panel and public routes.

The program consists of **8 work packages and 14 planned PRs: 8 host PRs, 1 SDK PR and 1 PR in each of the 5 adapter submodule repositories**. Avito and Medusa sources belong to the host repository; the other five adapters and SDK are separate repositories in `.gitmodules`. A work package is closed only when its linked PRs and acceptance gate are complete.

Review-driven corrections may add commits or narrowly scoped corrective PRs under section 9; they do not authorize a ninth work package or another feature stream. This is a scope budget, not a claim about elapsed time, defect count or lines of code.

### In scope

- Avito, Medusa, OpenCart, Ozon, Wildberries, WooCommerce and Yandex Market, using the audited contract/capability inventory as the frozen coverage index.
- Existing product export/create/update, bulk parity, supported variants/media/price/stock/grouping; existing order actions, shipments/packing/labels, promotions/coupons and group actions.
- Existing reads and long imports: truthful unavailable/partial results, cursor completion, current contract fixes and preservation of existing atomic local import/receipt behavior.
- Shared preparation, durable stages/attempts, safe recovery/Stop, per-entity ordering and request admission inside one instance database.
- The concrete F1-F18 defects and D1-D4 deltas in the audit, with D5 treated as domain constraints rather than a request for new features.
- Minimal API/operation-panel adaptation, necessary localized issue codes, contract fixtures and isolated acceptance.

An advertised base-class stub is blocked for that subaction before mutation; this plan does not implement the missing provider feature to make a broad flag true. Existing working SDK fallbacks remain supported after semantic fixes. Implemented but unadvertised features do not become newly enabled automatically. Do not remove a genuinely supported action merely to make its tests pass.

### Explicitly out of scope

New adapters, new commerce features, unrestricted category certification, AI/Control Plane changes, native platform rewrites, automatic business compensation/refunds, force-replay UI, new marketplace-account provisioning, performance optimization campaigns and a redesigned operation UI.

No Redis/RabbitMQ/Kafka/Temporal, new microservice, workflow DSL/editor, general DAG/event-sourcing framework, new production infrastructure dependency, generic policy editor, replacement mapping engine, generation of all provider clients or new observability platform. Cross-customer/cross-database quota coordination is excluded; other seller tools remain external quota consumers.

The only allowed bridge code changes are compatibility fixes in existing bundled bridge assets, notably WooCommerce's `wordpress-plugin/delegat-bridge`. If a required OpenCart/Medusa server fix lives outside the listed repositories, stop that acceptance item and request an explicit scope amendment; do not invent a native-platform replacement.

## 2. Fixed technical shape

### Reuse and additions

Reuse `Operation`, `OperationLogEntry`, SignalR events, operation GET/Stop routes, existing HTTP log retention, import receipts/checkpoints, file storage and media grants. Do not migrate AI or unrelated bulk jobs to the new executor.

Allow **at most four new persistence models/tables**:

1. `IntegrationCommand`: authoritative command ID, logical operation ID, store/principal/connection/account binding, operation kind, request key/hash, immutable input/version references, command state and Stop flag.
2. `IntegrationStage`: command/item/stage key, fixed recipe version, prepared payload or immutable file references, dependency position, effect evidence/receipt, state, next eligible/check time and claim version.
3. `IntegrationAttempt`: persisted before physical dispatch; dispatch ID, command/stage keys, attempt identity, request hash, sanitized response metadata, effect certainty and outcome. One batch may reference several stage keys of the same command; no extra item/join/event tables.
4. `IntegrationScopeState`: versioned account/method/entity budget and ownership records, including lease/cooldown/reset state. Reserve related scopes atomically in short database transactions.

No credentials or media bytes in these records. Bind returned provider identifiers from receipts; never reevaluate product values to finish an old attempt. Terminal UI-history deletion must not erase command idempotency/effect evidence, and unresolved commands cannot be deleted through existing clear/delete routes. Retention uses the existing maintenance path: retain unresolved evidence; compact terminal payloads after 30 days while retaining minimal request-key/result tombstones. Authorized status reads can fall back to that tombstone without restoring deleted UI history; exact old response-body replay after compaction is not promised, but the request must never redispatch. Do not build an archive service.

`Operation` is a compatible UI projection, not a second execution authority. Command admission plus its initial operation projection commit atomically. Stage transitions and their projection updates commit before SignalR emission. `OperationLogger`'s best-effort catches must not decide whether a write is safe.

Use **one coordinator, one polling worker, one preparation service and one admission service** as the host-level responsibilities. Private helpers are allowed; additional independently scheduled workers/services or a second lifecycle are not. Exactly one instance configuration switch, `IntegrationExecutionV1`, defaults off. Pin the executor generation per accepted command.

### SDK boundary

Keep `IMarketplacePlugin` and existing positional result constructors intact. Add the execution companion and transport seam; do not extend the old interface with new required methods. The fixed companion shape is:

```csharp
public interface IMarketplaceExecutionProvider
{
    PreparedIntegrationItem Prepare(IntegrationPreparationInput input);
    Task<IntegrationStageOutcome> ExecuteStageAsync(
        PreparedIntegrationStage stage, IntegrationAttemptContext attempt, CancellationToken ct);
    Task<IntegrationStageOutcome> ReconcileStageAsync(
        PreparedIntegrationStage stage, IntegrationAttemptContext attempt, CancellationToken ct);
}
```

`Prepare` is pure: the input contains already acquired metadata, local/mapping versions, resolved identities and action support. It returns validated native payloads/stages or structured issues. Metadata retrieval is explicit admitted I/O before compilation. Readiness calls this same preparation logic. Provider-generated IDs are the only late-bound payload values, taken from validated receipts.

`IMarketplaceTransport` is the sole V1 outbound seam. Its send context identifies provider/account/route policy, cost, command/stages and semantic effect safety. Each effectful dispatch requires a committed attempt. A stage performs one effectful request; chunks are explicit stage instances, and polls return a next-check time rather than privately waiting/retrying for minutes. Initial batching stays inside one command.

Recipes are finite adapter code for existing operation kinds, not user-authored graphs. Reuse native DTOs/mappers where correct. Queries keep existing methods but must return complete data or a typed unavailable/incomplete result, not successful emptiness after a failed page. No third generic query framework is required.

Old plugins must still load with the new SDK. V1 admission requires the companion and supported pinned contract/recipe version. Compatibility of new plugins with older SDK hosts is not assumed; release the tested host/SDK/plugin combination together.

### State and replay rules

Stage outcomes are limited to `Ready`, `InFlight`, `Accepted`, `Applied`, `PartiallyApplied`, `Deferred`, `RejectedBeforeEffect`, `Unknown` and `CancelledBeforeEffect`. Accepted and Unknown are not successful completion. Preserve per-effect evidence for non-atomic item writes.

- Command and item identity are separate from product ID; duplicate requests/overlapping batches cannot share the wrong owner.
- Safe reads have bounded retries through common admission. Writes retry only with proven no-effect/idempotency safety; otherwise reconcile first. A 5xx or timeout alone is insufficient proof.
- A stale lease or missing saved response never means "not sent". Unknown effects block conflicting successor writes for that entity.
- Stop prevents new writes, not confirmation of already in-flight effects. No automatic rollback or replay of an accepted mutation.
- Required-stage failure/omission prevents aggregate success. Missing/duplicate/foreign bulk results are protocol failures, preserving known applied items.
- Separate observed state from pending intent, especially optimistic order snapshots. Verify provider-normalized intended fields/task completion, not moderation or storefront publication unless explicitly promised.

Keep global `Operation` statuses compatible. Add `integrationState` and stage/issue summaries to operation reads; waiting/unknown remains active with explicit detail, partial completion is not projected as completed success. Client finish/status endpoints cannot finalize coordinator-owned operations.

### Admission scope

One instance database coordinates all its workers and connection keys for the same seller. Account identity is not a token prefix. Until a reliable seller/policy-class binding is available, use a conservative shared provider scope or block for the missing prerequisite; do not create independent unlimited buckets. Admit auxiliary metadata/auth/media/broker/poll/confirmation requests too.

Implement only the audited policy dimensions: method/category/account/entity scopes, interval/burst/concurrency, minute/hour/day/reset timezone, request/item/byte cost, dynamic quota observations and cooldown. Policies are versioned code plus fixtures, not editable configuration language. Estimate/reserve a safe known upper bound when response cost is not known until completion; settle actual cost afterward.

Preserve vendor-specific units and semantic throttle codes. Ozon entity budgets, WB token/method/error costs, Yandex resource/420 rules and Avito ordinary-client admission are mandatory cases. Bridge limits stay explicitly unknown; use protective local serialization and observed server cooldown without claiming an official quota. Do not add throughput tuning or global coordination.

## 3. API compatibility boundary

Keep existing mutation routes; do not add a parallel command API or API v2. First-party callers generate one stable `Idempotency-Key` per user intent before submission and reuse it across transport retries. The same scoped key/body returns the same command; different body with the same key returns `409 integration_request_conflict` before effects.

`Prefer: respond-async` requires the key and returns `202` after durable admission. Default callers wait at most five seconds: return the existing successful DTO only after the promised effect is proved; return existing structured validation/rejection errors when there is a definite no-effect failure; otherwise return `202 { operationId, integrationState, statusUrl }`. Never map an internal fault to customer validation. Partial/unknown effects remain explicit in the receipt and operation detail, not a failed whole-command replay suggestion. Already asynchronous queue starters retain their existing acceptance contract with additive details; their acknowledgement is not final business success.

Legacy callers without a key may receive a server-generated command ID, but cannot be promised request-level deduplication after losing that response. Do not guess intent from body hash alone. Their unknown effects still prevent unsafe same-entity replay. Document this limitation rather than claiming exactly-once behavior.

Reuse cabinet `/operations/{operationId}/status` and Public API `PublicConnectionsController.GetOperation` for polling. Use the existing routing conventions; do not introduce another prefix. All reads enforce existing store/principal authorization. Recheck connection/account binding before dispatch without persisting credentials.

The additional `202` case is a visible compatibility change, **not** a promise of byte-identical behavior for old consumers. Update first-party callers, Public API documentation, Swagger/XML examples, error-code locales/tests and CHANGELOG in the same host PR before enabling V1. Keeping false `200 Success` for incomplete work is not an allowed compatibility strategy.

## 4. Eight work packages

Dependencies are sequential except the five adapter PRs within P4, which may run in parallel after the SDK/kernel/transport contract is frozen. Do not split packages into additional feature streams. Package gates cover their relevant catalogue subcases/layer; an A-ID is fully closed only after P8 verifies the required combined profiles and entry paths.

### P1 - Freeze contracts, harness and protective regressions

**PRs: host H1 + SDK S1.** Allowed areas: SDK contracts/base fallback/HTTP redaction; `backend.Tests/IntegrationExecution/`; existing lifecycle/export outcome checks.

Freeze typed preparation/outcome/transport records and the audited support-case index. Add a stateful in-process provider fake, injectable clock and dedicated test-database guard. Reuse current probe evidence as desired-behavior tests. Fix F1/F2/F3/F4/F10/F17 without reorganizing unrelated classes. Fail closed on unsafe ambiguous replay.

**Exit:** focused A02 ownership-collision, A05 false-success/result-coverage, A07 SDK cancellation and A09 premature-finalization regressions fail on the relevant original behavior and pass after fixes; old plugin/result compatibility fixtures load. No external provider call is possible in the default fixture path.

### P2 - Durable executor and authoritative ownership

**PR: host H2.** Allowed areas: four new models/configuration, EF-generated migration pair/snapshot, integration coordinator/worker, operation projection and guarded Stop/delete/finalization.

Implement atomic admission, keyed request deduplication, claims, attempt-before-send, continuation, entity ordering and Stop. Persist waits instead of holding a worker or transaction through provider I/O. Do not rewrite import persistence or general operation logging.

**Exit:** A02/A04-A09 pass against dedicated MySQL for transactional/concurrency behavior; failed admission sends no HTTP. V1 stays off.

### P3 - Shared transport and audited admission

**PR: host H3.** Allowed areas: host transport/admission composition, HTTP correlation and fixed policy profiles; no product/business recipe rewrite.

Wire the SDK transport to durable attempts and scope reservations. Implement the policy dimensions in section 2 using the current local rate evidence. Add all auxiliary request paths to policy accounting and redact bridge/auth headers.

**Exit:** A10/A11 pass with a virtual clock, concurrent claimers and restart. No shortened provider cooldown, unadmitted retry or duplicate transport policy layer. V1 stays off.

### P4 - Seven-adapter preparation and execution recipes

**PRs: host H4 for shared product flow plus Avito/Medusa; one PR each for Ozon, WB, OpenCart, WooCommerce and Yandex submodules.** Each adapter PR includes its complete existing-surface companion recipes and current contract fixes; no later per-feature adapter PR stream.

Extract preparation/finite stage helpers, route single/bulk through the same plan, preserve supported variants/warehouses/media/grouping, and replace private mutation retry/poll loops. Fix F11-F16 and native D1-D4 request/response/pagination/label drift in their owning clients. Ozon `total_items` already has compatibility handling: prove the consumers, do not replace working logic because a notice exists.

First validate OpenCart synchronous effects and Ozon asynchronous export, then apply the same frozen seam to the remaining profiles. Do not turn variants into universal parent stock or enable undeclared features. Record unknown bridge contract evidence as a blocker, not a native-platform conformance claim.

**Exit:** A01/A03-A06/A10-A13 pass for the advertised adapter profiles in fixtures; all seven build against the pinned SDK. Remaining host commerce entry wiring belongs to P5/P6. V1 stays off.

### P5 - Order actions, shipments, packing and labels

**PR: host H5.** Allowed areas: existing commerce controllers/services and order projections, using P4 recipes.

Replace request-owned direct mutations with coordinator calls. Preserve action/channel/state rules; separate optimistic order display from confirmation. Recover shipment creation, membership, packing and asynchronous labels independently without repeating accepted effects.

**Exit:** A04-A09/A12/A13 cover each supported action, including stale refresh after acceptance and membership failure after supply creation. Unsupported subactions are blocked before mutation.

### P6 - Promotions, groups and read/import completeness

**PR: host H6.** Allowed areas: promotion/group entry points, query quality/pagination and existing import continuation seams.

Use P4 recipes for add/remove/removeAll, coupon-specific semantics and group dependencies. Stop treating swallowed errors as empty complete lists. Preserve atomic local graph/receipt/media-queue behavior; repair traversal/continuation without replacing the importer or adding another checkpoint model.

**Exit:** A03-A09/A12/A13 cover async/partial membership, incomplete pages/cursor stalls and local rollback/retry. No unsupported bridge semantics are invented.

### P7 - Compatible API/UI wiring and cutover controls

**PR: host H7.** Allowed areas: existing cabinet/Public API facades, operation store/components, call sites, EN/RU issue codes and current API/changelog docs.

Implement section 3, preserve request keys across retries and render pending/partial/unknown accurately. Include scheduling, tool/agent execution and internal service entry paths that already invoke these operations; none may bypass the coordinator. Do not change their AI runtime or add a new operation dashboard.

Add the one instance switch and generation-aware admission/drain behavior. This PR does not activate a production instance.

**Exit:** A02/A06-A09/A14 pass. Caller disconnect does not cancel an admitted command; user Stop does. Existing permissions and non-integration operations remain compatible.

### P8 - Conformance, isolated acceptance and closure

**PR: host H8.** Allowed areas: acceptance fixtures/tests, locked submodule references, bounded release/rollback checklist and removal of covered legacy dispatch.

Build all seven exact adapter revisions; run the fixed acceptance catalogue. Verify native state in designated OpenCart/WooCommerce shops and documented WB sandbox scenarios. Keep provider-account evidence levels explicit; never substitute a mock for an unperformed live certification.

**Exit:** A01-A15 has recorded results, every advertised case has an implemented proof or an explicit blocked dependency, and no covered V1 route uses legacy mutation dispatch. A blocked required case means the program is not accepted, not that a ninth package is automatically created.

## 5. Fixed acceptance catalogue

These are requirements, **not results already obtained**. Parameterize by the frozen supported profiles; do not multiply scope into every possible category or unsupported action.

- **A01:** same declared normalized state for single, bulk and one-item batch tail; required card/variant/media/price/stock/group stages cannot disappear.
- **A02:** same request key/body yields one command; key/body conflict sends nothing; several operations on one connection retain independent ownership.
- **A03:** exact prepared payload validation blocks bad types/units/IDs/graphs and incomplete metadata before mutation; edits create a new input version.
- **A04:** accepted write plus lost response and crash-before-receipt recover without duplicate create; absent reconciliation capability remains explicitly unknown.
- **A05:** per-item/per-effect rejection and missing/duplicate/foreign bulk results prevent false aggregate success while retaining known effects.
- **A06:** restart during poll/cooldown resumes the missing stage; exhausted retry/blocked/unknown is not silently completed.
- **A07:** Stop before send/in flight/after partial application prohibits further writes without claiming remote rollback.
- **A08:** concurrent workers and cross-entry-point commands respect entity ordering; expired ownership cannot authorize replay of an unresolved write.
- **A09:** receipt/projection persistence failure, history clear/delete and client finish/status cannot lose effects or falsely finalize coordinator-owned work.
- **A10:** account/method/entity windows, burst/error cost, long cooldown/reset and dynamic quota units include auxiliary calls; two keys for one seller share applicable instance budgets.
- **A11:** every effectful V1 HTTP dispatch has durable attempt evidence; secrets are redacted and mutation errors are classified without localized-string heuristics.
- **A12:** actual request/response fixtures cover D1-D4, 200 semantic errors, changed/malformed JSON/HTML, 401/403/420/429/5xx and async completion. Additive optional fields remain compatible.
- **A13:** order/supply/promotion/group postconditions and query completeness differ correctly from intent/cached data; import graph/receipt/media queue survives rollback/retry.
- **A14:** 200/202/poll/Stop, request-key reuse, localized issues and store/permission isolation work through existing UI and Public API routes.
- **A15:** native isolated-shop effects, applicable WB sandbox results, version inventory and cutover/rollback rehearsal are recorded without touching configured marketplace accounts.

Product fixtures are fixed to: simple; dictionary-heavy; multi-variant; multiple images; update adding a supported variant; explicit supported warehouse assignment. Assert unsupported combinations as preflight refusal, not fabricated support. Existing restrictions on non-Yandex video export remain unchanged.

## 6. Verification commands and evidence

During implementation create test classes prefixed `IntegrationExecution` and the frontend `integrationExecution.test.js`. Per package run only its relevant filter plus existing touched regressions, for example:

```bash
dotnet test backend.Tests/Delegat.Tests.csproj --configuration Debug -maxcpucount:1 --filter "FullyQualifiedName~IntegrationExecution"
node --test frontend/src/lib/integrationExecution.test.js frontend/src/lib/operationsControl.test.js frontend/src/lib/operationLogText.test.js frontend/src/lib/i18nMessages.test.js
npm run check:docs
```

Do not run the broad backend gate after every patch. One final P8 gate explicitly builds plugins and tests current binaries:

```bash
dotnet test backend.Tests/Delegat.Tests.csproj --configuration Debug -p:BuildPlugins=true -maxcpucount:1
npm --prefix frontend run build
npm run check:docs
```

Adapter/SDK PRs additionally run their existing project-local narrow tests and newly added native-fixture cases. Dedicated MySQL concurrency and shop/sandbox checks require explicit isolated destinations; never point them at `.env` production settings. Record exact versions, A-ID results and commands; do not invent green evidence when access is missing. Stop processes started for verification.

## 7. Migration and rollback

Generate additive migrations only through EF CLI from the changed models, including Designer/snapshot. Do not hand-write migration SQL. Stage the tested SDK and adapter pins before enabling host admission; never auto-upgrade incompatible plugin binaries independently.

Before cutover, stop new legacy admissions and classify existing work: completed remains historical; provably unsent work may be readmitted with a captured snapshot; started work lacking reliable receipts is held for reconciliation/review. Do not mass-replay legacy rows or rewrite old import checkpoints, including the protected `-1` case.

Enable V1 only after P7 and the required P8 gates. Each accepted command stays with its pinned generation. Rollback disables new V1 admissions and permits safe confirmation/completion or explicit suspension of its existing commands; it never hands them back to the old worker. Do not drop tables or attempt automatic reversal of remote business effects.

## 8. Inputs that may block, not expand, the program

Required inputs are approved isolated test destinations, exact bridge versions/wire fixtures, supported provider-method evidence and a compatible binary inventory. Existing local platform documentation is not the missing bridge contract.

If an input is absent, record the affected A-ID/profile as blocked and request that input. Obtaining seller accounts, discovering undocumented endpoints, building a new shop ecosystem or modifying another repository is not implicit work. Offline code completion and authorized live/provider certification are different claims.

## 9. Anti-expansion and review rule

Before each package, list its allowed areas, A-IDs, planned PR IDs and expected exit artifact. At its exit, stop: no adjacent cleanup, speculative abstraction or "while here" feature.

A newly found defect is in scope only if it violates a listed A-ID for the frozen support surface or is a regression introduced by these changes. Attach the finding to that requirement, add the smallest meaningful regression and fix it in the owning package.

Review comments use the same test: a correctness/security/compatibility regression or a missed in-scope invariant is corrected, including follow-up commits/PRs after commit review. An improvement/new feature requested during review is not automatically a bugfix exception.

**Stop and obtain explicit scope approval** before adding a work package, another planned feature PR, a fifth persistence model, another scheduler/lifecycle/service boundary, a new infrastructure dependency, a new provider feature, another repository or a breaking API strategy outside section 3. Record the reason and changed gate before coding, never afterward.

When a gate cannot be met inside these boundaries, report it as blocked; do not weaken the gate, remove a working capability or silently grow the architecture. Passing this program does not assert that all possible future provider changes or categories are certified.

## 10. Definition of done

All eight packages and their 14 planned PRs are accounted for; review corrections are linked to their original requirement. A01-A15 and the frozen support cases have evidence, required blocked cases are resolved, exact binaries are pinned, no covered mutation bypasses preparation/attempt/admission/recovery, and API/UI results reflect proved effects.

Current canonical API/architecture/changelog references and the bounded operational checklist are updated. Mark this record Implemented only then. No extra "polish", "generalization" or second refactoring phase is needed to close the program.

## 11. Implementation & Verification Record (P1–P8)

> **Important honesty boundary:** Offline implementation, cross-PR Devin Review closure, and automated verification across P1–P8 are complete, and `IntegrationExecutionV1` remains **OFF by default**. This program is **not** marked live-certified until the explicitly blocked external gates (`DELEGAT_TEST_MYSQL`, isolated OpenCart/WooCommerce shops, and Wildberries developer sandbox) are executed against approved isolated destinations.

### 11.1 Merged PR & Binary Pin Inventory

| Component | Repository / Scope | Merged PR(s) | Pinned Commit SHA |
|---|---|---|---|
| Host P1–P2 (Kernel, 4 tables, coordinator, worker) | `akhilya/delegat-module` | `#105` | `3bdc4a3` |
| Host P3 (Scope admission, HTTP transport, route policy) | `akhilya/delegat-module` | `#107` | `19c8ab0` |
| Host P4 (Preparation service, dispatcher, Avito + Medusa) | `akhilya/delegat-module` | `#108`, `#109` | `54f0034`, `dfeeb95` |
| Host P5 (Commerce orders, shipments, packing, labels) | `akhilya/delegat-module` | `#110` | `a6bfb1e` |
| Host P6 (Promotions, groups, pagination/import hardening) | `akhilya/delegat-module` | `#111` | `66defc6` |
| Host P7 (Public API V1, bounded sync wait, UI/i18n) | `akhilya/delegat-module` | `#112` | `aace7fd` |
| Host P8 (Conformance gate, ledger, cutover checklist) | `akhilya/delegat-module` | `#113` | `07e44ad` |
| Host PR A (5 reviewed adapter pins, order/shipment review fixes, `IMediaBlobReader`, cabinet `OrderDetailSheet` 202 pack flow, `docker-compose.prod.yml` flag) | `akhilya/delegat-module` | `#114` | `40b3528` |
| Host PR B (Catalog/group/price/stock/scheduled/tool/background-queue V1 routing, async worker link projection, honest `stopRequested` UI, 7-adapter A01 conformance) | `akhilya/delegat-module` | `integration-reliability/catalog-group-ui-wiring` | HEAD |
| SDK (`IMarketplaceExecutionProvider`, transport contracts) | `akhilya/delegat-marketplace-sdk` | `#3`, `#4`, `#5` | `8a06d671aeccf8f1ffe031e4c1b3bba80d525762` |
| OpenCart adapter | `akhilya/delegat-adapter-opencart` | `#2`, `#3` | `56fba4440060b9f91f348aa98fbeec948d2876ef` |
| Ozon adapter | `akhilya/delegat-adapter-ozon` | `#6`, `#7` | `566a7067423cbf306d01629bf2a1cf6e08a3fb19` |
| Wildberries adapter | `akhilya/delegat-adapter-wildberries` | `#2`, `#3`, `#4` | `800484d6a2630e6044305a9e529394e65381813f` |
| WooCommerce adapter | `akhilya/delegat-adapter-woocommerce` | `#4`, `#5` | `3704b4331ad56bcfb5cd83a0cc029f93d25b884a` |
| Yandex Market adapter | `akhilya/delegat-adapter-yandex-market` | `#1`, `#2` | `98413790c45389028d94f2cf5d6badfc77f07c79` |
| Avito & Medusa adapters | Host-owned (`plugins/`) | `#108`, `#109`, `#110`, `#114`, Host PR B | Host `main` |

Architecture lock verified: **4 persistence models/tables** (`IntegrationCommand`, `IntegrationStage`, `IntegrationAttempt`, `IntegrationScopeState`), **1 coordinator**, **1 polling worker**, **1 preparation service**, **1 admission service**, and **1 configuration switch** (`IntegrationExecutionV1`, default `false`).

### 11.2 Full P8 Verification Gate Results

Executed against host `main` (`40b3528`) + `integration-reliability/catalog-group-ui-wiring` with all 7 plugins built from exact pinned SHAs:

1. `dotnet test backend.Tests/Delegat.Tests.csproj --configuration Debug -p:BuildPlugins=true -maxcpucount:1`
   - **Result:** `Passed! - Failed: 0, Passed: 4143, Skipped: 14, Total: 4157` (58 s).
   - **Skipped (explicitly blocked — no `DELEGAT_TEST_MYSQL` instance provisioned):** 2 `IntegrationExecution` MySQL tests (`EffectfulSend_CommitsFenceOnMySql`, `ConcurrentMySqlClaimers_CannotBothOwnSameScope`) + 12 `LocalMySqlCatalogTests`.
2. `npm --prefix frontend run build`
   - **Result:** `✓ built in 25.51s` (0 errors).
3. `node --test frontend/src/lib/integrationExecution.test.js frontend/src/lib/operationsControl.test.js frontend/src/lib/operationLogText.test.js frontend/src/lib/i18nMessages.test.js`
   - **Result:** `pass 27, fail 0` (4 suites).
4. `npm run check:docs`
   - **Result:** `Project documentation checks passed (45 indexed docs, 7 adapters).`

### 11.3 Acceptance Evidence Ledger (A01–A15)

| ID | Status | Evidence / Blocker |
|---|---|---|
| **A01** | Offline-proven | `IntegrationExecutionP4HostTests`, `IntegrationExecutionP4AdaptersTests`, `IntegrationExecutionP8CatalogRoutingTests` (`AllSevenAdapters_A01_SingleVsBulkAndOneItemTail_PrepareIdenticalStages` + `ProductSync_SingleAndBulk_V1RoutesThroughCoordinator_WithIdenticalPayloadsAndAsyncProjection` verify single vs. bulk and 1-item tail stage parity across all 7 adapters with 0 legacy calls under V1). |
| **A02** | Offline-proven | `IntegrationExecutionP2Tests`, `IntegrationExecutionP7Tests`, `IntegrationExecutionP8CatalogRoutingTests` (canonical JSON hashing, legacy raw-hash retry compatibility, `409 integration_request_conflict`, independent multi-operation ownership). |
| **A03** | Offline-proven | `IntegrationExecutionP4AdaptersTests`, `IntegrationExecutionP5Tests`, `IntegrationExecutionP6Tests`, `IntegrationExecutionP8CatalogRoutingTests` (pure `PrepareAsync` preflight refusal before mutation; canonicalized order metadata conflict check; add+remove promotion conflict refusal; operation-kind adapter gating via `CanExecute`). |
| **A04** | Offline-proven | `IntegrationExecutionP2Tests`, `IntegrationExecutionHttpTransportTests` (committed attempt fence before send, lost-response confirmation, absent reconciliation stays `unknown`). |
| **A05** | Offline-proven | `IntegrationExecutionP2Tests`, `IntegrationExecutionP4AdaptersTests`, `IntegrationExecutionP8CatalogRoutingTests` (Medusa non-object bulk result guard, Wildberries missing supply ID fails closed as `Unknown`, partial/unknown never projected as completed, rejected price/product items never overwrite confirmed links). |
| **A06** | Offline-proven | `IntegrationExecutionP2Tests`, `IntegrationExecutionDeferralAndSeamTests` (`DeferredBeforeSend` recovery after provider pause without consuming send budget; restart resumes missing stage). |
| **A07** | Offline-proven | `IntegrationExecutionP2Tests`, `IntegrationExecutionP7Tests`, `operationsControl.test.js` (`OperationsStop_AfterSendStarted_PreservesAuthoritativeOutcomeOnPublicPoll` + honest `stopRequested` UI handling so coordinator-owned stops keep operations active until authoritative `cancelled`/`partial`/`failed` broadcasts). |
| **A08** | Partially blocked | SQLite concurrency & per-entity ordering proven in `IntegrationExecutionP2Tests` / `IntegrationExecutionScopeAdmissionTests`; **MySQL row-lock concurrency (`ConcurrentMySqlClaimers_CannotBothOwnSameScope`) and MySQL fence (`EffectfulSend_CommitsFenceOnMySql`) are BLOCKED** pending `DELEGAT_TEST_MYSQL`. |
| **A09** | Offline-proven | `IntegrationExecutionP2Tests`, `IntegrationExecutionP7Tests`, `IntegrationExecutionP8CatalogRoutingTests` (atomic `ExecuteUpdateAsync` snapshot & confirmed link projections for `order_action`, `group_update`, `price_export`, `stock_export`, and `product_export`, guarded history delete, unresolved command protection). |
| **A10** | Offline-proven | `IntegrationExecutionScopeAdmissionTests`, `IntegrationExecutionP3Tests` (account/method/entity windows, dynamic quota, cooldown, shared seller budgets across keys, HTTPS-only bridge route policy). |
| **A11** | Offline-proven | `IntegrationExecutionHttpTransportTests`, `IntegrationExecutionP4AdaptersTests` (durable attempt evidence per effectful dispatch, secret redaction, structured error classification). |
| **A12** | Offline-proven | Adapter PRs (`OpenCart#2/#3`, `Ozon#6/#7`, `Wildberries#2/#3/#4`, `WooCommerce#4/#5`, `YandexMarket#1/#2`) + `IntegrationExecutionP4AdaptersTests` + `IntegrationExecutionP7Tests` (D1–D4 fixtures, Ozon D2 async labels, WB D3 `trbx_assign` refusal & supply ID evidence, Yandex `PARTIALLY_AUTO` pending until `MANUAL`, Woo stored-image `Media` stage & `removeAll` safety, OpenCart fail-closed effective SKU). |
| **A13** | Offline-proven | `IntegrationExecutionP5Tests`, `IntegrationExecutionP6Tests`, `IntegrationExecutionP7Tests`, `IntegrationExecutionP8CatalogRoutingTests` (confirmed order snapshot & catalog/group/price/stock link projections, buffered complete group pagination, V1 promotion clearing in `PlatformToolJobService`). |
| **A14** | Offline-proven | `IntegrationExecutionP7Tests`, `integrationExecution.test.js`, `operationsControl.test.js`, `i18nMessages.test.js`, `OrderDetailSheet.vue`, `productGroupEditPage.vue` (200/202/409/poll/Stop, `apiKeyId` attribution, EN/RU issue codes, store isolation, 202 cabinet pack & group export handling). |
| **A15** | **BLOCKED (external)** | Offline cutover/rollback rehearsal (`CallerDisconnect_DoesNotCancelAdmittedCommand_WhileUserStopAndRollbackDrainWorkCleanly`), `docker-compose.prod.yml` env forwarding, and exact binary pin inventory are complete; **native isolated-shop OpenCart/WooCommerce runs and Wildberries developer sandbox runs are BLOCKED** pending isolated test destinations and sandbox credentials (configured seller accounts and `.env` were never touched). |

### 11.4 Bounded Cutover & Rollback Operational Checklist

1. **Pre-cutover verification (before setting `IntegrationExecutionV1=true`):**
   - Run the blocked MySQL gate (`DELEGAT_TEST_MYSQL=... dotnet test backend.Tests/Delegat.Tests.csproj --filter "FullyQualifiedName~IntegrationExecution"`), isolated OpenCart/WooCommerce shop checks, and Wildberries developer sandbox checks.
   - Verify all EF Core migrations (including the 4 integration tables) are applied and all 7 plugin binaries match the exact pinned SHAs in §11.1.
   - Pause legacy write admissions, classify in-flight legacy operations (completed = historical; provably unsent = safe to readmit; ambiguous = hold for manual review — never mass-replay legacy rows or overwrite `-1` import checkpoints).
2. **Enable V1:**
   - Ensure `IntegrationExecutionV1=true` is passed into the API process/container environment (when running under Docker Compose, forward the env var and recreate the container rather than a plain restart).
   - Verify `GET /api/v1/stores/{storeId}/operations/{operationId}` and `GET /api/operations/{operationId}/status` return `integration` summaries and `Operations` panel Stop/polling works for a single canary write.
3. **Rollback procedure (if an incident occurs):**
   - Set `IntegrationExecutionV1=false` in the API process/container environment and recreate/restart the API process.
   - New V1 admissions are blocked immediately while `IntegrationExecutionWorker` continues draining/confirming any already-admitted V1 commands. Wait for active/unresolved V1 commands on a given entity to drain or resolve before submitting competing legacy writes for that entity (never hand V1 commands back to legacy workers, never drop the 4 integration tables, and never trigger automatic remote rollback).

