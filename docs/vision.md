# Umbral's vision

## What Umbral is

Umbral is a self-hosted OpenID Connect provider whose only job is **signing
people in without passwords**: with a social provider they already use, or with
a passkey. You wire each provider up once, in Umbral, and every app you run
signs people in through it with any standard OIDC library.

Today there are three ways to add sign-in to a handful of apps: wire OAuth into
each app by hand, run an identity platform such as Keycloak or Authentik, or
rent one such as Auth0 or Clerk. The first repeats the same work in every app,
the second is far more than a few apps need, and the third puts your users'
data and your bill in someone else's hands. Umbral sits in that gap, and every
rule in the [constitution](../.specify/memory/constitution.md) exists to keep it
there.

## Who it is for

Someone with a homelab, a Raspberry Pi and five hobby apps; an indie developer
with a few small projects; a small community that wants one sign-in for its
tools. They want sign-in to work and then to stop thinking about it. Some of
them will not use Google, GitHub or Discord at all, and the passkey connector
is for them.

## What it aims for

- **One container, modest hardware.** One image with an embedded SQLite
  database, no other service to run. It runs on a 64-bit Raspberry Pi and idles
  under 150 MB of memory (160 MB with an external secret store). All state is
  one data directory: backup is a copy.
- **Three concepts.** App, Connector and User are the whole product, and the
  admin area has three screens, one for each.
- **Three values per app.** An app needs the issuer URL, a client ID and a
  client secret, nothing else.
- **A first sign-in within five minutes** of starting the container, counting
  the time to paste a provider's credentials.
- **Secure without a hardening guide.** The defaults are the safe ones; the
  operator does not have to turn anything on.
- **The documentation is the support channel.** There is no support team, so a
  feature is not done until its documentation is, and every error an operator
  sees must say what to do next.

## What it will not do

Umbral answers "who is this person?" and nothing more. It has no realms,
tenants, groups, roles or permissions, no SAML or LDAP, no MFA, no local
password accounts, no user federation and no login-flow builder, and it will
not get them. A passkey is neither MFA nor a password account: it is the whole
sign-in, never a second step after another one. What a person may do in an app
is that app's job. Umbral does not send email: an invitation to register a
passkey is a link the operator hands out.

Deciding who can be an Umbral user is in scope: limiting which verified email
domains may sign in, disabling a user, and choosing, for passkeys, between open
sign-up and invitations only. A rule on which people may use a given app is not.

If you need any of what is excluded, [Keycloak](https://www.keycloak.org/) and
[Authentik](https://goauthentik.io/) do it well, and pointing you there is the
right answer.

## What could go wrong

The largest risk is **scope creep**: every exclusion above will be asked for,
and each one, granted, would make Umbral the kind of platform it exists to
avoid. That is why the constitution's Principle VII makes "no" the default
answer. The other risks are a provider changing its OAuth behaviour, which is
why each connector has a walkthrough and is tested without real credentials,
and a community too small to keep the project going.

## How the scope has changed

- **1.0.0, 2026-10-09.** Social login only, configured from the admin area or
  from a configuration file equivalent to it.
- **2.0.0, 2026-10-10.** The configuration file was dropped: the admin area is
  the one place configuration changes, and the data directory is the whole
  state ([spec 004](../specs/004-remove-config-document/spec.md)).
- **2.1.0, 2026-10-15.** Limiting which verified email domains may sign in is
  declared in scope, as a rule on who can be a user, not an authorization model
  ([spec 006](../specs/006-allowed-email-domains/spec.md)).
- **3.0.0, 2026-10-19.** Passwordless sign-in: with a social provider or with a
  passkey Umbral verifies itself. MFA and passwords stay out
  ([spec 008](../specs/008-passkey-connector/spec.md)).

The first assessment of the idea, a chat reply from before the first line of
code, is kept in [docs/history](history/2026-vision-conversation.md).
