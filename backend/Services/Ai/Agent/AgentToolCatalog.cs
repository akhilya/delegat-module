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
        Prop("search_knowledge", "Search platform docs, OpenAPI, and canon (MEMORY/DESIGN).",
            Req(["query"]), ("query", Str()), ("limit", Int("Max hits, default 8"))),
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
            "Show 1–4 buttons in the chat reply (open a cabinet page and/or send a follow-up). Only /relative paths with real IDs from tools. Prefer buttons over telling the user to go somewhere.",
            Req([]),
            ("url", Str("In-app path, e.g. /products/12/edit")),
            ("label", Str("Button text — catalog name copied exactly")),
            ("prompt", Str("Optional: tapping the button sends this as the user's next message")),
            ("items", new AiToolProperty
            {
                type = "array",
                description = "Up to 4 buttons. Each needs url and/or prompt, plus label.",
                items = new AiToolProperty
                {
                    type = "object",
                    properties = new Dictionary<string, AiToolProperty>
                    {
                        ["url"] = Str("In-app path"),
                        ["label"] = Str("Button text"),
                        ["prompt"] = Str("Optional follow-up the user sends by tapping"),
                    },
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
