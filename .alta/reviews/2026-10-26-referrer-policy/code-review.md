# Code review: a site-wide Referrer-Policy of same-origin (task r279)

Branch `alta/graceful-tulip-cnsk`, one commit `93a24c4`. Reviewer: the `umbral` session, 2026-10-26. This task comes from N1 of the rp0l code review.

## Verdict

**Clear to merge.** No blocking items, no nits.

## What was checked

- **The middleware** (`Program.fs`): it sets `Referrer-Policy: same-origin` from `OnStarting`, so it sees the final response. It skips a response whose handler set its own policy, so the passkey invitation page keeps `no-referrer`. It also skips OpenIddict's `form_post` answer, which it identifies through `GetOpenIddictServerRequest().ResponseMode`; that covers error answers sent by `form_post` too.
- **Why not `no-referrer`, and why the `form_post` exception.** Under the Fetch rules, a non-GET request's `Origin` is serialised as `null` when the referrer policy is `no-referrer`, or when it is `same-origin` and the target is cross-origin. Umbral's own form posts are same-origin, so they keep their `Origin`. The only cross-site POST an Umbral page makes is OpenIddict's auto-submitted `form_post` to the app, and the exception keeps its `Origin` for apps that check it. I found no other cross-site POST from an Umbral page:
  - redirects to identity providers and back to apps are GETs;
  - the provider token exchanges, the Fediverse registration among them, are server-side;
  - passkey ceremonies use same-origin fetches.
- **Effect:** a navigation from Umbral to another site (a provider, an app's `redirect_uri` after a redirect, the footer links) carries no `Referer`, so no page tells another site the instance's address or the path. Nothing in Umbral reads `Referer` itself.
- **With `0a34bcf`** (`rel="noreferrer"` on the footer links, already on `main`): both can stay; the links are covered either way.
- **Tests:** `ReferrerPolicyTests.fs` has five tests: public pages; sign-in pages with the provider redirect and the redirect back to the app; admin pages and the sign-in redirect; the invitation page's own policy, set once; and the `form_post` answer with no policy.
- **CHANGELOG:** one bullet under Unreleased › Security.

## Merging

Onto `main` (`e9a595d`), the only conflict is `tests/Umbral.Tests/Umbral.Tests.fsproj`, where both sides add a `<Compile Include>` line (`FooterTests.fs` and `ReferrerPolicyTests.fs`). Keep both.
