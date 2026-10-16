# Discord connector

Lets people sign in to your apps with their Discord account. A good fit for a
community that already lives on Discord.

Back to the [README](../../README.md). Other providers:
[GitHub](github.md) · [Google](google.md) ·
[OpenID Connect](oidc.md).

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
`/callback` is the connector's **slug**, its identifier. The first Discord
connector always has the provider's name (`discord`) as its slug. A second one
has the slug you choose (see [Giving one app its own name](#giving-one-app-its-own-name)),
and its card shows its own callback URL. Do not type it by hand: Discord
compares it exactly, down to a trailing slash and upper/lower case.

## 2. Create the application in Discord

1. Go to the Developer Portal, `https://discord.com/developers/applications`,
   sign in, and click **New Application**.
2. Name it after your Umbral instance, for example `Cosmic Chimps Login`, accept
   the terms, and click **Create**. People see this name on Discord's
   authorization page for **every** app that signs in through this connector, so
   do not name it after one of your apps. To give one app its own name, see
   [Giving one app its own name](#giving-one-app-its-own-name).
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

## Giving one app its own name

The Discord application you created above stands for your Umbral instance, so
people see its name for **every** app that uses this connector. That is the
right default. When one app needs its own name on Discord's consent screen, give
it a connector of its own:

1. Create another application in the Discord Developer Portal, the same way as above. Name it as you want people to see it, for example
   `Frunchy`.
2. In Umbral, open **Connectors**. Under **Add another connector**, pick
   **Provider** Discord, type an **Identifier** such as `discord-frunchy`
   (lower-case letters, digits and hyphens; it cannot be changed later), a
   **Button label**, and the new application's **Client ID** and **Client
   secret**. Untick **Shared with every app**, then click **Add connector**.
3. The new card shows its own **Redirect URI to register with the provider**,
   `<UMBRAL__PUBLIC_ORIGIN>/connect/discord-frunchy/callback`. Register
   that URL in the new Discord application, not the first one.
4. Open **Apps**, set that app's **Sign-in methods** to **Only the connectors I
   choose**, tick the new connector, and click **Save sign-in methods**.

That app's sign-in page now offers only the new connector. Every other app keeps
the shared one. A person who signs in to both apps with the same Discord account
is still **one** Umbral user. Disabling or deleting a connector, or changing an
app's choice, warns you first if an app would be left with no way to sign in. A
connector that is not shared and that no app has chosen is offered nowhere. See
[Troubleshooting](../../README.md#troubleshooting).

## Scopes Umbral requests

`identify email`. Umbral requests them itself; you do not select scopes in the
portal.

| Scope | Why |
| --- | --- |
| `identify` | The account's ID, username, display name and avatar |
| `email` | The email address and whether Discord has verified it |

Umbral sends PKCE (`S256`) with the request.

## What people see

1. They click **Sign in with Discord** on Umbral's page.
2. Discord asks them to sign in if they are not, then shows an authorization page
   for your application's name (the one you chose above), saying it will know who they are and see their
   email. They click **Authorize**.
3. They are sent back to Umbral, which sends them on to the app they came from.

## What Umbral takes from Discord

- **Identity** is Discord's user ID, never the username (usernames change).
- **Name** is the global display name, or the username if there is none.
- **Avatar** is built from Discord's avatar hash. Accounts with no avatar get
  one of Discord's default avatars.
- **Email** only when Discord provides one, and it counts as **verified** only
  when Discord says it is.

## Allowed email domains

On the Connectors screen you can limit who can sign in by email domain, for the
whole instance or for one connector (see
[Who can sign in](../../README.md#who-can-sign-in)). While a rule applies, only
an email Discord reports as verified counts, and the domain must match exactly.

- Discord lets an account exist with an unverified email, or with none. While a
  rule applies, that person is refused. They can verify the address in Discord
  and try again.

To limit one app, create a connector for it with the domain rule and offer only
that connector to the app. For example, add a second Discord connector, "Discord (Acme)",
with your domain in **Allowed email domains**, then on **Apps** set the app's
**Sign-in methods** to **Only the connectors I choose** and tick only that
connector. See [Who can sign in](../../README.md#who-can-sign-in).

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
