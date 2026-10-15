# Google Cloud Secret Manager

Keeps your connectors' client secrets in Google Cloud Secret Manager instead of
in Umbral's own encrypted store under `/data`. Umbral creates, reads and deletes
the secrets itself; you enable the API and grant the permissions.

Back to the [README](../../README.md). Other stores: [AWS](aws.md) ·
[Azure Key Vault](azure.md) · [BellaBaxter](bellabaxter.md).

## Before you start

- Umbral running, with at least the local store (the default).
- A Google Cloud project to keep the secrets in, and the `gcloud` tool signed in
  to it. Secret Manager needs billing enabled on the project.
- A service account for Umbral, or an identity the host already has (Cloud Run,
  GKE workload identity).

The commands below use `PROJECT_ID` (the project's ID, such as `my-homelab`) and
`umbral@PROJECT_ID.iam.gserviceaccount.com` for the service account. Replace them.

## 1. Enable the API and create the service account

```bash
gcloud services enable secretmanager.googleapis.com --project PROJECT_ID
gcloud iam service-accounts create umbral --project PROJECT_ID \
  --display-name "Umbral secret store"
```

## 2. Grant the permissions

Umbral names every secret it creates `umbral-<slug>-<8 chars>` (for example
`umbral-github-k3f7x2qa`), so you can limit it to that prefix. Google's IAM
conditions can do that for everything except **creating** a secret, so the role
has two parts: `secretmanager.secrets.create` unconditionally, and the rest
only on secrets whose name starts with the prefix.

Create two custom roles, one for create and one for the rest:

```bash
gcloud iam roles create umbralSecretCreate --project PROJECT_ID \
  --title "Umbral: create secrets" \
  --permissions secretmanager.secrets.create

gcloud iam roles create umbralSecretUse --project PROJECT_ID \
  --title "Umbral: use its secrets" \
  --permissions secretmanager.secrets.get,secretmanager.secrets.delete,secretmanager.versions.add,secretmanager.versions.access
```

Bind the first with no condition, and the second only to names with the prefix.
The condition uses the project **number**, not the ID
(`gcloud projects describe PROJECT_ID --format 'value(projectNumber)'` prints
it):

```bash
gcloud projects add-iam-policy-binding PROJECT_ID \
  --member serviceAccount:umbral@PROJECT_ID.iam.gserviceaccount.com \
  --role projects/PROJECT_ID/roles/umbralSecretCreate \
  --condition None

gcloud projects add-iam-policy-binding PROJECT_ID \
  --member serviceAccount:umbral@PROJECT_ID.iam.gserviceaccount.com \
  --role projects/PROJECT_ID/roles/umbralSecretUse \
  --condition 'expression=resource.name.startsWith("projects/PROJECT_NUMBER/secrets/umbral-"),title=umbral-prefix'
```

Creating a secret cannot be limited by name: Google reports that a prefix
condition on create does not match, which is why it is bound without one. The
service account can therefore create secrets under any name, but read, change or
delete only those starting with `umbral-`. **Test create before you rely on it**
(step 4). If you changed `UMBRAL__SECRET_STORE_PREFIX`, use that prefix in the
condition.

If you give Umbral a key file, create one now and keep it out of the image:

```bash
gcloud iam service-accounts keys create umbral-gcp-key.json \
  --iam-account umbral@PROJECT_ID.iam.gserviceaccount.com
```

Treat that file like a password. Where the host provides an identity (Cloud Run,
GKE), use it instead and skip the key.

## 3. Set the variables

| Variable | Value |
| --- | --- |
| `UMBRAL__SECRET_STORE` | `gcp` |
| `GOOGLE_CLOUD_PROJECT` | The project **ID** new secrets are created in. **Required.** |
| `GOOGLE_APPLICATION_CREDENTIALS` | Path **inside the container** to the key file, unless the host gives Umbral an identity. |

Mount the key file read-only and point the variable at it:

```yaml
    environment:
      UMBRAL__PUBLIC_ORIGIN: https://${UMBRAL_DOMAIN}
      UMBRAL__SECRET_STORE: gcp
      GOOGLE_CLOUD_PROJECT: my-homelab
      GOOGLE_APPLICATION_CREDENTIALS: /run/secrets/umbral-gcp-key.json
    volumes:
      - ./umbral-gcp-key.json:/run/secrets/umbral-gcp-key.json:ro
```

Umbral runs as UID 1654, so the file must be readable by it (`chmod 644`, or
`chown 1654:1654` on the host). Workload identity federation files work the same
way.

## 4. First start

```bash
docker compose up -d
docker compose logs umbral
```

A healthy start has **no warning about the secret store** in the log, and the
Connectors screen shows no notice about it. Then save a connector. The card says
**Kept in Google Cloud Secret Manager**, and `gcloud secrets list --project
PROJECT_ID` shows `umbral-<slug>-<8 chars>`. If saving fails with a refusal, the
role is not applied yet or does not allow create (see Troubleshooting).

## 5. Move an instance in, or out

With `UMBRAL__SECRET_STORE=gcp` set and the container recreated:

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
# compose:
docker compose exec umbral /app/Umbral move-secrets --dry-run
```

The dry run lists what would move and writes nothing. The real run prints one
line per connector (`moved`, `skipped`, `failed` or `warning`), then a summary,
and exits 0 when nothing failed. It is safe to run again: a connector already in
Google Cloud is `skipped`, and one that failed keeps its old, readable copy. Each
secret is read back and compared before its connector is switched, and the old
copy is deleted last.

To move **out**, set `UMBRAL__SECRET_STORE=local`, **keep the Google variables
and key file**, recreate the container and run the same command. Remove them
only afterwards.

## 6. Deleting a connector

Deleting a connector deletes its secret in Google Cloud **immediately**, with no
recovery window. If Google refuses the delete, the connector is still deleted
and Umbral shows **The connector was deleted, but its client secret … could not
be removed** with the secret's name. Delete it by hand (`gcloud secrets delete
NAME`), or anyone with access to the project can still read it.

## What is where

| Where | What |
| --- | --- |
| Secret Manager | The client secret of each connector, one secret per connector |
| `/data/umbral.db` | The connector's other settings and a reference to where its secret is |

Two instances that use the same prefix in the same project share their secrets.
Give the second its own `UMBRAL__SECRET_STORE_PREFIX`. A restore of `/data`
needs the same project to read the secrets back; see
[Your data: back it up](../../README.md#your-data-back-it-up).

## The cache and the grace period

Umbral keeps a secret in memory for `UMBRAL__SECRET_CACHE_SECONDS` (default
`300`) so that sign-ins do not each call Google. If Google cannot be reached when
the entry has expired, Umbral keeps using the last secret it read for a further
`UMBRAL__SECRET_GRACE_SECONDS` (default `3600`), then refuses sign-in. Set the
grace to `0` to refuse at once.

## Troubleshooting

| What you see | What it means and what to do |
| --- | --- |
| `UMBRAL__SECRET_STORE is gcp but GOOGLE_CLOUD_PROJECT is not set. …` | The container did not start. Set it to the project **ID**. |
| `UMBRAL__SECRET_STORE is "…", which is not a store Umbral knows. …` | Check the spelling: `local`, `aws`, `gcp`, `azure` or `bellabaxter`. |
| `UMBRAL__SECRET_STORE_PREFIX is "…". Use 1 to 32 lowercase letters, digits or hyphens …` | Fix the prefix, or remove the variable to use `umbral`. |
| The Connectors screen shows a warning that starts **The secret store Google Cloud Secret Manager could not be reached when Umbral last checked it** (or **refused Umbral's credentials or permissions**), and the log has a warning that starts the same way | Umbral started but Google did not answer. Check the API is enabled, the key file is mounted, readable and the right project's, billing is on, and the role is bound to the service account. The log line gives the cause. Sign-ins keep working from the cache and grace period meanwhile. The notice clears once a secret is next saved to the store successfully; no restart is needed. |
| Saving a connector says **Google Cloud Secret Manager refused Umbral's credentials or permissions, so the client secret was not saved** | Nothing was saved. The usual cause is `secretmanager.secrets.create` bound **with** a prefix condition: bind it without one, as in step 2. Then enter the secret again. |
| … **could not be reached, so the client secret was not saved** | Nothing was saved. Check the network from the container to Google, and that `GOOGLE_APPLICATION_CREDENTIALS` points at a file that exists inside the container. |
| A connector card shows the warning **This connector's secret is kept in Google Cloud Secret Manager, which this instance cannot read** | Restore the variables of that store, or set the store back, or use **Replace client secret** to save a new secret into the active store. |
| A `move-secrets` line starts with `failed` | That connector was not changed. Read the cause on the line, fix it and run the command again. |
