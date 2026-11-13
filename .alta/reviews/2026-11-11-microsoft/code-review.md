# Code review: the Microsoft accounts connector (task m5ft, spec 016)

Branch `alta/warm-monsoon-8gdd`, `a82d8ec..07b350e`. Reviewer: the `umbral` session, 2026-11-13.

How it was reviewed:
- Two independent read-only reads by concern: token and issuer validation, identity and email, then the stored secret, the admin side, the re-check and the schema.
- My own read of `Providers/Microsoft.fs` (the validation path) and `Domains/Microsoft.fs` (`admits`).
- I verified every finding below against the code myself before writing it down.
- The full suite, run from a git checkout of `07b350e`'s files (results at the end).

## Verdict

**Clear to merge, with one nit worth taking now (N1) and four others.** No blocking items. It is the cleanest of the three connector reviews.

The hard parts are done right:
- the unvalidated `tid` is used only to build the one issuer the library accepts;
- the key that signed the token must have been issued for that tenant;
- the tenant rule runs after validation;
- the person is `tid#oid`, never `sub`;
- `email_verified` needs both an email and `xms_edov` as JSON `true`, which closes the nOAuth route.

## Nits

- **N1. Take now: log the audience when it changes.** Saving a Microsoft connector records `connector_updated` with `store=…` only when a secret was written, and nothing about who may sign in. A widening, from "personal only" or "one tenant" to every Microsoft account, leaves no trace. That is exactly the change a stolen operator session would make. The Fediverse and Bluesky saves already record `onlyListed=…` on create and on update (`Connectors.fs`, around 1548 and 1658). Put `audience=<stored form>` in the create detail, and in the update detail when it differs from the stored audience, judged under the lease as the other fields are. It isn't a secret. Add a test for both.
- **N2. Judge the key that verified the signature, not the first with its `kid`.** `keyIssuerMatches` takes `keySet.Keys |> Seq.tryFind (fun key -> key.Kid = kid)`. The `common` key set mixes template-issuer keys and personal-tenant keys. If Microsoft ever lists one `kid` twice with different `issuer` members, the answer depends on list order: a good token refused, or a personal-only key judged by the template entry. That isn't exploitable (only Microsoft signs), but the rule should bind to the key the library used (`SigningKey` of the validated token). Or refuse when one `kid` appears with two different issuers. Add a duplicate-`kid` test.
- **N3. A user with links in two tenants passes the re-check through either.** `MicrosoftAccounts.recheckTenant` admits when any of the user's Microsoft links is in the audience. Unlike Bluesky, linking is possible here: a verified email with `xms_edov` joins a work and a personal account. So after the audience narrows to "personal only", a session first opened through the work tenant keeps passing. The person still holds an admitted account, so it is arguably right. Either record the tenant a session was opened with and re-check that one, or write down in R3 that the rule is per person and why, with a test that pins the chosen behaviour.
- **N4. A stored "one organization" naming the personal tenant should load as unreadable.** `parseStoredAudience` accepts it, though the form refuses it. Only a hand-edited row gets there, and it widens nothing, but loading it as `None` (which fails closed) keeps the load and the form agreeing.
- **N5. A slug reused across kinds.** If the Microsoft connector is deleted and a generic OIDC connector takes the slug `microsoft`, old sessions carry that slug, and the re-check sees another kind and admits. This is true of every kind today (`ConnectorDelete` revokes nothing by design), so it isn't this change's to fix. I'll queue it as a follow-up: fail closed when a session's slug now names another kind.

## Test gaps

