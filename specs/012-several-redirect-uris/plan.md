# Implementation Plan: Several redirect URIs per app

**Spec**: [redirect-uris.spec.md](redirect-uris.spec.md) · **Research**: [research.md](research.md) · **Contract**: [contracts/routes.md](contracts/routes.md)

## Summary

Make an app's redirect URIs and post-logout redirect URIs editable lists (up to
10 each) on the app's pane, through OpenIddict's own storage, so nothing about
the schema, the client ID or the secret changes. A pure rule in
`Domains/RedirectUris.fs` decides each add and removal; a new handler module
reads, writes through `IOpenIddictApplicationManager`, and logs an
`app_updated` event per change.

## Technical Context

F# on .NET 10, Oxpecker views, OpenIddict 7.7.1 (EF Core, SQLite), xUnit with
the in-process test server. No new dependency.

## Constitution Check

- **I. Three concepts**: no new concept; a property of an App.
- **II. One artifact**: nothing new to run; no memory cost worth measuring.
- **III. Standards**: OpenIddict keeps validating `redirect_uri` and
  `post_logout_redirect_uri`; Umbral writes no matching code.
- **IV. Secure by default**: exact matching kept, wildcards still refused,
  antiforgery on every POST, every change in the security log.
- **VI. Documentation**: integrate and native-apps pages, changelog fragment.
- **VII. Default answer is no**: says where a person may be sent back, not what
  anyone may do. Loopback-any-port is left out (research R3).

## Files

- `Domains/RedirectUris.fs`: `maxPerApp`, `add`, `remove` (pure).
- `Views/Texts.fs`: the words and notice codes of the contract.
- `Handlers/Admin/AppRedirectUris.fs` (new, after `Apps.fs`): the four routes.
- `Handlers/Admin/Apps.fs`: the Connection section's two lists and forms; the
  refusal codes drawn as warnings.
- `Program.fs`, `Umbral.fsproj`: one line each for the new module.
- Tests: `RedirectUriTests.fs` (pure rule), `AppRedirectUrisTests.fs` (new:
  admin, log, sign-in, sign-out, secret).
- Docs: `docs/integrate.md`, `docs/native-apps.md`,
  `design/contracts/apps-connectors-master-detail.md` (one line),
  `changelog.d/012-several-redirect-uris.md`.

No schema change (research R1).
