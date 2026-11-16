# Assistant dialog defect analysis — generated-file export session (2026-11-16)

Source material: a stored `aiConversationMessages` transcript (message `dbId`s 34–40) of a real cabinet-chat session, audited against `backend/Services/Ai/`, `backend/Controllers/Ai*.cs`, `backend/Services/Ai/Agent/`, `frontend/src/lib/ai/` and `frontend/src/components/ai/`.

Each finding is labeled:

- **OBSERVED** — visible in the stored transcript.
- **VERIFIED** — confirmed by reading the code path cited.
- **HYPOTHESIS** — mechanism inferred from code, not yet reproduced; needs a scripted repro before fixing.

This document records problems only. It is the work plan for a follow-up fix PR; nothing here has been changed yet.

## 1. Session summary

User request (dbId 37): produce a CSV of all products whose names contain ≥15 consonants and ≥6 vowels, then upload it to store storage.

| dbId | Role | Content (gist) | toolCallsJson highlights |
|------|------|----------------|--------------------------|
| 34 | user | "выведи таблицу товаров, в названии которых минимум 15 согласных и 6 гласных" | — |
| 35 | assistant | proposal + execution of plan 1 (`run_python` count) merged into one row | `plan` (proposalId `6C4544…`), `planConfirmed`, `planExecuted`, `planExecStart: 0`, empty `content` |
| 36 | assistant | review: full 75-row table printed into chat | **also carries `plan` + all plan flags + `executionProposalId`** |
| 37 | user | "Сделай CSV и загрузи в хранилище" | — |
| 38 | assistant | plan 2 exec merged: 4 search tools + `run_python` that re-scanned the catalog and printed the CSV | `plan`, `planExecStart: 4`, `affectedCount: 74` (actual 75) |
| 39 | assistant | plan 3 proposal (from plan-2 review) + failed execution fused into one row | `platform_request` tool, `plan` (proposalId `FB0D00…`), `planConfirmed`, `planFailed`, **no `planExecStart`** |
| 40 | assistant | review of the failure: "Файл не сохранился… Скажи «повтори»…" | **also carries `plan` + plan flags + `executionProposalId`** |

Net user-visible result: the CSV was generated but never delivered as a file; the upload failed on an invented route; the plan card appeared to "move to the end"; the same plan rendered twice.

## 2. Defect catalog

### D1 — The agent cannot save generated content to store storage (capability gap) — P0

**OBSERVED.** Plan 3 executed `platform_request { "method": "POST", "path": "/store-files", "body": { "name", "mimeType", "content": <full CSV> } }` and got a 404. The file was never saved.

**VERIFIED root causes (three independent layers):**

1. The real upload route is unusable through the agent surface. `PUT /store-storage/files` requires `multipart/form-data` — `MultipartObjectUpload.SaveAsync` rejects anything where `!request.HasFormContentType` (`backend/Infrastructure/Storage/MultipartObjectUpload.cs`). `CabinetRequestInvoker` always serializes the body as JSON and sets `Content-Type: application/json` (`backend/Services/Ai/Agent/CabinetRequestInvoker.cs`, ~lines 97–103). So **even the correct path fails** with `multipart_content_type_invalid` if the model ever finds it.
2. No JSON-callable alternative exists. The only designed agent file path is staged chat attachments → `POST /products/{id}/files { attachmentIds }` (product files only, user-uploaded bytes). `create_file_download_link` covers the opposite direction (existing file → user download). There is no "write text content to a store file" tool or endpoint.
3. Discovery does not warn about this. `CabinetActionCatalog.IsSearchableAction` excludes only binary `GET …/download` routes (`backend/Services/Ai/Agent/CabinetActionCatalog.cs:62-65`); multipart writes are indexed like any JSON action with an auto-generated note (`"upload files store storage"`). `PlatformRequestPolicy.SurfaceBody` (the model-facing route contract) does not mention store-storage upload at all, so the model only learns the route exists — never that it is multipart-only.

**Consequence.** For the request class "generate a file and put it in storage", the agent is guaranteed to fail: it either invents a JSON route (what happened) or finds the real route and hits the multipart wall. This will recur on every similar request.

**Fix directions (pick one or combine):**

