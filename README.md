# Umbral

A self-hosted OpenID Connect provider whose only job is **social login**.
Your apps talk to Umbral using any standard OIDC library. Umbral talks to
Discord, GitHub and Google, and to any OpenID Connect provider you already run,
such as Authentik, Keycloak, Pocket ID or Authelia. You wire each provider up
once, in one place, instead of in every app.

It ships as **one container with an embedded SQLite database**: no Postgres, no
Redis, no sidecar. It is meant for a homelab or a small project: a Raspberry Pi
(64-bit OS) is enough.

It has three concepts, and that is the whole product:

| Concept | What it is |
| --- | --- |
| **App** | A program that signs people in through Umbral. It gets a client ID and a client secret. |
| **Connector** | Umbral's identity at an upstream provider (Discord, GitHub, Google, or an OpenID Connect provider of your own): the OAuth application you create there, with its credentials. There can be several per provider. |
| **User** | A person who has signed in at least once. |

**What Umbral is not.** It has no realms, tenants, groups, roles or permissions,
no SAML or LDAP, no MFA, no local password accounts and no login-flow builder,
and it will not get them. It answers "who is this person?"; what they may do is
the app's job. If you need any of those, use
[Keycloak](https://www.keycloak.org/) or [Authentik](https://goauthentik.io/),
which are good at it.

## Contents

- [Requirements](#requirements)
- [Quick start: a first login on your own machine](#quick-start-a-first-login-on-your-own-machine)
- [Integrate an app](#integrate-an-app)
- [Run it for real, behind HTTPS](#run-it-for-real-behind-https)
- [Your data: back it up](#your-data-back-it-up)
- [Forgotten operator credential](#forgotten-operator-credential)
- [Settings reference](#settings-reference)
- [Troubleshooting](#troubleshooting)

Provider walkthroughs: [Discord](docs/connectors/discord.md) ·
[GitHub](docs/connectors/github.md) · [Google](docs/connectors/google.md) ·
[Your own OpenID Connect provider](docs/connectors/oidc.md)

## Requirements

- **Docker.** Nothing else is needed to run Umbral.
- **A 64-bit machine**: `linux/amd64` or `linux/arm64` (a Raspberry Pi 4 or 5
  with a 64-bit OS works). Umbral is designed to idle under 150 MB of memory.
- **For a real deployment: a domain name and HTTPS.** Umbral refuses an
  `http://` address for anything but `localhost`, so a bare LAN IP such as
  `http://192.168.1.50:8080` does not work. See
  [Run it for real](#run-it-for-real-behind-https). To try Umbral first, the
  quick start below needs neither.
- **An account with at least one provider** (GitHub is the least friction) to
  create an OAuth app in, or an OpenID Connect provider of your own. Umbral
  cannot do that for you.

No image is published to a registry yet, so you build it from this repository.

## Quick start: a first login on your own machine

This gets one real login working on a laptop in a few minutes. It uses plain
`http://localhost`, which Umbral allows only when you ask for it explicitly.
**It is for a laptop only. Do not expose this setup to a network.** The
production setup is [further down](#run-it-for-real-behind-https).

### 1. Build the image

```bash
git clone <URL of this repository> umbral
cd umbral
docker build -t umbral .
```

The first build downloads the .NET SDK and compiles Umbral, which takes a few
minutes. On a Raspberry Pi it is slower; if you would rather build on a faster
machine and copy the image over:

```bash
docker buildx build --platform linux/arm64 -t umbral --load .
docker save umbral | ssh pi@raspberrypi docker load
```

### 2. Run it

```bash
docker run -d --name umbral \
  -p 127.0.0.1:8080:8080 \
  -v umbral-data:/data \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e UMBRAL__INSECURE_ALLOW_HTTP=true \
  -e UMBRAL__PUBLIC_ORIGIN=http://localhost:8080 \
  umbral
```

- `-v umbral-data:/data` keeps everything Umbral stores in a Docker volume.
  [Back it up](#your-data-back-it-up).
- `UMBRAL__PUBLIC_ORIGIN` is the address people type in the browser. Umbral
  refuses to start without it.
- The last two `-e` lines are the "plain http on localhost" switch. Both are
  required; either one alone is ignored.
- `127.0.0.1:` in `-p` keeps the port off your network.

### 3. Claim the instance

There is no default password. Read the one-time setup token from the log:

```bash
docker logs umbral
```

The token is in the box of `=====` lines near the end of the log. To show just
that:

```bash
docker logs umbral 2>&1 | grep -A6 "not configured yet"
```

You will see:

```
================================================================
  Umbral is not configured yet.

  Open     http://localhost:8080/admin/setup
  Token    xxxxxxxxxxxxxxxx
  ...
```

Open that address, paste the token, and choose an **operator credential** of at
least 12 characters. It is the only way into the admin area and it is not tied
to any provider, so a broken Discord or Google app can never lock you out. Use
a password manager: there is no recovery screen (but see
[Forgotten operator credential](#forgotten-operator-credential)). To change it
later while signed in, open **Credential** in the top bar: it asks for the
current one and the new one twice, and signs every other admin session out.

> `/admin` always takes you to the right place: the setup page the first time,
> the sign-in page afterwards. Umbral has no front page, so `/` answers
> "Not found".

You land on the **Connectors** screen. The admin area has three screens:
**Apps**, **Connectors**, **Users**.

#### Claim an instance from a script

To claim an instance without opening the setup page, start the container and
run this once:

```bash
docker exec umbral /app/Umbral reset-credential
# compose:
docker compose exec umbral /app/Umbral reset-credential
```

On an instance nobody has claimed, it claims it and prints a credential, once.
Have your script store it. The setup page then no longer asks for a token: it
sends you to the sign-in page. Run on an instance that is already claimed, the
same command replaces the credential (see
[Forgotten operator credential](#forgotten-operator-credential)).

### 4. Add a connector (GitHub)

1. On the **GitHub** card, find **Redirect URI to register with the provider**
   and copy it. It is `<origin>/connect/github/callback`, here
   `http://localhost:8080/connect/github/callback`. Umbral builds it for you:
   do not type it by hand.
2. In GitHub: your profile picture → **Settings** → **Developer settings**
   (bottom of the left menu) → **OAuth Apps** → **New OAuth App**
   (`https://github.com/settings/applications/new`).
3. Fill in:
   - **Application name**: the name of your Umbral instance, for example
     `Umbral` or `Cosmic Chimps Login`. People see it when GitHub asks them to
     authorize, whichever of your apps they are signing in to. Do not name it
     after one of your apps. (See
     [Naming the provider's application](#naming-the-providers-application).)
   - **Homepage URL**: anything, for example `http://localhost:8080`.
   - **Authorization callback URL**: the URL you copied in step 1.
   - Leave **Enable Device Flow** unchecked.
4. Click **Register application**, copy the **Client ID**, then click
   **Generate a new client secret** and copy it right away. GitHub shows it only
   once.
5. Back in Umbral, paste both into the GitHub card and click
   **Save connector**. It is enabled immediately. No restart is needed, now or
   whenever you change a connector later.

(Menu names are as of writing; GitHub moves things around occasionally. More
detail and the gotchas are in the [GitHub walkthrough](docs/connectors/github.md).)

### 5. Register an app

Open **Apps** and fill in:

- **Name**: anything.
- **Redirect URI**: where your app receives the login result. For this test use
  `http://localhost:3000/callback`. It is matched **exactly**; wildcards are
  refused.
- **App type**: **Web application** (the app has a server that can keep a
  secret). Pick **Single-page or native app** only for an app that cannot.
  It gets no secret and relies on PKCE alone.

- **Sign-in methods**: leave **All shared connectors** (the default). The app
  then offers every enabled connector that is shared, including ones you add
  later. Choose **Only the connectors I choose** to tick the ones this app
  offers; see [Giving one app its own name](#giving-one-app-its-own-name). You
  can change it later with **Save sign-in methods**.

Click **Create app**. A card appears with the **client ID** and **client
secret**. **Copy the secret now.** Umbral stores only a hash of it and shows it
this once. If you lose it, use **Regenerate secret**.

### 6. Prove it works, with no app of your own

Open this address in your browser (replace `CLIENT_ID`):

```
http://localhost:8080/connect/authorize?client_id=CLIENT_ID&response_type=code&redirect_uri=http://localhost:3000/callback&scope=openid%20email%20profile&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256&state=test
```

You get Umbral's sign-in page, titled **Sign in to** and the name of your app,
with a **Sign in with GitHub** button. Click it,
approve in GitHub, and the browser ends up on
`http://localhost:3000/callback?code=…&state=test&iss=…`. The page itself will
fail to load because nothing listens on port 3000, and that is fine: **a
`code=` in the address bar is the proof.** You are also now listed on the
**Users** screen.

(`code_challenge` above is the fixed example from RFC 7636, so the next command
can use its matching verifier. Use it for this test only, never in a real app.)

To trade the code for tokens, run this straight away (a code is short-lived
and works once):

```bash
curl -s -u CLIENT_ID:CLIENT_SECRET \
  -d grant_type=authorization_code \
  -d code=PASTE_THE_CODE \
  -d redirect_uri=http://localhost:3000/callback \
  -d code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk \
  http://localhost:8080/connect/token
```

The JSON has an `access_token` and an `id_token`. The `id_token` payload (a
standard JWT) carries `sub`, `email`, `email_verified`, `name` and `idp`.

## Integrate an app

An app needs exactly **three values**, all on the **Apps** screen:

| Value | Where it comes from |
| --- | --- |
| **Issuer** | Your `UMBRAL__PUBLIC_ORIGIN`, for example `https://auth.example.com` (the **Issuer URL** field on the app's card) |
| **Client ID** | The app's card |
| **Client secret** | Shown once, when you create the app |

Any OpenID Connect client library will configure itself from the discovery
document, so there is nothing Umbral-specific to install:

```
https://auth.example.com/.well-known/openid-configuration
```

And register one thing in Umbral: the app's **redirect URI**. Which one that is
depends on the library. ASP.NET Core's OpenID Connect handler uses
`https://app.example.com/signin-oidc` by default; most libraries document theirs.

A minimal ASP.NET Core example (`Microsoft.AspNetCore.Authentication.OpenIdConnect`,
the stock handler):

```csharp
builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = "Cookies";
        o.DefaultChallengeScheme = "Umbral";
    })
    .AddCookie("Cookies")
    .AddOpenIdConnect("Umbral", o =>
    {
        o.Authority = "https://auth.example.com";   // issuer
        o.ClientId = "…";
        o.ClientSecret = "…";
        o.ResponseType = "code";                    // the only flow Umbral offers
        o.UsePkce = true;                           // required
        o.Scope.Add("email");
        o.Scope.Add("profile");
    });
```

What an app can count on:

- **Authorization code flow with PKCE (`S256`) is the only flow.** PKCE is
  required even for apps with a secret, and `plain` is refused. No implicit or
  password grant.
- **Scopes:** `openid`, `email`, `profile`, and `offline_access` if you want a
  refresh token.
- **Redirect URIs are matched exactly.** A request whose `redirect_uri` is not
  the registered string is answered with an Umbral error page, not a redirect.
- **The token endpoint** (`/connect/token`) accepts the secret as HTTP Basic or
  as a form field (`client_secret_basic` or `client_secret_post`).
- **`sub`** is stable for a user and opaque: it is not derived from any
  provider's ID. If you delete a user and they sign in again, they get a
  *different* `sub`.
- **`email` and `email_verified` may be missing.** A GitHub user can keep their
  email private, and a Discord account can be unverified. Handle both.
- **`name`, `preferred_username`, `picture`** come with the `profile` scope when
  the provider supplied them. **`idp`** is Umbral's one extra claim: the
  provider the person used (`github`, `discord`, `google`, or `oidc` for any
  OpenID Connect provider you added). It is the provider's
  kind, never a connector's identifier: with several Discord connectors it is
  still `discord`. Ignore it freely.
- **Access tokens are opaque.** They are not JWTs: do not try to decode them.
  Validate them by calling `/connect/userinfo`. Disabling a user cuts their
  tokens off immediately.
- **The `issuer` in the discovery document ends with a slash**
  (`https://auth.example.com/`), and so does `iss` in the `id_token`. Libraries
  that read the discovery document do the right thing. If you hard-code the
  issuer and your library compares strings, copy it from the discovery document.

Limits of the Apps screen today: an app has **one redirect URI** (plus an
optional post-logout one), chosen when you create it, and the app's client ID is
generated. To change a redirect URI, delete the app and register it again. You
get a new client ID and secret. The app's **Sign-in methods** are different: you
can change them at any time on its card, without touching the client ID or the
secret.

### Naming the provider's application

Umbral is the one login for all your apps, so the application you create at a
provider represents **Umbral**, not one app. The provider shows that
application's name on its consent screen ("*Name* wants to access your
account") to people signing in to **every** app that uses the connector. Name it
after your Umbral instance, for example `Cosmic Chimps Login`. If you name it
after one app, people signing in to your other apps see that app's name and
think they are in the wrong place. Umbral's own sign-in page says which app
people are signing in to, so they still know where they are heading.

### Giving one app its own name

Most apps are fine with the shared name. When one app needs its own name on the
consent screen, give it its own connector:

1. Create another application at the provider, named as you want people to see
   it (for example `Frunchy`).
2. On **Connectors**, under **Add another connector**, pick the **Provider**,
   choose an **Identifier** (for example `discord-frunchy`), set the **Button
   label**, paste the **Client ID** and **Client secret**, untick **Shared with
   every app**, and click **Add connector**. The identifier is fixed once saved.
   It is part of the callback URL, `<origin>/connect/discord-frunchy/callback`,
   which the new card shows: register that URL at the provider. The first
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

### How people are identified

Signing in with the same provider account always gives the same Umbral user. If
someone then signs in through a **second provider** and both providers assert
the **same, verified email address**, Umbral links the two to one user. An email
a provider has not verified is never used to link anyone. If no email is
available, a new user is created. For an OpenID Connect provider you added, a
person is identified by that provider's issuer address together with their id
there, so two providers never merge people, and adding a provider back with the
same issuer returns everyone to the same user. You can disable or delete users on
the **Users** screen.

## Run it for real, behind HTTPS

Umbral serves plain HTTP on port 8080 inside the container and expects a reverse
proxy in front of it to terminate TLS. **The repository's
[`docker-compose.yml`](docker-compose.yml) does this with Caddy**, which also
fetches and renews the certificate for you.

Before you start:

1. Pick a **domain name** and point its DNS at the machine. A free dynamic-DNS
   name works.
2. Make ports **80 and 443** reach that machine. Caddy needs them to obtain the
   certificate. Until DNS and ports are right, the browser shows a TLS error,
   not an Umbral error.

Then, in the checkout:

```bash
UMBRAL_DOMAIN=auth.example.com docker compose up -d
docker compose logs umbral
```

(or put `UMBRAL_DOMAIN=auth.example.com` in a `.env` file next to the compose
file). `docker compose logs umbral` shows the setup URL and token, exactly as in
the quick start: look for the `=====` box at the end of the log, or
`docker compose logs umbral 2>&1 | grep -A6 "not configured yet"`. Continue from
step 3 with the **https** address.

Umbral itself is never published on a port, so everything reaches it through
Caddy. **Do not publish port 8080 on a public host.**

The image has a built-in health check: `docker compose ps` shows `healthy` once
Umbral answers and can open its database. For another orchestrator, probe
`GET /healthz` on port 8080 (`200 ok`, or `503` when the database is unusable).

> Compose names the container after the folder you cloned into, so
> `docker exec umbral …` and `docker logs umbral` may not find it. Use
> `docker compose logs umbral`, `docker compose exec umbral …` and so on.

### The public origin must match exactly

`UMBRAL__PUBLIC_ORIGIN` is the one setting Umbral cannot start without, and it
must be **exactly the address people use**: scheme, host and port, with no path.
Umbral builds the issuer and every provider callback URL from it. It never
guesses from the incoming request, because behind a proxy the request says
something else (`http://umbral:8080`).

That value must byte-match what you registered in the provider's console. When
it does not, the provider answers **`redirect_uri_mismatch`** (or Google's
"Error 400: redirect_uri_mismatch"), and the login fails. The compose file builds
the origin from `UMBRAL_DOMAIN` so the origin and Caddy's address cannot drift.
If you change the origin, the callback URL changes, so register the new one with
every provider. The Connectors screen shows a warning banner whenever you reach
it at an address that differs from the configured origin.

Umbral cannot run under a sub-path (`https://example.com/auth`): the origin must
be a bare host.

### Using a different reverse proxy

nginx, Traefik and others work, if the proxy:

- terminates TLS and talks to `umbral:8080` over plain HTTP;
- sends **`X-Forwarded-Proto`**. Without `X-Forwarded-Proto: https`, Umbral
  sees plain http and answers `This server only accepts HTTPS requests.`;
- sends **`X-Forwarded-For`**, and replaces rather than trusts a value the
  client sent. Umbral rate-limits sign-in attempts per client address (an IPv6
  client is counted per /64, and an IPv4-mapped address like
  `::ffff:203.0.113.10` as its IPv4 form), and without it every visitor looks
  like the proxy and shares one limit.

Every URL Umbral publishes comes from `UMBRAL__PUBLIC_ORIGIN`, never from the
`Host` header, so the proxy's host handling does not matter. Caddy's
`reverse_proxy` does both of the above by default. nginx needs
`proxy_set_header X-Forwarded-Proto $scheme;` and
`proxy_set_header X-Forwarded-For $remote_addr;` written out.

## Your data: back it up

> **Everything Umbral keeps is in one directory, `/data`. If you lose it, you
> lose every connector secret and every app secret, and every issued token stops
> working.** You would re-enter each provider's client secret, regenerate each
> app's secret and update every app. Back it up, and test that you can restore
> it.

The same copy is how you move Umbral to another machine, or rebuild it after
losing one. Start it on the copy with the same `UMBRAL__PUBLIC_ORIGIN`: the
tokens and the redirect addresses you registered with each provider depend on
it. App secrets come along, people stay signed in, and nothing else needs
re-entering.

What is in there:

| Path | What |
| --- | --- |
| `umbral.db` | SQLite database: connectors, users, apps, the security log. (While running you will also see `umbral.db-wal` and `umbral.db-shm`: they belong with it.) |
| `keys/` | Token signing and encryption keys, and the key ring that encrypts the stored secrets |

**Back up** = copy the directory. **Stop Umbral first**: SQLite keeps recent
writes in the `-wal` file, and a copy taken mid-write can be inconsistent. A
clean stop folds everything into `umbral.db`.

With the quick-start `docker run` container:

```bash
docker stop umbral
mkdir -p backup
docker run --rm --user root --entrypoint cp \
  -v umbral-data:/data:ro -v "$PWD/backup":/backup umbral -a /data/. /backup/
docker start umbral
```

With compose (run in the checkout):

```bash
docker compose stop umbral
mkdir -p backup
docker compose run --rm --no-deps --user root --entrypoint cp \
  -v "$PWD/backup":/backup umbral -a /data/. /backup/
docker compose start umbral
```

**Restore** is the same copy in the other direction. Umbral runs as a non-root
user (UID 1654), so the files must be owned by it:

```bash
docker stop umbral        # compose: docker compose stop umbral
docker run --rm --user root --entrypoint sh \
  -v umbral-data:/data -v "$PWD/backup":/backup:ro umbral \
  -c 'rm -rf /data/* && cp -a /backup/. /data/ && chown -R 1654:1654 /data'
docker start umbral
```

With compose:

```bash
docker compose stop umbral
docker compose run --rm --no-deps --user root --entrypoint sh \
  -v "$PWD/backup":/backup:ro umbral \
  -c 'rm -rf /data/* && cp -a /backup/. /data/ && chown -R 1654:1654 /data'
docker compose start umbral
```

If you use a bind mount (`-v ./data:/data`) instead of a named volume, copy the
host directory with `cp -a` and make it writable for UID 1654 first:
`chown -R 1654:1654 ./data`.

On Linux the copied files are owned by root; use `sudo` to move or read them.

**Treat the backup like a password database.** The keys that decrypt the stored
secrets are in the same directory, so anyone who holds a copy of `/data` holds
every secret in it.

## Forgotten operator credential

Run this on the machine that hosts the container:

```bash
docker exec -it umbral /app/Umbral reset-credential
# compose:
docker compose exec umbral /app/Umbral reset-credential
```

It replaces the credential with a freshly generated one and prints it, once.
Sign in at `/admin/signin` with it, then choose your own under **Credential**,
and store it in a password manager. Every admin session open before the reset is
ended. There is deliberately no "forgot your credential" page: reaching this
command needs access to the machine, which is more than reaching the web page
needs.

## Settings reference

Set these as environment variables on the container.

| Variable | Default | What it does |
| --- | --- | --- |
| `UMBRAL__PUBLIC_ORIGIN` | *required* | The address people reach Umbral at, e.g. `https://auth.example.com`. Scheme, host, optional port; no path. `http` is accepted only for `localhost`. |
| `UMBRAL__DATA_DIRECTORY` | `/data` (set in the image) | Where Umbral keeps everything. Must be writable. |
| `ASPNETCORE_ENVIRONMENT` + `UMBRAL__INSECURE_ALLOW_HTTP` | unset | `Development` **and** `true` together allow plain `http://localhost`. Laptop trial only. Never set them on a server. |

## Troubleshooting

Commands below use the quick-start container name. With compose, write
`docker compose logs umbral` for `docker logs umbral`.

**The container exits right after starting.**
`docker logs umbral`: Umbral names the setting. Usual causes:
`UMBRAL__PUBLIC_ORIGIN` is missing, has a path (`https://auth.example.com/auth`)
or is `http://` with a host other than `localhost`; or `/data` is not writable. For a bind mount, run
`chown -R 1654:1654 ./data`.

**I added a connector and nobody sees it.**
A connector that is **not shared** and that **no app has chosen** is offered
nowhere. The connector's card says so. Either tick **Shared with every app** on
the card, or open **Apps**, set an app's **Sign-in methods** to **Only the
connectors I choose**, tick the connector and click **Save sign-in methods**. A
disabled connector is not offered either.

**An app's sign-in page says no sign-in method is available.**
That app has no enabled connector it can offer: its chosen set is empty, or every
connector in it is disabled or deleted, or it is on **All shared connectors** and
none is shared and enabled. Fix it on **Apps** (**Sign-in methods**) or on
**Connectors**.

**Someone signed in, but the app shows its sign-in page again.**
A session counts for an app only if it came through a connector that app offers.
They signed in to Umbral through a connector this app does not offer, so they
must pick one it does. They are still signed in to Umbral for other apps.

**`/` shows "Not found".**
That is expected: Umbral has no front page. Go to `/admin`.

**I cannot find the setup token, or it is rejected.**
`docker logs umbral 2>&1 | grep -A6 "not configured yet"` and take the *latest*
`Token` line (the `=====` box near the end of the log). The token is regenerated
each time the container starts, and only matters until you set the credential.
If the credential is already set, use `/admin/signin`, or
[reset it](#forgotten-operator-credential).

**The provider says `redirect_uri_mismatch` (or "redirect URI is not valid").**
The URL registered in the provider's console is not byte-identical to the one
Umbral sends. Copy it again from the connector card
(`<origin>/connect/<slug>/callback`), check `http` vs `https`, the port, a
trailing slash and upper/lower case, and check that `UMBRAL__PUBLIC_ORIGIN` is
the address you actually use. If the Connectors screen shows an origin-mismatch
banner, fix the origin or the proxy first. Google can take a few minutes to
honour a new redirect URI.

**`This server only accepts HTTPS requests.`**
Umbral saw plain http. Either the proxy is not sending
`X-Forwarded-Proto: https` (see
[Using a different reverse proxy](#using-a-different-reverse-proxy)), or you are
talking to port 8080 directly. The laptop trial is the only place plain http is
allowed.

**My app shows "That sign-in request was rejected".**
Umbral's own error page, with a reason. The usual two: the app's `redirect_uri`
is not exactly the one registered for it (compare character by character, as it
appears in the app's authorization request), or the app is not using PKCE with
`S256`. Libraries usually have a "use PKCE" switch.

**Someone clicks a provider and sees "Sign-in didn't complete".**
They see "Sign-in didn't complete" and nothing technical; the cause is in the security log.
Typical causes: a wrong or rotated client secret (open **Connectors**, use
**Replace client secret**), or a provider app that is not allowed to log that
person in (Google "Testing" mode: see the [Google walkthrough](docs/connectors/google.md)).
To read the log, query the database from the host (needs the `sqlite3` tool):

```bash
sqlite3 -readonly /path/to/umbral.db \
  "select occurred_utc, event_type, outcome, connector_slug, detail
     from security_events order by id desc limit 20;"
```

With a named Docker volume on Linux the path is usually
`/var/lib/docker/volumes/umbral-data/_data/umbral.db` (needs `sudo`); on Docker
Desktop, [copy the directory out](#your-data-back-it-up) and open the copy.

**After a restore or a lost volume, connectors or apps stop working.**
If `/data/keys` did not come back, stored secrets cannot be decrypted and tokens
no longer validate. In **Connectors** use **Replace client secret** for each
provider, and **Regenerate secret** for each app, then update the apps.

## Contributing

```bash
dotnet test        # from the repository root; no Docker or network needed
```

Umbral is built on .NET 10 (F#), [Oxpecker](https://github.com/Lanayx/Oxpecker),
[OpenIddict](https://documentation.openiddict.com/) and SQLite. The project's
rules are in [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
and its specification is in [`specs/001-umbral-mvp/`](specs/001-umbral-mvp/).

The UI loads nothing from third parties: its stylesheet, htmx and its one small
script are served by Umbral itself, and every page carries a strict
Content-Security-Policy. The CSS is compiled once, not in the browser. After you
change a view, `src/Umbral/Assets/tailwind.config.js` (the design tokens) or
`src/Umbral/Assets/umbral.input.css` (the component classes), run
`src/Umbral/Assets/build-css.sh` — it downloads the pinned standalone Tailwind
CLI, no Node needed — and commit the regenerated `umbral.css`. The image build
runs it with `--check` and fails on a stale file.
