# Adapter HTTP logs: retention, performance, and recovery

This reference covers outbound marketplace adapter diagnostics, not Public API request logs or operation history. The implementation is in [`AdapterHttpLogService`](../../backend/Services/Marketplace/AdapterHttpLogService.cs), [`AdapterHttpLogCleanupService`](../../backend/Services/Marketplace/AdapterHttpLogCleanupService.cs), and the [cabinet log page](../../frontend/src/pages/marketplace/adapterLogsPage.vue).

## Current storage policy

- Summary retention: `ADAPTER_HTTP_LOG_RETENTION_DAYS`, default 7 days, clamped to 1–30. The instance-wide summary safety cap is 200,000 rows.
- Body/header retention: `ADAPTER_HTTP_LOG_DETAIL_RETENTION_HOURS`, default 24 hours, clamped to 1–168 and never longer than summary retention. Cleanup also keeps details on no more than the newest 5,000 records across the instance, not per connection.
- New request and response bodies are each capped at 64 Ki characters, plus a truncation marker when needed. Each header block is capped at 16 Ki characters. These are character limits, not byte quotas.
- Detail previews are capped at 16 Ki characters per body in the SQL projection. Downloads select only the requested body. Expired details are not returned even if physical cleanup has not caught up.
- Cleanup starts two minutes after startup and runs every ten minutes, including when new adapter logging is disabled. It uses 500-row batches and bounded work per run; a large backlog can require multiple runs. Age-based deletion and summary-cap deletion each remove at most 100,000 rows per run; age-based detail compaction and detail-cap compaction each affect at most 100,000 rows per run.
- Detail compaction advances a `(createdAt, Id)` cursor across batches so it does not repeatedly rescan the summaries it just cleared. Timestamp ties do not skip rows.
- Compaction clears bodies and headers, keeping timestamps, method, URL/path, adapter, connection, status, timing, error summary, and operation ID. Searches filter the URL/path, not the request/response body. Missing retained details are explicitly indicated in the cabinet; their downloads return `404 log_details_unavailable`.

The production Compose configuration forwards both retention variables. Changing `.env` requires recreating the API container to apply its environment; a plain process restart does not update a container's environment. New defaults take effect when the updated API is deployed. No schema migration or marketplace import/export is needed for this change.

## Root cause review — 2026-11-07

The findings below are verified against source/history and deterministic tests. They are not a measurement of a particular deployed database: no customer database, adapter credentials, slow-query log, or production query plan was accessed during this investigation.

### Why diagnostic storage grew

Commit `52557cb` increased the host's per-body storage allowance from 64 Ki to 2 Mi characters to support downloadable bodies. The seven-day retention and 200,000-row safety cap did not distinguish small request summaries from large payloads. Consequently, a row-count cap alone permitted a very large payload footprint, including successful high-volume marketplace exchanges. The SDK's larger capture allowance was also used directly as the host persistence limit rather than being treated as an upper bound the host could reduce.

The original cleanup deleted entire records by age/row count, hourly. It could not discard expensive bodies early while retaining inexpensive searchable history. Commit `9aa84bd` reduced cleanup batches from 5,000 to 500 rows; this limited individual delete work but did not change payload size, payload lifetime, or the amount of data allowed to accumulate. Also, the original production Compose file did not forward `ADAPTER_HTTP_LOG_RETENTION_DAYS`, so setting it only in the example `.env` did not configure that container.

### Why opening records did excessive work

The detail reader loaded the full entity, including both large bodies, before shortening them in application memory. A small browser preview therefore still incurred full database-to-backend payload transfer and allocation. The body download reader also selected both bodies and discarded the unrequested one afterwards. The correction moves preview truncation into SQL and narrows download projection.

List responses already projected summary fields; they did not send full bodies to the browser. Payload bloat can add database/storage pressure, but the exact contribution to live search latency requires an authorized instance measurement. The search uses substring matching on URL/path and exact filtered counts; those operations are not made constant-time merely by retaining less payload data.

### Why search and pagination could look broken independently of storage size

The cabinet keeps a cumulative `items` array, but `hasMore` compared `offset + items.length` against the total. After appending pages this counted part of the result twice, hiding the load-more action before all rows had been loaded. The corrected condition compares cumulative item count with the total.

Each Vuex action created a fresh `useAxios` instance with `abortPrevious: true`. That option applies within one instance, not across separate instances, so it did not cancel the preceding cabinet request. A delayed old search could overwrite a newer result, clear its loading state, or show an irrelevant error. Detail-sheet requests had the same race. The correction passes an explicit caller-owned `AbortSignal` directly to Axios and checks request ownership before changing results/loading state. Unmount cancels pending requests; repeated load-more clicks cannot append the same page concurrently.

### Why the earlier tests did not catch this

The existing tests covered redaction, the presence of truncation, and deletion of old rows in small batches. They did not enforce separate detail retention, a detailed-record cap, server-side preview projection, cumulative multi-page loading, or out-of-order request completion. Those gaps allowed individually bounded records and apparently enabled cancellation to look safe without testing their aggregate behavior.

Regression coverage now includes detail compaction while preserving search results, recent-detail caps with timestamp ties, expired downloads, store scoping, SQL preview generation, multi-page loading, explicit signal forwarding, stale list/detail responses, cancellation, and unmount cleanup. Run:

```powershell
dotnet test backend.Tests/Delegat.Tests.csproj --filter "FullyQualifiedName~AdapterHttpLog"
npm --prefix frontend run test:adapter-logs
npm --prefix frontend run test:unit
npm run check:docs
```

If a running local API locks the ordinary .NET output directory, use a separate `--artifacts-path` under the repository's ignored `backend.Tests/bin/` tree rather than stopping an unrelated process.

## Deploying and verifying safely

1. Deploy the updated API and frontend. Confirm the two retention environment values in the API container without exposing other environment variables or credentials.
2. Wait for the first scheduled cleanup and inspect the application's cleanup messages for deleted summaries and cleared payloads. A backlog must decrease over successive runs; a cleanup exception is not evidence that retention has been enforced.
3. On the user's designated test instance, verify filtered list results and counts, load through more than two pages, submit two searches in quick succession, switch between two detail records, and open a summary whose payload has expired. Do not export to a configured marketplace as a log-load test.
4. If slowness remains, collect authorized timings and MySQL query plans for the filtered count and list queries separately. Check remaining payload-bearing row count, retained rows per store, cleanup failures, and database I/O before choosing another index or changing the history contract. No live speed improvement is claimed solely from passing unit tests.

## Already enlarged MySQL files

Clearing payload columns or deleting rows makes space reusable inside InnoDB; it does not necessarily reduce the tablespace file on disk. Do not automatically run `OPTIMIZE TABLE`, rebuild a table, or delete database volume files. Physical reclamation is a separate operator-approved maintenance task requiring a verified backup, a lock/downtime assessment, and enough temporary disk space for a rebuild. This change neither performs that operation nor guarantees immediate filesystem reclamation.
