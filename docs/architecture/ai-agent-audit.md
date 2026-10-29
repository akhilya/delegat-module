# AI-agent quality and efficiency audit

**Snapshot:** 2026-10-28

**Scope:** prompt → tool selection → tool execution → conversation/memory context → evaluation and telemetry.
**Mode:** source/test audit plus safe live verification. No adapter exports/imports or plan executions were performed; unrelated working-tree changes were preserved.

## Working agreement

- Keep the objective on agent accuracy and operating cost; do not extend the work with tests or scaffolding without a concrete decision they validate.
- Work one bounded item at a time and report progress against the whole audit every time: completed first-pass scope, current item, and deferred backlog.
- Do not claim the full audit is complete while deferred items remain. Do not change the tool-round fuse or suppress calls without production evidence.

## Findings and status

### Fixed in the first pass

1. **Mapping/export instructions disagreed.** The field-mapping playbook both said not to export unless asked and instructed the assistant to export after a confirmed wiring plan. Tool descriptions also described `propose_plan` as unconditionally the first/only tool, while mapping tasks require a graph read and sometimes a required-value lookup first.
   - Policy now distinguishes known-input writes from writes requiring prerequisite reads: do only the explicitly required read-only lookup(s), then call `propose_plan` alone. No write runs before plan confirmation.
   - A mapping-only plan no longer implies export; export must have been explicitly requested and included in the confirmed plan.
   - Regression coverage: `AiPromptsChatTests.ChatSystem_MappingPlanAllowsOnlyRequiredReadsAndNeverAddsImplicitExport`.

2. **MCP names could collide.** Sanitization and truncation could map two remote tool names to the same model-visible name, after which dispatch selected the first matching remote tool.
   - Model-visible MCP names now include a stable hash of the original remote name; duplicate model-visible names are excluded from the listed set.
   - Added coverage for sanitation/truncation collisions and correct remote dispatch.

3. **MCP calls repeated discovery within a chat turn.** Listing tools created a session that was discarded; a later invocation handshook and ran `tools/list` again.
   - `McpHttpClient` now reuses the initialized session and remote-name map for the lifetime of that typed client/request. A direct mapped call without a prior list still performs discovery.
   - Added a test that verifies the listed session is reused and the second `tools/list` is omitted.

4. **Tool-result truncation could return malformed JSON.** A raw character slice could cut through a JSON value or remove an error field at the end.
   - Oversized results now become a valid JSON envelope containing a bounded preview and selected diagnostic fields (`error`, `status`, `code`, IDs, counts, etc.).
   - Added coverage for valid JSON and preservation of trailing failure fields.

5. **MCP input schemas lost JSON Schema keywords.** The hand-written DTO projection retained basic object properties and string enums but dropped constraints such as `additionalProperties`, nullable unions, numeric enums, and validation bounds before the provider saw the tool.
   - Keep the parsed schema as a cloned `JsonElement` and pass it through unchanged to both provider adapters; the older typed projection remains available to code that inspects basic fields.
   - Added tests for nested schema content, nullable types, non-string enums, validation constraints, and the built-in-tool DTO serialization fallback.

6. **Short continuations disabled tool calls.** `AsksToAnswerNow` treated standalone “и”, “and”, “ну”, and “дальше” as an instruction to stop using tools, which could prematurely terminate a multi-step request. The auto-continue nudge also still prohibited prerequisite reads before `propose_plan`.
   - Only explicit stop/answer requests now disable tools; continuation words leave the tool path available.
   - Auto-continue now permits only the minimum explicitly required read-only lookup(s), followed by a standalone plan call; writes remain confirmation-gated.
   - Added behavior tests proving continuation words do not activate the speak-only gate.

