// Adapt Zod 4 schemas to vee-validate's typed schema interface.
const formPath = (segments) =>
  segments.reduce((path, segment) => {
    if (typeof segment === "number") return `${path}[${segment}]`;
    return path ? `${path}.${segment}` : String(segment);
  }, "");

const schemaAtPath = (schema, path) => {
  const segments = path.match(/[^.[\]]+/g) || [];
  let current = schema;
  for (const segment of segments) {
    while (current && typeof current.unwrap === "function")
      current = current.unwrap();
    current =
      current?.shape?.[segment] ??
      (Array.isArray(current?.element)
        ? current.element[Number(segment)]
        : current?.element);
  }
  return current;
};

const defaultsFor = (schema) => {
  if (!schema?.shape) return {};
  return Object.fromEntries(
    Object.entries(schema.shape).flatMap(([key, field]) => {
      const parsed = field.safeParse(undefined);
      if (parsed.success) return [[key, parsed.data]];
      const nested = defaultsFor(field);
      return Object.keys(nested).length ? [[key, nested]] : [];
    }),
  );
};

const mergeDefaults = (defaults, values) => {
  if (!values || typeof values !== "object" || Array.isArray(values))
    return values ?? defaults;
  return Object.fromEntries(
    Object.entries({ ...defaults, ...values }).map(([key, value]) => [
      key,
      defaults[key] &&
      typeof defaults[key] === "object" &&
      !Array.isArray(defaults[key])
        ? mergeDefaults(defaults[key], value)
        : value,
    ]),
  );
};

export const toTypedSchema = (schema) => ({
  __type: "VVTypedSchema",
  async parse(value) {
    const result = await schema.safeParseAsync(value);
    if (result.success) return { value: result.data, errors: [] };
    const errors = new Map();
    for (const issue of result.error.issues) {
      const path = formPath(issue.path);
      if (!errors.has(path)) errors.set(path, { path, errors: [] });
      errors.get(path).errors.push(issue.message);
    }
    return { errors: [...errors.values()] };
  },
  cast(values) {
    const result = schema.safeParse(values);
    return result.success
      ? result.data
      : mergeDefaults(defaultsFor(schema), values);
  },
  describe(path) {
    const field = path ? schemaAtPath(schema, path) : schema;
    return {
      exists: !!field,
      required: !!field && !field.safeParse(undefined).success,
    };
  },
});
