# Umbral

A self-hosted OpenID Connect provider whose only job is **signing people in
without passwords**: with a social provider they already use, or with a
passkey. Your apps talk to Umbral using any standard OIDC library. Umbral talks
to Discord, GitHub, Google, Apple and Microsoft, to Mastodon and GoToSocial servers, to any
OpenID Connect provider you already run, such as Authentik, Keycloak, Pocket ID
or Authelia, and through that same connector to a forge, such as GitLab, Codeberg,
Forgejo or Gitea. It can also sign people in with a passkey on its own. You wire each
provider up once, in one place, instead of in every app.

It ships as **one container with an embedded SQLite database**: no Postgres, no
Redis, no sidecar. It is meant for a homelab or a small project: a Raspberry Pi
(64-bit OS) is enough.

It has three concepts, and that is the whole product:

| Concept | What it is |
| --- | --- |
| **App** | A program that signs people in through Umbral. It gets a client ID and a client secret. |
| **Connector** | Umbral's identity at an upstream provider (Discord, GitHub, Google, Apple, Microsoft, or an OpenID Connect provider of your own): the OAuth application you create there, with its credentials. There can be several per provider, except Apple and Microsoft, where there is one per instance. Three other connectors are one per instance too: **passkey**, where Umbral is the provider and there is nothing to create anywhere, **Fediverse**, where there is nothing to create either because Umbral registers itself with each Mastodon or GoToSocial server the first time someone from it signs in, and **Bluesky**, where there is nothing to create because Umbral publishes a small document about itself that each person's server reads. |
| **User** | A person who has signed in at least once. |

