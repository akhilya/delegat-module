import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      // Hugeicons (CP kit) behind lucide-compatible names — see src/lib/icons.js
      "lucide-vue-next": path.resolve(__dirname, "./src/lib/icons.js"),
    },
  },
  preview: {
    allowedHosts: ["dashboard.your-domain.com"],
  },
  server: {
    host: "0.0.0.0",
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
      "/hubs": {
        target: "http://localhost:5000",
        changeOrigin: true,
        ws: true,
      },
      "/swagger": {
        target: "http://localhost:5000",
        changeOrigin: true,
      },
    },
  },
});
