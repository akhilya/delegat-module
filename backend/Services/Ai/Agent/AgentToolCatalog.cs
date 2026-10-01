using Delegat.Services.Ai;

namespace Delegat.Services.Ai.Agent;

public static class AgentToolCatalog
{
    public static List<AiTool> Build()
    {
        List<AiTool> tools =
        [
        Prop("propose_plan",
            "ALWAYS the first and only tool when the user asked to change catalog or mapping. Do not GET, search_actions, or platform_request first. Shows the user a plan and waits for confirmation. Writes run only after they confirm.",
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
        Prop("search_knowledge", "Search fetched product docs, MEMORY/DESIGN, and instance operator playbooks. Follow hits with get_doc (slug or sourcePath).",
            Req(["query"]), ("query", Str()), ("limit", Int("Max hits, default 12"))),
        Prop("get_doc", "Load a product docs article by slug, or an indexed instance doc by sourcePath (MEMORY.md, docs/ai/operator-playbook.md, docs/…). Do not invent UI missing from the article.",
            Req(["slug"]), ("slug", Str("e.g. workspace/products or docs/ai/operator-playbook.md")), ("locale", Str("ru or en"))),
        Prop("search_catalog", "Search this store's products by name, SKU, barcode; also file display names.",
            Req([]), ("query", Str()), ("limit", Int())),
        Prop("search_actions", "Find cabinet HTTP the assistant can run (same JSON API as the SPA). Use before inventing a path or GET /tools. Query in store language (export, выгрузка, import, match). Hits include method, path, and body notes.",
            Req(["query"]), ("query", Str("e.g. export products to Medusa without photos")), ("limit", Int("Max hits, default 8"))),
        Prop("sql_readonly", "Read-only SELECT against allowlisted store tables. storeId is forced. No SELECT *. SHOW TABLES / SHOW COLUMNS FROM table / DESCRIBE table list the allowlist (or SELECT table_name, column_name FROM schema). WHERE supports = != <> < > <= >= LIKE NOT LIKE IN NOT IN IS NULL IS NOT NULL and IN or NOT IN (SELECT productId FROM files|product_images|product_attribute_values|store_product_attribute_values). SELECT DISTINCT col (one column). The same WHERE is the catalog filter on GET /products, PATCH /products/bulk, and export-batch (field where). There is no has-photos flag.",
            Req(["sql"]), ("sql", Str("SELECT id, name, sku FROM products LIMIT 50"))),
        ];
        if (PythonSandboxClient.IsConfigured())
        {
            tools.Add(Prop("run_python",
                "Run curated Python in the instance sandbox (pandas/pillow/openpyxl). `client` and `shops` are already loaded. `shops` holds this store’s connection keys, seller hosts, and `docs.platform` when Control Plane keeps that seller’s daily snapshot — call those hosts with urllib (direct HTTP). Read `client.marketplace_doc(docs.platform, query)` before inventing a seller path. Do not fetch a documentation URL yourself. Do not print credentials. Uses Agent HTTP for the cabinet, never the database URL. If the user asks to check this helper or run a snippet, call this tool this turn. A shop lookup or other read-only script does not need propose_plan; do not change the seller account from the script.",
                Req(["code"]), ("code", Str("Python source"))));
        }

        tools.AddRange(
        [
        Prop("platform_request", "Call the same cabinet JSON API as the SPA (method+path+optional JSON body). Secrets and a few irreversible routes are denied. Permissions and quotas are the signed-in user's. Writes require propose_plan first.",
            Req(["method", "path"]),
            ("method", Str("GET/POST/PATCH/DELETE")),
            ("path", Str("e.g. /products?startIndex=0&pageSize=50, /marketplace/connections/1/products/export-batch, /categories/5/attributes")),
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
            "Show 1–6 buttons on the reply. Each button is one action: url (open a page) OR prompt (send a follow-up), never both. Only /relative paths (query string allowed, e.g. /products?page=2&pageSize=15) with real IDs from tools. Prefer buttons over telling the user to go somewhere.",
            Req([]),
            ("url", Str("In-app path with optional query, e.g. /products/12/edit or /products?page=2&pageSize=15 — open the page; do not also set prompt")),
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
                        ["url"] = Str("In-app path with optional query (open), e.g. /products?page=2&pageSize=15. Do not set prompt on the same item."),
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
