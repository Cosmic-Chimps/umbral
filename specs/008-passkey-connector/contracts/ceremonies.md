# Contract: Sign-in, enrolment and the passkeys page

What a browser (and the test suite) sees. Every string shown is in
`Views/Texts.fs`; the wording below is the contract, the code reads it from
there.

## Where the passkey connector appears

- The sign-in page (`GET /signin?returnUrl=…`) lists the passkey connector
  like any other offered connector: `<a href="/connect/passkey/start?returnUrl=…"
  data-connector="passkey">` reading **Sign in with a passkey**, with the
  app's accent when the app has one (spec 006).
- `/connect/passkey/callback` is the generic callback route, which answers
  every slug the same way before looking the connector up: without a
  correlation cookie it is a failed sign-in (302 to `/signin?…msg=sign-in-failed`),
  and with one it finds no adapter for the kind. It can never establish a
  session for a passkey.

## The passkey sign-in page

`GET /connect/passkey/start?returnUrl=<local path>` → 200, a page in the plain
chrome (or the app's branded chrome when `returnUrl` names a branded app, as
the picker does), `data-screen="passkey-signin"`:

- Heading **Sign in with a passkey**; lead: *Your phone, laptop or security key
  will ask you to confirm it is you.*
- A button `data-passkey-signin` **Continue with your passkey**.
- With the connector in **open sign-up**: a form `data-passkey-signup` with a
  field `displayName` (**Your name**, required, 1–64) and an optional
  `nickname` (**Name this passkey (optional)**, up to 64), and a button
  **Create an account with a passkey**.
- With **invitations only**: a line `data-invitations-only`: *Need an account?
  Ask whoever runs this Umbral for an invitation link.*
- `<noscript data-javascript-required>`: *Passkeys need JavaScript on this
  page. Turn it on and reload, or go back and choose another way to sign in.*
- With `?msg=<code>`, a failure alert with the code's text (`knownNotice`).
- A hidden antiforgery input (`__RequestVerificationToken`), which the script
  sends as the `RequestVerificationToken` header.
- 404 when there is no passkey connector or it is disabled.

Rate limit: `sign-in` policy.

## Ceremony endpoints (JSON)

All: `POST`, `Content-Type: application/json`, header
`RequestVerificationToken`, rate limit `sign-in`. A missing or wrong token is
400 (the antiforgery page, as for every form). A body that is not the expected
JSON is 400 `{ "error": "bad-request" }`. Responses are JSON. Every refusal
is 400 `{ "error": "<notice code>" }` with one of the codes below, records a
`sign_in_failed` event with the research R6 reason word, and changes nothing.
The person sees the notice's text; the reason word stays in the log.

### `POST /passkey/assertion/options`

Request: `{ "returnUrl": "<local path>" }` (sanitised like the picker's).

Response 200: `{ "ceremony": "<id>", "options": <AssertionOptions JSON> }`
where `options` is the library's JSON (`challenge` base64url, `rpId`,
`userVerification: "required"`, `timeout`, no `allowCredentials`).

Refusals: `passkey-sign-in-failed` (connector missing or disabled →
`connector_disabled`).

### `POST /passkey/assertion`

Request: `{ "ceremony": "<id>", "credential": <PublicKeyCredential.toJSON()> }`
with `credential.response.userHandle` present.

Response 200: `{ "redirect": "<returnUrl>" }`; the response also sets the
Umbral session cookie (`idp=passkey`, `connector=passkey`). The script then
navigates to `redirect`.

Refusals (all `passkey-sign-in-failed` unless said): ceremony unknown, used
or expired (`ceremony_unknown_or_expired`); `userHandle` missing or not a
user (`user_not_found`); credential id not stored (`unknown_credential`);
credential not the handle's (`user_handle_mismatch`, from the library's
callback); the library's refusals (`origin_mismatch`, `rpid_mismatch`,
`challenge_mismatch`, `user_verification_missing`, `user_presence_missing`,
`invalid_signature`, `sign_count_regressed`, `type_mismatch`,
`malformed_response`, `verification_failed`); user disabled →
`account-disabled` (`user_disabled`), as the provider callback does.

Success also updates `sign_count` and `last_used_utc`, and records
`sign_in_succeeded`.

### `POST /passkey/registration/options`

Request: `{ "mode": "signup" | "invitation" | "add", "displayName"?: string,
"nickname"?: string, "invitation"?: "<secret>", "returnUrl"?: string }`.

| mode | needs | user in options |
| --- | --- | --- |
| `signup` | connector enabled and open (`enrolment_closed` otherwise); `displayName` valid | a fresh `users.id` minted now (not inserted yet), `name` = `displayName` |
| `invitation` | an open invitation (`invitation_*` words otherwise); `displayName` valid when the invitation is for a new person | the invitation's user, or a fresh id |
| `add` | an Umbral session whose user exists and is enabled; connector exists and is enabled; fewer than 10 passkeys (`passkey_limit_reached`) | the session's user |

Response 200: `{ "ceremony": "<id>", "options": <CredentialCreateOptions JSON> }`
with `attestation: "none"`, `authenticatorSelection: { residentKey:
"required", requireResidentKey: true, userVerification: "required" }`,
`extensions: { credProps: true }`, `excludeCredentials` = the user's existing
credentials (ids and transports), `user: { id, name, displayName }` per
research R10, `pubKeyCredParams` the library's default list.

Refusal codes: `passkey-registration-failed` (connector, limit, name),
`passkey-invitation-unusable` (any `invitation_*` word).

### `POST /passkey/registration`

