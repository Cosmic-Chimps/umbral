# Azure Key Vault

Keeps your connectors' client secrets in an Azure Key Vault instead of in
Umbral's own encrypted store under `/data`. Umbral creates, reads and deletes
the secrets itself; you create the vault and grant a role.

Back to the [README](../../README.md). Other stores: [AWS](aws.md) ·
[Google Cloud](gcp.md) · [BellaBaxter](bellabaxter.md).

## Before you start

- Umbral running, with at least the local store (the default).
- An Azure subscription and the `az` tool signed in to it.
- A way for Umbral to sign in to Azure: a service principal (you create it
  below), or an identity the host already has (a managed identity on an Azure
  VM or container app, workload identity on AKS).

The commands use `RG` for a resource group, `umbral-kv` for the vault name
(globally unique, 3–24 characters) and `westeurope` for a region. Replace them.

## 1. Create a dedicated vault

Use a vault **of its own** for Umbral. Key Vault cannot limit a role to secrets
whose name starts with something, so whatever Umbral may touch in a vault is
everything in it.

```bash
az group create --name RG --location westeurope
az keyvault create --name umbral-kv --resource-group RG \
  --location westeurope --enable-rbac-authorization true
```

`--enable-rbac-authorization true` is needed: it makes the vault use Azure roles,
which is what the next step grants. The command prints the vault's URI, such as
`https://umbral-kv.vault.azure.net/`. That is `UMBRAL__AZURE_KEY_VAULT_URI`.

## 2. Create the identity and grant the role

For a service principal (skip this if the host gives Umbral an identity, and
grant the role to that identity instead):

```bash
az ad sp create-for-rbac --name umbral-secrets --skip-assignment
```

It prints `appId`, `password` and `tenant`: those are `AZURE_CLIENT_ID`,
`AZURE_CLIENT_SECRET` and `AZURE_TENANT_ID`. The password is shown once.

Grant **Key Vault Secrets Officer** at the vault, nothing wider:

```bash
az role assignment create \
  --assignee APP_ID \
  --role "Key Vault Secrets Officer" \
  --scope $(az keyvault show --name umbral-kv --query id -o tsv)
```

The role can create, read and delete secrets in that vault. It cannot **purge**
a deleted one, which is what you want. A new role assignment can take a few
minutes to apply.

## 3. Set the variables

| Variable | Value |
| --- | --- |
| `UMBRAL__SECRET_STORE` | `azure` |
| `UMBRAL__AZURE_KEY_VAULT_URI` | The vault's address, `https://umbral-kv.vault.azure.net/`. **Required**; https, no path. |
| `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` | The service principal's values, unless the host gives Umbral an identity. |
| `AZURE_CLIENT_ID` alone | Selects a user-assigned managed identity. With none of these set, a system-assigned one is used. |
| `AZURE_FEDERATED_TOKEN_FILE` | Workload identity on AKS. |

```yaml
    environment:
      UMBRAL__PUBLIC_ORIGIN: https://${UMBRAL_DOMAIN}
      UMBRAL__SECRET_STORE: azure
      UMBRAL__AZURE_KEY_VAULT_URI: https://umbral-kv.vault.azure.net/
      AZURE_TENANT_ID: …
      AZURE_CLIENT_ID: …
      AZURE_CLIENT_SECRET: …
```

Put the secret in an `.env` file or your orchestrator's secrets rather than in
the compose file itself.

## 4. First start

```bash
docker compose up -d
docker compose logs umbral
```

A healthy start has **no warning about the secret store** in the log, and the
Connectors screen shows no notice about it. Then save a connector. The card says
**Kept in Azure Key Vault**, and `az keyvault secret list --vault-name umbral-kv`
shows `umbral-<slug>-<8 chars>`.

## 5. Move an instance in, or out