- **(a) Add a real capability.** A JSON endpoint or dedicated agent tool (e.g. `save_store_file` / `POST /store-storage/files/from-content`) that accepts `{ name, contentType, contentBase64|text, folderId? }` and writes via `IStorageService` directly, bypassing multipart. This is the only option that actually solves the user task.
- **(b) Signal unreachability.** Mark multipart/form-data actions in `CabinetActionCatalog` (detect via `IFormFile`/`IFormCollection` parameters or an explicit list) and append a note like "requires multipart — not callable via platform_request"; exclude them from `search_actions` results or rank them below JSON actions. Add a line to `SurfaceBody`: "file upload routes need multipart and cannot be called; use staged chat attachments for product files."
- **(c) Prompt guard.** Instruct the model not to `propose_plan` steps it cannot execute through available tools, and to say plainly when a request is unsupported instead of improvising routes.

### D2 — Agent ignored its own `search_actions` result; poor recovery after 404 — P1

**OBSERVED.** In the same turn that produced plan 3, `search_actions` already returned `PUT /store-storage/files` as the top hit. The model nevertheless emitted `POST /store-files` with an invented JSON body.

**OBSERVED.** The final review (dbId 40) refuses to retry: "угадывать второй путь наугад не буду" — factually wrong on two counts: the path was already known from search results, and the actual blocker (multipart vs JSON) was never diagnosed. The 404 title the model saw is a hardcoded generic string (see D7.1) that says nothing about content type.

**Fix directions.**

- Enrich the 404/error payload from `CabinetRequestInvoker`/`AgentPlatformGateway` with the route's real requirements when known (e.g. "this action consumes multipart/form-data; platform_request sends JSON only"). Ideally `search_actions` hits carry a `callableViaJson` flag so the model never plans an impossible call.
- Prompt rule: on `platform_request` failure, re-check `search_actions` for the exact route before declaring the task impossible; never invent path variants (`/store-files` vs `/store-storage/files`).
- Consider a deterministic pre-flight in `AgentToolHost`/`AgentPlatformGateway`: reject mutating calls to known-multipart routes with a corrective tool result instead of letting them 404.

### D3 — Plan state leaks into review/execution messages — P0

**OBSERVED.** dbId 36 and dbId 40 (the post-execution **review** messages) each contain a full copy of the `plan` object **plus** `planConfirmed`, `planExecuted`, `planExecStart`, `planConfirmedBy`, `planConfirmedAtUtc`, `planConfirmationId` — alongside their own `executionProposalId`. A review message should carry only `executionProposalId` + text/timeline.

**VERIFIED.** `AiSseTurnRecorder.FoldTurns` builds the persisted envelope and never writes `planConfirmed`/`planExecStart`/`planConfirmationId`; it writes `planExecuted`, `planFailed`, `turnStopped`, `executionProposalId`, `requestError` only (`backend/Services/Ai/AiSseTurnRecorder.cs`). The leaked flags can only come from two writers:

- `AiConversationTurnStore.TryMergePlanExecution` (server-side merge of the execute-plan snapshot into the proposal row), and
- `AiConversationTurnStore.PreserveServerPlan` (client history sync, `POST /api/ai/conversations/messages`).

`PreserveServerPlan(storedJson, incomingJson)` (`backend/Services/Ai/AiConversationTurnStore.cs`, ~lines 173–199) does the following when the **stored** row contains a `plan`: it deep-copies `stored["plan"]` into the incoming JSON and copies every server flag (`planConfirmed`, `planConfirmedBy`, `planConfirmedAtUtc`, `planConfirmationId`, `planExecuted`, `planExecStart`, `planFailed`) from stored → incoming, **removing** any flag the stored row lacks. There is **no check that the incoming message belongs to the same proposal** — no `proposalId` comparison, no "is this the proposal row itself" guard. Any client-synced message written into a plan-bearing row inherits the whole plan block.

**HYPOTHESIS (mechanism, needs repro).** The client sync path (`AiConversationController.SaveMessages`, `backend/Controllers/AiConversationController.cs` ~lines 132–284) matches incoming messages to existing rows by `dbId`, else by `role + content`, else rebases the final assistant message onto the last existing assistant row. Assistant rows produced by plan execution have **empty `content`** and review rows have distinct content, so content-matching is fragile exactly where plans live. A plausible sequence: the client's review message (content = final text, `executionProposalId`, no plan) failed to match a freshly persisted review row (live-turn persistence races client sync), fell into the final-assistant rebase branch or a content match onto a plan-bearing row, and `PreserveServerPlan` injected `stored.plan` + flags into it. The identical flag values on dbId 36/40 (`planExecStart: 0`, same `planConfirmedAtUtc`) match a byte-level copy from the plan row, which is the signature of `PreserveServerPlan`, not of `Fold`.

