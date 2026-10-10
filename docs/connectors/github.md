# GitHub connector

Lets people sign in to your apps with their GitHub account. GitHub is the
easiest of the three providers to set up: no review, no consent screen to
publish, and `http://localhost` callbacks are accepted for a laptop trial.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [Google](google.md) ·
[OpenID Connect](oidc.md).

## Before you start

You need Umbral running with `UMBRAL__PUBLIC_ORIGIN` set to the address people
really use. The callback URL you register below is built from it, so if you
change the origin later, update GitHub too.

## 1. Get the redirect URI from Umbral

Open **Connectors** in the admin area and look at the **GitHub** card. The field
**Redirect URI to register with the provider** holds the exact URL, with a Copy
button:

```
<UMBRAL__PUBLIC_ORIGIN>/connect/github/callback
```

for example `https://auth.example.com/connect/github/callback`. The part before
`/callback` is the connector's **slug**, its identifier. The first GitHub
connector always has the provider's name (`github`) as its slug. A second one
has the slug you choose (see [Giving one app its own name](#giving-one-app-its-own-name)),
and its card shows its own callback URL. Do not type the URL by hand: GitHub
compares it exactly.

## 2. Create the OAuth app in GitHub

Create an **OAuth App** (not a "GitHub App").

1. Go to `https://github.com/settings/developers` (profile picture →
   **Settings** → **Developer settings** → **OAuth Apps**), then **New OAuth
   App**. To own the app under an organization instead of your account, use that
   organization's **Settings** → **Developer settings** → **OAuth Apps**.
2. Fill in the form:

   | Field | Value |
   | --- | --- |
   | Application name | The name of your Umbral instance, for example `Cosmic Chimps Login`. GitHub shows it on the authorization page for **every** app that signs in through this connector, so do not name it after one of your apps. To give one app its own name, see [Giving one app its own name](#giving-one-app-its-own-name). |
   | Homepage URL | Anything. Your Umbral address is fine. |
   | Application description | Optional. |
   | Authorization callback URL | The URL from step 1. |
   | Enable Device Flow | Leave unchecked. |

3. Click **Register application**.
4. Copy the **Client ID** (shown at the top of the app's page).
5. Click **Generate a new client secret** and copy it **now**. GitHub shows the
   full value only once.

*Console menus and labels are as of writing; GitHub reorganizes its settings
now and then. The values to enter will not change.*

## 3. Give Umbral the credentials

In **Connectors → GitHub** paste the **Client ID** and **Client secret**, then
click **Save connector**. It is enabled right away; no restart is needed. People
see **Sign in with GitHub** on Umbral's sign-in page from the next page load.

The secret is stored encrypted and is shown masked afterwards (its last four
characters). It is never shown again: to change it, paste a new one into
**Replace client secret**.

### The same thing in the configuration file

```yaml
version: 1
connectors:
  - slug: github
    kind: github
    display_name: GitHub
    client_id: "Ov23liXXXXXXXXXXXXXX"
    client_secret: ${UMBRAL_GITHUB_SECRET}
    enabled: true
    # shared: true                     # optional, default true; false = offered only to apps that choose it
```

Set `UMBRAL_GITHUB_SECRET` in the container's environment. See
[Configuration as code](../../README.md#configuration-as-code). Remember that
the file is re-applied on every start and overwrites UI changes to this
connector.

## Giving one app its own name

The GitHub application you created above stands for your Umbral instance, so
people see its name for **every** app that uses this connector. That is the
right default. When one app needs its own name on GitHub's consent screen, give
it a connector of its own:

1. Create another **OAuth App** in GitHub, the same way as above. Name it as you want people to see it, for example
   `Frunchy`.
2. In Umbral, open **Connectors**. Under **Add another connector**, pick
   **Provider** GitHub, type an **Identifier** such as `github-frunchy`
   (lower-case letters, digits and hyphens; it cannot be changed later), a
   **Button label**, and the new application's **Client ID** and **Client
   secret**. Untick **Shared with every app**, then click **Add connector**.
3. The new card shows its own **Redirect URI to register with the provider**,
   `<UMBRAL__PUBLIC_ORIGIN>/connect/github-frunchy/callback`. Register
   that URL in the new GitHub application, not the first one.
4. Open **Apps**, set that app's **Sign-in methods** to **Only the connectors I
   choose**, tick the new connector, and click **Save sign-in methods**.

That app's sign-in page now offers only the new connector. Every other app keeps
the shared one. A person who signs in to both apps with the same GitHub account
is still **one** Umbral user. Disabling or deleting a connector, or changing an
app's choice, warns you first if an app would be left with no way to sign in. A
connector that is not shared and that no app has chosen is offered nowhere. See
[Troubleshooting](../../README.md#troubleshooting).

In the configuration file, the second connector gets its own slug and its own
secret variable, `UMBRAL_GITHUB_FRUNCHY_SECRET` (the slug upper-cased, anything
that is not a letter or digit as `_`), and the app lists it:

```yaml
connectors:
  - slug: github-frunchy             # fixed once created: it is in the callback URL
    kind: github
    display_name: GitHub
    client_id: "…"
    client_secret: ${UMBRAL_GITHUB_FRUNCHY_SECRET}
    enabled: true
    shared: false                      # offered only to apps that choose it
apps:
  - client_id: frunchy
    name: Frunchy
    client_secret: ${UMBRAL_FRUNCHY_SECRET}   # web app; leave out for a single-page or native app
    redirect_uris:
      - https://frunchy.example.com/callback
    connectors:                        # a chosen set; absent = all shared connectors
      - github-frunchy
```

An app listing a connector the file does not define refuses the whole file.

## Scopes Umbral requests

`read:user user:email`. Umbral requests them itself; there is nothing to
configure in GitHub (OAuth Apps have no scope setting). You may add more in the
configuration file with `extra_scopes`, but identity does not need them.

| Scope | Why |
| --- | --- |
| `read:user` | The profile: numeric ID, name, login, avatar |
| `user:email` | The list of the person's email addresses, including private ones |

Umbral sends PKCE (`S256`) with the request.

## What people see

1. They click **Sign in with GitHub** on Umbral's page.
2. GitHub asks them to sign in if they are not, then shows an authorization page
   for your application's name (the one you chose above), listing what it can read. The first time only, they
   click **Authorize**.
3. They are sent back to Umbral, which sends them on to the app they came from.

## What Umbral takes from GitHub

- **Identity** is GitHub's numeric user ID. A person who renames their GitHub
  login is still the same Umbral user, and someone who takes over an abandoned
  login name is not.
- **Name** is the profile name, or the login if there is none. **Avatar** is
  GitHub's avatar URL.
- **Email** is the person's **primary, verified** address, read from GitHub's
  email list on every sign-in. It is marked verified, so it can link to the same
  person's account from another provider (see
  [How people are identified](../../README.md#how-people-are-identified)).

## Gotchas

- **One callback URL per OAuth app.** GitHub's OAuth Apps take a single
  authorization callback URL. If you run two Umbral instances (say production
  and a test copy on another address), create one OAuth app for each. Changing
  your Umbral address means editing this one field. *(Unverified for 2026:
  re-check on GitHub if you need more than one URL.)*
- **Private emails are handled.** Many people hide their email. That is why
  Umbral asks for `user:email` and reads the email list instead of the public
  profile. A person with no primary verified address still signs in, but
  without an email, and Umbral will not link them to anyone else. Your app must
  cope with a missing `email`.
- **The secret is shown once.** GitHub lets you have more than one client secret
  at a time, so you can rotate without downtime: generate a new secret, paste it
  into Umbral with **Replace client secret**, then delete the old one in GitHub.
- **Plain http on localhost works** as a callback for a laptop trial (for
  example `http://localhost:8080/connect/github/callback`). Anywhere else Umbral
  requires https. *(Accepted by GitHub as of writing; unverified for 2026.)*
- **`redirect_uri` error from GitHub** ("The redirect_uri is not associated with
  this application") means the callback URL in GitHub does not match what Umbral
  sends. Re-copy it from the Connectors screen and check
  `UMBRAL__PUBLIC_ORIGIN`. See
  [Troubleshooting](../../README.md#troubleshooting).