7. **There was no content-free turn cost telemetry.** Stream usage was discarded and the agent loop did not aggregate model/tool latency or call counts.
   - Provider-reported token usage now flows through the internal stream event (Anthropic; OpenAI-compatible usage when the endpoint emits it).
   - A per-turn structured log records provider/model, prompt-policy version, outcome, wall/model/tool time, model/tool rounds, tool counts/failures, input/output character estimates, repeated tool-call fingerprints, and reported token/cache counts. Fingerprints hash the tool name and whitespace-normalized JSON arguments and are held only for the turn; arguments/results and user/session identifiers are not logged. The rendered prompt is not hashed because it contains store/user-specific context; maintainers bump the non-sensitive version constant when prompt or tool policy changes.
   - Added tests for multi-round aggregation and cumulative usage updates.

8. **Conversation history grew without a budget.** Every client-supplied prior message was copied into each model round.
   - Context now keeps a contiguous suffix of whole user turns under a 32,000-character history target. It never slices a message, always preserves the newest turn (even if that turn alone exceeds the target), and keeps `tool_use`/`tool_result` exchanges attached to their turn.
   - If older turns were dropped, the system prompt tells the model that their contents are unavailable and must not be guessed. The turn telemetry records dropped turn/character counts and whether the active turn alone exceeded the target.
   - Added behavior tests for tool-exchange retention, current-request preservation, and no-op trimming.

9. **Durable memory was injected newest-first on every turn.** Up to 40 user facts were added to the system prompt regardless of the current request.
   - Chat now ranks the current and two preceding user messages against memory category/key/value, and only injects matching entries (up to eight and 2,500 characters). Generic/empty queries add no memory; the existing explicit `memory_search` tool remains available for deeper retrieval.
   - Search ignores common English/Russian stopwords, weights key/category matches above value matches, and uses recency only as a tie-breaker. The 40-entry database scope stays bounded to the current store/user.
   - Added regression tests proving a topical older memory outranks unrelated recent facts and that the prompt block stays within budget.

10. **The complete chat model/tool loop had no deterministic regression harness.** Helper tests covered individual gates and transformations, but not the request/result/request sequence.
    - Added an injectable `IAiApiClient` boundary and a provider-free scripted-stream fixture around the real `AiChatService` and tool host.
    - The harness now covers 23 complete chat-turn cases: read grounding, store isolation, prerequisite reads, known-input plans, deferred mixed batches, duplicate-plan handling, CRUD/import/export gates, MCP/Python rejection, single-use confirmed execution, tool failure, and repeated-call telemetry.

11. **MCP response bodies had no memory bound.** Tool discovery and calls buffered arbitrary response bodies before parsing.
    - MCP JSON and SSE responses, including HTTP error bodies, are now read with a 256,000-byte cap using response-header streaming. Oversized responses fail closed before parsing.
    - Regression coverage verifies an oversized discovery response is rejected.

12. **Read-only Agent access had MCP authorization gaps.** MCP discovery/resource reads did not resolve the agent session, and forged calls could reach memory writes or configured connected MCP tools from a `workspace.view` session.
    - Discovery and resource reads now require an authorized agent session; read-only tool lists omit Python and memory writes. The host blocks memory mutations for view-only users and permits connected MCP dispatch only from the cabinet-chat execution path with `ai.use`.
    - Regression tests cover denied metadata access, read-only tool-list filtering, memory-write denial, and connected-tool isolation.

## Security findings and residual risks

### Fixed — Chat confirmation gate covers Python and connected MCP

The chat loop now treats every `run_python` and connected MCP call as an external capability requiring a standalone `propose_plan`; mutating `platform_request` calls remain gated too. A batch containing a plan defers every sibling tool. The plan runs only through the existing signed, persisted, single-use confirmation flow. Direct calls are rejected before `AgentToolHost` dispatches them. Prompt/tool descriptions also require these plans and mark tool output, documents, uploaded content, catalog values, API responses, and MCP metadata/results as untrusted data. That security pass bumped the prompt-policy version to `chat-v3`; subsequent live-quality corrections are recorded below through `chat-v8`.

- **Evidence:** `AgentChatTurnGate.cs`, `AiChatService.cs`, `AgentToolCatalog.cs`, `AiPrompts.cs`; deterministic end-to-end cases cover direct Python/MCP rejection, mixed batches, deferred writes, and confirmed-plan execution.
- **Live status:** MCP and Python were not invoked. Confirmation enforcement is not treated as proof that those capabilities are safe; the sandbox residual below remains.

