#!/usr/bin/env bash
# Runs one sample against a real Umbral built from this repository.
#
#   samples/test/run.sh <sample>        (a name from samples.conf)
#
# It starts Umbral (docker-compose.yml builds the image when umbral:samples is
# missing; CI builds it beforehand), claims the instance with
# `Umbral reset-credential`, then in a headless Playwright browser signs in as
# the operator, adds the fake connector and registers the sample as an app
# (setup.spec.ts). It then starts the sample with the app's client ID and
# secret and walks it (walk.spec.ts): public, private, sign in with the fake
# connector, sign out. Everything starts from a clean slate (`down -v`) and is
# torn down at the end, pass or fail.
#
# Needs Docker with Compose and nothing else; Node runs in the browser image.
# No host port is published; docker-compose.debug.yml adds them for a look
# from the host (see its header).
# `out/` (git-ignored) holds the traces of a failed run, in out/test-results.
# On a failure the logs of Umbral and the sample are printed. The password and
# the app's secret are never printed, and out/app.env (the secret, for the sample)
# is deleted at the end.
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "usage: $0 <sample>" >&2
  exit 2
fi
sample=$1

cd "$(dirname "${BASH_SOURCE[0]}")"

if ! read -r SAMPLE SAMPLE_PORT APP_TYPE REDIRECT_PATH POST_LOGOUT_PATH < <(
  awk -v name="$sample" '$0 !~ /^[[:space:]]*#/ && $1 == name { print $1, $2, $3, $4, $5; exit }' samples.conf
) || [ -z "${POST_LOGOUT_PATH:-}" ]; then
  echo "run.sh: sample '$sample' is not in samples/test/samples.conf (or its line has fewer than 5 columns)" >&2
  exit 1
fi
export SAMPLE SAMPLE_PORT APP_TYPE REDIRECT_PATH POST_LOGOUT_PATH
# Compose reads these for the sample's service, before they are known.
export UMBRAL_CLIENT_ID=${UMBRAL_CLIENT_ID:-} UMBRAL_CLIENT_SECRET=${UMBRAL_CLIENT_SECRET:-}

mkdir -p out
rm -f out/app.env

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    echo "run.sh: sample '$sample' failed, the logs of Umbral and the sample follow" >&2
    docker compose logs --no-color umbral sample >&2 || true
  fi
  docker compose down -v --remove-orphans >/dev/null 2>&1 || true
  rm -f out/app.env # the app's secret
  exit "$status"
}
trap cleanup EXIT

docker compose down -v --remove-orphans

# `run` would use a browser image built from older scripts; this is a cache hit
# when nothing changed.
echo "== Browser image"
docker compose build browser

echo "== Umbral"
docker compose up -d --wait umbral

ADMIN_PASSWORD=$(docker compose exec -T umbral /app/Umbral reset-credential | sed -n 's/^  New password  *//p')
if [ -z "$ADMIN_PASSWORD" ]; then
  echo "run.sh: reset-credential printed no password" >&2
  exit 1
fi
export ADMIN_PASSWORD

echo "== Register $sample in Umbral"
docker compose run --rm browser npx playwright test setup.spec.ts

if [ ! -s out/app.env ]; then
  echo "run.sh: setup did not write out/app.env" >&2
  exit 1
fi
set -a
# shellcheck disable=SC1091 # written by setup.spec.ts
. out/app.env
set +a

echo "== Start $sample"
docker compose up -d --wait sample

echo "== Walk $sample"
docker compose run --rm browser npx playwright test walk.spec.ts

echo "== sample '$sample' passed"
