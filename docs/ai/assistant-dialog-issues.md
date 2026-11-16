# Assistant dialog defect analysis — generated-file export session (2026-11-16)

Source material: a stored `aiConversationMessages` transcript (message `dbId`s 34–40) of a real cabinet-chat session, audited against `backend/Services/Ai/`, `backend/Controllers/Ai*.cs`, `backend/Services/Ai/Agent/`, `frontend/src/lib/ai/` and `frontend/src/components/ai/`.

Each finding is labeled:

- **OBSERVED** — visible in the stored transcript.
- **VERIFIED** — confirmed by reading the code path cited.
- **HYPOTHESIS** — mechanism inferred from code, not yet reproduced; needs a scripted repro before fixing.

This document is the work plan for the follow-up fixes; per-item status is tracked in §4 and in the file workstream (§7). The F1 bounded small-text upload is implemented in the same PR as this document update — not deployed; see §7. The analysis below otherwise records the session as observed.

## 1. Session summary

User request (dbId 37): produce a CSV of all products whose names contain ≥15 consonants and ≥6 vowels, then upload it to store storage.

| dbId | Role | Content (gist) | toolCallsJson highlights |
|------|------|----------------|--------------------------|
| 34 | user | "выведи таблицу товаров, в названии которых минимум 15 согласных и 6 гласных" | — |
| 35 | assistant | proposal + execution of plan 1 (`run_python` count) merged into one row | `plan` (proposalId `6C4544…`), `planConfirmed`, `planExecuted`, `planExecStart: 0`, empty `content` |
| 36 | assistant | review: full 75-row table printed into chat | **also carries `plan` + all plan flags + `executionProposalId`** (suspected copied state; see D3) |
| 37 | user | "Сделай CSV и загрузи в хранилище" | — |
| 38 | assistant | plan 2 exec merged: 4 search tools + `run_python` that re-scanned the catalog and printed the CSV | `plan`, `planExecStart: 4`, `affectedCount: 74` (actual 75) |
| 39 | assistant | plan 3 proposal (from plan-2 review) + failed execution fused into one row | `platform_request` tool, `plan` (proposalId `FB0D00…`), `planConfirmed`, `planFailed`, **no `planExecStart`** |
| 40 | assistant | review of the failure: "Файл не сохранился… Скажи «повтори»…" | **also carries `plan` + plan flags + `executionProposalId`** (suspected copied state; see D3) |

Net user-visible result: the CSV was generated but never delivered as a file; the upload failed on an invented route; the plan card appeared to "move to the end"; the same plan rendered twice.

## 2. Defect catalog

### D1 — The agent cannot save generated content to store storage (capability gap) — P0

**OBSERVED.** Plan 3 executed `platform_request { "method": "POST", "path": "/store-files", "body": { "name", "mimeType", "content": <full CSV> } }` and got a 404. The file was never saved.

**VERIFIED root causes (three independent layers):**

1. The real upload route is unusable through the agent surface. `PUT /store-storage/files` requires `multipart/form-data` — `MultipartObjectUpload.SaveAsync` rejects anything where `!request.HasFormContentType` (`backend/Infrastructure/Storage/MultipartObjectUpload.cs`). `CabinetRequestInvoker` always serializes the body as JSON and sets `Content-Type: application/json` (`backend/Services/Ai/Agent/CabinetRequestInvoker.cs`, ~lines 97–103). So **even the correct path fails** with `multipart_content_type_invalid` if the model ever finds it.
2. No JSON-callable alternative exists. The only designed agent file path is staged chat attachments → `POST /products/{id}/files { attachmentIds }` (product files only, user-uploaded bytes). `create_file_download_link` covers the opposite direction (existing file → user download). There is no "write text content to a store file" tool or endpoint.
3. Discovery does not warn about this. `CabinetActionCatalog.IsSearchableAction` excludes only binary `GET …/download` routes (`backend/Services/Ai/Agent/CabinetActionCatalog.cs:62-65`); multipart writes are indexed like any JSON action with an auto-generated note (`"upload files store storage"`). `PlatformRequestPolicy.SurfaceBody` (the model-facing route contract) does not mention store-storage upload at all, so the model only learns the route exists — never that it is multipart-only.

