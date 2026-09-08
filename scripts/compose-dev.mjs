import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { dockerComposeArgs, isAiEnabled, parseDotEnv } from "./ai-compose.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const [, , action = "up", ...extra] = process.argv;
const envFile = extra.includes("--prod") ? ".env" : ".env.dev";
const passthrough = extra.filter((a) => a !== "--prod");

const env = parseDotEnv(readFileSync(path.join(root, envFile), "utf8"));

function run(args) {
  return new Promise((resolve, reject) => {
    const child = spawn("docker", args, {
      cwd: root,
      stdio: "inherit",
      env: process.env,
    });
    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0) resolve();
      else reject(new Error(`docker ${args.join(" ")} exited ${code}`));
    });
  });
}

if (action === "up" && !isAiEnabled(env)) {
  try {
    await run([
      "compose",
      "--env-file",
      envFile,
      "--profile",
      "ai",
      "rm",
      "-fs",
      "sandbox",
    ]);
  } catch {
    /* sandbox was not running */
  }
}

await run(dockerComposeArgs({ envFile, action, extra: passthrough, env }));
