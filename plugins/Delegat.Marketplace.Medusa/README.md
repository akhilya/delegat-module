# Delegat.Marketplace.Medusa

Адаптер Delegat для **Medusa 2**. Каталог, цены и остатки уходят в магазин через admin-мост `/admin/delegat`.

## Credentials

| Ключ | Значение |
| --- | --- |
| `apiBaseUrl` | `http://localhost:9000/admin/delegat` |
| `apiKey` | Secret API key Medusa (`sk_…`) |
| `bulkChunkSize` | необязательно, по умолчанию 20 |

Авторизация: `Authorization: Basic base64(sk_…:)`.

## Мост в магазине

Маршруты живут в `shop/apps/backend/src/api/admin/delegat`.

- `GET /health`
- `GET /categories`
- `POST /categories/upsert`
- `GET /categories/{id}/fields`
- `POST /products/upsert`
- `POST /products/bulk`
- `GET /products/{id}`
- `POST /prices/bulk`
- `POST /stocks/bulk`

## Tests

```bash
dotnet test tests/Delegat.Marketplace.Medusa.Tests
```
