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

## 3. Prioritized fix plan

| Pri | Item | Why first |
|-----|------|-----------|
| P0 | D1 — real save-to-storage capability **or** explicit unsupported-route signaling | Every "generate file → storage" request fails today; model improvises routes |
| P0 | D3 — scope `PreserveServerPlan` by proposal identity | Corrupts persisted data and feeds D4 |
| P0 | D4 — suppress plan card on `executionProposalId` messages; investigate `planExecStart` gap | Direct cause of "plan moved to the end"; also protects already-corrupted rows |
| P1 | D2 — actionable error feedback + no-invented-routes rule | Cheap prompt/tooling fix, big recovery improvement |
| P1 | D7.1 — neutral or route-aware 404 hint | One-line-ish fix, removes misleading guidance |
| P2 | D5 — server-side/validated `affectedCount` | Wrong numbers on plan cards |
| P2 | D6 — artifact handoff, dedupe table, bridging review text, tone | Quality polish; some items depend on D1 design |
| P2 | D7.2/7.3 — clean status surface, exec-row summaries | UX polish |

## 4. Verification plan (for the fix PR)

- **Backend unit/integration** (`backend.Tests/Ai/`): scripted cases for (a) `PreserveServerPlan` never copying `plan` into an `executionProposalId` message; (b) `SaveMessages` race — review row persisted by live hub before/after client sync, assert final row has no `plan`; (c) multipart route marked non-callable in `search_actions`/`SurfaceBody` if option (b) of D1 is chosen; (d) new save-file endpoint/tool contract tests if option (a) is chosen.
- **Frontend unit**: `AiChatPanel`/`timeline`/`planState` cases — message with `executionProposalId` + leaked `plan` renders no plan card; `planExecStart`-less plan rows still lay out sanely.
- **E2E intercepted** (`frontend/e2e/`): replay the plan→confirm→execute→review flow with a stubbed SSE, reload mid-turn, assert single plan card and correct order.
- **Docs gate**: update `docs/architecture/ai-assistant.md` for any new tool/endpoint; CHANGELOG `Unreleased` entry for user-visible fixes.
- If D1 option (a) adds an endpoint: check whether it belongs to the public `/api/v1` surface (likely not — cabinet-only) and keep `docs/api/` in sync only if it does.

## 5. Open questions / needs reproduction

- Exact `SaveMessages` branch that wrote the leaked plan into dbId 36/40 (content-match vs final-assistant rebase vs `nextUnpersistedIndex` walk). Needs a timed repro; the `PreserveServerPlan` signature (identical flag values copied from the plan row) strongly suggests it, but the matching path is unproven.
- Whether dbId 39 was last written by `TryMergePlanExecution` or by the sync path — determines whether `planExecStart` absence is a merge bug or a sync bug.
- Whether the `run_python` sandbox preserves state between calls (would explain/justify the re-query) — check `SANDBOX_URL` worker semantics before designing the artifact-handoff fix.
- Whether old corrupted rows need a cleanup migration/job or read-side filtering is enough (depends on D4 fix choice).