### Fixed — Read-only Agent sessions could reach protected MCP capabilities

MCP `tools/list`, `resources/list`, and `resources/read` now resolve store membership and require `ai.use` or `workspace.view`. View-only sessions do not receive Python or memory-write tools, and the execution host rejects memory writes and connected MCP calls without the required capability. Connected HTTP MCP tools are available only from the cabinet chat path; the operator MCP surface does not advertise or dispatch them.

- **Evidence:** `AgentMcpController.cs`, `AgentToolHost.cs`, `AgentSessionResolver.cs`; controller and host tests cover metadata authorization, view-only filtering, and forged connected-tool calls.
- **Status:** statically verified and regression-tested; no remote MCP server was invoked live.

### P1 — Python still receives credentials and has unrestricted outbound networking

`PythonSandboxClient` forwards a short-lived user JWT and shop descriptors to the sandbox. Legacy descriptors still include decrypted Marketplace credentials and hosts, and arbitrary Python can make outbound requests. Selected broker-mode shops remove Marketplace credentials and route supported reads through the server-side broker, but the rollout is opt-in and other entries remain `access=legacy`. The in-chat confirmation gate reduces unapproved invocation; it does not constrain what confirmed code can do, and a misleading plan description or malicious task data could still induce a user to approve dangerous code.

- **Impact:** code run with user approval can use the forwarded JWT for Agent HTTP or legacy shop secrets for direct external requests; container CPU/memory limits do not restrict network destinations.
- **Evidence:** `PythonSandboxClient.cs`, `SandboxShops.cs`, `sandbox/python/delegat_sdk.py`, and `docker-compose.prod.yml`. No live Python execution was performed.
- **Recommendation:** remove the broad JWT and legacy secrets from general-purpose Python; expose narrowly scoped brokered reads instead and enforce network egress restrictions. Keep broker rollout off until that store's fixtures pass.

### P2 — Connected MCP relies on operator trust and plan text, not per-tool authorization

Connected MCP tools are configured by an operator with `settings.platform`; enabled tools are offered to cabinet users with `ai.use`. The host cannot distinguish remote read from write capabilities, so it requires confirmation for every call, but does not apply resource-specific permissions to the remote tool. Confirmation descriptions are model-authored text and are not a server-validated capability contract.

- **Impact:** any `ai.use` user can ask to run an enabled remote capability and may approve a plan whose visible description does not fully explain the remote effect.
- **Recommendation:** keep connected servers restricted to trusted operators/tools. Consider a server-side tool allowlist or capability metadata and stronger plan rendering before enabling write-capable servers for broad user roles.

### P2 — AI and agent endpoints are exempt from the global rate limiter

`Program.cs` returns `GetNoLimiter` for `/api/ai`, `/api/agent`, `/api/mcp`, and `/mcp`. No per-route chat/MCP limit was observed in these controllers (the Marketplace broker has its own policy). The checked-in nginx configs define no rate-limit zone/rule, and `docker-compose.prod.yml` publishes API port 5000 as well as routing through the frontend. The app also clears `KnownProxies` and `KnownNetworks` while trusting `X-Forwarded-For`; with the API port directly reachable, a caller may spoof the client IP used by IP-partitioned policies. Therefore the repository itself provides no compensating upstream limit on AI paths, and IP limits should not be treated as a robust fallback until proxy trust/direct reachability are verified. A deployment firewall, load balancer, or provider quota may still add protection, but those external controls were not inspectable here.

- **Status:** static operational risk, not load-tested. Verify deployment firewall/gateway/provider protections, whether port 5000 is externally reachable, and trusted-proxy configuration; review production usage before selecting limits. Do not alter the 40-round tool-loop fuse as a substitute.

## Safe live verification (2026-10-28)

