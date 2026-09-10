import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..");

/** Keys Vite may take from repo-root `.env.dev` / `.env` (docs only — do not override VITE_API_SERVER). */
const REPO_ENV_ALLOWLIST = new Set(["DOCS_PUBLIC_URL", "VITE_DOCS_PUBLIC_URL"]);

/** Load allowlisted KEY=VAL from repo-root env files into process.env (do not override existing). */
function loadRepoEnvFiles() {
  for (const name of [".env.dev", ".env"]) {
    const file = path.join(repoRoot, name);
    if (!fs.existsSync(file)) continue;
    const text = fs.readFileSync(file, "utf8");
    for (const line of text.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (!trimmed || trimmed.startsWith("#")) continue;
      const eq = trimmed.indexOf("=");
      if (eq <= 0) continue;
      const key = trimmed.slice(0, eq).trim();
      if (!REPO_ENV_ALLOWLIST.has(key)) continue;
      let value = trimmed.slice(eq + 1).trim();
      if (
        (value.startsWith('"') && value.endsWith('"')) ||
        (value.startsWith("'") && value.endsWith("'"))
      ) {
        value = value.slice(1, -1);
      }
      if (process.env[key] === undefined) process.env[key] = value;
    }
  }
}

loadRepoEnvFiles();

/**
 * Parent API may inject VITE_API_SERVER=http://localhost:5000 from root .env into the
 * Vite process. That overrides frontend/.env.development and breaks /api routes.
 * Bare origins get /api appended; prefer relative /api when possible in local serve.
 */
function normalizeInheritedApiServer({ command }) {
  const raw = process.env.VITE_API_SERVER;
  if (raw == null || String(raw).trim() === "") {
    if (command === "serve") process.env.VITE_API_SERVER = "/api";
    return;
  }
  const value = String(raw).trim().replace(/\/+$/, "");
  if (value.startsWith("/")) {
    process.env.VITE_API_SERVER =
      value === "/api" || value.endsWith("/api") ? value : `${value}/api`.replace(/\/{2,}/g, "/");
    return;
  }
  if (/^https?:\/\//i.test(value)) {
    try {
      const url = new URL(value);
      const path = (url.pathname || "/").replace(/\/+$/, "") || "/";
      if (path === "/" || path === "") {
        // Local absolute origin without /api — use Vite proxy instead of hard-coding :5000.
        if (command === "serve" && (url.hostname === "localhost" || url.hostname === "127.0.0.1")) {
          process.env.VITE_API_SERVER = "/api";
        } else {
          process.env.VITE_API_SERVER = `${url.origin}/api`;
        }
        return;
      }
      process.env.VITE_API_SERVER = `${url.origin}${path}`;
    } catch {
      process.env.VITE_API_SERVER = value.endsWith("/api") ? value : `${value}/api`;
    }
  }
}

function resolveDocsPublicUrl({ command }) {
  const fromEnv = String(
    process.env.DOCS_PUBLIC_URL || process.env.VITE_DOCS_PUBLIC_URL || "",
  )
    .trim()
    .replace(/\/$/, "");
  if (fromEnv) return fromEnv;
  // Local Vite / preview without explicit env → local docs-site.
  if (command === "serve" || process.env.NODE_ENV !== "production") {
    return "http://localhost:5174";
  }
  return "";
}

function docsRuntimeConfigPlugin() {
  const writeScript = (command) => {
    const docs = resolveDocsPublicUrl({ command });
    const escaped = docs.replace(/\\/g, "\\\\").replace(/"/g, '\\"');
    return `window.__DELEGAT_DOCS_PUBLIC_URL__="${escaped}";\n`;
  };

  return {
    name: "docs-runtime-config",
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const url = req.url?.split("?")[0];
        if (url !== "/runtime-config.js") return next();
        res.setHeader("Content-Type", "application/javascript; charset=utf-8");
        res.end(writeScript("serve"));
      });
    },
    generateBundle(_options, _bundle, isWrite) {
      if (isWrite === false) return;
      this.emitFile({
        type: "asset",
        fileName: "runtime-config.js",
        source: writeScript("build"),
      });
    },
  };
}

