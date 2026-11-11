# Samples

One small app per stack, each signing in through Umbral with its stack's
ordinary OpenID Connect library. There is no Umbral SDK to install: any
standard client works, and these show the few lines it takes. Every sample
behaves the same way, so the one for your stack is the one to read.

| Sample | Stack and library | App type in Umbral |
| --- | --- | --- |
| [ASP.NET Core](aspnetcore/README.md) | .NET 10 minimal APIs, `Microsoft.AspNetCore.Authentication.OpenIdConnect` | **Web application** (a server keeps the secret) |
| [Browser-only app](spa/README.md) | Vite and plain TypeScript, `oidc-client-ts` | **Single-page or native app** (no secret: PKCE alone) |
| [FastAPI](fastapi/README.md) | Python 3.13, FastAPI, Authlib's Starlette client | **Web application** (a server keeps the secret) |
| [Next.js](nextjs/README.md) | Next.js 16 (App Router), Auth.js (`next-auth` v5) | **Web application** (a server keeps the secret) |
| [Android](android/README.md) | Kotlin, Jetpack Compose, AppAuth-Android | **Single-page or native app** (no secret: PKCE alone) |
| [iOS](ios/README.md) | Swift, SwiftUI, AppAuth-iOS | **Single-page or native app** (no secret: PKCE alone) |
| [.NET MAUI](maui/README.md) | C#, .NET MAUI (Android and iOS), `Duende.IdentityModel.OidcClient` | **Single-page or native app** (no secret: PKCE alone) |

