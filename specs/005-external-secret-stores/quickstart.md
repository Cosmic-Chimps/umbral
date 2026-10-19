# Quickstart: validating external secret stores

**Feature**: [spec.md](./spec.md) | Contracts: [settings](./contracts/settings.md),
[admin surface](./contracts/admin-surface.md), [move-secrets](./contracts/move-secrets-cli.md),
[secret store seam](./contracts/secret-store.md)

Steps 1–4 need no cloud account and are what CI runs. Steps 5–6 are the manual
checks against real managers, done once per store before calling the feature
done.

## 1. The default is untouched (FR-002, SC-004)

```bash
dotnet build -c Release
dotnet test -c Release
```

Expected: the whole existing suite passes with no setting added. The new
default-path test asserts that with no `UMBRAL__SECRET_STORE` no vendor
assembly (`AWSSDK.*`, `Google.Apis.*`, `Azure.*`, `BellaBaxter.*`) is loaded
after a create, a sign-in and a delete.

## 2. Each store against canned responses (FR-025, SC-008)

```bash
dotnet test -c Release --filter-class "*SecretStore*"
```

Expected: for each of AWS, GCP, Azure and BellaBaxter, write, read, replace,
delete, not found, access denied, throttled, 5xx and hang all map to the
`StoreFailure` of the [seam contract](./contracts/secret-store.md), and a hang
ends at the 10 s bound (driven by the test clock, not a real wait).

## 3. The product paths with an in-memory store (Stories 1, 2, 4)

```bash
dotnet test -c Release --filter-class "*ExternalSecret*"
```

Expected, each a test:

- Create a GitHub connector with `UMBRAL__SECRET_STORE=aws` (in-memory store
  registered as `aws`); the card has `data-secret-store="aws"`; a full sign-in
  completes; the data directory (database file, `-wal`, keys) contains neither
  the secret nor any ciphertext of it (SC-002).
- Store refuses the write: nothing saved, `connector-secret-store-failed`
  notice, `secret_store_failed` event, no 500.
- Delete with a store that refuses: connector gone,
  `connector-secret-left-behind` notice naming the secret, both events.
- Cache: a second sign-in within 300 s makes no store call; advance the clock
  past 300 s with the store down → sign-in succeeds and records `stale=true`;
  past 3 900 s → fails with `connector_secret_unreadable;store=aws;cause=unreachable`.
  With the store answering not found instead → fails at once.
- Replace a secret → next sign-in uses the new value whatever is cached.
- Grace set to 0 → the first sign-in after expiry with the store down fails.
- Reference in `azure` on an instance with no Azure credentials → card has
  `data-secret-unreadable`; startup with an unreachable active store → page has
  `data-secret-store-problem`.
- Incomplete settings (`UMBRAL__SECRET_STORE=azure` without the vault URI) →
  start refused, message names `UMBRAL__AZURE_KEY_VAULT_URI`.
- Re-create a connector with the slug of one just deleted → a different name in
  the store (FR-009).

## 4. Moving, interrupted at every step (Story 3, SC-003)

```bash
dotnet test -c Release --filter-class "*SecretMove*"
```

Expected: three connectors in `local` move to an in-memory `aws` and back. For
every N, a store that fails on its Nth call and a re-run leave all three able to
sign in, with no reference to a missing secret and no second copy in the
target. Dry run changes nothing. Exit codes as in the
[contract](./contracts/move-secrets-cli.md). A `SubcommandTests` case runs the
built `Umbral.dll move-secrets --dry-run` as a process with only environment
variables.

Idle memory with an external store (SC-006), Linux only, as in CI:

```bash
dotnet test -c Release --filter-class "*IdleMemory*"
```

Expected: the extra run with `UMBRAL__SECRET_STORE=aws` against a local stub
(`AWS_ENDPOINT_URL_SECRETS_MANAGER`) stays under 160 MB (the local-store run stays
under 150 MB; SC-006, amended 2026-10-19).

## 5. Real managers (manual, once per store)

For each store, following its walkthrough in `docs/secret-stores/`:

1. Create the place (vault, project, Bella environment) and grant exactly the
   permissions the walkthrough lists.
2. Start the container with the walkthrough's variables. No warning in the log;
   no notice on the Connectors screen.
3. Create a GitHub connector. The manager's console shows
   `umbral-<slug>-<8 chars>` (Bella: `UMBRAL_UMBRAL_<SLUG>_<8 CHARS>`). Sign in.
4. Replace the secret: the console shows a new version; sign in again.
5. Delete the connector: the console shows the secret deleted (scheduled, for
   AWS; soft-deleted, for Azure). Re-create it with the same slug: it saves.
6. Revoke Umbral's access: the startup log and the Connectors notice say so
   and what to check; sign-ins keep working until cache + grace runs out.
7. With connectors in `local`, switch to the store, run
   `move-secrets --dry-run`, then `move-secrets`; sign in. Switch back to
   `local`, run it again; sign in. `grep -c` of the secret over `/data` is 0
   while it is in the manager.

Timed end to end from an existing account, steps 1–3 should take under
15 minutes (SC-001).

## 6. BellaBaxter specifics (manual)

Against a real Bella server, confirm what research R7 could only read from the
decompiled client:

- The single-secret GET returns the value where the adapter reads it.
- A zero-knowledge environment (`BELLA_BAXTER_PRIVATE_KEY`) accepts Umbral's
  write and returns it decrypted.
- An environment with two secret providers refuses with the message naming
  `UMBRAL__BELLABAXTER_PROVIDER`, and works once it is set.
- A Frunchy-style app loading the same environment with `AddBellaSecrets` sees
  Umbral's keys under the `UMBRAL_` prefix (the walkthrough advises a separate
  environment).

Record the results in `research.md` R7 and fix the adapter test fixtures if any
differ.