Request: `{ "ceremony": "<id>", "credential": <PublicKeyCredential.toJSON()> }`.

Response 200: `{ "redirect": "<path>" }`: for `signup` the `returnUrl`; for
`invitation` and `add` `/account/passkeys?msg=passkey-added`. For `signup` and
`invitation` the response sets the Umbral session cookie through the passkey
connector.

In one transaction: insert the user when the purpose created one; insert the
passkey (nickname as typed or the default); mark the invitation used
(conditional update; zero rows → rollback and `invitation_used`); record
`user_created` (when created) and `passkey_registered` with `via=<purpose>`.

Refusals: `ceremony_unknown_or_expired`; `credential_not_unique` (the
callback or the `UNIQUE` constraint); `passkey_not_discoverable`
(`credProps.rk === false`); `passkey_limit_reached` (re-checked);
`user_disabled`; `connector_disabled`; the library's words.

## The invitation page

`GET /invite/{secret}` → 200, plain chrome, `data-screen="invitation"`:

- For a new person: heading **You're invited**, lead *Choose the name apps
  will see, then create your passkey.*, form `data-passkey-invitation` with
  `displayName` (required) and `nickname` (optional), button **Create my
  passkey**.
- For an existing user: heading **Add a passkey to your account**, a line
  `data-invitation-account` naming the account's display name, `nickname`
  only, the same button.
- The `<noscript>` note as above.
- Unusable (unknown, used, expired, revoked, user gone or disabled): 200 with
  `data-screen="invitation-unusable"`, heading **This invitation link can't be
  used**, body *It may have been used already, run out, or been withdrawn. Ask
  whoever runs this Umbral for a new one.* Same page for every cause; one
  `sign_in_failed` with `stage=invitation` and the cause word. Not a 404: the
  person did nothing wrong and should read the next step.

Rate limit: `sign-in` policy.

## The passkeys page

`GET /account/passkeys`:

- No Umbral session → 302 `/signin?returnUrl=%2Faccount%2Fpasskeys`.
- Session whose user is gone or disabled → the session is cleared and the
  same redirect.
- Otherwise 200, plain chrome (never an app's), `data-screen="passkeys"`:
  heading **Your passkeys**; lead *A passkey signs you in with your
  fingerprint, face or PIN, on the device that holds it.*; a line
  `data-ways-in` listing the ways this account signs in (each linked provider's
  name and "Passkey" when any); the list, one `<article data-passkey="<id>">`
  per passkey with nickname, **Added** date, **Last used** date or *Never*,
  and a form `POST /account/passkeys/<id>/remove` with `data-confirm` **Remove
  this passkey? You will not be able to sign in with it any more.**; the add
  form `data-passkey-add` with `nickname` (optional) and the button **Add a
  passkey** when a passkey connector exists and is enabled; otherwise a line
  `data-passkeys-not-enabled`: *Passkeys are not enabled on this Umbral.*; a
  line `data-passkeys-limit` when the user holds 10: *You have the most
  passkeys an account can hold. Remove one to add another.*
- `?msg=` notices: `passkey-added` (*Your passkey is ready.*),
  `passkey-removed`, `passkey-last-cannot-remove` (*You can't remove your
  only way to sign in. Add another passkey first, or ask whoever runs this
  Umbral to delete your account.*), `passkey-registration-failed`.

`POST /account/passkeys/{id}/remove` (form, antiforgery): session required;
the passkey must belong to the session's user (else 302 with
`passkey-removed`'s neutral sibling `passkey-not-found`, nothing changes);
last passkey of a user with no linked provider → 302 with
`passkey-last-cannot-remove`, nothing changes; otherwise delete, record
`passkey_removed` (`by=user`), 302 `/account/passkeys?msg=passkey-removed`.

## The home page

`GET /` gains, in the "Signing in" card, a link `data-passkeys-link` **Your
passkeys** to `/account/passkeys` when a passkey connector exists (enabled or
not). Still nothing else about the instance.

## What an app receives

Through the normal OIDC flow, with `idp: "passkey"`:

| Claim | Passkey-only user | User with a linked provider |
| --- | --- | --- |
| `sub` | the user's id | the same id as before |
| `name` | the display name typed at enrolment | the display name |
| `email`, `email_verified` | absent | as stored for the user |
| `picture` | absent | as stored |
| `idp` | `passkey` | `passkey` |

The session counts for an app only if the app offers the passkey connector
(spec 003 FR-008). Allowed-email-domain rules never refuse it (spec FR-010).

## The script (`/assets/passkeys.js`)

- Acts on `[data-passkey-signin]`, `[data-passkey-signup]`,
  `[data-passkey-invitation]`, `[data-passkey-add]`.
- Reads the antiforgery token from the page's hidden input and sends it as
  the `RequestVerificationToken` header on every POST.
- Uses `PublicKeyCredential.parseCreationOptionsFromJSON`,
  `parseRequestOptionsFromJSON` and `credential.toJSON()` when present; else
  converts `challenge`, `user.id`, `excludeCredentials[].id` and
  `allowCredentials[].id` from base64url to `ArrayBuffer` and the response's
  buffers back, always including `clientExtensionResults` (`{}` when the
  browser gives none) and `response.transports` (`[]` when it cannot say).
- On `NotAllowedError` / `AbortError`: does nothing (the person cancelled).
- On a server refusal `{ error }`: navigates to the current page with
  `?msg=<error>` (keeping `returnUrl`), so the notice is rendered by the
  server and no text lives in the script.
- On success: navigates to `redirect`.
- Says nothing of its own: every visible string is already in the page.