- Targeted only the local development instance at `127.0.0.1:5000`. Signed in with the existing admin account from `.env.dev`; verified `isAdmin`, selected store, and `ai.use`. No user was created. There was one pre-existing admin conversation; four new test sessions were persisted to that same account (the context session contains two turns).
- **Grounded read:** the cabinet's read-only product-list endpoint reported 61 products. The assistant used `sql_readonly` and answered 61. The answer was grounded against the independent endpoint result.
- **Multi-turn context:** the assistant returned the exact sentinel `LIMON-4827` on the second turn; no tools were called and both turns were present in the saved history.
- **Direct prompt injection:** a request to reveal the hidden prompt and credentials received a refusal; no tools were called and no credential-shaped output was detected. This checks direct user-text injection only. The new untrusted-data policy has prompt-level regression checks but was not exercised against live malicious documents/catalog data.
- **Unconfirmed plan:** the assistant presented a pending `platform_request` plan for one existing product. The plan was not confirmed and `/api/ai/execute-plan` was never called; a follow-up GET confirmed the product name was unchanged. The plan contained no export step.
- No adapter export/import, MCP tool, or Python sandbox call was made. The direct chat bypass is now blocked by code, but MCP/Python behavior, sandbox credentials, and network isolation remain unverified dynamically and are not considered safe to probe on this instance.

### Additional live agent-quality chats (2026-10-28)

The same existing admin and store were used. Six new conversations were created and verified in the conversation list (11 conversations total); all were preserved and titled for discovery. One is explicitly marked invalid because the test harness sent a literal `$code` placeholder instead of its intended random value; it is not scored.

- **Grounded total:** the assistant used a read-only catalog query and returned **61**, matching the independent product-list endpoint. It also volunteered catalog capacity and other context despite being asked for the number, so the factual result passed but answer-shape compliance did not.
- **Products without price:** the assistant returned **0**, matching an independent read-only `GET /products?where=price IS NULL`; it used a read-only catalog query and answered in the requested format.
- **Negative SKU lookup:** a unique synthetic SKU had zero results in an independent product search. The assistant used catalog search and said there was no exact match; it did not invent a product.
- **Same-chat recall:** in a fresh chat, a random synthetic code was supplied without tools or persistent memory. On the second turn, the assistant repeated it exactly; both turns were persisted.
- **Cross-chat boundary:** in a fresh chat, the assistant correctly said it could not retrieve another chat's transcript, but it still called `memory_search` despite an explicit request not to search. The tool description at the time instructed the model to use memory search for facts from another chat, although that tool searches saved memories, not transcripts. This is a confirmed instruction/tool-description defect.
- No write, plan execution, Python, or connected MCP call occurred in this batch. The conversations are test artifacts, not scripted-eval evidence.

The confirmed defects drove a source change to prompt policy `chat-v4`: follow single-value output formats exactly and clarify that saved-memory search is not transcript search and must respect an explicit no-search request. After the local `dotnet watch` process rebuilt/restarted the API, two more fresh chats tested this policy:

- **Scalar-only answer:** `AI audit - prompt-v4 scalar-only response` returned exactly `61`, matching the independent product count, with no tool call.
- **No transcript search:** `AI audit - prompt-v4 no transcript memory search` said it could not know another chat's code and made no tool call, honoring the explicit no-search instruction.

Both conversations persisted and appeared in the recent conversation list; at that checkpoint the account had 13 conversations. These are live spot checks, not repeated-run evidence of model consistency.

### Further live accuracy checks and prompt iterations (2026-10-28)

All scenarios used the same local test instance, existing admin, and store 1. Each scenario had its own saved conversation; no user was created.

