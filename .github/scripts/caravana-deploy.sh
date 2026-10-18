#!/usr/bin/env bash
# Roll the image just built out on a Caravana host and wait for the rollout.
#
# Normally this calls the app's webhook, which redeploys what Caravana has
# stored (and does nothing when the image digest has not changed). The webhook
# never chooses an image, so it cannot give an app its first definition: an app
# created in the console with only a name ("Create without deploying") answers
# 409 app_not_defined. In that one case this script defines the app once, from
# deploy/caravana-app.json, through the API. Later releases go back to the
# webhook, so edits made in Caravana's console are never overwritten by the
# file.
#
# Inputs, all environment variables (the release workflow takes them from Bella
# Baxter secrets: API_TOKEN is CARAVANA_TOKEN, WEBHOOK_TOKEN is
# CARAVANA_WEBHOOK_TOKEN, APP_ID is CARAVANA_APP_ID):
#   CARAVANA_URL     console address, for example https://caravana.example.com
#   APP_ID           the app's name in Caravana
#   WEBHOOK_TOKEN    the app's webhook token (cvwh_...)
#   API_TOKEN        an API token (cvat_...): reads the rollout, defines the app
#   IMAGE            the image without a tag, for example ghcr.io/owner/umbral
#   DIGEST           the digest that was just pushed (sha256:...); reported, not
#                    used to define the app (see define_app)
#   CARAVANA_DOMAIN  the app's domain; only needed for the first release
#   CARAVANA_BACKUP_SCHEDULE  optional, first release only: a five-field cron
#                    line in UTC, for example "0 3 * * *". When set, the app is
#                    defined with a volume backup (engine volume, keep 7). It
#                    needs a backup destination set in Caravana first.
#   DEFINITION       the app definition (default: deploy/caravana-app.json)
#   GITHUB_STEP_SUMMARY  where the job summary goes (default: /dev/null)
#   POLL_SECONDS     seconds between reads of the rollout (default: 5)
#   TIMEOUT_SECONDS  how long to wait for the rollout (default: 600)
#
# Needs: curl and jq. Tokens go to curl on stdin, never in its arguments, and
# are never printed.
set -euo pipefail

: "${CARAVANA_URL:?CARAVANA_URL is not set}"
: "${APP_ID:?APP_ID is not set}"
: "${WEBHOOK_TOKEN:?WEBHOOK_TOKEN is not set}"
: "${API_TOKEN:?API_TOKEN is not set}"
: "${IMAGE:?IMAGE is not set}"
: "${DIGEST:?DIGEST is not set}"
CARAVANA_DOMAIN="${CARAVANA_DOMAIN:-}"
CARAVANA_BACKUP_SCHEDULE="${CARAVANA_BACKUP_SCHEDULE:-}"
DEFINITION="${DEFINITION:-deploy/caravana-app.json}"
GITHUB_STEP_SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/null}"
POLL_SECONDS="${POLL_SECONDS:-5}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-600}"

base="${CARAVANA_URL%/}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
headers="$work/headers"
body="$work/body"

# The token goes to curl on stdin as a config line, not as an argument, so it
# never shows up in a process list.
caravana() {
  local token="$1"; shift
  printf 'header = "Authorization: Bearer %s"\n' "$token" |
    curl --silent --show-error --max-time 60 --config - "$@"
}

# Prints "code: message" from a Caravana error body, or the start of the body
# when it is not one.
describe_error() {
  jq -er 'select(.error) | .error | "\(.code): \(.message)"' "$1" 2>/dev/null || head -c 500 "$1"
}

summary() { echo "$1" >> "$GITHUB_STEP_SUMMARY"; }

# How long we wait, for messages: "10 minutes" or "90 seconds".
waited() {
  if [ $((TIMEOUT_SECONDS % 60)) -eq 0 ]; then
    echo "$((TIMEOUT_SECONDS / 60)) minutes"
  else
    echo "$TIMEOUT_SECONDS seconds"
  fi
}

# Defines the app from the definition file and starts its first deployment.
# Leaves the answer in $headers and $body, like the webhook call does.
define_app() {
  if [ -z "$CARAVANA_DOMAIN" ]; then
    echo "::error::App '$APP_ID' is not defined on Caravana yet (it has no services), and the first deploy needs the Bella Baxter secret CARAVANA_DOMAIN, for example auth.example.com. Set it and run the release again, or define the app in the Caravana console."
    exit 1
  fi
  if [ ! -f "$DEFINITION" ]; then
    echo "::error::App '$APP_ID' is not defined on Caravana yet and $DEFINITION does not exist. Add it, or define the app in the Caravana console."
    exit 1
  fi

  # Fill the placeholders on the JSON string values, not on the raw text.
  # The app is stored with the moving tag `latest`, not the digest: the webhook
  # redeploys the stored app only when its image resolves to a new digest, so
  # a digest stored here would make every later release a no-op. Caravana pins
  # the tag to a digest on this deploy; `latest` is the image the image job
  # pushed a moment ago.
  # A backup policy exists only in the app's definition (the console cannot
  # turn one on), so it is added here when a schedule is given.
  jq --arg image "$IMAGE:latest" --arg domain "$CARAVANA_DOMAIN" \
    --arg schedule "$CARAVANA_BACKUP_SCHEDULE" '
    walk(if type == "string"
         then (split("__IMAGE__") | join($image)) | (split("__DOMAIN__") | join($domain))
         else . end)
    | if $schedule == "" then .
      else .services[0].backup = {engine: "volume", schedule: $schedule, keep: 7}
      end' "$DEFINITION" > "$work/app.json" || {
    echo "::error::$DEFINITION is not valid JSON."
    exit 1
  }
  if ! jq -e '[.. | strings | select(contains("__IMAGE__") or contains("__DOMAIN__"))] | length == 0' \
    "$work/app.json" > /dev/null; then
    echo "::error::A placeholder in $DEFINITION was not filled."
    exit 1
  fi

  echo "App '$APP_ID' has no services yet; defining it from $DEFINITION."
  local code
  code="$(caravana "$API_TOKEN" -X POST -H 'Content-Type: application/json' \
    --data-binary "@$work/app.json" -o "$body" -D "$headers" \
    -w '%{http_code}' "$base/v1/apps/$APP_ID/deployments")" || {
    echo "::error::Could not reach $base"
    exit 1
  }
  case "$code" in
    202)
      echo "::notice::App '$APP_ID' was defined from $DEFINITION. Later releases redeploy through the webhook, so changes made in the Caravana console are kept."
      ;;
    401)
      echo "::error::Caravana answered 401 defining the app: check the Bella Baxter secret CARAVANA_TOKEN."
      exit 1
      ;;
    *)
      echo "::error::Defining the app answered $code: $(describe_error "$body")"
      exit 1
      ;;
  esac
}

