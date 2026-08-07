#!/bin/sh
# Write SPA runtime config from container env (Dokploy / compose).
# Official nginx image runs scripts under /docker-entrypoint.d/ before nginx starts.
set -eu

DOCS="${DOCS_PUBLIC_URL:-https://docs.module.delegat.pro}"
# Trim trailing slashes
while [ "${DOCS%/}" != "$DOCS" ]; do
  DOCS="${DOCS%/}"
done

# Escape for a double-quoted JS string
ESCAPED=$(printf '%s' "$DOCS" | sed 's/\\/\\\\/g; s/"/\\"/g')
printf 'window.__DELEGAT_DOCS_PUBLIC_URL__="%s";\n' "$ESCAPED" \
  > /usr/share/nginx/html/runtime-config.js