**Why it matters beyond cosmetics.** The leaked `plan` makes the frontend render a second `AiPlanCard` on the answer message (see D4), and the leaked flags (`planExecuted`, `planFailed`) corrupt plan-state reconciliation in `frontend/src/lib/ai/planState.js` / `persist.js`.

**Fix directions.**

- Scope `PreserveServerPlan` by identity: transfer `plan` + flags only when `incoming.executionProposalId` is absent **and** (stored plan's `proposalId` equals incoming plan's `proposalId`, or the incoming message is the proposal row itself). Never copy `plan` into a message that carries `executionProposalId`.
- Alternatively/additionally: treat `executionProposalId`-bearing messages as review rows in `SaveMessages` matching — match them by `executionProposalId` inside `toolCallsJson`, not by `role+content`.
- Add a regression test that replays the exact race: live-turn `AppendAssistantAsync` for the review row vs. client `syncHistory` arriving before/after it, asserting the review row never gains `plan`.

### D4 — Plan card renders at the end / duplicated; `planExecStart` inconsistency — P0

**OBSERVED.** The user saw the plan "move to the end by itself": a plan card appeared below/after content that should follow it, and the same plan rendered twice.

**VERIFIED rendering rule.** `AiChatPanel.vue` renders `<AiPlanCard>` for any message where `planHasContent(msg)` is true (~lines 1060–1082). There is no exclusion for review messages. So any message carrying a leaked `plan` (D3) shows a full second plan card directly above the final answer — visually "the plan moved to the end".

**VERIFIED layout fallback.** `frontend/src/lib/ai/timeline.js` `executionView`/`splitPlanExecution` rely on `planExecStart` to separate proposal-process from execution-process. dbId 39 **lacks `planExecStart`** (unlike dbId 35 `=0` and dbId 38 `=4`), so `hasPlanExec` is false, `replyText` is suppressed by the `msg.plan → ""` branch, and the layout degenerates to `executionView(everything)`: order becomes process → reply → **plan card last** — i.e. a confirmation card rendered *after* the already-failed `platform_request` step. The persisted shape is inconsistent across the three plan rows.

**HYPOTHESIS.** dbId 39's missing `planExecStart` is consistent with the row being last written through the `SaveMessages`/`PreserveServerPlan` path at a moment when the stored row had `planConfirmed` but not yet `planExecuted`/`planExecStart` (the flag copy removes keys the stored row lacks). This again points at D3's multi-writer race rather than `TryMergePlanExecution`, which always sets `planExecStart`.

**Fix directions.**

- Frontend: never render `AiPlanCard` when `msg.executionProposalId` is set (review message), regardless of a leaked `plan`. Defense in depth even after D3 is fixed, because old rows already contain the leak.
- Frontend: in `planState.js`/`persist.js`, ignore `plan*` fields on messages carrying `executionProposalId` during reconcile/hydrate.
- Backend: normalize — ensure every plan-bearing persisted row has `planExecStart` (or explicitly document its absence) regardless of which writer touched it last.
- Data note: existing stored rows already contain leaked plans; decide whether a one-off cleanup is needed or whether read-side filtering suffices.

### D5 — `affectedCount` is model-supplied and wrong — P2

**OBSERVED.** Plan 2 declared `affectedCount: 74`; the run produced 75 rows. Plan 1 declared `affectedCount: 97` — that is "products read", not "affected".

**VERIFIED.** `AiChatService` passes `planInput.AffectedCount` through into the plan object (~lines 454–455); server-side validation of `affectedCount` exists only for named-export `productIds` plans, not for `run_python` steps. The plan card therefore shows a number the model guessed.

**Fix directions.** Compute the count server-side where possible, or rename/relabel the field semantics for non-write plans (e.g. `expectedRows`/`null`), and validate `affectedCount ≤ actual` post-execution where a count is derivable.

### D6 — Content-quality defects — P2

