using System.Text.Json;
using Delegat.Services.Ai;

namespace Delegat.Services.Ai.Agent;

public static class AgentToolCatalog
{
    /// <summary>
    /// <paramref name="includeGeneratedArtifacts"/> is the cabinet-chat surface only:
    /// external MCP/Agent callers build the default catalog and never see the
    /// chat-confirmed import tool.
    /// </summary>
    public static List<AiTool> Build(bool includeGeneratedArtifacts = false)
    {
        List<AiTool> tools =
        [
        Prop("propose_plan",
            "Use for every cabinet change and before every run_python or connected MCP call. Python and connected MCP calls must be proposed by themselves and wait for confirmation because external capabilities are not guaranteed to be read-only. For a cabinet change, if the request and CURRENT PAGE provide all required facts, call this as the first and only tool; if an exact current state or required value is missing, do only the minimum required built-in read-only lookup(s) first, then call propose_plan by itself. The user-visible plan must state the exact intended effect. Operations run only after confirmation.",
            Req(["summary", "steps"]),
            ("summary", Str("User-visible summary in store language (UI locale). No tool names, SQL, or HTTP.")),
            ("steps", new AiToolProperty
            {
                type = "array",
                description = "Steps with tool + args. Use platform_request for cabinet API mutations, including POST /products/{id}/files with attachmentIds for staged chat files. Connected MCP tools and run_python may only appear inside a plan. Each description is shown to the user in store language and must state the exact intended effect.",
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
        Prop("memory_search", "Search only durable facts and preferences explicitly saved for the current user in this store. This does not search conversation transcripts. Use only when an answer needs a saved fact that is not already present in the current conversation or supplied memory context, and the user has not asked you not to search.",
            Req([]), ("query", Str("Words to find in saved memories; omit to list recent memories"))),
        Prop("memory_save", "Save or update one durable, non-sensitive user fact/preference for future conversations. Never save credentials, secrets, or inferred facts. Use only facts the user stated; save stable preferences automatically when useful, or anything they explicitly ask you to remember.",
            Req(["category", "key", "value"]), ("category", Str("Short group, e.g. preference, workflow, business")),
            ("key", Str("Stable fact name, e.g. preferred_language")), ("value", Str("Concise user-stated fact"))),
        Prop("memory_delete", "Delete one saved memory by the id returned by memory_search. Use when the user asks to forget or correct a stored fact.",
            Req(["id"]), ("id", Int("Memory id from memory_search"))),
        Prop("get_doc", "Load a product docs article by slug, or an indexed instance doc by sourcePath (MEMORY.md, docs/ai/operator-playbook.md, docs/…). Do not invent UI missing from the article.",
            Req(["slug"]), ("slug", Str("e.g. workspace/products or docs/ai/operator-playbook.md")), ("locale", Str("ru or en"))),
        Prop("read_chat_attachment", "Read a live file attached to this chat. Returns bounded extracted text or worksheet rows; binary bytes never enter the model payload. Use only an attachment id listed in the chat context. For workbooks, read all relevant columns with nextStartColumn before continuing at nextStartRow; for text, continue with nextStartOffset until relevant content is read. Read the file before planning changes that depend on its contents; never search server paths, query file tables, or discover a download route.",
            Req(["attachmentId"]),
            ("attachmentId", Int("Live chat attachment id listed in the conversation")),
            ("sheetName", Str("Optional worksheet name for .xlsx/.xlsm; defaults to the first sheet")),
            ("startRow", Int("1-based worksheet row to start at; use nextStartRow to continue")),
            ("rowLimit", Int("Worksheet rows to return, default 50, maximum 200")),
            ("startColumn", Int("1-based worksheet column; use nextStartColumn for wide sheets")),
            ("startOffset", Int("0-based character offset for text formats; use nextStartOffset to continue")),
            ("maxCharacters", Int("Text characters to return, default 12000, maximum 16000"))),
        Prop("search_catalog", "Search this store's products by name, SKU, barcode; also file display names. Exact SKU/barcode/name hits precede partial matches. Product hits contain identity metadata only (id, SKU, barcode, name, category), not price, storageQuantity, or description; missing fields are not evidence of null. productsHasMore/filesHasMore mean the matching set exceeds the limit; never present a limited result as a complete list. For those facts, read the requested field from the exact product separately. For a specific lookup, provide a non-empty query. An empty/missing query lists a broad set; use it only when the user explicitly asks to browse or list products, never to clarify which product they mean.",
            Req([]), ("query", Str()), ("limit", Int())),
        Prop("search_actions", "Find native cabinet actions (same JSON API as the SPA). Search the specific field, operation, and scope: regenerate product barcodes, change catalog stock, export listings to shop. Prefer supported bulk/server generation to Python preparation or one write per product. Hits include method, path, supported semantics and accepted body fields; a hit must actually solve the task. Refine an unrelated result with the concrete field, never guess from generic update matches. Binary download routes are omitted; use create_file_download_link for product-file bytes.",
            Req(["query"]), ("query", Str("e.g. export products to Medusa without photos")), ("limit", Int("Max hits, default 8"))),
        Prop("sql_readonly", "Read-only SELECT against allowlisted store tables. storeId is forced. No SELECT *. SHOW TABLES / SHOW COLUMNS FROM table / DESCRIBE table list the allowlist (or SELECT table_name, column_name FROM schema). WHERE supports = != <> < > <= >= LIKE NOT LIKE IN NOT IN IS NULL IS NOT NULL and IN or NOT IN (SELECT productId FROM files|product_images|product_attribute_values|store_product_attribute_values). SELECT DISTINCT col (one column). The same WHERE is the catalog filter on GET /products, PATCH /products/bulk, and export-batch (field where). There is no has-photos flag.",
            Req(["sql"]), ("sql", Str("SELECT id, name, sku FROM products LIMIT 50"))),
        ];
        if (PythonSandboxClient.IsConfigured())
        {
            var capture = includeGeneratedArtifacts
                          && PythonSandboxClient.ArtifactCaptureConfigured();
            var runPython = Prop("run_python",
                "Run Python in the instance sandbox. Every run_python call must be a step in a standalone propose_plan and waits for user confirmation; never call it directly in the chat loop because code can make side-effecting requests. The visible plan description must state what the code will do. For broker-enabled Marketplace connections, use only declared `broker` capabilities: `client.shop_request` when `get=true`, or discover named read-only POST operations with `client.shop_operations` and call `client.shop_read` when `operations=true`; do not make direct seller HTTP calls for those connections. Only connections marked `access=legacy` retain the old host/auth descriptor. Read `client.marketplace_doc` before choosing seller-specific paths. Do not print credentials, fetch documentation URLs yourself, or write to seller APIs from Python. Cabinet requests use Agent HTTP, never a database URL."
                + (capture
                    ? " Optional outputs declares up to four files as {fileName, contentType}; write each using client.output_path(fileName). Capture returns metadata and a server-owned temporary downloadUrl, available to the same user in the selected store until expiresAtUtc (24 hours after capture). The cabinet renders the download control; never invent a link, print file bytes/base64, or rerun computation merely to download. Download-only creates no permanent store/product file. Saving permanently still requires a later separate import_generated_file plan with the exact returned artifact metadata and destination."
                    : ""),
                Req(["code"]), ("code", Str("Python source. " + PythonSdkContract.Instructions)));
            if (capture)
            {
                runPython.input_schema.properties!["outputs"] = new AiToolProperty
                {
                    type = "array",
                    description = "Optional declared outputs: up to four files. fileName is a strict basename (at most 180 UTF-16 code units and 255 UTF-8 bytes), contentType a single media type (default application/octet-stream); no paths.",
                    items = new AiToolProperty
                    {
                        type = "object",
                        properties = new Dictionary<string, AiToolProperty>
                        {
                            ["fileName"] = Str("Strict basename of the declared output; at most 180 UTF-16 code units and 255 UTF-8 bytes"),
                            ["contentType"] = Str("Single media type; default application/octet-stream"),
                        },
                        required = ["fileName"],
                    },
                };
            }
            tools.Add(runPython);
        }

        if (includeGeneratedArtifacts)
            tools.Add(ImportGeneratedFileTool());

        tools.AddRange(
        [
        Prop("platform_request", "Call the same cabinet JSON API as the SPA (method+path+optional JSON body). Secrets and a few irreversible routes are denied. Permissions and quotas are the signed-in user's. Writes require propose_plan first.",
            Req(["method", "path"]),
            ("method", Str("GET/POST/PATCH/DELETE")),
            ("path", Str("e.g. /products?startIndex=0&pageSize=50, /marketplace/connections/1/products/export-batch, /categories/5/attributes")),
            ("body", new AiToolProperty { type = "object", description = "JSON body for writes" })),
        Prop("create_file_download_link", "Prepare a short-lived private HTTP download for a product file. Returns filename, size, SHA-256, expiry, and a download URL; the file bytes never go through MCP, JSON, or the model. Use this instead of platform_request GET /files/{id}/download. The URL supports Range/resume and does not change file visibility.",
            Req(["fileId"]), ("fileId", Int("Product file id from this store"))),
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

    /// <summary>
    /// The import tool's contract needs nullable integers, which the legacy
    /// <see cref="AiToolProperty"/> DTO cannot express; the exact JSON Schema rides
    /// in <see cref="AiToolInputSchema.RawSchema"/> (authoritative for providers),
    /// while a DTO mirror keeps property enumeration working.
    /// </summary>
    private static AiTool ImportGeneratedFileTool()
    {
        const string schemaJson = """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "artifactId": { "type": "string", "description": "Opaque ID returned by run_python artifacts" },
                "sha256": { "type": "string", "description": "Exact lowercase SHA-256 returned for that artifact" },
                "length": { "type": "integer", "description": "Exact byte length returned for that artifact" },
                "expiresAtUtc": { "type": "string", "description": "Exact expiry returned for that artifact" },
                "destination": { "type": "string", "enum": ["store", "product"], "description": "store or product; never infer an ambiguous destination" },
                "productId": { "type": ["integer", "null"], "description": "Exact product ID for product destination; null for store" },
                "folderId": { "type": ["integer", "null"], "description": "Exact folder of the target store/product; explicit null for root" }
              },
              "required": ["artifactId", "sha256", "length", "expiresAtUtc", "destination", "productId", "folderId"]
            }
            """;
        using var doc = JsonDocument.Parse(schemaJson);
        var schema = new AiToolInputSchema
        {
            RawSchema = doc.RootElement.Clone(),
            required = ["artifactId", "sha256", "length", "expiresAtUtc", "destination", "productId", "folderId"],
            properties = new Dictionary<string, AiToolProperty>
            {
                ["artifactId"] = Str("Opaque ID returned by run_python artifacts"),
                ["sha256"] = Str("Exact lowercase SHA-256 returned for that artifact"),
                ["length"] = new AiToolProperty { type = "integer", description = "Exact byte length returned for that artifact" },
                ["expiresAtUtc"] = Str("Exact expiry returned for that artifact"),
                ["destination"] = Str("store or product; never infer an ambiguous destination"),
                ["productId"] = new AiToolProperty { type = "integer", description = "Exact product ID for product destination; null for store" },
                ["folderId"] = new AiToolProperty { type = "integer", description = "Exact folder of the target store/product; explicit null for root" },
            },
        };
        return new AiTool
        {
            name = GeneratedFileContract.ImportToolName,
            description = "Save one captured generated artifact to its exact store or product destination after a NEW signed plan confirmation. Use only metadata returned in artifacts by a successful run_python. Required arguments bind artifactId, sha256, length, expiresAtUtc, destination, productId, and folderId; use explicit null for root or no product. Generation and import cannot share a plan. Do not regenerate an existing artifact to save it, put file bytes/base64 in arguments, substitute a store for a product, or invent a source URL/path. Only confirmed cabinet chat executes this tool; Python and external MCP cannot import. Use only the returned downloadUrl after success.",
            input_schema = schema,
        };
    }

    private static AiToolInputSchema Req(string[] required) => new() { required = required.ToList(), properties = new() };
    private static AiToolProperty Str(string? desc = null) => new() { type = "string", description = desc };
    private static AiToolProperty Int(string? desc = null) => new() { type = "number", description = desc };
    private static AiToolProperty Bool(string? desc = null) => new() { type = "boolean", description = desc };
}
