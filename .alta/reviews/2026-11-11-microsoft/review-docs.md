# Documentation review: the Microsoft accounts connector (spec 016)

Task m5ft. Branch `alta/warm-monsoon-8gdd` on main `a82d8ec`; docs commit f4f0188, read against
the code up to 07b350e. Reviewed 2026-11-13 by the documentation reviewer.

## Verdict

**Not clear yet: three blocking items, four nits.** The walkthrough is careful and honest about
what was not checked live. Every string it quotes is verbatim in `Texts.fs`, and every log word
and identity rule matches the code. The blocking items are one notice that says something false
(a product question as much as a docs one), one claim about the tenant ID, and PRODUCT.md. One
re-check after the fixes.

## What was checked

- **Every quoted string** against `Texts.fs` by script: the field labels, the four card titles,
  the display-name hint, the six field refusals, the four notices with `(<reason>)`, *This
  account can't be used to sign in here…* and *Sign-in didn't complete*. All verbatim. Default
  display name "Microsoft" (`MicrosoftConnector.fs:206`), and no card is preselected on a new
  connector (`:212-216`). Right.
- **Log words**: `refused: tenant_not_allowed;tenant=<tid>` (`AccountRefused` →
  `ProviderAdapters.fs:61`, the notice `account-not-allowed` via `Picker.fs:60`);
  `microsoft:tenant_not_allowed;tenant=<tid>` at authorize and token (`Authorize.fs:282`,
  `Token.fs:125`); `id_token_invalid: tenant|key_issuer|subject` (`Providers/Microsoft.fs:171-191`)
  and `issuer` from the shared validator; `rejected: invalid_client`; `no_correlation_cookie`.
  Right.
- **Identity and claims**: `<tid>#<oid>` (`Domains/Microsoft.fs:113`), an `oid` that is not a
  GUID refused, `email_verified` only with an `email` and the JSON literal `true` in `xms_edov`,
  `name` then `preferred_username`, no avatar, "any work or school account" excludes the
  personal tenant (`admits`). Right.
- **README, oidc.md, integrate.md, who-can-sign-in.md, sidebar, the navigation lines**: right.
- **backup.md**: schema 13 (`Database.fs:306`), one nullable `microsoft_audience` column. Right.
- **Changelog fragment**: right, except one phrase (N4). `changelog.sh check` reports "in order".
- **Links**: `check-links.py` reports 383 links in 38 files, all resolve.

## Blocking

### B1. "Saved, but not enabled" on a connector that stays enabled

When an existing, enabled connector is saved and the discovery check fails, the update path
(`saveMicrosoft` → `applyChanges` → `ConnectorStore.saveMicrosoft`) writes the change and leaves
`is_enabled` alone. It then shows `connector-saved-microsoft-unreachable` or `-wrong`, which say
*Saved, but not enabled…*. The test
``saving a saved connector while Microsoft is unreachable keeps it enabled and still says so``
asserts exactly this, so it is deliberate. But it does two things it should not:

- **The notice is false** for that connector. The walkthrough's rows say "The connector is saved
  … then press **Enable**". An enabled connector has no Enable button, only Disable.
- **It departs from FR-003**: "An unreachable or wrong answer saves the connector disabled".
  For the *wrong* case this matters. An operator who mistypes a new tenant ID on an enabled
  connector (a GUID that names no tenant) gets a connector that stays enabled, now pointing at
  nothing. Every person's sign-in then fails.

This needs a decision before the docs can describe it, and it touches code and the contract, so
take it to the code reviewer too. My suggestion:

- **wrong** → disable, as FR-003 says, because the saved settings cannot work;
- **unreachable** → keep it enabled (an outage at Microsoft is no reason to switch sign-in off,
  and the previous settings did work), with a notice of its own that says so. For example:
  *Saved. Umbral could not reach Microsoft to check the change (<reason>); the connector stays
  enabled.*

Then the walkthrough says which happens. In "Enter the values in Umbral", after "**Enable** makes
the same check every time", add: "Saving a connector that is already enabled checks again. If
Microsoft's answer is not usable, the connector is disabled. If Microsoft cannot be reached, it
stays enabled and the pane says so." Add a row to "What the form says" for the new notice. If you
choose otherwise, amend FR-003 and the contract, and still make the notice true.

### B2. The tenant ID is not kept when another card is saved

