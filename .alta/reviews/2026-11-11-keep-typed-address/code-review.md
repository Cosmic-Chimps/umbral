# Code review: keep the typed address after an add is refused as invalid (task k3ep)

Branch `alta/jaunty-rainbow-4jk9`, `ad3cef8..b9c066c`, reviewed as committed (from `git archive b9c066c`, since the worktree had edits in flight). Reviewer: the `umbral` session, 2026-11-11.

## Verdict

**Clear to merge at `b9c066c`.** No blocking items and no nits. The designer's follow-up (the refusal's reason as `why=<code>` and a field error) gets the one re-check.

## The tests, run by the reviewer

From a git checkout of `b9c066c`'s files: `dotnet build -c Release` with 0 warnings, then the classes the change touches.

| Class | Passed |
| --- | --- |
| `AppRedirectUrisTests` | 24 of 24 |
| `AppsMasterDetailTests` | 16 of 16 |
| `RedirectUriTests` | 56 of 56 |
| `MetaTests` | 46 of 46 |

The task's six cases are all tested, plus two good extras: the address can't be read in the `Set-Cookie` header, and a cookie just under the bound stays under 4,096.

## Against the task's checks

- **The cookie's scope.** It is the existing `WebForms.NoticeReason`: Data-Protected, single-use, `SameSite=Strict`, path `/admin`, five minutes. It is about `app` = the client ID, and `consume` answers only a read that names the same notice, `about` and value, so a kept address is for its own app only (tested).
- **Who can plant one.** Only this server writes it, on an operator's own post that passed the antiforgery check. A crafted `?msg=app-redirect-uri-invalid` link gets nothing, because `consume` needs a cookie this server protected. The pane reads it only when the notice is that one.
- **The encoding.**
  - `encodePairs` doesn't escape (I read it), so `list=<route>;uri=<Uri.EscapeDataString>` escapes the address exactly once, and `decode` unescapes it exactly once.
  - An escaped address has no raw `;` or `=`, so `parsePairs` can't be split by one.
  - `decode` answers only for its own list, a non-empty value, and something that unescapes.
- **The size bound.** The bound is checked on the raw value and again on the encoded reason, which is the one that matters: escaping can triple a URI. A long address is refused and not kept, and the field comes back empty as before.
- **The pane.** The value goes through Oxpecker's typed `value` attribute (escaped; the test with separators and quotes checks it), with `aria-invalid="true"` only when something was kept. `consume` deletes the cookie on the read, so a second visit shows the field empty (tested). There is no new script.
- **Nothing in the URL or the log.** The redirect is the same `?msg=` as before (tested: the address isn't in `Location`). The refusal path stashes and redirects before any `SecurityEvents.record`, so it logs nothing.

## For the re-check (the designer's follow-up)

Only one thing I'll look at in particular: `why` must be a code from the closed `Problem` set, drawn as Umbral's own text from `Texts.fs`. It must never be text taken from the cookie, so the cookie keeps carrying only what this server chose to say.

## Re-check at `9a8e1c7` (2026-11-11)

**Clear to merge.** It takes the designer's nit: the reason a value was refused is shown under the field.
- **What I held it to.** `why` travels as a code from the closed `RedirectUris.Problem` set. `problemOfCode` turns any other code into nothing, so a forged or stale code draws no reason line (the address still comes back). The sentence drawn is always `Texts.AppUriProblem problem`, never text from the cookie.
- **The rule is the same.** `check` makes every decision the old `acceptable` made, in the same order, with the wildcard first. `acceptable` is now only words on top of it, and re-parses solely to keep the file-URI advice apart from the plain "not an absolute URL". No address is accepted or refused differently, and `RedirectUriTests` still checks the messages.
- **The pane.** `p#{addId}-error.um-field-error` comes after the hint. The input's `aria-describedby` names it only when there is a problem. The handler's refusal now carries `(notice, problem option)`, and only an add with a problem stashes anything.
- **Tests run by the reviewer**, from a git checkout of `9a8e1c7`'s files: `dotnet build -c Release`, 0 warnings.

  | Class | Passed |
  | --- | --- |
  | `AppRedirectUrisTests` | 28 of 28 |
  | `AppsMasterDetailTests` | 16 of 16 |
  | `RedirectUriTests` | 63 of 63 |
  | `MetaTests` | 46 of 46 |
