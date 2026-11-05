# ASP.NET Core sample

A small ASP.NET Core app (.NET 10, minimal APIs) that signs in through Umbral
with the stock `Microsoft.AspNetCore.Authentication.OpenIdConnect` handler. In
Umbral it is a **Web application**: a confidential client, because the server
keeps the secret. It follows the [shared contract](../README.md): the same three
pages, settings and protocol as every other sample.

## What to create in Umbral

Open **Apps** and click **Create app** with:

- **Name**: anything.
- **Redirect URI**: `http://localhost:5000/signin-oidc`
- **Post-logout redirect URI (optional)**: `http://localhost:5000/signout-callback-oidc`
- **App type**: **Web application**

Then copy the **Client ID** and the **Client secret**. The secret is shown once.

Both URIs are the handler's defaults (`CallbackPath` and `SignedOutCallbackPath`),
and Umbral matches them exactly. If you run the sample on another port or host,
register those URIs instead.

## How to run it

The sample reads three variables, and the port:

| Variable | What it is |
| --- | --- |
| `UMBRAL_ISSUER` | The **Issuer URL** on the app's page, for example `https://auth.example.com`. |
| `UMBRAL_CLIENT_ID` | The app's **Client ID**. |
| `UMBRAL_CLIENT_SECRET` | The app's **Client secret**. |
| `PORT` | The port to listen on. Default `5000`. |

**Without Docker**, from this folder. This is the way to try it against the
Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`:

```sh
UMBRAL_ISSUER=http://localhost:8080 UMBRAL_CLIENT_ID=... UMBRAL_CLIENT_SECRET=... dotnet run
```

**With Docker**, put the variables in a `.env` file next to `docker-compose.yml`
and run:

```sh
docker compose up
```

A container cannot reach an Umbral on your laptop's `localhost`, so use Docker
with an Umbral at a real address (its `UMBRAL__PUBLIC_ORIGIN`), and `dotnet run`
for the trial on your laptop.

Behind a reverse proxy that terminates TLS, set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` so the app knows it is on https.

## What you should see

- `/` shows a heading, the links **Public** and **Private**, and **Sign in**.
- `/public` opens for anyone.
- `/private` sends you to Umbral's sign-in page, titled **Sign in to** and the
  app's name. After you sign in, it shows a table with `sub`, `name` and `idp`,
  and `email` and `email_verified` when Umbral has an address for you.
- **Sign out** goes through Umbral and comes back to `/`, which shows **Sign in**
  again. `/private` then sends you to Umbral again.

## How it works

All of it is in [`Program.cs`](Program.cs).

- **Discovery.** Only `Authority` is set, to the issuer. The endpoints are read
  from `/.well-known/openid-configuration`, never written by hand.
- **PKCE.** `UsePkce = true` with `ResponseType = "code"`: Umbral offers only
  the authorization code flow with PKCE. The handler checks `state` and `nonce`.
- **Scopes.** `openid`, `profile` and `email`.
- **`MapInboundClaims = false`.** `sub`, `name` and `idp` keep their names, so
  `/private` can show them as they come.
- **`GetClaimsFromUserInfoEndpoint = false`.** Umbral's `id_token` already
  carries the same claims as userinfo, so there is no second call.
- **`SaveTokens = true`.** On sign-out the handler sends the saved `id_token` as
  `id_token_hint`, which the end-session request uses to name the app, so the
  post-logout redirect URI registered for it is honoured.
- **Sign-out on both schemes.** It ends the app's cookie session, then goes to
  Umbral's end-session endpoint. It is a POST, so a stray `<img src>` cannot
  sign anyone out.
- **Cookie.** `HttpOnly`, `SameSite=Lax`, and `Secure` always, so a proxy that
  terminates TLS and speaks http to the app cannot leave it without. Only the
  trial with an issuer on plain http at a loopback address, such as
  `http://localhost`, goes without, and that is the only plain http Umbral
  accepts.

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).
