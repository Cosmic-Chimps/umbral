# Documentation review: spec 010, the Fediverse connector

Task: `.alta/tasks/2026-10-23-fediverse-connector-sign-in-with-a-fediverse-account-f3dv.md`;
checklist T10 and process section 6 of `docs/audit-2026-10.md`.
Branch: `alta/earnest-cetus-9pd3` at `67eced4` (doc commit `b09a342`, corrections
after `fb2156e`), on `main` at `1f3a89e`.
Reviewed 2026-10-24 by the documentation reviewer session, from the diff, the
"Documentation review" section of `notes.md`, the connector's code, and my own
runs. The design review by `umbral-designer` is separate; this file is the
documentation half.

## Verdict

**Not yet clear. One blocking item (the changelog's Upgrade entry for schema
version 7, and the README's schema version), five nits.** Fix the blocking
item, take the nits you agree with, answer in `notes.md`, and I re-check once.

Everything else in T10 is met: the walkthrough has every required section in
the walkthroughs' order, "What the server learns" included; every quoted UI
string is in `Texts.fs` verbatim (I checked all nineteen); every behavioural
statement I tested against the code holds; the README's five touch points and
the CHANGELOG entry are where T10 asked; the link check adds no finding.

## What was checked

Against the code on the branch, statement by statement:

- **Which servers work.** `Domains/Fediverse.fs:337-352` (`fromMetadata`):
  supported when the OAuth metadata lists `S256` and names authorization,
  token and registration endpoints on the server's own host; `:357-367`
  (`fromNodeInfo`): otherwise only software named `gotosocial`. So Akkoma,
  Pleroma, Misskey, Sharkey and Mastodon before 4.3 are refused by
  construction, whatever they may add later, and the deviation from T10
  (Pleroma/Akkoma refused) is right. The refusal text is
  `FediverseUnsupported` verbatim.
- **Registration.** `Providers/Fediverse.fs:355-366`: `client_name` "Umbral",
  the callback as `redirect_uris`, `scopes` `read:accounts`, `website` the
  public origin. Stored per host in `fediverse_servers` (`Database.fs:169-172`),
  the secret as Data Protection ciphertext with purpose
  `Umbral.FediverseServers.v1` (`FediverseServers.fs:34`), so it is under the
  key ring in `keys/` whatever `UMBRAL__SECRET_STORE` says.
- **Identity and claims.** `fediverseSubject` is `{host}#{accountId}`
  (`Domains/Fediverse.fs:376`); `toProfile` (`Providers/Fediverse.fs:549-561`):
  display name with the username as fallback, avatar and page only when https,
  `Username` as `user@server`, no email. `Authorize.fs:121-126` and
  `UserInfo.fs:58-63` emit `preferred_username` and `profile` when set; the
  other four providers set `Username = None`, so today only a fediverse user
  has `preferred_username`. `idp` is the kind's value `fediverse`
  (`Domains/Connectors.fs:47`).
- **The server list.** Checked for the typed name and the WebFinger name before
  any contact (`Handlers/Connect/Fediverse.fs:160`), and again at the callback
  (`:371`), landing on `account-not-allowed`. Wildcard and not-an-address
  texts verbatim. The Apps screen counts the connector as limited exactly on
  "Only these servers" (`Handlers/Admin/Apps.fs:239-240`). Allowed email
  domains never apply (`EmailDomainRules.fs:73`).
- **Addresses.** `isPublicAddress` (`Domains/Fediverse.fs:262-290`) refuses
  loopback, private, link-local, CGNAT, unique-local, multicast, documentation
  and tunnel ranges; a non-public result shows `FediverseNotAHandle`
  (`Handlers/Connect/Fediverse.fs:150,166`), as the Troubleshooting section
  says. The development pair lifts it (`Program.fs:205`).
- **Re-registration.** Re-check after 24 hours (`Handlers/Connect/Fediverse.fs:25`);
  `invalid_client` at the exchange deletes the row and the next attempt
  registers afresh (`:440-446`); a changed origin re-registers (`:259`).
- **Security log.** `fediverse_server_registered` with `server=<host>`
  (`SecurityEvents.fs:91`, `Handlers/Connect/Fediverse.fs:242`);
  `sign_in_failed` details `fediverse:<reason>;server=<host>` (`:443-447`).
- **Rate limit.** The three routes carry `RateLimits.SignIn`
  (`Handlers/Connect/Fediverse.fs:467-470`), which is 30 a minute
  (`RateLimits.fs:99`).
- **Timeouts.** 10 s per call, 64 KiB bodies (`Providers/Fediverse.fs:53-55`).
- **README.** Opening paragraph, Connector row, walkthrough links,
  prerequisites, quick start step 4, the claims bullet, "Who can sign in": all
  present and pointing at the right anchors. **CHANGELOG:** one `### Added`
  block under `## Unreleased`, in the voice of 0.2.0; `changelog.sh released`
  is clean per the notes.
- **Links.** `check-links.py`: the one failure is W15 (`oidc.md:181`), which is
  the same on `main` and which T1 removes on its own branch.

## Blocking

1. **Schema version 7 has no Upgrade entry, and the README will say 6.**
   `Database.fs:272` has `SchemaVersion = 7` (the two connector columns and
   the `fediverse_servers` table, `:371-375`), and an older image refuses a
   version 7 database as it did for 6 (`:385-389`). The CHANGELOG's header
   requires an **Upgrade** subsection "when an upgrade needs more than pulling
   the new image", and 0.2.0 set the precedent for a schema step: "The database
   becomes schema version 6. An older image refuses to start on it: take a
   backup before you upgrade." Nothing under `## Unreleased` says so for 7.
   And the README: on this branch it still carries the two old paragraphs
   (lines 543 and 552, "schema version 4" and "schema version 6"); T1 (reviewed
   clear, unmerged) replaces them with one `### Upgrading` paragraph that says
   "The database is at **schema version 6**". After both merge, the README
   states 6 and the code is 7.
   - Add under `## Unreleased` a `### Upgrade` block: "The database becomes
     schema version 7. An older image refuses to start on it: take a backup
     before you upgrade." (Add the memory line there too if you want it on the
     record, as 0.2.0 did: the Linux figure from CI.)
   - For the README, say in `notes.md` which branch sets the version to 7:
     if this one merges after T1, change T1's paragraph to 7 when you merge
     `main` in; if before, T1's implementer does it, and I will tell them. The
     maintainer decides the order; see the note at the end.

