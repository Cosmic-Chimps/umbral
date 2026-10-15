# OpenID Connect connector (your own identity provider)

Lets people sign in to your apps through an identity provider **you already
run**: Authentik, Keycloak, Pocket ID, Authelia, Zitadel, Kanidm, GitLab,
Microsoft Entra, or anything else that speaks OpenID Connect. Instead of wiring
every app to that provider by hand, you wire it to Umbral once and your apps
keep talking to Umbral.

You do not need this if you only want Discord, GitHub or Google. Those have
walkthroughs of their own, and no provider of your own to run.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) · [Google](google.md).

## Before you start

- Umbral running with `UMBRAL__PUBLIC_ORIGIN` set to the address people really
  use. The callback URL you register below is built from it, so if you change
  the origin later, update your provider too.
- An identity provider that Umbral's container can reach **over https**. If
  yours is only reachable over plain `http://`, read
  [A provider that is only on plain http](#a-provider-that-is-only-on-plain-http)
  first.
- **Read the email warning below.** Adding a provider means trusting it.

> **Umbral trusts the emails your provider calls verified.** When someone signs
> in through it and it says their email address is verified, Umbral treats that
> person as the owner of that address and links them to anyone who already signed
> in with the same verified address through another provider (see
> [How people are identified](../../README.md#how-people-are-identified)). Add
> only a provider you run or trust. One that lets people claim an address they do
> not control would let them sign in as someone else. Umbral cannot detect that:
> there is no per-provider switch, so the decision is yours when you add it.
> An address the provider does not mark verified is never used to link anyone.

## 1. Choose the identifier, and so the callback URL

An OpenID Connect provider has no card of its own to fill in: you add it with
the form **Add another connector** on the **Connectors** screen, and you choose
its **Identifier** (its slug) there. The identifier is part of the callback URL:

```
<UMBRAL__PUBLIC_ORIGIN>/connect/<identifier>/callback
```

For example, with the identifier `home` and the origin `https://auth.example.com`:
`https://auth.example.com/connect/home/callback`. Use lower-case letters, digits
and hyphens, up to 32 characters, such as `home` or `club-keycloak`. It cannot
be the name of a built-in provider (`discord`, `github`, `google`) or `oidc`, and
it cannot be changed later.

Because you choose it, you can type the callback URL into your provider's
console before you add the provider in Umbral. After you add it, the new card
shows the same URL in **Redirect URI to register with the provider**, with a Copy
button. If the two differ by a character, copy the card's: your provider
compares it exactly.

## 2. Find the issuer URL

The **issuer URL** is the one address Umbral needs to know about your provider.
It reads everything else from it: the sign-in address, the token address, the
signing keys.

**The test that works for every provider:** the issuer is the URL where
`<issuer>/.well-known/openid-configuration` opens in your browser as a page of
JSON. That page has a line `"issuer": "…"`. Copy exactly what is written there,
including any path. A trailing `/` or not does not matter: Umbral treats
`https://a.example.com/o/` and `https://a.example.com/o` as the same issuer.

Where each product puts it:

| Provider | Issuer | Where to look |
| --- | --- | --- |
| **Authentik** | `https://auth.example.com/application/o/<application-slug>/` | **Each application has its own issuer.** Open the provider (**Applications → Providers**, then your OAuth2/OpenID provider): the page lists the **OpenID Configuration Issuer**. Note the `<application-slug>` in the path: using the bare `https://auth.example.com` does not work. |
| **Keycloak** | `https://kc.example.com/realms/<realm>` | One issuer per realm. An older Keycloak, or one configured with `/auth` as its relative path, puts `/auth` first: `https://kc.example.com/auth/realms/<realm>`. **Realm settings → General → Endpoints → OpenID Endpoint Configuration** opens the discovery page. |
| **Pocket ID** | `https://id.example.com` | The instance root: the address you open Pocket ID at, with no path. |
| **Authelia** | `https://auth.example.com` | The instance root: the address of your Authelia portal, with no path. |
| **Another provider** | its discovery page without `/.well-known/openid-configuration` | Look for "Issuer", "Discovery URL" or "OpenID configuration" in its settings, or try the test above. |

*Menu names and issuer shapes are as of writing and are unverified against the
2026 versions of these products. The discovery test above settles it whatever
the console says.*

## 3. Create the client in your provider

Create an **OpenID Connect / OAuth 2.0 client** (some products call it an
application or a provider) for Umbral. Every product asks for roughly the same
things:

| Setting | Value |
| --- | --- |
| Client type | **Confidential**. Umbral always signs in with a client secret. A "public" client, or one that relies on PKCE alone, does not work. |
| Flow / grant | **Authorization code** (sometimes "Standard flow"). Leave implicit, device and password flows off. |
| Redirect URI | The callback URL from step 1: `<UMBRAL__PUBLIC_ORIGIN>/connect/<identifier>/callback`. Exact match, with no wildcards. |
| Token endpoint authentication | `client_secret_basic` or `client_secret_post`. Either is fine; "client secret" or "client authentication: on" in most consoles. Umbral uses Basic when your provider allows it. |
| Signing algorithm for the identity token | **RS256** or **ES256** (the others in the RS, PS and ES families work too). **Not HS256.** See below. |
| Scopes | `openid`, `email` and `profile`. These are what Umbral always asks for. If your provider needs another scope before it releases something (a `groups` claim, say), add it as an extra scope in step 4. |

Copy the **client ID** and the **client secret** the provider gives you. Most
products show the secret once.

Umbral also sends PKCE (`S256`) when your provider says it supports it. You do
not need to turn it on or off.

### Signing: RS256 or ES256, not HS256

An identity token can be signed with the client secret (`HS256`) or with a key
pair the provider publishes (`RS256`, `ES256`). Umbral accepts only the second:
a token anyone holding the secret could forge is not proof of who signed in.
Most products sign with a key pair out of the box. The one setting to look for
is called something like "signing key", "ID token signing algorithm" or "JWT
algorithm":

- **Authentik:** on the OAuth2/OpenID provider, set **Signing Key** to a
  certificate (the built-in "authentik Self-signed Certificate" is fine). With
  none selected, Authentik signs with the client secret and `HS256`, and Umbral
  refuses it.
- **Keycloak:** realms sign with `RS256` by default. If someone changed it,
  **Clients → your client → Advanced → Fine grain OpenID Connect configuration →
  ID token signature algorithm** can be set to `RS256`.
- **Pocket ID and Authelia:** both are expected to sign with a key pair by
  default. If you changed the algorithm, choose RS256 or ES256. *(Unverified for
  2026: neither the default nor the setting names. Look for "signing algorithm"
  in their configuration.)*

*(Authentik and Keycloak setting names are as of writing, unverified for 2026.)*

Umbral accepts `RS256/384/512`, `PS256/384/512` and `ES256/384/512`. An
algorithm outside that list, such as `EdDSA`, is refused the same way `HS256` is.

## 4. Add it in Umbral

Open **Connectors** in the admin area, and under **Add another connector** fill
in the form:

| Field | What to enter |
| --- | --- |
| **Provider** | **OpenID Connect provider** |
| **Identifier** | The identifier from step 1, for example `home`. It becomes part of the callback URL and cannot be changed afterwards. |
| **Button label** | The name people see: **Sign in with** *this*. Up to 64 characters, for example `Home` or `Club`. |
| **Issuer URL** | The issuer from step 2. |
| **Client ID** | From your provider. |
| **Client secret** | From your provider. Required. |
| **Extra scopes** | Optional. More scopes to ask for besides `openid email profile`, separated by spaces, like `groups offline_access`. Leave it empty if you are not sure. |
| **Shared with every app** | Ticked by default: every app set to "all shared connectors" offers it. Untick it to offer this provider only to the apps that choose it (**Apps → Sign-in methods**). |

The form carries the email warning from the top of this page. Click **Add
connector**.

Umbral then asks your provider about itself (that is the check of the issuer)
before it saves anything, so the click can take a few seconds. When the provider
answers and describes itself correctly, the connector is saved and **enabled**,
and a card for it appears on the screen with its **Redirect URI to register with
the provider**. Register that URL in your provider if you have not already. No
restart is needed: people see **Sign in with** your label on Umbral's sign-in
page from the next page load.

The secret is stored encrypted and shown masked afterwards (its last four
characters). It is never shown again: to change it, type a new one into
**Replace client secret** on the card.

### If the provider cannot be reached

If Umbral cannot reach the issuer at all when you add it (the provider is down,
the address does not resolve, it times out, or the answer is not a discovery
document), the connector is saved **disabled**, with a warning that names the
reason. It is not offered to anyone. Once your provider is up and
`<issuer>/.well-known/openid-configuration` opens in a browser, press **Enable**
on its card: Umbral runs the check again, and enables the connector only if it
passes.

A provider that **answers wrongly** is never saved: a different issuer in its
discovery document, an endpoint that is not https, no asymmetric signing, no
client secret method Umbral can use. The form keeps what you typed and the
message says what to change.

### Keeping it in the configuration file

The provider can also live in Umbral's [configuration
file](../../README.md#configuration-as-code), next to your other connectors.
It is a connector entry with `kind: oidc` and the issuer:

```yaml
connectors:
  - slug: home                       # fixed: it is in the callback URL
    kind: oidc
    issuer: https://auth.example.com/application/o/umbral   # fixed: people are recognised by it
    display_name: Home
    client_id: umbral
    client_secret: ${UMBRAL_HOME_SECRET}
    enabled: true
    extra_scopes: [groups]           # optional
```

Umbral never contacts the provider when it applies the file, so it starts even
if the provider is down; a provider that is wrong fails its sign-ins with the
usual message instead. The `enabled` value is written as it is. The issuer
follows the same rules as the form (https, no `?` or `#`), and a file that gives
an existing identifier a **different issuer**, or an issuer another connector
already has, stops Umbral from starting and says which entry: delete the
connector, or use a new identifier. `GET /admin/config` exports the provider
in this form.

### The issuer and the identifier cannot be changed

Both are shown on the card and are read-only. The issuer is part of how Umbral
recognises every person who signs in through this provider (see
[What Umbral takes from your provider](#what-umbral-takes-from-your-provider)),
and the identifier is in the callback URL registered with your provider. To
change either, **delete the connector and add it again**. Nobody loses their
account: **people are recognised by the issuer, so adding a provider back with
the same issuer (under any identifier) returns everyone to the same Umbral user.**
A different issuer, even for the same provider, starts everyone as a new user.

Two connectors cannot share one issuer.

## What people see

1. They click **Sign in with** your label on Umbral's page.
2. Your provider asks them to sign in if they are not already. Some products
   show a consent page the first time.
3. They are sent back to Umbral, which sends them on to the app they came from.

## What Umbral takes from your provider

- **Identity** is the pair of your provider's issuer and the person's `sub`
  (their stable id at your provider). A person who changes their username or
  email is still the same Umbral user. Two providers that happen to give out the
  same `sub` are two different people in Umbral.
- **Name** is the `name` claim, or `preferred_username` if there is no `name`.
  **Avatar** is `picture`. If the identity token lacks an email or a name, and
  your provider has a userinfo endpoint, Umbral asks it, and refuses the sign-in
  if the answer is about a different person.
- **Email** is the `email` claim. It can link to the same person's account from
  another provider only when **`email_verified` is the JSON value `true`**.
  A string `"true"`, a `1`, a missing flag and `false` all count as unverified.
  Some providers send the string form; for those, the account is created but
  never linked by email.

### What your apps see

Apps receive Umbral's tokens, not your provider's. The `idp` claim in them is
`oidc` for every OpenID Connect provider, never the identifier or the issuer:
you can delete a provider and add it back under another identifier, so that is
not a value to hand an app. See
[Integrate an app](../../README.md#integrate-an-app).

## A provider that is only on plain http

Umbral refuses an issuer that is not `https://`. (The one exception is a
`localhost` provider while Umbral itself runs in its plain-http laptop-trial
mode.) The refusal reads "The issuer must start with https://" and sends you
here.

The fix is the same as for Umbral itself: put the provider behind the reverse
proxy that already fronts Umbral. The repository's `docker-compose.yml` runs
Caddy, so add a second site to its Caddyfile. Suppose your provider listens on
`http://192.168.1.20:9000` and you want it at `https://id.example.com`:

```
${UMBRAL_DOMAIN} {
  reverse_proxy umbral:8080
}

id.example.com {
  reverse_proxy 192.168.1.20:9000
}
```

In `docker-compose.yml` that goes in the `content:` of the `caddyfile` config.
Then:

- Point `id.example.com` at this host in DNS, like Umbral's own domain. Caddy
  gets the certificate by itself once DNS does. Umbral's container must trust
  that certificate, so use a publicly trusted one (Caddy's default) and **not**
  `tls internal`: Umbral has no setting for a private certificate authority.
- **Tell the provider its public address.** Most products build the `issuer` in
  their discovery page from a "public URL", "external URL" or "hostname" setting.
  If it still says `http://192.168.1.20:9000`, Umbral will answer with
  "describes itself as …" (see below). Set it to `https://id.example.com`.
- Use `https://id.example.com` as the **Issuer URL**, and register the callback
  with the provider over that address.
- Umbral's container must be able to reach `https://id.example.com`. If your DNS
  only resolves from outside your network, check from the machine running Umbral
  (`curl https://id.example.com/.well-known/openid-configuration`).

*The Caddy snippet follows the repository's compose file but was not run
against a real provider.*

## Troubleshooting

**"That address describes itself as … not …"**
Umbral found a discovery page at the address you typed, but its `issuer` line
says something else, and Umbral does not accept that. Copy the issuer exactly as
the discovery page spells it, including any path. Common causes:

- Authentik: the address is missing `/application/o/<application-slug>/`, or has
  another application's slug.
- Keycloak: the wrong realm, a missing or extra `/auth`, or Keycloak is behind a
  proxy and builds its issuer from an internal host name. Set Keycloak's public
  hostname so its issuer is the address people use.
- Any provider behind a reverse proxy: the provider's own "public URL" setting
  still names the internal address (see the Caddy section).
- A trailing slash is **not** the problem: Umbral ignores one.

**"The issuer must start with https://"**
Umbral does not talk to a provider over plain http. See
[A provider that is only on plain http](#a-provider-that-is-only-on-plain-http).
The same applies to a provider whose discovery page names an endpoint over
`http://`: the message names the field.

**"Umbral could not reach …", or the connector was saved but is disabled**
The provider did not answer a request for its discovery page (or answered with
something that is not one). From the machine running Umbral, check
`curl -i <issuer>/.well-known/openid-configuration`: it should return JSON. If it
does not, the problem is the network, DNS, the certificate (Umbral's container
must trust it) or the provider being down. When it works, press **Enable** on the
card.

**"This provider only signs with a shared secret"**
The provider's discovery page lists only `HS256`-family signing. Switch the
client's signing to `RS256` or `ES256` (see
[Signing](#signing-rs256-or-es256-not-hs256)), then add the provider again.
A provider that does not announce its signing algorithms is not caught when you
add it: the form is accepted and the first sign-in fails. If people see "Sign-in didn't complete"
and the security log (below) says `id_token_invalid: algorithm`, this is it.
*(Unverified: which products announce their algorithms.)*

**"Umbral signs in as a confidential client, so it needs the client secret"**
The secret is required. Create the client as **confidential** (see step 3) so
the provider generates one.

**Someone clicks the provider and sees "Sign-in didn't complete"**
They see nothing technical; the cause is in Umbral's security log. Open it as
the README describes in [Troubleshooting](../../README.md#troubleshooting) and
look at the `detail` of the latest `sign_in_failed` row for this connector:

| `detail` | What it means and what to do |
| --- | --- |
| `id_token_invalid: signature` | The identity token is not signed by a key your provider publishes. After a key rotation Umbral fetches the new keys by itself (once every five minutes at most), so if it persists: the client belongs to a different provider than the issuer you typed, or something between Umbral and the provider changes the token. |
| `id_token_invalid: algorithm` | Signed with `HS256`, `none`, or an algorithm Umbral does not accept. See "only signs with a shared secret" above. |
| `id_token_invalid: issuer` | The token's `iss` is not the issuer you configured. Same causes as "describes itself as …". |
| `id_token_invalid: audience` | The token was issued to a different client ID than the one in Umbral. Check the **Client ID** on the card matches the client in your provider. |
| `id_token_invalid: expired` (or `not_yet_valid`) | The clocks of Umbral's host and your provider's host disagree by more than two minutes. Fix the time on both: run NTP, `timedatectl`, or on a Raspberry Pi check that it has a clock source. |
| `id_token_invalid: nonce` | The token is not for this sign-in. Rare: usually a stale tab opened from an old attempt. Try again from the start. |
| `id_token_missing` | The provider's answer had no identity token. The client must request the `openid` scope and the provider must be able to issue ID tokens. |
| `userinfo_subject_mismatch` | The provider's userinfo endpoint described a different person than the identity token did. This is a provider or proxy problem: Umbral refuses on purpose. |

A token endpoint that answers with an error is recorded as a failed sign-in
too: check the client secret (use **Replace client secret** on the card), the
redirect URI registered in the provider, and that the client is confidential
with the authorization-code flow on.

**The provider says the redirect URI is invalid or does not match**
The URL registered in the provider is not byte-identical to the one on Umbral's
card. Copy it again from **Redirect URI to register with the provider**, and
check `http` against `https`, the port, a trailing slash and upper/lower case.
Also check `UMBRAL__PUBLIC_ORIGIN`. See
[Troubleshooting](../../README.md#troubleshooting).

**Signing in works, but people are not linked to their other accounts**
Linking needs an email your provider marks `email_verified: true`, as a real
JSON `true`. Check what your provider sends: with `curl` on its userinfo
endpoint, or by decoding an identity token. Some providers
only mark an address verified once the person or an admin has confirmed it.

## Allowed email domains

On the Connectors screen you can limit who can sign in by email domain, for the
whole instance or for one connector (see
[Who can sign in](../../README.md#who-can-sign-in)). While a rule applies, only
an email your provider marks `email_verified: true` counts, and the domain must match exactly.

- A provider that does not send `email_verified` as the JSON value `true`
  refuses everyone while a rule applies. Check what yours sends (see
  [Troubleshooting](#troubleshooting)).
- How far to trust the domain depends on how your provider verifies addresses.
  If it only marks an address verified once an admin or the person has
  confirmed it, a rule is as strong as that step.

To limit one app, create a connector for it with the domain rule and offer only
that connector to the app. For example, add a second OpenID Connect connector, "Company login",
with your domain in **Allowed email domains**, then on **Apps** set the app's
**Sign-in methods** to **Only the connectors I choose** and tick only that
connector. See [Who can sign in](../../README.md#who-can-sign-in).

## Gotchas

- **One client per Umbral connector.** Create a client per connector; the
  redirect URI is different for each identifier.
- **The secret is shown once** by most providers. If you lose it, generate a new
  one in the provider and paste it into **Replace client secret** on the card.
- **Disabling or deleting** a provider warns you first if an app would be left
  with no way to sign in, as for any connector. Deleting keeps everyone's
  account.
- **Rotating the provider's signing keys** needs nothing from you: Umbral reads
  the published keys again when it sees a token signed with one it does not
  know.
