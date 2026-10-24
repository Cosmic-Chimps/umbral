# AWS Secrets Manager

Keeps your connectors' client secrets in AWS Secrets Manager instead of in
Umbral's own encrypted store under `/data`. Umbral creates, reads and deletes
the secrets itself; you create nothing in AWS by hand except the permissions.

Back to the [README](../../README.md). Other stores:
[Google Cloud](gcp.md) · [Azure Key Vault](azure.md) ·
[BellaBaxter](bellabaxter.md).

## Before you start

- Umbral running, with at least the local store (the default). Nothing changes
  until you set `UMBRAL__SECRET_STORE`.
- An AWS account and a region to keep the secrets in (for example `eu-west-1`).
- Credentials Umbral can use: an access key for an IAM user, or an identity the
  host already has (an ECS task role, an EKS web identity, an instance profile).
- Be aware of the cost: Secrets Manager charges per stored secret per month.
  One connector is one secret.

## 1. Grant the permissions

Umbral names every secret it creates `umbral-<slug>-<8 chars>` (for example
`umbral-github-k3f7x2qa`), so you can grant access to that prefix and nothing
else. Attach this policy to the IAM user or role Umbral uses, replacing `REGION`
and `ACCOUNT`:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "secretsmanager:CreateSecret",
        "secretsmanager:PutSecretValue",
        "secretsmanager:GetSecretValue",
        "secretsmanager:DescribeSecret",
        "secretsmanager:DeleteSecret"
      ],
      "Resource": "arn:aws:secretsmanager:REGION:ACCOUNT:secret:umbral-*"
    }
  ]
}
```

If you changed `UMBRAL__SECRET_STORE_PREFIX`, use that prefix instead of
`umbral`. If you encrypt with your own KMS key rather than the default
`aws/secretsmanager` one, also allow `kms:Encrypt`, `kms:Decrypt` and
`kms:GenerateDataKey` on that key.

Nothing wider is needed: Umbral cannot list your other secrets.

## 2. Set the variables

| Variable | Value |
| --- | --- |
| `UMBRAL__SECRET_STORE` | `aws` |
| `AWS_REGION` | The region new secrets are created in. **Required.** |
| `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` | The IAM user's key, unless the host gives Umbral an identity. Add `AWS_SESSION_TOKEN` for temporary credentials. |
| `AWS_EC2_METADATA_DISABLED` | `true` anywhere that is not AWS. See below. |

Umbral reads the rest of the AWS credential chain exactly as the AWS tools do
(`AWS_PROFILE`, `AWS_WEB_IDENTITY_TOKEN_FILE`, ECS and EKS identities), so any of
them works. Settings for the prefix and the cache are in
[the last section](#the-cache-and-the-grace-period).

**Off AWS, set `AWS_EC2_METADATA_DISABLED=true`.** With no key set, the AWS
credential chain spends a second or two asking a metadata address that does not
exist before it gives up. With a key set it makes no difference, but the setting
is harmless.

In the compose file, next to the other `UMBRAL__` variables:

```yaml
    environment:
      UMBRAL__PUBLIC_ORIGIN: https://${UMBRAL_DOMAIN}
      UMBRAL__SECRET_STORE: aws
      AWS_REGION: eu-west-1
      AWS_ACCESS_KEY_ID: AKIA…
      AWS_SECRET_ACCESS_KEY: …
      AWS_EC2_METADATA_DISABLED: "true"
```

Put the key in an `.env` file or your orchestrator's secrets rather than in the
compose file itself.

## 3. First start

```bash
docker compose up -d
docker compose logs umbral
```

A healthy start has **no warning about the secret store** in the log, and the
Connectors screen shows no notice about it. A setting that is wrong stops the
start with a message naming the variable; a store that cannot be reached starts
anyway, with a warning, so a brief outage never locks you out.

Then add or open a connector and save it. The page says **Kept in AWS Secrets
Manager**, and the AWS console shows a secret named `umbral-<slug>-<8 chars>`.

## 4. Move an instance in, or out

Connectors you created before you switched stay in the local store until you
move them. With `UMBRAL__SECRET_STORE=aws` set and the container recreated:

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
# compose:
docker compose exec umbral /app/Umbral move-secrets --dry-run
```

The dry run lists what would move and writes nothing. The real run prints one
line per connector (`moved`, `skipped`, `failed` or `warning`), then a summary,
and exits 0 when nothing failed. It is safe to run again: a connector already in
AWS is `skipped`, and one that failed keeps its old, readable copy until the
next run succeeds. Each secret is read back and compared before its connector is
switched, and the old copy is deleted last.

Umbral may be serving while you move. Each connector is locked for the few
seconds its move takes: if you are saving or deleting that connector on the
Connectors screen at that moment, its line is `skipped` and says **being edited;
run move-secrets again**, and the screen in turn says **A secret move is working
on this connector. Try again in a minute.** if you try to save or delete it
during the move. A connector that changed while it was being copied is also
`skipped` (**changed while it was being moved**). Neither makes the command
exit 1; run it again for those.

