# Task 3694: Client ID and Issuer URL one per row

Branch `alta/rare-prelude-mgc9`, stacked on `alta/buoyant-taiga-cxt3` (v6pm, the screenshot script): merge it after v6pm.

## The change
- `src/Umbral/Handlers/Admin/Apps.fs`, the Connection section of an app's pane: the two copy fields are no longer in a `grid gap-3 sm:grid-cols-2`; they are one per row, as the client secret above them, in a `space-y-3` wrapper (12px), 20px from the redirect-URI lists (the design review's nit 1, taken). Below 640px nothing changes (the grid was one column there already).
- `design/contracts/apps-connectors-master-detail.md`, Detail pane item 3: "in a two-column grid from 640px" becomes "one per row", with the date and the reason.
- `site/scripts/screenshots/capture.mjs`: the `fit` mechanism (try 1180, 1280, 1440, 1600) is gone, since `app-created` was its only user; the frame is taken at 1180 like the other operator screens and its clip ends at the Issuer URL field. The walk now fails if either value is cut at 1180, so the docs image cannot regress silently.
- `docs/images/app-created-{dark,light}.png` retaken by `run.sh` (the other 14 images came out byte-identical).
- `changelog.d/fix-app-connection-fields.md` (Fixed).

## Verification
- Full suite at 406b8b0: **3003 of 3003** passed, 0 skipped (Debug, 24 min 39 s).
- After nit 1 (a wrapper div only): the eight classes that render the app pane (AdminScreen, AppBrandingAdmin, AppConnectorChoice, AppRedirectUris, AppsMasterDetail, DeclaredAdmin, OidcProtocol, Csrf), **155 of 155** passed.
- `.github/scripts/changelog.sh check`: in order.
- `site/scripts/screenshots/run.sh`: exit 0; both values whole at 1180px.

## Captures
Taken 2026-11-15 from a Release build on `http://localhost:18080` in Chrome (Playwright) by `capture.mjs` in this folder: the pane of an app on a later visit, `pane-{1280,360}-{dark,light}.png`. The created state, with the secret shown once, is the docs image `docs/images/app-created-*.png` (1180px).

What each field holds, measured by filling it until it scrolls:

| Width | Holds | Client ID (16) | Issuer `http://localhost:18080` (22) | Page overflow |
| --- | --- | --- | --- | --- |
| 1180 (docs) | — | whole | whole | — |
| 1280 | 55 characters | whole | whole | none |
| 360 | 22 characters | whole | whole | none |

Before: side by side, at 1280 the issuer showed `http://localhost:808`.

## Questions for the designer
1. **360px.** The field holds 22 characters there, so an issuer such as `https://login.example.com` (25) is cut on a phone, as it was before this change. Copy still copies it whole. Is that acceptable, or should the issuer show wrapped below 640px (like the home page's `copyText`)? Not in this change.
2. **Rhythm.** The two fields were 20px apart (the section's `space-y-5`), the same gap as between the issuer and the redirect URIs. A tighter pair (12px) would group them; I kept one rhythm for the section.

Answers (review.md): 1 is a follow-up for the queue (`Components.copyText` for both values); 2 became nit 1, taken: the pair is 12px apart. Captures and docs images retaken after the nit.
