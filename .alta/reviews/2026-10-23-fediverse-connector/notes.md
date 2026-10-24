# Spec 010, the Fediverse connector: notes for review

Task `2026-10-23-fediverse-connector-sign-in-with-a-fediverse-account-f3dv`.
Branch `alta/earnest-cetus-9pd3` (worktree `~/.alta/worktrees/umbral/earnest-cetus-9pd3`).

**Base.** This branch was fast-forwarded onto `alta/urbane-zambezi-t9pn` (the
Connectors list-and-detail task, review clear, not yet on `main`) at `fb2156e`,
so the connector is built as a **pane** in that frame, not as a card. Merge
`alta/urbane-zambezi-t9pn` first; this branch then sits on top of it.

**Commits.** `fb2156e..HEAD`: the spec (`specs/010-fediverse-connector/`), the
design contract correction from `umbral-designer` (cherry-pick `30aa886` of
`44e7872`), the implementation, tests and docs.

There are two reviews below: design (`umbral-designer`) and documentation
(`umbral-documentation`).

---

## Design review

Design contract: `design/contracts/fediverse-connector-ui.md` (sections 1 to 4),
read against the master-detail contract.

### What to look at

| Capture (`<state>-<width>-<theme>.png`) | State |
| --- | --- |
| `connectors-off` | Connectors screen, Fediverse entry selected, not set up |
| `connectors-any` | enabled, Any server |
| `connectors-list` | enabled, Only these servers with `mastodon.social, fosstodon.org` |
| `connectors-list-refused` | the form drawn again after a refused entry (`*.example.social`), 422 |
| `picker` | sign-in page of an app with Discord, Fediverse, GitHub, Passkey |
| `picker-branded` (1280) | the same on an app with an accent and a logo |
| `step` | the account step, empty, autofocus on the field |
| `step-branded` (1280) | the step in the branded chrome (extra, not in section 5) |
| `step-unsupported` | `@someone@akko.wtf`: a real Akkoma server, refused for no PKCE |
| `step-not-a-handle` (1280) | `not a handle` (extra) |
| `step-refused` | Only these servers (`fosstodon.org`), `@ana@mastodon.social` typed |
| `step-focus` (1280 dark) | keyboard focus on the field |

Measured on every capture (from the capture script): no horizontal overflow at
any width; on every refused state `aria-invalid="true"` is set and the message
is the `.um-field-error` under the field; focus is on the `account` field on
every step state (GET and 422).

### Choices to confirm

1. **Callback field label (new copy, not in the contract).** The providers'
   label is "Redirect URI to register with the provider", which is wrong here:
   the operator registers nothing. The pane uses `FediverseCallbackLabel`
   "Callback URL" and the hint `FediverseCallbackHint` "Umbral registers this
   address on each server. There is nothing to paste anywhere; a server's admin
   sees it in their list of apps."
2. **Empty list note (new copy, the contract has none).** `FediverseServersEmptyNote`,
   a warning under the field when Only these servers is saved with no server:
   "The list is empty, so nobody can sign in through Fediverse. Add a server, or
   choose Any server." (spec Edge Cases require the pane to say so.)
3. **The radio and shared box after a refused save** show what the operator
   submitted, not the stored values; the badges in the heading keep the stored
   state.
4. **Supported software** in the step hint and the unsupported error: Mastodon
   and GoToSocial only (your correction of 2026-10-24; Akkoma and Pleroma have no
   PKCE).
5. The picker button is the ordinary `SignInWith "Fediverse"`, placed where the
   picker's existing order puts it.

### Texts.fs strings added

Contract section 5, verbatim: `FediverseCardLead`, `FediverseNoEmailNote`,
`FediverseScopeAnyLabel`, `FediverseScopeAnyHint`, `FediverseScopeListedLabel`,
`FediverseScopeListedHint`, `FediverseServersLabel`, `FediversePlaceholder`,
`FediverseServersHint`, `FediverseServersEmptyNote` (proposed, item 2),
`FediverseEnableButton`, `FediverseServerNotAddress`, `FediverseServerWildcard`,
`FediverseStepTitle`, `FediverseAccountLabel`, `FediverseAccountPlaceholder`,
`FediverseAccountHint`, `FediverseContinue`, `FediverseOtherWays`,
`FediverseNotAHandle`, `FediverseUnsupported`, `FediverseNotAllowed`,
`FediverseUnreachable`; notices `fediverse-connector-saved` and
`fediverse-connector-invalid`. Added after the captures were first taken:
`FediverseCallbackLabel`, `FediverseCallbackHint` (item 1).

### CSS

