import assert from "node:assert/strict";
import { describe, it } from "node:test";
import {
  adapterOrderActionFields,
  applyOrderActionFieldValues,
  defaultOrderActionFieldValues,
  mergeOrderStatusPayload,
  orderActionFieldsValid,
} from "./orderActionFields.js";

describe("orderActionFields", () => {
  const medusa = {
    orderActionFields: {
      cancel: [
        {
          key: "reason",
          name: "Причина",
          type: "select",
          required: true,
          target: "reason",
          options: [
            { value: "out_of_stock", label: "Нет в наличии" },
            { value: "other", label: "Другая" },
          ],
        },
        {
          key: "note",
          name: "Комментарий",
          type: "textarea",
          target: "extra",
          requiredWhenKey: "reason",
          requiredWhenValue: "other",
          visibleWhenKey: "reason",
          visibleWhenValue: "other",
        },
        {
          key: "refund",
          name: "Вернуть оплату",
          type: "checkbox",
          target: "extra",
          defaultValue: "true",
        },
      ],
    },
  };

  it("reads adapter cancel fields and builds the status payload", () => {
    const fields = adapterOrderActionFields(medusa, "cancel");
    assert.equal(fields.length, 3);
    const values = defaultOrderActionFieldValues(fields);
    assert.equal(values.reason, "out_of_stock");
    assert.equal(values.refund, true);
    assert.equal(orderActionFieldsValid(fields, values), true);
    assert.deepEqual(applyOrderActionFieldValues(fields, values), {
      reason: "out_of_stock",
      extra: { refund: "true" },
    });
  });

  it("requires a note when the adapter marks other as required", () => {
    const fields = adapterOrderActionFields(medusa, "cancel");
    const values = {
      ...defaultOrderActionFieldValues(fields),
      reason: "other",
      note: "",
    };
    assert.equal(orderActionFieldsValid(fields, values), false);
    values.note = "нет размера";
    assert.equal(orderActionFieldsValid(fields, values), true);
    assert.deepEqual(applyOrderActionFieldValues(fields, values), {
      reason: "other",
      extra: { note: "нет размера", refund: "true" },
    });
  });

  it("merges adapter extra onto the ship payload", () => {
    assert.deepEqual(
      mergeOrderStatusPayload(
        { action: "ship", extra: { supplyId: "WB-1" } },
        { extra: { cancelReasonId: "352" }, reason: "x" },
      ),
      {
        action: "ship",
        extra: { supplyId: "WB-1", cancelReasonId: "352" },
        reason: "x",
      },
    );
  });
});
