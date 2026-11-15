#!/usr/bin/env bash
# What the browser container runs: capture, then optimise, then hand out/ to
# the host user. This container runs as root, so on Linux everything it writes
# belongs to root on the host, and run.sh could neither read nor delete it.
# Docker Desktop already maps ownership to the host user and may refuse the
# chown: nothing to do.
set -uo pipefail
status=0
node capture.mjs && node optimise.mjs || status=$?
if [ -n "${HOST_UID:-}" ] && [ -n "${HOST_GID:-}" ]; then
  chown -R "$HOST_UID:$HOST_GID" out 2>/dev/null || true
fi
exit "$status"
