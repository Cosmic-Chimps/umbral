# Apple connector

Lets people sign in to your apps with their Apple Account. Apple is the provider
with the most steps to set up, and Umbral takes away the part that other guides
make you repeat every six months: you give Umbral the key Apple lets you
download, and Umbral signs what Apple asks for on its own, so there is nothing
to renew.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) · [Google](google.md) ·
[OpenID Connect](oidc.md) · [Passkeys](passkey.md) · [Fediverse](fediverse.md).

## Before you start

- **A membership of the Apple Developer Program.** It is paid, and yearly.
  Creating the key in step 2 needs the **Account Holder** or **Admin** role in
  your team.
- **Umbral at an https address on a real domain name.** Apple accepts no
  `localhost`, no IP address and no plain `http` as a return URL. See
  [Run it for real](../run/https.md). This is also why the laptop trial of the
  README's quick start cannot be used with Apple; for trying it on one machine,
  see [Trying it from a laptop](#trying-it-from-a-laptop).
- **One Apple connector per Umbral instance.** Its identifier is always `apple`.
  The other providers let you add a second connector to give one app its own
  name on the button. Apple's rules fix this button's title, so two Apple
  connectors would be two identical buttons. An app that must have a Services
  ID of its own needs an Umbral instance of its own.
- **Why you might need it.** App Store Review Guideline 4.8 (Login Services,
  read on 2026-11-11) says that an app that uses a third-party or social login
  service for the person's primary account "must also offer as an equivalent
  option another login service" that limits data collection to name and email,
  lets people keep their email address private, and does not collect
  interactions for advertising without consent. Sign in with Apple is the usual
  answer. The guideline is at
  <https://developer.apple.com/app-store/review/guidelines/#login-services>.
  One thing to know first: the Apple logo is not in Umbral's button yet (see
  [The button](#gotchas) under Gotchas), so check that before you submit an app
  to the App Store.

## 1. Get the return URL from Umbral

Open **Connectors** in the admin area and choose **Apple** in the list. The
field **Redirect URI to register with the provider** holds the exact URL, with a
Copy button:

```
<UMBRAL__PUBLIC_ORIGIN>/connect/apple/callback
```

for example `https://auth.example.com/connect/apple/callback`. Apple calls it a
**Return URL**. Do not type it by hand: Apple compares it exactly.

## 2. On Apple's developer site

Everything below is under **Certificates, Identifiers & Profiles** in your
account: <https://developer.apple.com/account/resources>. You make four things:
an App ID, a Services ID, a key, and you read one number, your Team ID.

### a. An App ID with Sign in with Apple

A Services ID cannot exist without an App ID that has Sign in with Apple turned
on, called its **primary App ID**. If you already have the App ID of an iOS app
with Sign in with Apple, use it and go to b.

1. Open **Identifiers** and click the add button (**+**).
2. Choose **App IDs** and continue.
3. Fill in the description and the identifier Apple asks for.
4. In the list of capabilities, tick **Sign in with Apple**.
5. Continue, then register it.

### b. A Services ID

1. Open **Identifiers**, click **+**, and choose **Services IDs**.
2. Enter a **description** you will recognise, such as the name of your Umbral
   instance, and an **identifier** such as `com.example.signin`. Then register
   it.
3. Open the Services ID you just made, tick **Sign in with Apple**, and click
   **Configure**.
4. Choose your primary App ID from step a.
5. Under **Website URLs**, enter the **domain** of your Umbral address, for
   example `auth.example.com` (no `https://`), and the **Return URL** you copied
   in step 1.
6. Click **Done**, **Continue** and **Save**.

The identifier is the **Services ID** that Umbral asks for in step 3.

### c. A key

1. Open **Keys** and click **+**.
2. Enter a **key name**, and tick **Sign in with Apple**.
3. Click **Configure** and choose your primary App ID.
4. Continue, review the key and confirm.
5. Click **Download**. The file is named `AuthKey_<Key ID>.p8`.

Apple lets you download the key **once**: keep the file somewhere safe. Note the
**Key ID**, ten characters, shown on the key's page and in the file name.

### d. The Team ID

The **Team ID** is ten characters. It is on the **Membership details** page of
your account.

*The steps for the Services ID and the key follow Apple's own help pages as read
on 2026-11-11:
[Configure Sign in with Apple for the web](https://developer.apple.com/help/account/configure-app-capabilities/configure-sign-in-with-apple-for-the-web/)
and
[Create a private key to access a service](https://developer.apple.com/help/account/keys/create-a-private-key).
The App ID steps and the place of the Team ID were **not checked** on the live
site. Apple changes its menus from time to time; the four values to enter in
Umbral do not change.*

## 3. Enter the values in Umbral

In **Connectors → Apple**, fill in the form:

| Field | What to enter |
| --- | --- |
| **Display name** | A name for the admin screens. The sign-in button always says "Sign in with Apple", whatever you type here. |
| **Services ID** | The identifier from step 2b, like `com.example.signin`. Other providers call it the client ID. |
| **Team ID** | The ten characters from step 2d. |
| **Key ID** | The ten characters from step 2c. |
| **Private key (.p8)** | Open the `.p8` file in a text editor and paste all of it, including the `BEGIN` and `END` lines. |

Then click **Save connector**. Umbral checks that the key can sign and that it
can reach Apple. If both work, the connector is enabled and no restart is needed:
people see **Sign in with Apple** on Umbral's sign-in page from the next page
load. If Apple cannot be reached, the connector is saved but **not** enabled,
the pane says why, and **Enable** tries again. A key that cannot sign is not
saved at all. **Enable** makes the same two checks every time, with the key it
reads from the secret store.

The key is stored encrypted, in the secret store you configured (see
[Secret stores](../run/secret-stores.md)). It is never shown again, only
replaced: once saved, the pane shows it as dots, and the field **Replace the
private key** takes a new one. Leave that field empty to keep the saved key.
Replacing the key leaves the connector enabled or disabled as it was.

The pane also has **Allowed email domains**, as the other panes do: see
[Allowed email domains](#allowed-email-domains) below.

### What the form says

| What the form says | What to do |
| --- | --- |
| "Enter a name for this connector, then try again." | The display name is empty. |
| "Enter the Services ID, like com.example.signin, with no spaces." | The Services ID is empty or has a space in it. |
| "A Team ID is ten letters and digits, like DEF123GHIJ." | Copy it again from **Membership details**. |
| "A Key ID is ten letters and digits, like ABC123DEFG." | Copy it again from the key's page, or from the file name. |
| "Paste the .p8 key Apple let you download." | The key field is empty on a new connector. |
| "That is not a usable .p8 key. Paste the whole file again, including the BEGIN and END lines." | The pasted text is not a P-256 private key in PEM form. Paste the whole file. |
| "Saved, but not enabled: Umbral could not reach Apple (&lt;reason&gt;). Press Enable to try again." | The connector is saved. Check that the machine can reach `appleid.apple.com`, then press **Enable**. |
| "Not enabled: Umbral could not reach Apple (&lt;reason&gt;). Try again in a moment." | Same, when you pressed **Enable**. |
| "Not enabled: the stored key cannot sign. Paste the .p8 key again under Replace the private key." | What the secret store holds is not a usable key. Paste the `.p8` key again. |
| "Not enabled: Umbral could not read the key from the secret store (&lt;reason&gt;). Check the store, then press Enable again." | The secret store did not answer or refused. The reason names the store and the cause; see [Secret stores](../run/secret-stores.md). |

A refused form shows each message under the field it is about and keeps what
you typed, except the key, which is never drawn back. The last four messages
appear at the top of the pane.

## 4. What people see

1. They click **Sign in with Apple** on Umbral's page.
2. Apple's page asks them to sign in if they are not signed in. The first time,
   it asks whether to share their email or hide it, and lets them edit their
   name.
3. They are sent back to Umbral, which sends them on to the app they came from.

The app gets an ordinary OpenID Connect user: a stable subject, the email (their
own, or a relay address Apple made for them), and the name from the first
sign-in on. A person whose Apple Account sends no email (Apple says this can
happen with Sign in with Apple at Work & School) signs in without one, so your
app must cope with a missing `email`.

## Trying it from a laptop

Apple needs a public https address, so a laptop has to be given one. A tunnel
service such as Cloudflare Tunnel or ngrok gives you an https address on a real
domain name that forwards to your machine. Then:

1. Start the tunnel and note its https address.
2. Set `UMBRAL__PUBLIC_ORIGIN` to that address and restart Umbral.
3. In the Services ID of step 2b, register that domain and the return URL that
   Umbral now shows in step 1.
4. Open Umbral through the tunnel's address, **not** through `localhost`.

A tunnel is a proxy in front of Umbral. Which settings it needs (the forwarded
headers, and who may send them) is in [Run it for real](../run/https.md#using-a-different-reverse-proxy).
Use the tunnel's own documentation for starting it. Every time the tunnel gets a
new address, change the origin and the Services ID again.

## Allowed email domains

On the Connectors screen you can limit who can sign in by email domain, for the
whole instance or for one connector (see
[Who can sign in](../who-can-sign-in.md)). While a rule applies, only
an email Apple reports as verified counts, and the domain must match exactly.

- Apple reports an address as verified for the person's own address and for a
  relay address. Umbral accepts Apple's `email_verified` as the boolean `true`
  or the text `"true"`, the two forms Apple documents, and nothing else.
- A person who chose to hide their email arrives with an address at
  `privaterelay.appleid.com`, whatever their real domain is. A rule that lists
  only your company's domain therefore refuses them. Add
  `privaterelay.appleid.com` to the list only if anyone with an Apple Account
  may sign in: that domain says nothing about who the person is.
- A person with no email, or with one Apple does not report as verified (Apple
  says this can happen with Sign in with Apple at Work & School), is refused
  while a rule applies.

## Gotchas

- **The name comes once.** Apple sends the name only the first time a person
  authorizes your Services ID. Umbral stores it then and keeps it: later
  sign-ins bring no name and do not clear it. If that first sign-in failed after
  Apple's page, the user is created as "Apple user". The person can make Apple
  ask again by stopping the use of Sign in with Apple with your app (on an
  iPhone: **Settings**, their name, **Sign in with Apple**, the app, **Delete**)
  and signing in again: Apple then has them share their name and email as on a
  first sign-in.
- **The email may be a relay address.** A person who chooses to hide their email
  is given an address ending in `@privaterelay.appleid.com`, which forwards to
  them. Umbral treats it as their verified email. Three things follow. It is not
  the address of their Google or GitHub account, so Umbral does not join the two
  users (see
  [How people are identified](../integrate.md#how-people-are-identified)). A
  person who shares their real address is joined as with any provider: when
  Apple reports it as verified and one existing user has that verified address,
  they are the same user. If you
  limit sign-in to [allowed email domains](#allowed-email-domains), a relay
  address passes only if `privaterelay.appleid.com` is in the list. And if your
  app sends mail to its users, Apple delivers mail to relay addresses only from
  email sources you registered for its private email relay service. That is
  between your app and Apple: Umbral sends no mail.
- **No client secret to renew.** Other guides have you generate a "client
  secret" token from the key and replace it at least every six months, because
  Apple accepts none that lives longer. Umbral keeps the key and signs a new
  token, valid for five minutes, for each sign-in, so there is nothing to renew.
  The key itself does not expire. Replace it when it may have leaked: create a
  second key at Apple (a primary App ID can have two), paste it under **Replace
  the private key** with its new Key ID, save, then revoke the old key at Apple.
- **No localhost, no IP address, https only.** Apple refuses any other return
  URL, and the browser would not keep Umbral's sign-in cookie on plain http
  anyway. See [Before you start](#before-you-start) and
  [Trying it from a laptop](#trying-it-from-a-laptop).
- **The button.** Its title is always "Sign in with Apple", black in the light
  theme and white in the dark theme, and it does not take an app's accent colour:
  these are Apple's rules for the button. Apple's logo is not in the button yet:
  the artwork is Apple's own and the Umbral project has to add it. Until a
  release notes that it is there, do not rely on this button for an App Store
  review.
- **The subject belongs to your team.** Apple's identifier for a person is the
  same for every app of one developer team, and different for another team.
  Moving the Services ID to another team makes everyone a new user.

## Troubleshooting

For a failed sign-in, the cause is in the security log.
[Troubleshooting](../run/troubleshooting.md) says how to read it, in its entry
about a person who clicks a provider and sees *Sign-in didn't complete*.

| What you see | Cause and fix |
| --- | --- |
| Apple's page says the request is invalid, before the person signs in. | The return URL or the domain is not registered in the Services ID exactly as in step 1, or the Services ID in Umbral is not the one you configured. |
| Back in Umbral, "Sign-in didn't complete", and the log shows `rejected: invalid_client`. | The Team ID, Key ID or Services ID does not match the key, the key was revoked, or the key belongs to another primary App ID than the Services ID. |
| The log shows `no_correlation_cookie`. | Umbral was opened at an address that is not https, or not the public origin, so the browser did not keep or send the sign-in cookie. Open Umbral at `UMBRAL__PUBLIC_ORIGIN`. |
| The log shows `id_token_invalid: …`. | The clock of the machine is wrong, or something sits between Umbral and Apple. |
