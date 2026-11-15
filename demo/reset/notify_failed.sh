#!/bin/sh
# Run by umbral-demo-reset-failed.service when a reset fails: a restore Caravana refused or
# that failed, or a run that never reached Caravana (an expired token, the golden backup
# gone). Caravana's needs-you list shows only a restore that started and failed; this covers
# the rest. With NTFY_URL set (https://ntfy.example.com/umbral-demo), the last lines of the
# reset's journal go to that topic; NTFY_TOKEN is sent as a bearer token when set.
set -u

lines=$(journalctl -u umbral-demo-reset.service -n 15 --no-pager -o cat 2>/dev/null)
echo "umbral-demo reset failed:"
echo "$lines"

if [ -z "${NTFY_URL:-}" ]; then
  echo "no NTFY_URL in /etc/umbral-demo/notify.env: not sent anywhere"
  exit 0
fi

auth=""
if [ -n "${NTFY_TOKEN:-}" ]; then
  auth="Authorization: Bearer $NTFY_TOKEN"
fi

curl -fsS --max-time 30 -H "Title: Umbral demo: the reset failed" -H "Priority: high" ${auth:+-H "$auth"} \
  --data-binary "$lines" "$NTFY_URL" >/dev/null && echo "sent to $NTFY_URL"
