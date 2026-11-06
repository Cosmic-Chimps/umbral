# Browser-only app sample

A small single-page app (Vite, plain TypeScript, no framework) that signs in
through Umbral with `oidc-client-ts`, the ordinary OpenID Connect library for
browsers. In Umbral it is a **Single-page or native app**: a public client,
because a browser app cannot keep a secret. It follows the
[shared contract](../README.md): the same three pages, settings and protocol as
every other sample.

## What to create in Umbral

Open **Apps** and click **Create app** with:

- **Name**: anything.
- **Redirect URI**: `http://localhost:5173/signin-callback`
- **Post-logout redirect URI (optional)**: `http://localhost:5173/signout-callback`
- **App type**: **Single-page or native app**

Then copy the **Client ID**. There is no secret.

Both URIs are the sample's own paths (`oidc-client-ts` has no default ones),
and Umbral matches them exactly. If you run the sample on another port or host,
register those URIs instead.

## Why a browser app has no secret

Anything in a bundle, in `sessionStorage` or in a response can be read by whoever
opens the developer tools. A secret in a browser app protects nothing and only
pretends to. What binds a sign-in to this app is PKCE (the verifier is created
for each sign-in and stays in the browser's `sessionStorage` until the token
request) and the exact redirect URI registered in Umbral. So Umbral gives this
app type no secret, and the sample reads no `UMBRAL_CLIENT_SECRET`.

## How to run it

The sample reads two variables, and the port:

| Variable | What it is |
| --- | --- |
| `UMBRAL_ISSUER` | The **Issuer URL** on the app's page, for example `https://auth.example.com`. |
| `UMBRAL_CLIENT_ID` | The app's **Client ID**. |
| `PORT` | The port to listen on. Default `5173`. |

There is no `UMBRAL_CLIENT_SECRET`.

**Without Docker**, from this folder. This is the way to try it against the
Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`:

```sh
npm ci
UMBRAL_ISSUER=http://localhost:8080 UMBRAL_CLIENT_ID=... npm run dev
```

**With Docker**, put the variables in a `.env` file next to `docker-compose.yml`
and run:

```sh
docker compose up
```

The container only serves files: it is your browser, not the container, that
talks to Umbral. So the issuer must be an address your browser can reach, and
the quick start's `http://localhost:8080` works with Docker too.

## What you should see

- `/` shows a heading, the links **Public** and **Private**, and **Sign in**.
- `/public` opens for anyone.
- `/private` sends you to Umbral's sign-in page, titled **Sign in to** and the
  app's name. After you sign in, it shows a table with `sub`, `name` and `idp`,
  and `email` and `email_verified` when Umbral has an address for you.
- **Sign out** goes through Umbral and comes back to `/`, which shows **Sign in**
  again. `/private` then sends you to Umbral again.

## How it works

All of it is in [`src/main.ts`](src/main.ts).

- **Discovery.** Only `authority` is set, to the issuer. The endpoints are read
  from `/.well-known/openid-configuration`, never written by hand.
- **PKCE.** `response_type: 'code'`: Umbral offers only the authorization code
  flow with PKCE. The library creates the verifier and checks `state` and
  `nonce` on the way back.
- **Scopes.** `openid`, `profile` and `email`.
- **Tokens in `sessionStorage`.** Not `localStorage`: they are gone when the tab
  closes, not shared with other tabs, and not kept for a later visit. The
  sign-in in flight (`state`, `nonce` and the PKCE verifier) is kept there too,
  since the library would otherwise put it in `localStorage`.
- **No silent renew.** Umbral issues no refresh token to this app, and
  `automaticSilentRenew` is off. A signed-in page simply asks again when the
  token expires.
- **Configuration through `/config.json`.** The bundle holds no configuration
  and no secret. The dev server and the nginx container both serve that file
  from the environment, so one build runs against any Umbral.
- **Sign-out.** It goes to Umbral's end-session endpoint with the `id_token` as
  `id_token_hint`, which names the app, and the post-logout redirect URI from
  configuration. Then `/signout-callback` cleans up and lands on `/`.
- **No cookie.** This app has no session cookie at all. The cookie rules of the
  contract concern a server-side session, and this app has none.

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).
