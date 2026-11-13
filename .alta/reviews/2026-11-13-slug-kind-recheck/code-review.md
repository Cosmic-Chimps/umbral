# Code review: sessions fail closed when their connector's slug is gone or names another kind (task s1ug)

Branch `alta/brave-puffin-wbmj`, `30d4626..69595db` (the fix is `5d09cb0`). Reviewer: the `umbral` session, 2026-11-13.

## Verdict

**Clear to merge.** No blocking items; two nits, both "write it down or decide".

## What was checked

- **The premise: no new claim is needed.** `idp` holds the connector's *kind*, not its slug (`Sessions.IdpClaim`, set from `kind` in `Sessions.fs`; research R3 of feature 003), and the session cookie and every code and refresh token carry it. `connector` holds the slug. So comparing the slug's current connector with the kind the sign-in carries is exactly the check the task asked for (option 1).
- **`ConnectorStore.recheckKind`:**
  - no connector under the slug is `Deleted`;
  - the same kind passes;
  - any other kind is `OtherKind`;
  - a sign-in carrying no kind is treated as another kind, so it fails closed.
- **Authorize.** It runs first in the offered branch. The slug is `Sessions.currentConnector`, which falls back to `idp` for a session from before feature 003, when every slug equalled its kind. A session with neither claim looks up `""`, finds nothing and is signed out, so that case fails closed too. A refusal signs the session out and records `connector_changed;reason=…;stage=authorize[;kind=…]`.
- **Token.** It runs for both grants when the token carries a `connector` claim, before the email-domain and per-kind re-checks. A refusal is `invalid_grant` with the same detail at `stage=token`. Folding the old refusal blocks into one `refuse` helper doesn't change their details or answers.
- **The behaviour change.** A refresh through a deleted connector is now refused, where specs/006 judged it by the instance's email list alone. That's the task's intent, it's in the changelog, and `AllowedDomainsSignInTests` was rewritten for it. A disabled connector behaves as before.
- **Tests.** For Fediverse, Bluesky and Microsoft, an OIDC connector written under the old slug at store level (`Journeys.replaceWithOidc`, since the admin form refuses a kind's own name for another kind) refuses the remembered session and the refresh. The `SecurityEventsTests` cases cover the detail.

## Nits

- **N1. Write down that re-creating the same kind under the same slug carries old sign-ins over.** `recheckKind` compares kinds, so deleting an OIDC connector `corp` (issuer A) and creating another OIDC connector `corp` (issuer B, other rules) keeps admitting A's sessions and refresh tokens, judged by B's rules. The `connectors` table has no immutable ID (`slug` is the primary key), so binding a session to one specific connector would need a new column and a new claim, which is more than this task. The re-creation is the operator's own deliberate act. Say so where deleting a connector is described (the delete confirmation's wording or the documentation), so nobody expects a re-created connector to start from no sessions.
- **N2. A token chain from before specs/006 skips the new check.** `Token.fs` runs `recheckKind` only when the token has a `connector` claim. Refresh tokens rotate and keep their claims, so a chain begun before 006 can still be alive and goes straight to the domain check. Either fail it closed (no `connector` claim means it can't be shown to match, so the holder signs in again, once), or say in the code comment why skipping is acceptable (for example, if every such chain has expired by now under the refresh lifetime). I'd fail it closed, for the same reason `recheckKind` refuses a missing kind.

## Re-check at `2bb9aec` (2026-11-13)

**Clear to merge.** Both nits are taken.
- **N2, fail closed** (`ed4a6ea`). In `Token.fs`, a code or refresh token with no `connector` claim is now `Connectors.Unrecorded`, refused as `invalid_grant` with `connector_changed;reason=unrecorded;stage=token`. The union moved to `Domains.Connectors` as `ConnectorChange`, so the log detail is built from the same three cases the check returns and can't drift from it. No end-to-end fact is acceptable, since the code exchange now refuses such a token before one can be minted through the real flow, and adding a production hook only to make one would be worse. Allowed email domains shipped in 0.1.0, so only a pre-release chain can meet this.
- **N1, written down.** The case of the same kind re-created under the same slug is in `recheckKind`'s doc comment, in `notes.md` ("What this does not cover"), in `docs/connectors/oidc.md` beside the delete-and-re-add advice, in troubleshooting, and in the changelog fragment.

**Tests run by the reviewer**, from a git checkout of `2bb9aec`'s files: `dotnet build -c Release` with 0 warnings. `AllowedDomainsSignInTests` 41 of 41, `BlueskySessionTests` 8 of 8, `FediverseSessionRecheckTests` 5 of 5, `MicrosoftRecheckTests` 10 of 10, `SecurityEventsTests` 28 of 28. `check-links.py` resolves 385 links in 38 files, and `changelog.sh check` is in order. Your full run before these commits was 2762 of 2765, the three being `reset-credential` CLI timeouts under load that pass alone (9 of 9); the CLI isn't touched here.