- `email_verified: true` (the standard claim) without `xms_edov` stays unverified. The code ignores the claim; a test should pin that.
- A token with `preferred_username` or `upn` and no `email` leaves the stored email empty.
- `FakeMicrosoft` publishes template-issuer keys even for one tenant. Give it a real tenant key set (that tenant's issuer on its keys), so the `key_issuer` rule is exercised under "one organization", not only `admits`.
- On the Microsoft path itself: `alg` `none` or `HS256`, and an expired or not-yet-valid token. The shared validator is covered by the OIDC tests, but nothing shows this adapter uses it.
- Admin: a refused edit of an existing connector (say a bad tenant ID) leaves the audience, `client_secret_ref`, hint and secret unchanged, and a secret typed into that refused edit is neither drawn back nor written to the store.
- Re-check end to end: `common` narrowed to one tenant, and `organizations` narrowed to one tenant. Today only both-to-personal is covered. Also the authorization-code grant after narrowing.

## Checked and sound

- **The order inside `ExchangeAndFetch`.** Discovery for the stored audience, the code exchange, then the `id_token` parsed but not trusted, then:
  1. `tid` must be a JSON string in exactly canonical lower-case GUID form, else `tenant`;
  2. one expected issuer, `issuerFor tid`, from a constant template, exactly one string;
  3. `validateIdTokenWith`: signature against that audience's key set (throttled refresh per document), asymmetric algorithms only with signed tokens required, audience equal to the client ID, lifetime, nonce;
  4. the key-issuer rule against the configuration that validated (the refreshed one after a refresh);
  5. `oid` as a canonical GUID;
  6. `admits` after all of it.

  An `iss` naming another tenant than its `tid`, or the literal template, is refused. A key with no `issuer` member is refused.
- **`admits`.** Personal only is exactly the personal tenant; one organization is that GUID; any organization is every tenant but the personal one; both is all.
- **The `validateIdToken` split.** The wrapper passes `[issuer; issuer + "/"]` and the old refresh, as before. Apple's and the generic OIDC adapter's call sites are unchanged. `Check` and `Refresh` are now `CheckAt(i, i)` and `RefreshAt(i, i)`, the same cache key and throttle. A cached document still has its issuer checked on every call.
- **Identity and email.** The subject is `microsoftSubject tid oid`, with nothing falling back to `sub`, email, `preferred_username` or `upn`. `EmailVerified = email.IsSome && isJsonTrue payload "xms_edov"`, and only JSON `true` counts. Userinfo is never called. Linking and the email-domain rule both need verified.
- **The secret.** It takes the generic client secret's path on create (`putNew`, discarded when a race is lost) and on edit (`applyChanges` under the lease, a new version, the old copy removed when the store changed). SQLite holds only the reference and the 4-character hint. A blank field keeps the stored secret, the box is always drawn empty, and nothing is written before every refusal is collected.
- **The form.** `requireOperator` and `readValidated` (picked up by `CsrfTests`). The route is registered before the generic `{slug}` one. Tenant and client IDs must be GUIDs, the personal tenant is refused for one organization, and the audience is one of four values.
- **The re-check.** The tenant is part of the link key, so a link without a tenant can't exist. No audience, no readable link and a removed link all fail closed. It runs at authorize and at both token grants.
- **Schema 13.** `microsoft_audience TEXT NULL` in both the fresh schema and the upgrade step, rewriting nothing, and covered for 12 → 13, a fresh database and each audience's round trip.
- **Logs.** Fixed reason words, plus a tenant GUID on a refusal, as Fediverse and Bluesky log a host.

## For the re-check: the save rule from the documentation review (B1)

The documentation review found that saving an existing, enabled connector whose check fails keeps it enabled while saying "Saved, but not enabled". The implementer's rule (FR-003 and the contract) is right:
- an existing connector, a changed audience and a failed check: nothing is saved, and the answer is a 422 with the reason under the choice;
- an unchanged audience: the other changes are saved, the state is kept, and the notice tells the truth;
- a new connector is still saved disabled.

The fix should also:
- **Tell a mistyped tenant from an outage.** I asked Microsoft on 2026-11-13: discovery for an unknown tenant GUID answers **HTTP 400** with `{"error":"invalid_tenant","error_description":"AADSTS90002: Tenant '…' not found…"}`, while `consumers` answers 200. Map that 400 to `Wrong` ("No Microsoft tenant has this ID"), and keep `Unreachable` for a timeout, a 5xx or a network error ("Microsoft didn't answer; nothing was saved"). Never quote Microsoft's description on the page; it carries trace IDs. Give the fake that exact 400 body.
- **Draw nothing secret back on the 422**, and write nothing to the store.
- **Log the audience (N1) only for a change actually saved.**

## The tests, run by the reviewer

From a git checkout of `07b350e`'s files, with CI's commands: `dotnet build -c Release` with **0 warnings**, then `dotnet test -c Release --no-build --filter-not-class "Umbral.Tests.IdleMemoryTests"`, **2677 of 2677 passed**. Your 2682 includes the idle-memory tests.
