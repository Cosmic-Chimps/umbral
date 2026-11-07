# FastAPI sample

A small FastAPI app (Python 3.13) that signs in through Umbral with Authlib's
Starlette client. In Umbral it is a **Web application**: a confidential client,
because the server keeps the secret. It follows the [shared contract](../README.md):
the same three pages, settings and protocol as every other sample.

## What to create in Umbral

Open **Apps** and click **Create app** with:

- **Name**: anything.
- **Redirect URI**: `http://localhost:8000/signin-callback`
- **Post-logout redirect URI (optional)**: `http://localhost:8000/signout-callback`
- **App type**: **Web application**

Then copy the **Client ID** and the **Client secret**. The secret is shown once.

Both URIs are the sample's own paths (Authlib has no default ones: they are
the two routes in [`main.py`](main.py)), and Umbral matches them exactly. If you
run the sample on another port or host, register those URIs instead.

## How to run it

The sample reads three variables, the port, and one optional variable of its own:

| Variable | What it is |
| --- | --- |
| `UMBRAL_ISSUER` | The **Issuer URL** on the app's page, for example `https://auth.example.com`. |
| `UMBRAL_CLIENT_ID` | The app's **Client ID**. |
| `UMBRAL_CLIENT_SECRET` | The app's **Client secret**. |
| `PORT` | The port to listen on. Default `8000`. |
| `SESSION_SECRET` | Optional. The key that signs the session cookie (below). |

`SESSION_SECRET` is optional: the key that signs the session cookie. Without
it a random one is made at every start, so a restart ends the sessions.

**Without Docker**, from this folder. This is the way to try it against the
Umbral of the [quick start](../../README.md#quick-start-a-first-login-on-your-own-machine)
on `http://localhost:8080`:

```sh
python3.13 -m venv .venv && . .venv/bin/activate && pip install -r requirements.txt
UMBRAL_ISSUER=http://localhost:8080 UMBRAL_CLIENT_ID=... UMBRAL_CLIENT_SECRET=... python main.py
```

**With Docker**, put the variables in a `.env` file next to `docker-compose.yml`
and run:

```sh
docker compose up
```

A container cannot reach an Umbral on your laptop's `localhost`, so use Docker
with an Umbral at a real address (its `UMBRAL__PUBLIC_ORIGIN`), and `python main.py`
for the trial on your laptop.

Behind a reverse proxy that terminates TLS, set `FORWARDED_ALLOW_IPS` to the
proxy's address (uvicorn trusts `X-Forwarded-*` from loopback only by default)
so the app knows it is on https. Without it the app builds an `http://`
redirect URI, and Umbral refuses the sign-in because that URI is not the one
registered.

## What you should see

- `/` shows a heading, the links **Public** and **Private**, and **Sign in**.
- `/public` opens for anyone.
- `/private` sends you to Umbral's sign-in page, titled **Sign in to** and the
  app's name. After you sign in, it shows a table with `sub`, `name` and `idp`,
  and `email` and `email_verified` when Umbral has an address for you.
- **Sign out** goes through Umbral and comes back to `/`, which shows **Sign in**
  again. `/private` then sends you to Umbral again.

## How it works

All of it is in [`main.py`](main.py).

- **Discovery.** Only `server_metadata_url` is set, to the issuer's
  `/.well-known/openid-configuration`. The endpoints, the end-session one
  included, are read from it, never written by hand.
- **PKCE.** `code_challenge_method` is `S256`: Umbral offers only the
  authorization code flow with PKCE, and Authlib sends PKCE only when it is
  named. Authlib checks `state` and the id_token's `nonce` on the way back.
- **Scopes.** `openid`, `profile` and `email`.
- **The id_token's claims.** `authorize_access_token` validates the `id_token`
  and returns its claims, and Umbral's `id_token` already carries the same ones
  as userinfo, so there is no second call.
- **The access log.** uvicorn's log shows the sign-in callback's address, with
  its `code` and `state`. That is left in on purpose: `state` is not a secret,
  and the code is single-use, already redeemed in that same request, and useless
  without both the PKCE verifier and the client secret.
- **Session cookie.** Starlette's `SessionMiddleware`: signed, not encrypted,
  so it holds only the person's own claims, the `id_token` and, during a
  sign-in, Authlib's `state`, `nonce` and PKCE verifier; nothing of the app's.
  It is `HttpOnly`, `SameSite=Lax`, and `Secure` always, so a proxy that
  terminates TLS and speaks http to the app cannot leave it without. Only the
  trial with an issuer on plain http at a loopback address, such as
  `http://localhost`, goes without, and that is the only plain http Umbral
  accepts. The signing key is random unless `SESSION_SECRET` is set, so a
  restart ends the sessions.
- **Sign-out.** A POST, so a stray `<img src>` cannot sign anyone out. It ends
  the app's session, then goes to Umbral's end-session endpoint with the saved
  `id_token` as `id_token_hint`, which names the app, so the post-logout
  redirect URI registered for it is honoured. Umbral sends the person back to
  `/signout-callback`, which redirects to `/`.

For what each claim means, see [Integrate an app](../../README.md#integrate-an-app).
