# Adapter API watch

The prompt below is what the external watch bot runs. The adapter list is `adapter-api-watch.json`, taken from `plugins/*/adapter.json` and plugin submodules in `.gitmodules`. When an adapter is added or removed, update the JSON, refresh the prompt between the markers, and give the owner that new prompt. A test fails while the prompt and the catalog disagree.

<!-- prompt -->
You watch the official API news and documentation for every Delegat marketplace adapter listed below. Report what the platform plans to change, what it marks legacy, and what it has already changed. Do not edit repositories. Do not fill a gap from memory when a page does not open.

Window: notes and documentation changes dated after the date the owner gives. If no date is given, use the last 14 days.

For each adapter, open every News URL and every Documentation URL. Read release notes, deprecation banners, shutdown dates, and the method pages for paths this adapter calls. Then open the adapter repository and search those paths. Mark each item "we call this" or "we do not call this".

Report an item when any of these is true:
- The platform named a shutdown date, a replacement, or a legacy mark.
- A field this adapter sends was moved, renamed, retyped, or made required.
- A method this adapter calls will be removed, or the docs now describe a new error for it.
- The docs say a value belongs in a different JSON field.
- A planned change will replace a method this adapter calls.

Do not report a brand-new method this adapter does not call, unless the note says it replaces one it does call.

Write the digest in Russian. One section per adapter, in the order below. For each item give the date, one of Upcoming, Legacy, Changed, or Removed, a quote from the official page, the link, whether this adapter calls that path, and the file in the adapter repository if you found one. If an adapter has no matching notes in the window, write "без изменений". If a page did not open, say which URL failed.

Do not propose a patch. Do not rewrite this prompt. The list below is the whole catalog. If an adapter you were told about is missing here, stop and say this prompt is stale.

### Wildberries (`wildberries`)
Repository: https://github.com/akhilya/delegat-adapter-wildberries
News:
- https://dev.wildberries.ru/en/release-notes
- https://dev.wildberries.ru/
Documentation:
- https://dev.wildberries.ru/
Seller Content, Prices, and Marketplace APIs. Card create and update are POST /content/v2/cards/upload and POST /content/v2/cards/update.

### Ozon (`ozon`)
Repository: https://github.com/akhilya/delegat-adapter-ozon
News:
- https://docs.ozon.ru/api/seller/#tag/News
Documentation:
- https://docs.ozon.ru/api/seller/
Seller API. Product create and update go through POST /v3/product/import. Prices, stocks, and orders use the methods in OzonApiClient.cs.

### Yandex Market (`yandex`)
Repository: https://github.com/akhilya/delegat-adapter-yandex-market
News:
- https://yandex.ru/dev/market/partner-api/doc/ru/changelog/all
Documentation:
- https://yandex.ru/dev/market/partner-api/doc/ru/
Partner API. Offers, cards, prices, and stocks use the /v2/businesses and /v2/campaigns methods in YandexMarketApiClient.cs.

### OpenCart (`opencart`)
Repository: https://github.com/akhilya/delegat-adapter-opencart
News:
- https://github.com/opencart/opencart/releases
Documentation:
- https://docs.opencart.com/
The plugin calls the Delegat bridge, not the stock OpenCart API. A release matters when it changes the extension system, catalog tables, or request routing that bridge is built on.

### WooCommerce (`woocommerce`)
Repository: https://github.com/akhilya/delegat-adapter-woocommerce
News:
- https://developer.woocommerce.com/changelog/
Documentation:
- https://developer.woocommerce.com/docs/apis/rest-api/
The plugin calls the Delegat bridge at wp-json/delegat/v1, not wc/v3 directly. A release matters when its REST, admin, or database notes would break that bridge. Posts titled What's coming are upcoming changes.

### Medusa (`medusa`)
Repository: https://github.com/akhilya/delegat-module/tree/main/plugins/Delegat.Marketplace.Medusa
News:
- https://medusajs.com/changelog
- https://github.com/medusajs/medusa/releases
Documentation:
- https://docs.medusajs.com/
Medusa 2. The plugin calls the shop bridge /admin/delegat, not the stock Admin API. A release matters when its breaking changes, or the admin product, price, inventory, and order docs, would change that bridge.

### Avito (`avito`)
Repository: https://github.com/akhilya/delegat-adapter-avito
News:
- https://developers.avito.ru/api-catalog
Documentation:
- https://developers.avito.ru/api-catalog
Avito API. OAuth client_credentials uses POST /token; account identity uses GET /core/v1/accounts/self. Listings and prices use /core/v1/items and /core/v1/items/{item_id}/update_price. Categories and listing fields use /autoload/v1/user-docs/tree and /autoload/v1/user-docs/node/{node_slug}/fields. Listing publication is an XML feed flow, not a direct item create/update endpoint.

<!-- /prompt -->
