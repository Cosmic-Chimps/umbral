# Contract: the `umbral_last_connector` cookie and the picker

## Set-Cookie

Sent on the response that completes a successful end-user sign-in:

- `GET /connect/{slug}/callback` → `302` to the app's authorize request
- `POST /passkey/assertion` → `200` JSON `{ "redirect": … }`
- `POST /passkey/registration` (sign-up or invitation) → `200` JSON `{ "redirect": … }`

```http
Set-Cookie: umbral_last_connector=<slug>; max-age=31536000; path=/; samesite=lax; httponly[; secure]
```

Not sent by: `GET /connect/{slug}/start`, `GET /connect/passkey/start`, a failed
or refused callback, a refused passkey ceremony, `POST /passkey/registration`
for a signed-in person adding a passkey, the admin sign-in, `/connect/logout`.

## GET /signin

Request carries `Cookie: umbral_last_connector=<value>`.

When `<value>` is the slug of one of two or more offered connectors:

```html
<div class="flex flex-col gap-3">
  <a href="/connect/<slug>/start?returnUrl=…" class="um-btn … w-full justify-center"
     data-connector="<slug>" data-last-used="" aria-label="Sign in with <Name>, last used">
    <svg …/> Sign in with <Name> <span class="um-last-used">Last used</span>
  </a>
  <div class="um-or" data-or-divider="">or</div>
  <a … data-connector="<other>">…</a>
  …
</div>
```

Otherwise the response body is the same as with no cookie.