**Consequence.** For the request class "generate a file and put it in storage", the agent is guaranteed to fail: it either invents a JSON route (what happened) or finds the real route and hits the multipart wall. This will recur on every similar request.

**Status (update, historical baseline).** The absolute claim above describes the pre-F1 state. F1 (`POST /store-storage/files/from-text`, see §7) can persist small text already available to the model; it does not solve byte-exact sandbox artifact transfer and does not prove the original session is fixed. The multipart route is also explicitly described as not JSON-callable in the `platform_request` surface. D1 is therefore **partially** addressed: binary and large sandbox artifacts still have no upload path (F2, §7), so the defect is not closed.

**Fix directions (pick one or combine):**

- **(a) Add a real capability.** A JSON endpoint or dedicated agent tool (e.g. `save_store_file` / `POST /store-storage/files/from-content`) that accepts `{ name, contentType, contentBase64|text, folderId? }` and writes via `IStorageService` directly, bypassing multipart. This is the only option that actually solves the user task. **Implemented in this PR, not deployed**: F1 (`POST /store-storage/files/from-text`, plain text/CSV/JSON up to 262144 UTF-8 bytes); large or binary artifacts remain part of F2.
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

**OBSERVED.** dbId 36 and dbId 40 (the post-execution **review** messages) each contain a full copy of the `plan` object **plus** `planConfirmed`, `planExecuted`, `planExecStart`, `planConfirmedBy`, `planConfirmedAtUtc`, `planConfirmationId` — alongside their own `executionProposalId`. On both rows the embedded plan carries the parent proposal's flag values (`planExecStart: 0`, same `planConfirmedAtUtc`). The earlier audit described matching parent-plan fields; this is a copied-state hypothesis requiring proposal-ID and authoritative message-ownership validation, not proof from marker co-presence.

**Correction to an earlier claim.** "A review message should carry only `executionProposalId` + text/timeline" is **unsupported** and is retracted: a review that proposes a *new* plan legitimately carries both `executionProposalId` and `plan` (see counter-evidence below). The defect to investigate is unintended copying of parent state, not marker co-presence.

**VERIFIED counter-evidence (marker co-presence is legitimate).** `executionProposalId` is the *parent execution* linkage (proposal A); `plan.proposalId` is the message's *own* proposal (B). `AiChatService` emits the review turn's `AssistantStart` with `executionProposalId = A` (`backend/Services/Ai/AiChatService.cs` ~line 845); when the model then proposes a correction, the subsequent `Plan` event mints a distinct B, and `AiSseTurnRecorder.FoldTurns` preserves **both** fields on the same persisted envelope (`backend/Services/Ai/AiSseTurnRecorder.cs`, `executionProposalId` + `plan`). `backend.Tests/Ai/AiChatAgentEvaluationTests.cs` `FailedPythonPlan_ReviewsRealErrorAndProposesCorrectionWithoutExecutingIt` (~lines 483–521) exercises exactly this flow: the review proposes B ≠ A, the turns persist, and B is later consumed — so `executionProposalId` + `plan` co-presence is **not** proof of a leak.

**VERIFIED.** `AiSseTurnRecorder.FoldTurns` builds the persisted envelope and never writes `planConfirmed`/`planExecStart`/`planConfirmationId`; it writes `planExecuted`, `planFailed`, `turnStopped`, `executionProposalId`, `requestError` only (`backend/Services/Ai/AiSseTurnRecorder.cs`). The leaked flags can only come from two writers:

- `AiConversationTurnStore.TryMergePlanExecution` (server-side merge of the execute-plan snapshot into the proposal row), and
- `AiConversationTurnStore.PreserveServerPlan` (client history sync, `POST /api/ai/conversations/messages`).

`PreserveServerPlan(storedJson, incomingJson)` (`backend/Services/Ai/AiConversationTurnStore.cs`, ~lines 173–199) does the following when the **stored** row contains a `plan`: it deep-copies `stored["plan"]` into the incoming JSON and copies every server flag (`planConfirmed`, `planConfirmedBy`, `planConfirmedAtUtc`, `planConfirmationId`, `planExecuted`, `planExecStart`, `planFailed`) from stored → incoming, **removing** any flag the stored row lacks. There is **no check that the incoming message belongs to the same proposal** — no `proposalId` comparison, no "is this the proposal row itself" guard. Any client-synced message written into a plan-bearing row inherits the whole plan block.

**HYPOTHESIS (mechanism, needs repro).** The client sync path (`AiConversationController.SaveMessages`, `backend/Controllers/AiConversationController.cs` ~lines 132–284) matches incoming messages to existing rows by `dbId`, else by `role + content`, else rebases the final assistant message onto the last existing assistant row. Assistant rows produced by plan execution have **empty `content`** and review rows have distinct content, so content-matching is fragile exactly where plans live. A plausible sequence: the client's review message (content = final text, `executionProposalId`, no plan) failed to match a freshly persisted review row (live-turn persistence races client sync), fell into the final-assistant rebase branch or a content match onto a plan-bearing row, and `PreserveServerPlan` injected `stored.plan` + flags into it. The identical flag values on dbId 36/40 (`planExecStart: 0`, same `planConfirmedAtUtc`) match a byte-level copy from the plan row, which is the signature of `PreserveServerPlan`, not of `Fold`.

**Why it matters beyond cosmetics.** A *copied* parent plan on a review row makes the frontend render a second `AiPlanCard` on the answer message (see D4), and copied flags (`planExecuted`, `planFailed`) can corrupt plan-state reconciliation in `frontend/src/lib/ai/planState.js` / `persist.js`. By contrast, an own proposal B on a review row is legitimate and must stay visible and confirmable — the same rendering path must not treat it as a leak.

**Design rules (agreed).**

- `PreserveServerPlan` may preserve only *own-proposal* state: transfer `plan` + flags only when the stored plan's `proposalId` equals the incoming message's own `plan.proposalId` **and** the message identity is stable — or when the incoming message is the proposal row itself. The parent link must never overwrite B with A: a review carrying B must not inherit A's `planConfirmed`/`planExecuted`/`planFailed`/`planExecStart`.
- A same-A duplication is only a **candidate** for suppression/normalization: apply it only when stable message identity plus authoritative proposal ownership establish the `plan` as a copy — never on marker presence alone or an unverified heuristic.
- Rows with missing or ambiguous IDs are **retained and investigated**; no destructive blanket cleanup.
- `executionProposalId` alone is not a unique join key (reused across snapshots/retries): `SaveMessages` must match review rows by stable message identity plus their own plan identity, not by `executionProposalId` or `role+content` alone.
- A review that proposes no plan must never acquire an unrelated parent plan — enforced by the same identity checks, not by filtering `plan` off `executionProposalId` messages.
- Regression tests (paired cases listed in §5) replay the exact race: live-turn `AppendAssistantAsync` for the review row vs. client `syncHistory` arriving before/after it — asserting the review never gains a *copied* plan while a legitimately proposed B survives.

### D4 — Plan card renders at the end / duplicated; `planExecStart` inconsistency — P0

**OBSERVED.** The user saw the plan "move to the end by itself": a plan card appeared below/after content that should follow it, and the same plan rendered twice.

