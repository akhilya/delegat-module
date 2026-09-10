# Assistant questionnaire (`ask_form` questions)

Validated 2026-09-09. Short series on the reply, then one user message.

## Shape

Same tool `ask_form`. `kind=connection` is unchanged (browser-only keys). `kind=questions` (and legacy `kind=fields`) is a shadcn-vue **Questionnaire** on the reply: up to **5** steps, one on screen.

Step types: `single`, `multiple`, `text`. Optional steps can be skipped. Choices may include a freeform “other” (`allowOther`). No passwords or secrets.

## After submit

The model still gets one user turn: `followUp` with `{key}` filled (multiple values joined with `, `). Skipped keys omitted.

That turn is **not shown as a chat bubble**. Answers stay on the questionnaire card (read-only recap) and in the assistant-turn envelope (`formReplies`) so a reload cannot ask again.

## When the model asks

Only if a missing fact would change the next write. If intent is clear, assume in one clause and act. Do not ask “are you sure?” (`propose_plan`). Do not list questions in the reply text.
