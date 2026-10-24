# BellaBaxter

Keeps your connectors' client secrets in a BellaBaxter environment instead of in
Umbral's own encrypted store under `/data`. If you already run BellaBaxter for
your other apps, this puts Umbral's secrets next to theirs. Umbral creates, reads
and deletes the secrets itself.

Back to the [README](../../README.md). Other stores: [AWS](aws.md) ·
[Google Cloud](gcp.md) · [Azure Key Vault](azure.md).

## Before you start

- Umbral running, with at least the local store (the default).
- A BellaBaxter account (or server) and permission to create an API key.

## 1. Give Umbral its own environment and key

Create a **project and environment of Umbral's own** in BellaBaxter, and an API
key for them. Umbral's keys all start with `UMBRAL_`, so an app that loads the
same environment with `AddBellaSecrets` would see them as configuration: a
connector's client secret would reach that app. A separate environment keeps
them out. If you do share one, rely on the `UMBRAL_` prefix and be sure that
your apps ignore it.

Copy the API key. It looks like `bax-{id}-{hex}`, and is shown once.

## 2. Set the variables

They are the same variables the BellaBaxter configuration package for .NET reads
(Frunchy uses them too), read from the **environment only**: `BellaBaxter:*`
configuration keys are not read.

| Variable | Value |
| --- | --- |
| `UMBRAL__SECRET_STORE` | `bellabaxter` |
| `BELLA_BAXTER_API_KEY` | The API key. The project and environment are taken from it. |
| `BELLA_BAXTER_ACCESS_TOKEN` | Instead of a key: an access token. It wins if both are set, and then needs the next two. |
| `BELLA_BAXTER_PROJECT` (or `BELLA_PROJECT`), `BELLA_BAXTER_ENV` (or `BELLA_ENV`) | With a token: the project's and the environment's slugs. |
| `BELLA_BAXTER_URL` | Your server, if it is not `https://api.bella-baxter.io`. https, except for localhost. |
| `UMBRAL__BELLABAXTER_PROVIDER` | The provider's slug, **only** when the environment has no secret provider or several. See below. |
| `BELLA_BAXTER_PRIVATE_KEY` | For a zero-knowledge environment. See below. |

```yaml
    environment:
      UMBRAL__PUBLIC_ORIGIN: https://${UMBRAL_DOMAIN}
      UMBRAL__SECRET_STORE: bellabaxter
      BELLA_BAXTER_API_KEY: bax-…
```

Put the key in an `.env` file or your orchestrator's secrets rather than in the
compose file itself.

**The provider.** A BellaBaxter environment keeps secrets under a provider. If
the environment has exactly one provider for secrets, Umbral uses it. If it has
none or more than one, set `UMBRAL__BELLABAXTER_PROVIDER` to the slug of the one
to use; without it the connection fails and the message names this variable.

**Zero-knowledge environments.** If the environment is zero-knowledge, set
`BELLA_BAXTER_PRIVATE_KEY` to the device's private key, the PEM of a P-256 key.
Umbral then decrypts what it reads. Umbral does not log it or show it anywhere.
*(Whether a zero-knowledge environment accepts Umbral's writes is not yet
confirmed against a real server; if saving a connector is refused there, use a
standard environment for Umbral.)*

## 3. First start

```bash
docker compose up -d
docker compose logs umbral
```

A healthy start has **no warning about the secret store** in the log, and the
Connectors screen shows no notice about it. Then save a connector. The page says
**Kept in BellaBaxter**, and the environment shows a key named
`UMBRAL_UMBRAL_<SLUG>_<8 CHARS>` (for the `github` connector,
`UMBRAL_UMBRAL_GITHUB_K3F7X2QA`): `UMBRAL_`, the prefix, the connector's slug,
and 8 characters, in capitals with hyphens as underscores.

## 4. Move an instance in, or out

