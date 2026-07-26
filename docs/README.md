# Documentation

## Architecture

- [**control-plane.md**](architecture/control-plane.md) - Central platform (канон временно здесь; переезд в `delegat-module-platform` — см. MVP brief §0)
- [**saas-architecture.md**](architecture/saas-architecture.md) - Per-instance SaaS: provisioning, scaling, quotas, Redis, Registry/Provisioner draft
- [**ai-assistant.md**](architecture/ai-assistant.md) - AI assistant: architecture, prompts, tools, rollback, tariff access
- [**ai-system.md**](architecture/ai-system.md) - Full AI system reference (RAG-oriented)

## Design canon (impeccable)

- [**PRODUCT.md**](../PRODUCT.md) - Strategic product context for agents (`register: product`)
- [**DESIGN.md**](../DESIGN.md) - Visual system tokens and Do/Don't (OKLCH, Onest, shadcn)
- [**DESIGN.json**](../DESIGN.json) - Sidecar for live panel / tooling

## Platform

- [**capabilities.md**](platform/capabilities.md) - Current instance features and roadmap
- [**platform-link.md**](platform/platform-link.md) - Instance ↔ Control Plane m2m + Reference Catalog contract (переезжает в CP-репо)
- [**product.md**](platform/product.md) - Instance UX brief (points to root PRODUCT/DESIGN as agent canon)
- [**releasing.md**](platform/releasing.md) - SemVer / `VERSION` / `CHANGELOG.md` process for commits and releases

## API

- [**public-v1.md**](api/public-v1.md) - Partner Public API v1 (API keys, catalog, sync)

## Adapters

- [**opencart.md**](adapters/opencart.md) - OpenCart bridge: credentials, actions, payload format
- [**woocommerce.md**](adapters/woocommerce.md) - WooCommerce bridge: credentials, endpoints, PRINTO notes

## Security

- [**SECURITY_AUDIT.md**](security/SECURITY_AUDIT.md) - 2026-07-17 audit: encryption, secrets, AI integration
- [**SECURITY_AUDIT_REPORT.md**](security/SECURITY_AUDIT_REPORT.md) - 2026-07-17 audit: uploads, plugins, storage

## Implementation Plans

- [**2026-07-21-control-plane-mvp-brief.md**](plans/2026-07-21-control-plane-mvp-brief.md) - CP MVP: repo, DNS, scope, **docs migration §0**, acceptance
- [**2026-07-21-control-plane-init-prompt.md**](plans/2026-07-21-control-plane-init-prompt.md) - Prompt to scaffold `delegat-module-platform` in a new folder/repo
- [**2026-07-18-platform-test-catalog.md**](plans/2026-07-18-platform-test-catalog.md) - Platform test catalog status
- [**2026-07-17-public-api-v1-design.md**](plans/2026-07-17-public-api-v1-design.md) - Public API v1 design
- [**2026-03-17-product-soft-delete.md**](plans/2026-03-17-product-soft-delete.md) - Soft delete for products