No class was added: `build-css.sh` regenerates `umbral.css` with no change. The
pane uses `.um-choice` radio cards, `Components.fieldWith`, `.um-alert-info`,
`.um-alert-warning`; the step uses the picker's card and button classes. The
Impeccable detector was not run: there is no CSS change to run it on.

---

## Documentation review

Checklist: T10 and section 6 of `docs/audit-2026-10.md` (branch
`worktree-docs-audit-2026-10`). One deviation from T10: it lists Pleroma/Akkoma
as working; they have no PKCE, so the walkthrough says they are refused (agreed
with `umbral-designer`, spec Clarifications).

### Files

- `docs/connectors/fediverse.md` (new)
- `README.md`: opening paragraph; the Connector row of the concepts table;
  provider walkthrough links; the prerequisites bullet; quick start step 4
  ("GitHub is one way"); the claims list (`profile`, `idp` = `fediverse`);
  "Who can sign in".
- `CHANGELOG.md`: one `### Added` block under `## Unreleased`.
- `docs/connectors/{discord,github,google,oidc,passkey}.md`: Fediverse in their
  "Other providers" line.

### Walkthrough sections, in order

What it is and which servers work; Before you start; 1. Nothing to create at a
provider; 2. Enable the connector; How a person signs in; The server list;
Scopes Umbral requests; What people see; What Umbral takes from the server; What
the server learns; Allowed email domains; Troubleshooting; Gotchas. (No "Giving
one app its own name": there is one Fediverse connector per instance.)

### Statements and the code behind them

| Statement | Code |
| --- | --- |
| Supported: Mastodon 4.3+ (metadata with S256 and a registration endpoint), GoToSocial (NodeInfo); others refused | `src/Umbral/Domains/Fediverse.fs:337` (`fromMetadata`), `:357` (`fromNodeInfo`); `Infrastructure/Providers/Fediverse.fs` `discover` |
| Registers an app named "Umbral", `read:accounts`, the callback, the public origin as website | `Infrastructure/Providers/Fediverse.fs:363-365`; `Handlers/Connect/Fediverse.fs:193` (website) |
| Client stored per server, secret with the local key ring in `keys/`, whatever store connector secrets use | `Infrastructure/FediverseServers.fs:34` (purpose `Umbral.FediverseServers.v1`, `IDataProtectionProvider`); `Infrastructure/DataDirectory.fs:34` |
| Four accepted input forms; capitals, trailing slash, `:443` tidied | `Domains/Fediverse.fs:79` (`normaliseServer`), `parseInput` |
| A handle's domain is asked where the account lives (WebFinger) | `Infrastructure/Providers/Fediverse.fs:243` (`lookup`) |
| The list is checked before any contact, for the typed name and the name WebFinger gives | `Handlers/Connect/Fediverse.fs:160` (`checkHost`, called for both) |
| Wildcards and non-addresses refused, nothing saved | `Domains/Fediverse.fs:186` (`parseServers`); `Handlers/Admin/Connectors.fs:1252` (`saveFediverse`, 422 redraw) |
| The list is checked again at the callback; refusal lands on `account-not-allowed` | `Handlers/Connect/Fediverse.fs:371` (`callbackHandler`) |
| PKCE S256 on every sign-in; the access token is used once for the account and dropped | `Providers/Fediverse.fs` `authorizeUrl`, `exchange` (`:474`), `account` (`:489`) |
| Subject is server + account id; `name` falls back to the username; `preferred_username` = `user@server`; `profile` only when https; no email | `Providers/Fediverse.fs:549` (`toProfile`), `:540` (`httpsOnly`); `Handlers/Connect/Authorize.fs:55` |
| `idp` is `fediverse` | `Sessions.signIn` with `connector.Kind.Value` via `Callback.finishSignIn` |
| Allowed email domains do not apply | `Infrastructure/EmailDomainRules.fs:73`; `Callback.finishSignIn` called with `applyEmailRule = false` |
| Apps screen: limited exactly when Only these servers | `Handlers/Admin/Apps.fs:239` |
| Checked at most once a day; re-registered once on `invalid_client`; a refused re-registration shows "did not answer" | `Handlers/Connect/Fediverse.fs:25` (`recheckAfter`), `ensureRegistration`, `refusalOf` |
| A changed public origin re-registers on the next sign-in | `Handlers/Connect/Fediverse.fs:259` |
| `invalid_client` at the code exchange deletes the stored client | `Handlers/Connect/Fediverse.fs:443` |
| Private, loopback addresses refused; checked again at connect time; the development setting lifts it | `Domains/Fediverse.fs:262` (`isPublicAddress`); `Providers/Fediverse.fs:61` (`connect`), `:108`; `Handlers/Connect/Fediverse.fs:125`; `Program.fs:205` |
| 10 s per call, no redirects, 64 KiB bodies | `Providers/Fediverse.fs:53-55`, `:108` |
| Rate limit: the sign-in limit, 30 a minute per address | `Handlers/Connect/Fediverse.fs:469`; `Infrastructure/RateLimits.fs` |
| Security log: `fediverse_server_registered` with `server=<host>`; `sign_in_failed` with `fediverse:<reason>;server=<host>` | `Handlers/Connect/Fediverse.fs:242`, `record'` calls |
| Deleting the connector keeps registrations and people | no foreign key from `fediverse_servers` (`Infrastructure/Database.fs`, schema 7); `linked_accounts` keyed by kind |
| "Other ways to sign in" only when the app offers more than one connector | `Handlers/Connect/Fediverse.fs:98` |

Every quoted UI string in the docs is from `Views/Texts.fs` (the list above, plus
the existing notices `sign-in-failed`, `access-denied`, `account-not-allowed`,
and `ConnectorSharedLabel` "Shared with every app").

### How it was verified

- The walk: a local instance (Release build, Development with
  `UMBRAL__INSECURE_ALLOW_HTTP=true`, which OpenIddict needs over plain http)
  through the capture script: enabling, Any server, a server list, a refused
  entry, the picker, the step, a real Akkoma server refused, an unlisted server
  refused, all as the walkthrough describes.
- Tests: `FediverseDomainTests` (160), `FediverseClientTests` (25),
  `FediverseSignInTests` (8), `FediverseAdminTests` (9),
  `FediverseStepErrorsTests` (14), `FediverseReRegistrationTests` (5), plus a
  fourth `IdleMemoryTests` run. Full suite: see the commit that adds this file.
- `.github/scripts/check-links.py`: no new finding (the one it reports is the
  known `oidc.md#configuration-as-code` anchor, W15, which the CI step allows).
- `.github/scripts/changelog.sh released` against `main`: every released section
  unchanged.

### Idle memory (SC-007)

`IdleMemoryTests` has a fourth run, an instance with the Fediverse connector
enabled and its account step used (GET and two refused POSTs), held to the
150 MiB local-store ceiling on Linux. On this Mac (arm64, sanity ceiling only):
fediverse 120.7 MiB, local store 115.9 MiB, passkey in use 134.5 MiB, aws store
130.3 MiB. The Linux figure comes from CI.

---

## Answers to the reviews (2026-10-24)

### Design (`review.md`): clear at 67eced4

The optional nit is taken: `.um-field-error` is `items-start` and its icon has
`mt-0.5 shrink-0`, so the icon sits on the first line of a message that wraps
(`umbral.input.css`, `umbral.css` regenerated; `build-css.sh --check` clean).
Commit `39f8012`.

### Documentation (`review-docs.md`)

**Merge state.** `main` now has the Connectors master-detail task (merged at
`fb2156e`) and T1 and T9. This branch merged `main` in at `5489c6f`, so T1's
README Upgrading paragraph is here, and this branch is the second to merge.

**Blocking 1, schema 7.** Done here:
- `CHANGELOG.md`, `## Unreleased`: a `### Upgrade` block, "The database becomes
  schema version 7. An older image refuses to start on it: take a backup before
  you upgrade.", and the idle-memory line (the Fediverse run is held to the
  150 MiB local-store ceiling on Linux, `IdleMemoryTests`).
