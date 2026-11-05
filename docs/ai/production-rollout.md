# Assistant and knowledge-search production rollout

This runbook prepares an existing instance for the assistant fixes and optional hybrid documentation retrieval. It does not authorize a deployment, marketplace export/import, or a fleet-wide setting change. The release procedure remains [Releasing](../platform/releasing.md).

## Release contract

- Ship the assistant fixes, documentation improvements, and text-only retrieval optimization in the normal API/frontend images. **They work in production without Ollama.**
- The API applies the generated additive migration `20261102191230_AddKnowledgeEmbeddings` on startup. It adds two nullable columns to `aiKnowledgeChunks`; no catalog table or existing row is removed by this migration. Take the normal database backup before updating. Startup documentation reindexing rebuilds the global knowledge rows, not the product catalog.
- Hybrid retrieval is a deployment option: `AI_KNOWLEDGE_VECTOR_ENABLED=true`. The production Compose file now forwards it and provides the private `rag` Ollama service. No model container or model download is required for a lexical deployment.
- This topology is **instance-local**, not a shared multi-instance model service. A shared inference service is technically possible but is a separate, deferred architecture/security decision. The current client accepts only loopback or the `ollama` Docker hostname and does not follow redirects.

Use the exact release/image tag approved for the rollout (`IMAGE_TAG`); avoid mixing API/frontend tags. Do not create a second version source or publish a release by manually creating a tag. A change to root `VERSION` on `main` invokes the repository's release workflow. Keep this change under `Unreleased` until the release is actually cut.

For Control Plane-managed instances, provisioning/topology belongs to the sibling platform repository; this Compose file is not a fleet provisioning change. Use the same API environment/model contract through the managed deployment's supported configuration. Do not assume an instance image update creates an Ollama service across the fleet. Ordinary lexical/assistant fixes can be released independently of that optional topology.

## Deploy the ordinary fixes

Keep `AI_KNOWLEDGE_VECTOR_ENABLED=false` (the default), use the existing production secrets and stack identity, and follow the normal image update procedure. For an already running Compose stack:

```sh
docker compose -f docker-compose.prod.yml pull delegat-api frontend
docker compose -f docker-compose.prod.yml up -d --no-deps delegat-api frontend
docker compose -f docker-compose.prod.yml ps delegat-api frontend
docker compose -f docker-compose.prod.yml logs --since=10m --tail=200 delegat-api
```

Do not run these against the development `.env.dev`. Ensure the intended production `.env`/project is selected. Confirm the expected `/api/health` version, successful migrations and knowledge reindexing, and normal sign-in. No export/import or plan execution is needed to verify this release.

## Enable hybrid on a selected instance

### Configure and prepare the model

Keep the flag **false while preparing**. In that instance's deployment environment set:

```dotenv
AI_ENABLED=true
AI_KNOWLEDGE_VECTOR_ENABLED=false
AI_KNOWLEDGE_OLLAMA_URL=http://ollama:11434
AI_KNOWLEDGE_EMBEDDING_MODEL=embeddinggemma:retrieval-v1
OLLAMA_IMAGE=ollama/ollama:0.40.13
OLLAMA_CONTAINER_NAME=delegat-YOUR_STACK-ollama
OLLAMA_VOLUME_NAME=delegat-YOUR_STACK-ollama-data
OLLAMA_CPUS=1.0
OLLAMA_MEM_LIMIT=2g
```

Use unique container/volume names for the actual stack. Recreate the API to apply the model settings while keeping the flag false. The sidecar has no published port, only the instance Docker network; it limits CPU, memory, swap, parallelism, queue size, and loaded models. These are container limits, **not proof that a particular host has sufficient free memory**. Budget for the API, database, other services, and the model together. The local GPU measurements are not CPU-only server sizing.

Start only the explicitly selected model service, then download and freeze the tested weights under the configured alias:

```sh
docker compose -f docker-compose.prod.yml --profile rag up -d --no-deps ollama
docker compose -f docker-compose.prod.yml --profile rag exec -T ollama ollama pull embeddinggemma
docker compose -f docker-compose.prod.yml --profile rag exec -T ollama ollama cp embeddinggemma embeddinggemma:retrieval-v1
docker compose -f docker-compose.prod.yml --profile rag exec -T ollama ollama list
```

Record the image tag and model ID/digest from `ollama list`. Do not replace the weights behind an existing configured alias: use a new alias and change `AI_KNOWLEDGE_EMBEDDING_MODEL` when updating the model, which triggers re-embedding instead of reusing incompatible vectors. Do not copy over `retrieval-v1` on later restarts. The persistent volume keeps it; neither Compose nor the API automatically pulls models. The model health check verifies the configured alias exists, not that inference is already warm.

Warm and verify inference from inside the API container before enabling it (the API image includes `curl`):

```sh
docker compose -f docker-compose.prod.yml exec -T delegat-api sh -c 'curl --fail --silent --show-error --max-time 120 http://ollama:11434/api/embed -H "Content-Type: application/json" -d "{\"model\":\"$AI_KNOWLEDGE_EMBEDDING_MODEL\",\"input\":\"task: search result | query: documentation health check\",\"truncate\":false}" >/dev/null'
```

This sends only a synthetic documentation query. An inference failure is a reason to keep lexical mode, not to change marketplace credentials or open the model port to the internet.

### Activate and verify

Set `AI_KNOWLEDGE_VECTOR_ENABLED=true`, then recreate **only the selected API**:

```sh
docker compose -f docker-compose.prod.yml up -d --no-deps delegat-api
docker compose -f docker-compose.prod.yml logs --since=10m --tail=200 delegat-api
```

The API reindexes docs and embeds missing/changed chunks in the background. An HTTP 200 health response alone does not prove vectors are ready. Check the isolated global knowledge rows for the configured alias; for the example above:

```sql
SELECT COUNT(*) AS chunks,
       SUM(embeddingModel = 'embeddinggemma:retrieval-v1' AND embeddingJson IS NOT NULL) AS embedded
FROM aiKnowledgeChunks
WHERE storeId IS NULL;
```

The counts should be nonzero and equal after indexing completes. Use an authorized read-only SQL client; never print database credentials into rollout logs. Missing vectors and provider failures leave lexical search available. Query embeddings have a three-second budget; indexing batches retain the separate twenty-second HTTP timeout. A cold model may cause a lexical response rather than blocking chat until it loads.

The process keeps one shared parsed-vector snapshot, not one copy per request. Reindexing and successful embedding batches invalidate it; it also expires after one minute. A replica or a manual database edit can therefore take up to one minute to become visible to that process. Catalog facts never use this cache.

Smoke-test documentation questions in both supported UI languages, without confirming any plan or modifying catalog data. For example, ask about the documented lifetime/privacy of a product-file download link without creating one. Check that questions still receive supported answers and normal chat latency. The [evaluation record](assistant-quality-evaluation.md) documents known answer limitations; enabling embeddings is not a promise of perfect factual accuracy.

## Rollback

Set `AI_KNOWLEDGE_VECTOR_ENABLED=false` and recreate the API with the same image/tag and original instance settings:

```sh
docker compose -f docker-compose.prod.yml up -d --no-deps delegat-api
docker compose -f docker-compose.prod.yml --profile rag stop ollama
```

The assistant then uses lexical search without calling Ollama; the ordinary fixes remain active. Keep the model volume and nullable embedding columns. **Do not run `down -v`, remove database volumes, delete conversations, or revert the EF migration** to turn hybrid off. An application-image rollback follows the normal release procedure; do not downgrade the database merely to disable a retrieval option.

## Preparation checks

`npm run check:ai-production` renders production Compose with placeholder secrets and checks private networking, opt-in defaults, resource limits, model readiness, and environment forwarding. CI and the release workflow run it without starting containers or downloading a model. Backend tests cover cache refresh/concurrency/cancellation and lexical fallback. These gates verify packaging and regression safety; actual deployment and host-specific smoke checks remain operator actions.
