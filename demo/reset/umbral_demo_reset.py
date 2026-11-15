#!/usr/bin/env python3
"""The nightly reset of the Umbral demo: restore its golden snapshot through Caravana's API.

A restore of a volume backup stops Umbral, puts the backup's files back in its /data volume
and starts it again: the operator credential, the connectors and the declared app come back
as they were when the snapshot was taken, and every user, session and token made since is
gone. This script starts that restore (POST /v1/apps/{app}/backups/{backup}/restore) and
follows it in the app's backup list until it has succeeded or failed.

Once a week, after a restore that succeeded, it also takes the backup of the blank instance
(POST /v1/apps/{app}/backups): the backup policy in the AppHost never fires on its own, so no
copy is ever taken of an instance whose reset failed.

Settings, from the environment (the systemd unit loads /etc/umbral-demo/reset.env):

  CARAVANA_URL        Caravana's address, https://caravana.example.com, no trailing slash.
                      Plain http is refused unless the host is loopback: the token travels in it.
  CARAVANA_APP        The app's id: umbral-demo.
  UMBRAL_SERVICE      The service whose volume is restored: umbral.
  GOLDEN_BACKUP_ID    The golden backup's id, from the Backups tab or GET /v1/apps/{app}/backups.
  GOLDEN_LABEL        Instead of the id, once this Caravana pins backups: the label (golden).
  BACKUP_WEEKDAY      The day of the weekly backup, mon..sun; sun when unset; "never" for none.
  RESTORE_TIMEOUT     Seconds to wait for the restore, and again for the backup. 900 when unset.

The full API token (a deploy token is refused for backups) is read from the file
$CREDENTIALS_DIRECTORY/caravana-token, which systemd's LoadCredential= gives this process
alone from /etc/umbral-demo/caravana-token; CARAVANA_TOKEN in the environment is the fallback
for a run by hand.

Exit code 0 when the restore (and the backup, on its day) succeeded, 1 otherwise, with the
reason on stderr. Only the Python 3 standard library is used, so it runs on the host as it is.
"""

import datetime
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

WEEKDAYS = ("mon", "tue", "wed", "thu", "fri", "sat", "sun")


def setting(name, default=None):
    value = os.environ.get(name, "").strip()
    if value:
        return value
    if default is not None:
        return default
    fail(f"{name} is not set")


def fail(message):
    print(f"umbral-demo reset: {message}", file=sys.stderr)
    sys.exit(1)


def read_token():
    directory = os.environ.get("CREDENTIALS_DIRECTORY", "").strip()
    path = os.path.join(directory, "caravana-token") if directory else ""
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            token = f.read().strip()
        if token:
            return token
        fail(f"{path} is empty")
    return setting("CARAVANA_TOKEN")


def check_base(base):
    parts = urllib.parse.urlsplit(base)
    loopback = parts.hostname in ("localhost", "127.0.0.1", "::1")
    if parts.scheme != "https" and not (parts.scheme == "http" and loopback):
        fail(f"CARAVANA_URL must be https (the token travels in each request): {base}")
    return base.rstrip("/")


class Caravana:
    def __init__(self, base, token):
        self.base = base
        self.token = token

    def call(self, method, path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        headers = {"Authorization": f"Bearer {self.token}", "Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.status, json.load(response)
        except urllib.error.HTTPError as error:
            try:
                payload = json.load(error)
            except ValueError:
                payload = {}
            return error.code, payload
        except (urllib.error.URLError, TimeoutError, OSError) as error:
            fail(f"{method} {path}: {error}")

    def backups(self, app):
        status, listing = self.call("GET", f"/v1/apps/{app}/backups")
        if status != 200:
            fail(f"could not list the backups of {app}: {status} {describe(listing)}")
        return listing

    def follow(self, app, kind, run_id, timeout):
        """Waits until the backup or restore `run_id` ends. Returns its record."""
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            time.sleep(10)
            status, listing = self.call("GET", f"/v1/apps/{app}/backups")
            if status != 200:
                continue
            current = next((r for r in listing.get(kind, []) if r.get("id") == run_id), None)
            if current is None:
                continue
            if current.get("status") != "running":
                return current
        fail(f"{kind[:-1]} {run_id} did not end within {timeout} seconds; see the app's Backups tab")


def describe(payload):
    # Caravana's errors carry `error` (a code) and `message`; never echo more than that.
    if not isinstance(payload, dict):
        return "no detail"
    return f"{payload.get('error', '')} {payload.get('message', '')}".strip() or "no detail"


def find_golden(listing, service, backup_id, label):
    backups = [b for b in listing.get("backups", []) if b.get("service") == service]
    if backup_id:
        golden = next((b for b in backups if b.get("id") == backup_id), None)
        if golden is None:
            fail(f"backup {backup_id} of service {service} is not in the destination any more: "
                 "take a new golden snapshot and set GOLDEN_BACKUP_ID")
    else:
        golden = next((b for b in backups if b.get("pin") == label), None)
        if golden is None:
            fail(f"no backup of service {service} is pinned under {label}")
    if golden.get("status") != "succeeded":
        fail(f"backup {golden['id']} is {golden.get('status')}, not a succeeded one")
    return golden["id"]


def main():
    base = check_base(setting("CARAVANA_URL"))
    token = read_token()
    app = setting("CARAVANA_APP", "umbral-demo")
    service = setting("UMBRAL_SERVICE", "umbral")
    timeout = int(setting("RESTORE_TIMEOUT", "900"))
    weekday = setting("BACKUP_WEEKDAY", "sun").lower()
    backup_id = os.environ.get("GOLDEN_BACKUP_ID", "").strip()
    label = os.environ.get("GOLDEN_LABEL", "").strip()

    if weekday not in WEEKDAYS and weekday != "never":
        fail(f"BACKUP_WEEKDAY must be one of {', '.join(WEEKDAYS)} or never, not {weekday}")
    if not backup_id and not label:
        fail("set GOLDEN_BACKUP_ID (or GOLDEN_LABEL on a Caravana that pins backups)")

    caravana = Caravana(base, token)
    golden = find_golden(caravana.backups(app), service, backup_id, label)

    status, restore = caravana.call("POST", f"/v1/apps/{app}/backups/{golden}/restore")
    if status != 202:
        fail(f"restore of {golden} refused: {status} {describe(restore)}")
    restore_id = restore.get("id")
    print(f"umbral-demo reset: restore {restore_id} of {golden} started")

    ended = caravana.follow(app, "restores", restore_id, timeout)
    if ended.get("status") != "succeeded":
        fail(f"restore {restore_id} {ended.get('status')}: {ended.get('message', 'no detail')}")
    print(f"umbral-demo reset: restore {restore_id} succeeded")

    # The weekly copy of the blank instance, only now that the reset has succeeded.
    today = WEEKDAYS[datetime.datetime.now(datetime.timezone.utc).weekday()]
    if today != weekday:
        return
    status, backup = caravana.call("POST", f"/v1/apps/{app}/backups", {"service": service})
    if status != 202:
        fail(f"the weekly backup was refused: {status} {describe(backup)}")
    backup_run = backup.get("id")
    print(f"umbral-demo reset: weekly backup {backup_run} started")
    ended = caravana.follow(app, "backups", backup_run, timeout)
    if ended.get("status") != "succeeded":
        fail(f"weekly backup {backup_run} {ended.get('status')}: {ended.get('message', 'no detail')}")
    print(f"umbral-demo reset: weekly backup {backup_run} succeeded")


if __name__ == "__main__":
    main()
