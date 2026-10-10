# Discord connector

Lets people sign in to your apps with their Discord account. A good fit for a
community that already lives on Discord.

Back to the [README](../../README.md). Other providers:
[GitHub](github.md) · [Google](google.md).

## Before you start

You need Umbral running with `UMBRAL__PUBLIC_ORIGIN` set to the address people
really use. The redirect URI you register below is built from it, so if you
change the origin later, update Discord too.

## 1. Get the redirect URI from Umbral

Open **Connectors** in the admin area and look at the **Discord** card. The
field **Redirect URI to register with the provider** holds the exact URL, with a
Copy button:

```
<UMBRAL__PUBLIC_ORIGIN>/connect/discord/callback
```

for example `https://auth.example.com/connect/discord/callback`. The part before
`/callback` is the connector's **slug**. In the admin UI it is always the
provider's name (`discord`). Do not type it by hand: Discord compares it
exactly, down to a trailing slash and upper/lower case.

## 2. Create the application in Discord

1. Go to the Developer Portal, `https://discord.com/developers/applications`,
   sign in, and click **New Application**.
2. Name it, accept the terms, and click **Create**. The name is what people see
   on Discord's authorization page, so pick one they will recognize.
3. In the left menu open **OAuth2**.
4. Under **Redirects**, click **Add Redirect**, paste the URL from step 1, and
   click **Save Changes** at the bottom of the page. It is easy to miss: the
   redirect is not registered until you save.
5. Copy the **Client ID** (shown on the same page).
6. Click **Reset Secret** to reveal a **Client Secret** and copy it **now**.
   Discord shows it only once, and may ask you to confirm with your Discord
   password or two-factor code. Resetting a secret later invalidates the old one.

Notes:

- You do **not** need a bot, and you do **not** need the **OAuth2 URL
  Generator**. Umbral builds the authorization request itself.
- If the OAuth2 page has a **Public Client** switch, leave it **off**. Umbral
  authenticates with the client secret.

*Portal menus and labels are as of writing and could not be verified against
the live portal while writing this page; Discord changes it from time to time.*

## 3. Give Umbral the credentials

In **Connectors → Discord** paste the **Client ID** and **Client secret**, then
click **Save connector**. It is enabled right away; no restart is needed. People
see **Sign in with Discord** on Umbral's sign-in page from the next page load.

The secret is stored encrypted and is shown masked afterwards (its last four
characters). It is never shown again: to change it, paste a new one into
**Replace client secret**. If you **Reset Secret** in Discord, do this at once,
or logins fail until you do.

### The same thing in the configuration file

```yaml
version: 1
connectors:
  - slug: discord
    kind: discord
    display_name: Discord
    client_id: "123456789012345678"
    client_secret: ${UMBRAL_DISCORD_SECRET}
    enabled: true
```

Set `UMBRAL_DISCORD_SECRET` in the container's environment. See
[Configuration as code](../../README.md#configuration-as-code). Remember that
the file is re-applied on every start and overwrites UI changes to this
connector.

## Scopes Umbral requests

`identify email`. Umbral requests them itself; you do not select scopes in the
portal.

| Scope | Why |
| --- | --- |
| `identify` | The account's ID, username, display name and avatar |
| `email` | The email address and whether Discord has verified it |

You may add more in the configuration file with `extra_scopes`, but identity
does not need them. Umbral sends PKCE (`S256`) with the request.

## What people see

1. They click **Sign in with Discord** on Umbral's page.
2. Discord asks them to sign in if they are not, then shows an authorization page
   for your application name, saying it will know who they are and see their
   email. They click **Authorize**.
3. They are sent back to Umbral, which sends them on to the app they came from.

## What Umbral takes from Discord

- **Identity** is Discord's user ID, never the username (usernames change).
- **Name** is the global display name, or the username if there is none.
- **Avatar** is built from Discord's avatar hash. Accounts with no avatar get
  one of Discord's default avatars.
- **Email** only when Discord provides one, and it counts as **verified** only
  when Discord says it is.

## Gotchas

- **The email may be missing or unverified.** Discord lets an account exist with
  an unverified address, and can return no email at all even with the `email`
  scope. Umbral treats an unverified email as no proof of anything: that person
  is created as a new user and is **not** linked to an existing account that has
  the same address. Your app must cope with a missing `email` and with
  `email_verified: false`.
- **Exact redirect match.** Discord rejects a redirect that differs from the
  registered one by even a trailing slash, with "Invalid OAuth2 redirect_uri".
  Re-copy the URL from the Connectors screen, and check that
  `UMBRAL__PUBLIC_ORIGIN` is the address you actually use.
- **PKCE.** Umbral sends a PKCE `S256` challenge. Discord's documentation does
  not describe PKCE support for the authorization code grant. As far as this
  project knows it is accepted, but that has **not been confirmed against a real
  Discord application yet**. If the login fails at the final step with an error
  about `code_verifier` or `code_challenge`, please report it: it is a one-line
  switch inside Umbral.
- **Plain http on localhost** is, to our knowledge, accepted as a redirect for a
  laptop trial (for example `http://localhost:8080/connect/discord/callback`).
  Anywhere else Umbral requires https. *(Unverified.)*