## Nits

1. **"It needs no client ID, secret or callback URL"** (section 2). The pane
   shows a field labelled **Callback URL** (`FediverseCallbackLabel`) with the
   hint "Umbral registers this address on each server. There is nothing to
   paste anywhere". A reader who has just been told there is no callback URL
   and then sees one will stop. Say "It needs no client ID or secret. The
   **Callback URL** it shows is the one Umbral registers on each server itself;
   there is nothing to paste anywhere."
2. **State the rule, then the names.** The opening and the CHANGELOG say
   Akkoma, Pleroma, Misskey, Sharkey and old Mastodon "cannot do the PKCE check
   Umbral requires". For Misskey and Sharkey the code's reason is different: no
   app registration (`fromNodeInfo`'s comment, and your own Gotchas says it
   right). One sentence with the rule the code applies makes the list a
   consequence and survives the next server that qualifies: "A server works
   when its OAuth metadata advertises PKCE `S256` and app registration, or when
   it is GoToSocial, which enforces PKCE without publishing metadata. Akkoma
   and Pleroma do no PKCE; Misskey and Sharkey have no app registration Umbral
   can use; Mastodon before 4.3 publishes no metadata." Same fix in the
   CHANGELOG bullet.
3. **"Umbral will not send a registration to `localhost`, an IP address or a
   name that resolves to a private address"** (Gotchas). The code refuses
   non-public addresses; a bare IP literal is parsed (`parseInput` handles
   IPv6 literals and ports), so a public literal is not refused by this rule.
   Say what the code does: "to `localhost`, to a private, loopback or
   link-local address, or to a name that resolves to one".
4. **"The secret is encrypted with the local key ring, in `keys/`"** (section
   1). It reads as if the secret were in `keys/`. The secret is in `umbral.db`
   (`fediverse_servers.client_secret`), encrypted with the key ring that lives
   in `keys/`. "kept in the database, encrypted with the key ring in `keys/`".
5. **The README claims bullet** now reads "`name`, `preferred_username`,
   `picture` come with the `profile` scope when the provider supplied them, and
   `profile` (the person's page) for a fediverse sign-in." Audit W1 (T2, not
   yet run) says `preferred_username` was never emitted; your change makes it
   true for the fediverse only. Since you are editing the sentence anyway,
   move it: "`name` and `picture` come with the `profile` scope when the
   provider supplied them; a fediverse sign-in adds `preferred_username`
   (`user@server`) and `profile` (the person's page)." Then the bullet is true
   today and T2 has one thing less to do. I will tell T2's implementer either
   way.

## notes.md

Complete under section 6: files, the section order, a table of every
behavioural statement with file:line, the quoted strings, how it was verified
(the walk, the six test classes, the link check, the changelog check), and the
memory figures. The deviation from T10 is stated with its reason and its
agreement. One addition for the re-check: the Upgrade entry and the answer to
the README version question.

## For the maintainer (outside this task)

T1 (`alta/festive-marble-n7qj`) writes "schema version 6" into the README's new
Upgrading paragraph; this branch takes the schema to 7. Whichever merges second
must change that one number. If 010 merges second, its implementer does it
when merging `main` in (blocking item 1 above asks them to say so); if T1
merges second, T1 needs a one-line follow-up. Merging T1 first is simpler.
