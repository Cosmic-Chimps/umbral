# Google connector

Lets people sign in to your apps with their Google account. Google is the most
demanding of the three providers to set up, and it has one limit that decides
whether you can use it at all. Read the next section first.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) ·
[OpenID Connect](oidc.md).

## Read this first: Google will not accept a raw IP address

Google refuses to register a redirect URI whose host is an **IP address**, and
refuses any plain `http://` address other than `localhost`. It also refuses
hosts that are not on a real public domain (names ending in `.local`, `.lan`,
`.internal` and similar). So the most natural homelab address,

```
http://192.168.1.50:8080/connect/google/callback        # cannot be registered
```

**cannot be registered with Google at all.** Umbral shows this warning on the
Google card before you start. It cannot be fixed in Umbral, because the rule is
Google's.

What to do, best first:

1. **Use a real domain name with HTTPS.** This is the way to run Umbral anyway
   (see [Run it for real](../../README.md#run-it-for-real-behind-https)). A
   free dynamic-DNS name from a service such as DuckDNS works, as does a domain
   you buy. Point the name at the machine, put the reverse proxy with a
   certificate in front of Umbral (the repository's `docker-compose.yml` does
   it with Caddy), and set `UMBRAL__PUBLIC_ORIGIN=https://yourname.duckdns.org`.
   Then the redirect URI is `https://yourname.duckdns.org/connect/google/callback`.
2. **For trying it on one machine only**, use `http://localhost`. Google accepts
   `http://localhost` redirects. Set `UMBRAL__PUBLIC_ORIGIN=http://localhost:8080`
   together with the laptop-trial switches in the
   [README quick start](../../README.md#2-run-it) and open Umbral at
   `localhost` in the same machine's browser. Nobody else can sign in this way,
   and it must never be exposed to a network.
3. **Skip Google** and offer [GitHub](github.md) or [Discord](discord.md), which
   have no such rule.

Whatever name you use, it must be the **same address** you set as
`UMBRAL__PUBLIC_ORIGIN`.

## 1. Get the redirect URI from Umbral

Open **Connectors** in the admin area and look at the **Google** card. The field
**Redirect URI to register with the provider** holds the exact URL, with a Copy
button:

```
<UMBRAL__PUBLIC_ORIGIN>/connect/google/callback
```

for example `https://auth.example.com/connect/google/callback`. The part before
`/callback` is the connector's **slug**, its identifier. The first Google
connector always has the provider's name (`google`) as its slug. A second one
has the slug you choose (see [Giving one app its own name](#giving-one-app-its-own-name)),
and its card shows its own callback URL. Do not type it by hand: Google
compares it exactly.

## 2. Create the OAuth client in Google

1. Go to the [Google Cloud console](https://console.cloud.google.com/) and
   select a project, or create one (the project picker at the top → **New
   project**). Billing is not needed.
2. Open the **Google Auth Platform** (`https://console.cloud.google.com/auth`;
   in older consoles this is **APIs & Services → OAuth consent screen**). On
   first use it asks you to **Get started** and then for:
   - **App information**: an app name and a support email. People see the name
     on Google's consent page for **every** app that signs in through this
     connector, so use the name of your Umbral instance, for example
     `Cosmic Chimps Login`, not the name of one of your apps. To give one app
     its own name, see [Giving one app its own name](#giving-one-app-its-own-name);
   - **Audience**: choose **External** (unless every person who will sign in is
     in your own Google Workspace organization, in which case **Internal**);
   - **Contact information**: an email address.

   Agree to the policy and click **Create**.
3. Go to **Clients** and click **Create client**.
   - **Application type**: **Web application**.
   - **Name**: anything.
   - **Authorized JavaScript origins**: leave empty.
   - **Authorized redirect URIs**: **Add URI** and paste the URL from step 1.
   - Click **Create**.
4. Copy the **Client ID** and **Client secret** from the dialog **now**. Google
   shows the full secret only at this point.
5. Decide who may sign in. See
   [Testing vs. In production](#testing-vs-in-production) below. This is the
   step that trips people up.

*Console menus and labels are as of writing, and were **not verified** against
the live console while writing this page. Google renamed and reorganized this
area recently and keeps doing so. The values to enter will not change.*

## 3. Give Umbral the credentials

In **Connectors → Google** paste the **Client ID** and **Client secret**, then
click **Save connector**. It is enabled right away; no restart is needed. People
see **Sign in with Google** on Umbral's sign-in page from the next page load.

The secret is stored encrypted and is shown masked afterwards (its last four
characters). It is never shown again: to change it, paste a new one into
**Replace client secret**.

## Giving one app its own name

The Google application you created above stands for your Umbral instance, so
people see its name for **every** app that uses this connector. That is the
right default. When one app needs its own name on Google's consent screen, give
it a connector of its own:

> **Google needs a separate Cloud project.** The name people see on Google's
> consent screen belongs to the whole Google Cloud project (its **Branding**
> page), not to one OAuth client. A second OAuth client in the same project would
> show the same name. *(Unverified against the live console.)* So for a Google connector with its own name, create a new
> Google Cloud project, set up its consent screen with the name you want, and
> create the OAuth client there (steps 1 to 3 of "Create the OAuth client in
> Google"), all over again.

1. Create a new Google Cloud project (the project picker at the top → **New
   project**). Name it, and later its consent screen (**Branding** → app name),
   as you want people to see it, for example `Frunchy`.
2. In Umbral, open **Connectors**. Under **Add another connector**, pick
   **Provider** Google, type an **Identifier** such as `google-frunchy`
   (lower-case letters, digits and hyphens; it cannot be changed later), a
   **Button label**, and the new application's **Client ID** and **Client
   secret**. Untick **Shared with every app**, then click **Add connector**.
3. The new card shows its own **Redirect URI to register with the provider**,
   `<UMBRAL__PUBLIC_ORIGIN>/connect/google-frunchy/callback`. Register
   that URL in the new project's OAuth client, not the first one.
4. Open **Apps**, set that app's **Sign-in methods** to **Only the connectors I
   choose**, tick the new connector, and click **Save sign-in methods**.

That app's sign-in page now offers only the new connector. Every other app keeps
the shared one. A person who signs in to both apps with the same Google account
is still **one** Umbral user. Disabling or deleting a connector, or changing an
app's choice, warns you first if an app would be left with no way to sign in. A
connector that is not shared and that no app has chosen is offered nowhere. See
[Troubleshooting](../../README.md#troubleshooting).

## Scopes Umbral requests

`openid email profile`. Umbral requests them itself; there is nothing to set in
Google. These are Google's basic, non-sensitive sign-in scopes. You do not need
to add any on the console's **Data access** page.

| Scope | Why |
| --- | --- |
| `openid` | Makes Google return an ID token. Without it, nothing works. |
| `email` | The email address and whether Google has verified it |
| `profile` | Name and picture |

Umbral sends PKCE (`S256`) and a `nonce`, and checks the ID token's issuer,
audience, expiry and nonce, then reads the profile from Google's userinfo
endpoint.

## Testing vs. In production

A new Google OAuth client starts in **Testing** status (shown on the
**Audience** page). While it is in Testing:

- **Only people you list as test users can sign in.** Add their Google addresses
  under **Audience → Test users**. Anyone else gets a Google error page
  ("Access blocked", `access_denied`) before Umbral is involved.
- **At most 100 test users.**

To let anyone with a Google account sign in, go to **Audience** and click
**Publish app** (status becomes **In production**). Umbral asks only for the
basic scopes above, which Google does not treat as sensitive, so this should not
need Google's verification review. *(Not verified against the live console.
Google can still show an "unverified app" notice if you add a logo or other
branding.)*

If your only users are you and a few friends, staying in Testing and listing
them is fine.

## What people see

1. They click **Sign in with Google** on Umbral's page.
2. Google shows its account chooser, then a consent page naming your app. The
   first time only, they approve it.
3. They are sent back to Umbral, which sends them on to the app they came from.

## What Umbral takes from Google

- **Identity** is Google's `sub`, never the email address (people change email
  addresses).
- **Name** and **picture** come from the profile.
- **Email** is used, and counts as **verified**, only when Google reports
  `email_verified: true`. If the flag is false or missing, that person is
  created as a new user and is **not** linked to an existing account with the
  same address (see
  [How people are identified](../../README.md#how-people-are-identified)).

## Allowed email domains

On the Connectors screen you can limit who can sign in by email domain, for the
whole instance or for one connector (see
[Who can sign in](../../README.md#who-can-sign-in)). While a rule applies, only
an email Google reports as verified counts, and the domain must match exactly.

- A Google Workspace domain is owned by the company, so an address there was
  created by the company.
- A consumer Google account can also carry a verified address at that domain
  if the person controls that inbox. Umbral checks the email Google returns,
  not the kind of account.
- With **exactly one** domain in the rule, Umbral sends Google's `hd` hint, so
  the account chooser shows accounts of that domain first. It is only a hint:
  Umbral checks every account Google returns itself. With two or more domains,
  or none, no hint is sent.

To limit one app, create a connector for it with the domain rule and offer only
that connector to the app. For example, add a second Google connector, "Google (Acme)",
with your domain in **Allowed email domains**, then on **Apps** set the app's
**Sign-in methods** to **Only the connectors I choose** and tick only that
connector. See [Who can sign in](../../README.md#who-can-sign-in).

## Gotchas

- **`redirect_uri_mismatch` ("Error 400")** means the URL in the Google console
  is not exactly what Umbral sends. Re-copy it from the Connectors screen, check
  `http` vs `https`, port, trailing slash, and that `UMBRAL__PUBLIC_ORIGIN` is the
  address you actually use. Google can take **a few minutes, occasionally
  longer,** to honour a new or changed redirect URI. *(Per Google's own
  documentation; not measured here.)*
- **"Access blocked" or `access_denied`** for a person who is not you: the client
  is in Testing and they are not a listed test user. See above.
- **The consent page shows the app name** you gave on the **Branding** page, for
  every app that signs in through this connector. Name it after your Umbral
  instance, not after one app. The name belongs to the whole Cloud project, so a
  second OAuth client in the same project shows the same name.
- **Changing your Umbral address** means editing the redirect URI in the Google
  console.