**What Umbral is not.** It has no realms, tenants, groups, roles or permissions,
no SAML or LDAP, no MFA, no local password accounts and no login-flow builder,
and it will not get them. A passkey is neither MFA nor a password account: it
is the whole sign-in, never a second step after another one, and there is no
password to begin with. It answers "who is this person?"; what they may do is
the app's job. If you need any of those, use
[Keycloak](https://www.keycloak.org/) or [Authentik](https://goauthentik.io/),
which are good at it.

## Contents

On this page:

- [Requirements](#requirements)
- [Quick start: a first login on your own machine](#quick-start-a-first-login-on-your-own-machine)
- [Integrate an app](#integrate-an-app)
- [Run it for real, behind HTTPS](#run-it-for-real-behind-https)
- [Your data: back it up](#your-data-back-it-up)
- [Contributing](#contributing)

Everything else is in the documentation, at **<https://cosmic-chimps.github.io/umbral/>**,
with a sidebar and search. It is built from the Markdown in [`docs/`](docs/), which
reads the same here on GitHub.

- **Start:**
  [Integrate an app](https://cosmic-chimps.github.io/umbral/integrate/) ·
  [Samples](https://cosmic-chimps.github.io/umbral/samples/) ·
  [Native and mobile apps](https://cosmic-chimps.github.io/umbral/native-apps/)
- **Connectors:**
  [Discord](https://cosmic-chimps.github.io/umbral/connectors/discord/) ·
  [GitHub](https://cosmic-chimps.github.io/umbral/connectors/github/) ·
  [Google](https://cosmic-chimps.github.io/umbral/connectors/google/) ·
  [Apple](https://cosmic-chimps.github.io/umbral/connectors/apple/) ·
  [Microsoft](https://cosmic-chimps.github.io/umbral/connectors/microsoft/) ·
  [Your own OpenID Connect provider](https://cosmic-chimps.github.io/umbral/connectors/oidc/) ·
  [GitLab, Codeberg, Forgejo and Gitea](https://cosmic-chimps.github.io/umbral/connectors/oidc/#a-public-forge-gitlab-codeberg-or-one-you-run) (through the OpenID Connect connector; read its email note) ·
  [Passkeys](https://cosmic-chimps.github.io/umbral/connectors/passkey/) ·
  [Fediverse](https://cosmic-chimps.github.io/umbral/connectors/fediverse/) ·
  [Bluesky](https://cosmic-chimps.github.io/umbral/connectors/bluesky/)
- **Who can sign in:**
  [Who can sign in](https://cosmic-chimps.github.io/umbral/who-can-sign-in/) ·
  [Brand an app's sign-in page](https://cosmic-chimps.github.io/umbral/branding/) ·
  [How it looks](https://cosmic-chimps.github.io/umbral/look/)
- **Run it:**
  [HTTPS and proxies](https://cosmic-chimps.github.io/umbral/run/https/) ·
  [Back up your data](https://cosmic-chimps.github.io/umbral/run/backup/) ·
  [Passkeys for the admin area](https://cosmic-chimps.github.io/umbral/run/operator-passkeys/) ·
  [Forgotten operator credential](https://cosmic-chimps.github.io/umbral/run/operator-credential/) ·
  [Secret stores](https://cosmic-chimps.github.io/umbral/run/secret-stores/) ·
  [AWS Secrets Manager](https://cosmic-chimps.github.io/umbral/secret-stores/aws/) ·
  [Google Cloud Secret Manager](https://cosmic-chimps.github.io/umbral/secret-stores/gcp/) ·
  [Azure Key Vault](https://cosmic-chimps.github.io/umbral/secret-stores/azure/) ·
  [BellaBaxter](https://cosmic-chimps.github.io/umbral/secret-stores/bellabaxter/) ·
  [Deploying on Caravana](https://cosmic-chimps.github.io/umbral/run/caravana/) ·
  [Settings reference](https://cosmic-chimps.github.io/umbral/run/settings/) ·
  [Troubleshooting](https://cosmic-chimps.github.io/umbral/run/troubleshooting/)
- **Reference:**
  [Release notes](https://cosmic-chimps.github.io/umbral/release-notes/) ·
  [The vision](https://cosmic-chimps.github.io/umbral/vision/)

## Requirements

- **Docker.** Nothing else is needed to run Umbral.
- **A 64-bit machine**: `linux/amd64` or `linux/arm64` (a Raspberry Pi 4 or 5
  with a 64-bit OS works). Umbral is designed to idle under 150 MB of memory (under 160 MB with an
  external secret store).
- **For a real deployment: a domain name and HTTPS.** Umbral refuses an
  `http://` address for anything but `localhost`, so a bare LAN IP such as
  `http://192.168.1.50:8080` does not work. See
  [Run it for real](#run-it-for-real-behind-https). To try Umbral first, the
  quick start below needs neither.
- **An account with at least one provider** (GitHub is the least friction) to
  create an OAuth app in, or an OpenID Connect provider of your own. Umbral
  cannot do that for you. Or the **passkey connector**, which needs no account
  anywhere: it needs HTTPS (or `localhost`) and a device that holds passkeys,
  such as a phone, a laptop or a security key. See [Passkeys](docs/connectors/passkey.md). Or the
  **Fediverse connector**, which also needs no account to create: people sign in
  with an account on a Mastodon or GoToSocial server. See the
  [Fediverse walkthrough](docs/connectors/fediverse.md). Or the **Bluesky
  connector**, which needs no account to create either: people sign in with
  their Bluesky handle, but Umbral must be at an https address without a port.
  See the [Bluesky walkthrough](docs/connectors/bluesky.md).

The quick start builds the image from this repository, so it works from any
checkout. Each release is also published as `ghcr.io/cosmic-chimps/umbral`, for
`linux/amd64` and `linux/arm64`, tagged `latest` and with its version (for
example `0.9.0`), so you can pull it instead of building it.

## Quick start: a first login on your own machine

This gets one real login working on a laptop in a few minutes. It uses plain
`http://localhost`, which Umbral allows only when you ask for it explicitly.
**It is for a laptop only. Do not expose this setup to a network.** The
production setup is [further down](#run-it-for-real-behind-https).

### 1. Build the image

```bash
git clone https://github.com/Cosmic-Chimps/umbral.git umbral
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
least 12 characters. It is a password. It is the only way into the admin area
at first and it is not tied to any provider, so a broken Discord or Google app
can never lock you out. Use a password manager: there is no recovery screen
(but see [Forgotten operator credential](docs/run/operator-credential.md)). To
change it later while signed in, open **Credential** in the side rail: under
**Password** it asks for the current one and the new one twice, and signs every
other admin session out.

Once you are in, you can add a passkey under **Credential** and sign in with
it from the sign-in page with **Sign in with a passkey**. When you have one,
you can turn password sign-in off there (see
[Passkeys](docs/run/operator-passkeys.md)).

> `/admin` always takes you to the right place: the setup page the first time,
> the sign-in page afterwards. `/` is a short public home page (what Umbral is,
> the issuer URL for app developers, and a link to `/admin`); it shows nothing
> about how this instance is set up.

You land on the **Connectors** screen. The admin area has three screens,
**Apps**, **Connectors** and **Users**, and the **Credential** page.

#### Claim an instance from a script

To claim an instance without opening the setup page, start the container and
run this once:

```bash
docker exec umbral /app/Umbral reset-credential
# compose:
docker compose exec umbral /app/Umbral reset-credential
```

On an instance nobody has claimed, it claims it and prints a password, once.
Have your script store it. The setup page then no longer asks for a token: it
sends you to the sign-in page. Run on an instance that is already claimed, the
same command replaces the password (see
[Forgotten operator credential](docs/run/operator-credential.md)).

### 4. Add a connector (GitHub)

1. On **Connectors**, choose **GitHub** in the list. Under **Set-up**, find
   **Redirect URI to register with the provider** and copy it. It is
   `<origin>/connect/github/callback`, here
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
     [Naming the provider's application](docs/integrate.md#naming-the-providers-application).)
   - **Homepage URL**: anything, for example `http://localhost:8080`.
   - **Authorization callback URL**: the URL you copied in step 1.
   - Leave **Enable Device Flow** unchecked.
4. Click **Register application**, copy the **Client ID**, then click
   **Generate a new client secret** and copy it right away. GitHub shows it only
   once.
5. Back in Umbral, paste both into GitHub's **Settings** and click
   **Save connector**. It is enabled immediately. No restart is needed, now or
   whenever you change a connector later.

(Menu names are as of writing; GitHub moves things around occasionally. More
detail and the gotchas are in the [GitHub walkthrough](docs/connectors/github.md).)

GitHub is one way to get a first login. The other is the **Passkey** entry in the
same list, which needs no account anywhere: see [Passkeys](docs/connectors/passkey.md). The
**Fediverse** and **Bluesky** entries need nothing created either: see the
[Fediverse walkthrough](docs/connectors/fediverse.md) and the
[Bluesky walkthrough](docs/connectors/bluesky.md).

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
  offers; see [Giving one app its own name](docs/integrate.md#giving-one-app-its-own-name). You
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
standard JWT) carries `sub`, `email`, `email_verified`, `name` and `idp` (with a passkey
sign-in, `idp` is `passkey` and there is no `email`: see [Passkeys](docs/connectors/passkey.md)).

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

Working examples are in [`samples/`](samples/README.md): one small app per
stack, each signing in with its stack's ordinary OpenID Connect library and
showing the claims on a private page. Today there are seven:
[ASP.NET Core](samples/aspnetcore/README.md),
[FastAPI](samples/fastapi/README.md) and
[Next.js](samples/nextjs/README.md) (each a **Web application**), a
[browser-only app](samples/spa/README.md), an
[Android app](samples/android/README.md), an
[iOS app](samples/ios/README.md) and a
[.NET MAUI app](samples/maui/README.md) for both (each a **Single-page or
native app**, with no secret). CI runs each web sample against a real Umbral before every release. For an iPhone, Android or
desktop app, see [Native and mobile apps](docs/native-apps.md).

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

Beyond these: [what an app can count on](docs/integrate.md#what-an-app-can-count-on) (the flow,
the scopes and every claim), [the endpoints](docs/integrate.md#endpoints),
[signing out](docs/integrate.md#signing-out),
[what happens when a person is disabled or deleted](docs/integrate.md#when-a-person-is-disabled-or-deleted),
[naming the provider's application](docs/integrate.md#naming-the-providers-application),
[giving one app its own name](docs/integrate.md#giving-one-app-its-own-name) and
[how people are identified](docs/integrate.md#how-people-are-identified).

## Run it for real, behind HTTPS

Umbral serves plain HTTP on port 8080 inside the container and expects a reverse proxy in front
of it to terminate TLS. The repository's [`docker-compose.yml`](docker-compose.yml) does this
with Caddy, which also fetches and renews the certificate: point a domain name at the machine,
make ports 80 and 443 reach it, and run `UMBRAL_DOMAIN=auth.example.com docker compose up -d`.
The rest is in [Run it for real, behind HTTPS](docs/run/https.md): why `UMBRAL__PUBLIC_ORIGIN`
must be exactly the address people use, and what another reverse proxy must send. On a Caravana
host, see [Deploying on Caravana](docs/run/caravana.md).

## Your data: back it up

Everything Umbral keeps is in one directory, `/data`: the database and the keys. If you lose it,
you lose every connector secret and every app secret, and every issued token stops working. Back
it up, with Umbral stopped, and test that you can restore it.
[Your data: back it up](docs/run/backup.md) has the commands, and what an upgrade does to the
database.

## Contributing

```bash
dotnet test        # from the repository root; no Docker or network needed
```

Umbral is built on .NET 10 (F#), [Oxpecker](https://github.com/Lanayx/Oxpecker),
[OpenIddict](https://documentation.openiddict.com/) and SQLite. The project's
rules are in [`.specify/memory/constitution.md`](.specify/memory/constitution.md)
and its specifications are in [`specs/`](specs/), one folder per feature.
The vision is in [docs/vision.md](docs/vision.md).
A change people who run Umbral will notice also gets a note, one file in
[`changelog.d/`](changelog.d/README.md); [Release](docs/run/caravana.md#release) says how it is used.
The documentation site is built from this README and `docs/` by [`site/`](site/README.md),
with Node, in CI only: nothing of it is in the image.

The UI loads nothing from third parties: its stylesheet, htmx and its one small
script are served by Umbral itself, and every page carries a strict
Content-Security-Policy. The CSS is compiled once, not in the browser. After you
change a view, `src/Umbral/Assets/tailwind.config.js` (the design tokens) or
`src/Umbral/Assets/umbral.input.css` (the component classes), run
`src/Umbral/Assets/build-css.sh` — it downloads the pinned standalone Tailwind
CLI, no Node needed — and commit the regenerated `umbral.css`. The image build
runs it with `--check` and fails on a stale file.

## License

Umbral is released under the [MIT License](LICENSE).
