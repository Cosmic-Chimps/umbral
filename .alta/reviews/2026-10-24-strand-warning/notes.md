# Confirm buttons built from URL values: notes for the code review

Task: `.alta/tasks/2026-10-24-strand-warning-trusts-the-url-slug-s7rd.md` (main checkout).
Branch `alta/hardy-eagle-3xq2` (worktree `~/.alta/worktrees/umbral/hardy-eagle-3xq2`), from `main` at `6f0467e`.

## The approach: a confirm button needs the cookie the refusal wrote

The task asked for a strand warning only when the query's slug is a stored connector and the selected
pane. I went one step further, the shape the task gives for the Apps screen, and used it for every
confirm button on both screens: **a confirm is drawn only for a change the operator just submitted**,
proven by the existing Data-Protected, single-use `WebForms.NoticeReason` cookie, which the refusing
POST writes and the pane reads once. The query keeps only `msg` (and `slug` on Connectors, which the
cookie must match). This covers everything the task lists and also:

- a crafted `?msg=connector-delete-strands-apps&slug=discord` on Discord's own pane: with only the
  stored-and-selected check it would still draw a working **Delete anyway** that skips the strand
  check; now it draws nothing to click;
- the same flaw in **Remove anyway** of the allowed email domains (`allowed-domains-removal`), found
  on the way: any pane, or the list URL, with `?msg=allowed-domains-removal` drew a form that clears
  the instance list in one click. Now only `/admin/connectors/domains`, after the refused POST.

Without the cookie (a link, a refresh, another pane) the notice's fixed text is drawn as a warning, with
no list and no form. A refresh after a real refusal therefore loses the button; submitting again brings
it back. That is the behaviour the OIDC and branding notices already have.

### Connectors (`Connectors.fs`, `ConnectorToggle.fs`, `ConnectorDelete.fs`, `PasskeyConnector.fs`)

- The five refusals (`toggle`, `delete`, generic save, Passkey save, Fediverse save) call
  `NoticeReason.redirect … "slug" <slug> <"disable"|"delete"|"unshare">`. The `change=unshare` query
  parameter is gone: the action comes from the cookie.
- `render` builds a `StrandWarning` only when: strand code, `selection = Item s`, query `slug = s`,
  a stored connector `s`, the cookie for that code and slug, an action consistent with the code, and a
  non-empty list of stranded apps.
- `StrandWarning.Stored` is a `Connector` (no option); every action is `detailPath stored.Slug + …`.
- `noticeKind`: the two strand codes and `allowed-domains-removal` are warnings (the fixed text used
  to fall through to Success).

### Passkey unshare confirm: hidden fields, not a special case in the save

The smaller change: the unshare confirm posts what the kind's save reads, from the stored connector.
Passkey: `enrolment` (`stored.PasskeyEnrolment.Value`, `invitations`/`open`, what
`Passkeys.parseEnrolment` reads). The save handlers are unchanged.

**Fediverse had the same bug** (unreleased: the connector is new on `main`): its save requires `scope`
and `servers`, so the confirm was refused as `fediverse-connector-invalid`. Had `scope` passed, an
empty `servers` would also have wiped the list. The confirm now posts `scope`
(`Fediverse.scopeValue`) and `servers` (the stored list, one per line). The generic provider save
needs nothing more: without an `allowedDomains` field it keeps the stored list.

### Apps (`Apps.fs`): the cookie, not a 422 re-render

