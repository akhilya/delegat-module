using Delegat.Services.Ai;

namespace Delegat.Services.Ai.Agent;

public static class AgentToolCatalog
{
    public static List<AiTool> Build()
    {
        List<AiTool> tools =
        [
        Prop("propose_plan",
            "ALWAYS call first before any mutating operation (platform_request writes, run_python that changes data). Shows the user a plan and waits for confirmation.",
            Req(["summary", "steps"]),
            ("summary", Str("User-visible summary in store language (UI locale). No tool names, SQL, or HTTP.")),
            ("steps", new AiToolProperty
            {
                type = "array",
                description = "Steps with tool + args. Use platform_request for API mutations, including POST /products/{id}/files with attachmentIds for staged chat files. Each description is shown to the user in store language.",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["tool"] = Str("Internal tool name (not shown)"),
                        ["description"] = Str("User-visible step in store language (UI locale). No tool names, SQL, or HTTP."),
                        ["args"] = new AiToolProperty { type = "object", description = "Exact arguments" },
                    },
                },
            }),
            ("affectedCount", Int("How many entities")),
            ("isDestructive", Bool("True for delete/overwrite"))),
        Prop("search_knowledge", "Search fetched product docs and canon (MEMORY/DESIGN). For how-tos, follow with get_doc.",
            Req(["query"]), ("query", Str()), ("limit", Int("Max hits, default 8"))),
        Prop("get_doc", "Load the full product documentation article by slug. Do not invent UI that is not in the article.",
            Req(["slug"]), ("slug", Str("e.g. workspace/products")), ("locale", Str("ru or en"))),
        Prop("search_catalog", "Search this store's products by name, SKU, barcode; also file display names.",
            Req([]), ("query", Str()), ("limit", Int())),
        Prop("sql_readonly", "Read-only SELECT against allowlisted store tables (products, files, categories, category_attributes, product_attribute_values, store_attributes, store_product_attribute_values, groups, connections, mappings, orders). storeId is forced. No SELECT *. WHERE supports = != LIKE IN (...).",
            Req(["sql"]), ("sql", Str("SELECT id, name FROM category_attributes WHERE categoryId = 5"))),
        ];
        if (PythonSandboxClient.IsConfigured())
        {
            tools.Add(Prop("run_python",
                "Run curated Python in the instance sandbox (pandas/pillow/openpyxl). `client` is already imported. Uses Agent HTTP, never the database URL.",
                Req(["code"]), ("code", Str("Python source"))));
        }

        tools.AddRange(
        [
        Prop("platform_request", "Call the same cabinet JSON API as the SPA (method+path+optional JSON body). Secrets and a few irreversible routes are denied. Permissions and quotas are the signed-in user's. Writes require propose_plan first.",
            Req(["method", "path"]),
            ("method", Str("GET/POST/PATCH/DELETE")),
            ("path", Str("e.g. /products?searchQuery=&categoryId=&startIndex=&pageSize=, /categories/5/attributes, /marketplace/orders?tab=")),
            ("body", new AiToolProperty { type = "object", description = "JSON body for writes" })),
        Prop("match_files", "Optional: match filenames (including staged chat attachments) to products in this store (SKU/barcode/name in the filename).",
            Req(["files"]),
            ("files", new AiToolProperty
            {
                type = "array",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["relativePath"] = Str(),
                        ["fileName"] = Str(),
                    },
                    required = ["relativePath"],
                },
            })),
        Prop("navigate_to",
            "Show 1–6 buttons on the reply. Each button is one action: url (open a page) OR prompt (send a follow-up), never both. Only /relative paths with real IDs from tools. Prefer buttons over telling the user to go somewhere.",
            Req([]),
            ("url", Str("In-app path, e.g. /products/12/edit — open the page; do not also set prompt")),
            ("label", Str("Button text — catalog name copied exactly")),
            ("prompt", Str("Follow-up the user sends by tapping (store language). Omit url on this button.")),
            ("items", new AiToolProperty
            {
                type = "array",
                description = "Up to 6 buttons. Each needs label plus url XOR prompt.",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["url"] = Str("In-app path (open). Do not set prompt on the same item."),
                        ["label"] = Str("Button text"),
                        ["prompt"] = Str("Follow-up to send. Do not set url on the same item."),
                    },
                },
            })),
        Prop("ask_form",
            "Show a form on the reply. kind=connection: browser collects marketplace keys and POSTs create-connection — never put keys in chat. kind=questions: up to 5 steps (single, multiple, text); optional steps can be skipped; submit sends followUp with {key} filled. kind=fields still works as questions.",
            Req(["kind"]),
            ("kind", Str("connection or questions (fields still works)")),
            ("adapterKey", Str("ozon, wildberries, yandexmarket, woocommerce, opencart")),
            ("name", Str("Optional connection name")),
            ("title", Str("Short form heading in store language")),
            ("submitLabel", Str("Submit button in store language")),
            ("followUp", Str("questions: user message after submit, with {key} placeholders")),
            ("questions", new AiToolProperty
            {
                type = "array",
                description = "Up to 5 steps. Non-secret. Prefer choices over free text.",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["key"] = Str("placeholder name, letters/digits/underscore"),
                        ["title"] = Str("Question in store language"),
                        ["description"] = Str("Optional hint"),
                        ["type"] = Str("single, multiple, or text"),
                        ["required"] = Bool("Default true. False = skippable"),
                        ["allowOther"] = Bool("Freeform besides choices"),
                        ["placeholder"] = Str(),
                        ["choices"] = new AiToolProperty
                        {
                            type = "array",
                            description = "single/multiple. Up to 8.",
                            items = new AiToolProperty
                            {
                                type = "object",
                                properties = new Dictionary<string, AiToolProperty>
                                {
                                    ["value"] = Str(),
                                    ["label"] = Str(),
                                    ["description"] = Str(),
                                },
                            },
                        },
                    },
                    required = ["key", "title"],
                },
            }),
            ("fields", new AiToolProperty
            {
                type = "array",
                description = "Legacy. Non-secret text/select. Prefer questions.",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["key"] = Str("placeholder name, letters/digits/underscore"),
                        ["label"] = Str("Field label in store language"),
                        ["type"] = Str("text or select"),
                        ["required"] = Bool("Default true"),
                        ["placeholder"] = Str(),
                        ["options"] = new AiToolProperty
                        {
                            type = "array",
                            items = new AiToolProperty
                            {
                                type = "object",
                                properties = new Dictionary<string, AiToolProperty>
                                {
                                    ["value"] = Str(),
                                    ["label"] = Str(),
                                },
                            },
                        },
                    },
                    required = ["key", "label"],
                },
            })),
        ]);
        return tools;
    }

    private static AiTool Prop(string name, string desc, AiToolInputSchema schema, params (string key, AiToolProperty prop)[] props)
    {
        schema.properties ??= new();
        foreach (var (key, prop) in props)
            schema.properties[key] = prop;
        return new AiTool { name = name, description = desc, input_schema = schema };
    }

    private static AiToolInputSchema Req(string[] required) => new() { required = required.ToList(), properties = new() };
    private static AiToolProperty Str(string? desc = null) => new() { type = "string", description = desc };
    private static AiToolProperty Int(string? desc = null) => new() { type = "number", description = desc };
    private static AiToolProperty Bool(string? desc = null) => new() { type = "boolean", description = desc };
}
