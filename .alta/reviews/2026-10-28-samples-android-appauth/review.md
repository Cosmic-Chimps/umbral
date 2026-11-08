# Documentation review: the Android sample (`samples/android`) and "A phone app"

Task: `2026-10-28-samples-android-appauth-4ndK`.
Branch: `alta/mindful-starling-nw5b`, commits `eb3c0f7` (the sample) and
`95a61f3` (the notes), on `main` at `4657c66`.
Reviewed 2026-11-07 by the documentation reviewer session, per section 6 of
`docs/audit-2026-10.md`, from the diff, `notes.md`, the sample's code, the
manifest inside the AppAuth 0.11.1 library, AppAuth-Android's README, and
OpenID Connect Core 1.0 §3.1.3.7.

## Verdict

**Clear to merge.** No blocking item. Five nits, three for the contract and
two for the sample's README; one re-check after them, as you asked, mainly for
the App Links snippet. One note for the maintainer about merge order.

## What was checked

- **Quoted UI strings.** **Apps**, **Create app**, **Name**, **Redirect URI**,
  **Post-logout redirect URI (optional)**, **App type**, **Single-page or
  native app**, **Client ID**, **Issuer URL** and "Sign in to" are in
  `Texts.fs` at the lines `notes.md` gives. The app's own labels (**Public**,
  **Private**, **Sign in**, **Sign out**, **Could not reach Umbral**, the
  `UMBRAL_CLIENT_ID` message, **Umbral sample**) are in `MainActivity.kt`,
  `UmbralAuth.kt:72` and `strings.xml`.
- **The README against the code.** Discovery only (`fetchFromIssuer`, the
  end-session endpoint from the same configuration); the three scopes; the two
  custom-scheme URIs with a single slash (`UmbralAuth.kt:147-148`); the
  `appAuthRedirectScheme` placeholder (`build.gradle.kts:34`), which the
  library's own manifest uses for `RedirectUriReceiverActivity`'s intent
  filter; no client authentication on the token request; the Keystore
  AES-256-GCM key and only ciphertext in preferences (`TokenStore.kt`);
  backups off (`allowBackup="false"` and both rule files); no
  `offline_access`, an expired `id_token` counts as signed out; sign-out
  discards the tokens before building the `EndSessionRequest`; the connection
  builder refuses `http://` except to a loopback host and follows no
  redirects; the network security config allows cleartext only to `localhost`
  and `127.0.0.1`. Every statement in "How it works" is in the code.
- **Settings and the build.** `umbral.properties` holds the two settings and
  is copied into `BuildConfig`; the committed file has the quick start's
  issuer and an empty client ID, which the app reports instead of opening
  Umbral.
- **Root README, `docs/native-apps.md`, table, fragment, workflows.** "CI runs
  each web sample against a real Umbral" is now exact, since
  `samples-android.yml` builds and lints but walks nothing, and `samples.yml`
  ignores a change under `samples/android/` alone; the native-apps page's
  "Samples" gains its first entry and links the new contract section; the
  fragment is an `### Added` in the prescribed format and says the sign-in is
  checked by hand. `changelog.sh check` passes. "An Android" is right.
- **Links.** `check-links.py`: 243 links in 20 files, all resolve, the new
  `#a-phone-app` and `#samples` anchors included.
- **The hand walk** in `notes.md` covers the contract's walk and more (a
  relaunch keeping the session, the preferences file empty after sign-out, a
  second sign-in without an email). It is the right substitute for the
  harness.

## Your two questions

### Does "A phone app" hold for the iOS and MAUI samples?

Mostly, and the shape is right: screens for pages, the system browser, a
configuration file, the platform's secure store, the same protocol, a build
workflow and a hand walk. Three bullets are worded for Android only and would
have to be rewritten by the next sample; see nits 1 to 3.

### The App Links section

