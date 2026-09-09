import { marked } from "marked";
import DOMPurify from "dompurify";

function escapeAttr(value) {
  return String(value || "")
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;");
}

export function resolveShotTargetPath(shotRef) {
  const raw = String(shotRef || "").trim();
  const withoutProtocol = raw.replace(/^shot:/i, "").replace(/^\//, "");
  const cleaned = withoutProtocol
    .replace(/^docs-media\//, "")
    .replace(/\.(webp|png|jpe?g|gif)$/i, "");
  if (!cleaned) return "/docs-media/";
  return `/docs-media/${cleaned}.webp`.replace(/\/+/g, "/");
}

export function absolutizeDocsMedia(src, siteOrigin) {
  const origin = String(siteOrigin || "").replace(/\/$/, "");
  const raw = String(src || "").trim();
  if (/^shot:/i.test(raw)) return origin + resolveShotTargetPath(raw);
  if (raw.startsWith("/docs/media/")) {
    return origin + raw.replace(/^\/docs\/media\//, "/docs-media/");
  }
  if (raw.startsWith("/docs-media/")) return origin + raw;
  if (raw.startsWith("docs-media/")) return `${origin}/${raw}`;
  return raw;
}

export function rewriteDocsMediaInMarkdown(markdown, siteOrigin) {
  return String(markdown || "").replace(
    /(!\[[^\]]*\]\()([^)\s]+)([^)]*\))/g,
    (_, prefix, src, suffix) => prefix + absolutizeDocsMedia(src, siteOrigin) + suffix,
  );
}

export function slugFromDocsPathname(pathname) {
  const p = String(pathname || "")
    .split("?")[0]
    .replace(/\.md$/i, "")
    .replace(/\/+$/, "");
  const m = p.match(/^\/docs(?:\/en)?(?:\/(.*))?$/);
  if (!m) return null;
  const rest = m[1] || "";
  if (!rest || rest === "index") return "";
  return rest;
}

function resolveRelativeSlug(currentSlug, rel) {
  const baseParts = String(currentSlug || "")
    .split("/")
    .filter(Boolean);
  if (baseParts.length) baseParts.pop();
  const stack = [...baseParts];
  for (const part of String(rel || "").split("/")) {
    if (!part || part === ".") continue;
    if (part === "..") stack.pop();
    else stack.push(part);
  }
  return stack.join("/");
}

/**
 * @returns {{ kind: "hash" | "sheet" | "external", href?: string, slug?: string, url?: string }}
 */
export function classifyDocsHref(href, { currentSlug = "", siteOrigin } = {}) {
  const raw = String(href || "").trim();
  if (!raw || raw.startsWith("#")) return { kind: "hash", href: raw || "#" };
  if (/^(mailto:|tel:)/i.test(raw)) return { kind: "external", url: raw };
  if (/^javascript:/i.test(raw)) return { kind: "hash", href: "#" };

  const origin = String(siteOrigin || "").replace(/\/$/, "");

  if (/^https?:\/\//i.test(raw)) {
    try {
      const u = new URL(raw);
      const slug = slugFromDocsPathname(u.pathname);
      if (slug != null && origin && u.origin === origin) {
        return { kind: "sheet", slug };
      }
      return { kind: "external", url: raw };
    } catch {
      return { kind: "external", url: raw };
    }
  }

  const pathOnly = raw.split("#")[0];
  if (pathOnly === "/docs" || pathOnly.startsWith("/docs/")) {
    return { kind: "sheet", slug: slugFromDocsPathname(pathOnly) ?? "" };
  }

  if (
    raw.startsWith("/console") ||
    raw.startsWith("/register") ||
    raw.startsWith("/legal") ||
    raw.startsWith("/pricing") ||
    raw.startsWith("/company") ||
    raw.startsWith("/login") ||
    raw.startsWith("/adapters")
  ) {
    return { kind: "external", url: origin + raw };
  }

  if (raw.startsWith("/")) {
    return { kind: "external", url: origin + raw };
  }

  const noHash = pathOnly.replace(/\.md$/i, "");
  return { kind: "sheet", slug: resolveRelativeSlug(currentSlug, noHash) };
}

export function rewriteDocsHtmlHrefs(html, ctx) {
  return String(html || "").replace(/href="([^"]*)"/g, (_, href) => {
    const classified = classifyDocsHref(href, ctx);
    if (classified.kind === "sheet") {
      return `href="${escapeAttr(href)}" data-docs-slug="${escapeAttr(classified.slug)}"`;
    }
    if (classified.kind === "external") {
      return `href="${escapeAttr(classified.url)}" target="_blank" rel="noopener noreferrer"`;
    }
    return `href="${escapeAttr(classified.href || href)}"`;
  });
}

export function renderDocsMarkdown(markdown, ctx = {}) {
  const rewritten = rewriteDocsMediaInMarkdown(markdown, ctx.siteOrigin);
  const html = marked.parse(rewritten || "", { breaks: true, gfm: true });
  const withLinks = rewriteDocsHtmlHrefs(String(html), ctx);
  if (typeof window === "undefined") return withLinks;
  return DOMPurify.sanitize(withLinks, { ADD_ATTR: ["target", "rel"] });
}