To move **out**, set `UMBRAL__SECRET_STORE=local`, **keep the AWS variables**
(Umbral still needs them to read the secrets it is moving), recreate the
container and run the same command. Remove the AWS variables only afterwards.

An instance can keep working with connectors in two stores at once: each
connector remembers where its secret is.

## 5. Deleting a connector

Deleting a connector deletes its secret in AWS with a **7-day recovery
window**: the console shows it as scheduled for deletion, and you can restore it
during that time. After that it is gone. Creating a connector with the same
identifier again works straight away, because the new secret gets a new name.

If AWS refuses the delete, the connector is still deleted and Umbral shows
**The connector was deleted, but its client secret … could not be removed**
with the secret's name. Delete it by hand in the console, or anyone with access
to the account can still read it.

If you replace a secret that is still in another store (after switching
`UMBRAL__SECRET_STORE`), Umbral saves the new one here and removes the old copy
from the other store. If that removal is refused, the connector is saved and
Umbral shows **The connector was saved, but its previous secret could not be
removed from …** with the secret's name there: remove it by hand.

## What is where

| Where | What |
| --- | --- |
| AWS Secrets Manager | The client secret of each connector, one secret per connector |
| `/data/umbral.db` | The connector's other settings and a reference to where its secret is. No secret, and no AWS name you could use without the credentials. |

Two instances that use the same prefix in the same account and region share
their secrets. Give the second instance its own `UMBRAL__SECRET_STORE_PREFIX`.
A restore of `/data` needs the same AWS account to read the secrets back; see
[Your data: back it up](../../README.md#your-data-back-it-up).

## The cache and the grace period

Umbral keeps a secret in memory for `UMBRAL__SECRET_CACHE_SECONDS` (default
`300`) so that sign-ins do not each call AWS. If AWS cannot be reached when the
entry has expired, Umbral keeps using the last secret it read for a further
`UMBRAL__SECRET_GRACE_SECONDS` (default `3600`), then refuses sign-in. Set
the grace to `0` to refuse at once. A secret you replace takes effect on the next
sign-in on this instance.

## Troubleshooting

| What you see | What it means and what to do |
| --- | --- |
| `UMBRAL__SECRET_STORE is aws but AWS_REGION is not set. …` | The container did not start. Set `AWS_REGION`, for example `eu-west-1`. |
| `UMBRAL__SECRET_STORE is "…", which is not a store Umbral knows. …` | Check the spelling: `local`, `aws`, `gcp`, `azure` or `bellabaxter`. |
| `UMBRAL__SECRET_STORE_PREFIX is "…". Use 1 to 32 lowercase letters, digits or hyphens …` | Fix the prefix, or remove the variable to use `umbral`. |
| The Connectors screen shows a warning that starts **AWS Secrets Manager could not be reached.** (or **AWS Secrets Manager refused Umbral's credentials or permissions.**), and the log has a warning that starts **Secret store check at startup:** followed by the same words | Umbral started but AWS did not answer. Check the network and `AWS_REGION`; check the key has not been rotated or disabled; check the policy above is attached and its `Resource` matches your region, account and prefix. The first sentence of the warning says which: could not be reached (the network and the region) or refused the credentials or permissions (the key and the policy). Sign-ins keep working from the cache and grace period meanwhile. A refused save raises the same notice. It clears once a secret is next saved to the store successfully; no restart is needed. |
| Saving a connector says **AWS Secrets Manager refused Umbral's credentials or permissions.** and ends **The client secret was not saved and nothing was changed** | Nothing was saved. Check the policy has `CreateSecret` and `PutSecretValue` on `umbral-*`, then enter the secret again. |
| … **AWS Secrets Manager could not be reached.** and ends **The client secret was not saved and nothing was changed** | Nothing was saved. Check the network from the container to AWS, and `AWS_REGION`. If you are off AWS and it is slow, set `AWS_EC2_METADATA_DISABLED=true`. |
| Saving a connector says **AWS Secrets Manager holds a deleted secret under this connector's name. Restore it (or purge it) in the console, then save again.** | Someone deleted that connector's secret in the AWS console and it is still inside its recovery window, so its name cannot be written to. Restore it (or delete it with no recovery window), then save again. |
| A connector page shows the warning **This connector's secret is kept in AWS Secrets Manager, which this instance cannot read** | The connector's secret is in a store this instance is not set up for (you changed or removed the variables). Restore them, or set the store back, or use **Replace client secret** to save a new secret into the active store. |
| Someone signs in and sees "Sign-in didn't complete" after the AWS outage | The cache and the grace period ran out. Fix AWS access; the next sign-in works. The security log has a `secret_store_failed` event. |
| A `move-secrets` line starts with `failed` | That connector was not changed. Read the cause on the line, fix it and run the command again. |