- **Existing-content injection scan:** independently read all 61 product detail DTOs (descriptions, notes, attributes, and returned metadata) and checked for common English/Russian instruction-injection phrases. No matching fixture was found. No product, file, or other catalog content was changed; this is not evidence that unseen file contents or all injection forms are safe.
- **Exact SKU grounding:** SKU `FI10` resolved independently through the product endpoint to the same product name and price **1500.00** that the assistant returned. The v8 chat used `search_catalog` with the exact non-empty SKU and `sql_readonly`; it did not navigate or propose a write. Session: `09bb6c0b-7a17-4df5-afda-26c900820438`. Earlier checks of the same item also matched: `3b5a805a-6c45-449d-bfa5-b3248ce299b4` and `7b121af1-b34e-43f8-9e76-df422cd38763`.
- **Missing product / ambiguity:** pre-fix chats `6fceaf4d-c280-43ac-bae2-6a73307222e6` and `c666fd65-fcb3-47a3-98c6-bacbe1fca768` correctly asked which item, but also read broad product lists and added navigation/edit links. Prompt-v5 reduced this to a clarification form (`bc89f514-4d8f-42a9-9e99-1d0ba6d9b1f9`). Prompt-v7 regressed: it called `search_catalog` with no query and listed ten candidates (`d2fa1c93-6e63-49f3-a8af-6ad4e75094e3`). Prompt-v8 added the empty-query rule to both the system prompt and tool description. Its fresh chat used only a one-field `ask_form`, did not query/list the catalog, navigate, or guess a price, and asked for the missing identifier (`1c2ade40-1708-4f2f-b9ee-4169bebdd6f7`). It still repeated the clarification in surrounding prose; this is a minor response-shape issue to monitor, not a factual error.
- **Freshness and scoring:** two earlier chats (`730a2cc5-8d4f-471f-872c-92c6b11a068c`, `e45974bc-843c-479c-8b6d-c40610c637bb`) were marked pre-restart and are excluded because the loaded assembly could not be established. Subsequent v6, v7, and v8 turns were confirmed by content-free telemetry as using the named prompt version. The v8 exact-SKU and ambiguity chats both persisted in history. The account's recent-conversation list contains 27 conversations at this checkpoint.
- No plan was executed, and no Python sandbox, connected MCP, import, or export was invoked in this batch. The two observed catalog-list reads were GET/read-only; the edit links were suggestions only and were not opened.

### Prompt-v8 repeat and set-completeness checks (2026-10-28)

The active API telemetry confirmed `chat-v8` for this batch. Three additional conversations were created under the same existing admin and saved to the conversation list:

- **Ambiguous-price repeat:** `55edddfc-08cb-49be-a726-e1bf1b48806b` again asked for the missing product via a one-field form. It did not call catalog search, SQL, cabinet HTTP, or navigation and did not guess a price. The text still redundantly asks for the identifier around the form.
- **Near-match SKU:** the independent exact search returned 0 for `FI1O` while `FI10` returned 1. In `fbe005ec-1859-4a20-928f-48ce72d1efae`, the assistant queried the exact `FI1O`, got zero rows from both read-only SQL and catalog search, said no exact SKU exists, and did not substitute or mention `FI10`.
- **Set completeness:** independent `GET /products?where=price < 1600&pageSize=100` returned 16 products. In `bc1f8495-d3d7-4992-98b4-a6e918948e2b`, the assistant used `sql_readonly` and returned all 16 SKUs, no extra catalog SKU, and the matching price for each. No plan or write was involved.

All three chats have two persisted messages and are visible in the account's history; the account now has 30 conversations. No Python, MCP, import, or export call was made.

### Questionnaire reply investigation (2026-10-28)

The saved chat `AI audit - v8 ambiguous price repeat` (`55edddfc-08cb-49be-a726-e1bf1b48806b`) contains two form submissions. Both were persisted with `formReply.answers: {}`; the follow-ups were `Price for ` and an empty second reply. The assistant therefore received no product identifier and repeated its clarification. This is a client-side answer-collection failure, not a model/tool or conversation-persistence failure.

The questionnaire text input conditionally added its `name` and form association based on reactive selection state, while the submit handler immediately built `FormData`. The input now remains named and associated with the questionnaire form; empty answers are still filtered and skipped questions remain excluded. A regression test covers the stable field name and answer extraction. Local regression checks ran before the end-to-end live check below.

### Prompt-v9 live checks (2026-10-28)

The local audit API was restarted from the existing dev launcher; content-free telemetry confirmed `chat-v9` before scoring these turns. The same existing admin and store 1 were used.

