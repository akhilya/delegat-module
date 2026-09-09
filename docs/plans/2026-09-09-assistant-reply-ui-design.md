# Assistant reply UI — buttons and forms

Validated in chat (2026-09-09). Implementation lives in `AgentToolHost` / `AgentToolCatalog` / `AiPrompts.ChatSystem` and `frontend/src/lib/ai/replyUi.js` plus `AiChatActions` / `AiChatForm`.

## Reply buttons (`navigate_to`)

- Up to **6** buttons via `items[]`.
- **One action per button:** `url` (open an in-app path) XOR `prompt` (send a follow-up). If both are set, keep `url` and drop `prompt`.
- Open and Continue are visually distinct. Open shows the **path**. Continue shows the **follow-up text**.
- Chrome stays on the reply, not in the process disclosure.

## Reply forms (`ask_form`)

**`kind=connection`:** the SPA collects marketplace credentials with the same `ConnectionCreateForm` as Adapters and `POST /marketplace/connections`. Secrets are **browser-only**: the model sees schema + “filled”, never the key; the key is not stored on the thread.

**`kind=fields`:** still accepted; the SPA turns it into the same questionnaire.

Connection create stays **`route_denied`** for agent `platform_request`. Invalid `ask_form` input returns `{ form: false, error }`.
