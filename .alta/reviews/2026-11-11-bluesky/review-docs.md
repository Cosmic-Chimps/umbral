# Documentation review: the Bluesky connector (spec 015)

Task b5ky. Branch `alta/snowy-eagle-vdg4`, `df32802..ddcf64d`, docs commits 8e8fa6a and
c5b3907. Reviewed 2026-11-12 by the documentation reviewer, against the code at `ddcf64d`
and the uncommitted design-nit changes in the worktree (`BlueskyConnector.fs`, `Texts.fs`).

## Verdict

**Not clear yet: four blocking items, four nits.** The walkthrough is accurate in almost
everything it claims, and every string it quotes is verbatim in `Texts.fs`. The blocking items
are the two cases your notes raised (the pane on an unusable origin and what a person sees
there), one wrong count in the claims table, and PRODUCT.md. One re-check after the fixes.

## What was checked

- **Every quoted string** in `docs/connectors/bluesky.md` against `Texts.fs` by script: the
  origin alert, metadata label and hint, the development note, both scope hints, the Servers
  hint, Enable Bluesky, the step's title, label and hint, the five step messages (including
  `BlueskyUnsupported` with `{handle}`), and the two callback notices. All verbatim.
  *Sign in with Bluesky* is `SignInWith` with the display name `Bluesky` (fixed in
  `Connectors.fs:1615`, `:1661`): right.
- **Pane labels**: *Shared with every app*, *Any server*, *Only these servers*. Right.
- **The design-nit change in the worktree** (address as wrapping text with Copy under it,
  no Servers placeholder): "with a copy button" still holds, and the doc never mentions
  the placeholder. No change needed.
- **Log reasons** against `Handlers/Connect/Bluesky.fs` and `checkTokenAnswer`:
  `not_a_handle`, `handle_not_found`, `server_not_allowed`, `server_unreachable`,
  `server_unsupported`, `iss_mismatch`, `exchange_failed`, `token_invalid`, `did_mismatch` and
  `not_authoritative` are all real. One is missing (B2).
- **Claims**: `profile` is `https://bsky.app/profile/<did>` (`Bluesky.fs:551`), there is no
  email, and `name` falls back to the handle. Right. The round-trip cookie lifetime of ten
  minutes is right (`OAuthRoundTrip.fs:21`).
- **README** (concept table, docs list, step 4), **integrate.md** (`email`,
  `preferred_username`/`profile`, `idp`), **who-can-sign-in.md**, **sidebar**, and the "Other
  providers" line on the seven other walkthroughs: right.
- **settings.md**: the PLC default and the https rule against `Program.fs:274-292`. Close
  (N2).
- **backup.md**: schema 12 against `Database.fs:301`. Right, with one gap (N3).
- **Changelog fragment**: right. `changelog.sh check` reports "in order".
- **Links**: `check-links.py` reports 342 links in 37 files, all resolve.

## Blocking

### B1. "Before you start": the pane on an unusable origin (your point 1)

Lines 27-32 say the pane "shows only this" and "has no settings and no **Enable** button".
That holds only when there is no connector yet. Even then, the pane also shows the lead line
(`BlueskyCardLead`), so "only this" is not quite right. With a connector that already exists
(for example, the origin changed after it was enabled), `paneOnlyAlert` is false
(`BlueskyConnector.fs:92`). The Set-up and Settings sections stay under the warning, and Save,
Disable and Delete all work. Only Enable is refused, by `ConnectorToggle.fs:146-158`, with
the notice *Not enabled: Bluesky needs Umbral at an https address without a port, reachable
from the internet.* An operator in that case is reading this paragraph, and it describes a
pane they are not looking at.

Suggested text:

