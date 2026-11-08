# Code review: the Android (Kotlin, AppAuth) sample (task 4ndK)

Branch `alta/mindful-starling-nw5b`, `4657c66..95a61f3` (the code is `eb3c0f7`). Reviewer: the `umbral` session, 2026-11-07.

**What this review rests on.** I read every file. I could not build or run it: this machine has no Android SDK or emulator. The build and lint rest on the implementer's local run and on the new `samples-android` workflow, which will run on the merge push; the sign-in rests on the implementer's hand walk on a Pixel 7 emulator (API 35), recorded in `notes.md`. The maintainer should check that the first `samples-android` run is green.

## Verdict

**Clear to merge.** No blocking items; two nits.

## What was checked, against the task's list

- **The system browser, not a WebView.** AppAuth's `getAuthorizationRequestIntent` and `getEndSessionRequestIntent` open a Custom Tab; the app has no `WebView` anywhere. AppAuth's `RedirectUriReceiverActivity` receives the redirect, merged in from the library with the scheme from `manifestPlaceholders["appAuthRedirectScheme"]`.
- **PKCE, `state` and `nonce`.** All three left at AppAuth's defaults (S256, random `state` checked on return, `nonce` checked in the `id_token`); the `id_token`'s issuer, audience, nonce and expiry are validated by AppAuth during the exchange. The emulator walk saw `code_challenge_method=S256`, `state` and `nonce` on the authorize URL.
- **No secret in the app.** A public client: the token request has no client authentication, and `umbral.properties` holds only the issuer and the client ID, both public.
- **Tokens in the platform's secure store.** `TokenStore` encrypts AppAuth's whole state with an AES-256-GCM key generated in the Android Keystore (never exportable), with a fresh IV per write and the IV stored with the ciphertext; an unreadable value (lost key) is dropped and the person is signed out. Not depending on the deprecated `security-crypto` is right. `allowBackup="false"` with backup and data-extraction rules keeps tokens off backups and device transfers.
- **Transport.** `network_security_config.xml` refuses cleartext except to `localhost` and `127.0.0.1`; `LoopbackOrHttpsConnectionBuilder` allows https always and http only to loopback, with timeouts and no redirects; `setSkipIssuerHttpsCheck` only for a loopback http issuer.
- **The redirect scheme.** `com.cosmicchimps.umbral.sample:/callback` and `:/signout-callback`, single slash, matching Umbral's exact comparison (the README and `umbral.properties` name both).
- **Sign-out through Umbral.** Local state discarded first, then `EndSessionRequest` with `id_token_hint`, the post-logout URI and AppAuth's `state`; the walk came back through the receiver activity and Umbral showed its page again.
- **Session expiry.** `claims()` treats an expired `id_token` as signed out (no `offline_access`, so no refresh token), which matches the contract.
- **Logs.** `describe` logs AppAuth's error code and description, never a token.
- **Pins.** AGP 9.5.1, the Kotlin Compose plugin 2.4.21, the Compose BOM 2026.09.00, activity-compose 1.13.0, AppAuth 0.11.1, in the version catalog; Gradle 9.9.0 with `distributionSha256Sum`. `gradle/actions/setup-gradle` validates `gradle-wrapper.jar` against Gradle's published checksums before anything runs it.
- **The CI job.** `samples-android.yml`: `contents: read`, `persist-credentials: false`, every action pinned by SHA, `ubuntu-24.04`, a 20-minute timeout, `assembleDebug lintDebug`, the lint report uploaded on failure. `samples.yml` skips a change under `samples/android/` alone, and the release's call of `samples.yml` is unaffected since the Android sample has no line in `samples.conf`.
- **The contract.** `samples/README.md` gains "A phone app": the same contract adapted (screens, the system browser, a configuration file, custom-scheme redirects, a secure store instead of cookies, the same protocol), built in CI and walked by hand. That is the right shape for the iOS and MAUI tasks to follow.

## Nits

- **N1. `::1`.** `isLoopback` and the comment in `umbral.properties` accept `::1`, but the network security config lists only `localhost` and `127.0.0.1`, so an `http://[::1]` issuer would be refused by the platform before AppAuth sees it. Drop `::1` from the comment (an emulator reaches the host through `adb reverse` on `localhost` anyway), or say it isn't supported.
- **N2. Your own settings in a committed file.** `umbral.properties` is committed with an empty client ID, and the README asks people to edit it, so a developer's issuer and client ID can end up in a commit. Nothing secret is at stake (both are public), but a one-line README hint keeps it tidy: `git update-index --skip-worktree samples/android/umbral.properties`, or an optional, git-ignored `umbral.local.properties` that the build reads first.

## Implementer's answers

- **N1, taken.** `::1` is gone from `isLoopback` too, not only from the comment, so the connection
  builder, AppAuth's issuer check and `network_security_config.xml` all name the same two hosts
  (`localhost`, `127.0.0.1`); a comment on `isLoopback` points at the config. `notes.md` updated.
  `assembleDebug lintDebug` green again.
- **N2, taken as a README hint**, without a build change: under "How to run it", `git update-index
  --skip-worktree umbral.properties` and its undo before pulling a change to the file.
