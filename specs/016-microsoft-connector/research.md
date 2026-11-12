# Research: The Microsoft accounts connector

Every Microsoft page cited here was read on 2026-11-12. The live discovery
documents and key sets were fetched the same day; see
`.alta/reviews/2026-11-11-microsoft/notes.md`.

## R1. Why the generic connector cannot do it (the premise)

Microsoft's shared endpoints publish a discovery document whose `issuer` is not
their own address:

| Endpoint | Who signs in | `issuer` in the document |
| --- | --- | --- |
| `consumers` | personal Microsoft accounts | `https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0` |
| `<tenant GUID>` | one organization | `https://login.microsoftonline.com/<that GUID>/v2.0` |
| `organizations` | any work or school account | `https://login.microsoftonline.com/{tenantid}/v2.0` (a template) |
| `common` | both | the same template |

Umbral's discovery check refuses a document whose issuer differs from the address
typed (OIDC Discovery §4.3, the mix-up defence), so the generic connector refuses
`consumers`, `organizations` and `common` at Save. Step 1 ran it and recorded the
messages. The defence stays: this connector does not loosen it, it states
exactly which issuer each endpoint must name.

## R2. Issuer validation

Microsoft, "Access tokens in the Microsoft identity platform", sections *Validate
the issuer* and *Validate the signing key issuer*,
<https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens>
(applies to ID tokens too: "Web apps validating ID tokens ... need to validate the
issuer"):

- "the application should replace the `{tenantid}` value in the issuer metadata
  with the tenant ID that is the target of the current request, and then check the
  exact match (`tid` claim of the token)."
- "Validate that the `tid` claim is a GUID and the `iss` claim is of the form
  `https://login.microsoftonline.com/{tid}/v2.0` where `{tid}` is the exact `tid`
  claim."
- "Keys that have an issuer value with a GUID like `…/9188040d-…/v2.0` should only
  be used when the `iss` claim in the token matches the value exactly. Keys that
  have a templated issuer value like `…/{tenantid}/v2.0` should only be used when
  the `iss` claim in the token matches this value after substituting the `tid`
  claim in the token for the `{tenantid}` placeholder."
- "the `tid` claim must be part of the key used to access the user's data."

"Convert single-tenant app to multitenant",
<https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant>,
says the same for `common` and `organizations`.

**Decision.** For every audience, not only the templated ones:

1. Discovery is read from `https://login.microsoftonline.com/<segment>/v2.0` and
   its `issuer` must equal, exactly, the issuer R1's table gives for that
   segment. Anything else is refused as `Wrong`, as for any provider.
2. The `id_token`'s `tid` is read from its payload before validation, only to
   build one string. It must be a GUID in its canonical lower-case form (36
   characters, `Guid.ToString("D")` of itself). Otherwise: refused, reason
   `tenant`.
3. The expected issuer is `https://login.microsoftonline.com/<tid>/v2.0`, built
   from a constant template, never from the token's `iss`. It is handed to
   Microsoft.IdentityModel as the one valid issuer, so the library compares it
   with `iss` exactly, alongside the signature, the audience (the client ID), the
   algorithm (asymmetric only) and the lifetime, as for the OIDC connector. A
   token whose `iss` names another tenant than its own `tid` fails here: reason
   `issuer`.
4. The signing key's issuer: the key in the discovery document's key set whose
   `kid` is the token's `kid` must carry an `issuer` property; with `{tenantid}`
   replaced by `tid` it must equal `iss` exactly. A key with no `issuer`, or a
   token with no `kid`, is refused: reason `key_issuer`. (Microsoft's key sets
   carry it on every key: 6 templated keys and 2 personal-account keys at
   `common`, fetched 2026-11-12.)
5. `nonce` and `azp` as for the OIDC connector.
6. The tenant is then judged by the operator's choice (R3). A tenant outside it is
   refused as an account not allowed, not as a malformed answer.

Why our code and not `Microsoft.IdentityModel.Validators` (`AadIssuerValidator`):
that validator fetches Microsoft's v1 and v2 metadata itself and accepts v1
issuers (`https://sts.windows.net/{tid}/`) too. Umbral needs neither. What is left
for Umbral is three string comparisons (2, 4 and the `tid` set of R3); the
cryptography and the claim checks stay the library's, as constitution III asks
and as `Oidc.fs` already does for `nonce` and `azp`.

Sovereign clouds (`login.microsoftonline.us`, `login.chinacloudapi.cn`) are out of
scope: the host is fixed.

## R3. Who may sign in

| Choice | Endpoint segment | Discovery issuer | `tid` admitted |
| --- | --- | --- | --- |
| Personal Microsoft accounts | `consumers` | `…/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0` | exactly `9188040d-6c67-4c5b-b112-36a304b66dad` |
| Work or school accounts from one organization | the tenant GUID | `…/<GUID>/v2.0` | exactly that GUID |
| Any work or school account | `organizations` | template | any GUID except `9188040d-…` |
| Personal and any work or school account | `common` | template | any GUID |

The personal-accounts tenant ID is from Microsoft's "ID token claims reference",
`tid`: "For sign-ins to the personal Microsoft account tenant (services like Xbox,
Teams for Life, or Outlook), the value is `9188040d-6c67-4c5b-b112-36a304b66dad`",
<https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference>,
and the `consumers` document names it as its issuer.