- **Semantic-name price lookup:** independent `GET /products?searchQuery=Rambo` returned one item, `FI10` at **1500.00**. In `AI audit - v9 semantic product-name lookup Rambo` (`045f304a-6975-4024-9c37-371e1d060f95`), the assistant returned the SKU and price using `search_catalog` followed by `sql_readonly`. No plan, write, or navigation occurred.
- **Questionnaire round trip:** in `AI audit - v9 questionnaire reply FI10` (`6960fe7d-a272-4165-935f-e36861fc9dcb`), a browser test opened the saved `ask_form`, entered `FI10`, and submitted it. The input was named `identifier` and belonged to the questionnaire form. The saved follow-up contains `answers.identifier = FI10`; the next `/api/ai/chat` request included that value, and the assistant returned **1500.00**. Independent exact-SKU search returned one `FI10` product at the same price. The persisted trace contains `ask_form`, then `search_catalog` and `sql_readonly`; the four-message conversation is visible in history.
- **Plain-text clarification:** `AI audit - v9 ambiguous price text clarification` (`393aa1ed-832a-40f3-9543-4de6bee91fd1`) asked the missing-product question in plain text, which the assistant answered without tools. It is not scored as a form interaction.

No plan was executed, and no Python, connected MCP, import, export, navigation, or catalog write was invoked in these checks.

### Anthropic history rejection (2026-10-28)

The later follow-up in `AI audit - v9 questionnaire reply FI10` (`6960fe7d-a272-4165-935f-e36861fc9dcb`) was rejected before any tool ran. The provider response identified an assistant `tool_use` in `messages.5.1` whose matching `tool_result` was not in the immediately following message; telemetry recorded one model round and zero tool rounds. The frontend had merged assistant content across successive model rounds, then serialized all tool results after that combined content. The history builder now starts a new assistant block after each result and emits each tool-result group before the next assistant round. A regression test covers a two-round search/read trace. The existing conversation was not retried or otherwise modified.

After the fix, `AI audit - v9 Anthropic history follow-up` (`82f8df13-5c1f-44c2-b8ef-653b91bb2fb7`) exercised the same path in one browser session: `ask_form` → `search_catalog` → `sql_readonly` → a new follow-up `ask_form`. The independent exact-SKU endpoint still returned `FI10` at **1500.00**; the assistant returned that price, the third request's tool-use IDs each had a matching result in the immediately following user message, and the provider completed the turn. No write, plan, or non-read tool ran. This probe's persisted follow-up text contains `FI10` but its stored row did not retain `_ui=formReply` metadata. Source inspection traced this to the equal-count history-sync branch, which did not update an already-inserted matching user row; it now syncs metadata only when role and content match, covered by `AiConversationHistorySyncTests.SaveMessages_UpdatesUserMetadata_WhenLiveTurnAlreadyInsertedMatchingText`. The existing probe row was not retroactively modified. Two other saved sessions (`7e5d29a2-ea8b-4d82-ae2c-682841edb295`, `f2218c17-ec25-4f63-83fc-340fcdc47bc7`) are explicitly marked incomplete: the browser harness stopped before form submission on a field-name assumption and an ambiguous button selector, respectively.

### Follow-up audit checkpoint (2026-10-28)

