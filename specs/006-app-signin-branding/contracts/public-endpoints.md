# Contract: Public Endpoints and the Sign-In Page

**Feature**: [../spec.md](../spec.md) | **Research**: R4, R5, R7

None of these endpoints needs authentication, antiforgery or a rate limit. They
serve the sign-in page before anyone is signed in, like `Handlers/Assets.fs`.

## `GET /branding/logo/{sha256}.png`

| Case | Response |
| --- | --- |
| `{sha256}` matches `^[0-9a-f]{64}$` and is the `logo_sha256` of an app | `200`, body = `logo_png` |
| Same, with `If-None-Match` naming the ETag (or `*`, or the `W/` form) | `304`, no body |
| Any other value, a replaced logo's old hash, or a deleted app's hash | `404` |

Headers on `200` and `304`:

```text
Content-Type: image/png                                   (200 only)
X-Content-Type-Options: nosniff
Content-Security-Policy: default-src 'none'; sandbox
Cross-Origin-Resource-Policy: same-origin
Cache-Control: public, max-age=31536000, immutable
ETag: "{sha256}"
```

## `GET /branding/accent/{rrggbb}.css`

| Case | Response |
| --- | --- |
| `{rrggbb}` matches `^[0-9a-f]{6}$` | `200`, `text/css; charset=utf-8` |
| Anything else, uppercase included | `404` |

Body, exactly one rule. Channels are decimal and separated by spaces. The
on-accent colour is `0 0 0` or `255 255 255` (R4):

```css
[data-accent="f26b1d"]{--color-primary-rgb:242 107 29;--color-on-primary-rgb:0 0 0}
```

Headers: `X-Content-Type-Options: nosniff`,
`Cache-Control: public, max-age=31536000, immutable`, `ETag: "{rrggbb}-1"`. The
`-1` is the rule's format version: bump it if the body's shape changes.

## `GET /signin` (changed)

How the app is resolved is unchanged (`returnUrl` →
`/connect/authorize?client_id=`). These markers are what tests assert. The
wording is free to change.

### Unbranded

No app, an unknown client, or an app without a branding row. The markup is
byte-for-byte what it was before, apart from the regenerated stylesheet hash
in `?v=`. In particular:

- the `<header>` with the wordmark is present
- the footer text is `Texts.Footer`
- there is no `data-accent`, no `data-signin-logo` and no `data-secured-by`
- the connector buttons have the class `um-btn-secondary`

### Branded

| Element | Present when | Marker |
| --- | --- | --- |
| `<header>` with the wordmark | never | absence of `header nav` |
| `<link rel="stylesheet" href="/branding/accent/{hex}.css">` after `umbral.css` | accent set | |
| `<body data-accent="{hex}">` | accent set | `data-accent` |
| `<img src="/branding/logo/{sha}.png" alt="{app name}" width height>` first in the card | logo set | `data-signin-logo="{clientId}"` |
| The app name in the heading, as today | always | `data-signin-app="{clientId}"` (unchanged) |
| Connector buttons with `um-btn-primary` | accent set | `data-connector` (unchanged) |
| Connector buttons with `um-btn-secondary` | logo only | |
| Footer with the mark and `Texts.SecuredByUmbral` | always | `data-secured-by` |
| Failure alert (`?msg=`) | as today | |

Invariants:

- The `Content-Security-Policy` header equals `Layout.contentSecurityPolicy`.
  It is unchanged.
- Every `src` and `href` in the page is a path on this origin (SC-005).
- The mark inside `data-secured-by` uses fixed colours (`text-on-surface-variant`).
  No rule in the accent stylesheet can reach it, because the accent stylesheet
  only sets the two `--color-*-rgb` variables.

## Pages that are never branded

`/connect/error` (protocol errors Umbral cannot send back to the app), every
`/admin/*` page, message pages (404, 500, rate limit) and end-session pages
render as before, whatever the request names (FR-005).
