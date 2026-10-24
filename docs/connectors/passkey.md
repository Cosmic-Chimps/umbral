# Passkey connector

Lets people sign in to your apps with a passkey, and no account at any provider.
A passkey is a key that a person's phone, laptop or security key holds, unlocked
by their fingerprint, face or PIN. Nothing is typed, there is nothing to phish,
and no third party learns who signed in. Umbral is the provider: it keeps only
the public half of the key, which is not a secret.

Phones, recent laptops and tablets hold passkeys (the platform's own keychain or
password manager syncs them), and so do hardware security keys such as a YubiKey.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) · [Google](google.md) ·
[OpenID Connect](oidc.md) · [Fediverse](fediverse.md).

## Before you start

You need Umbral running with `UMBRAL__PUBLIC_ORIGIN` set to the address people
really use, and that address must be HTTPS (or `http://localhost` for a laptop
trial). A LAN address such as `http://192.168.1.50:8080` gets no passkeys.

A passkey is bound to that address. If you change `UMBRAL__PUBLIC_ORIGIN` later,
every passkey stops working and Umbral cannot move them. See
[Gotchas](#gotchas).

## 1. Enable the connector

Open **Connectors** in the admin area and choose **Passkey** in the list. It
needs no client ID, secret or callback URL. Choose who can create an account:

- **Invitations only**: *Only people you send an invitation link to can create
  an account. The safe default.*
- **Anyone can sign up**: *Anyone who reaches a sign-in page that offers this
  connector can create an account.*

Leave **Shared with every app** ticked to offer it everywhere, or untick it and
choose it per app on the **Apps** screen, as with any connector. Click **Enable
passkeys**: *Saved. People can sign in with a passkey from the next page load.*
No restart is needed. There is one passkey connector per instance, and you can
change the mode later; it takes effect at once and nobody's passkeys change.

## 2. Invite someone

On its page, type a **Note (optional)**: *For you: who the link is for. People
never see it.* (up to 64 characters), and click **Invite someone**. Umbral shows
the link once:

> Send this link to the person over a channel you trust. It works once and for 7
> days; it is not shown again.

Umbral sends no email. The link is a bearer link: whoever holds it can use it, so
treat it like a password. The page lists **Open invitations** (the note, who it is
for, when it was created and when it expires). To withdraw one, click **Revoke**
and confirm.

## 3. What the person sees

1. They open the link. The page says **You're invited**: *Choose the name apps
   will see, then create your passkey.*
2. They type their name (1 to 64 characters, and optionally a nickname for the
   passkey, such as "Phone"), and click **Create my passkey**.
3. Their device asks for a fingerprint, face or PIN.
4. They are signed in to Umbral and see **Your passkeys**, with the new one on it
   (*Your passkey is ready.*).

An invitation is for a new person. If someone who already has an account redeems
one, they become a second user. To give an existing user a passkey, see
[Recovery](#recovery).

## Signing in

On an app's sign-in page the person presses **Sign in with a passkey**, then
**Continue with your passkey**. Their device shows its own list of accounts for
this Umbral and asks them to confirm. No name is typed, and they are sent back to
the app. With **Anyone can sign up**, the page also offers **Create an account
with a passkey**. With invitations only it says *Need an account? Ask whoever runs
this Umbral for an invitation link.*

## Adding a passkey to an account that uses a provider

Someone who signs in with GitHub, Google or Discord can add a passkey to the same
account, and stay one user with the same `sub`:

1. Open `/account/passkeys` (linked from Umbral's home page as **Your
   passkeys**). Without a session you are sent to the sign-in page, and brought
   back after signing in through any connector.
2. Optionally type a nickname, and click **Add a passkey**.
3. Confirm on the device.

An account holds up to 10 passkeys. At 10: *You have the most passkeys an account
can hold. Remove one to add another.* If the connector is not enabled the page
says *Passkeys are not enabled on this Umbral.*

## Recovery

When someone loses a device, open **Users**, find their row and click **Invite to
add a passkey**. Umbral shows a link once. Send it over a channel you trust. It
works once and for 7 days. The page it opens names the account and asks for no
name. The person registers a new passkey on the same account, so their `sub` is
the same in every app, and removes the old one on their passkeys page. The same
button gives a person who signs in only with a provider a passkey of their own.

## Removing a passkey

- **The person** clicks **Remove** on `/account/passkeys` and confirms: *Remove
  this passkey? You will not be able to sign in with it any more.* They cannot
  remove their last passkey while they have no provider to sign in with: *You
  can't remove your only way to sign in. Add another passkey first, or ask whoever
  runs this Umbral to delete your account.*
- **You** click **Remove** under the person's row on **Users**, for example when
  a device is stolen. You may remove their last one; to cut a person off, disable
  or delete the user.

Deleting a user deletes their passkeys and invitations. Deleting the connector
deletes its invitations but not anyone's passkeys: they work again if you enable
the connector again.

## What Umbral stores

Per passkey: the public key, a signature counter, the nickname, when it was added
and last used, and what the passkey itself reports: the authenticator's model id
and transports (usb, nfc, internal and so on). Umbral asks for no attestation and
stores none. No private key ever reaches Umbral, and none of this is a secret.
Invitations are stored as a hash, never as the link.

## What an app receives

An ordinary OpenID Connect sign-in, so an app that already works with Umbral
needs no change:

| Claim | Passkey-only user | User who also signs in with a provider |
| --- | --- | --- |
| `sub` | the user's id | the same id as before |
| `name` | the name typed at enrolment | their name |
| `email`, `email_verified` | absent | as stored |
| `idp` | `passkey` | `passkey` |

An app that must have an email can ask the person for it itself, or not offer the
passkey connector (on the **Apps** screen, choose its sign-in methods).

## Allowed email domains

They do not apply: passkeys carry no email, so there is nothing to judge. Who can
create an account is the enrolment mode, set on its page. The **Apps** screen
warns about an app that offers a passkey connector set to **Anyone can sign up**,
the way it warns about a connector without a domain rule, and does not warn when
it is invitations only. See [Who can sign in](../../README.md#who-can-sign-in).

## Gotchas

- **Changing the domain stops every passkey.** A passkey is bound to the address in
  `UMBRAL__PUBLIC_ORIGIN`. The Passkey page says so: *Passkeys are bound to
  this address. If it changes, every passkey stops working and people need a new
  invitation.* Recovery is a new invitation per person.
- **JavaScript is needed** on the sign-in, invitation and passkeys pages: *Passkeys
  need JavaScript on this page. Turn it on and reload, or go back and choose
  another way to sign in.* Removing a passkey and all operator actions work
  without it.
- **A security key that cannot store a passkey** (an old one) is refused with a
  message to use another device. Umbral needs a passkey the device can present by
  itself, so that no name has to be typed.
- **Synced passkeys and the counter.** Many synced passkeys always report a
  counter of 0, and Umbral accepts that. Only a counter that goes backwards from a
  non-zero value is refused.
- **Rate limit.** Passkey ceremony requests share the sign-in limit: 30 a minute
  per address. Beyond it the person sees the "too many attempts" page.
- **Cancelling the device prompt** changes nothing and records nothing.
- **Testing without hardware.** In Chrome, open DevTools → **More tools** →
  **WebAuthn**, enable the virtual authenticator environment, add an
  authenticator with **resident keys** and **user verification** ticked, and the
  page works as with a real device. (Menu names are as of writing.)
- **Several passkeys per account**, up to 10: a phone and a security key make a
  good pair. A second passkey comes from a second device: Umbral tells the
  browser which passkeys the account already has, so the same device refuses to
  make another one for it (the page says the passkey wasn't saved). That is the
  device's doing, not a fault.
- **The invitation is a bearer link.** Send it over a channel you trust. It works
  once, for 7 days, and a used, expired, revoked or unusable link all show the
  same page: *This invitation link can't be used*. The cause is in
  `security_events` as `passkey_refused;reason=invitation_…`.
- **The security log** records `passkey_registered`, `passkey_removed`,
  `passkey_invitation_created` and `passkey_invitation_revoked`, and refusals as
  `sign_in_failed` with `passkey_refused;reason=…`. See
  [Troubleshooting](../../README.md#troubleshooting).
