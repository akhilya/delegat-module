import assert from "node:assert/strict";
import { test } from "node:test";
import { z } from "zod";
import { toTypedSchema } from "./zodSchema.js";

test("Zod 4 form adapter maps nested errors to vee-validate paths", async () => {
  const schema = toTypedSchema(
    z.object({
      items: z.array(z.object({ name: z.string().min(1, "Name required") })),
    }),
  );

  const result = await schema.parse({ items: [{ name: "" }] });
  assert.deepEqual(result.errors, [
    { path: "items[0].name", errors: ["Name required"] },
  ]);
  assert.deepEqual(await schema.parse({ items: [{ name: "Valid" }] }), {
    value: { items: [{ name: "Valid" }] },
    errors: [],
  });
});

test("Zod 4 form adapter preserves defaults and required field descriptions", () => {
  const schema = toTypedSchema(
    z.object({
      rememberMe: z.boolean().default(true),
      product: z.object({ name: z.string().min(1) }),
    }),
  );

  assert.deepEqual(schema.cast({ product: { name: "Draft" } }), {
    rememberMe: true,
    product: { name: "Draft" },
  });
  assert.deepEqual(schema.describe("rememberMe"), {
    exists: true,
    required: false,
  });
  assert.deepEqual(schema.describe("product.name"), {
    exists: true,
    required: true,
  });
});
