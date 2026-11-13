# Task s1ug: a remembered sign-in fails closed when its connector is gone or is another kind

Found as nit N5 in the code review of spec 016 (Microsoft). Small enough for a plan here
rather than a Spec Kit cycle: no schema change, no new screen, one new check at the two
places that already re-check remembered sign-ins.

## The gap

`Authorize.fs` (a session reused) and `Token.fs` (a code or refresh token) look up the
session's connector by slug (`ConnectorStore.findBySlug`), and each kind re-check
(`FediverseServers.recheckServer`, `BlueskyAccounts.recheckServer`,
`MicrosoftAccounts.recheckTenant`) acts only when the connector it finds is its own kind.
Anything else admits. So:

1. **Slug now names another kind.** The admin create paths already refuse a kind's own
   name (`microsoft`, `fediverse`, `bluesky`, …) for another kind (`Connectors.fs`,
   `reserved` / `reservedForOtherKind`), so the exact scenario of the task (an OIDC
   connector named `microsoft`) is only reachable from a database written before those
   kinds existed, or edited by hand. Any other slug can change kind freely: delete the
   Discord connector `work`, add an OIDC connector `work`, and every session and refresh
   token from the Discord one is judged by the new connector's rules.
2. **Slug names nothing (plain delete).** At authorize this is already safe: a deleted
   connector is not offered, so the session does not count for the app (it is not signed
   out either; the person signs in through an offered connector). At the token endpoint a
   refresh through a deleted connector is admitted today, judged by the instance email
   list alone (specs/006 research R7, test `a refresh whose connector was deleted is
   judged by the instance list alone`). A refresh through a deleted Microsoft connector
   that was limited to one tenant, or a Fediverse or Bluesky one limited to some servers,
   goes on being issued with no limit at all.

## Choice: option 1, with the kind the session already carries

Every session cookie already has the connector's kind in its `idp` claim
(`Sessions.IdpClaim`, written from `connector.Kind.Value` by `Callback.fs`, and `passkey`
by the passkey ceremonies), and every code and refresh token has it too
(`OpenIddictSetup.IdpClaim`, set by `Authorize.setProfileClaims`). A session from before
feature 003 has no `connector` claim and its `idp` holds the slug, which then equalled the
kind, so `Sessions.currentConnector` and the kind still line up.

So option 1 needs no new claim, no migration, and covers every session and token already
issued. Option 2 (a tombstone of deleted slugs) would need a new table, would forbid an
operator from reusing a name forever, and would still leave the plain-delete refresh to
decide separately. Option 1 decides both with one rule: **a remembered sign-in is honoured
only while its slug names a connector of the kind it was made with.**

## Plain delete at refresh: refused

A refresh is an issuance, and the connector's rules (its email list, its server list, its
tenant audience) are what an issuance is judged by. After a delete there is no rule left,
and falling back to the instance list loses every per-connector limit. So refresh through a
deleted connector answers `invalid_grant`. The app sends the person through authorize
again, where they pick a connector the app offers. The account and its links are kept, so
the `sub` does not change.

This changes specs/006 R7 for tokens: "a connector deleted since: the instance list alone"
is no longer reached from the token endpoint (it stays in `EmailDomainRules.recheck`,
which is a judge of its inputs, with a note). Adding the connector back with the same slug
and the same kind before the refresh admits it again, as the delete confirmation promises
("adding it back returns them to the same account").

A disabled connector is unchanged: it still exists, its rules still apply, and its tokens
still refresh. Disabling stops new sign-ins; deleting now also stops refreshes.

A code or refresh token with no `connector` claim (issued before specs/006) is unchanged:
nothing says which connector to compare, and `EmailDomainRules.recheck` already judges it.

## The rule in code

- `ConnectorStore.recheckKind config slug kind : Task<KindChange option>`:
  `None` admits; `Some Deleted` when no connector has the slug; `Some(OtherKind now)` when
  the connector's kind is not `kind` (also when the session has no kind at all).
- `SecurityEvents.connectorChangedDetail change stage`:
  `connector_changed;reason=deleted;stage=token` or
  `connector_changed;reason=other_kind;stage=authorize;kind=oidc`. Closed words only.
- `Authorize.fs`: run first, inside the "offered" branch, before the email-domain check.
  A refusal signs the person out and records `sign_in_failed` with that detail.
- `Token.fs`: run first when the token has a `connector` claim, before the email-domain
  check. A refusal records `sign_in_failed` and answers `invalid_grant`.

## Tests

- Microsoft, Fediverse, Bluesky: sign in under the kind, delete the connector, create a
  generic OIDC connector with the same slug (written to the store directly, since the
  admin screens refuse it): the remembered session is signed out at authorize and the
  refresh token is `invalid_grant`, each with `reason=other_kind`.
- A non-reserved slug (the Fake connector) deleted and re-created as OIDC through the store:
  the same refusals.
- Plain delete: a refresh is `invalid_grant`, `reason=deleted;stage=token`; re-adding a
  connector of the same kind under the same slug admits it again.
- The specs/006 test for a refresh after delete is rewritten to the new behaviour.