`setConnectors` stashes `choice=…;connectors=…` (`NoticeReason.encodePairs`) keyed `app`/client id and
redirects with `?msg=app-connectors-strand` only, as the branding notices do. (No `&app=`:
`AppsMasterDetailTests` asserts app redirects carry none.) `itemNotice` reads the choice only from the
cookie. The confirm form goes through `Components.confirmSubmit` ("Save this choice anyway? "{name}"
will offer no way to sign in.", new `AppConnectorsStrandConfirmMessage`) and `detailPath`. I chose the
cookie over a 422 re-render so both screens work the same way and POST/redirect/GET stays.

### One-time values

- `Cache-Control: no-store` on every response under `/admin`: a middleware in `Program.fs`
  `configureApp`, set in `OnStarting` so no handler can leave another value. Static files are under
  `/assets` and `/branding`, untouched.
- The new client secret is consumed only on `/admin/apps/<id>` (not the list URL's implicit first app),
  the Passkey invitation link only on `/admin/connectors/passkey`. `create` already redirects to the
  app's own URL.

### Nits

- Apps, empty list: the notice is drawn once, at the top; the create pane draws one only on `/new`.
- Reserved slugs `new` / `domains`: the add form already refused them. Versions 0.1.0 to 0.4.0
  accepted both (`^[a-z0-9-]{1,32}$`, only kind names reserved), so one can exist; its page cannot be
  opened, so `initialize` logs a warning at start naming it. The start is not stopped. (See N1 below
  for the remedy.)
- Refused domain list to a slug with no stored row: the pane of a kind **not set up** exists and draws
  the draft (the first set-up), so that still answers 422 with the pane. Only when the slug has no row
  and another connector of that kind exists (no pane at all: a stale form) does it go to the list with
  `connector-unknown`, as the other handlers do. Fediverse always has a pane, so it is unchanged.

## Released versions with the bugs

From `git grep` on each tag:
- Connectors strand confirm from `?slug=` (unescaped, in a form action), Apps strand confirm from
  `?choice=&connectors=`, and **Remove anyway** from `?msg=` alone: **every release, v0.1.0 to
  v0.5.0**.
- Passkey unshare confirm refused: v0.2.0 (when Passkey arrived) to v0.5.0.
- Secret or invitation link used up by the list URL: v0.5.0 (the master-detail screens).
- Fediverse unshare confirm: not released.

The release is the maintainer's call; CHANGELOG has it under Unreleased → Fixed / Security. As the
code review notes, Unreleased also holds the Fediverse connector and schema 7, so the next release
from `main` is 0.6.0, not a 0.5.1 patch.

## Tests

New `tests/Umbral.Tests/CraftedLinkTests.fs` (13 tests):
- nine crafted Connectors links (`../apps/<id>`, `../users/<id>`, another connector's slug with and
  without `change=unshare`, an unknown slug, the pane's own slug without the cookie, the list URL)
  draw no `confirm=yes`, no `data-stranded-app`, no form action with `..`, `/admin/apps` or
  `/admin/users`. On the old code the first link drew a form with a `..` action;
- a refused delete and a refused disable draw the confirm once on the connector's own pane (a
  refresh draws none), and not on another pane or the list (which leave the cookie for the right one);
- Apps: the crafted `?msg=app-connectors-strand&choice=chosen&connectors=` draws no form; the real
  refused POST draws it with `data-confirm`, once;
- allowed domains: the crafted removal link draws no confirm; the refused POST does, once;
- Passkey unshare anyway, end to end: the confirm form's own fields posted back give
  `passkey-connector-saved`, not shared, enrolment kept. The same for Fediverse (scope and servers
  kept);
- the client secret and the passkey invitation link are not used up by the list URL (or the domains
  pane), and are shown on the item's own URL, once;
- `no-store` on `/admin/apps/<id>`, `/admin/connectors/passkey`, `/admin/apps`, `/admin/users` and a
  POST redirect; not on `/assets/umbral.css`.

`AdminScreenTests.adminRoutes` gains `GET /admin/apps/new`, `/admin/apps/{id}`, `/admin/connectors`,
`/new`, `/domains`, `/{slug}`, and the POSTs `/admin/apps/{id}/connectors`, `/admin/connectors`,
`/{slug}`, `/{slug}/toggle`, `/{slug}/delete`, `/passkey`, `/passkey/invitations`, `/fediverse`,
`/admin/allowed-domains`; the setup-redirect and sign-in-redirect tests cover them.

`dotnet test tests/Umbral.Tests`: 1699 passed, 0 failed, 0 skipped.

## Visible changes

None in the real flows: the strand warnings are drawn as before. A crafted or refreshed link now shows
the fixed warning text without a list or button (before: the list and button). The Apps confirm adds
the browser's native `confirm()`, no drawn element. I did not send a design review for this.

## Answers to the code review (`code-review.md`: clear to merge, two nits)

- **N1, the startup warning pointed to a POST nobody can make by hand.** Agreed. Versions 0.1.0 to
  0.4.0 did accept `new` and `domains`, so such a connector can exist, and today nothing but the
  database reaches it. Making its page reachable is more than a nit inside a security fix:
  `detailPath` is concatenated with `/delete`, `/toggle` and `?msg=` all over. So the warning now
  says only what is true: the page cannot be opened, the connector still signs people in, and it
  cannot be changed or deleted from the admin screens in this version. The CHANGELOG line matches.
  The real fix is proposed as the follow-up task
  `2026-10-26-a-connector-saved-under-the-slug-new-or-domains-can-be-opene-4z87`.
- **N2, "A crafted Connectors link".** Now "A crafted link to the admin screens".
- **Release note.** Taken: the notes above now say 0.6.0.