With `UMBRAL__SECRET_STORE=bellabaxter` set and the container recreated:

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
# compose:
docker compose exec umbral /app/Umbral move-secrets --dry-run
```

The dry run lists what would move and writes nothing. The real run prints one
line per connector (`moved`, `skipped`, `failed` or `warning`), then a summary,
and exits 0 when nothing failed. It is safe to run again: a connector already in
BellaBaxter is `skipped`, and one that failed keeps its old, readable copy. Each
secret is read back and compared before its connector is switched, and the old
copy is deleted last.

Umbral may be serving while you move. Each connector is locked for the few
seconds its move takes: if you are saving or deleting that connector on the
Connectors screen at that moment, its line is `skipped` and says **being edited;
run move-secrets again**, and the screen in turn says **A secret move is working
on this connector. Try again in a minute.** if you try to save or delete it
during the move. A connector that changed while it was being copied is also
`skipped` (**changed while it was being moved**). Neither makes the command
exit 1; run it again for those.

To move **out**, set `UMBRAL__SECRET_STORE=local`, **keep the `BELLA_BAXTER_`
variables**, recreate the container and run the same command. Remove them only
afterwards.

## 5. Deleting a connector

Deleting a connector deletes its key from the environment. Whether BellaBaxter
keeps a recoverable copy is up to your server; Umbral does not depend on it, and
creating a connector with the same identifier again works either way.

If BellaBaxter refuses the delete, the connector is still deleted and Umbral
shows **The connector was deleted, but its client secret … could not be
removed** with the key's name. Delete it by hand in BellaBaxter, or anyone with
access to the environment can still read it.

If you replace a secret that is still in another store (after switching
`UMBRAL__SECRET_STORE`), Umbral saves the new one here and removes the old copy
from the other store. If that removal is refused, the connector is saved and
Umbral shows **The connector was saved, but its previous secret could not be
removed from …** with the secret's name there: remove it by hand.

## What is where

| Where | What |
| --- | --- |
| BellaBaxter | The client secret of each connector, one key per connector |
| `/data/umbral.db` | The connector's other settings and a reference to where its secret is |

Two instances that use the same prefix in the same environment share their
secrets. Give the second its own `UMBRAL__SECRET_STORE_PREFIX`. A restore of
`/data` needs the same environment to read the secrets back; see
[Your data: back it up](../../README.md#your-data-back-it-up).

## The cache and the grace period

Umbral keeps a secret in memory for `UMBRAL__SECRET_CACHE_SECONDS` (default
`300`) so that sign-ins do not each call BellaBaxter. If it cannot be reached
when the entry has expired, Umbral keeps using the last secret it read for a
further `UMBRAL__SECRET_GRACE_SECONDS` (default `3600`), then refuses sign-in.
Set the grace to `0` to refuse at once.

## Troubleshooting

| What you see | What it means and what to do |
| --- | --- |
| `UMBRAL__SECRET_STORE is bellabaxter but neither BELLA_BAXTER_API_KEY nor BELLA_BAXTER_ACCESS_TOKEN is set. …` | The container did not start. Set `BELLA_BAXTER_API_KEY`. |
| `BELLA_BAXTER_API_KEY does not look like a Bella Baxter API key, which has the form bax-{id}-{secret}. …` | Copy the whole key again, with nothing before or after it (no quotes, no spaces). |
| `BELLA_BAXTER_ACCESS_TOKEN is set but BELLA_BAXTER_PROJECT is not. …` (or `…_ENV`) | A token does not name a project or an environment. Set `BELLA_BAXTER_PROJECT` and `BELLA_BAXTER_ENV` to their slugs. |
| `BELLA_BAXTER_URL is "…". Set it to the https address of your Bella Baxter server …` | Use an https address. Plain http is accepted only for localhost. |
| `UMBRAL__SECRET_STORE is "…", which is not a store Umbral knows. …` | Check the spelling: `local`, `aws`, `gcp`, `azure` or `bellabaxter`. |
| `UMBRAL__SECRET_STORE_PREFIX is "…". Use 1 to 32 lowercase letters, digits or hyphens …` | Fix the prefix, or remove the variable to use `umbral`. |
| The Connectors screen shows a notice that says **BellaBaxter has more than one secret provider in this environment, or none** and names `UMBRAL__BELLABAXTER_PROVIDER`, and the log has the same text at startup | The environment has no secret provider or several. Set that variable to the provider's name and restart. Saving a connector shows the same text until you do. |
| The notice says **BellaBaxter has no project, environment or provider by the configured name** | `BELLA_BAXTER_PROJECT`, `BELLA_BAXTER_ENV` or `UMBRAL__BELLABAXTER_PROVIDER` names something BellaBaxter does not have. Fix the one that is wrong and restart. |
| The Connectors screen shows a warning that starts **BellaBaxter could not be reached.** (or **BellaBaxter refused Umbral's credentials or permissions.**), and the log has a warning that starts **Secret store check at startup:** followed by the same words | Umbral started but BellaBaxter did not answer. Check `BELLA_BAXTER_URL`, that the key has not been revoked, and that it may write to this environment. The first sentence of the warning says which: could not be reached, or refused the credentials or permissions. Sign-ins keep working from the cache and grace period meanwhile. A refused save raises the same notice. It clears once a secret is next saved to the store successfully; no restart is needed. |
| Saving a connector says **BellaBaxter refused Umbral's credentials or permissions.** and ends **The client secret was not saved and nothing was changed** | Nothing was saved. Check the key's permissions on the environment, then enter the secret again. |
| … **BellaBaxter could not be reached.** and ends **The client secret was not saved and nothing was changed** | Nothing was saved. Check the network from the container to BellaBaxter. |
| A connector page shows the warning **This connector's secret is kept in BellaBaxter, which this instance cannot read** | Restore the variables of that store, or set the store back, or use **Replace client secret** to save a new secret into the active store. |
| A `move-secrets` line starts with `failed` | That connector was not changed. Read the cause on the line, fix it and run the command again. |
