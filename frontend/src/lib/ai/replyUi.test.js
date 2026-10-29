import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  chatActionsFromTool,
  answersFromFormData,
  interpolateFollowUp,
  normalizeAskForm,
  normalizeChatAction,
  recapFormAnswers,
} from "./replyUi.js";

describe("reply UI", () => {
  it("includes typed questionnaire answers from form data", () => {
    const formData = new FormData();
    formData.set("product", "FI10");

    assert.deepEqual(
      answersFromFormData([{ key: "product", type: "text" }], formData),
      { product: "FI10" },
    );
  });

  it("keeps open and continue as separate actions and drops a hidden prompt on a url button", () => {
    const open = normalizeChatAction({
      url: "/products/12/edit",
      label: "Hello soap",
      prompt: "should not send",
    });
    assert.equal(open.kind, "open");
    assert.equal(open.url, "/products/12/edit");
    assert.equal(open.prompt, "");

    const cont = normalizeChatAction({
      label: "Put photos",
      prompt: "Put these photos on Hello soap",
    });
    assert.equal(cont.kind, "continue");
    assert.equal(cont.prompt, "Put these photos on Hello soap");
    assert.equal(cont.url, "");
  });

  it("keeps catalog query strings on open buttons", () => {
    const open = normalizeChatAction({
      url: "/products?page=2&pageSize=15",
      label: "Catalog",
    });
    assert.equal(open.kind, "open");
    assert.equal(open.url, "/products?page=2&pageSize=15");
    assert.equal(
      normalizeChatAction({ url: "/products?next=https://evil.example" }),
      null,
    );
  });

  it("reads navigate_to actions from the tool result", () => {
    const actions = chatActionsFromTool({
      result: {
        navigate: true,
        actions: [
          { url: "/products/12/edit", label: "Hello soap", kind: "open" },
          { prompt: "Put these photos on Hello soap", label: "Put photos" },
        ],
      },
    });
    assert.equal(actions.length, 2);
    assert.equal(actions[0].kind, "open");
    assert.equal(actions[1].kind, "continue");
  });

  it("drops secret fields from a stored ask_form result", () => {
    const form = normalizeAskForm({
      form: true,
      kind: "fields",
      followUp: "Shop is {shop}",
      fields: [
        { key: "shop", label: "Shop URL", type: "text" },
        { key: "apiKey", label: "Key", type: "password" },
        { key: "token", label: "Token", secret: true },
      ],
    });
    assert.equal(form.kind, "questions");
    assert.equal(form.questions.length, 1);
    assert.equal(form.questions[0].key, "shop");
    assert.equal(
      interpolateFollowUp(form.followUp, { shop: "https://a.example" }),
      "Shop is https://a.example",
    );
  });

  it("normalizes a questions questionnaire and drops secrets", () => {
    const form = normalizeAskForm({
      form: true,
      kind: "questions",
      followUp: "Use {adapter} and {scope}",
      questions: [
        {
          key: "adapter",
          title: "Which marketplace?",
          type: "single",
          choices: [
            { value: "ozon", label: "Ozon" },
            { value: "wb", label: "Wildberries" },
          ],
        },
        {
          key: "scope",
          title: "What to inspect?",
          type: "multiple",
          required: false,
          choices: [
            { value: "prices", label: "Prices" },
            { value: "stock", label: "Stock" },
          ],
        },
        { key: "apiKey", title: "Key", type: "password" },
      ],
    });
    assert.equal(form.kind, "questions");
    assert.equal(form.questions.length, 2);
    assert.equal(form.questions[0].type, "single");
    assert.equal(form.questions[1].required, false);
    assert.equal(
      interpolateFollowUp(form.followUp, {
        adapter: "ozon",
        scope: ["prices", "stock"],
      }),
      "Use ozon and prices, stock",
    );
  });

  it("turns legacy fields into questionnaire steps", () => {
    const form = normalizeAskForm({
      form: true,
      kind: "fields",
      fields: [
        { key: "shop", label: "Shop URL", type: "text" },
        {
          key: "channel",
          label: "Channel",
          type: "select",
          options: [{ value: "ozon", label: "Ozon" }],
        },
      ],
    });
    assert.equal(form.kind, "questions");
    assert.equal(form.questions.length, 2);
    assert.equal(form.questions[0].type, "text");
    assert.equal(form.questions[1].type, "single");
    assert.equal(form.questions[1].choices[0].value, "ozon");
  });

  it("recaps questionnaire answers with choice labels", () => {
    const rows = recapFormAnswers(
      [
        {
          key: "adapter",
          title: "Which marketplace?",
          choices: [
            { value: "ozon", label: "Ozon" },
            { value: "wb", label: "Wildberries" },
          ],
        },
        { key: "note", title: "Note", choices: [] },
      ],
      { adapter: "ozon", note: "FBS" },
    );
    assert.equal(rows.length, 2);
    assert.equal(rows[0].answer, "Ozon");
    assert.equal(rows[1].answer, "FBS");
  });

  it("requires a real adapter key for a connection form", () => {
    assert.equal(
      normalizeAskForm({ form: true, kind: "connection", adapterKey: "ozon" })
        .adapterKey,
      "ozon",
    );
    assert.equal(
      normalizeAskForm({ form: true, kind: "connection", adapterKey: " " }),
      null,
    );
  });
});
