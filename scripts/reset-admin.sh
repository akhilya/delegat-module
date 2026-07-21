#!/bin/sh
# Break-glass: generate a new platform admin password inside the API container,
# persist it, and print JSON credentials to stdout for Control Plane / ops.
#
#   ./scripts/reset-admin.sh [container-name]
#
# Output (single line):
#   {"email":"...","username":"...","password":"..."}
#
# The running API process is not restarted. Existing JWT sessions remain valid
# until expiry; the new password applies on the next sign-in.
#
# Default container name: delegat-api

set -e

CONTAINER="${1:-delegat-api}"

if ! docker inspect "$CONTAINER" > /dev/null 2>&1; then
    echo "Error: container '$CONTAINER' not found." >&2
    exit 1
fi

STATUS="$(docker inspect -f '{{.State.Status}}' "$CONTAINER")"
if [ "$STATUS" != "running" ]; then
    echo "Error: container '$CONTAINER' is not running (status: $STATUS)." >&2
    exit 1
fi

# Prefer a one-shot exec that only runs --reset-admin (no need for ADMIN_PASSWORD).
docker exec "$CONTAINER" dotnet Delegat.dll --reset-admin
