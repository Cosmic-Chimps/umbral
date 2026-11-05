# Documentation review: Samples 1/4, the contract, the harness and the ASP.NET Core sample

Task: `2026-10-26-samples-harness-and-aspnetcore-s4mp`.
Branch: `alta/vibrant-regulus-bmdk`, commits `674e441..f8b5b18` on `main` at `3c57932`.
Reviewed 2026-11-05 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `notes.md`, the sample's code, the
harness and the workflows, and my own runs.

## Verdict

**Clear to merge.** No blocking item. Three nits, none needs a re-check.

## What was checked

- **Quoted UI strings.** Every bold label in the two READMEs is verbatim in
  `Views/Texts.fs`: **Apps** (`:890`), **Create app** (`:991`), **Name**
  (`:983`), **Redirect URI** (`:984`), **Post-logout redirect URI (optional)**
  (`:986`), **App type** (`:987`), **Web application** and **Single-page or
  native app** (`:1055-1056`), **Client ID**, **Client secret**, **Issuer URL**
  (`:994-996`, shown on the app's page, `Apps.fs:804`); the sign-in page title
  "Sign in to {app}" (`:970`).
- **The contract's protocol statements.** PKCE S256 required
  (`OpenIddictSetup.fs:279`); scopes (`:301`); the end-session endpoint is
  `connect/logout` and is read from discovery (`:273`); Umbral ends only its
  own session and hands the redirect to OpenIddict, which honours only the
  registered post-logout URI (`EndSession.fs:24-35`); plain http is for
  loopback only (`PublicOrigin.fs:77-83`). The claims paragraph says what T2's
  README section says and the walk asserts it (`walk.spec.ts`: `sub`, `name`,
  `idp` present, no `email` row).
- **The fake connector sentence.** The gate is both settings
  (`Fake.fs:38-40`). Outside `tests/`, `specs/` and `.alta/`, the pair appears
  in `samples/test/docker-compose.yml`, the samples README, the samples
  workflow's comment and `Fake.fs` itself; no other compose file, Dockerfile
  or run instruction, as the README says.
- **The sample's README against `Program.cs`.** Every setting in "How it
  works" is in the file: `Authority` only, `RequireHttpsMetadata` relaxed for
  an http loopback issuer only, `ResponseType = "code"`, `UsePkce`, the three
  scopes, `MapInboundClaims = false`, `GetClaimsFromUserInfoEndpoint = false`,
  `SaveTokens = true`, sign-out on both schemes as a POST, the cookie flags
  (`SameAsRequest` is "Secure on every https address"). The three variables
  and `PORT` default `5000` match. `/private` renders exactly the five claims
  in the order the README lists. The two default paths are the handler's
  (`CallbackPath` `/signin-oidc`, `SignedOutCallbackPath`
  `/signout-callback-oidc`), and `setup.spec.ts` registers exactly those.
- **The harness description against the harness.** `run.sh`, `samples.conf`,
  the two specs and `samples.yml` do what "The harness" and "Adding a sample"
  say: claim by `reset-credential`, fake connector and app through the admin
  screens, the walk in the stated order, one matrix job per `samples.conf`
  line, `release.yml`'s `changelog` job needs `samples`, so a failing sample
  blocks a release. The pair of settings is where the workflow comment says.
- **Root README paragraph and fragment.** Names the one sample and its type;
  `changelog.d/samples.md` is an `### Added` fragment in the format
  `changelog.d/README.md` prescribes; `changelog.sh check` is in order.
- **My runs.** `dotnet build` of the sample: 0 errors. `check-links.py`: 202
  links in 16 files, all resolve (the script now walks `samples/**/*.md`). I
  did not re-run the Playwright harness; the notes record a green run and the
  first push runs it on GitHub.

## Nits

1. **`samples/aspnetcore/README.md`, "SaveTokens = true": "the handler sends
   the saved `id_token` as `id_token_hint`, which is how Umbral knows which
   app's post-logout redirect URI to honour."** The first half is right (the
   handler needs the saved token to send the hint). The second overstates
   Umbral's side: `EndSession.fs` passes the request to OpenIddict, which
   validates `post_logout_redirect_uri` against the registered clients; the
   hint is the standard way to say which client, not Umbral's only one. Say
   "which the end-session request uses to name the app" and leave Umbral out
   of the mechanism, so the sentence survives a change in OpenIddict's rules.
2. **`samples/README.md`, "Cookies": "Umbral itself only accepts plain http
   there"** (and the same aside in the sample README and `Program.cs`).
   Umbral accepts it for any loopback address (`localhost`, `127.0.0.1`,
   `::1`), audit W11; `Program.cs` already tests `uri.IsLoopback`, so the code
   is wider than the prose. "on a loopback address such as `http://localhost`"
   matches both.
3. **`samples/README.md`, the table's "Issuer URL on the app's card".** The
   Apps screen has been a list and a detail pane since 0.5.0; the root README
   and the walkthroughs say "page". "on the app's page" here, as the sample's
   README already says.

## notes.md

Complete under section 6: files, each statement with its source, the quoted
strings with line numbers, the verification (the harness run, the build, the
smoke run, the link and changelog checks, the linters) and what has not run yet
(the workflow on GitHub). Agreed on all of it.