- The local `chat-v9` API log contains **13** turn telemetry records: 12 completed and 1 provider error. Across this sample, 11 turns reported cache-read usage (**219,904** cache-read input tokens; **0** cache-creation tokens), and one repeated model tool call was recorded. No turn exceeded four model rounds or trimmed history. These are local test-instance observations, not production distributions or billing evidence; do not change the fuse, add duplicate suppression, or enable the explicit Anthropic cache pilot from this sample.
- The same log contains **two** `POST /api/ai/conversations/messages` responses with HTTP 500 and `DbUpdateConcurrencyException`; other history-sync requests returned 200. A deterministic in-memory concurrency test reproduced the failure: a stale client snapshot deleted an assistant row while live persistence updated its `toolCallsJson` concurrency token. History sync is now additive, reconciles the unpersisted client tail with server-inserted messages, and rebases concurrent updates on the latest row while preserving server-owned plan metadata. Regressions cover the delete/update race, concurrent plan changes, form metadata after known history, and live-turn persistence. A new authenticated API-level form-reply check after the API restart is recorded below; browser-level form interaction remains unverified.
- Offline checks cover Unicode lexical matching with a synthetic Russian memory fixture; this demonstrates Cyrillic tokenization and a common suffix variation, not translation or general multilingual semantic recall. No embedding/indexing change is justified by this check.
- The local Debug API held `backend/bin` outputs open, and `dotnet watch` could not apply its Hot Reload update. Building into the separate `Audit` configuration avoided disturbing that process; the focused history/live-turn/plan tests passed **26/26**, including deterministic concurrency races and mixed-history form metadata. The expanded AI/Agent/MCP/SandboxShops suite passed **745/745** after those regressions. After the locked child process and stalled watcher were restarted with the existing dev launcher, the current Debug API became healthy; existing MySQL and MinIO containers were started without recreation or volume changes. The expanded suite passed **745/745** again against the resulting source state.
- Existing live chats demonstrate that the configured Anthropic-compatible `deepseek-flash` endpoint accepted the built-in read-only tool schemas and returned tool calls. The MCP mapper preserves representative JSON Schema in unit tests, but there is no provider-by-provider MCP schema smoke result. No provider call was made in this checkpoint.
- The 61-product injection scan found no suitable existing malicious-content fixture; no content was seeded or changed. This session exposes no browser-control or MCP resource. Authenticated API-level live checks were possible, but browser rendering/input collection remains unverified. The local `/api/health` endpoint returned 200.
- Sentry is selected for optional AI performance traces. The implementation uses the core SDK for manual turn/model/tool spans and aggregate measurements only; it has no ASP.NET request instrumentation, error-event capture, or raw prompt/catalog capture. Export remains off until `SENTRY_DSN` is configured; the default trace sample rate is 10%. No DSN was supplied for this test run, so external trace delivery and dashboard percentiles remain unverified.

### Continued live checks after local API recovery (2026-10-28)

The local `dotnet watch` child had held `backend/bin/Debug/net8.0/Delegat.exe`, and the watcher remained stalled after the failed rebuild. The exact old Debug child and its backend watcher were restarted with the existing `.env.dev` launcher; the existing `delegat-mysql` and `delegat-minio` containers were started without recreation or volume changes. The API was rebuilt from current source, `/api/health` returned **200**, and the expanded AI/Agent/MCP/SandboxShops selection passed **745/745**. Browser-control and MCP resources remain unavailable in this session, so these checks used authenticated HTTP as the existing Owner in store 1. All four sessions below were verified in that account's conversation list.

- **Form reply persistence:** `77733f11-8c09-441b-9c0c-5b25b748f70b` asked a single `ask_form` question for an exact SKU. The follow-up was saved with `_ui=formReply`, the matching form ID, and `answers.sku = FI10`; a subsequent read after the assistant completed confirmed the metadata remained present. The assistant used `sql_readonly`, returned the matching product name and **1500.00**, and agreed with independent exact-SKU `GET /products?searchQuery=FI10`. This validates API persistence, not browser input collection/rendering.
- **Set completeness with another filter:** independent `GET /products?where=price < 1600&pageSize=100` returned **16** products. In `09f6f755-8374-44d8-93c1-e043519a6764`, one read-only `sql_readonly` SELECT returned every expected SKU, no extra catalog SKU, and no plan.
- **Duplicate intent:** `a94de2b9-35b9-4de4-8126-93f6ee29a842` requested the same exact `FI10` price lookup twice. `search_catalog` plus `sql_readonly` grounded the answer at **1500.00**; no identical repeated read-call arguments were observed. This is one spot check, not evidence for changing duplicate suppression.
- **Unknown SKU:** independent exact search returned **0** for `AUDIT-NO-SUCH-SKU-20261028`. In `cc75ee8b-852e-4461-ac15-b56f7cf72511`, `search_catalog` and `sql_readonly` reported no exact match, mentioned no existing SKU, and invented no price.

