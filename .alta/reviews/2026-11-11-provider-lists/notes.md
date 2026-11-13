# Task l1st, name Apple (and the Fediverse) wherever the providers are listed: notes for review

- **Task**: `2026-11-11-name-apple-and-the-fediverse-where-providers-are-listed-l1st`.
- **Branch**: `alta/emerald-anthem-tqws` (worktree `~/.alta/worktrees/umbral/emerald-anthem-tqws`), on main at `0bc654d`.
- **Precondition**: met. Apple (spec 013), Bluesky (spec 015) and Microsoft (spec 016) are all on `main`
  (`Domains/Connectors.fs:14-52` has the nine kinds).
- **Scope note**: the task was written when Apple was the one missing kind. Bluesky and Microsoft landed
  after it, so the home sentence names every kind the Connectors screen lists, not Apple alone;
  otherwise it would fall behind again the day it merges.
- **PRODUCT.md**: not touched, as the task's update of 2026-11-12 says (`umbral-documentation` did it).

## What changed

### `src/Umbral/Views/Texts.fs`, `HomeLead` (the lead paragraph of `GET /`)

Was:

> A self-hosted sign-in server. Your apps offer passwordless sign-in through one OpenID Connect issuer:
> a passkey, or an account people already have at Discord, GitHub, Google, a Mastodon-compatible server
> or any OpenID Connect provider.

Is:

> A self-hosted sign-in server. Your apps offer passwordless sign-in through one OpenID Connect issuer:
> a passkey, or an account people already have at Discord, GitHub, Google, Apple, Microsoft, a
> Mastodon-compatible server, Bluesky or any OpenID Connect provider.

- **The order** of the named providers is the Connectors screen's, read from
  `Handlers/Admin/Connectors.fs:1204-1214` (`kindOrder`: Discord 0, GitHub 1, Google 2, Apple 3,
  Microsoft 4, OpenID Connect 5, Fediverse 7, Bluesky 8, Passkey 9) and `rowsFor`, which closes the list
  with Fediverse, Bluesky, Passkey. "Any OpenID Connect provider" stays **last**, as the sentence's
  catch-all: the design review's one nit (`review.md`, taken), which corrects the task's "screen's order"
  for that one item. At `bda34fe` it sat before the Mastodon-compatible server. The passkey stays first in
  the sentence, as it was: the sentence opens with what Umbral does on its own, then the accounts people
  already have.
- **The Fediverse** keeps its words, "a Mastodon-compatible server", as the task asks (the kind's display
  name on the screen is "Fediverse", `Connectors.fs:76`; the sentence stays in words a visitor knows).
- **Bluesky** is named as "Bluesky", the kind's display name (`Connectors.fs:77`).
- **Tests**: no test asserts the sentence (`grep` of `HomeLead`, `Mastodon-compatible` and `already have at`
  over `tests/` finds nothing). `HomeTests` checks the headings and the links of the page, not the lead.
- **Where it shows**: only the home page, `Handlers/Home.fs:28`.

### `README.md`, line 6 (the opening paragraph; also the start page of the docs site)

Found in step 4. The opening sentence listed Discord, GitHub, Google, Apple and Microsoft, Mastodon and
GoToSocial servers, any OpenID Connect provider, the forges, and the passkey. **Bluesky was missing** from it,
while the Connector row of the concepts table (line 21), the connector links (line 62), the quick start
(line 102) and the Connectors section (line 252) all name it. Added "to Bluesky," after the Mastodon and
GoToSocial clause, before the OpenID Connect one, which is where the README's own order puts the Fediverse.

## Other lists looked at (step 4), unchanged