The table says of **Directory (tenant) ID**: "Umbral keeps what you typed if you choose another
card, and ignores it." Only a *refused* form keeps it (the draft, `MicrosoftConnector.fs:219-221`).
Nothing stores a tenant ID except inside "one organization" (`OneOrganization g`; there is no
tenant column). So once you save another card, the field is empty the next time. Say instead:
"With another card the field is ignored. A refused form keeps what you typed, but a saved one
does not: choosing **one organization** again means entering the tenant ID again."

### B3. PRODUCT.md does not name Microsoft

This branch is on current main, whose PRODUCT.md names Apple and Bluesky. Three places:

- **Product Purpose**: "(Discord, GitHub, Google, Apple, Microsoft, any OpenID Connect provider
  the operator runs, …"
- **Operating Context**, the credentials bullet: "…at Discord, GitHub, Google, Apple, Microsoft
  or their own OIDC provider…". Then a bullet of its own after the Apple-related text or the
  credentials bullet: "- The Microsoft connector's client secret expires (180 days recommended,
  two years at most) and Umbral cannot see the date, so the operator renews it; the operator
  chooses personal accounts, one organization, any work or school account, or both."
- **Capabilities, Connector kinds**: "…and five that are one per instance: `apple` (…),
  `bluesky` (…), `fediverse` (…), `microsoft` (Microsoft shows the registration's name, so one
  registration per instance) and `passkey` (…)."

## Nits

1. **The display-name refusal is missing from "What the form says".** Add a row: *Enter the label
   for the sign-in button, then save again.* | The display name is empty. (It is the other panes'
   message, as the contract says; that is fine.)
2. **The page's own navigation line links to itself.** Line 9 lists `[Microsoft](microsoft.md)`.
   Every other walkthrough leaves itself out. Drop it.
3. **Two token failures are not in Troubleshooting.** `id_token_invalid: malformed` (an identity
   token that does not parse, `Providers/Microsoft.fs:166`) and `id_token_missing` (the token
   answer has none, `:155`). Add both to the row about "something between Umbral and
   Microsoft". The full log text is `malformed_response: id_token_invalid: …`. The page uses the
   short form as oidc.md does, which is fine.
4. **Where narrowing ends a session.** "Changing who may sign in" says the person is refused at
   authorize or at refresh, "and their session ends". Only the authorize path signs them out
   (`Authorize.fs`, `Sessions.signOut`). At refresh the token request is refused (`invalid_grant`,
   "This account can no longer be used to sign in."). Say "…refused the next time an app
   authorizes them with their existing session, which ends it, or the next time an app refreshes
   their token". The changelog's "ends, at their next sign-in, the sessions" should say the same:
   "…turns away people no longer admitted the next time an app authorizes them or refreshes their
   token".

## Re-check at 1572212 (2026-11-13)

**Clear to merge.** All three blocking items and all four nits are fixed (`ad2ee92..1572212`:
docs 0511a79, code 84cdaed, design nits and their doc rows 1572212).

- **B1**: the new rule is better than the one I suggested. A changed choice that fails the check
  is not saved at all (422, reason under the choice or the tenant ID). An unchanged choice saves
  the other changes: an enabled connector stays enabled with the new *Saved. Umbral could not
  check the connector…* notice, and a disabled one keeps the "Saved, but not enabled" notices,
  which are now true. Code (`Connectors.fs:2737-2758`, the `unchecked` notice for an enabled
  stored connector), FR-003, the contract and the walkthrough agree. `invalid_tenant` reads as
  *No Microsoft tenant has this ID* (`OidcDiscovery.fs:199`). The four new strings are verbatim
  in `Texts.fs`.
- **B2**: the tenant ID row now says a saved form does not keep it. Right.
- **B3**: PRODUCT.md names Microsoft in all three places. Right.
- **N1-N4**: in. The changelog fragment's narrowing sentence is fixed as well.
- `check-links.py`: 383 links in 38 files, all resolve. `changelog.sh check`: in order.

One optional wording point, not a condition: "The notices about Microsoft appear at the top of
the pane, except the last one" should say "except the last three". All three *Nothing was saved*
messages are drawn under a field (`MicrosoftConnector.refusedChoice`: under the tenant ID with
one organization, else under **Who may sign in**). That includes *Microsoft did not answer…*,
whose row does not say where it appears.
