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
- [How it looks](#how-it-looks)
- [Brand an app's sign-in page](#brand-an-apps-sign-in-page)
- [Who can sign in](#who-can-sign-in)
- [Run it for real, behind HTTPS](#run-it-for-real-behind-https)
- [Deploying on Caravana](#deploying-on-caravana)
- [Your data: back it up](#your-data-back-it-up)
- [Forgotten operator credential](#forgotten-operator-credential)
- [Moving connector secrets between stores](#moving-connector-secrets-between-stores)
- [Settings reference](#settings-reference)
- [Troubleshooting](#troubleshooting)

Provider walkthroughs: [Discord](docs/connectors/discord.md) ·
[GitHub](docs/connectors/github.md) · [Google](docs/connectors/google.md) ·
[Your own OpenID Connect provider](docs/connectors/oidc.md)

Where connector secrets can live (optional):
[AWS Secrets Manager](docs/secret-stores/aws.md) ·
[Google Cloud Secret Manager](docs/secret-stores/gcp.md) ·
[Azure Key Vault](docs/secret-stores/azure.md) ·
[BellaBaxter](docs/secret-stores/bellabaxter.md)

An external store loads the manager's client library, so an instance that uses one idles under
160 MB instead of 150 MB.

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
  cannot do that for you.

The quick start builds the image from this repository, so it works from any
checkout. Tagged releases are also published to GHCR; see
[Deploying on Caravana](#deploying-on-caravana).

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
> the sign-in page afterwards. `/` is a short public home page (what Umbral is,
> the issuer URL for app developers, and a link to `/admin`); it shows nothing
> about how this instance is set up.

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

## How it looks

Umbral has one look, called Umbra: a warm gold accent on near-black, or on
paper in the light theme, with the **Gnomon** as its mark (a sundial's upright
and its shadow). The operator screens have a side rail with the four screens
and **Sign out**; on a narrow screen it stacks above the page.

**Two themes, and it follows your system.** By default Umbral is dark or light
as your operating system or browser says, and changes when it does. To pick one
yourself use the **Theme** control: *Follow system*, *Light* or *Dark*. It is at
the bottom of the side rail on the operator screens and in the footer of every
other page, the sign-in page people see included (also on an app's branded
page). It is a plain form, so it works with JavaScript off.

**The choice is one cookie, in that browser.** `umbral_theme` holds `light` or
`dark` for a year; *Follow system* removes it. Nothing about it is stored on the
server, and it is not tied to a person or an account, so each browser chooses for
itself.

**The fonts are served by Umbral itself.** Bricolage Grotesque (headings),
Schibsted Grotesk (text) and Spline Sans Mono (identifiers and secrets) are
shipped inside the image, as a Latin subset of about 84 KB in all, and loaded
from Umbral's own address. No page asks Google or anyone else for a font, so no
third party learns that someone opened your sign-in page. All three are under the
SIL Open Font License; the licence files are in `src/Umbral/Assets/fonts/` and,
in the image, in `/app/licenses/fonts/`.

## Brand an app's sign-in page

An app can show its own logo and colour on the sign-in page people see when it
sends them to Umbral. You set both on the app's card on **Apps**, under
**Sign-in page branding**. There is no global setting and no extra screen. An
app with no branding looks exactly as it did before.

**Where it shows, and where it does not.** Only on that app's sign-in page (the
one with the buttons), including the page people come back to when a sign-in
fails. Never on the admin screens: the app's card previews the colour, but the
screen around it stays Umbral's. Never on the error page Umbral shows when an
app's request cannot be sent back to the app (a redirect address you did not
register, say). That request failed its checks, and if it could carry an app's
logo, anyone could put a trusted app's logo on an error page they built.

**"Secured by Umbral" cannot be removed.** A branded page drops Umbral's name
bar and footer and shows a small **Secured by Umbral** line with the Gnomon at
the bottom instead (next to the theme control, which stays, so people can still
pick light or dark). It is always there, and no setting hides it or recolours it. It is how
a person tells Umbral's real sign-in page from a lookalike: if branding could
remove it, it would prove nothing.

### The logo

On the app's card, under **Logo**, choose a file and click **Save logo**.

- **Formats:** PNG, JPEG or WebP. Not SVG, because an SVG can carry script, and
  not a web address: Umbral never fetches a logo from anywhere else, so no third
  party learns who is signing in.
- **Replace** it by saving another file; the old logo is gone at once.
  **Remove logo** asks first, and puts the app back on no logo.

| Limit | Value | Checked by |
| --- | --- | --- |
| The file you choose | 2 MB | your browser, before anything is sent |
| The image you choose | 4096 pixels on a side | your browser |
| The logo Umbral stores | 32 to 512 pixels on a side, at most 1.25 MB | Umbral |
| One upload request | 3 MB | Umbral (more is refused, whatever it holds) |

**Cropping needs JavaScript.** When you choose a file, a crop rectangle appears
over it. Drag it to move it, drag a corner to resize it, or use the arrow keys
(hold Shift to resize); **Use whole image** puts it back. When you save, your
browser cuts the crop out, scales it down to fit 512 pixels, turns it into a PNG
and uploads that. Umbral never decodes your image. It checks the PNG's structure
and writes a new file from it, keeping the picture and dropping everything else
in it (camera details, location, comments), and stores that file.

**Without JavaScript** nothing can crop or convert, so upload a PNG that already
fits: 32 to 512 pixels on a side, not interlaced, at most 1.25 MB. Anything else
is refused and the message says what to change. A JPEG, for example, gets:
*Umbral stores logos as PNG. Turn on JavaScript on this page to crop, scale and
convert a PNG, JPEG or WebP image, or upload a PNG of at most 512 × 512 pixels.*
A refused upload changes nothing: the logo that was there stays.

### The accent colour

Type it in the **Accent colour** field as `#` and six hexadecimal digits, for
example `#d9480f`. Upper or lower case is fine. `#fff`, a colour name or
`rgb(...)` is refused with: *Enter the colour as # followed by six hexadecimal
digits, for example #d9480f.* With JavaScript on, a colour picker next to the
field fills it in for you. Leave the field empty and save to clear the colour.

The colour replaces Umbral's gold on that app's sign-in page: the buttons and
the focus outlines. It is the same colour in the dark and the light theme, whichever
one the person has. Nothing else changes, and the Gnomon in **Secured by Umbral**
keeps its own colours.

**You do not choose the text colour.** Umbral picks black or white for text on
your colour, whichever reads better, so button text is always readable
(at least 4.5 to 1) whatever colour you pick.

**A low-contrast warning, not a refusal.** Because the colour shows on both
themes, Umbral checks it against both backgrounds. If it is hard to see on either
(under 3 to 1 against the dark card or the light one), it is saved anyway and you
are told: *Saved. This colour is hard to see on Umbral's dark or light
background, so outlines and highlights in it will be faint. Buttons stay
readable.* A very light colour warns on the light theme, a very dark one on the
dark theme, and some mid-tones such as `#f26b1d` just miss on the light one;
`#d9480f` and `#c2410c` are safe on both. The card's sample button shows what
people will see.

### Where it is kept, and upgrading

Logos and colours live in `umbral.db`, so the [backup](#your-data-back-it-up)
you already take covers them. Deleting an app deletes its branding with it.

Branding adds a table, which makes the database **schema version 4**. Umbral
upgrades an older database by itself when it starts, and every app starts with no
branding, so no page changes. It does not work the other way: an older image
refuses to start on a version 4 database, and says so in `docker logs umbral`
(*...written by a newer Umbral. Run the newer image, or restore the backup of the
data directory taken before the upgrade.*). Take a backup before you upgrade, and
you can always go back.

## Who can sign in

By default, anyone with an account at an enabled provider can sign in and
become a user. To let in only some people, list the email domains you allow.
You can do it in two places:

- **For the whole instance:** on the **Connectors** screen, in the **Who can
  sign in** card at the top, fill in **Allowed email domains**, for example
  `acme.com, example.org`, and click **Save**.
- **For one connector:** open the connector's card (or the add form) and fill in
  its own **Allowed email domains** field.

Separate domains with commas, spaces or new lines. Umbral takes care of a
leading `@`, a trailing dot and capital letters. If an entry is not a domain
name, Umbral saves nothing and tells you which one to fix. Write each domain on
its own: `*.acme.com` is refused.

**Which list applies.** A connector's own list replaces the instance list for
that connector. A connector with an empty list uses the instance list. If
neither has a list, anyone can sign in, as before. Each connector's card shows
which rule applies to it.

**What counts.**

- Only an email the provider has **verified** counts. An unverified email, or
  no email at all, is refused while a rule applies.
- The domain must match **exactly**, ignoring capital letters. `eu.acme.com` is
  not `acme.com`: if you want both, list both.
- The email checked is the one the provider gives for that sign-in, through
  that connector. It is never the email stored for the user from another
  provider.

A refused person goes back to the app's sign-in page with one message: "This
account can't be used to sign in here. Try a different account or another
option below, or contact whoever runs this Umbral." It does not name a domain
and does not say whether the email was unverified or from another domain. No
user is created.

**People who are already signed in.** Saving a rule does not sign anyone out,
disable anyone or delete anyone. Instead, the rule is checked the next time a
person comes back: at their next sign-in, the next time an app asks Umbral to
authorize them with their existing session, or the next time an app refreshes
their token. If the rule no longer admits them, they are refused there. Access
tokens already issued keep working until they run out, within an hour. Their
user stays on the **Users** screen, enabled and unchanged. Sessions and refresh
tokens created before you updated Umbral to this version do not record what was
checked, so while a rule applies to them they are not accepted, and the person
signs in again.

**To limit one app.** There is no rule per app. Instead, create a connector for
the app with the domain rule and offer only that connector to the app: for
example a second Google connector, "Google (Acme)", limited to `acme.com`, and
on **Apps** set the app's **Sign-in methods** to **Only the connectors I
choose** with only "Google (Acme)" ticked. This holds because an app only
accepts a session that was started through a connector it offers. Someone
signed in through another connector still gets the app's sign-in page, where
"Google (Acme)" is the only button. If you offer that app a connector that has
no domain rule as well, the **Apps** screen warns you, and names the connector
that would let anyone in. The warning does not block saving. It does not appear
when the instance list is set, because then every connector has a rule.

**Taking a list off.** If you empty the instance list while some connector has
no list of its own, Umbral first names those connectors, which would then let
anyone in, and saves only after you click **Remove the list anyway**.

**Where to look.** Every refusal is a `sign_in_failed` event in
`security_events`, with a detail that starts `email_domain_refused`. It gives
the connector, the reason (`domain_not_allowed`, `email_not_verified` or
`no_email`), which list applied, the stage and the email's domain, but never the
full address. Every change to a list is an `allowed_domains_changed` event.

How far a provider's "verified" can be trusted differs, so each walkthrough has
an **Allowed email domains** section:
[Discord](docs/connectors/discord.md#allowed-email-domains) ·
[GitHub](docs/connectors/github.md#allowed-email-domains) ·
[Google](docs/connectors/google.md#allowed-email-domains) ·
[Your own OpenID Connect provider](docs/connectors/oidc.md#allowed-email-domains).

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

**Who may send those headers.** By default Umbral believes `X-Forwarded-*` from
any sender, which is safe only while the proxy is the one thing that can reach
Umbral's port. If anything else can reach it (other containers on a shared
network, other apps on the same host), that thing can send its own
`X-Forwarded-For` on every request, pick its own client address and so dodge the
sign-in limits, and it can claim `https` or another host in the headers too. In
that case set `UMBRAL__TRUSTED_PROXIES` to the proxy's address, for example
`UMBRAL__TRUSTED_PROXIES=192.0.2.10`. Headers from every other connection
address are then ignored and that connection's real address is used. Umbral logs
one line at startup saying which proxies it trusts.

With the bundled docker-compose and Caddy setup you can leave it unset: only
Caddy is on that compose network, so only Caddy can reach Umbral.

Use the address your proxy's connections actually arrive from, and nothing a
neighbour can send from. A shared bridge's gateway is the wrong choice: other
containers that reach Umbral through a published host port often arrive
masqueraded as that gateway.

## Deploying on Caravana

Caravana is a self-hosted PaaS on Nomad. Its Traefik already terminates TLS
and routes by domain, so on a Caravana host you use neither Caddy nor the
compose file above. You create the app in Caravana by name and the release
workflow does the rest: pushing a tag builds the image, defines the app the
first time, and tells Caravana to roll it out.

### Create the app in Caravana

In the console choose **New app**, enter the name `umbral`, and pick **Create
without deploying**. A name is enough: the app has no services yet, and the first
release gives it its definition. Then copy the app's **webhook token**
(Configure, Webhook token). It starts with `cvwh_` and is shown once.

| Setting | Value |
| --- | --- |
| Name | `umbral` (or any name, with the Bella Baxter secret `CARAVANA_APP_ID` set to it) |

The first release defines the app from
[`deploy/caravana-app.json`](deploy/caravana-app.json), so you do not type the
settings. It sets:

- the image: `ghcr.io/<owner>/umbral` (`<owner>` is the GitHub user or
  organization that owns the repository, in lowercase), tag `latest`;
- container port `8080`, with an HTTP health check on `/healthz`;
- one volume, `umbral-data`, mounted at `/data`;
- your domain (the Bella Baxter secret `CARAVANA_DOMAIN`, for example
  `auth.example.com`), routed to that port;
- `UMBRAL__PUBLIC_ORIGIN=https://<your domain>`;
- `UMBRAL__TRUSTED_PROXIES=10.99.0.1`.

To change what the first definition contains (more memory, another variable),
edit that file before the first release. It is only read when the app has no
services yet: once the app is defined, change it in the Caravana console, and
later releases keep your changes. The default memory (256 MB) is enough: Umbral
idles under 150 MB (under 160 MB with an external secret store).

`UMBRAL__PUBLIC_ORIGIN` must match the domain exactly, scheme included; the
[section above](#the-public-origin-must-match-exactly) explains why.

`UMBRAL__TRUSTED_PROXIES=10.99.0.1` tells Umbral to believe `X-Forwarded-*`
only from Traefik. On a host set up by Caravana's installer, Traefik reaches
Umbral from the `caravana0` address, `10.99.0.1`, on every host. Other apps on
the same host can reach Umbral's port directly (Caravana does not isolate apps
from each other yet). Without this setting, they could claim any client address
and get past the sign-in limits. Their direct requests arrive from their own
container address or from the bridge gateway (`172.26.64.1`), never from
`10.99.0.1`, so do not trust the gateway.

The app is stored with the moving tag `latest`. That is fine: Caravana pins the
tag to a digest on every deploy, so each rollout is a specific image and
Caravana can roll it back. (The workflow does not store the digest itself: the
webhook only redeploys when the stored tag now resolves to a new digest.)

**The GHCR package.** A new GHCR package is usually private. You have
two choices:

- **Make it public** (the simpler choice). On GitHub, open the package (your
  profile or organization, Packages, `umbral`), then Package settings, Change
  visibility. Caravana then pulls anonymously. The image holds no secrets: every
  secret lives in `/data`.
- **Keep it private** and give Caravana a registry login: Settings, Registries,
  with a GitHub token that has `read:packages`. The trade-off is that Caravana
  puts the registry token in the app's container environment, so Umbral's
  container can read it.

If the first rollout cannot pull the image, the workflow fails with the error
Caravana reports; fix the package visibility or the registry login and run the
release again.

### Connect the repository

The release workflow reads the Caravana settings and tokens from
Bella Baxter and signs in to it with the job's GitHub
OIDC token (workload identity). No secret is stored in GitHub, so there is
nothing in the repository to leak or rotate.

**In Bella Baxter**, create a project and an environment (the examples use the
project `umbral-workflow` and the environment `production`) holding these
secrets:

| Secret | Value |
| --- | --- |
| `CARAVANA_URL` | The console domain, for example `https://caravana.example.com`, with no trailing slash. |
| `CARAVANA_DOMAIN` | The domain Umbral is served on, for example `auth.example.com`, without `https://`. Needed for the first release, which defines the app with it; ignored afterwards. |
| `CARAVANA_TOKEN` | An API token that lets the workflow define the app the first time and read the rollout: Settings, API tokens, or `sudo caravana admin create-token --name umbral-ci` on the host. It starts with `cvat_`. |
| `CARAVANA_WEBHOOK_TOKEN` | The app's webhook token: Configure, Webhook token. It starts with `cvwh_` and is shown once. It can only trigger a redeploy. |
| `CARAVANA_APP_ID` | Optional. The app's name, if it is not `umbral`. |
| `CARAVANA_BACKUP_SCHEDULE` | Optional. A five-field cron line in UTC, for example `0 3 * * *`. The first release then defines the app with a volume backup (see [Back up `/data` with Caravana](#back-up-data-with-caravana)). Ignored once the app is defined. |

Then let GitHub Actions read that environment. On the environment, open
Identity, Add trust domain, and pick the GitHub template:

- Issuer: `https://token.actions.githubusercontent.com`.
- Claim rules (all must match): `repository` equals `<owner>/umbral`, and
  `environment` equals `production`. Once the repository exists, prefer
  `repository_id` equals its numeric id: it survives a rename or a transfer.
- Role: Consumer (read only). The default token lifetime of 15 minutes is enough.
- Accepted audience: the console suggests `bella-baxter/<environmentId>`. If you
  keep it, set the repository variable `BELLA_OIDC_AUDIENCE` (below) to exactly
  that. If you leave it empty, the audience is `bella-baxter`, and the variable
  stays unset.

**In GitHub**, under Settings, Secrets and variables, Actions, Variables:

| Variable | Value |
| --- | --- |
| `BELLA_BAXTER_TENANT` | Your Bella Baxter tenant code, for example `code4fun-17d1c3`. |
| `BELLA_BAXTER_PROJECT` | The project, for example `umbral-workflow`. Without it the release builds the image and skips the deploy. |
| `BELLA_BAXTER_ENV` | The environment. Optional: `production` if unset. |
| `BELLA_OIDC_AUDIENCE` | Optional. The accepted audience, only if the trust domain uses one other than `bella-baxter`. |

No GitHub secrets are needed.

Also create an **environment** named `production` (Settings, Environments). The
deploy job runs in it, and two things depend on it:

- Bella Baxter's trust domain requires `environment` to equal `production`, so
  only jobs running in that environment can read the secrets.
- Under Deployment branches and tags, choose Selected branches and tags and add
  the tag pattern `v*`. Bella Baxter cannot limit a token to certain tags, so
  this rule is what keeps a branch or an unrelated tag from deploying.

You can also add required reviewers there to approve each rollout by hand.

The first time, go in this order: create the app in Caravana (above), copy its
webhook token, put the secrets in Bella Baxter (`CARAVANA_URL`,
`CARAVANA_DOMAIN`, `CARAVANA_TOKEN`, `CARAVANA_WEBHOOK_TOKEN`; and, if you want
backups from the start, set a backup destination in Caravana and
`CARAVANA_BACKUP_SCHEDULE`), add the trust domain, set the GitHub variables and
the `production` environment, then push a tag. The first release defines the app
from `deploy/caravana-app.json`. Every later release redeploys through the
webhook and keeps the changes you made in the console.

### Release

While you work, add what changed for the people who run Umbral to
[`CHANGELOG.md`](CHANGELOG.md), under `## Unreleased` (its top says how: what
changed in plain words, under Added, Changed, Fixed, Security or Upgrade). To
release, push a tag:

```bash
git tag v1.2.3
git push origin v1.2.3
```

The workflow ([`release.yml`](.github/workflows/release.yml)) then:

1. turns `## Unreleased` into `## 1.2.3 - <today>` in its own checkout
   ([`changelog.sh`](.github/scripts/changelog.sh)), and stops here, before
   anything is built, if there is nothing under it: no empty releases;
2. runs the same tests as CI;
3. builds `linux/amd64` and `linux/arm64` and pushes `ghcr.io/<owner>/umbral:1.2.3`
   and `:latest`;
4. calls the app's webhook and waits, up to 10 minutes, until Caravana reports the
   rollout healthy. If the app has no services yet (the first release), it
   defines the app from `deploy/caravana-app.json` with the API token instead,
   using the image it just built. It fails if the rollout fails or is reverted,
   and prints the deployment's warnings
   ([`caravana-deploy.sh`](.github/scripts/caravana-deploy.sh));
5. publishes the GitHub release, with that changelog section as its notes
   (and GitHub's list of commits after it). It does not wait for the rollout,
   so a failed rollout still has a published image and a release. It then
   commits the renamed section to `main` as `docs(changelog): release 1.2.3`,
   but only if `main` is still the tagged commit. If `main` has moved on, the
   workflow warns instead, and you run `.github/scripts/changelog.sh prepare
   1.2.3` on `main` by hand, keeping any newer entries under `## Unreleased`.
   The tag never moves;
6. deletes image versions older than the last 10.

Re-running a release is safe: when the image has not changed, Caravana deploys
nothing and the workflow says so.

Things to know:

- **A deploy briefly stops the app.** `/data` is a volume that only one copy can
  use, so Caravana stops the old copy before it starts the new one: a few seconds
  of downtime, and a rollout takes 15 to 30 seconds. Pick a quiet moment, or put
  a required reviewer on the `production` environment.
- **Prereleases do not deploy.** A tag with a hyphen, such as `v1.2.0-rc.1`,
  publishes its image but does not move `latest` and does not call Caravana. Pull
  it by tag to try it. It gets a GitHub prerelease whose notes list what is under
  `## Unreleased`, and it leaves `CHANGELOG.md` alone, so the real release can
  still claim those entries.
- **After merging a released `main` into a feature branch, check your entries.**
  The merge brings in the renamed section; make sure the entries you added are
  still under `## Unreleased`, not under a version that has already shipped.
  Nobody edits a released section.
- **Re-running a release is safe for the notes too.** If the GitHub release
  already exists, the run replaces its notes with the changelog section instead
  of failing.
- **Only the last 10 versions are kept**, so Caravana's "Redeploy this version"
  reaches 10 releases back; for an older one, whose image was deleted, Caravana
  refuses it. The pruning
  is careful with multi-arch images: it deletes an old version together with its
  own per-architecture parts and nothing else
  ([`prune-images.sh`](.github/scripts/prune-images.sh); run it with `DRY_RUN=1`
  to see what it would delete). It does not run when the deploy failed, so the
  image a failed rollout reverts to is never deleted under it.

### Back up `/data` with Caravana

Caravana can back up the `umbral-data` volume to the installation's S3-compatible
backup destination. The backup is part of the app's definition: the console cannot
turn it on, and a backup policy is refused while no destination is set.

1. Set the backup destination first: Caravana, Settings, Backup destination.
   Without it Caravana refuses the definition and names the field.
2. Then either set the Bella Baxter secret `CARAVANA_BACKUP_SCHEDULE` (for
   example `0 3 * * *`) **before the first release**, which defines the app with
   `"backup": {"engine": "volume", "schedule": "<your schedule>", "keep": 7}`; or,
   for an app that is already deployed, send its definition once more with a
   `backup` object through the API, with an API token. The stored app is the
   `app` field of what `GET /v1/apps/umbral` answers; add the `backup` to the
   service and post it back (values of secrets stay as they are):

   ```bash
   BASE=https://caravana.example.com     # your CARAVANA_URL
   curl -s -H "Authorization: Bearer $TOKEN" "$BASE/v1/apps/umbral" |
     jq '.app | .services[0].backup = {engine: "volume", schedule: "0 3 * * *", keep: 7}' |
     curl -s -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
       --data @- "$BASE/v1/apps/umbral/deployments"
   ```

   The schedule is a five-field cron line in UTC; leave it out for `0 3 * * *`.
   `keep` is how many backups stay (7 if left out).

The backup **stops Umbral for a few seconds** while `/data` is archived, so the
SQLite file and the keys are copied consistently. Schedule it for a quiet hour.
Restoring is a Caravana operation too; to copy `/data` by hand instead, see
[Your data: back it up](#your-data-back-it-up), with Umbral stopped.

## Your data: back it up

> **Everything Umbral keeps is in one directory, `/data`. If you lose it, you
> lose every connector secret and every app secret, and every issued token stops
> working.** You would re-enter each provider's client secret, regenerate each
> app's secret and update every app. (Umbral keeps only a hash of an app's
> secret, so it cannot be recovered from anywhere.) Back it up, and test that you can restore
> it.

The same copy is how you move Umbral to another machine, or rebuild it after
losing one. Start it on the copy with the same `UMBRAL__PUBLIC_ORIGIN`: the
tokens and the redirect addresses you registered with each provider depend on
it. App secrets come along, people stay signed in, and nothing else needs
re-entering.

What is in there:

| Path | What |
| --- | --- |
| `umbral.db` | SQLite database: connectors, users, apps, each app's sign-in logo and colour, the security log. (While running you will also see `umbral.db-wal` and `umbral.db-shm`: they belong with it.) |
| `keys/` | Token signing and encryption keys, and the key ring that encrypts the connectors' client secrets kept here |

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
connector secrets are in the same directory, so anyone who holds a copy of
`/data` holds every secret in it.

**With an external secret store, the connector secrets are not in `/data`.**
If you set `UMBRAL__SECRET_STORE` to `aws`, `gcp`, `azure` or `bellabaxter`
(see the [walkthroughs](docs/secret-stores/aws.md)), the backup holds only a
reference to each secret, so a restore needs the same manager, reachable with
the same credentials, to read them back. Two instances that use the same
`UMBRAL__SECRET_STORE_PREFIX` in the same manager share their secrets: give the
second one its own prefix.

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

## Moving connector secrets between stores

If you change `UMBRAL__SECRET_STORE`, connectors already saved keep their
secrets where they are until you move them. Recreate the container with the new
setting (to move out of a manager, set `local` and keep that manager's
credentials), then:

```bash
docker exec umbral /app/Umbral move-secrets --dry-run
docker exec umbral /app/Umbral move-secrets
# compose:
docker compose exec umbral /app/Umbral move-secrets
```

It prints one line per connector (`moved`, `skipped`, `failed`, `warning`) and a
summary, never a secret. It exits 1 if any connector failed; that connector
keeps its old, readable copy, and running the command again finishes the rest.
It can run while Umbral is serving: a connector you are saving or deleting at
that moment is `skipped` (**being edited; run move-secrets again**), and the
Connectors screen says **A secret move is working on this connector. Try again
in a minute.** if you touch a connector while it is being moved.
Details are in the [walkthroughs](docs/secret-stores/aws.md).

## Settings reference

Set these as environment variables on the container.

| Variable | Default | What it does |
| --- | --- | --- |
| `UMBRAL__PUBLIC_ORIGIN` | *required* | The address people reach Umbral at, e.g. `https://auth.example.com`. Scheme, host, optional port; no path. `http` is accepted only for `localhost`. |
| `UMBRAL__TRUSTED_PROXIES` | unset (believe forwarded headers from any sender) | Comma-separated IP addresses and networks, IPv4 or IPv6, whose `X-Forwarded-For`, `-Proto` and `-Host` headers Umbral believes, e.g. `192.0.2.10, 10.0.0.0/8, fd00::/8`. Spaces around entries are ignored. Set it when anything other than your reverse proxy can reach Umbral's port, so it cannot choose its own client address and avoid the rate limits. A value that is not an address or network (including a network with bits set beyond its prefix, such as `10.0.0.5/8`) stops Umbral from starting and names the entry. See [Using a different reverse proxy](#using-a-different-reverse-proxy). |
| `UMBRAL__DATA_DIRECTORY` | `/data` (set in the image) | Where Umbral keeps everything. Must be writable. |
| `ASPNETCORE_ENVIRONMENT` + `UMBRAL__INSECURE_ALLOW_HTTP` | unset | `Development` **and** `true` together allow plain `http://localhost`. Laptop trial only. Never set them on a server. |
| `UMBRAL__SECRET_STORE` | `local` | Where connector client secrets are kept: `local` (encrypted in `/data`), `aws`, `gcp`, `azure` or `bellabaxter`. Anything else stops the start. |
| `UMBRAL__SECRET_STORE_PREFIX` | `umbral` | Start of every secret's name in the manager. 1–32 lowercase letters, digits or hyphens, not starting or ending with a hyphen. |
| `UMBRAL__SECRET_CACHE_SECONDS` | `300` | How long a secret read from the manager is kept in memory. `0` turns the cache off. |
| `UMBRAL__SECRET_GRACE_SECONDS` | `3600` | How long Umbral keeps using the last secret it read when the manager cannot be reached. `0` refuses at once. |
| `UMBRAL__AZURE_KEY_VAULT_URI` | unset | The vault's `https://` address, no path. Required when the store is `azure`. |
| `UMBRAL__BELLABAXTER_PROVIDER` | unset | The BellaBaxter provider's slug; needed only when the environment has no secret provider or several. |

The cloud's own variables (`AWS_REGION`, `GOOGLE_CLOUD_PROJECT`, `AZURE_*`,
`BELLA_BAXTER_*`) are listed in each store's walkthrough:
[AWS](docs/secret-stores/aws.md) · [Google Cloud](docs/secret-stores/gcp.md) ·
[Azure](docs/secret-stores/azure.md) · [BellaBaxter](docs/secret-stores/bellabaxter.md).

## Troubleshooting

Commands below use the quick-start container name. With compose, write
`docker compose logs umbral` for `docker logs umbral`.

**The container exits right after starting.**
`docker logs umbral`: Umbral names the setting. Usual causes:
`UMBRAL__PUBLIC_ORIGIN` is missing, has a path (`https://auth.example.com/auth`)
or is `http://` with a host other than `localhost`; `UMBRAL__TRUSTED_PROXIES`
holds an entry that is not an IP address or network; or `/data` is not writable. For a bind mount, run
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
A change people who run Umbral will notice also gets an entry under `## Unreleased`
in [`CHANGELOG.md`](CHANGELOG.md); [Release](#release) says how it is used.

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
