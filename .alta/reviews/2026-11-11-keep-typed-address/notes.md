# Task k3ep: keep the typed address after an add is refused as invalid

- **Task:** `.alta/tasks/2026-11-11-apps-keep-the-typed-address-after-a-refused-add-k3ep.md`. It comes from nit 3 of the spec 012 design review.
- **Branch:** `alta/jaunty-rainbow-4jk9` (worktree `~/.alta/worktrees/umbral/jaunty-rainbow-4jk9`), started from `main` at `ad3cef8` (spec 012 and spgx merged).
- **Commit range:** `ad3cef8..b9c066c` (implementation). This folder is committed on top of it.

## What changed
- **`Handlers/Admin/AppRedirectUris.fs`:** an add refused with `app-redirect-uri-invalid` (only that refusal, on either list) stashes `list=<route>;uri=<Uri.EscapeDataString address>` in `WebForms.NoticeReason` (`about` = `app`, `value` = the client ID). The redirect is unchanged: `/admin/apps/{clientId}?msg=app-redirect-uri-invalid`. Nothing is logged for a refusal, as before.
- **`Handlers/Admin/Apps.fs`:**
  - A new public module, `KeptAddress` (`MaxLength = 2000`, `encode`, `decode`). Over 2,000 characters, before or after escaping, nothing is carried, so the field comes back empty as it did before.
  - `appPane` consumes the cookie once, only for that notice. Each list decodes with its own route, so the value goes back only into the field of the list it was typed in.
  - The add input gets the value through Oxpecker's typed `value` (escaped) and `aria-invalid="true"`. The existing `.um-input[aria-invalid]` style draws the red border; there is no new CSS, and no `build-css.sh` run was needed.
  - **No focus script:** the pane moves focus to no notice's field today, so none was added.
  - **Notice text:** unchanged, the fixed text. Showing the refusal's own reason ("contains a wildcard…") was left out until the designer rules on it (see Questions).
- **`changelog.d/keep-typed-address.md`:** under **Changed**.
- **Texts.fs:** no wording changes.

## Tests (`tests/Umbral.Tests/AppRedirectUrisTests.fs`)
- a refused redirect URI comes back in its own field and not in the other
- a refused post-logout URI comes back in its own field and not in the other
- the refused address is not in the Location or readable in a Set-Cookie
- a second visit to the page shows the field empty
- an address with separators and quotes comes back whole and escaped
- an address over the bound is refused and not kept
- an address just under the bound comes back, in a cookie a browser keeps (name and value under 4,096)
- a kept address is for its own app only
- KeptAddress round-trips what it carries
- KeptAddress carries nothing for another list, an empty address, too much or garbage

**Results:** filtered `AppRedirectUris` 24/24. The full suite was 1,896/1,896 before the under-the-bound test was added; that test is test-only.

## Captures
Taken 2026-11-11 from a Release build on `http://localhost:18080` in Chrome (Playwright), by `capture.mjs` in this folder. Each refused state is a real submission of the add form in the browser, because the cookie is single-use.

| State | What was typed |
| --- | --- |
| `refused-invalid-*` | A redirect URI with a wildcard in the host (long, wraps nowhere: it is in an input) |
| `refused-invalid-post-logout-*` | A post-logout URI with a fragment |
| `refused-invalid-refresh-1280-dark` | The same page after a reload: the field is empty and the notice keeps its fixed text |

Each state is at 1280 and 360, dark and light. No state overflows horizontally at 360. In every capture, the walk checked that the other list's field was empty and had no `aria-invalid`.

## Questions for the designer
1. **The refusal's own reason.** Should it travel in the same cookie and be shown with the notice? `RedirectUris.acceptable` already says why: "is not an absolute URL", "contains a fragment…", "contains a wildcard…". It is not in this change.

## Follow-up for the design nits (`9a8e1c7`)

- **Nit 1, the reason under the field:** the reason travels as a code, `list=..;why=<code>;uri=..`, from the closed set `RedirectUris.Problem`. The page reads it back with `problemOfCode`, so an unknown code shows no reason line, and the text is always Umbral's own `Texts.AppUriProblem` with the designer's four strings. It is drawn as a `um-field-error` after the hint, and the input's `aria-describedby` is `<hint> <error>`. `RedirectUris.check` returns the Problem; `acceptable` keeps its messages word for word on top of it.
- **NoHost is unreachable on .NET 10:** every http(s) address with no host fails `Uri.TryCreate`, so the operator sees NotAbsolute. The string is kept for completeness.
- **Nit 2, the notice wording:** the notice ends "then try again."
- **Tests:** the four affected groups (AppRedirectUris, RedirectUri, AppsMasterDetail, Meta) pass, 158/158. The full suite was 1,908/1,908 on these sources (the child's run).
- **Captures:** retaken from `9a8e1c7` with the same names, by the same script. The reason line is visible: wildcard on the redirect list, fragment on the post-logout list.
