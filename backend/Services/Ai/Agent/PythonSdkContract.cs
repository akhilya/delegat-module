namespace Delegat.Services.Ai.Agent;

/// <summary>Model-facing contract of sandbox/python/delegat_sdk.py; keep its tests aligned.</summary>
public static class PythonSdkContract
{
    public const string Instructions = """
        PYTHON CLIENT CONTRACT (the preloaded client is DelegatClient, not requests):
        - client.sql(sql: str) returns {table, columns, rows}; read result["rows"]. SQL defaults to LIMIT 50 and clamps explicit LIMIT to 200: a SELECT without LIMIT is NOT the whole catalog. For all product rows, use ORDER BY id LIMIT 200 OFFSET n, advancing by the received count until a short page (or check COUNT(*) separately). The same read-only SQL allowlist/store scoping applies. A response with error is a failure, not an empty result.
        - client.platform(method: str, path: str, body=None) returns cabinet JSON through the bounded agent gateway. GET /products?startIndex=0&pageSize=100 returns {items, totalCount, lastPage}; the gateway caps items to 12 per response and can include itemsTruncated. Read items; advance startIndex by the number actually received, NOT the requested pageSize, until lastPage is true. Require the fetched unique count to equal totalCount for a full catalog scan. An empty page before totalCount, a repeated page, a loop/page-budget limit, missing/error fields, or inconsistent pagination is incomplete input: fail explicitly, never silently report a complete result. Do not write from Python.
        - client.search_catalog(query, limit=50), client.search_knowledge(query, limit=8), client.match_files(files), client.marketplace_doc(adapter, query=""), client.shop_request(connection_id, host, path), client.shop_operations(connection_id), client.shop_read(connection_id, operation, arguments=None) are the other public helpers.
        - There is NO client.get, client.request, client.post, requests-style session, or direct database connection. Do not guess helper names or catch AttributeError to try another invented method.
        For rule-based searches, compute the predicate over the complete fetched set and print the actual matching rows and counts. Preserve the user's exact alphabet/letter and comparison (exactly/at least/more than); do not conflate Cyrillic and Latin lookalikes. Use explicit consonant alphabets when needed: ь and ъ are not consonants. Do not count/filter names mentally or invent a result table before the script succeeds. If input/output is incomplete, report that limitation instead of claiming all matches.
        """;
}