**VERIFIED rendering rule.** `AiChatPanel.vue` renders `<AiPlanCard>` for any message where `planHasContent(msg)` is true (~lines 1060–1082). There is no exclusion for review messages. So a review row carrying any `plan` — a copied parent plan (D3) *or* its own proposal B — renders a plan card directly above the final answer. For a copied plan that is visually "the plan moved to the end"; for B it is the legitimate, required rendering.

**VERIFIED layout fallback.** `frontend/src/lib/ai/timeline.js` `executionView`/`splitPlanExecution` rely on `planExecStart` to separate proposal-process from execution-process. dbId 39 **lacks `planExecStart`** (unlike dbId 35 `=0` and dbId 38 `=4`), so `hasPlanExec` is false, `replyText` is suppressed by the `msg.plan → ""` branch, and the layout degenerates to `executionView(everything)`: order becomes process → reply → **plan card last** — i.e. a confirmation card rendered *after* the already-failed `platform_request` step. Caveat: `planExecStart` marks execution start, so a *pending* proposal legitimately lacks it — the blanket rule "every plan-bearing row must have `planExecStart`" is retracted. What is suspicious on dbId 39 specifically is a row that *executed* (carries `planFailed`) yet lacks the marker.

**HYPOTHESIS.** dbId 39's missing `planExecStart` is consistent with the row being last written through the `SaveMessages`/`PreserveServerPlan` path at a moment when the stored row had `planConfirmed` but not yet `planExecuted`/`planExecStart` (the flag copy removes keys the stored row lacks). This again points at D3's multi-writer race rather than `TryMergePlanExecution`, which always sets `planExecStart`.

**Fix directions.**

- Frontend: suppress `AiPlanCard` only when the `plan` is established as a **copy** of the parent proposal — same `proposalId` as the parent execution's plan plus copied flag state **and** authoritative message ownership with a stable ID; all three conditions are required. A review whose `plan.proposalId` is its own new proposal B must still render B's card — B stays visible and confirmable. `executionProposalId` presence alone is not a suppression signal; blanket filtering on it is retracted as a fix direction.
- Frontend `planState.js`/`persist.js`: reconcile/hydrate own-proposal state keyed by the message's own `plan.proposalId` + stable message identity; a distinct B must not inherit A's confirmed/executed/failed flags.
- Backend: keep `planExecStart` semantics honest — it marks execution start and pending proposals legitimately lack it. The open question is only whether an *executed* row that lacks it (dbId 39) indicates a writer bug (§6).
- Data note: retain existing rows; no destructive blanket cleanup. Rows *proven* to carry a copied parent plan (identity evidence, not heuristics) may be normalized read-side; ambiguous rows are investigated, not rewritten.

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

The defects above are mostly deterministic (a copied `plan` field, a wrong render branch). The deepest problem in this session is different: **the model invented `POST /store-files`, ignored the real route its own `search_actions` had returned, continued with the invented route after a 404, and ended by deferring to the user instead of diagnosing.** This section analyzes that layer separately, because "make the model smarter" is not an actionable fix — the actionable surface is the harness around it.

### 3.1 Why the model invented a route

- **Affordance contradiction with no resolution signal.** `search_actions` reported that `PUT /store-storage/files` exists; `platform_request` can only send JSON. Nothing in either surface says "exists but not callable by you". Faced with that contradiction, the transcript shows an invented JSON route (`POST /store-files` + `{name, mimeType, content}`). Whether the model internally "resolved" the contradiction or merely sampled a plausible continuation is not observable from the trace — that framing is an unverified hypothesis, as is the generalization that LLMs confabulate under constraint violation more often than they report incapacity.
- **Action hits carry no callability semantics.** `CabinetActionCatalog` notes are auto-generated from action/controller names (`"upload files store storage"`). The hit does not expose HTTP content type, whether the action consumes `IFormFile`/`IFormCollection`, or whether `platform_request` can express the call at all. "Route exists" and "I can call it" are indistinguishable to the model.
- **No negative capability space.** `PlatformRequestPolicy.SurfaceBody` enumerates what `platform_request` *can* do; nothing enumerates what the assistant *cannot* do (multipart uploads, binary downloads, arbitrary storage writes). Models fill silent gaps with plausible guesses.

