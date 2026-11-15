#!/usr/bin/env bash
# Generates the screenshots of the documentation, never by hand:
#
#   site/scripts/screenshots/run.sh
#
# It builds Umbral from this checkout (always: a stale image would show an old
# UI; it is a cache hit when nothing changed), starts it in Docker with the
# quick start's settings, claims the instance with `Umbral reset-credential`,
# and in a headless Playwright browser sets it up through the real UI and
# captures eight screens in the dark and the light theme (capture.mjs). The
# captures are then optimised with sharp (optimise.mjs) and copied to
# docs/images/. Everything runs from a clean slate and is torn down at the end,
# pass or fail: no container, no volume is left.
#
# Needs Docker with Compose and nothing else; Node runs in the browser image.
# The Compose project is named after this checkout's path, printed on the
# first line, so runs from two worktrees do not tear each other down.
# `out/` (git-ignored) keeps the unoptimised captures in out/raw. The password
# is never printed. On a failure Umbral's logs are printed.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"
here=$(pwd -P)
images="$here/../../../docs/images"

project_path=$(printf '%s' "$here" | cksum | cut -d ' ' -f 1)
export COMPOSE_PROJECT_NAME="umbral-screenshots-$project_path"
echo "== Compose project $COMPOSE_PROJECT_NAME"

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    echo "run.sh: the screenshots failed, the logs of Umbral follow" >&2
    docker compose logs --no-color umbral >&2 || true
  fi
  docker compose down -v --remove-orphans >/dev/null 2>&1 || true
  exit "$status"
}
trap cleanup EXIT

docker compose down -v --remove-orphans

rm -rf out/raw out/final
mkdir -p out

echo "== Images"
docker compose build umbral browser

echo "== Umbral"
docker compose up -d --wait umbral

ADMIN_PASSWORD=$(docker compose exec -T umbral /app/Umbral reset-credential | sed -n 's/^  New password  *//p')
if [ -z "$ADMIN_PASSWORD" ]; then
  echo "run.sh: reset-credential printed no password" >&2
  exit 1
fi
export ADMIN_PASSWORD
HOST_UID=$(id -u)
HOST_GID=$(id -g)
export HOST_UID HOST_GID

echo "== Capture and optimise"
docker compose run --rm browser

echo "== docs/images"
mkdir -p "$images"
count=0
for file in out/final/*.png; do
  cp "$file" "$images/$(basename "$file")"
  count=$((count + 1))
done
if [ "$count" -ne 16 ]; then
  echo "run.sh: expected 16 images, found $count" >&2
  exit 1
fi
echo "== $count images written to docs/images/"