- `README.md`, Upgrading: "The database is at **schema version 7**."

**Nits, all taken:**
1. Section 2: "It needs no client ID or secret. The **Callback URL** it shows is
   the one Umbral registers on each server itself; there is nothing to paste
   anywhere."
2. The opening states the rule first (metadata with `S256` and app registration,
   or GoToSocial), then the names with each one's reason (Akkoma and Pleroma: no
   PKCE; Misskey and Sharkey: no app registration; Mastodon before 4.3: no
   metadata). Same in the CHANGELOG bullet.
3. Gotchas: "to `localhost`, to an IP address typed as the server, or to a name
   that resolves to a private, loopback or link-local address". Kept "an IP
   address" on purpose: outside development `normaliseServer` refuses every IP
   literal, public or not (`Domains/Fediverse.fs:79`, the `isLiteralHost` branch;
   `FediverseDomainTests` "IP literals ... refused"), so a public literal is
   refused too. Only the resolved-name rule is about non-public addresses.
4. Section 1: "in the database in the data directory, the secret encrypted with
   the key ring in `keys/`".
5. README claims bullet: "`name` and `picture` come with the `profile` scope when
   the provider supplied them; a fediverse sign-in adds `preferred_username`
   (`user@server`) and `profile` (the person's page)."

Checks after the changes: `check-links.py` 182 links in 14 files, all resolve;
`changelog.sh released` against `main`, every released section unchanged.
