# Avito adapter

Experimental system adapter for the Avito API.

## Implemented API

- OAuth `client_credentials` token at `POST /token`.
- Account validation and account ID at `GET /core/v1/accounts/self`.
- Paged listing summaries at `GET /core/v1/items`.
- Listing category tree and dynamic listing-field metadata through the Autoload user-docs endpoints.
- Price read from the listing summary and update through `POST /core/v1/items/{item_id}/update_price`.

The adapter intentionally does not advertise full product export, stock synchronization, orders, or public-catalog search. The provided Avito REST specifications do not expose a direct listing create/edit method. Publishing uses an XML feed that Avito fetches from a public URL; generating and hosting that feed needs a separate host-side design and category feed-format coverage.

The listing import API returns summary data (id, title, price, category, status, URL), not the full listing body, images, or seller article. The adapter does not invent those values.
