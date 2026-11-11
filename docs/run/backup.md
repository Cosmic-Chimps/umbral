# Your data: back it up

> **Everything Umbral keeps is in one directory, `/data`. If you lose it, you
> lose every connector secret and every app secret, and every issued token stops
> working.** You would re-enter each provider's client secret, regenerate each
> app's secret and update every app. (Umbral keeps only a hash of an app's
> secret, so it cannot be recovered from anywhere.) Back it up, and test that you can restore
> it.

The same copy is how you move Umbral to another machine, or rebuild it after
losing one. Start it on the copy with the same `UMBRAL__PUBLIC_ORIGIN`: the
tokens and the redirect addresses you registered with each provider depend on
it. App secrets come along, people stay signed in, and nothing else needs
re-entering.

What is in there:

| Path | What |
| --- | --- |
| `umbral.db` | SQLite database: connectors, users, apps, each app's sign-in logo and colour, the operator's passkeys (public keys, which are not secrets) and whether password sign-in is on, the security log. (While running you will also see `umbral.db-wal` and `umbral.db-shm`: they belong with it.) |
| `keys/` | Token signing and encryption keys, and the key ring that encrypts the connectors' client secrets kept here |

**Back up** = copy the directory. **Stop Umbral first**: SQLite keeps recent
writes in the `-wal` file, and a copy taken mid-write can be inconsistent. A
clean stop folds everything into `umbral.db`.

With the quick-start `docker run` container:

```bash
docker stop umbral
mkdir -p backup
docker run --rm --user root --entrypoint cp \
  -v umbral-data:/data:ro -v "$PWD/backup":/backup umbral -a /data/. /backup/
docker start umbral
```

With compose (run in the checkout):

```bash
docker compose stop umbral
mkdir -p backup
docker compose run --rm --no-deps --user root --entrypoint cp \
  -v "$PWD/backup":/backup umbral -a /data/. /backup/
docker compose start umbral
```

**Restore** is the same copy in the other direction. Umbral runs as a non-root
user (UID 1654), so the files must be owned by it:

```bash
docker stop umbral        # compose: docker compose stop umbral
docker run --rm --user root --entrypoint sh \
  -v umbral-data:/data -v "$PWD/backup":/backup:ro umbral \
  -c 'rm -rf /data/* && cp -a /backup/. /data/ && chown -R 1654:1654 /data'
docker start umbral
```

With compose:

```bash
docker compose stop umbral
docker compose run --rm --no-deps --user root --entrypoint sh \
  -v "$PWD/backup":/backup:ro umbral \
  -c 'rm -rf /data/* && cp -a /backup/. /data/ && chown -R 1654:1654 /data'
docker compose start umbral
```

If you use a bind mount (`-v ./data:/data`) instead of a named volume, copy the
host directory with `cp -a` and make it writable for UID 1654 first:
`chown -R 1654:1654 ./data`.

On Linux the copied files are owned by root; use `sudo` to move or read them.

**Treat the backup like a password database.** The keys that decrypt the stored
connector secrets are in the same directory, so anyone who holds a copy of
`/data` holds every secret in it.

**With an external secret store, the connector secrets are not in `/data`.**
If you set `UMBRAL__SECRET_STORE` to `aws`, `gcp`, `azure` or `bellabaxter`
(see the [walkthroughs](../secret-stores/aws.md)), the backup holds only a
reference to each secret, so a restore needs the same manager, reachable with
the same credentials, to read them back. Two instances that use the same
`UMBRAL__SECRET_STORE_PREFIX` in the same manager share their secrets: give the
second one its own prefix.

## Upgrading

The database is at **schema version 10**. Umbral upgrades an older database by
itself when it starts, and nothing changes until you use what the upgrade
added: for example, every app starts with no branding, nobody can use a
passkey until you enable the passkey connector, and the operator has no
passkey, and password sign-in stays on until you turn it off under
**Credential**. Version 10 is of this kind: it adds two empty columns for the
Apple connector's Team ID and Key ID, and nobody can sign in with Apple until you
set that connector up. The one exception is version 9: it sets `email_verified` back
to `false` for every user whose stored address no provider verifies, and the
log says how many (see the 0.8.0 notes in the [CHANGELOG](../../CHANGELOG.md)). It does not work the other way: an older
image refuses to start on a newer database, and says so in `docker logs umbral`
(*...written by a newer Umbral. Run the newer image, or restore the backup of
the data directory taken before the upgrade.*). Take a
[backup](#your-data-back-it-up) before you upgrade, and you can always go back.
