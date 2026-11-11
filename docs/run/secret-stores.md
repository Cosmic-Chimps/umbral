# Secret stores

By default Umbral keeps each connector's client secret in `/data`, encrypted
(`UMBRAL__SECRET_STORE=local`). It can keep them in a secrets manager instead.

Where connector secrets can live (optional):

- [AWS Secrets Manager](../secret-stores/aws.md)
- [Google Cloud Secret Manager](../secret-stores/gcp.md)
- [Azure Key Vault](../secret-stores/azure.md)
- [BellaBaxter](../secret-stores/bellabaxter.md)

An external store loads the manager's client library, so an instance that uses one idles under
160 MB instead of 150 MB.

## Moving connector secrets between stores

If you change `UMBRAL__SECRET_STORE`, connectors already saved keep their
secrets where they are until you move them. Recreate the container with the new
setting (to move out of a manager, set `local` and keep that manager's
credentials), then:

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
# compose:
docker compose exec umbral /app/Umbral move-secrets
```

It prints one line per connector (`moved`, `skipped`, `failed`, `warning`) and a
summary, never a secret. It exits 1 if any connector failed; that connector
keeps its old, readable copy, and running the command again finishes the rest.
It can run while Umbral is serving: a connector you are saving or deleting at
that moment is `skipped` (**being edited; run move-secrets again**), and the
Connectors screen says **A secret move is working on this connector. Try again
in a minute.** if you touch a connector while it is being moved.
Details are in the walkthroughs listed above.
