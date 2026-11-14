# Task 7t7k, spec 018: remove one linked provider account from a user

Branch `alta/quiet-memphis-783n`, on main `3288b9f` (spec 017). Spec, data model, plan, contract and
tasks: `specs/018-remove-a-link/`.

| Commit | What |
| --- | --- |
| 499a230..b4ccff0 | spec, the designer's answers, two code-review rounds before the build (stranger passkeys; the passkey pages) |
| d6dd634 | DESIGN.md: One Red Fill rule, the row-action sentence (designer's words) |
| da740e8 | T001 storage: `removed_links`, `passkeys.added_via`, schema 15, `removeLink` (transitive passkeys, guarded DELETE, h762 lowering, one transaction), `isRemoved`, `credentialStillHeld` |
| 20879de | T002a: `credential` / `umbral_credential` claims, the removed-account clamp in `finishSignIn`, `Passkey.AddedVia` |
| 38f19a1 | T002b: `EndUserSession` (recheck at authorize and token; `current` for `/account/passkeys` and the Add ceremony, start and finish) |
| 7ba2b7a, 628bcb5, b7fb2d4 | docs, changelog; the docs review's B1 taken as decision 5 / FR-013 |
| 0786250 | T003: the Users card (Linked accounts, the confirm block), `UserLinks` handler, `user_unlinked`, Texts, notice tones |
| 9472ef8 | T003b: `legacy_sessions_ended` (any removal ends the user's pre-upgrade sessions); `Sessions.signIn`'s credential required |

## Where each guarantee lives

- **Never joined again** (decision 1): `Callback.finishSignIn` reads `UserStore.isRemoved` beside spec 017's trust
  clamp; a removed provider account's `EmailVerified` is false before the domain rule, linking, the users row and the
  session. `user_created` carries `reason=link_removed`.
- **Sessions and tokens end** (decision 2, FR-009, FR-012): `EndUserSession.recheck` = s1ug's kind re-check, then
  `credentialStillHeld` (a `link:<fp>` must still be one of the user's links; a `passkey:<id>` must be the user's own,
  `id AND user_id`; unparseable fails closed). Used by authorize, token, `/account/passkeys` (page and remove) and the
  Add ceremony at start and at finish.
- **Stranger passkeys go** (decision 3): `removeLink` deletes `WITH RECURSIVE` on `added_via` first, then the guarded
  link DELETE counts what remains; a refusal rolls both back.
- **Pre-upgrade sessions** (decision 5, FR-013): `removeLink`, `Passkeys.remove`, `Passkeys.removeOwnIfNotLast` record
  the user in `legacy_sessions_ended` in their transactions; `credentialStillHeld` with no reference refuses a recorded
  user first. `Sessions.signIn`'s credential is a required `string`, so no claimless session can be made now.
- **Never in a token an app reads**: `umbral_credential` has no destination (`Authorize.destinationsFor`).

## Known limits (in the spec)

- An access token already issued lasts until it expires (one hour); userinfo serves it. Not closed at userinfo: the
  claim would need the access-token destination. Note for the code review: apps are not granted the introspection
  permission (`AppClients.fs:22`), so the introspection argument I gave is weaker than stated; the destination would
  still put the reference in the reference-token payload. Your call whether to close the window there.
- A refresh already past the check when the removal commits gets one more token, refused at its next use.
- A refused Add start writes two `sign_in_failed` events (the session check's and the ceremony's).
- The toggle's htmx fragment redraws a card without the search term, so Cancel on a block opened after a toggle
  loses the search.
- The confirm block has a red border (`border border-error rounded-md`): the implementer's choice, not in the contract.
  With the block open, the card has two red fills (the block's Remove link and Delete), as the contract asks.

## Tests

`UserLinkStoreTests.fs`, `UserLinkSignInTests.fs`, `UserLinkRecheckTests.fs`, `UserLinkAdminTests.fs`, `DatabaseTests`
(14 → 15), `LinkingRuleTests`, `SecurityEventCoverageTests` (a removal in the journey). Filtered runs per step all
green; the full suite at 9472ef8 is running (result below when it lands).

## Captures

`capture.mjs` (Playwright, Chrome), output in `capture-output.log`. Release build of 9472ef8, a fresh instance on
`http://127.0.0.1:18080` (development exception, `HTTPS_PROXY=http://127.0.0.1:9`), claimed through `/admin/setup`. An
OpenID Connect connector "Open forge" (issuer `http://127.0.0.1:9/forge`, refused, saved disabled) labels the forge links.
Users, links and passkeys can only come from real sign-ins, so the script writes them into the SQLite file before each
shot (fixed dates); every screen state goes through the browser forms. 41 shots, `<state>-<width>-<theme>.png`, 1280 and
360, dark and light, full page; **none overflows**.

| State | On screen |
| --- | --- |
| `card-two-links-passkey` | Alice: GitHub and Open forge links with dates, Remove link on each; Passkeys label and list |
| `card-one-link` | Bob: one Discord link, no passkey: no list (as today) |
| `card-same-provider` | Carol: two GitHub links, same label, different dates |
| `block-plain` | Dave's Discord row opened (422): question, "This cannot be undone.", Remove link (Danger) and Cancel |
| `block-gone` | Grace's forge row: the removed-too list (one passkey added through it) |
| `block-check` | Henry's forge row: the check-these list (a passkey of unknown origin added after the link; one added before is not listed) |
| `block-both` | Alice's forge row: two removed-too passkeys (one through the other, transitively) and one to check |
| `block-focus` (1280 dark) | the block holds the focus on load (`document.activeElement` is the block's div) |
| `notice-removed` | after confirming Dave's removal: "Link removed." (Success) |
| `notice-last` | Erin (one link, her only passkey added through it): "That is Erin Castillo's only way to sign in, so it was not removed. …" (Warning) |
| `notice-not-found` | Frank's link removed meanwhile, then confirmed: "That link was already removed." (Warning) |
