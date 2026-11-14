# Security decisions, spec 019 (task a5ap, declared apps), before the build

Reviewer: the `umbral` session. Commit `b88587b` on `alta/twilight-fornax-23bd`: the spec's
"Decisions taken in this spec", `contracts/declaration.md`, `data-model.md` and `plan.md` (R2–R5).

**Verdict: D1–D11 are cleared, with two conditions (C1, C2) to write into the spec before the build,
and three nits.** No decision needs to change direction.

## The four you asked about

**D10, apply before the claim: agreed as written.**
- The claim guards the admin area. Signing in to an app never needed it.
- Whoever sets the container's environment can already run `reset-credential`, so they gain nothing
  new.
- The one consequence worth stating in the docs: with `"signUp": "open"` and an unclaimed instance
  on a public host, people can sign up and sign in to the declared apps before the operator has
  claimed it. That's what the declaration says, but add a sentence to `docs/run/aspire.md`: "claim
  the instance before you publish it".

**D5, refuse the passkey connector rather than adopt it: agreed, with condition C2.**
Refusing is right whenever adopting would change who can enrol. But the rule as written also
refuses a connector that is *already exactly* what the declaration says. That's the common upgrade
case: every existing `samples/aspire` volume had the passkey connector set up by the old step 2
(enabled, shared, **Anyone can sign up**). As written, those volumes stop starting the day the
sample adopts the declaration.
- **C2:** adopt the passkey connector when it was never declared and its stored state equals the
  declared one: enabled, shared, same sign-up mode. Adopting then changes nothing and widens
  nothing. Record it as `connector_updated` with `declared;adopted`.
- Refuse in every other case, as written.
- Test: a volume set up by the old sample steps starts with the new sample, and the sample signs
  in.
- Apps don't need this. The old sample's app has a random client ID, so the key `sample` never
  collides. It's left as an orphan app, which is harmless, and the samples README may say it can be
  deleted.

**D3, an unset variable un-declares everything: agreed.**
- Un-declaring changes no field, deletes nothing and disables nothing. It only hands the item back
  to the admin area.
- Re-declaring is allowed, because the item was declared once.
- So a deploy that loses the variable by mistake costs only a period in which the admin area could
  edit those apps, and the next good deploy restores them.
- The start line's "N no longer declared" and the `undeclared` events make it visible.

**R2, one transaction across OpenIddict's EF context and the raw rows: agreed, with condition C1.**
- Beginning the transaction on the scoped `UmbralContext` and writing Umbral's rows on
  `GetDbConnection()` with `GetDbTransaction()` is right. OpenIddict's EF stores call
  `SaveChangesAsync` on that same context, which enlists in the current transaction.
- The OpenIddict application manager must come from **the same scope** as the context, or it
  writes on another context and connection, outside the transaction.
- **C1:** the application manager keeps an in-memory cache (`IOpenIddictApplicationCache`, wired in
  `OpenIddictSetup.fs:277`). A create or update that is rolled back can leave an entry in that cache
  for an app the database doesn't have, or with old redirect URIs. So **any** failure after
  validation (an exception while applying, not only a refusal) must also stop the start, never log
  and continue. The process, and its cache, then go away with the transaction. Say so in D6.
- Add a test with an induced failure mid-apply: no app in the database, and the start raises.
- The fallback (one unit per item) is acceptable only together with C1. Tell me if you take it.

## The others

- **D1 (JSON in one variable, a secret variable per app):** right. Publish targets do treat
  container files unevenly.
  - The docs must say that the secrets are ordinary environment variables: visible to
    `docker inspect`, written to a compose publish's `.env`, and masked in the Aspire dashboard as
    secret parameters.
  - The declaration itself holds no secret, which is the point.
- **D2 (the declaration owns what it names):** right. Spec 004's last-writer-wins fault is exactly
  what read-only owned fields avoid.
  - The refusal must be enforced on the server for every route that changes an owned field (redirect
    URIs, secret, delete, connector choice, the passkey connector's toggle, delete and enrolment), as
    FR-009 says, not only by hiding buttons.
  - The admin-route tests should post to each route directly.
- **D4 (the key is the client ID):** fine. A client ID is not a secret. A `web` app still needs its
  secret of at least 32 characters, and a `spa` app needs PKCE.
- **D6 (refuse everything, write nothing):** right, plus C1. Refusing a leftover
  `UMBRAL__APP_SECRETS__<key>` is strict, and I agree.
- **D7 (secrets):** right. Validating with `ValidateClientSecretAsync` at each start and rotating on
  a mismatch is the correct convergence, and Umbral never generating the secret is right.
- **D8 (events, actor `system`):** right. The `System` actor exists (`SecurityEvents.fs:285`). No
  URI and no secret in a detail.
- **D9 (both modes; publish needs explicit redirect URIs):** right.
- **D11 (constitution 3.2.0 MINOR):** right.

## Nits

- **N1. Hyphens in a variable name.** A key such as `my-app` gives `UMBRAL__APP_SECRETS__my-app`.
  Docker and compose accept that, but some platforms and shells don't. Accept the key with `-`
  replaced by `_` as well. This is unambiguous, because `_` can't appear in a key. The package
  should emit the underscore form.
- **N2. The package's generated secret.** Generate at least 32 characters (I'd use 48), and pin it
  with a test, so a default `AddApp` never trips D7's length rule.
- **N3. The start line** could name what was adopted (C2), for example `passkey connector adopted`.

## Next

Once C1 and C2 are in the spec, the build can start. No re-read is needed for the nits. I'll
review the code against C1 (the scope, the failure test) and C2 (the adopt test on an old sample
volume) in particular.