With `UMBRAL__SECRET_STORE=azure` set and the container recreated:

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
# compose:
docker compose exec umbral /app/Umbral move-secrets --dry-run
```

The dry run lists what would move and writes nothing. The real run prints one
line per connector (`moved`, `skipped`, `failed` or `warning`), then a summary,
and exits 0 when nothing failed. It is safe to run again: a connector already in
the vault is `skipped`, and one that failed keeps its old, readable copy. Each
secret is read back and compared before its connector is switched, and the old
copy is deleted last.

To move **out**, set `UMBRAL__SECRET_STORE=local`, **keep the Azure variables**,
recreate the container and run the same command. Remove them only afterwards.

## 6. Deleting a connector

Deleting a connector **soft-deletes** its secret: it is no longer readable but
stays in the vault's deleted secrets until the vault's retention period ends
(90 days by default). You can recover it with `az keyvault secret recover`.
Umbral gives every secret a new name, so creating a connector with the same
identifier again works while the old one is still soft-deleted.

Umbral cannot purge, so a deleted secret leaves the vault only when the
retention ends, or when you purge it yourself:
`az keyvault secret purge --vault-name umbral-kv --name NAME`. (A vault with
purge protection on cannot be purged before then.)

If Azure refuses the delete, the connector is still deleted and Umbral shows
**The connector was deleted, but its client secret … could not be removed** with
the secret's name. Delete it by hand, or anyone with access to the vault can
still read it.

## What is where

| Where | What |
| --- | --- |
| Key Vault | The client secret of each connector, one secret per connector |
| `/data/umbral.db` | The connector's other settings and a reference to where its secret is |

Two instances that use the same prefix in the same vault share their secrets.
Give the second its own `UMBRAL__SECRET_STORE_PREFIX`. A restore of `/data`
needs the same vault to read the secrets back; see
[Your data: back it up](../../README.md#your-data-back-it-up).

## The cache and the grace period

Umbral keeps a secret in memory for `UMBRAL__SECRET_CACHE_SECONDS` (default
`300`) so that sign-ins do not each call Azure. If Azure cannot be reached when
the entry has expired, Umbral keeps using the last secret it read for a further
`UMBRAL__SECRET_GRACE_SECONDS` (default `3600`), then refuses sign-in. Set the
grace to `0` to refuse at once.

## Troubleshooting

| What you see | What it means and what to do |
| --- | --- |
| `UMBRAL__SECRET_STORE is azure but UMBRAL__AZURE_KEY_VAULT_URI is not set. …` | The container did not start. Set it to your vault's URI, such as `https://umbral-kv.vault.azure.net/`. |
| `UMBRAL__AZURE_KEY_VAULT_URI is "…". Give your vault's address as an https URL with no path …` | Copy the **Vault URI** from the vault's overview page; remove anything after the host name. |
| `UMBRAL__SECRET_STORE is "…", which is not a store Umbral knows. …` | Check the spelling: `local`, `aws`, `gcp`, `azure` or `bellabaxter`. |
| `UMBRAL__SECRET_STORE_PREFIX is "…". Use 1 to 32 lowercase letters, digits or hyphens …` | Fix the prefix, or remove the variable to use `umbral`. |
| The Connectors screen shows a notice that the secret store cannot be reached or refused Umbral | Umbral started but Azure did not answer. Check the vault URI, the three `AZURE_` values, that the role is assigned at the vault (it can take minutes) and that the vault uses Azure roles (`--enable-rbac-authorization`). If the vault has a firewall, allow this host. The log line gives the cause. Sign-ins keep working from the cache and grace period meanwhile. |
| Saving a connector says **Azure Key Vault refused Umbral's credentials or permissions, so the client secret was not saved** | Nothing was saved. Check the **Key Vault Secrets Officer** assignment, then enter the secret again. |
| … **could not be reached, so the client secret was not saved** | Nothing was saved. Check the network from the container to the vault. |
| … **rejected the request** and the log says `soft_deleted` | A secret of that exact name is soft-deleted. Recover or purge it (step 6), or save again: Umbral picks a new name. |
| A connector card shows a warning that its secret is in a store this instance is not set up to read | Restore the variables of that store, or set the store back, or use **Replace client secret** to save a new secret into the active store. |
| A `move-secrets` line starts with `failed` | That connector was not changed. Read the cause on the line, fix it and run the command again. |