### 3.2 Why it kept working with the invented route

- **Unstructured error feedback.** The invoker returned a bare 404 plus the generic title `"This path does not exist. Do not invent cabinet URLs."` — which is literally true but carries no error class. The model could not distinguish "I guessed the wrong path" from "the route exists but I called it wrong" from "the route needs a content type I cannot send". With no discriminating signal it stayed consistent with the prior (invented) call. The proposed mechanism — a self-consistency bias under which models almost never retract their own tool calls — is an unverified hypothesis, not a measurement from this trace.
- **Uncertainty → deferral.** "Скажи «повтори»" is the classic buck-passing failure mode: when unsure, a model minimizes risk by handing the next step to the user rather than attempting another unverifiable call.

### 3.3 What "reasoning" we actually control

The provider-side chain-of-thought was not exposed in this transcript — we saw only tool calls and text. Whether it is uniformly opaque across providers is unverified; what is verified is that the reasoning surface we *do* control is the harness: system prompt, tool schemas/descriptions, tool-result payloads, error taxonomy, plan gates, and the review loop. The missing/ambiguous harness signals are verified weaknesses, but their contribution to this model output remains a hypothesis. The fix is therefore to make *known-invalid* calls rejected or self-correcting in that harness — gates can block known invalid calls, not eliminate fabrication — not to ask the model to try harder.

### 3.4 Agent-layer work items (new, not covered by D1–D7)

- **H1 — Structured tool-error taxonomy (P1).** `platform_request`/gateway failures should return machine-readable classes — `route_not_found`, `method_not_allowed`, `multipart_required`, `validation_failed`, `forbidden` — plus `hint` and `callableAlternatives[]`, instead of free text. The frontend must not render raw classes to users (ties to D7.2). This single change converts "404 + vibes" into an actionable correction signal.
- **H2 — Pre-flight validation before invoke (P0/P1).** `AgentToolHost`/`AgentPlatformGateway` should check `method + path` against `CabinetActionCatalog`/`PlatformRequestPolicy` *before* dispatch: reject known-impossible combinations (multipart route via the JSON invoker, denied prefixes, wrong verb) with a corrective `tool_result` so the model gets a second chance **in the same turn** instead of a post-hoc 404. This is the strongest anti-confabulation lever: the invented call never reaches a controller.
- **H3 — Explicit capability manifest (P1).** Prompt + `SurfaceBody` should state CAN/CANNOT plainly: JSON cabinet routes, python sandbox, connected MCP, staged attachments (user→product direction only), download tickets — and "creating store files from generated content: NOT SUPPORTED" until D1 option (a) ships. Negative space is as important as the route list.
- **H4 — Plan feasibility gate (P1).** `propose_plan` validation should reject steps whose `platform_request` targets known-unusable routes (multipart/binary/wrong-verb), so a plan card never promises a known-unexecutable step. The observed plan 3 was confirmable and executable-shaped but guaranteed to fail — the gate exists for exactly this.
- **H5 — Review-loop diagnostics (P2).** Execution outcomes already return to the review model; add structured per-step `errorClass` and, where known, the verified cause ("previous path was not a real route; the real route `PUT /store-storage/files` requires multipart, which is unavailable"). Review should diagnose root cause, not narrate a guess.
- **H6 — `search_actions` callability flags (P1).** Hits should carry `callableViaJson`, `contentType`, `binaryDownload` fields; scoring should penalize non-callable hits for write intent and the note should say so explicitly. Cheap, high-leverage: it fixes D1-discovery and D2 simultaneously.
- **H7 — Anti-confabulation prompt rules + audit (P2).** Rules like "never invent paths; if `search_actions` returns no callable route, state the capability is missing" and "on failure, report the verified cause, not a plausible one". `AssistantInstructionAudit` already fails tests when prompt/tool contract drift — extend its fixtures with this session as a regression case, and add the session to `assistant-quality-evaluation.md`'s real-model fixture list.
- **H8 — Reasoning visibility (P3, optional).** Some providers expose reasoning summaries; not required for the fixes above, but the offline eval protocol should capture full tool-call traces per run so confabulation patterns are measurable over time.