echo "Calling the webhook of app '$APP_ID' at $base"
code="$(caravana "$WEBHOOK_TOKEN" -X POST -o "$body" -D "$headers" \
  -w '%{http_code}' "$base/v1/webhooks/$APP_ID")" || {
  echo "::error::Could not reach $base"
  exit 1
}

case "$code" in
  200)
    if [ "$(jq -r '.changed' "$body" 2>/dev/null)" = "false" ]; then
      echo "::notice::Caravana says the image digest has not changed; nothing was deployed."
      summary "### Caravana"
      summary "Nothing to deploy: the image digest is unchanged (\`$DIGEST\`)."
      exit 0
    fi
    echo "::error::Unexpected answer 200 from the webhook: $(head -c 500 "$body")"
    exit 1
    ;;
  202) ;;
  401)
    echo "::error::Caravana answered 401: check the Bella Baxter secrets CARAVANA_WEBHOOK_TOKEN and CARAVANA_APP_ID."
    exit 1
    ;;
  409)
    # Only "the app has no services yet" is ours to fix. Any other conflict
    # (a deploy already running, say) stays an error.
    if [ "$(jq -r '.error.code // ""' "$body" 2>/dev/null)" = "app_not_defined" ]; then
      define_app
    else
      echo "::error::The webhook answered 409: $(describe_error "$body")"
      exit 1
    fi
    ;;
  *)
    echo "::error::The webhook answered $code: $(describe_error "$body")"
    exit 1
    ;;
esac

location="$(tr -d '\r' < "$headers" | awk 'tolower($1) == "location:" { print $2 }')"
if [ -z "$location" ]; then
  echo "::error::Caravana answered 202 without a Location header."
  exit 1
fi
# Location is a path (/v1/apps/<app>/deployments/<id>); allow a full URL too.
case "$location" in http://*|https://*) url="$location" ;; *) url="$base$location" ;; esac
deployment="${location##*/}"
echo "Rollout started: deployment $deployment"

deadline=$((SECONDS + TIMEOUT_SECONDS))
last=""
status=""
while :; do
  if got="$(caravana "$API_TOKEN" -o "$body" -w '%{http_code}' "$url")"; then
    :
  else
    got="000"
  fi
  case "$got" in
    200)
      status="$(jq -r '.status' "$body")"
      if [ "$status" != "$last" ]; then echo "Status: $status"; last="$status"; fi
      case "$status" in
        healthy|failed|reverted|superseded) break ;;
      esac
      ;;
    401)
      echo "::error::Caravana answered 401 reading the deployment: check the Bella Baxter secret CARAVANA_TOKEN."
      exit 1
      ;;
    000)
      echo "::warning::Could not read the deployment; trying again."
      ;;
    *)
      echo "::error::Reading the deployment answered $got: $(describe_error "$body")"
      exit 1
      ;;
  esac
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "::error::The rollout was still '$status' after $(waited); giving up waiting."
    summary "### Caravana"
    summary "Deployment \`$deployment\` was still \`${status:-unknown}\` after $(waited)."
    exit 1
  fi
  sleep "$POLL_SECONDS"
done

# Warnings count on success too (a dropped secret, a changed meaning).
jq -r '.warnings[]? | "::warning::" + ((.field // "") | if . == "" then "" else . + ": " end) + (.message | gsub("\n"; " "))' "$body"

jobversion="$(jq -r '.jobVersion // "unknown"' "$body")"
{
  echo "### Caravana"
  echo
  echo "| | |"
  echo "| --- | --- |"
  echo "| Status | \`$status\` |"
  echo "| Deployment | \`$deployment\` |"
  echo "| Nomad job version | \`$jobversion\` |"
  echo "| Image digest | \`$DIGEST\` |"
} >> "$GITHUB_STEP_SUMMARY"

if [ "$status" != "healthy" ]; then
  message="$(jq -r '.error // "no reason given"' "$body" | tr '\n' ' ')"
  echo "::error::The rollout ended '$status': $message"
  summary ""
  summary "The rollout ended \`$status\`: $message"
  exit 1
fi
echo "Healthy: job version $jobversion"
