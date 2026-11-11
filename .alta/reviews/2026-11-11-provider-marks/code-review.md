# Code review: provider marks on the sign-in buttons (task b6d7, part A)

Branch `alta/bold-dove-crws`, `aad649e..b329dbe` on `main` `8df9daa` (the code is `f129bf6`). Reviewer: the `umbral` session, 2026-11-11.

## Verdict

**Clear to merge.** No blocking items and no nits. There is one requirement for part B, written down below before anyone builds it.

## What was checked

- **The mark is chosen by kind, never by display name.** `Picker.leading` matches on the connector's kind: `GitHub` gets the GitHub mark, `Passkey` the Key icon, and the rest keep the link icon. The Apple slot is unchanged. A connector named "GitHub" that isn't the GitHub kind can't borrow the mark.
- **The SVG is inlined safely.** `Components.githubMark` uses `raw`, but its only input is `Theme.GitHubMarkPath`, a `[<Literal>]` compiled into the binary, so no operator or user text can reach it. It is `aria-hidden` and `focusable="false"`, so the button's name stays its text.
- **The licence.** The path is Primer Octicons `mark-github-24` (MIT), credited in `Theme.fs` with its source. The mark itself is GitHub's trademark, used as GitHub's logo rules allow (to say "sign in with GitHub", in black or white, not redrawn). The reasoning per provider is in `specs/014-provider-marks/research.md`.
- **Black or white only.** I checked the comment's claim against the tokens:
  - On the secondary button, the CSS pins the mark to `#fff` in the dark theme and `#000` in the light one. The selectors are the same three as `.um-btn-apple`'s (`:root`, `[data-theme="light"]`, and `[data-theme="system"]` under `prefers-color-scheme: light`).
  - On the primary button the mark keeps `currentColor`. That's correct only because the picker uses the primary button solely when the app chose an accent (`Picker.fs`). Under an accent, `Handlers/Branding.fs` sets `--color-on-primary-fill` to exactly `0 0 0` or `255 255 255` (`Accent.onAccent`). Umbral's own themes' `on-primary-fill` (`#2a1d05`, `#f5c26b`) never reaches a GitHub button.
- **The stylesheet.** `build-css.sh --check` exits 0: the committed `umbral.css` matches a fresh build.
- **Tests run by the reviewer**, from a git checkout of `b329dbe`'s files: `dotnet build -c Release`, 0 warnings.

  | Class | Passed |
  | --- | --- |
  | `ProviderMarkTests` | 8 of 8 |
  | `AppleButtonTests` | 7 of 7 |
  | `SignInFlowTests` | 39 of 39 |
  | `PageAssetsTests` | 23 of 23 |
  | `MetaTests` | 46 of 46 |

  I didn't repeat the full suite (2092 of 2092 in your run): the change is limited to the picker's leading icon and one CSS block.

## For part B (not built yet; a requirement before it is)

Part B lets the operator paste Apple's logo as SVG. **An SVG pasted by a person must never go through `raw`, as this constant one does.** SVG can carry `<script>`, event handlers (`onload`), `<foreignObject>` with HTML, and external references (`<use href>`, `<image>`). Inlined on the sign-in page, that becomes script running in Umbral's origin, on the one page where people sign in.

Whoever builds it should choose one of these, and say which in its spec:
- **serve it as an image.** `<img src>` from an Umbral route, with `Content-Type: image/svg+xml`, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'` and `X-Content-Type-Options: nosniff` on that response. A browser runs no script in an SVG loaded as an image;
- **or sanitise it to an allowlist** before storing: `svg`, `path`, `g`, `rect`, `circle` and presentation attributes only; no `script`, `foreignObject`, `use`, `image` or `on*`; no `href` of any kind; and a size cap.

Either way, a test should paste a hostile SVG and check that it does nothing.