1. **No artifact handoff between steps.** The upload plan inlined the entire ~75-row CSV into the `platform_request` JSON body (dbId 39). There is no mechanism for `run_python` output to become a file/attachment that a later step references. For larger catalogs this breaks on request-size limits. Even with D1 fixed, a staged-content path ("python writes artifact → referenced by id in next plan step") is needed for robustness.
2. **Full recomputation.** Plan 2 re-queried all 97 products and recounted letters from scratch although the previous turn already held the data. Stateless plan steps make this expected, but the two scripts are **not identical**: script 1's `C_LAT` includes `y` as a consonant, script 2's `CON` does not. Results coincided (75 = 75) only because no product name contains `y`. Latent inconsistency; also wasted tokens/time.
3. **Duplicate table dump.** The 75-row table appears both in `run_python` stdout and verbatim in the review's `content` (dbId 36) — double token cost for data already on screen. Review guidance should summarize + offer the file instead of re-printing.
4. **Review hijacked by next plan, no bridge text.** After plan 2 executed, the review produced no "CSV готов" answer — it directly emitted plan 3's proposal (`discardDraftText` erased any prose). The user sees two plan cards back-to-back with no explanation of what happened between them.
5. **Tone/register break.** dbId 40 switches from formal «вы» to «ты» («Скажи», «чтобы ты скачал») mid-conversation.
6. **Context blindness in the failure answer.** "могу вывести таблицу прямо в чат, чтобы ты скачал её сам" — the table was already printed in dbId 36; and chat text is not downloadable anyway. The offered "alternative" is both redundant and non-functional.
7. **Read-only work forced through plan confirmation.** Counting letters is pure reads, but `run_python` always requires `propose_plan` → the user confirmed two plans for a computation. `sql_readonly` cannot express letter counting, so the friction is structural; consider a read-only python mode or clearer plan summaries ("только чтение, ничего не изменится").

### D7 — Error-surface defects — P1/P2

1. **Hardcoded, irrelevant 404 hint.** Any cabinet-route 404 is rewritten to `"This path does not exist. Do not invent cabinet URLs."` plus a hardcoded mapping-canvas hint (`backend/Services/Ai/Agent/CabinetRequestInvoker.cs`, ~lines 331–347). For `/store-files` the mapping hint is meaningless and actively misleads the model toward the mapping-graph surface.
2. **System status text shown as assistant prose.** `AgentToolResult.FormatPlanOutcome` emits "Не удалось завершить шаги (0 из 1). Ошибка не доказывает, что изменений не было…" as a `Text` delta in the execution segment (`backend/Services/Ai/Agent/AgentToolResult.cs:185-229`, yielded from `AiChatService` ~line 820), and the raw model-facing error line "• This path does not exist…" lands in the visible reply. Users see internal contract text instead of a clean status.
3. **Empty `content` on execution rows.** By design (data lives in `toolCallsJson`), but combined with D3/D4 it amplifies the "messages are split apart" impression. Consider a short human summary in `content` for exec rows.

## 3. Agent harness — why the model confabulated, and what we actually control

The defects above are mostly deterministic (a leaked JSON field, a wrong render branch). The deepest problem in this session is different: **the model invented `POST /store-files`, ignored the real route its own `search_actions` had returned, continued with the invented route after a 404, and ended by deferring to the user instead of diagnosing.** This section analyzes that layer separately, because "make the model smarter" is not an actionable fix — the actionable surface is the harness around it.

### 3.1 Why the model invented a route

- **Affordance contradiction with no resolution signal.** `search_actions` reported that `PUT /store-storage/files` exists; `platform_request` can only send JSON. Nothing in either surface says "exists but not callable by you". Faced with an impossible task, the model resolved the contradiction by confabulating a plausible JSON route (`POST /store-files` + `{name, mimeType, content}`). LLMs confabulate under constraint violation far more often than they report incapacity — this is expected behavior, not a bug in the weights.
- **Action hits carry no callability semantics.** `CabinetActionCatalog` notes are auto-generated from action/controller names (`"upload files store storage"`). The hit does not expose HTTP content type, whether the action consumes `IFormFile`/`IFormCollection`, or whether `platform_request` can express the call at all. "Route exists" and "I can call it" are indistinguishable to the model.
- **No negative capability space.** `PlatformRequestPolicy.SurfaceBody` enumerates what `platform_request` *can* do; nothing enumerates what the assistant *cannot* do (multipart uploads, binary downloads, arbitrary storage writes). Models fill silent gaps with plausible guesses.

### 3.2 Why it kept working with the invented route

