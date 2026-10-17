# Contract: Theme choice, Create app page, fonts (spec 007)

## `<html data-theme>`

Every page Umbral renders through `Layout` carries exactly one of:

| Cookie `umbral_theme` | `<html data-theme>` | `<meta name="color-scheme">` | `<meta name="theme-color">` |
| --- | --- | --- | --- |
| absent | `system` | `dark light` | two: `#0b0b14` with `media="(prefers-color-scheme: dark)"`, `#f4f1ea` with `media="(prefers-color-scheme: light)"` |
| `light` | `light` | `light` | `#f4f1ea` |
| `dark` | `dark` | `dark` | `#0b0b14` |
| anything else (`Light`, `blue`, empty, `light;x`) | `system` | as absent | as absent |

Match is exact and case-sensitive. The cookie is never echoed into the page.

## `POST /theme`

No operator session required. Not rate limited.

**Form** (`application/x-www-form-urlencoded`):

| Field | Values |
| --- | --- |
| `__RequestVerificationToken` | antiforgery token (required; `WebForms.readValidated`) |
| `theme` | `system` \| `light` \| `dark` |
| `returnUrl` | where to go back to |

**Effects**:

| `theme` | Cookie |
| --- | --- |
| `light` / `dark` | `Set-Cookie: umbral_theme=<value>; Max-Age=31536000; Path=/; SameSite=Lax; HttpOnly` (+ `Secure` when the request is HTTPS) |
| `system` | `umbral_theme` deleted (`Max-Age=0` / expired, same Path) |
| anything else | no `Set-Cookie` |

**Answer**: `303 See Other`, `Location` = `returnUrl` if it is a local path, else `/`.

Local path: non-empty, starts with `/`, second character (if any) is neither `/`
nor `\`, and no character is a control character or whitespace. Examples:

| `returnUrl` | Location |
| --- | --- |
| `/admin/apps` | `/admin/apps` |
| `/signin?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dx` | same |
| `/` | `/` |
| `//evil.example` | `/` |
| `/\evil.example` | `/` |
| `https://evil.example/` | `/` |
| `` (missing) | `/` |
| `/a\r\nSet-Cookie:x` | `/` |

**Refusals**: missing or bad antiforgery token → the existing "This form
expired" message page (400), cookie untouched.

**Security log**: none (not an authentication, credential or app change).

## The theme control (markup hooks)

Rendered in the operator rail and in the footer of every non-operator page:

```html
<form method="post" action="/theme" data-theme-switch>
  <input type="hidden" name="__RequestVerificationToken" value="…">
  <input type="hidden" name="returnUrl" value="/admin/apps">
  <span id="theme-label">Theme</span>
  <div role="group" aria-labelledby="theme-label">
    <button type="submit" name="theme" value="system" aria-label="Follow system" aria-pressed="true">…</button>
    <button type="submit" name="theme" value="light"  aria-label="Light"         aria-pressed="false">…</button>
    <button type="submit" name="theme" value="dark"   aria-label="Dark"          aria-pressed="false">…</button>
  </div>
  <p data-theme-note>Following your system.</p>
</form>
```

`returnUrl` = the request's path and query on a GET; on any other method,
`/admin` under `/admin`, else `/`. Strings come from `Texts.fs`. The `id` must be
unique per page (only one control per page).

## `GET /admin/apps/new`

Behind `requireOperator`. Renders the Apps screen's create form (same fields,
same `POST /admin/apps`) on an operator page with `data-screen="app-new"`, a
heading, and a Cancel link to `/admin/apps`. When no enabled shared connector
exists it shows the same "needs a connector" empty state the Apps screen shows.

On the Apps screen, "Create app" is
`<a href="/admin/apps/new" data-dialog="create-app">`, and the form is inside
`<dialog id="create-app" aria-labelledby="create-app-title">` with a
`button type="button" data-dialog-close` Cancel. `umbral.js` opens it with
`showModal()` on click (falling back to following the link if `showModal` is
missing) and closes it on `[data-dialog-close]`.

## Fonts

| Path | Content-Type | Cache |
| --- | --- | --- |
| `/assets/fonts/bricolage-grotesque-latin-800-v1001.woff2` | `font/woff2` | `public, max-age=31536000, immutable`, ETag |
| `/assets/fonts/schibsted-grotesk-latin-400-600-v1100.woff2` | `font/woff2` | same |
| `/assets/fonts/spline-sans-mono-latin-400-500-v1004.woff2` | `font/woff2` | same |

Every `url(` in `umbral.css` names one of these paths. No page or stylesheet
names `fonts.googleapis.com`, `fonts.gstatic.com` or any other host.

## Accent sheet (`/branding/accent/{hex}.css`, spec 006, amended)

The one rule now also sets `--color-primary-fill-rgb`, `--color-on-primary-fill-rgb`,
`--color-primary-fill`, `--color-on-primary-fill` to the accent and its text
colour. ETag format version bumped.