### 3.5 Why the fix plan weights contract/UI bugs first anyway

Deterministic defects (D3 plan leak, D4 card placement) are more tractable than model behavior — though "corrupt persisted data" is still a strong hypothesis rather than a proven mechanism: the copied-plan signature on dbId 36/40 is consistent with `PreserveServerPlan`, but no ID-specific reproduction has run yet (§6). The agent-layer items (H1–H8) constrain a probabilistic system: they raise the floor (known-unusable calls get rejected, errors become actionable) but cannot guarantee a particular answer. Both layers are needed; neither substitutes for the other.

## 4. Prioritized fix plan

| Pri | Item | Why first |
|-----|------|-----------|
| P0 | D1 — save-to-storage capability: **F1 small-text path DONE (§7)**; F2 binary/large artifact upload still open | Small generated text now succeeds; large/binary artifacts still fail and invite improvisation |
| P0 | D3 — scope `PreserveServerPlan` by own-proposal + stable message identity; never overwrite a distinct B with parent A | Suspected persisted-data corruption (strong signature, ID-specific repro pending); feeds D4 |
| P0 | D4 — suppress plan card only for proven-copied plans, not all `executionProposalId` rows; investigate `planExecStart` gap | Direct cause of "plan moved to the end"; protects suspect rows without hiding legitimate B proposals |
| P1 | D2 — actionable error feedback + no-invented-routes rule | Cheap prompt/tooling fix, big recovery improvement |
| P1 | D7.1 — neutral or route-aware 404 hint | One-line-ish fix, removes misleading guidance |
| P1 | H2 — pre-flight route validation before `platform_request` invoke | Makes known-unusable calls self-correcting in-turn |
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

- **Backend unit/integration** (`backend.Tests/Ai/`): the paired D3 regression cases — (a) a legitimate review proposing B ≠ A retains B through client sync and reload with B still confirmable — the existing `FailedPythonPlan_ReviewsRealErrorAndProposesCorrectionWithoutExecutingIt` covers persistence and consumption of B only; the client-sync/reload legs remain planned; (b) a review row proven to carry a copied parent plan A is normalized and not rendered twice; (c) a review with no new plan cannot inherit an unrelated parent plan through `PreserveServerPlan`; (d) two review rows sharing the same `executionProposalId` are not merged by A alone; (e) the `SaveMessages` race — review row persisted by live hub before/after client sync — never injects a copied plan. Plus (f) multipart route marked non-callable in `search_actions`/`SurfaceBody` if option (b) of D1 is chosen; (g) save-file endpoint contract tests for option (a) — F1 covered.
- **Frontend unit**: `AiChatPanel`/`timeline`/`planState` cases — a message carrying a proven-copied parent plan renders no second card, while a review with its own plan B renders B's card; pending (`planExecStart`-less) proposal rows still lay out sanely.
- **E2E intercepted** (`frontend/e2e/`): replay the plan→confirm→execute→review flow with a stubbed SSE, reload mid-turn, assert a copied plan is not rendered twice while a fresh B proposal stays confirmable.
- **Agent harness** (`backend.Tests/Ai/`): scripted cases for pre-flight rejection of multipart-via-JSON calls (H2), `callableViaJson` flags on catalog hits (H6), plan-gate rejection of impossible steps (H4), and structured error classes reaching the review payload (H1/H5). Extend `AssistantInstructionAudit` fixtures per H7.
- **Docs gate**: update `docs/architecture/ai-assistant.md` for any new tool/endpoint; CHANGELOG `Unreleased` entry for user-visible fixes.
- If D1 option (a) adds an endpoint: check whether it belongs to the public `/api/v1` surface (likely not — cabinet-only) and keep `docs/api/` in sync only if it does.

