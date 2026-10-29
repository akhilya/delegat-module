# Avito adapter

The Avito adapter connects through Avito API using OAuth client credentials.

## What you need

- **Client ID** (`clientId`)
- **Client secret** (`clientSecret`)

Create an API application in your Avito account and obtain its client credentials from Avito. Access to API methods depends on the application and account permissions granted by Avito. Keep the client secret private.

In Delegat, open **Marketplaces → Add connection → Avito**, enter the Client ID and Client secret, and run **Test connection**. Delegat requests an OAuth token and verifies access by reading the current Avito account.

## Available operations

- Import listing summaries (ID, title, price, category, status, and URL).
- Read Avito categories and category fields.
- Import listing prices and update prices in Avito.

## Current limitations

The adapter is experimental. The provided Avito REST API does not expose direct listing creation or editing, so full product-card publishing is not available. Avito publication uses an XML feed that Avito fetches from a publicly accessible URL; feed generation and hosting are not yet part of the adapter. The listings endpoint also returns summary information rather than full descriptions, images, or seller SKU.

Orders, stock synchronization, and public-catalog search are not supported by this adapter.

## Related

- [Marketplaces and connections](/docs/workspace/adapters)
- [Import catalog from a marketplace](/docs/guides/import-from-marketplace)
- [Avito API documentation](https://developers.avito.ru/)
