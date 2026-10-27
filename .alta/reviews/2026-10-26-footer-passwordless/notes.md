# Footer line and home lead say "passwordless sign-in": notes for review

Task `2026-10-26-the-footer-line-and-the-home-lead-say-passwordless-sign-in-n-kk8w`, a follow-up
of rp0l raised by `umbral-designer`. Branch `alta/earnest-puffin-5z88`, on top of `main` at
`063b163`. Wording from `umbral-designer` (2026-10-26).

## Change

`src/Umbral/Views/Texts.fs`, two strings. No markup and no CSS change, so `umbral.css` is not
rebuilt.

| Key | Old | New |
| --- | --- | --- |
| `Footer` | Umbral — self-hosted social login | Umbral — self-hosted passwordless sign-in |
| `HomeLead` | A self-hosted sign-in server that lets your apps offer social login (Discord, GitHub, Google, any OpenID Connect provider) through one OpenID Connect issuer. | A self-hosted sign-in server. Your apps offer passwordless sign-in through one OpenID Connect issuer: a passkey, or an account people already have at Discord, GitHub, Google, a Mastodon-compatible server or any OpenID Connect provider. |

## Tests

No test asserted the old literals: the layout tests compare with `Texts.en.Footer`, so they follow
the new value. `HomeTests`, `SignInBrandingTests`, `ThemeTests`: 37 passed.

## CHANGELOG, under `## Unreleased`, `### Changed`

> The footer line reads **Umbral — self-hosted passwordless sign-in** instead of "self-hosted
> social login", and the home page says that apps can offer a passkey as well as an account at
> Discord, GitHub, Google, a Mastodon-compatible server or any OpenID Connect provider.

## Captures (`<state>-<width>-<theme>.png`, 1280 and 360, dark and light)

| State | What |
| --- | --- |
| `home` | `/`: the lead and the footer line |
| `signin` | `/signin` for an unbranded app: a plain page's footer |
| `admin` | `/admin/apps`: the operator's footer, beside the rail |

Fresh Release instance on :18080 with `UMBRAL__PUBLIC_ORIGIN=https://auth.lacalabaza.com`,
`/tmp/umcap/capture-kk8w.mjs`. Measured in each capture: no horizontal overflow, and the footer
line holds on one line at 360 px. The branded sign-in page is not captured: it shows "Secured by
Umbral" instead of `Footer`, and that is unchanged.

The footer links of rp0l (`alta/chipper-waltz-z7fp`) are not on `main` yet, so they are not in
these captures.