No plan was proposed or executed, and no write, Python, connected MCP, import, or export tool ran in these four checks. They are live spot checks, not repeated-run consistency or production evidence.

### Remaining work, in recommended order

1. **Finish browser-only form validation.** The current API-level check verified that a fresh ask-form reply's `_ui=formReply`, form ID, and `answers.sku = FI10` survive in the saved conversation after the assistant follow-up; set-completeness, duplicate-intent, and unknown-SKU checks also completed against independent read-only product endpoints. When browser-control is available, verify actual input collection and rendering in the UI. Do not call live Python/MCP or execute plans.
2. **Continue safe prompt-injection evaluation.** The existing 61 product DTOs had no common instruction-like fixture. Inspect only already-existing retrieved content if an appropriate fixture is found; do not seed or change catalog content. The provider-free harness's 23 cases verify tool traces, not natural-language quality.
3. **Verify Sentry in deployment.** Configure a deployment DSN and confirm a cost-appropriate sample rate, then verify AI parent/child traces and Sentry latency percentiles in the target project. Keep payload capture disabled. Anthropic and compatible OpenAI endpoints may report streamed usage; the resolved OpenAI .NET SDK 2.14 has no typed `StreamOptions` property, and the app accepts arbitrary OpenAI-compatible URLs, so don't force `stream_options.include_usage` globally. Add it only behind an explicit endpoint capability if exact usage is needed.
4. **Improve history context selection.** The 32,000-character cap is configurable via `AI_CHAT_HISTORY_MAX_CHARACTERS`; it remains character-based and does not summarize dropped turns. A true token budget needs a tokenizer compatible with the selected model/provider. Memory retrieval is relevance-ranked and bounded but lexical; validate multilingual recall before adding embedding/indexing cost.
5. **Verify MCP schema compatibility per provider.** The source JSON Schema is preserved, but Anthropic and OpenAI-compatible endpoints accept different schema subsets. Exercise representative schemas against supported providers and report a clear tool-registration error if a provider rejects a schema; avoid silently reducing it.
6. **Control tool-loop cost.** Calls in a batch are sequential; the fuse remains 40 rounds. Observe production distributions before changing it or adding duplicate suppression; parallelize only independent reads. Do not choose limits or suppress calls without production evidence.
7. **Pilot prompt caching.** Keep the two-part Anthropic prompt-cache opt-in disabled until telemetry shows same-turn read hits and provider billing supports enabling it by default. The cached prefix includes dynamic store context; cross-turn hits are not guaranteed. OpenAI caching remains automatic where supported.

## Tooling candidates

- [Promptfoo](https://www.promptfoo.dev/docs/intro): first choice for offline prompt/model regressions. Use a custom provider/adapter that invokes the full .NET agent loop and returns tool-trace metadata; testing only the rendered system prompt misses the most important behavior.
- [OpenTelemetry GenAI semantic conventions](https://github.com/open-telemetry/semantic-conventions-genai): portable naming for model, agent, tool, and MCP telemetry. Pin the convention version because this area is evolving.
- [Langfuse](https://langfuse.com/docs/observability/overview): optional UI for trace inspection, human review, datasets, and experiments; consider self-hosting and disable raw-content capture by default.

## Validation

An earlier full .NET run in the separate `Audit` configuration passed **2,835/2,838**; the three failures were two error-code expectation mismatches (`store_attribute_key_used` vs `store_attribute_key_taken`, and `category_has_products` vs `category_not_empty`) plus the `Preview_FromRemote_ScansCatalogOnceInsteadOfRepeatingSkuLookups` CatalogMatch failure. They were not AI-agent tests. The latest expanded AI/Agent/MCP/SandboxShops .NET selection passed **745/745**, including telemetry and history-sync regressions; `npm run check:docs` passed (37 indexed docs, 7 adapters). Python sandbox tests last passed **13/13**; frontend unit tests now pass **491/491**, and the frontend production build passed. The local `/api/health` returned 200 after each API restart. Passing scripted tests do not establish live semantic consistency or live MCP/Python safety.
