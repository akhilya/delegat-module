# Documentation

## Control Plane (canon elsewhere)

Control Plane product, billing, Ops, Platform Link **server**, and provisioning docs live in **`delegat-module-platform`** (local: `C:\Users\ARTEM\projects\delegat-module-platform\docs\`).  
This instance repo keeps **stubs** only — do not maintain a second CP canon here.

| Stub | Points to |
|------|-----------|
| [architecture/control-plane.md](architecture/control-plane.md) | CP `docs/control-plane.md` |
| [platform/platform-link.md](platform/platform-link.md) | CP contract + instance client notes |
| [plans/2026-07-21-control-plane-mvp-brief.md](plans/2026-07-21-control-plane-mvp-brief.md) | CP `docs/mvp-brief.md` |
| [plans/2026-07-21-control-plane-init-prompt.md](plans/2026-07-21-control-plane-init-prompt.md) | (historical; CP already scaffolded) |

## Architecture (instance)

- [**saas-architecture.md**](architecture/saas-architecture.md) - Per-instance SaaS: provisioning sketch, quotas, Redis
- [**ai-assistant.md**](architecture/ai-assistant.md) - AI assistant: architecture, prompts, tools
- [**ai-system.md**](architecture/ai-system.md) - Full AI system reference

## Design canon (impeccable)

- [**PRODUCT.md**](../PRODUCT.md) - Strategic product context for agents
- [**DESIGN.md**](../DESIGN.md) - Visual system tokens and Do/Don't
- [**DESIGN.json**](../DESIGN.json) - Sidecar for live panel / tooling

## Platform (instance)

- [**capabilities.md**](platform/capabilities.md) - Current instance features and roadmap
- [**platform-link.md**](platform/platform-link.md) - Instance Platform Link **client** notes (contract in CP)
- [**product.md**](platform/product.md) - Instance UX brief
- [**releasing.md**](platform/releasing.md) - SemVer / VERSION / CHANGELOG

## API

- [**public-v1.md**](api/public-v1.md) - Partner Public API v1

## Adapters

- [**opencart.md**](adapters/opencart.md) - OpenCart bridge
- [**woocommerce.md**](adapters/woocommerce.md) - WooCommerce bridge

## Security

- [**SECURITY_AUDIT.md**](security/SECURITY_AUDIT.md)
- [**SECURITY_AUDIT_REPORT.md**](security/SECURITY_AUDIT_REPORT.md)

## Implementation plans (instance)

- [**2026-07-18-platform-test-catalog.md**](plans/2026-07-18-platform-test-catalog.md)
- [**2026-07-17-public-api-v1-design.md**](plans/2026-07-17-public-api-v1-design.md)
- [**2026-03-17-product-soft-delete.md**](plans/2026-03-17-product-soft-delete.md)
- [**2026-07-22-api-unavailable-page-design.md**](plans/2026-07-22-api-unavailable-page-design.md)