The endpoint already stops most people Umbral would refuse (Microsoft does not let
a work account sign in at `consumers`), so the `tid` check is the rule and the
endpoint is the hint, as Google's `hd` is for allowed domains (specs/006 R9).

The tenant is given as a GUID only, not a domain name (`contoso.onmicrosoft.com`):
tokens carry the GUID, and a domain could be compared with nothing. The
personal-accounts tenant is refused as "one organization" with a message pointing
at the first choice.

This is a rule about who can become a user, like the passkey enrolment and the
Fediverse server list (Principle VII). It says nothing about what anyone may do.
Narrowing it reaches people already signed in, at their next authorization, as
the Fediverse and Bluesky server lists do (FR-011). The re-check is per user, as
theirs is (spec 015's code review, N3): a session does not record which link it
was made with, so a user with two Microsoft links (a personal one and a work one,
joined by a verified email) passes while either is admitted. It fails closed: a
Microsoft connector whose audience cannot be read, or a user with no readable
Microsoft link, is refused.

## R4. The identity: `tid` and `oid`

"ID token claims reference":

- `sub`: "The subject is a pairwise identifier and is unique to an application ID.
  If a single user signs into two different apps using two different client IDs,
  those apps receive two different values for the subject claim."
- `oid`: "The immutable identifier for an object, in this case, a user account.
  This ID uniquely identifies the user across applications ... the `profile` scope
  is required to receive this claim. If a single user exists in multiple tenants,
  the user contains a different object ID in each tenant."
- *Use claims to reliably identify a user*: "If you need to share data across
  services, `oid` and `tid` is best as all apps get the same `oid` and `tid`
  claims for a user acting in a tenant."

**Decision.** The subject is `<tid>#<oid>`, both lower-case GUIDs, under provider
`microsoft`. An app registration is replaceable (a secret lost with nobody able to
renew it, a registration moved to another tenant), and with `sub` every person
would come back as a new user after it. With `tid` and `oid` they do not. `profile`
is already in the scopes. A token without an `oid` that is a GUID is refused,
reason `subject`.

Consequences, written in the walkthrough:
- A guest in a tenant (B2B) signing in under "one organization" has that tenant's
  `tid` and their guest object's `oid`: a different person from their home-tenant
  sign-in. Microsoft says to treat guests that way ("Guest scenarios ... should
  treat the user as if they're a brand new user").
- Switching the choice (personal to both, say) keeps every person: the subject
  does not depend on the endpoint.
- Deleting the connector and setting it up again finds the same people.

## R5. Email: never verified on Microsoft's word alone

"ID token claims reference", `email`: "This value isn't guaranteed to be correct
and is mutable over time. Never use it for authorization or to save data for a
user." The `email` of a work account can be set by the tenant's administrator to
any address, including one in a domain the tenant does not own: the "nOAuth"
account takeover. Microsoft's tokens carry no `email_verified` (it is not in the
discovery documents' `claims_supported`).

"Optional claims reference",
<https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference>,
`xms_edov`: "Boolean value indicating whether the user's email domain owner has
been verified. An email is considered to be domain verified if it belongs to the
tenant where the user account resides and the tenant admin has done verification
of the domain. Also, the email must be from a Microsoft account (MSA), a Google
account, or used for authentication using the one-time passcode (OTP) flow.
Facebook and SAML/WS-Fed accounts do not have verified domains. For this claim to
be returned in the token, the presence of the `email` claim is required."
"Migrate away from using email claims for user identification or authorization",
<https://learn.microsoft.com/en-us/entra/identity-platform/migrate-off-email-claim-authorization>,
uses the same claim for the same purpose.

**Decision.** `email_verified` is true only when the `id_token` has an `email`
and `xms_edov` is the JSON literal `true`. Not the string `"true"`, not `1`: the
documented type is Boolean, and a value of another type fails safe as false.
Personal accounts follow the same rule: Microsoft's sentence above covers MSA
addresses, and no Microsoft page says the `email` of a personal account is
verified without it. The userinfo endpoint is never called: it has no
`xms_edov`, and an address from it could only be unverified.

The operator turns the signal on in the app registration (Token configuration,
Add optional claim, ID, `xms_edov`). Without it, everyone signs in with an
unverified address: no linking to an existing account by email (Linking's rule),
and a connector or instance with allowed email domains admits nobody. The
walkthrough says both.

Not used: `verified_primary_email` and `verified_secondary_email`, which Microsoft
describes only as "Sourced from the user's PrimaryAuthoritativeEmail" with no
statement of who verified it; and the application's `removeUnverifiedEmailClaim`
behaviour, a beta Graph setting that removes an unverified `email` but does not
mark the remaining one verified.

Whether Microsoft sends `xms_edov` as a JSON boolean is confirmed only by a real
sign-in (the maintainer's walk, "Still open").

## R6. The protocol details

- **PKCE**: none of the documents lists `code_challenge_methods_supported`; the
  OIDC connector's rule sends S256 (specs/002 R5). Microsoft supports it.
- **Client authentication**: the documents list `client_secret_basic` first; the
  OIDC connector's `exchangeCode` is reused as it is.
- **Scopes**: `openid email profile`. `profile` is required for `oid`.
- **Name**: `name`, else `preferred_username`. Display only.
- **Avatar**: none (Microsoft Graph's photo needs `User.Read` and a second call).
- **Keys**: the discovery cache's, with the OIDC connector's one throttled refresh
  on an unknown `kid`.

## R7. The client secret's expiry

Microsoft client secrets expire: the Microsoft Entra admin center offers 180 days
(recommended), 365 days, 18 months, 2 years, or a custom date up to 2 years
("Add credentials", <https://learn.microsoft.com/en-us/entra/identity-platform/how-to-add-credentials>).
Once expired, the token endpoint answers `invalid_client` (AADSTS7000222) and every
sign-in fails as "the provider refused the request". Umbral cannot read the expiry
(that needs Microsoft Graph and an application permission). The walkthrough says
how to renew: add a new secret before the old one expires, paste it under Replace
the client secret, then delete the old one in Microsoft's admin center.

## R8. The button and Microsoft's mark

"Sign in with Microsoft branding guidelines",
<https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-branding-in-apps>:
the button pairs the Microsoft logo with "Sign in with Microsoft", in a light or
dark scheme; "DON'T alter the Microsoft logo"; end users should read "work or
school account" and "personal account", never "Azure" or "Active Directory". The
page offers the artwork "for use in your app" and says nothing about
redistributing it in software others deploy. As spec 014 decided for Discord, the
button keeps the link icon in this change; the mark is a follow-up for the
maintainer. The screens' wording follows the account names above.