CI runs each web sample against a real Umbral, built from this repository, and
walks through it in a headless browser: see [The harness](#the-harness). A
phone app is built in CI and walked by hand: see [A phone app](#a-phone-app). The
samples are also where the claims an app receives are tested from the app's
side, so they say the same thing as [Integrate an app](../README.md#integrate-an-app).

## What every sample does

The same three pages, the same settings, the same protocol, whichever stack.
A sample has nothing else: no styling beyond the browser's defaults, no
database, no other feature. It is the shortest working integration, not a
starter kit.

### Pages

| Page | What it does |
| --- | --- |
| `/` | A heading, a link to `/public`, a link to `/private`, and **Sign in** or **Sign out** depending on whether you are signed in. |
| `/public` | Open to anyone. |
| `/private` | When you are signed out, it starts the sign-in through Umbral and brings you back to `/private`. When you are signed in, it shows the claims below. |

`/private` shows `sub`, `name` and `idp`, which are always there, and `email`
and `email_verified` only when they are there. They come together or not at
all: Umbral sends neither when it has no address for the person, a passkey or
fediverse user for example (see
[Integrate an app](../README.md#integrate-an-app) for what each claim means).
A sample shows the claims as they come, without interpreting them.

### Signing in and out

- **Sign in** starts the authorization code flow at Umbral and comes back to `/`.
- **Sign out** ends the app's own session, then sends the person to Umbral's
  end-session endpoint (`/connect/logout`, read from discovery) with the app's
  post-logout redirect URI, so that Umbral's session ends too and the person
  lands back on `/`. The post-logout redirect URI is registered on the app in
  Umbral, which redirects only to it (see
  [Signing out](../docs/integrate.md#signing-out)).

### Settings

Three environment variables, and the port:

| Variable | What it is |
| --- | --- |
| `UMBRAL_ISSUER` | The issuer, the **Issuer URL** on the app's page: your `UMBRAL__PUBLIC_ORIGIN`, for example `https://auth.example.com`. |
| `UMBRAL_CLIENT_ID` | The app's **Client ID**. |
| `UMBRAL_CLIENT_SECRET` | The app's **Client secret**. A sample that is a **Single-page or native app** has none and does not read it. |
| `PORT` | The port the sample listens on. Each sample has its own default, named in its README. |

A sample may read optional variables of its own stack (a session key, say), as
long as it runs without them and its README names each one.

### The protocol

- **Authorization code with PKCE (S256)**, the only flow Umbral offers. The
  library does PKCE; the sample never turns it off.
- **`state` and `nonce` are checked** by the library on the way back. The
  sample never turns that off either.
- **Scopes:** `openid profile email`.
- **Endpoints come from discovery** (`UMBRAL_ISSUER` + `/.well-known/openid-configuration`),
  never hard-coded: not the authorization endpoint, not the token endpoint,
  not the end-session endpoint.
- The **redirect URI** and the **post-logout redirect URI** are whatever the
  library uses by default, and the sample's README names both, since they are
  what you register in Umbral. Umbral matches them exactly.

### Cookies

The app's session cookie is `HttpOnly` and `SameSite=Lax`, and `Secure` on
every https address. Only plain http on a loopback address, such as
`http://localhost`, goes without `Secure`, and that is the only plain http
Umbral itself accepts.

### Markup

The harness walks every sample with the same script, so the three pages share
this much markup, and no more:

- `/`: an `<h1>`, a link whose text is `Public`, a link whose text is
  `Private`, and either a link whose text is `Sign in` or a form with a button
  whose text is `Sign out` (a POST, so a stray `<img src>` cannot sign anyone
  out; a real app also protects it with an anti-forgery token, which the
  samples leave out to stay short), or, for an app with no server session, a
  plain button whose text is `Sign out`.
- `/public`: an `<h1>` whose text is `Public`.
- `/private`: an `<h1>` whose text is `Private` and a `<table>` with one row
  per claim, the claim's name in a `<th>` and its value in a `<td>`.

### The README of a sample

Each sample's README has, in this order:

1. **What to create in Umbral**: **Create app** on the **Apps** screen, with
   the **App type** to pick, the **Redirect URI** to paste and the
   **Post-logout redirect URI (optional)** to paste.
2. **How to run it**, with `docker compose up` and without Docker.
3. **What you should see**, page by page.

## A phone app

The [Android](android/README.md), [iOS](ios/README.md) and
[.NET MAUI](maui/README.md) samples follow the same contract, adapted to an
app, and
[Native and mobile apps](../docs/native-apps.md) explains the pattern they show:

- **Screens instead of pages.** A home screen with **Public**, **Private** and
  **Sign in** or **Sign out**, a public screen, and a private screen that starts
  the sign-in when you are signed out and shows the same claims as `/private`.
- **The system's sign-in browser,** a Custom Tab on Android and
  `ASWebAuthenticationSession` on iOS (the .NET MAUI sample's `WebAuthenticator`
  uses the same two), never a WebView.
- **A configuration file instead of environment variables.** `UMBRAL_ISSUER`
  and `UMBRAL_CLIENT_ID` are set in one file the sample's README names, and
  built into the app. There is no secret and no port.
- **Redirect URIs with a custom scheme or a claimed `https://` link,** which
  the README names, since they are what you register in Umbral.
- **Tokens in the platform's secure store** (the Android Keystore, the iOS
  Keychain) instead of a cookie, so the
  [Cookies](#cookies) rules do not apply.
- **The same protocol:** PKCE, `state` and `nonce` checked by the library,
  `openid profile email`, endpoints from discovery, and sign-out through
  Umbral's end-session endpoint.
- **How to run it** covers a device and an emulator instead of `docker compose
  up`, and says how the app reaches a local Umbral (`adb reverse` on Android;
  the iOS Simulator shares the computer's `localhost`).

A phone app has no line in `test/samples.conf`: the harness walks web pages in
a headless browser and cannot drive an app. Its own workflow builds it and runs
the platform's lint on every change to its folder, and its sign-in is walked by
hand, on a device or an emulator, before it is merged; the walk is recorded in
the task's review folder, in `notes.md`.

## The harness

`test/` runs a sample against a real Umbral. `test/run.sh <sample>` builds
Umbral from this repository and the sample from its own Dockerfile, starts
both, claims the instance, adds the fake connector and registers the app
through the admin screens, and then walks the sample in a headless browser:

`/` → `/public` → `/private` (sent to Umbral) → sign in with the fake
connector → `/private` shows `sub`, `name` and `idp`, and no `email` row for
an identity without an email → **Sign out** → `/private` sends you to Umbral
again.

The fake connector signs in anyone as whoever they claim to be, so it exists
only on an instance started with both `ASPNETCORE_ENVIRONMENT=Development` and
`UMBRAL__ALLOW_FAKE_CONNECTOR=true`. In this repository that pair is set in
`test/docker-compose.yml` and in no other compose file, Dockerfile or run
instruction (Umbral's own tests set it in memory, for the Umbral they start in
the test process). The harness's Umbral listens on `localhost` of a Docker
network of its own.

`.github/workflows/samples.yml` runs the harness for every sample on a change
under `samples/`, on request, and before every release: a failing sample
blocks the release.

### Adding a sample

1. A folder under `samples/` with the app, a `Dockerfile`, a
   `docker-compose.yml` and a README in the shape above.
2. One line in `test/samples.conf`: the folder, the port, the app type and
   the two redirect paths. The workflow's matrix reads the same file.
3. A row in the table at the top of this page, and a name in the list under
   [Integrate an app](../README.md#integrate-an-app).

A phone app replaces step 2 with a workflow of its own that builds it (see
[A phone app](#a-phone-app)), and adds a line to the samples list of
[Native and mobile apps](../docs/native-apps.md#samples).