- **Unstructured error feedback.** The invoker returned a bare 404 plus the generic title `"This path does not exist. Do not invent cabinet URLs."` — which is literally true but carries no error class. The model could not distinguish "I guessed the wrong path" from "the route exists but I called it wrong" from "the route needs a content type I cannot send". With no discriminating signal, the cheapest continuation is to stay consistent with the prior (invented) call — self-consistency bias: models almost never retract their own tool calls, they confabulate a narrative consistent with having acted ("the storage section does not exist").
- **Uncertainty → deferral.** "Скажи «повтори»" is the classic buck-passing failure mode: when unsure, a model minimizes risk by handing the next step to the user rather than attempting another unverifiable call.

### 3.3 What "reasoning" we actually control

The provider-side chain-of-thought is opaque and not exposed to us; we see only tool calls and text. The reasoning surface we *do* control is the harness: system prompt, tool schemas/descriptions, tool-result payloads, error taxonomy, plan gates, and the review loop. Every confabulation above traces to a missing or ambiguous signal in that harness — not to a missing "reasoning" feature. The fix is therefore to make wrong behavior **impossible or self-correcting**, not to ask the model to try harder.

### 3.4 Agent-layer work items (new, not covered by D1–D7)

- **H1 — Structured tool-error taxonomy (P1).** `platform_request`/gateway failures should return machine-readable classes — `route_not_found`, `method_not_allowed`, `multipart_required`, `validation_failed`, `forbidden` — plus `hint` and `callableAlternatives[]`, instead of free text. The frontend must not render raw classes to users (ties to D7.2). This single change converts "404 + vibes" into an actionable correction signal.
- **H2 — Pre-flight validation before invoke (P0/P1).** `AgentToolHost`/`AgentPlatformGateway` should check `method + path` against `CabinetActionCatalog`/`PlatformRequestPolicy` *before* dispatch: reject known-impossible combinations (multipart route via the JSON invoker, denied prefixes, wrong verb) with a corrective `tool_result` so the model gets a second chance **in the same turn** instead of a post-hoc 404. This is the strongest anti-confabulation lever: the invented call never reaches a controller.
- **H3 — Explicit capability manifest (P1).** Prompt + `SurfaceBody` should state CAN/CANNOT plainly: JSON cabinet routes, python sandbox, connected MCP, staged attachments (user→product direction only), download tickets — and "creating store files from generated content: NOT SUPPORTED" until D1 option (a) ships. Negative space is as important as the route list.
- **H4 — Plan feasibility gate (P1).** `propose_plan` validation should reject steps whose `platform_request` targets known-unusable routes (multipart/binary/wrong-verb), so a plan card never promises an impossible step. The observed plan 3 was confirmable and executable-shaped but guaranteed to fail — the gate exists for exactly this.
- **H5 — Review-loop diagnostics (P2).** Execution outcomes already return to the review model; add structured per-step `errorClass` and, where known, the verified cause ("previous path was not a real route; the real route `PUT /store-storage/files` requires multipart, which is unavailable"). Review should diagnose root cause, not narrate a guess.
- **H6 — `search_actions` callability flags (P1).** Hits should carry `callableViaJson`, `contentType`, `binaryDownload` fields; scoring should penalize non-callable hits for write intent and the note should say so explicitly. Cheap, high-leverage: it fixes D1-discovery and D2 simultaneously.
- **H7 — Anti-confabulation prompt rules + audit (P2).** Rules like "never invent paths; if `search_actions` returns no callable route, state the capability is missing" and "on failure, report the verified cause, not a plausible one". `AssistantInstructionAudit` already fails tests when prompt/tool contract drift — extend its fixtures with this session as a regression case, and add the session to `assistant-quality-evaluation.md`'s real-model fixture list.
- **H8 — Reasoning visibility (P3, optional).** Some providers expose reasoning summaries; not required for the fixes above, but the offline eval protocol should capture full tool-call traces per run so confabulation patterns are measurable over time.

### 3.5 Why the fix plan weights contract/UI bugs first anyway

Deterministic defects (D3 plan leak, D4 card placement) have certain fixes and corrupt persisted data — they are fixed regardless of model behavior. The agent-layer items (H1–H8) constrain a probabilistic system: they raise the floor (impossible calls get rejected, errors become actionable) but cannot guarantee a particular answer. Both layers are needed; neither substitutes for the other.

## 4. Prioritized fix plan