> If the instance is not at such an address, the **Bluesky** pane on the **Connectors**
> screen says so: *Bluesky needs Umbral at an https address without a port, reachable from
> the internet. This instance is at {origin}, so Bluesky cannot be enabled here.* (with your
> origin in place of `{origin}`). With no connector yet, that is all it offers: no settings
> and no **Enable Bluesky**, and a crafted request to create one is refused. A connector set
> up before the address changed keeps its sections, so you can still save its settings,
> disable it or delete it. Enabling it is refused with *Not enabled: Bluesky needs Umbral at
> an https address without a port, reachable from the internet.* For a laptop trial, see
> [Gotchas](#gotchas).

### B2. Gotcha "An origin that cannot work": where people land, and the missing reason (your point 2)

The gotcha says people who press the button "are sent to the sign-in error page". They
are not. `originUnsuitable` (`Connect/Bluesky.fs:113-117`) calls `Picker.redirectWithFailure`,
which sends them back to `/signin` with the usual notice *Sign-in didn't complete. Try
again, or choose a different option.* It records `bluesky:origin_unsuitable`, a reason that
appears in neither list in Troubleshooting. Say "are sent back to the sign-in page with *Sign-in
didn't complete…*", and add `origin_unsuitable` (the instance's address cannot work for
Bluesky; see [Before you start](#before-you-start)) to the reasons in Troubleshooting.

### B3. "The last four need the `profile` scope"

The table's last four rows are `preferred_username`, `picture`, `profile` and `idp`. But `idp`
is always there, as `integrate.md` says ("**`idp`** is always there"), and `name` does need
the scope. Name them: "`name`, `preferred_username`, `picture` and `profile` need the
`profile` scope; `idp` is always there."

### B4. PRODUCT.md does not name Bluesky

PRODUCT.md is the product's own description, and it lists every connector kind. This branch
does not change it. The branch's copy is also older than the fixes on
`alta/indigo-pheasant-5jd9` (a6f22ad, 10d646c, 85d75f3: Fediverse, Apple, constitution 3.1.0),
which is not an ancestor here. Editing PRODUCT.md now would conflict with that branch. So do
this after `alta/indigo-pheasant-5jd9` is in main and main is merged into this branch, and make
three changes on top of that version:

- **Product Purpose**, the provider list: "…or an account on a Mastodon or GoToSocial server,
  or a Bluesky account), or with a passkey…"
- **Operating Context**, after the Fediverse bullet: "- The Bluesky connector needs nothing
  created either: Umbral publishes a client metadata document that each person's server
  reads, so it needs Umbral at an https address without a port, reachable from the internet.
  The operator chooses any server or a list."
- **Capabilities, Connector kinds**: "…and four that are one per instance: `apple` (…),
  `bluesky` (servers read Umbral's one metadata document), `fediverse` (…) and `passkey` (…)."

If the maintainer merges this branch before the Apple one, this item moves to whichever
branch merges second. Either way it must be done before both are on main.

## Nits

1. **Cancelling at the server.** A person who presses Cancel at their server comes back to
   *You cancelled the sign-in. Choose an option below to try again.* (`access-denied`,
   `Connect/Bluesky.fs:440-441`). The paragraph after the step messages names only the
   other two notices. Add it. Note also that `exchange_failed` from the round trip carries the
   provider's error after it (`bluesky:exchange_failed;<detail>;server=<host>`), which the
   security-log gotcha's format does not show.
2. **settings.md, `UMBRAL__BLUESKY_PLC_DIRECTORY`.** `http` needs both settings, not just
   `UMBRAL__INSECURE_ALLOW_HTTP`, and a value that is not allowed stops the start
   (`Program.fs:290-292`). Suggested ending: "It must be an `https` address; `http` only with
   `ASPNETCORE_ENVIRONMENT=Development` and `UMBRAL__INSECURE_ALLOW_HTTP=true`. Anything else
   stops the start and names the value."
3. **backup.md skips version 11.** The paragraph goes from version 10 to version 12. Version 11
   (the provider marks, b6d7) adds the empty `apple_logo` column, which is the same kind of
   change. Add one clause: "Version 11 adds an empty column for the Apple button's logo." Also,
   version 12's column is on the person's Bluesky link, not the connector (`provider_server`, the
   server they last signed in through). "adds one empty column that records the server a
   Bluesky user last signed in through" is more exact.
4. **Step 2, the empty list.** "the pane says so" could quote it: *The list is empty, so nobody
   can sign in through Bluesky. Add a server, or choose Any server.* Optional, but the
   walkthrough quotes every other message on the pane.
