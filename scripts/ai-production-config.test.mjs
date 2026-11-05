import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
function productionConfig(overrides = {}, enableProfiles = true) {
  const environment = { ...process.env,
    COMPOSE_PROFILES: "",
    MYSQL_DATABASE: "config_test", MYSQL_USER: "config_test", MYSQL_PASSWORD: "not-a-secret",
    MYSQL_ROOT_PASSWORD: "not-a-secret", MINIO_ACCESS_KEY: "config_test", MINIO_SECRET_KEY: "not-a-secret",
    MINIO_BUCKET: "config-test", JWT_KEY: "not-a-secret", JWT_ISSUER: "config_test", JWT_AUDIENCE: "config_test",
    ENCRYPTION_KEY: "not-a-secret", CORS_ORIGINS: "https://example.invalid",
    AI_KNOWLEDGE_VECTOR_ENABLED: "", AI_KNOWLEDGE_OLLAMA_URL: "", AI_KNOWLEDGE_EMBEDDING_MODEL: "",
    OLLAMA_IMAGE: "", OLLAMA_CONTAINER_NAME: "", OLLAMA_VOLUME_NAME: "", OLLAMA_CPUS: "", OLLAMA_MEM_LIMIT: "",
    ...overrides };
  const result = spawnSync("docker", ["compose", "--env-file", ".env.example", "-f", "docker-compose.prod.yml",
    ...(enableProfiles ? ["--profile", "rag", "--profile", "ai"] : []), "config", "--format", "json"],
  { cwd: root, env: environment, encoding: "utf8", timeout: 30_000, windowsHide: true });
  assert.equal(result.error, undefined, "Docker Compose CLI must be available for deployment validation");
  assert.equal(result.status, 0, result.stderr);
  return JSON.parse(result.stdout);
}

test("ordinary production startup does not require or start the model service", () => {
  const config = productionConfig({}, false);
  assert.equal(config.services["delegat-api"].environment.AI_KNOWLEDGE_VECTOR_ENABLED, "false");
  assert.equal(config.services.ollama, undefined);
});

test("production knowledge defaults stay lexical and the model stays private and opt-in", () => {
  const config = productionConfig();
  const api = config.services["delegat-api"];
  const model = config.services.ollama;
  assert.equal(api.environment.AI_KNOWLEDGE_VECTOR_ENABLED, "false");
  assert.equal(api.environment.AI_KNOWLEDGE_OLLAMA_URL, "http://ollama:11434");
  assert.equal(api.environment.AI_KNOWLEDGE_EMBEDDING_MODEL, "embeddinggemma");
  assert.deepEqual(model.profiles, ["rag"]);
  assert.equal(model.ports, undefined);
  assert.equal(model.image, "ollama/ollama:0.40.13");
  assert.equal(model.mem_limit, "2147483648");
  assert.equal(model.memswap_limit, model.mem_limit);
  assert.equal(model.environment.OLLAMA_MAX_LOADED_MODELS, "1");
  assert.equal(model.environment.OLLAMA_MAX_QUEUE, "8");
  assert.equal(api.depends_on.ollama.required, false);
  assert.deepEqual(model.healthcheck.test, ["CMD", "ollama", "show", "embeddinggemma"]);
  assert.equal(model.volumes[0].source, "ollama_data");
});

test("production knowledge settings and model readiness follow explicit deployment configuration", () => {
  const config = productionConfig({ AI_KNOWLEDGE_VECTOR_ENABLED: "true",
    AI_KNOWLEDGE_EMBEDDING_MODEL: "embeddinggemma:retrieval-v1",
    OLLAMA_CONTAINER_NAME: "test-instance-ollama", OLLAMA_VOLUME_NAME: "test-instance-models" });
  assert.equal(config.services["delegat-api"].environment.AI_KNOWLEDGE_VECTOR_ENABLED, "true");
  assert.equal(config.services["delegat-api"].environment.AI_KNOWLEDGE_EMBEDDING_MODEL, "embeddinggemma:retrieval-v1");
  assert.equal(config.services.ollama.container_name, "test-instance-ollama");
  assert.deepEqual(config.services.ollama.healthcheck.test, ["CMD", "ollama", "show", "embeddinggemma:retrieval-v1"]);
  assert.equal(config.volumes.ollama_data.name, "test-instance-models");
});
