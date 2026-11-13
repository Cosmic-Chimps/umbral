# Microsoft connector

Lets people sign in to your apps with a Microsoft account: a personal one
(Outlook.com, Hotmail, Xbox, Skype), a work or school one (Microsoft Entra ID),
or both. You choose who may sign in, and Umbral refuses everyone else.

Back to the [README](../../README.md). Other providers:
[Discord](discord.md) · [GitHub](github.md) · [Google](google.md) · [Apple](apple.md) ·
[OpenID Connect](oidc.md) · [Passkeys](passkey.md) ·
[Fediverse](fediverse.md) · [Bluesky](bluesky.md).

## Before you start

- **A Microsoft account that can register an app** in the Microsoft Entra admin
  center, <https://entra.microsoft.com>: one that is allowed to register
  applications in a directory (a tenant).
- **One Microsoft connector per Umbral instance.** Its identifier is always
  `microsoft`. Microsoft shows the app registration's name to people when they
  first sign in, so one registration named after your Umbral instance is what
  you want. An app that needs a registration of its own needs an Umbral
  instance of its own.
- **Which connector to use.** The [OpenID Connect connector](oidc.md) already
  works for the people of **one** organization's Microsoft Entra tenant, and it
  can stay in use there. Use this connector instead when personal Microsoft
  accounts should sign in, or people from more than one organization. Microsoft
  serves those through shared addresses (`consumers`, `organizations`,
  `common`) whose discovery document names a different issuer than the address
  it is served at, and the OpenID Connect connector refuses such a document on
  purpose. This connector knows exactly which issuer each address must name. It
  also keys people on the tenant and the account together (see
  [Who the person is](#who-the-person-is)) and reads Microsoft's own signal that
  an email address is verified (see [step 2d](#d-add-the-optional-claim-xms_edov)).
  Neither is something the generic connector does.

## 1. Get the redirect URI from Umbral

Open **Connectors** in the admin area and choose **Microsoft** in the list. The
field **Redirect URI to register with the provider** holds the exact URL, with a
Copy button:

```
<UMBRAL__PUBLIC_ORIGIN>/connect/microsoft/callback
```

for example `https://auth.example.com/connect/microsoft/callback`. Do not type
it by hand: Microsoft compares it exactly.

## 2. Register the app in Microsoft

Everything below is in the Microsoft Entra admin center,
<https://entra.microsoft.com>, under **Entra ID**, **App registrations**.

### a. A new registration

1. Click **New registration**.
2. **Name:** a name that names this Umbral instance, for example "Umbral — home".
   Microsoft shows it to people when they first sign in.
3. **Supported account types:** choose the line that matches who should sign in.
   Microsoft's labels, and the card to choose in Umbral in step 3, are:

   | In Microsoft's registration | In Umbral, under **Who may sign in** |
   | --- | --- |
   | Personal accounts only | **Personal Microsoft accounts** |
   | Single tenant only | **Work or school accounts from one organization** |
   | Multiple Entra ID tenants | **Any work or school account** |
   | Any Entra ID Tenant + Personal Microsoft accounts | **Personal and any work or school account** |

   The two choices must match. Microsoft decides by the registration which
   accounts it lets through, and Umbral adds its own check on top: if they
   differ, people are refused (see [Gotchas](#gotchas)).
4. **Redirect URI:** choose the platform **Web** and paste the URL from step 1.
5. Click **Register**.

The registration's **Overview** page now shows the **Application (client) ID**
and the **Directory (tenant) ID**, both GUIDs. Umbral asks for the first one,
and for the second only for "one organization".

### b. The redirect URI, if you did not enter it

If you left it empty, open **Authentication** in the registration, add a
platform **Web**, and paste the URL from step 1. Microsoft compares the address
exactly, so copy it from Umbral.

### c. A client secret

1. Open **Certificates & secrets** and click **New client secret**.
2. Give it a description you will recognise, and choose when it expires.
   Microsoft offers 180 days (recommended), 365 days, 18 months, 2 years, or a
   date of your own up to 2 years.
3. Click **Add**. Copy the secret's **Value** at once: Microsoft shows it only
   now. Do not copy the **Secret ID**, the other column: it is not a secret and
   does not work as one.

**The secret expires.** Write the date down. Once it has passed, nobody can sign
in through Microsoft until you replace the secret (see
[Renewing the secret](#renewing-the-secret)). Umbral cannot read the date from
Microsoft, so it cannot warn you.

### d. Add the optional claim `xms_edov`

1. Open **Token configuration** and click **Add optional claim**.
2. Choose the token type **ID**.
3. Tick **email** and **xms_edov**, and add them.

Do not skip this. `xms_edov` is the signal Microsoft documents for "the email
domain owner has been verified", and it is the only thing that lets Umbral treat
an address as verified. The `email` claim alone is not trusted: for a work
account the tenant's administrator can set it to any address, including one in a
domain the tenant does not own, so Umbral never treats it as proof by itself.
Without `xms_edov` in the token:

- everyone signs in with an **unverified** address, so Umbral never joins a
  Microsoft sign-in to an existing user by email (see
  [How people are identified](../integrate.md#how-people-are-identified));
- a connector or an instance with [allowed email domains](#allowed-email-domains)
  admits nobody through Microsoft.

*The steps follow Microsoft's pages as read on 2026-11-12:
[Register an application](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app),
[Add credentials](https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-credentials)
and the
[optional claims reference](https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference).
The menu path to the optional claim, the place of the redirect URI after
registration and the exact wording of the claim picker were **not checked** on
the live admin center. Microsoft changes its menus from time to time; the values
to enter in Umbral do not change.*

## 3. Enter the values in Umbral

In **Connectors → Microsoft**, fill in the form:

| Field | What to enter |
| --- | --- |
| **Display name** | Shown on the sign-in button as "Sign in with …" and on the admin screens. The default is "Microsoft". |
| **Application (client) ID** | From the registration's **Overview** page, a GUID. |
| **Client secret** | The secret's **Value** from step 2c, not its Secret ID. |
| **Who may sign in** | One of the four cards, matching the account types you chose in step 2a. None is chosen for you: Umbral asks you to choose. |
| **Directory (tenant) ID** | Only for **Work or school accounts from one organization**: the organization's tenant ID from the registration's **Overview** page, a GUID. Not a domain name such as `contoso.onmicrosoft.com`. With another card the field is ignored. A refused form keeps what you typed, but a saved one does not: choosing **one organization** again means entering the tenant ID again. |

Then click **Save connector**. Umbral reads Microsoft's discovery document for
the choice you made. If that works, the connector is saved and enabled, and
people see **Sign in with Microsoft** on Umbral's sign-in page from the next page
load. If Microsoft cannot be reached, or its answer is not usable (a tenant ID
that names no tenant is such an answer: Umbral says "No Microsoft tenant has
this ID"), what happens depends on the connector:

- A **new** connector is saved but **not** enabled, the pane says why, and
  **Enable** tries again. **Enable** makes the same check every time, and stays
  off with a notice when it fails.
- An **existing** connector whose choice of who may sign in **changed** is **not
  saved**. The form comes back with the reason under the choice (under the tenant
  ID with "one organization"), so a mistyped tenant ID never reaches a connector
  people use. Check the tenant ID, or wait if Microsoft did not answer, and try
  again.
- An **existing** connector whose choice did **not** change saves your other
  changes and keeps its state. If it is enabled, it stays enabled and the pane
  says that Umbral could not check it. If it is disabled, it is saved but not
  enabled, as for a new connector.

The client secret is stored encrypted, in the secret store you configured (see
[Secret stores](../run/secret-stores.md)). It is never shown again, only
replaced: once saved, the pane shows it as dots, and the field **Replace the
client secret** takes a new one. Leave it empty to keep the saved secret.
Replacing the secret leaves the connector enabled or disabled as it was.

The pane also has **Allowed email domains**, as the other panes do: see
[Allowed email domains](#allowed-email-domains) below.

### What the form says

A refused form shows each message under the field it is about and keeps what you
typed, except the secret, which is never drawn back.

| What the form says | What to do |
| --- | --- |
| "Enter the Application (client) ID, a GUID like 00000000-0000-0000-0000-000000000000." | Copy it again from the registration's **Overview** page. |
| "Enter the client secret's Value." | The secret is empty on a new connector. Paste the Value from step 2c. |
| "Choose who may sign in. It must match the account types chosen when the app was registered." | None of the four cards is chosen. |
| "Enter the Directory (tenant) ID of the organization." | **Work or school accounts from one organization** is chosen and the field is empty. |
| "That is not a tenant ID. It is a GUID like 00000000-0000-0000-0000-000000000000." | Use the GUID from the **Overview** page, not a domain name. |
| "That is the tenant of personal Microsoft accounts: choose Personal Microsoft accounts instead." | `9188040d-6c67-4c5b-b112-36a304b66dad` is the tenant every personal account belongs to, not an organization. Choose the first card. |
| "Enter the label for the sign-in button, then save again." | The display name is empty. |
| "Saved, but not enabled: Umbral could not reach Microsoft (&lt;reason&gt;). Press Enable to try again." | A new connector, or one that was disabled, is saved but not enabled. Check that the machine can reach `login.microsoftonline.com`, then press **Enable**. |
| "Not enabled: Umbral could not reach Microsoft (&lt;reason&gt;). Try again in a moment." | Same, when you pressed **Enable**. |
| "Saved, but not enabled: Microsoft's answer was not usable (&lt;reason&gt;). Check the tenant ID, then press Enable." | A new connector, or one that was disabled. The usual reason is "No Microsoft tenant has this ID": the tenant ID names no tenant. Correct it and press **Enable**. |
| "Not enabled: Microsoft's answer was not usable (&lt;reason&gt;). Check the tenant ID, then press Enable again." | Same, when you pressed **Enable**. |
| "Saved. Umbral could not check the connector with Microsoft (&lt;reason&gt;), so it stays enabled as it was." | An enabled connector whose choice did not change. Your other changes are saved and sign-in goes on as before. Check that the machine can reach `login.microsoftonline.com`, and save again later if you want the check. |
| "Microsoft did not confirm this choice: &lt;reason&gt;. Nothing was saved: check the tenant ID, then try again." | Under the tenant ID, for an existing connector changed to **one organization**. Nothing was saved, so the connector still works as before. The usual reason is "No Microsoft tenant has this ID". Correct the tenant ID. |
| "Microsoft did not confirm this choice: &lt;reason&gt;. Nothing was saved: check that the choice matches the app's registration, then try again." | Under **Who may sign in**, for an existing connector changed to another card. Nothing was saved. Compare the card with the registration's **Supported account types**. |
| "Microsoft did not answer (&lt;reason&gt;). Nothing was saved: try again in a moment." | An existing connector whose choice changed, while Microsoft could not be reached. Nothing was saved. Try again later. |

The notices about Microsoft appear at the top of the pane, except the last one,
which is under the choice (or under the tenant ID). Several fields can be refused
at once.

### Renewing the secret

Renew before the date you noted. Nobody is signed out while you do it:

1. In Microsoft's admin center, under **Certificates & secrets**, add a **new**
   client secret, and copy its **Value**.
2. In **Connectors → Microsoft**, paste it under **Replace the client secret**
   and save.
3. Sign in once through Microsoft to see that it works.
4. Delete the old secret in Microsoft's admin center.

Do not delete the old secret first: until you have saved the new one in Umbral,
it is the one Umbral is using.

An expired secret looks like this: every sign-in through Microsoft ends on
Umbral's "Sign-in didn't complete", and the security log shows
`rejected: invalid_client`. Add a new secret as above and replace it.

### Changing who may sign in

You can change the card, or the tenant ID, at any time. A changed choice is
checked with Microsoft before it is saved: if the check fails, nothing is saved
(see [What the form says](#what-the-form-says)). It keeps every person: who
someone is does not depend on the choice (see
[Who the person is](#who-the-person-is)). **Narrowing** it reaches people who are
already signed in. Someone whose tenant is no longer admitted is refused the next
time an app authorizes them with their existing session, which ends that session,
or the next time an app refreshes their token. The security log records it as
`microsoft:tenant_not_allowed;tenant=<tid>`. Widening it admits the same people
again. Remember to change the registration's **Supported account types** to
match.

## What people see

1. They click **Sign in with Microsoft** on Umbral's page.
2. Microsoft's page asks them to sign in if they are not signed in. The first
   time, Microsoft shows the app registration's name from step 2a and may ask
   them to agree.
3. They are sent back to Umbral, which sends them on to the app they came from.

A person whose account is not admitted by your choice, or by an allowed email
domain rule, lands on Umbral's sign-in page with "This account can't be used to
sign in here. Try a different account or another option below, or contact
whoever runs this Umbral." The page does not say why.

The app gets an ordinary OpenID Connect user: a stable subject, a name (Microsoft's
`name`, else `preferred_username`), and an email if Microsoft sent one. There is
no avatar: Umbral does not call Microsoft Graph, where the photo lives. The
sign-in button carries no Microsoft logo (see [Gotchas](#gotchas)).

## Who the person is

Umbral identifies a person by two things Microsoft puts in the identity token:
the tenant they signed in to (`tid`) and their account in it (`oid`). The
identifier is `<tid>#<oid>`. It is not Microsoft's `sub`, which is different for
every app registration. Two things follow.

- **Replacing the app registration keeps everyone.** Lose the secret with nobody
  able to renew it, move the registration to another tenant, or delete the
  connector and set it up again: the same people come back as the same Umbral
  users, because `tid` and `oid` do not depend on the registration.
- **A guest is another person.** A guest of an organization's tenant (a person
  invited from another organization) has the tenant's `tid` and an `oid` of the
  guest object there. That is not the `tid` and `oid` of the same person's home
  account. Under **one organization**, someone who signs in as a guest and later
  as themselves at home is two Umbral users. Microsoft says to treat guests that
  way.

A person with one account at two organizations is two users, as well: the `oid`
differs per tenant. A token without an `oid` that is a GUID is refused.

## Allowed email domains

On the Connectors screen you can limit who can sign in by email domain, for the
whole instance or for one connector (see
[Who can sign in](../who-can-sign-in.md)). While a rule applies, only an email
that Microsoft reports as verified counts, and the domain must match exactly.

- Umbral counts an address as verified only when the token has an `email` and
  Microsoft's `xms_edov` claim is `true`. That claim is there only if you added
  it in [step 2d](#d-add-the-optional-claim-xms_edov). Without it, **no one** is
  admitted while a rule applies.
- Microsoft sets `xms_edov` when the address belongs to the person's own tenant
  and the tenant's administrator has verified that domain. For a personal
  Microsoft account, a Google account or a one-time passcode sign-in, it is set
  by Microsoft too, according to its own documentation. Facebook and SAML
  accounts never have a verified domain.
- Umbral does not ask Microsoft for the address a second way. The userinfo
  endpoint carries no `xms_edov`, so an address from there could only be
  unverified.

*Whether Microsoft sends `xms_edov` as a JSON boolean, as its documentation
says, has not been seen on a real sign-in yet. Umbral accepts only the boolean
`true` and treats anything else as not verified.*

The rule can be set for the whole instance or on this connector. There is only
one Microsoft connector, so it cannot give one app a rule of its own the way a
second Google connector can. To keep one organization out of an app, narrow
**Who may sign in**, or offer the app only the connectors you choose (see
[Who can sign in](../who-can-sign-in.md)).

## Gotchas

- **The choice must match the registration.** If **Supported account types** in
  Microsoft says one thing and the card in Umbral another, Microsoft or Umbral
  refuses people. When Microsoft refuses first, it shows its own error page,
  with an `AADSTS` code, before Umbral sees anything. When Umbral refuses, the person sees "This
  account can't be used to sign in here" and the security log shows
  `refused: tenant_not_allowed;tenant=<tid>`.
- **The tenant ID is a GUID, not a domain.** The tokens carry GUIDs, and Umbral
  compares them. `contoso.onmicrosoft.com` is refused.
- **The personal tenant is not an organization.** The tenant
  `9188040d-6c67-4c5b-b112-36a304b66dad` is the one every personal account
  belongs to. It cannot be chosen as "one organization": choose **Personal
  Microsoft accounts**.
- **The secret expires.** 180 days is Microsoft's recommended lifetime, 2 years
  the longest. See [Renewing the secret](#renewing-the-secret). Umbral cannot
  see the date.
- **Without `xms_edov` no address is verified.** See
  [step 2d](#d-add-the-optional-claim-xms_edov).
- **No avatar.** Microsoft's `picture` is not in the identity token; the photo
  needs Microsoft Graph and a second call, which Umbral does not make.
- **The button carries no Microsoft logo.** Microsoft's branding guidelines for
  the sign-in button, at
  <https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-branding-in-apps>,
  ask for the logo, unaltered, and do not say that software may redistribute
  it. The button shows a link icon and its title, "Sign in with" and the display
  name you chose, in Umbral's colours. If your app must show Microsoft's logo,
  that is between you and Microsoft's guidelines.
- **Only Microsoft's public cloud.** Umbral talks to `login.microsoftonline.com`.
  The sovereign clouds (`login.microsoftonline.us`, `login.chinacloudapi.cn`) are
  not supported, and neither are Azure AD B2C or Microsoft Entra External ID
  tenants. An organization's own workforce tenant is the case this connector is
  for.

## Troubleshooting

For a failed sign-in, the cause is in the security log.
[Troubleshooting](../run/troubleshooting.md) says how to read it, in its entry
about a person who clicks a provider and sees *Sign-in didn't complete*.

| What you see | Cause and fix |
| --- | --- |
| Microsoft's page shows an error with `AADSTS` and a code, before the person signs in. | The redirect URI or the client ID is wrong, or the account is not of a type the registration's **Supported account types** accepts. Check step 2a and step 1. Microsoft's page names the cause. |
| Back in Umbral, "Sign-in didn't complete", and the log shows `rejected: invalid_client`. | The client secret is wrong, was deleted, or has **expired**. Add a new secret and replace it (see [Renewing the secret](#renewing-the-secret)). |
| Back in Umbral, "This account can't be used to sign in here", and the log shows `refused: tenant_not_allowed;tenant=…`. | The person's tenant is not admitted by **Who may sign in**. Check the card and the registration's account types. If you narrowed the choice, the same line `microsoft:tenant_not_allowed` appears when a signed-in person is turned away. |
| The log shows `no_correlation_cookie`. | Umbral was opened at an address that is not the public origin, so the browser did not keep or send the sign-in cookie. Open Umbral at `UMBRAL__PUBLIC_ORIGIN`. |
| The log shows `id_token_invalid: tenant`, `issuer`, `key_issuer`, `subject` or `malformed`, or `id_token_missing`. | Umbral refused the token's tenant, issuer, signing-key issuer or account identifier, or the token does not parse, or Microsoft's answer carried no identity token. Something between Umbral and Microsoft changes the answer, or the token is not from Microsoft's public cloud. |
| The log shows `id_token_invalid: signature`, `audience`, `expired` or `nonce`. | The same as for the [OpenID Connect connector](oidc.md#troubleshooting): the clock of the machine is wrong, the client ID is not the registration's, or something sits between Umbral and Microsoft. |
| People can sign in, but Umbral never joins them to an existing user by email, and an allowed email domain rule admits nobody. | The optional claim `xms_edov` is missing from the registration. See [step 2d](#d-add-the-optional-claim-xms_edov). |
