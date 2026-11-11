# Code review: several redirect URIs per app (task r3du, spec 012)

Branch `alta/primal-zanzibar-dfrt`, `d6d2373..3650f8f` (the code is `2993ffa`, the tests `30b2f7d`, the layout `2ebb5e6`). Reviewer: the `umbral` session, 2026-11-11. I read the spec's research and route contract, every changed source file and the new tests, and ran the suite myself.

## Verdict

**Clear to merge.** No blocking items and no nits.

## The tests, run by the reviewer

- **The suite.** `git archive` of the branch, then `dotnet build -c Release` (0 warnings) and CI's own command, `dotnet test -c Release --no-build --filter-not-class "Umbral.Tests.IdleMemoryTests"`: **1824 of 1826 passed** in 21 min.
- **The two failures came from my copy, not the branch.** Both are `MetaTests` secret-scan tests, which call `git ls-files` and say "Run the tests inside the git checkout"; an archive has no `.git`. I made the same files a git checkout and re-ran `MetaTests`: **46 of 46 passed**. So 1826 of 1826. Your 1831 likely includes the idle-memory tests, which CI runs in their own job.

## Against the task's four items

1. **Exact matching is kept.**
   - `RedirectUris.acceptable` is unchanged, and both new functions go through it.
   - Umbral adds no matching code. The lists go into OpenIddict's descriptor sets and OpenIddict compares `redirect_uri` and `post_logout_redirect_uri` itself. `Uri(s).OriginalString` gives back the stored string exactly, so a list survives a round trip unchanged.
   - Duplicates are refused when the text is the same or the parsed `Uri` is equal (R4). That is the safe direction. `Uri` equality is looser than OpenIddict's ordinal comparison (host case, user info, escaping), so a near-duplicate is refused rather than silently merged by the descriptor's `HashSet<Uri>`. The pane never says "added" when nothing was.
   - Removal matches the posted string ordinally, so it removes exactly the row that was clicked.
   - The tests sign in through each of two addresses, show a removed address refused at authorize (400, no `Location`), and sign out through each post-logout address, with a removed one landing on `/`, never on the app.
2. **The client ID and secret are unchanged.** It's `PopulateAsync(descriptor, app)`, then one set edited, then `UpdateAsync(app, descriptor)`, with no secret given, so OpenIddict keeps the stored hash. The test that exchanges a code with the app's original secret after an edit pins it, and it passed.
3. **The security log.** One `app_updated` per change: operator, client ID, remote IP, and `redirect_uri_added;uri=<escaped>` (and the removed and post-logout forms). `Uri.EscapeDataString` keeps a `;` or `=` in a query from adding a field, and the test with both in the query proves it. A refusal returns before the write and before the log, so it records nothing.
4. **No schema step: agreed.** OpenIddict's EF store already keeps both lists as JSON arrays, and an app made by today's create form holds a one-item list, so there is nothing to convert and no upgrade path to test. `SchemaVersion` stays 9, and test 6 covers exactly the app that exists today, after an add and after a remove.

## Also checked

- **Who may call the routes.** All four sit behind `AdminAuth.requireOperator` and read the form through `WebForms.readValidated`. A post without the antiforgery token is refused and changes nothing (tested), and an unknown client ID goes back to the list with `app-not-found` (tested). The rules are decided in the handler as well as on the pane, so a direct post can't remove the last redirect URI or go past ten.
- **Two tabs open.** Each request reads the stored list fresh, so a stale page can't put back an address another tab removed.
- **The pane.** The stored address goes through Oxpecker's typed attributes (the hidden `uri` field and the `aria-label`) and into `data-confirm`, all escaped. Remove uses the same `Components.confirmSubmit` as every other destructive button, so the earlier crafted-URL confirmation fix (s7rd) is not undone. Without JavaScript the form posts without asking, as the others do.
- **Loopback on any port** (R3) stays out of this spec, with its follow-up task already queued: `2026-11-11-apps-accept-a-loopback-redirect-on-any-port-for-desktop-apps-spgx.md`.
