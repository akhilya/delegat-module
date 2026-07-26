# Product

> **Agent / design canon:** root [`PRODUCT.md`](../../PRODUCT.md) and [`DESIGN.md`](../../DESIGN.md) (impeccable). This file remains the longer instance UX brief; keep principles aligned with the root PRODUCT.md.

## Users

E-commerce managers and small business owners who manage product listings across Russian marketplaces (Ozon, Wildberries). Their context is operational - they work through product lists, edit attribute details, map attributes, and monitor sync/integration status. Speed and clarity are paramount; they are in the interface for sustained, repetitive catalog work, not casual visits.

## Product Purpose

A catalog-management dashboard for multi-marketplace sellers. It centralizes product listings, attribute mapping, and marketplace integrations so a seller can keep one source of truth and push consistent, correct data to each marketplace. Success looks like: fewer listing errors, faster bulk edits, and confidence that sync status reflects reality.

## Brand Personality

Professional, Organized, Trustworthy - in the spirit of Notion or Linear. Competent and reliable, never flashy or playful. The voice is plain and operational (all UI text is Russian). Users should feel their data is correct and their actions are unambiguous.

## Anti-references

- Playful, colorful, or marketing-heavy aesthetics.
- Decorative dashboards that prioritize "wow" over legibility.
- Gradient-and-glass SaaS landing-page styling applied to operational tooling.

## Design Principles

1. **Clarity over decoration** - every element serves a purpose; no ornamental UI. Structure is carried by whitespace, type weight, and layout hierarchy.
2. **Data density with breathing room** - show enough to be efficient across tables and forms, but keep comfortable spacing so the interface never feels cramped.
3. **Consistent patterns** - reuse the established component library (Card, Table, Form, Dialog) uniformly: same spacing, same interaction patterns, same visual weight across pages.
4. **Semantic color only** - neutral palette by default; reserve color for status and actions (destructive / warning / success) so attention stays on content, not chrome.
5. **Accessible by default** - WCAG AA: sufficient contrast, visible focus states, proper ARIA, full keyboard navigation.

## Accessibility & Inclusion

Target WCAG 2.1 AA. Sufficient contrast ratios in both light and dark themes, visible focus indicators, proper ARIA roles/states on interactive elements, and keyboard navigation throughout. Dark mode is fully supported via the `.dark` class with OKLCH variable swapping.
