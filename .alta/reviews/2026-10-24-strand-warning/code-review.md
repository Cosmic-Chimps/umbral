# Code review: confirm buttons built from URL values (task s7rd)

Branch `alta/hardy-eagle-3xq2`, `6f0467e..9d08c0f`. Reviewer: the `umbral` session, 2026-10-26. I read the whole diff against the task and the two code reviews it came from (Connectors and Apps master-detail). Each point below was checked against the code.

## Verdict

**Clear to merge.** No blocking items; two nits that need no re-check. Going further than the task asked, so that every confirm button is gated by the cookie (the strand confirms on both screens and **Remove anyway**), is the right call. It also fixes **Remove anyway**, which had the same flaw since 0.1.0.

## What was checked

- **Where a confirm button comes from now.** Every confirm button needs a single-use reason that only the refusing POST writes (`WebForms.NoticeReason.redirect`/`stash`). That covers the Connectors strand (delete, disable, unshare), the Apps strand and the allowed-domains **Remove anyway**. The reason cookie is:
  - data-protected (purpose `Umbral.NoticeReason.v2`), `HttpOnly`, `SameSite=Strict`, path `/admin`, and valid for 5 minutes;
  - bound to the notice, what it is about (`slug`, `app`, `list`) and the id;
  - deleted on the first read whatever it holds.

  An attacker cannot make the refusing POST (antiforgery) or forge the cookie, so a link alone draws only the notice's fixed text.
- **The Connectors strand.** The warning is built only when:
  - the selection is `Item s`;
  - the query's `slug` equals `s`;
  - `s` is a stored connector;
  - the reason matches the notice (delete, disable or unshare);
  - at least one app is still stranded.

  Every action is `detailPath stored.Slug + …`, from the stored row and escaped. `StrandWarning.Stored` is no longer optional, so no branch can draw a form for a connector that doesn't exist.
- **The unshare confirms.** Passkey posts the stored `enrolment`, and Fediverse its stored `scope` and `servers`, so the save is no longer refused (Passkey) and no longer wipes the server list (Fediverse, unreleased). `PasskeyEnrolment` is a plain value in the model, so `.Value` can't fail.
- **Remove anyway.** It's drawn only on the domains pane, only when the instance list isn't empty, and only with the cookie.
- **The Apps strand.** The pending choice comes from the cookie, keyed to the app id from the path, through `encodePairs`/`parsePairs`, never from `choice` or `connectors` in the query. The form action is `detailPath warning.ClientId`, and the button gains `confirmSubmit`. The connector slugs are already validated as known slugs before the reason is stashed, so they hold no `;`, `=` or `,`.
- **One-time values.** A client secret is consumed only when `selection = Item row.ClientId`, and a passkey invitation link only when `selection = Item "passkey"`. The list URL's implicit selection no longer uses one up. Every response under `/admin` sets `Cache-Control: no-store` from `OnStarting`, so no handler can leave another value behind. Static assets are untouched (tested).
- **The other items:**
  - the notice drawn twice after deleting the last app is fixed;
  - a refused domain list on a slug whose kind already has a row goes back to the list with `connector-unknown` instead of a pane-less 422;
  - a connector stored under a reserved slug is logged at start.
- **Tests:** `CraftedLinkTests.fs` (13) covers:
  - the crafted `../apps/<id>`, `../users/<id>`, other-slug and unknown-slug links on both screens;
  - a refusal drawing its form once, and only on its own pane;
  - both unshare confirms end to end;
  - one-time values not used up by another page;
  - `no-store`.

  `AdminScreenTests.adminRoutes` now includes the connector routes and `/admin/apps/new` and `/admin/apps/<id>`, so the signed-out and setup-redirect tests cover them.
- **CHANGELOG:** under Unreleased, Fixed and Security, naming the affected versions (0.1.0 to 0.5.0).

## Nits (no re-check needed)

- **N1. The startup warning tells the operator to "Delete it with POST /admin/connectors/{Slug}/delete".** That POST needs a signed-in session and an antiforgery token, so it can't be done by hand. Either say what does work (for example, a short README entry under Troubleshooting), or make that one pane reachable some other way. Low priority: no such slug is likely to exist, because the add form never accepted them once they were reserved.
- **N2. The CHANGELOG's Security bullet starts with "A crafted Connectors link"** but also covers the Apps screen and the domains list. "A crafted link to the admin screens" would be accurate.

## Release note for the maintainer

`## Unreleased` now holds both the Fediverse connector (a new feature, schema version 7) and this fix. The release workflow releases from `main`, so the next release is **0.6.0** (a minor version, with the schema 7 Upgrade note), not a 0.5.1 patch. Release it soon after merging: the crafted-link bug is in every version up to 0.5.0.

## Tests

A full `dotnet test` at `9d08c0f`, run by the reviewer: **1699 passed, 0 failed, 0 skipped** (8 min). It agrees with the implementer's 1699/1699.
