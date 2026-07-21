# Platform test catalog (Delegat)

Implementation status as of 2026-07-18:

- Harness: `backend.Tests/Infrastructure/` (`TestDbFactory`, `TestDataBuilder`, `FakeCurrentUserService`)
- Backend: **400** xUnit tests in `backend.Tests/` - all green (`dotnet test backend.Tests/Delegat.Tests.csproj`)
- Frontend E2E scaffold: `frontend/e2e/critical.spec.ts` + `frontend/playwright.config.ts` (`npm run test:e2e`)

Waves covered (domain-level + Public API helpers + ToolDispatcher smoke):
1. G00-G04, G19 - auth/JWT, quotas, settings, currency, encryption, API keys, memberships
2. G05-G08, G20 - products/categories/attributes/groups + Public API catalog logic
3. G11, G13, G15 - connections, pending sync/batches, operations
4. G09-G10, G12, G14, G16-G18 - files/storage, mappings, group links, transfer, notes, admin logs
5. G21-G24 - AI tools dispatcher, model config, adapter log cleanup, plugin smoke, Playwright critical paths