The steps are right in substance: an `assetlinks.json` at `/.well-known/`
with the package and the signing certificate's SHA-256, an intent filter with
`autoVerify`, and the `https://` URI registered character for character
(Umbral's `RedirectUris.acceptable` accepts it). The snippet differs from
AppAuth's own documented form, and it has not been built; see nit 4.

## Nits

1. **Contract, "The system's sign-in browser, a Custom Tab on Android".** Name
   the iOS one too, so the iOS and MAUI tasks inherit the rule rather than
   extend it: "a Custom Tab on Android, `ASWebAuthenticationSession` on iOS
   (.NET MAUI's `WebAuthenticator` uses the same two), never a WebView".
   Likewise "Tokens in the platform's secure store" can name both: "the
   Android Keystore, the iOS Keychain".
2. **Contract, "Custom-scheme redirect URIs".** The sample's own README
   recommends App Links as the stronger choice, and the iOS sample may use a
   claimed `https://` link. "Redirect URIs with a custom scheme or a claimed
   `https://` link, which the README names" keeps the contract true for both.
3. **Contract, "The README of a sample", item 2** still says "with `docker
   compose up` and without Docker". Add to "A phone app": "How to run it covers
   a device and an emulator instead, and says how the app reaches a local
   Umbral (`adb reverse` on Android)". And say where the hand walk is
   recorded: "in the review folder's `notes.md`", as the workflow comment
   already does.
4. **Sample README, the App Links snippet.** AppAuth's README declares the
   activity with `tools:node="replace"` (and `xmlns:tools` on the
   `<manifest>`), so it replaces the library's declaration. The library's
   manifest (`appauth-0.11.1.aar`) declares `RedirectUriReceiverActivity` with
   `exported="true"` and an intent filter on `${appAuthRedirectScheme}`. The
   snippet as written has no `replace`, so the merger keeps both filters: it
   works, the custom-scheme filter included, and the build still needs the
   placeholder defined. Either way, say it: use AppAuth's form, and add
   "Keep `appAuthRedirectScheme` set in `build.gradle.kts`, as the sample
   does: the library's manifest refers to it." This needs no domain to
   check: add the snippet, run `./gradlew assembleDebug`, and look at the
   merged manifest (`app/build/intermediates/merged_manifests/`); record what
   it shows in `notes.md`. Also mention, in the last paragraph, that an app
   signed through Google Play's app signing needs Play's certificate
   fingerprint in `assetlinks.json`, not the upload key's, which is the most
   common reason verification fails.
5. **Sample README, two sentences.**
   - "AppAuth does not check its signature: … which is what OpenID Connect
     asks of a client that does that." OIDC Core §3.1.3.7, step 6, says TLS
     server validation **MAY** be used instead of the signature for a token
     received directly from the token endpoint. "allows" for "asks", and note
     that on the `http://localhost` trial there is no TLS either, which is
     acceptable only because it is a trial on one machine.
   - "If you close the sheet by hand instead, the app is still signed out."
     True, and Umbral's session in the browser is then still alive, so the
     next sign-in may come back without showing Umbral's page. Say so in one
     clause.
   And, smaller: `umbral.properties` says plain http is accepted for
   `localhost`, `127.0.0.1` and `::1`, but the network security config allows
   cleartext only to the first two, so `http://[::1]` fails. Drop `::1` from
   the comment, or add it to the config.

## For the maintainer (outside this task)

This branch and the Next.js branch (`alta/golden-ibex-wbsb`, clear) both
change the root README's samples sentence to "Today there are four", each
naming a different fourth sample, and both add a row to the table in
`samples/README.md`. Whichever merges second must say five and keep both rows;
it is a textual conflict git will show, so it will not slip through silently.

## notes.md

Complete under section 6: every statement against the code, AppAuth or the
catalogue, the hand walk on a named emulator image with what it saw, the CI
job, and what was not exercised (App Links) said plainly. Agreed on all of it.

---

## Addendum, 2026-11-07: the review is of `1403b20`

The code review's commit `1403b20` reached the worktree while this review was
being written, and `7ce4b4b` sits on top of it, so the verdict above stands for
`1403b20`. Two things it changed:

- **The `::1` sub-nit in nit 5 is already fixed.** `isLoopback` now accepts
  `localhost` and `127.0.0.1` only (`UmbralAuth.kt:151`), and
  `umbral.properties` says the same, matching the network security config.
  Drop that line from nit 5.
- **The new `skip-worktree` paragraph** in "How to run it" is right: the file
  is tracked, and a pull that changes it fails while the flag is set, so "undo
  it before you pull a change to the file" is the right warning. One nit, the
  sixth: the code block shows both commands one after the other, which reads as
  "run both". Split it, or comment the second: `# and to undo it:`.

Still clear to merge; one re-check after the nits.

## Implementer's answers

1. **Taken.** "A phone app" names `ASWebAuthenticationSession` beside Custom Tabs (and MAUI's
   `WebAuthenticator`), and the Android Keystore and the iOS Keychain.
2. **Taken.** "Redirect URIs with a custom scheme or a claimed `https://` link".
3. **Taken.** A bullet says a phone app's **How to run it** covers a device and an emulator instead of
   `docker compose up`, and how the app reaches a local Umbral (`adb reverse` on Android); the last
   paragraph says the walk is recorded in the review folder's `notes.md`.
4. **Taken, and built.** The snippet is AppAuth's form, with `tools:node="replace"` and `xmlns:tools`.
   Built in a scratch copy and read in the merged manifest (details in `notes.md`): with `replace`
   only the https filter is left, and the build succeeds even without `appAuthRedirectScheme`, so the
   README says the placeholder is then unused; without `replace` both filters merge and the
   placeholder must stay set, which the README also says. The Play app signing fingerprint is in the
   last paragraph.
5. **Taken.** "allows" instead of "asks", with the no-TLS caveat for the `http://localhost` trial;
   closing the sheet by hand leaves Umbral's browser session alive, said in the same bullet. The
   `::1` point was already fixed at 1403b20.
6. **Taken.** The two `update-index` commands are in two blocks, the second introduced as the undo.

---

## Re-check, 2026-11-07, at `88140bf`

**Clear to merge.**

- Nits 1 to 3 (the contract): the sign-in browser names `ASWebAuthenticationSession`
  and MAUI's `WebAuthenticator` beside Custom Tabs; redirect URIs allow a
  claimed `https://` link; the secure store names the Keystore and the
  Keychain; "How to run it" for a phone app covers a device and an emulator;
  the hand walk is recorded in the review folder's `notes.md`. The iOS and
  MAUI tasks can follow the section as written.
- Nit 4 (App Links): built rather than reasoned, and the measurement corrects
  my expectation. With `tools:node="replace"` the merged manifest has the
  `https://` filter only and builds without `appAuthRedirectScheme`; without
  `replace` both filters merge and the placeholder must stay. The README says
  both, names the merged manifest's path, and adds the Play app signing
  fingerprint. `notes.md` records the two builds.
- Nit 5: "allows" for "asks", the no-TLS caveat on the loopback trial, and
  Umbral's browser session surviving a sheet closed by hand.
- Nit 6: the two `skip-worktree` commands are now two blocks, the second
  introduced as the undo.
- Link check: 243 links in 20 files, all resolve; `changelog.sh check` in
  order.

The merge-order note above stands: this branch and the Next.js branch both
say "Today there are four".
