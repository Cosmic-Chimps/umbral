# Contract: routes, command, script hooks

Every route is under `/admin`, so the admin cookie (path `/admin`, SameSite
Strict) reaches it. Every POST checks the antiforgery token first: a form field
for form posts, the request header for JSON (as `/passkey/*` does). JSON bodies
are capped like the connector's (64 KiB). A JSON refusal is
`400 { "error": "<notice code>" }`; the page then shows `?msg=<code>`.

## Sign-in

| Route | Body | Answer | Limit |
| --- | --- | --- | --- |
| `GET /admin/signin` | | password form while on; "Sign in with a passkey" (hidden until the script runs); while off, a `<noscript>` line | |
| `POST /admin/signin` | form `credential` | unchanged; while off, `signin-incorrect` like a wrong password | `RateLimits.AdminAuth` |
| `POST /admin/signin/passkey/options` | `{}` | `{ ceremony, options }` (`operator-signin`, discoverable, UV required) | `RateLimits.SignIn` |
| `POST /admin/signin/passkey` | `{ ceremony, credential }` | `{ redirect: "/admin/connectors" }` and the admin cookie; refusal `operator-passkey-signin-failed` | `RateLimits.AdminAuth` |

Before first-run setup, all four send the browser to `/admin/setup` (GET) or
refuse (POST), as today.

## Credential screen (all behind `AdminAuth.requireOperator`)

The re-check fields, read by every action below: `current` (the password; a
failure while password sign-in is off) or `ceremony` + `credential` (the JSON
answer of an `operator-confirm` ceremony, as a string in a form post, as an
object in a JSON body).

| Route | Body | Effect | Limit |
| --- | --- | --- | --- |
| `GET /admin/credential` | | the screen | |
| `POST /admin/credential` | form `current`, `credential`, `confirm` | replace the password; refused while off | `RateLimits.AdminAuth` |
| `POST /admin/credential/confirm/options` | `{}` | `{ ceremony, options }` (`operator-confirm`) | `RateLimits.SignIn` |
| `POST /admin/credential/passkeys/options` | `{ name, current }` or `{ name, ceremony, credential }` | re-check, then `{ ceremony, options }` (`operator-add`, existing passkeys excluded) | `RateLimits.AdminAuth` |
| `POST /admin/credential/passkeys` | `{ ceremony, credential }` | store; `{ redirect: "/admin/credential?msg=operator-passkey-added" }` | `RateLimits.AdminAuth` |
| `POST /admin/credential/passkeys/{id}/remove` | form, re-check optional | no re-check: 422, the screen with that row's confirm block; with one: remove, end other sessions; refusal `operator-passkey-last` while off | `RateLimits.AdminAuth` |
| `POST /admin/credential/password-signin` | form `state=on\|off`, ceremony optional | no ceremony: 422, the card's confirm block; with one: set; off ends other sessions; refusal `operator-password-off-needs-passkey` | `RateLimits.AdminAuth` |

A 422 confirm redraw is not a failure: no event is written. A re-check that is
present and wrong is a failure (event, Warning notice). The switch takes only a
passkey ceremony in both directions: turning it off requires one (FR-007), and
while it is off no password is accepted. Words and layout:
`design/contracts/operator-passkeys-ui.md`.

Notice codes are registered in `Texts.knownNotice`; their words are the
designer's.

## Script hooks (`Assets/passkeys.js`)

| Attribute | On | Does |
| --- | --- | --- |
| `data-operator-passkey-signin` | button, rendered `hidden` | revealed on load; a `get` ceremony against the two sign-in routes |
| `data-operator-passkey-add` | form with `name` and maybe `current` | with a password typed, `{ name, current }`; otherwise a confirm ceremony first; then the `create` ceremony |
| `data-operator-confirm` | form (remove, switch) | with a password typed, a normal submit; otherwise a confirm ceremony fills hidden `ceremony` and `credential`, then submits |
| `data-operator-confirm-passkey` | a submit button inside such a form | forces the passkey path even when a password field exists |

## Command

```
/app/Umbral reset-credential [--forget-passkeys]
```

Prints the new password once, switches password sign-in on, ends every admin
session, and keeps every operator passkey; with `--forget-passkeys` removes them
all. The output says how many passkeys were kept or removed.
