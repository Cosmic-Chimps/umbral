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

## Part B: the operator pastes Apple's logo (`12ce8b7`, `61a8ee6`, reviewed at `dc93dcf`)

**Clear to merge, with one nit worth taking (N1).** It meets the requirement above, and does better than filter: it rebuilds.

### What was checked

- **Nothing pasted is ever echoed.** `render` writes the SVG from values `parse` rebuilt, never from the pasted text:
  - the `viewBox` and every transform are re-formatted from parsed, finite numbers;
  - `d` passes a one-class path-data alphabet;
  - `fill-rule` is one of exactly two words.

  None of these can hold a quote, `<` or `&`, so no attribute can be closed and no element opened, whatever was pasted. The rules on `on*`, `href` and `style` with `url(` refuse suspicious input but aren't what keeps the page safe; the rebuild is. Only `svg` and `path` are ever written.
- **Only a parsed logo can reach `raw`.** `AppleLogo`'s fields are private, so `parse` is the only way to make one, and `Components.appleLogoSlot` takes an `AppleLogo option`, not a string. A later change can't pass pasted text to it by mistake. `ConnectorStore.appleLogo` parses the column again on every read, so a value written into the database by hand is refused before it reaches a page (tested with SQL).
- **The parser.**
  - `DtdProcessing.Prohibit` and no resolver, so no entity expansion and no external fetch.
  - CDATA and text outside `title` and `desc` are refused, and any element outside the list is refused.
  - Nesting is capped at 16, paths at 64, `d` at 16 KiB and the input at 32 KiB. The rendered size is checked too, because group transforms are written into each path.
  - No exception escapes.
- **The routes.** `POST /admin/connectors/apple/logo` and `/logo/remove` sit behind `requireOperator` and `readValidated` (picked up by `CsrfTests`). A refusal is a 422 that never draws the paste back. The events are `connector_updated` with fixed details, `apple-logo=saved|removed`. The picker reads the logo only when Apple is offered.
- **Tests run by the reviewer**, from a git checkout of `dc93dcf`'s files: `dotnet build -c Release` with 0 warnings, and `build-css.sh --check` exits 0.

  | Class | Passed |
  | --- | --- |
  | `AppleLogoTests` | 35 of 35 |
  | `AppleLogoAdminTests` | 14 of 14 |
  | `AppleButtonTests` | 9 of 9 |
  | `ProviderMarkTests` | 8 of 8 |
  | `DatabaseTests` (schema 11) | 35 of 35 |
  | `CsrfTests` | 6 of 6 |
  | `SignInFlowTests` | 39 of 39 |
  | `MetaTests` | 46 of 46 |

### Nit

- **N1. The number pattern backtracks quadratically.** In `numberPattern`, `[0-9]+\.?[0-9]*` lets two digit runs compete. When the `.` is absent and the token ends in a character that isn't a digit, .NET's backtracking engine tries every split. Measured with the same pattern and options:

  | Input | Time |
  | --- | --- |
  | 1,000 digits, then `x` | 23 ms |
  | 4,000 digits, then `x` | 0.4 s |
  | 16,000 digits, then `x` | 6.3 s |
  | 32,000 digits, then `x` | 29 s |

  The input cap allows the last one, as a `viewBox`, a root `width` or `height`, or a transform's arguments. So one paste can hold a request thread and a core for half a minute. The reach is small: only a signed-in operator can paste, and a valid stored number matches at once. But the fix is one line, and the pattern exists to be the safe part. Write it so the runs can't overlap:

  ```fsharp
  let private numberPattern = @"[-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][-+]?[0-9]+)?"
  ```

  On 13 cases (`1`, `-1.5`, `1.`, `.5`, `1e5`, `1.5E-3`, `+0.25`, `1.2.3`, `e5`, `.`, `--1`, `1e`, `01.10`) it gives the same answers as today's pattern, and the 32,000-digit case takes 5 ms. `RegexOptions.NonBacktracking` on the current pattern also works (1 ms). Add the 32,000-digit `viewBox` as a test with a time bound.

### Re-check at `55bc21c` (2026-11-12)

**Clear to merge.**
- **N1 is taken.** `numberPattern` is the one-group form, with a comment saying why. The new test feeds 32,000 digits then `x` into a `viewBox`, a `width` and a transform, each under `MaxChars` so it reaches the parser, and each must be refused in under a second.
- **The design fix to Remove logo.** The button sits on Save's row and posts the separate `apple-logo-remove` form through its `form` attribute, so it never sends the pasted text. That form is a sibling of the Save form, not nested inside it: a nested `<form>` would be dropped by the browser's parser. It holds the antiforgery token and carries `data-confirm`, and `umbral.js` confirms on the submit event of the form actually submitted, so the confirmation still runs.
- **Tests run by the reviewer**, from a git checkout of `55bc21c`'s files: `dotnet build -c Release` with 0 warnings, `build-css.sh --check` exits 0, and `AppleLogoTests` 36 of 36, `AppleLogoAdminTests` 14 of 14, `AppleButtonTests` 9 of 9.