## 6. Open questions / needs reproduction

- Exact `SaveMessages` branch that wrote the leaked plan into dbId 36/40 (content-match vs final-assistant rebase vs `nextUnpersistedIndex` walk). Needs a timed repro; the `PreserveServerPlan` signature (identical flag values copied from the plan row) strongly suggests it, but the matching path is unproven.
- Whether dbId 39 was last written by `TryMergePlanExecution` or by the sync path — determines whether `planExecStart` absence is a merge bug or a sync bug.
- Authoritative proposal-ownership signal: what persisted evidence distinguishes "plan owned by this row (B)" from "plan copied from the parent row (A)" when `proposalId` values collide or are missing — needed before any suppression or normalization rule can be safely applied.
- Whether the `run_python` sandbox preserves state between calls (would explain/justify the re-query) — check `SANDBOX_URL` worker semantics before designing the artifact-handoff fix.
- Whether old rows carrying copied plans need normalization or read-side filtering is enough (depends on the D4 identity rule); ambiguous rows stay untouched per the no-blanket-cleanup rule.
- How to detect multipart/binary actions for H2/H6 — scan action parameters for `IFormFile`/`IFormCollection`/`IFormFile` collections vs. maintaining an explicit list; needs a catalog-level decision before implementing.

## 7. File workstream (F1–F3) — status

The "generate a file → save to storage → user downloads" request class is now a mandatory, separately tracked workstream. Downloads reuse existing mechanisms: `create_file_download_link` already issues 10-minute authenticated tickets for **product** files, store files already stream through the authenticated selected-store route `GET /api/store-storage/files/{id}/download`, and chat markdown renders that route as an ordinary link. Whether a deterministic chat download control or a typed store-file ticket (F3) is actually needed stays **open** — code inspection alone does not settle it; decide from real sessions.

Scope note: this step implements **F1 only**. None of the harness workstreams (H1–H8) — including H1 error taxonomy and H2 pre-flight route validation — is implemented here.

- **F1 — bounded small-text upload: implemented in this PR, unit-tested, not deployed.** `POST /store-storage/files/from-text` accepts `{ fileName, text, contentType?, folderId? }`, requires `files.manage` on the selected store, encodes `text` as UTF-8 with a strict 262144-byte cap (request envelope capped at 2 MiB), allows only `text/plain` (default), `text/csv`, `application/json`, enforces the store's file/storage quotas before upload, and persists one private `StoreFile` under the existing `{storeId}/store/{guid}/{name}` key convention — never public, no overwrite, no public link. The response is `{ id, fileName, contentType, length, folderId, downloadUrl }`; a failed row save deletes the uploaded object. `platform_request` treats it as a write (confirmed plan required), `search_actions` finds it via RU/EN aliases, and the canonical `AssistantWriteContract` sentence is embedded in the surface and audited by `AssistantInstructionAudit`. Coverage: unit tests only — **no live-model or real-catalog proof yet**.
- **F2 — sandbox→HTTP streaming artifact upload: OPEN (P0).** For binary or large artifacts produced inside the `run_python` sandbox: a streaming HTTP upload (or presigned artifact handoff) that keeps model context metadata-only (id/name/size — never bytes), still under an explicit confirmed write authorization. The Python contract stays explicitly write-free — no Python-side storage bypass.
- **F3 — typed store-file tickets + deterministic chat download control: OPEN (optional).** Product-file tickets are already store/user-checked but require the product file type; an equivalent typed private ticket for store files, independent of the currently selected store header, plus a deterministic chat download control if plain authenticated links prove insufficient.
