#!/usr/bin/env bash
# Idle resident set of an Umbral image on Linux (Docker), for task b5ky (spec 015, SC-006).
# Usage: measure-idle.sh <image> <label> [bluesky]
#   With "bluesky", a Bluesky connector is stored enabled before the measured start.
# Runs on a loopback origin with the development flags, so the Bluesky connector can be
# enabled (AT Protocol's localhost client). Same settings for every image measured.
set -euo pipefail
image="$1"; label="$2"; withBluesky="${3:-}"
port=18097
data="$(mktemp -d /tmp/b5ky-idle-XXXX)"
name="b5ky-idle-$label"
env=(-e UMBRAL__PUBLIC_ORIGIN="http://127.0.0.1:$port" -e ASPNETCORE_ENVIRONMENT=Development -e UMBRAL__INSECURE_ALLOW_HTTP=true -e ASPNETCORE_URLS=http://+:8080)

start() {
  docker rm -f "$name" >/dev/null 2>&1 || true
  docker run -d --name "$name" -p "127.0.0.1:$port:8080" -v "$data:/data" "${env[@]}" "$image" >/dev/null
  for _ in $(seq 1 60); do
    curl -fsS "http://127.0.0.1:$port/healthz" >/dev/null 2>&1 && return 0
    sleep 1
  done
  docker logs "$name" | tail -20; exit 1
}

start
if [ "$withBluesky" = "bluesky" ]; then
  docker stop "$name" >/dev/null
  now="$(date -u +%Y-%m-%dT%H:%M:%S.0000000+00:00)"
  sqlite3 "$data/umbral.db" "INSERT INTO connectors (slug, kind, display_name, client_id, client_secret_ref, client_secret_hint, is_enabled, created_utc, updated_utc) VALUES ('bluesky','bluesky','Bluesky','','','',1,'$now','$now');"
  start
fi

# Touch the pipeline, the views and the database, as IdleMemoryTests does.
for path in / /healthz /signin /.well-known/openid-configuration /connect/bluesky/client-metadata.json; do
  curl -s -o /dev/null -w "$path %{http_code}\n" "http://127.0.0.1:$port$path"
done
sleep 20
rss="$(docker exec "$name" sh -c 'grep VmRSS /proc/1/status' 2>/dev/null || docker exec "$name" cat /proc/1/status | grep VmRSS)"
echo "$label: $rss"
docker exec "$name" sh -c 'grep -l -i -E "dnsclient|duende" /proc/1/maps >/dev/null 2>&1 && grep -i -o -E "[^/ ]*(DnsClient|Duende)[^/ ]*\.dll" /proc/1/maps | sort -u || echo "no DnsClient or Duende assembly mapped"'
docker rm -f "$name" >/dev/null
rm -rf "$data"
