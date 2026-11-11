# Next.js sample

A small Next.js app (Next.js 16, App Router) that signs in through Umbral with
Auth.js (`next-auth` v5) and its generic OpenID Connect provider. In Umbral it
is a **Web application**: a confidential client, because the server keeps the
secret. It follows the [shared contract](../README.md): the same three pages,
settings and protocol as every other sample.

`next-auth` v5 is still published under npm's `beta` tag; the sample pins
`5.0.0-beta.32` in `package-lock.json`, as it pins everything else.

## What to create in Umbral

Open **Apps** and click **Create app** with:

- **Name**: anything.
- **Redirect URI**: `http://localhost:3000/api/auth/callback/umbral`
- **Post-logout redirect URI (optional)**: `http://localhost:3000/signout-callback`
- **App type**: **Web application**

Then copy the **Client ID** and the **Client secret**. The secret is shown once.

The redirect URI is Auth.js's default for a provider whose id is `umbral`. The
post-logout redirect URI is the sample's own route (Auth.js has none: see
**Sign-out** under [How it works](#how-it-works)). Umbral matches both exactly. If you run the
sample at another address, set `AUTH_URL` to it and register those URIs
instead.

## How to run it

The sample reads three variables, the port, and two optional variables of
Auth.js:

| Variable | What it is |
| --- | --- |
| `UMBRAL_ISSUER` | The **Issuer URL** on the app's page, for example `https://auth.example.com`. |
| `UMBRAL_CLIENT_ID` | The app's **Client ID**. |
| `UMBRAL_CLIENT_SECRET` | The app's **Client secret**. |
| `PORT` | The port to listen on. Default `3000`. |
| `AUTH_URL` | Optional. The app's public address, for example `https://app.example.com`. Default `http://localhost:` and the port. |
| `AUTH_SECRET` | Optional. The key that encrypts the session cookie (below). |

Without `AUTH_SECRET` a random key is made at every start, so a restart ends
the sessions. To keep them, set one: `openssl rand -base64 32` makes a good
one. Keep it out of the code and out of the repository.

**Without Docker**, from this folder, with Node.js 24. This is the way to try
it against the Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`:

```sh
npm ci
UMBRAL_ISSUER=http://localhost:8080 UMBRAL_CLIENT_ID=... UMBRAL_CLIENT_SECRET=... npm run dev
```

`npm run build && npm start` runs the production build instead. Next.js also
reads the variables from a `.env.local` file in this folder (git-ignored).

**With Docker**, put the variables in a `.env` file next to `docker-compose.yml`
and run:

```sh
docker compose up
```

A container cannot reach an Umbral on your laptop's `localhost`, so use Docker
with an Umbral at a real address (its `UMBRAL__PUBLIC_ORIGIN`), and
`npm run dev` for the trial on your laptop.

Behind a reverse proxy that terminates TLS, set `AUTH_URL` to the app's public
https address. Without it the app builds `http://localhost:3000` URIs, and
Umbral refuses the sign-in because that redirect URI is not one of those registered.
You do not need `AUTH_TRUST_HOST`, which would have Auth.js build its URIs from
the request's `Host` and `X-Forwarded-*` headers: `AUTH_URL` gives the address
from configuration instead. The proxy must still pass the `Host` header (or
`X-Forwarded-Host`) through, since Next.js checks the sign-out form's origin
against it.

## What you should see

- `/` shows a heading, the links **Public** and **Private**, and **Sign in**.
- `/public` opens for anyone.
- `/private` sends you to Umbral's sign-in page, titled **Sign in to** and the
  app's name. After you sign in, it shows a table with `sub`, `name` and `idp`,
  and `email` and `email_verified` when Umbral has an address for you.
- **Sign out** goes through Umbral and comes back to `/`, which shows **Sign in**
  again. `/private` then sends you to Umbral again.

## How it works

The Auth.js setup is in [`auth.ts`](auth.ts), the settings in
[`instrumentation-node.ts`](instrumentation-node.ts), sign-out in
[`app/actions.ts`](app/actions.ts).

- **Discovery.** The provider is `type: "oidc"` with only `issuer` set. Auth.js
  reads the endpoints from the issuer's `/.well-known/openid-configuration`;
  sign-out reads the end-session endpoint from the same document. None is
  written by hand.
- **PKCE, `state` and `nonce`.** `checks: ["pkce", "state", "nonce"]`: Auth.js
  sends a PKCE challenge (S256), and checks `state` and the id_token's `nonce`
  on the way back.
- **Scopes.** `openid`, `profile` and `email`.
- **The claims shown.** `/private` shows the id_token's claims as they come,
  kept at sign-in by the `jwt` callback. It does not use Auth.js's user, since
  `profile()` only maps `sub` and `name` there and invents nothing: no email,
  because Auth.js would lowercase it, and Auth.js replaces the user's id with a
  random one anyway.
- **Starting the sign-in.** A page cannot set the cookies a sign-in needs, so
  `/private` redirects to `/signin`, a route handler that calls Auth.js's
  `signIn` and comes back to `/private`. Where it comes back to is picked from a
  fixed list, never taken from the query.
- **Session cookie.** Auth.js keeps the session in its own cookie, an encrypted
  JWT, with no database, that lasts 14 days, as in the other samples (Auth.js's
  default is 30). It holds the claims above and the id_token, which
  sign-out needs; the id_token never reaches the browser's scripts, since the
  `session` callback leaves it out of what Auth.js serves at `/api/auth/session`.
  The cookie is `HttpOnly`, `SameSite=Lax`, and `Secure` when `AUTH_URL` is
  https. Only plain http on a loopback address, such as `http://localhost`,
  goes without, and that is the only plain http Umbral accepts.
- **Sign-out.** Auth.js's `signOut` only clears its own cookie and leaves the
  Umbral session alive, so the sample does one more step. **Sign out** is a
  form that posts to a server action, which Next.js accepts only from the app's
  own origin, so another site cannot sign anyone out. The action reads the
  id_token from the session cookie, clears the cookie with `signOut`, then sends
  the person to Umbral's end-session endpoint with the id_token as
  `id_token_hint`, which names the app, and the post-logout redirect URI. That
  URI is built from `AUTH_URL`, never from the request, so it cannot be turned
  into a redirect elsewhere. Umbral sends the person back to
  `/signout-callback`, which redirects to `/`.
- **Logs.** The production server logs no request, so no code, token or claim
  reaches its log. `npm run dev` logs each request's address, so the sign-in
  callback's `code` and `state` appear there. That is fine: `state` is not a
  secret, and the code is single-use, already redeemed in that same request,
  and useless without both the PKCE verifier and the client secret.

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).
