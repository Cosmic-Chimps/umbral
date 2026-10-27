# Code review: the footer and the home lead say passwordless sign-in (task kk8w)

Branch `alta/earnest-puffin-5z88`, `063b163..006cf5f` (the change is `9736001`). Reviewer: the `umbral` session, 2026-10-26.

## Verdict

**Clear to merge.** No blocking items, no nits.

## What was checked

- **`Texts.fs`:** two strings change, `Footer` and `HomeLead`. Both are accurate for what Umbral does now (constitution 3.0.0: passwordless sign-in, through a social provider or a passkey). The lead names a passkey, Discord, GitHub, Google, a Mastodon-compatible server (the Fediverse connector) and any OpenID Connect provider, which are the connector kinds the code has. That also closes the docs audit's M21 ("`HomeLead` names passkeys").
- **Tests:** `SignInBrandingTests` asserts the footer through `Texts.en.Footer`, not a literal, so it follows the change. No test or doc quotes the old strings. The remaining "self-hosted social login" text is in historical design mockups (`design/umbra/B-SignIn*.dc.html`), the MVP spec and review notes, which record what was written then and should stay.
- **CHANGELOG:** one bullet under Unreleased › Changed.

## Merging

It conflicts with `alta/chipper-waltz-z7fp` (footer links, task rp0l) in `Texts.fs`, because both edit the lines around `Footer`, and in `CHANGELOG.md`. Keep both sides: the new `Footer` string, and rp0l's three new fields with their values.
