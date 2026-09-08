/** Same rules as FeatureFlags.IsAiEnabled (explicit true/false; unset follows CONTROL_PLANE_URL). */

const TRUTHY = /^(true|1|yes|on)$/i;

export function parseDotEnv(text) {
  const env = {};
  for (const raw of String(text || "").split(/\r?\n/)) {
    const line = raw.trim();
    if (!line || line.startsWith("#")) continue;
    const trimmed = line.startsWith("export ") ? line.slice(7).trim() : line;
    const eq = trimmed.indexOf("=");
    if (eq <= 0) continue;
    const key = trimmed.slice(0, eq).trim();
    let value = trimmed.slice(eq + 1).trim();
    if (
      (value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))
    ) {
      value = value.slice(1, -1);
    }
    env[key] = value;
  }
  return env;
}

export function isAiEnabled(env = {}) {
  const raw = String(env.AI_ENABLED ?? "").trim();
  if (!raw) return !String(env.CONTROL_PLANE_URL ?? "").trim();
  return TRUTHY.test(raw);
}

/** Compose profile that owns the Python sidecar. Empty → do not start sandbox. */
export function composeAiProfiles(env = {}) {
  return isAiEnabled(env) ? ["ai"] : [];
}

export function dockerComposeArgs({
  envFile = ".env.dev",
  action = "up",
  extra = [],
  env = {},
} = {}) {
  const fileArgs = ["compose", "--env-file", envFile];
  if (action === "down") {
    return [...fileArgs, "--profile", "ai", "down", ...extra];
  }
  const profiles = composeAiProfiles(env);
  const profileArgs = profiles.flatMap((p) => ["--profile", p]);
  return [...fileArgs, ...profileArgs, action, ...extra];
}
