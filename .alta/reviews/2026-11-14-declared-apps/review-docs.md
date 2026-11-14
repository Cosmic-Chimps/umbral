# Documentation review: declared apps (spec 019, task a5ap)

Branch `alta/twilight-fornax-23bd`, docs commits 75b02b2, 63e92b8, 45f7204 and ca0398a, read against
the spec, `security-decisions.md` (D1–D11, C1, C2, N1–N3) and the code up to 91a6e93
(`Domains/Declaration.fs`, `Infrastructure/Declarations.fs`, the Aspire package, `Texts.fs`). Reviewed
2026-11-14 by the documentation reviewer.

## Verdict

**Not clear yet: one blocking item, four nits.** This is careful work. The pages say what the
declaration owns and what it never touches. They say that conflicts are refused, not merged, and
that a refused or failed declaration stops the start and writes nothing (C1). They say how an
identical passkey connector is adopted (C2). They carry both points the code reviewer asked for:
the secrets are ordinary environment variables, and you claim before you publish with open sign-up.
The blocking item is a case where following the docs stops Umbral. One re-check after the fix.

## What was checked

- **The JSON** in settings.md against the parser: `version` 1, `passkeys.signUp` `"invitations"` or
  `"open"` (`Declaration.fs:445-449`), `connectors` `"all-shared"` or a list, unknown properties
  refused, the redirect URI rules. Right.
- **Secret variables**: one per web app, at least 32 characters, the `_` form for `-` (N1), and both
  forms set for one key stops the start. Right.
- **The package**: `AddApp` key rules and duplicate refusal, the generated secret at 48 characters
  (N2) named `<umbral>-<key>-client-secret` (`UmbralAppBuilderExtensions.cs:20,261`), the spa type
  without a secret, `WithReference(app)` variables, the run-mode callback paths, and the refusal to
  derive redirect URIs when publishing. The dashboard's second link is **Claim (first run)**, going to
  `/admin/setup` (`UmbralResourceBuilderExtensions.cs:48`). Right.
- **The admin refusal** quoted in aspire.md is verbatim (`Texts.fs:983`), and so is the **Declared**
  mark (`DeclaredBadge`).
- **C1, C2, D3, D6, D7, D8** as the "Declared apps" section states them: one transaction, any failure
  stops the start, adopt only an identical passkey connector, an unset variable hands everything
  back, rotation on a changed secret, actor `system` with no secret or URI in a detail. Right.
- **The constitution** (3.2.0, MINOR) and **PRODUCT.md** name the exception in the same terms. The
  data directory stays the whole state: a copy without the variable hands everything back, and the
  apps keep working on their stored hashes. Right, apart from N4.
- **backup.md** schema 16, **integrate.md**'s pointer, the **sample README** (0.12.0 needed, with the
  `Umbral:ImageTag` override until then) and the **package README**: right.
- `check-links.py`: 432 links resolve. `changelog.sh check`: in order.

## Blocking

### B1. A secret variable left behind stops Umbral, and the docs say the app "keeps working"

"When an app leaves the declaration" says an app you stop declaring "is handed back, never deleted,
disabled or changed: it keeps working". In a compose file the operator removes the app from
`UMBRAL__DECLARATION` and, naturally, leaves `UMBRAL__APP_SECRETS__web` in place. The parser refuses
that: "UMBRAL__APP_SECRETS__web: no declared web app has the key "web". Remove the variable."
(`Declaration.fs:469-476`, D6). The refusal stops the start, so the deploy that was meant to hand an
app back takes Umbral down until someone reads the log. The Aspire package removes the variable
itself, so this bites the compose path, which settings.md describes.

Say it in both places:

- aspire.md, "When an app leaves the declaration", after the first sentence: "Remove its
  `UMBRAL__APP_SECRETS__<key>` variable with it: a secret variable for a key that is no longer
  declared stops the start. The Aspire package does this for you."
- settings.md, the `UMBRAL__APP_SECRETS__<key>` row: "A variable for a key the declaration does not
  name stops the start."

## Nits

1. **The compose example runs `:latest`.** The declaration needs 0.12.0 or later, and an image that
   upgrades the database on start should not move under a production file (backup.md: an older image
   refuses a newer database). Write `ghcr.io/cosmic-chimps/umbral:0.12.0` and add "The declaration
   needs Umbral 0.12.0 or later" under the example, as the sample README does.
2. **The changelog's "`aspire run` then signs in with a new passkey with no admin step"** holds only
   with `WithPasskeys(PasskeySignUp.Open)`. With the default, invitations only, a new person needs an
   invitation, which comes from the admin area. Say "With `WithPasskeys(PasskeySignUp.Open)`, `aspire
   run` then signs in…". aspire.md already gets this right.
3. **"Claim before you publish with open sign-up"** says to start the instance, claim it, and only then
   make it reachable. On most publish targets, starting is what makes it reachable. Add the one way
   that works everywhere: "or publish it with `PasskeySignUp.InvitationsOnly` first, claim it, then
   change to `Open` and deploy again."
4. **The constitution's "It never holds a secret".** The declaration (the JSON) holds none, but the
   exception as a whole does take a secret from the environment, one variable per web app. For the
   document the other rules are judged by, say exactly that: "The declaration itself never holds a
   secret: a declared web app's secret comes from its own variable, and Umbral stores only its hash.
   It never declares a secret-bearing connector, a user or the operator credential."

## Re-check at 9da0772 (2026-11-14)

**Clear to merge.** B1 and the four nits are fixed (`c5db73f..9da0772`: 008a9c6, plus 9da0772, a
changelog reflow).

- **B1**: aspire.md says to remove the app's `UMBRAL__APP_SECRETS__<key>` with it, that a leftover
  stops the start, and that the package does this for you. The settings.md row says a variable for a
  key the declaration does not name stops the start, and to remove it with the app. Right.
- **N1**: the compose example runs `ghcr.io/cosmic-chimps/umbral:0.12.0`, with "The declaration needs
  Umbral 0.12.0 or later." Right.
- **N2**: the changelog sentence is scoped to `WithPasskeys(PasskeySignUp.Open)`. Right.
- **N3**: "Claim before you publish" adds the `InvitationsOnly`-first route for targets where starting
  makes the instance reachable. Right.
- **N4**: the constitution says the declaration itself holds no secret, that a web app's secret comes
  from its own variable, and that only its hash is stored. Right.
- `check-links.py`: 432 links in 40 files, all resolve. `changelog.sh check`: in order.
