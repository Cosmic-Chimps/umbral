# Integrate an app

An app needs three values (the issuer, a client ID and a client secret), and any OpenID Connect
library configures itself from the discovery document. The README has them, with a first example,
under [Integrate an app](../README.md#integrate-an-app), and [`samples/`](../samples/README.md) has
a working app per stack. This page is the rest: what an app can count on, the endpoints, signing
out, what happens when a person is disabled or deleted, the name of the provider's application, and
how people are identified.

## What an app can count on

- **Authorization code flow with PKCE (`S256`) is the only flow.** PKCE is
  required even for apps with a secret, and `plain` is refused. No implicit or
  password grant.
- **Scopes:** `openid`, `email`, `profile`, and `offline_access` if you want a
  refresh token.
- **Redirect URIs are matched exactly.** An app can have several, and a request
  whose `redirect_uri` is not one of them, character for character, is
  answered with an Umbral error page, not a redirect.
- **The token endpoint** (`/connect/token`) accepts the secret as HTTP Basic or
  as a form field (`client_secret_basic` or `client_secret_post`).
- **A browser-only app can call Umbral directly.** The discovery document, the
  keys, `/connect/token`, `/connect/userinfo` and `/connect/revoke` answer a
  request from any origin (`Access-Control-Allow-Origin: *`), and the preflight
  a browser sends first. There is nothing to set: PKCE and the exact redirect
  URI tie the exchange to the app, and a browser app has no secret to protect.
  The sign-in and admin pages are not reachable that way: a browser navigates
  to them.
- **`sub`** is stable for a user and opaque: it is not derived from any
  provider's ID. If you delete a user and they sign in again, they get a
  *different* `sub`.
- **`email` and `email_verified`** come with the `email` scope, both or
  neither. Neither, when Umbral has no address for the person: a fediverse or
  passkey-only user, or a GitHub account with no verified primary address or
  whose addresses GitHub did not return on that sign-in. When
  they are there, `email_verified` is a JSON boolean and can be **`false`**:
  Discord, Google and your own OpenID Connect provider can give an address they
  have not verified, and Umbral passes it on as unverified. Trust the address
  only when the flag is `true`.
- **The email is the first one Umbral saw.** A person who changes their address
  at the provider keeps the old one in Umbral, so `email` does not change.
  `email_verified` becomes `true` only when a provider verifies **that same** address,
  and it does not go back to `false` afterwards. There is one exception: the upgrade to
  schema version 9 may set it back to `false` once, for a user whose stored address no
  provider has verified.
- **`name`** comes with the `profile` scope and is always there: the name the
  provider gave, the name a passkey user typed, or, when a provider gave none, a
  placeholder such as `OpenID Connect user`. **`picture`** comes with it when the
  provider gave one. A fediverse sign-in adds **`preferred_username`**
  (`user@server`) and **`profile`** (the person's page); the other providers send
  neither.
- **`idp`** is always there, and is Umbral's one extra claim: the provider the
  person used (`github`, `discord`, `google`, `oidc` for any OpenID Connect
  provider you added, `passkey`, or `fediverse`). It is the provider's kind,
  never a connector's identifier: with several Discord connectors it is still
  `discord`. Ignore it freely.
- **Access tokens are opaque.** They are not JWTs: do not try to decode them.
  Validate them by calling `/connect/userinfo`. Disabling a user cuts their
  tokens off immediately.
- **The `issuer` in the discovery document ends with a slash**
  (`https://auth.example.com/`), and so does `iss` in the `id_token`. Libraries
  that read the discovery document do the right thing. If you hard-code the
  issuer and your library compares strings, copy it from the discovery document.

An app can have **up to 10 redirect URIs** and up to 10 post-logout redirect
URIs, one for each place it runs: `http://localhost:3000/callback` on your
machine, a staging host, production. You give the first when you create the
app, and add or remove the others on its page at any time. The client ID and
the secret stay the same. An app keeps at least one redirect URI: to replace
the last one, add the new one first. A removed URI is refused from the next
sign-in on. Each one is matched exactly, so register every place separately;
wildcards are refused. Every addition and removal is an `app_updated` event in
the security log, with the URI and one of `redirect_uri_added`,
`redirect_uri_removed`, `post_logout_redirect_uri_added` or
`post_logout_redirect_uri_removed`.

The client ID is generated, and the app's name and type are chosen when you
create it. The app's **Sign-in methods** can change at any time on its page,
like its redirect URIs, without touching the client ID or the secret.

## Endpoints

Your library reads these from the discovery document; they are listed here so
you know what each one does.

| Endpoint | What it does |
| --- | --- |
| `/.well-known/openid-configuration` | The discovery document. |
| `/.well-known/jwks` | The public keys that sign the `id_token`. |
| `/connect/authorize` | Starts a sign-in (GET or POST). Umbral shows its sign-in page, or, when the person already has an Umbral session through a connector the app offers, sends the code straight back. |
| `/connect/token` | Exchanges a code for tokens, or a refresh token for new ones. A refresh reads the person again: a changed name or avatar shows in the new `id_token`, and a disabled or deleted person gets `invalid_grant`. |
| `/connect/userinfo` | GET or POST, with the access token as a Bearer token. Reads the person from Umbral at every call. |
| `/connect/revoke` | Revokes an access token or a refresh token. |
| `/connect/logout` | Ends the person's Umbral session; see [Signing out](#signing-out). |

**Introspection** (`/connect/introspect`) is in the discovery document, but no
app is allowed to call it: it answers `unauthorized_client`. To check an access
token, call `/connect/userinfo` with it.

**The `id_token` and userinfo carry the same claims**, under the same scopes:
`sub` and `idp` always, `name`, `picture`, `preferred_username` and `profile` with
`profile`, `email` and `email_verified` with `email`. The `id_token` describes the
person when it was issued (or last refreshed); userinfo describes them now, and
answers `401` with `invalid_token` once they are disabled or deleted.

## Signing out

Send the person to `/connect/logout` (GET or POST). It ends their **Umbral**
session and nothing else: they stay signed in at Discord, GitHub or whichever
provider they used, and the tokens your app already holds keep working until
they expire. Revoke the refresh token at `/connect/revoke` if you want it gone.

An Umbral session lasts 14 days from its last use and survives the browser
closing, so closing the browser does not sign anyone out: on a shared computer,
signing out is how a person leaves. Disabling or deleting them on the **Users**
screen ends it too (see below).

To bring them back to your app afterwards, pass `post_logout_redirect_uri` with
one of the post-logout URIs registered for the app on the **Apps** screen (and
`state`, which comes back with it). Umbral redirects only to those: without one,
or with any other address, the person is still signed out and lands on Umbral's
own home page.

## When a person is disabled or deleted

Disabling someone on the **Users** screen revokes every token issued to them,
at once: userinfo answers `401` with `invalid_token` and a refresh gets
`invalid_grant`. When they next try to sign in through a provider or a passkey,
Umbral's sign-in page says "This account has been disabled. Contact whoever runs
this Umbral." If they still have an Umbral session, your app's next
authorization request comes back with `error=access_denied`.

Deleting someone revokes their tokens the same way and removes the user and
the provider accounts linked to it. An authorization request on a session they
still have comes back with `error=login_required`. If they sign in again, they
are a new user, with a new `sub`.

Both errors come back to your app's redirect URI as an ordinary OpenID Connect
error, not a fault: Umbral has already ended the session, so treat them as
"sign in again". A disabled person who does is told why on Umbral's page.

## Naming the provider's application

Umbral is the one login for all your apps, so the application you create at a
provider represents **Umbral**, not one app. The provider shows that
application's name on its consent screen ("*Name* wants to access your
account") to people signing in to **every** app that uses the connector. Name it
after your Umbral instance, for example `Cosmic Chimps Login`. If you name it
after one app, people signing in to your other apps see that app's name and
think they are in the wrong place. Umbral's own sign-in page says which app
people are signing in to, so they still know where they are heading.

## Giving one app its own name

Most apps are fine with the shared name. When one app needs its own name on the
consent screen, give it its own connector:

1. Create another application at the provider, named as you want people to see
   it (for example `Frunchy`).
2. On **Connectors**, under **Add another connector**, pick the **Provider**,
   choose an **Identifier** (for example `discord-frunchy`), set the **Button
   label**, paste the **Client ID** and **Client secret**, untick **Shared with
   every app**, and click **Add connector**. The identifier is fixed once saved.
   It is part of the callback URL, `<origin>/connect/discord-frunchy/callback`,
   which the new page shows: register that URL at the provider. The first
   connector of a provider keeps the provider's name as its identifier (`discord`);
   only additional ones need one.
3. On **Apps**, set the app's **Sign-in methods** to **Only the connectors I
   choose**, tick the new connector, and click **Save sign-in methods**.

Now that app's sign-in page offers only that connector, and every other app
keeps the shared one. A person who signs in to both apps with the same provider
account is **one** Umbral user, with the same `sub`. A session counts for an app
only if it came through a connector that app offers; otherwise the person sees
that app's sign-in page (they stay signed in to Umbral for the other apps). If a
change (disabling or deleting a connector, or changing an app's choice) would
leave an app with no way to sign in, Umbral warns first and names the apps.

## How people are identified

Signing in with the same provider account always gives the same Umbral user. The
account is recognised by the provider's own id for the person, never by a
username or an email, so someone who changes either at the provider is still the
same user. For an OpenID Connect provider you added, that id is the provider's
issuer together with the person's `sub` there: two issuers are never the same
account, and adding a provider back with the same issuer returns everyone to the
same user.

A provider account Umbral has not seen before is **linked** to an existing user
when the provider asserts a **verified** email address and exactly **one**
existing user has that same address, also verified. The address Umbral keeps for a user
is the first one a provider gave, and it counts as verified only once a provider has
verified that address, not another one. This holds for every kind of
connector, OpenID Connect providers you added included, which is why the form
for adding one asks you to add only a provider you trust. Otherwise Umbral
creates a new user:

- the provider gave no email, or one it has not verified;
- no existing user has that address verified;
- more than one user has it. Umbral does not guess between them: it creates a
  new user and leaves the others for you to delete.

You can disable or delete users on the **Users** screen.

A person who enrols with a passkey has no email, so nothing can link them to a
provider account later: they are their own user, and signing in with a provider
afterwards creates a different one. A person who signs in with a provider and
then adds a passkey to that account is one user, with the same `sub`.
