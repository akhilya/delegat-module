#!/bin/sh
# Reset superadmin credentials to the values currently in ADMIN_EMAIL / ADMIN_USERNAME / ADMIN_PASSWORD env vars.
#
# Run from the host machine:
#
#   ./scripts/reset-admin.sh [container-name]
#
# Default container name: delegat-api
# The container must be running and ADMIN_EMAIL / ADMIN_USERNAME / ADMIN_PASSWORD must be set in its environment.

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

# Show which credentials will be applied (from the container's own env)
ADMIN_EMAIL="$(docker exec "$CONTAINER" sh -c 'echo "$ADMIN_EMAIL"')"
ADMIN_USERNAME="$(docker exec "$CONTAINER" sh -c 'echo "$ADMIN_USERNAME"')"

if [ -z "$ADMIN_EMAIL" ] || [ -z "$ADMIN_USERNAME" ]; then
    echo "Error: ADMIN_EMAIL or ADMIN_USERNAME is not set in container '$CONTAINER'." >&2
    exit 1
fi

echo "Container : $CONTAINER"
echo "Email     : $ADMIN_EMAIL"
echo "Username  : $ADMIN_USERNAME"
echo "Password  : (from ADMIN_PASSWORD env var)"
echo ""
printf "Reset admin credentials? [y/N] "
read -r CONFIRM
case "$CONFIRM" in
    y|Y) ;;
    *) echo "Aborted."; exit 0 ;;
esac

echo ""
echo "Running reset..."
docker exec "$CONTAINER" dotnet Delegat.dll --reset-admin
