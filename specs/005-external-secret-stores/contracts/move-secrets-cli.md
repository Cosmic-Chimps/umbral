# Contract: the `move-secrets` command

**Feature**: `005-external-secret-stores` | FR-016 to FR-019, SC-003

## Invocation

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
```

Runs in the container like `reset-credential`, with the same environment as
the running instance. It does not start a web host. It may run while the
instance is serving. Each connector is moved under a short **lease** on its
secret (a row in the `settings` table, two minutes; research R8), which the
admin screen also takes when it replaces or deletes that connector's secret.
Whichever arrives second does not write: the admin screen refuses with the
notice `connector-secret-busy` ("A secret move is working on this connector.
Try again in a minute."), and the command reports the connector as skipped
(`being edited; run move-secrets again`). Behind the lease, each connector's
switch is still guarded by its old reference, so an edit that got in anyway
wins and the connector is reported as skipped.

The target is always the store `UMBRAL__SECRET_STORE` names. To move out of a
manager, set `UMBRAL__SECRET_STORE=local` (keeping that manager's credentials
available) and run the command.

## Output

One line per connector, then a summary. Plain text, no colour, stable enough
for tests to match on the leading word.

```text
moved    github     local → aws
skipped  discord    already in aws
failed   google     local → aws: could not reach AWS Secrets Manager (check network and AWS_REGION)
warning  my-oidc    moved local → aws; the old copy could not be deleted from local: …

3 connectors: 1 moved, 1 already in place, 1 failed.
```

A connector that is being edited, or changed while it was being copied, is a
`skipped` line too (`being edited; run move-secrets again`, `changed while it was
being moved, so nothing was overwritten; run the command again`); neither
changes the exit code, and the summary counts them under "changed meanwhile".

Dry run prints `would move  <slug>  <from> → <to>` for each connector to move,
`skipped` for the rest, and the summary with "would move". It makes no call that
writes or deletes; it may read nothing at all.

No line ever contains a secret value, a locator, or a credential.

## Exit codes

| Code | When |
| --- | --- |
| 0 | Every connector moved or already in place, including "nothing to move" and every dry run whose settings are valid |
| 1 | At least one connector failed (it still references its old, readable copy) |
| non-zero, before any output line | Settings invalid: the start-refusal message of [settings.md](./settings.md) |

A `warning` line (old copy left behind) does not make the exit non-zero: the
connector is moved and nothing is lost.

## Guarantees

- Order per connector: read source → write target → read back and compare →
  switch reference with its event in one transaction → evict cache → delete
  source.
- Interrupted at any point and re-run: completes the rest; no connector is ever
  left without a readable secret; a repeated write lands on the same name in
  the target (as a new version), never as a second copy.
- Idempotent: a second run after a full success prints only `skipped` lines and
  exits 0.