| Where | What it says | Why it is left |
| --- | --- | --- |
| `PRODUCT.md:32` and `PRODUCT.md:106-112` | all nine kinds, Apple and Microsoft and the three others one per instance | done by `umbral-documentation` on 2026-11-12; the task says skip |
| `PRODUCT.md:69-71` | credentials come from OAuth apps at "Discord, GitHub, Google, Apple, Microsoft or their own OIDC provider" | a list of the kinds with credentials to create; the Fediverse (line 78), Bluesky and the passkey create nothing, so they do not belong in it |
| `README.md:21` | the Connector concept, all nine kinds | complete |
| `README.md:55-62` | the connector links of the docs site | complete |
| `docs/vision.md:23` | "many of them will not use Google, GitHub or Discord at all" | an argument about the audience, not a list of what Umbral offers |
| `docs/native-apps.md:27` | "Google, GitHub, Apple, a passkey and so on" | an example list, closed by "and so on" |
| `docs/connectors/oidc.md:10` | "You do not need this if you only want Discord, GitHub, Google, Apple or Microsoft. Those have walkthroughs of their own" | the kinds with a walkthrough where an OAuth app is created; the Fediverse, Bluesky and passkey walkthroughs create no app. A reader who wants only those does not land on the OIDC page |
| `docs/connectors/passkey.md:84` | "Someone who signs in with GitHub, Google or Discord can add a passkey" | an example of a provider user |
| `docs/integrate.md:112` | "they stay signed in at Discord, GitHub or whichever provider" | "or whichever" |
| `Texts.fs:1384`, `ConnectorAddLead` | "A second Discord, GitHub or Google application … The first connector of Discord, GitHub and Google needs no form here" | the add form offers Discord, GitHub, Google and OpenID Connect only (`addableKinds`, `Connectors.fs:1192`); Apple, Microsoft, the Fediverse, Bluesky and the passkey are one per instance and never added there |
| `Texts.fs:1393`, the issuer hint of the add form | "Leave it empty for Discord, GitHub and Google" | same form, same three kinds |
| `CHANGELOG.md:202` | the 0.6.0 note that quotes the old sentence | a released section; never edited |

The docs site has no start page of its own: `site/src` holds the content config, the assets and the stylesheet,
and the README is the start page, so the README change is the site change.

## Changelog

No `changelog.d/` file yet. The task says a one-line **Changed** note for `HomeLead` only if the documentation
reviewer wants one. The README's opening is not a change to what Umbral does.

## Verification

- `dotnet build src/Umbral -c Release`: see "Build and tests" below.
- `HomeTests` and `FooterTests` (the two that render `GET /`): see below.
- Captures: `capture.mjs` (Playwright 1.64.1 from `/tmp/r3du-pw`, Chrome, node 22.23.4), against the Release
  build on a fresh data directory, `ASPNETCORE_ENVIRONMENT=Development`, `UMBRAL__INSECURE_ALLOW_HTTP=true`,
  `UMBRAL__PUBLIC_ORIGIN=http://127.0.0.1:18080`. Files `home-<width>-<theme>.png`, widths 1280 and 360,
  themes dark and light, full page. The home page is static and needs no setup. The instance is stopped.

### Build and tests

- `dotnet build src/Umbral -c Release`: 0 warnings, 0 errors.
- `dotnet test tests/Umbral.Tests --filter "FullyQualifiedName~HomeTests|FullyQualifiedName~FooterTests"`:
  11 passed, 0 failed, 0 skipped (2026-11-13). No test changed: none asserts the sentence.
- Captures: 4 shots, **none overflows horizontally** (`capture-output.log`: every one `scrollWidth == innerWidth`).
  The lead reads, in the page: "A self-hosted sign-in server. Your apps offer passwordless sign-in through one
  OpenID Connect issuer: a passkey, or an account people already have at Discord, GitHub, Google, Apple,
  Microsoft, a Mastodon-compatible server, Bluesky or any OpenID Connect provider." (captures retaken after
  the nit; the build at that point had 0 warnings too.)

## Reviews

- Design (`review.md`): clear to merge at `bda34fe`, one nit (the catch-all last), taken; no re-check asked.
- Documentation (`review-docs.md`, committed by the reviewer as `f5a929b`): clear, no blocking, no nits; no
  changelog fragment wanted. One condition about the base: the branch is on a local `main` that holds the
  f0rg history; **do not push it** until f0rg is on GitHub's main as one squashed commit, then rebase.
