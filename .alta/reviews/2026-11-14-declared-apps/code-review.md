# Code review, task a5ap (spec 019, declared apps)

Reviewer: the `umbral` session. Range `a962660..91a6e93`, read against the spec (with C1, C2 and
N1–N3 folded in at `47f02e4`), `contracts/declaration.md` and `data-model.md`.

**Verdict: clear to merge from the code and security side, with two nits.** Nothing blocking.

## C1 and C2, as built

**C1: any failure while applying stops the start.**
- `Declarations.apply` opens one scope and takes `UmbralContext` and `IOpenIddictApplicationManager`
  from it.
- `plan` does every read (OpenIddict, `declared_items`, the connectors, the stored connector choice,
  `ValidateClientSecretAsync`) before the transaction.
- The transaction is begun on the context. Umbral's rows go through `GetDbConnection()` and
  `GetDbTransaction()` using the `...In` store variants, so no second connection writes meanwhile.
- Nothing catches. `Program.applyDeclaration` lets the exception end `initialize`.
- The test "a failure in the middle of the apply stops the start and leaves no app and no row"
  uses a trigger that aborts `security_events` after OpenIddict's `CreateAsync`, and finds no app,
  no `declared_items` row and no event. That proves OpenIddict's store saves inside the ambient
  transaction, which was R2's open question. A second test covers handing items back.
- A start that changes nothing opens no transaction at all.

**C2: adopt an identical passkey connector.**
- `AdoptPasskey` happens only for a connector that was never declared (no `declared_items` row)
  and is enabled, shared and has the declared sign-up mode.
- Anything else is refused, and the refusal names what differs.
- Recorded as `connector_updated declared;adopted`, and the start line says "adopted".
- Walked for real on an old r5hd sample volume.
- A connector at `passkey` of another kind is refused.
- The pane always uses the slug `passkey` (`PasskeyConnector.fs:21`), so the lookup by that slug
  can't miss one.

## The rest

- **Parse:**
  - Every refusal is collected before Umbral exits, with nothing written.
  - Unknown properties are refused.
  - A leftover `UMBRAL__APP_SECRETS__<key>` is refused, and `_` is accepted for `-` (N1).
  - The secret is held in `AppSecret`, whose `ToString` is `***`, so no log line, `%A` or problem
    line can print it.
  - `Ids.isValidSlug` now ends in `\z`. That's a real fix: `$` let a key ending in a newline
    through.
- **Apply:**
  - URI lists are compared as sets.
  - The secret is checked with `ValidateClientSecretAsync` and a mismatch rotates it.
  - `PopulateAsync` then `UpdateAsync` keeps the stored hash unless a new secret is set, which is
    OpenIddict's own comparison, so there's no rehash on every start.
  - A type change sets or drops the secret. PKCE stays required for every app
    (`AppClients.buildDescriptor`), so a `web` → `spa` change never leaves a public client
    without it.
  - Un-declaring changes nothing but the row and the event.
  - Re-declaring is allowed only for a key with history.
  - A connector slug that doesn't exist yet is left out with a warning, since the foreign key
    forbids storing it. It's added at a later start.
- **Admin area:**
  - The refusal comes after `readValidated` and before any read, on every route that changes what
    the declaration owns: connectors (`Apps.setConnectors`), secret, redirect URIs, delete, and the
    passkey connector's save, toggle and delete. Branding and invitations stay open.
  - `DeclaredAdminTests` posts to each route directly.
  - Deleting an un-declared item forgets its history row, so a later admin-made app with that
    client ID isn't mistaken for a declared one.
- **Events:**
  - Actor `system`.
  - The details are closed words and field names: `declared`, `fields=…`, `undeclared`,
    `adopted`, `redeclared`, `shared`.
  - No secret and no URI.
- **The package:**
  - The generated secret is a 48-character, persisted, secret parameter (N2).
  - The variable name uses `_` for `-`.
  - Publish mode refuses redirect URIs derived from an endpoint.
  - The walk's grep found the secret nowhere but `docker inspect`, as expected and as the docs
    say.

Run here at `91a6e93`: `Declaration*`, `DeclaredAdmin*`, `DatabaseTests` and `MetaTests`, filtered:
199/199. Package tests: 77/77.

## Nits

**N1. The declaration in a publish manifest** (the risk named in the implementer's message).
- `UMBRAL__DECLARATION` is plain JSON, so it begins with `{`. Aspire's manifest uses `{…}` for
  expressions, and a tool that evaluates manifest strings (azd, Aspirate) may try to read
  `{"version":…}` as one and fail the deploy. Nothing insecure can come of it: a misread
  declaration either fails the deploy or is refused by Umbral at start, writing nothing.
- The deploy this project uses is Caravana's publisher (`fzzx`). Check the declaration there
  first, during fzzx.
- If a target does break on it, the fix is a second accepted form later (for example
  `UMBRAL__DECLARATION` as base64 JSON, recognised by not starting with `{`), not a change now.
- Add one sentence to `docs/run/aspire.md` naming the risk for other targets.

**N2. Two starts at once.**
- Two containers on one volume, starting together, would both plan before either commits. The
  second's insert of an app would then fail on OpenIddict's unique client ID and stop that start
  (C1).
- That is safe, and it is the right outcome. One instance per data directory is already the
  documented rule. No change, but a line in the edge cases would record it.

## Re-check

None needed for the nits; take them or leave them as documented. The full-suite count is the
last thing outstanding.
