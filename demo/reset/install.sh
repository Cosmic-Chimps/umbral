#!/bin/sh
# Installs the nightly reset on the Caravana host: a system user, the scripts, the settings
# files (from the examples, unless they exist), the token file, and the systemd units. Run it
# as root from a checkout of the Umbral repository on the host, at /opt/umbral:
#
#   sudo sh /opt/umbral/demo/reset/install.sh
#
# Then put the real values in place and run it once by hand:
#
#   sudo -e /etc/umbral-demo/reset.env              # CARAVANA_URL, GOLDEN_BACKUP_ID
#   sudo sh -c 'umask 077; printf %s "cvat_..." > /etc/umbral-demo/caravana-token'
#   sudo -e /etc/umbral-demo/notify.env             # optional: a ntfy topic for failures
#   sudo systemctl start umbral-demo-reset.service && journalctl -u umbral-demo-reset -n 20
#
# Running it again updates the scripts and the units and leaves the settings alone.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
user=umbral-demo-reset
lib=/usr/local/lib/umbral-demo
conf=/etc/umbral-demo

if [ "$(id -u)" -ne 0 ]; then
  echo "run it as root: sudo sh $0" >&2
  exit 1
fi

command -v python3 >/dev/null || { echo "python3 is needed (apt install python3)" >&2; exit 1; }
command -v curl >/dev/null || { echo "curl is needed (apt install curl)" >&2; exit 1; }

if ! id "$user" >/dev/null 2>&1; then
  useradd --system --no-create-home --shell /usr/sbin/nologin "$user"
fi

install -d -m 0755 "$lib"
install -m 0755 "$here/umbral_demo_reset.py" "$lib/umbral_demo_reset.py"
install -m 0755 "$here/notify_failed.sh" "$lib/notify_failed.sh"

install -d -m 0750 -o root -g "$user" "$conf"
if [ ! -f "$conf/reset.env" ]; then
  install -m 0400 -o "$user" -g "$user" "$here/reset.env.example" "$conf/reset.env"
  echo "wrote $conf/reset.env from the example: put the real values in it (sudo -e $conf/reset.env)"
fi
if [ ! -f "$conf/caravana-token" ]; then
  # Root only: systemd reads it and hands it to the service as a credential.
  (umask 077; printf '%s' 'cvat_replace-me' > "$conf/caravana-token")
  echo "wrote $conf/caravana-token with a placeholder: put the full API token in it"
fi
if [ ! -f "$conf/notify.env" ]; then
  install -m 0600 -o root -g root "$here/notify.env.example" "$conf/notify.env"
  echo "wrote $conf/notify.env from the example: name a ntfy topic, or leave NTFY_URL empty"
fi

install -m 0644 "$here/umbral-demo-reset.service" /etc/systemd/system/umbral-demo-reset.service
install -m 0644 "$here/umbral-demo-reset-failed.service" /etc/systemd/system/umbral-demo-reset-failed.service
install -m 0644 "$here/umbral-demo-reset.timer" /etc/systemd/system/umbral-demo-reset.timer
systemctl daemon-reload
systemctl enable --now umbral-demo-reset.timer

echo "installed. Next runs:"
systemctl list-timers umbral-demo-reset.timer --no-pager