| Pri | Item | Why first |
|-----|------|-----------|
| P0 | D1 — real save-to-storage capability **or** explicit unsupported-route signaling | Every "generate file → storage" request fails today; model improvises routes |
| P0 | D3 — scope `PreserveServerPlan` by proposal identity | Corrupts persisted data and feeds D4 |
| P0 | D4 — suppress plan card on `executionProposalId` messages; investigate `planExecStart` gap | Direct cause of "plan moved to the end"; also protects already-corrupted rows |
| P1 | D2 — actionable error feedback + no-invented-routes rule | Cheap prompt/tooling fix, big recovery improvement |
| P1 | D7.1 — neutral or route-aware 404 hint | One-line-ish fix, removes misleading guidance |
| P1 | H2 — pre-flight route validation before `platform_request` invoke | Makes impossible calls self-correcting in-turn |
| P1 | H6 — `callableViaJson`/content-type flags on `search_actions` hits | Fixes discovery + gives model callability semantics |
| P1 | H3 — explicit CAN/CANNOT capability manifest | Closes the negative-space gap that invited confabulation |
| P1 | H4 — plan feasibility gate rejects impossible steps | Plan card must not promise unexecutable calls |
| P2 | H1 — structured tool-error taxonomy | Enables H5 and cleaner UX; depends on H2 design |
| P2 | H5 — structured diagnostics into review loop | Improves post-failure diagnosis quality |
| P2 | H7 — anti-confabulation prompt rules + audit fixtures | Prompt-level reinforcement; verify via instruction audit |
| P2 | D5 — server-side/validated `affectedCount` | Wrong numbers on plan cards |
| P2 | D6 — artifact handoff, dedupe table, bridging review text, tone | Quality polish; some items depend on D1 design |
| P2 | D7.2/7.3 — clean status surface, exec-row summaries | UX polish |
| P3 | H8 — reasoning-summary capture in eval harness | Optional; measurement, not a fix |

## 5. Verification plan (for the fix PR)

- **Backend unit/integration** (`backend.Tests/Ai/`): scripted cases for (a) `PreserveServerPlan` never copying `plan` into an `executionProposalId` message; (b) `SaveMessages` race — review row persisted by live hub before/after client sync, assert final row has no `plan`; (c) multipart route marked non-callable in `search_actions`/`SurfaceBody` if option (b) of D1 is chosen; (d) new save-file endpoint/tool contract tests if option (a) is chosen.
- **Frontend unit**: `AiChatPanel`/`timeline`/`planState` cases — message with `executionProposalId` + leaked `plan` renders no plan card; `planExecStart`-less plan rows still lay out sanely.
- **E2E intercepted** (`frontend/e2e/`): replay the plan→confirm→execute→review flow with a stubbed SSE, reload mid-turn, assert single plan card and correct order.
- **Agent harness** (`backend.Tests/Ai/`): scripted cases for pre-flight rejection of multipart-via-JSON calls (H2), `callableViaJson` flags on catalog hits (H6), plan-gate rejection of impossible steps (H4), and structured error classes reaching the review payload (H1/H5). Extend `AssistantInstructionAudit` fixtures per H7.
- **Docs gate**: update `docs/architecture/ai-assistant.md` for any new tool/endpoint; CHANGELOG `Unreleased` entry for user-visible fixes.
- If D1 option (a) adds an endpoint: check whether it belongs to the public `/api/v1` surface (likely not — cabinet-only) and keep `docs/api/` in sync only if it does.

## 6. Open questions / needs reproduction

- Exact `SaveMessages` branch that wrote the leaked plan into dbId 36/40 (content-match vs final-assistant rebase vs `nextUnpersistedIndex` walk). Needs a timed repro; the `PreserveServerPlan` signature (identical flag values copied from the plan row) strongly suggests it, but the matching path is unproven.
- Whether dbId 39 was last written by `TryMergePlanExecution` or by the sync path — determines whether `planExecStart` absence is a merge bug or a sync bug.
- Whether the `run_python` sandbox preserves state between calls (would explain/justify the re-query) — check `SANDBOX_URL` worker semantics before designing the artifact-handoff fix.
- Whether old corrupted rows need a cleanup migration/job or read-side filtering is enough (depends on D4 fix choice).
- How to detect multipart/binary actions for H2/H6 — scan action parameters for `IFormFile`/`IFormCollection`/`IFormFile` collections vs. maintaining an explicit list; needs a catalog-level decision before implementing.
