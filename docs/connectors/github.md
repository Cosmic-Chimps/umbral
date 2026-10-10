# GitHub connector

Lets people sign in to your apps with their GitHub account. GitHub is the
easiest of the three providers to set up: no review, no consent screen to
publish, and `http://localhost` callbacks are accepted for a laptop trial.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [Google](google.md).

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
`/callback` is the connector's **slug**. In the admin UI it is always the
provider's name (`github`). Do not type the URL by hand: GitHub compares it
exactly.

## 2. Create the OAuth app in GitHub

Create an **OAuth App** (not a "GitHub App").

1. Go to `https://github.com/settings/developers` (profile picture →
   **Settings** → **Developer settings** → **OAuth Apps**), then **New OAuth
   App**. To own the app under an organization instead of your account, use that
   organization's **Settings** → **Developer settings** → **OAuth Apps**.
2. Fill in the form:

   | Field | Value |
   | --- | --- |
   | Application name | Anything people will recognize, for example `My Umbral`. GitHub shows it on the authorization page. |
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
```

Set `UMBRAL_GITHUB_SECRET` in the container's environment. See
[Configuration as code](../../README.md#configuration-as-code). Remember that
the file is re-applied on every start and overwrites UI changes to this
connector.

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
   for your application name, listing what it can read. The first time only, they
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