function isDevApiProxyPath(url) {
  const pathname = String(url || "").split("?")[0];
  return (
    pathname === "/api" ||
    pathname.startsWith("/api/") ||
    pathname === "/hubs" ||
    pathname.startsWith("/hubs/") ||
    pathname === "/swagger" ||
    pathname.startsWith("/swagger/")
  );
}

function createApiPortProbe(port = 5000, host = "127.0.0.1") {
  let open = false;
  let lastAt = 0;
  let inFlight = null;
  const ttlMs = 400;

  return function probe() {
    const now = Date.now();
    if (inFlight) return inFlight;
    if (now - lastAt < ttlMs) return Promise.resolve(open);

    inFlight = new Promise((resolve) => {
      let settled = false;
      const sock = net.connect({ host, port });
      const done = (value) => {
        if (settled) return;
        settled = true;
        open = value;
        lastAt = Date.now();
        sock.destroy();
        resolve(value);
      };
      sock.setTimeout(250);
      sock.once("connect", () => done(true));
      sock.once("error", () => done(false));
      sock.once("timeout", () => done(false));
    }).finally(() => {
      inFlight = null;
    });
    return inFlight;
  };
}

/** Answer 503 before the proxy when the API is not listening — avoids ECONNREFUSED log floods. */
function apiBootGatePlugin() {
  const probe = createApiPortProbe();
  return {
    name: "api-boot-gate",
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        if (!isDevApiProxyPath(req.url)) {
          next();
          return;
        }
        probe()
          .then((up) => {
            if (up) {
              next();
              return;
            }
            res.statusCode = 503;
            res.setHeader("Content-Type", "application/json");
            res.end(JSON.stringify({ error: "api_unavailable" }));
          })
          .catch(next);
      });
    },
  };
}

function apiProxy(extra = {}) {
  return {
    target: "http://localhost:5000",
    changeOrigin: true,
    configure(proxy) {
      proxy.on("error", (err, _req, res) => {
        if (err?.code !== "ECONNREFUSED") return;
        if (res && !res.headersSent && typeof res.writeHead === "function") {
          res.writeHead(503, { "Content-Type": "application/json" });
          res.end(JSON.stringify({ error: "api_unavailable" }));
        }
      });
    },
    ...extra,
  };
}

export default defineConfig(({ command }) => {
  normalizeInheritedApiServer({ command });

  // Mirror into VITE_* so import.meta.env also sees the same origin in app code.
  const docs = resolveDocsPublicUrl({ command });
  if (docs && !process.env.VITE_DOCS_PUBLIC_URL) {
    process.env.VITE_DOCS_PUBLIC_URL = docs;
  }

  return {
    plugins: [vue(), tailwindcss(), docsRuntimeConfigPlugin(), apiBootGatePlugin()],
    resolve: {
      alias: {
        "@": path.resolve(__dirname, "./src"),
        // MingCute Core Filled behind lucide-compatible names — see src/lib/icons.js
        "lucide-vue-next": path.resolve(__dirname, "./src/lib/icons.js"),
      },
    },
    build: {
      rollupOptions: {
        output: {
          manualChunks(id) {
            if (!id.includes("node_modules")) return;
            // Mapping canvas stays on its route chunk.
            if (id.includes("@vue-flow") || id.includes("@dagrejs")) return;
            if (
              id.includes("vue-router") ||
              id.includes("vuex") ||
              id.includes("vue-i18n") ||
              /[/\\]vue[/\\]/.test(id)
            ) {
              return "vue";
            }
            if (
              id.includes("reka-ui") ||
              id.includes("@vueuse") ||
              id.includes("@tanstack")
            ) {
              return "ui";
            }
            if (id.includes("@microsoft/signalr")) return "signalr";
            if (id.includes("axios")) return "http";
          },
        },
      },
    },
    preview: {
      allowedHosts: ["dashboard.your-domain.com"],
    },
    server: {
      host: "0.0.0.0",
      port: 5173,
      proxy: {
        "/api": apiProxy({
          timeout: 60 * 60 * 1000,
          proxyTimeout: 60 * 60 * 1000,
        }),
        "/hubs": apiProxy({ ws: true }),
        "/swagger": apiProxy(),
      },
    },
  };
});
