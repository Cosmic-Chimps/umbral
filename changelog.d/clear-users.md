### Added

- `docker exec umbral /app/Umbral clear-users --yes` deletes every user, with their linked
  accounts, passkeys, invitations and tokens, in the running container and without a restart.
  It keeps the connectors, apps, operator credential, settings and keys. Without `--yes` it only
  says what it would delete. Add `--security-log` to empty the security log too; either way it
  records a `users_cleared` event. Documented in "Start over: clear every user".
